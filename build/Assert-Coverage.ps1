[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,

    [ValidateRange(0, 100)]
    [double] $MinimumLineCoverage = 98.0
)

$ErrorActionPreference = 'Stop'

$targets = @('Localisation.WPF', 'Localisation.WPF.Reactive', 'Localisation.Avalonia', 'Localisation.Avalonia.Reactive')

$resolvedCoverageDirectory = Resolve-Path -LiteralPath $ResultsDirectory -ErrorAction Stop
$reports = @(Get-ChildItem -LiteralPath $resolvedCoverageDirectory -Filter '*.cobertura.xml' -File -Recurse)
if ($reports.Count -eq 0) {
    throw "No Cobertura reports were found under '$resolvedCoverageDirectory'."
}

$coverageByAssembly = @{}
foreach ($assembly in $targets) {
    $coverageByAssembly[$assembly] = @{}
}

foreach ($report in $reports) {
    try {
        [xml] $xml = Get-Content -LiteralPath $report.FullName -Raw
    }
    catch {
        throw "Could not parse Cobertura report '$($report.FullName)': $($_.Exception.Message)"
    }

    foreach ($package in @($xml.SelectNodes('/coverage/packages/package'))) {
        $assembly = [string] $package.GetAttribute('name')
        if (-not $coverageByAssembly.ContainsKey($assembly)) { continue }

        foreach ($class in @($package.SelectNodes('./classes/class'))) {
            $className = [string] $class.GetAttribute('name')
            $fileName = [string] $class.GetAttribute('filename')
            if ([string]::IsNullOrWhiteSpace($fileName)) {
                throw "Class '$className' in '$($report.FullName)' has no source filename."
            }
            $fileName = $fileName -replace '\\', '/' -replace '^\./', ''

            foreach ($line in @($class.SelectNodes('./lines/line'))) {
                $lineNumber = [string] $line.GetAttribute('number')
                $hits = 0
                if (-not [int]::TryParse([string] $line.GetAttribute('hits'), [ref] $hits)) {
                    throw "Invalid line hit count for '$fileName' line '$lineNumber' in '$($report.FullName)'."
                }
                $key = "$fileName`:$lineNumber"
                if (-not $coverageByAssembly[$assembly].ContainsKey($key)) {
                    $coverageByAssembly[$assembly][$key] = $false
                }
                if ($hits -gt 0) {
                    $coverageByAssembly[$assembly][$key] = $true
                }
            }
        }
    }
}

$failed = $false
foreach ($assembly in $targets) {
    $lines = $coverageByAssembly[$assembly]
    if ($lines.Count -eq 0) {
        Write-Error "No executable Cobertura lines were found for assembly '$assembly'."
        $failed = $true
        continue
    }

    $covered = @($lines.Values | Where-Object { $_ }).Count
    $total = $lines.Count
    $percentage = 100.0 * $covered / $total
    Write-Host ('{0}: {1:N2}% line coverage ({2}/{3})' -f $assembly, $percentage, $covered, $total)
    if ($percentage -lt $MinimumLineCoverage) {
        Write-Error ('{0} line coverage {1:N2}% is below the required {2:N2}%.' -f $assembly, $percentage, $MinimumLineCoverage)
        $failed = $true
    }
}

if ($failed) { exit 1 }
