[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackagesDirectory,
    [switch] $ResolvePublishedPackages,
    [string] $PublishedPackagesDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata

function Read-EntryBytes($Entry) {
    $inputStream = $Entry.Open()
    $memory = [IO.MemoryStream]::new()
    try {
        $inputStream.CopyTo($memory)
        return ,$memory.ToArray()
    }
    finally { $inputStream.Dispose(); $memory.Dispose() }
}

function Read-PackageIdentity($Archive) {
    $spec = @($Archive.Entries | Where-Object FullName -Like '*.nuspec')
    if ($spec.Count -ne 1) { throw 'A package must contain exactly one nuspec.' }
    $xml = [xml]::new()
    $stream = $spec[0].Open()
    try { $xml.Load($stream) } finally { $stream.Dispose() }
    return @{
        Id = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]').InnerText
        Version = $xml.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]').InnerText
    }
}

function Read-PdbBytes($Archive, $DllEntry, $Reader) {
    $pdbPath = [IO.Path]::ChangeExtension($DllEntry.FullName, '.pdb')
    $pdb = $Archive.GetEntry($pdbPath)
    if ($null -ne $pdb) { return ,(Read-EntryBytes $pdb) }
    $embedded = @($Reader.ReadDebugDirectory() | Where-Object Type -EQ 'EmbeddedPortablePdb')
    if ($embedded.Count -ne 1) { throw "No portable PDB is available for '$($DllEntry.FullName)'." }
    [byte[]] $data = $Reader.GetSectionData($embedded[0].DataRelativeVirtualAddress).GetContent(0, $embedded[0].DataSize)
    if ([Text.Encoding]::ASCII.GetString($data, 0, 4) -ne 'MPDB') { throw 'Invalid embedded PDB header.' }
    $compressed = [IO.MemoryStream]::new($data, 8, $data.Length - 8)
    $deflate = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
    $memory = [IO.MemoryStream]::new()
    try {
        $deflate.CopyTo($memory)
        if ($memory.Length -ne [BitConverter]::ToInt32($data, 4)) { throw 'Invalid embedded PDB size.' }
        return ,$memory.ToArray()
    }
    finally { $deflate.Dispose(); $compressed.Dispose(); $memory.Dispose() }
}

function Assert-PdbMatches($Bytes, $Reader, $Path) {
    $memory = [IO.MemoryStream]::new($Bytes)
    $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($memory)
    try {
        $metadata = $provider.GetMetadataReader()
        $id = [Reflection.Metadata.BlobContentId]::new($metadata.DebugMetadataHeader.Id)
        $codeView = @($Reader.ReadDebugDirectory() | Where-Object Type -EQ 'CodeView')
        if ($codeView.Count -ne 1 -or $Reader.ReadCodeViewDebugDirectoryData($codeView[0]).Guid -ne $id.Guid -or $codeView[0].Stamp -ne $id.Stamp) {
            throw "Portable PDB does not match '$Path'."
        }
        $checksums = @($Reader.ReadDebugDirectory() | Where-Object Type -EQ 'PdbChecksum')
        if ($checksums.Count -ne 1) { throw "Missing PDB checksum for '$Path'." }
        $checksum = $Reader.ReadPdbChecksumDebugDirectoryData($checksums[0])
        if ($checksum.AlgorithmName -ne 'SHA256') { throw "Unsupported PDB checksum for '$Path'." }
        [byte[]] $hashBytes = $Bytes.Clone()
        [Array]::Clear($hashBytes, $metadata.DebugMetadataHeader.IdStartOffset, $metadata.DebugMetadataHeader.Id.Length)
        $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hashBytes))
        $expected = [Convert]::ToHexString([byte[]] $checksum.Checksum)
        if ($actual -ne $expected) { throw "PDB checksum mismatch for '$Path'." }
    }
    finally { $provider.Dispose(); $memory.Dispose() }
}

$packages = @(Get-ChildItem -LiteralPath $PackagesDirectory -Filter '*.nupkg' -File)
if ($packages.Count -eq 0) { throw 'No NuGet packages were found.' }
foreach ($package in $packages) {
    $symbolPath = [IO.Path]::ChangeExtension($package.FullName, '.snupkg')
    if (-not (Test-Path -LiteralPath $symbolPath)) { throw "Missing symbol package for '$($package.Name)'." }
    $main = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try { $identity = Read-PackageIdentity $main } finally { $main.Dispose() }
    $publishedPath = $null
    $downloadPath = $null
    try {
        if ($PublishedPackagesDirectory) {
            $candidate = Join-Path $PublishedPackagesDirectory $package.Name
            if (Test-Path -LiteralPath $candidate) { $publishedPath = $candidate }
        }
        elseif ($ResolvePublishedPackages) {
            $id = $identity.Id.ToLowerInvariant()
            $version = $identity.Version.ToLowerInvariant()
            $downloadPath = [IO.Path]::GetTempFileName()
            try {
                Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $downloadPath
                $publishedPath = $downloadPath
            }
            catch {
                if ([int] $_.Exception.Response.StatusCode -ne 404) { throw }
            }
        }
        if ($publishedPath) {
            # NuGet versions are immutable. Recover symbols from the original published binaries.
            Copy-Item -LiteralPath $publishedPath -Destination $package.FullName -Force
        }
        $main = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        $symbols = [IO.Compression.ZipFile]::Open($symbolPath, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $mainIdentity = Read-PackageIdentity $main
            $symbolIdentity = Read-PackageIdentity $symbols
            if ($mainIdentity.Id -ne $identity.Id -or $mainIdentity.Version -ne $identity.Version -or $symbolIdentity.Id -ne $identity.Id -or $symbolIdentity.Version -ne $identity.Version) {
                throw "Package identity mismatch for '$($package.Name)'."
            }
            if ($publishedPath) {
                foreach ($entry in @($symbols.Entries | Where-Object FullName -Like '*.pdb')) { $entry.Delete() }
            }
            $dlls = @($main.Entries | Where-Object { $_.FullName -like 'lib/*.dll' -and $_.FullName -notlike '*.resources.dll' })
            if ($dlls.Count -eq 0) { throw "No library assemblies in '$($package.Name)'." }
            foreach ($dll in $dlls) {
                $memory = [IO.MemoryStream]::new((Read-EntryBytes $dll))
                $reader = [Reflection.PortableExecutable.PEReader]::new($memory)
                try {
                    $pdbPath = [IO.Path]::ChangeExtension($dll.FullName, '.pdb')
                    if ($publishedPath) {
                        $bytes = Read-PdbBytes $main $dll $reader
                        Assert-PdbMatches $bytes $reader $dll.FullName
                        $outputStream = $symbols.CreateEntry($pdbPath).Open()
                        try { $outputStream.Write($bytes, 0, $bytes.Length) } finally { $outputStream.Dispose() }
                    }
                    else {
                        $pdb = $symbols.GetEntry($pdbPath)
                        if ($null -eq $pdb) { throw "Missing '$pdbPath' in '$symbolPath'." }
                        Assert-PdbMatches (Read-EntryBytes $pdb) $reader $dll.FullName
                    }
                }
                finally { $reader.Dispose(); $memory.Dispose() }
            }
            Write-Host "Validated $($dlls.Count) matching portable PDBs for $($package.Name); reused published binaries: $([bool] $publishedPath)."
        }
        finally { $symbols.Dispose(); $main.Dispose() }
    }
    finally { if ($downloadPath) { Remove-Item -LiteralPath $downloadPath -Force } }
}
