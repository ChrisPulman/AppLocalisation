[CmdletBinding()]
param(
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $Configuration = 'Release',
    [switch] $UseEnvironmentalTools
)

$ErrorActionPreference = 'Stop'
if ($UseEnvironmentalTools) {
    if ($RuntimeIdentifier -ne 'win-x64') { throw 'Environmental tool initialization currently supports win-x64.' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installations = & $vswhere -all -prerelease -products '*' -property installationPath
    if ($LASTEXITCODE -ne 0) { throw 'Visual Studio installation discovery failed.' }
    $installation = $null
    $toolsDirectory = $null
    foreach ($candidate in $installations) {
        $toolRoot = Join-Path $candidate 'VC/Tools/MSVC'
        if (-not (Test-Path -LiteralPath $toolRoot)) { continue }
        $tools = Get-ChildItem -LiteralPath $toolRoot -Directory |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'lib/x64/libcmt.lib') } |
            Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1
        if ($tools -and (Test-Path -LiteralPath (Join-Path $candidate 'Common7/Tools/VsDevCmd.bat'))) {
            $installation = $candidate
            $toolsDirectory = $tools.FullName
            break
        }
    }
    if (-not $installation) { throw 'Visual Studio x64 C++ build tools were not found.' }
    $developerCommand = '"' + (Join-Path $installation 'Common7/Tools/VsDevCmd.bat') + '" -arch=x64 -host_arch=x64 >nul && set'
    $developerEnvironment = & $env:ComSpec /d /s /c $developerCommand
    if ($LASTEXITCODE -ne 0) { throw 'Visual Studio developer environment initialization failed.' }
    foreach ($entry in $developerEnvironment) {
        if ($entry -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
        }
    }
    $env:LIB = (Join-Path $toolsDirectory 'lib/x64') + ';' + $env:LIB
    $env:PATH = (Join-Path $toolsDirectory 'bin/Hostx64/x64') + ';' + $env:PATH
    $env:IlcUseEnvironmentalTools = 'true'
}

$project = Join-Path $PSScriptRoot '../src/Localisation.AotSmoke/Localisation.AotSmoke.csproj'
$outputRoot = Join-Path $PSScriptRoot '../artifacts/aot-verification'
foreach ($mode in @('trimmed', 'native')) {
    $output = Join-Path $outputRoot $mode
    $native = $mode -eq 'native'
    dotnet publish $project -c $Configuration -r $RuntimeIdentifier --self-contained true `
        "-p:LocalisationPublishAot=$native" `
        -p:ILLinkTreatWarningsAsErrors=true -o $output
    if ($LASTEXITCODE -ne 0) { throw "$mode publish failed." }
    $executable = Join-Path $output 'Localisation.AotSmoke.exe'
    & $executable
    if ($LASTEXITCODE -ne 0) { throw "$mode resource verification failed." }
}
