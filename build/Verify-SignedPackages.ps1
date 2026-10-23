[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackagesDirectory,
    [Parameter(Mandatory = $true)]
    [string] $CertificateFingerprint
)

$ErrorActionPreference = 'Stop'
$packages = @(Get-ChildItem -LiteralPath $PackagesDirectory -Filter '*.nupkg' -File)
if ($packages.Count -eq 0) { throw 'No signed NuGet packages were found.' }
$temporary = [IO.Directory]::CreateTempSubdirectory('localisation-verify-')
try {
    foreach ($package in $packages) {
        dotnet nuget verify $package.FullName --all --certificate-fingerprint $CertificateFingerprint --verbosity detailed
        if ($LASTEXITCODE -ne 0) { throw "NuGet signature verification failed: $($package.Name)" }
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $dlls = @($archive.Entries | Where-Object FullName -Like '*.dll')
            if ($dlls.Count -eq 0) { throw "Package has no DLLs: $($package.Name)" }
            foreach ($entry in $dlls) {
                # Never use archive paths as filesystem paths.
                $path = Join-Path $temporary.FullName ([Guid]::NewGuid().ToString('N') + '.dll')
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path)
                & "$PSScriptRoot/Verify-Authenticode.ps1" -Path $path -CertificateFingerprint $CertificateFingerprint
                Write-Host "Verified packaged DLL: $($package.Name)/$($entry.FullName)"
            }
        }
        finally { $archive.Dispose() }
    }
}
finally { $temporary.Delete($true) }
