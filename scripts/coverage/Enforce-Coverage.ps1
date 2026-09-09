[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,

    [string] $ThresholdsFile = (Join-Path $PSScriptRoot 'coverage-thresholds.json')
)

$ErrorActionPreference = 'Stop'

function Stop-CoverageCheck {
    param([string] $Message)
    Write-Error "Coverage check failed: $Message"
    exit 1
}

function Get-ProductionPath {
    param([string] $Filename)

    if ([string]::IsNullOrWhiteSpace($Filename)) {
        return $null
    }

    $path = $Filename.Replace('\', '/').Trim()
    $marker = $path.LastIndexOf('/src/', [StringComparison]::OrdinalIgnoreCase)
    if ($marker -ge 0) {
        $path = $path.Substring($marker + 1)
    }
    else {
        while ($path.StartsWith('./', [StringComparison]::Ordinal)) {
            $path = $path.Substring(2)
        }
        if (-not $path.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }
    }

    if ($path -match '/(?:bin|obj)/') {
        return $null
    }

    return $path.ToLowerInvariant()
}

function Resolve-ProductionPath {
    param(
        [string] $Filename,
        [string[]] $Sources
    )

    $resolved = Get-ProductionPath $Filename
    if ($null -ne $resolved) {
        return $resolved
    }
    if ([IO.Path]::IsPathRooted($Filename)) {
        return $null
    }

    foreach ($source in $Sources) {
        if ([string]::IsNullOrWhiteSpace($source)) {
            continue
        }

        try {
            $combined = [IO.Path]::GetFullPath([IO.Path]::Combine($source, $Filename))
        }
        catch {
            continue
        }
        $resolved = Get-ProductionPath $combined
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    return $null
}

if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    Stop-CoverageCheck "results directory '$ResultsDirectory' does not exist."
}
if (-not (Test-Path -LiteralPath $ThresholdsFile -PathType Leaf)) {
    Stop-CoverageCheck "threshold file '$ThresholdsFile' does not exist."
}

try {
    $thresholds = Get-Content -LiteralPath $ThresholdsFile -Raw | ConvertFrom-Json
    $requiredThresholds = @('minimumLinePercent', 'minimumBranchPercent', 'targetLinePercent', 'targetBranchPercent')
    foreach ($property in $requiredThresholds) {
        if ($thresholds.PSObject.Properties.Name -notcontains $property) {
            throw "missing required property '$property'"
        }
    }
    $minimumLine = [decimal] $thresholds.minimumLinePercent
    $minimumBranch = [decimal] $thresholds.minimumBranchPercent
    $targetLine = [decimal] $thresholds.targetLinePercent
    $targetBranch = [decimal] $thresholds.targetBranchPercent
}
catch {
    Stop-CoverageCheck "threshold file '$ThresholdsFile' is malformed: $($_.Exception.Message)"
}

foreach ($value in @($minimumLine, $minimumBranch, $targetLine, $targetBranch)) {
    if ($value -lt 0 -or $value -gt 100) {
        Stop-CoverageCheck 'threshold percentages must be between 0 and 100.'
    }
}
if ($targetLine -lt $minimumLine -or $targetBranch -lt $minimumBranch) {
    Stop-CoverageCheck 'target percentages must be at least their corresponding minimum percentages.'
}

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -File -Filter 'coverage.cobertura.xml')
if ($reports.Count -eq 0) {
    Stop-CoverageCheck "no coverage.cobertura.xml reports were found under '$ResultsDirectory'."
}

$lineCoverage = @{}
$branchConditions = @{}
$aggregateBranches = @{}
$detailedBranchLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($report in $reports) {
    try {
        [xml] $document = Get-Content -LiteralPath $report.FullName -Raw
    }
    catch {
        Stop-CoverageCheck "report '$($report.FullName)' is malformed XML: $($_.Exception.Message)"
    }

    if ($null -eq $document.DocumentElement -or $document.DocumentElement.Name -ne 'coverage') {
        Stop-CoverageCheck "report '$($report.FullName)' is not a Cobertura coverage document."
    }

    $sources = @($document.SelectNodes('//sources/source') | ForEach-Object { $_.InnerText })
    foreach ($class in @($document.SelectNodes('//class[@filename]'))) {
        $productionPath = Resolve-ProductionPath ($class.GetAttribute('filename')) $sources
        if ($null -eq $productionPath) {
            continue
        }

        foreach ($line in @($class.SelectNodes('./lines/line[@number]'))) {
            $lineNumber = 0
            if (-not [int]::TryParse($line.GetAttribute('number'), [ref] $lineNumber) -or $lineNumber -le 0) {
                Stop-CoverageCheck "report '$($report.FullName)' contains an invalid line number."
            }

            $lineKey = "$productionPath`:$lineNumber"
            $hits = 0L
            if (-not [long]::TryParse($line.GetAttribute('hits'), [ref] $hits) -or $hits -lt 0) {
                Stop-CoverageCheck "report '$($report.FullName)' contains invalid hit data at $lineKey."
            }
            $lineCoverage[$lineKey] = [bool] ($lineCoverage[$lineKey] -or $hits -gt 0)

            if (-not $line.GetAttribute('branch').Equals('true', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $covered = 0
            $total = 0
            if ($line.GetAttribute('condition-coverage') -match '\((\d+)\s*/\s*(\d+)\)') {
                $covered = [int] $Matches[1]
                $total = [int] $Matches[2]
            }
            if ($total -le 0 -or $covered -lt 0 -or $covered -gt $total) {
                Stop-CoverageCheck "report '$($report.FullName)' contains invalid branch coverage at $lineKey."
            }

            $conditions = @($line.SelectNodes('./conditions/condition[@number]'))
            if ($conditions.Count -eq $total) {
                [void] $detailedBranchLines.Add($lineKey)
                foreach ($condition in $conditions) {
                    $conditionKey = "$lineKey`:$($condition.GetAttribute('type')):$($condition.GetAttribute('number'))"
                    $conditionCoverage = $condition.GetAttribute('coverage')
                    if ($conditionCoverage -notmatch '^\s*([0-9]+(?:\.[0-9]+)?)%\s*$') {
                        Stop-CoverageCheck "report '$($report.FullName)' contains invalid condition coverage at $lineKey."
                    }
                    $conditionPercent = [decimal]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
                    if ($conditionPercent -lt 0 -or $conditionPercent -gt 100) {
                        Stop-CoverageCheck "report '$($report.FullName)' contains out-of-range condition coverage at $lineKey."
                    }
                    $isCovered = $conditionPercent -gt 0
                    $branchConditions[$conditionKey] = [bool] ($branchConditions[$conditionKey] -or $isCovered)
                }
            }
            else {
                if (-not $aggregateBranches.ContainsKey($lineKey)) {
                    $aggregateBranches[$lineKey] = [pscustomobject] @{ Covered = $covered; Total = $total }
                }
                else {
                    $aggregateBranches[$lineKey].Covered = [Math]::Max($aggregateBranches[$lineKey].Covered, $covered)
                    $aggregateBranches[$lineKey].Total = [Math]::Max($aggregateBranches[$lineKey].Total, $total)
                }
            }
        }
    }
}

$lineTotal = $lineCoverage.Count
if ($lineTotal -eq 0) {
    Stop-CoverageCheck 'the reports contained no usable production line data under src/.'
}
$lineCovered = @($lineCoverage.Values | Where-Object { $_ }).Count

$branchCovered = @($branchConditions.Values | Where-Object { $_ }).Count
$branchTotal = $branchConditions.Count
foreach ($entry in $aggregateBranches.GetEnumerator()) {
    if (-not $detailedBranchLines.Contains($entry.Key)) {
        $branchCovered += $entry.Value.Covered
        $branchTotal += $entry.Value.Total
    }
}
if ($branchTotal -eq 0) {
    Stop-CoverageCheck 'the reports contained no usable production branch data under src/.'
}

$linePercent = [decimal] 100 * $lineCovered / $lineTotal
$branchPercent = [decimal] 100 * $branchCovered / $branchTotal
Write-Host ('Coverage: lines {0:N2}% ({1}/{2}, minimum {3:N2}%); branches {4:N2}% ({5}/{6}, minimum {7:N2}%).' -f `
    $linePercent, $lineCovered, $lineTotal, $minimumLine, $branchPercent, $branchCovered, $branchTotal, $minimumBranch)
Write-Host ('Targets: lines {0:N2}%; branches {1:N2}%.' -f $targetLine, $targetBranch)

$failures = @()
if ($linePercent -lt $minimumLine) {
    $failures += ('line coverage {0:N2}% is below {1:N2}%' -f $linePercent, $minimumLine)
}
if ($branchPercent -lt $minimumBranch) {
    $failures += ('branch coverage {0:N2}% is below {1:N2}%' -f $branchPercent, $minimumBranch)
}
if ($failures.Count -gt 0) {
    Stop-CoverageCheck ($failures -join '; ')
}

exit 0
