$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-CoverageFixture {
    param([string] $Directory)
    $shell = (Get-Process -Id $PID).Path
    $output = & $shell -NoProfile -File (Join-Path $PSScriptRoot 'Enforce-Coverage.ps1') `
        -ResultsDirectory $Directory 2>&1
    return [pscustomobject] @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

$tempRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "tuvima-coverage-$([Guid]::NewGuid().ToString('N'))"))
$systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $tempRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Synthetic coverage directory resolved outside the system temporary directory.'
}

try {
    $passDirectory = New-Item -ItemType Directory -Path (Join-Path $tempRoot 'pass') -Force
    $duplicateDirectory = New-Item -ItemType Directory -Path (Join-Path $passDirectory 'duplicate') -Force
    $passXml = @'
<?xml version="1.0"?>
<coverage><packages><package><classes><class filename="/repo/src/Product/Service.cs"><lines>
<line number="1" hits="1" branch="true" condition-coverage="50% (1/2)"><conditions><condition number="0" type="jump" coverage="100%"/><condition number="1" type="jump" coverage="0%"/></conditions></line>
<line number="2" hits="1"/><line number="3" hits="0"/><line number="4" hits="0"/><line number="5" hits="0"/>
<line number="6" hits="0"/><line number="7" hits="0"/><line number="8" hits="0"/><line number="9" hits="0"/><line number="10" hits="0"/>
</lines></class></classes></package></packages></coverage>
'@
    Set-Content -LiteralPath (Join-Path $passDirectory 'coverage.cobertura.xml') -Value $passXml
    $relativeDuplicateXml = $passXml.Replace(
        '<coverage><packages>', '<coverage><sources><source>/repo/src/</source></sources><packages>').Replace(
        '/repo/src/Product/Service.cs', 'Product\Service.cs')
    Set-Content -LiteralPath (Join-Path $duplicateDirectory 'coverage.cobertura.xml') -Value $relativeDuplicateXml
    $pass = Invoke-CoverageFixture $passDirectory
    Assert-True ($pass.ExitCode -eq 0) "Expected duplicate fixture to pass: $($pass.Output)"
    Assert-True ($pass.Output -match '2/10') "Duplicate line totals were counted more than once: $($pass.Output)"
    Assert-True ($pass.Output -match '1/2') "Duplicate branch totals were counted more than once: $($pass.Output)"

    $failDirectory = New-Item -ItemType Directory -Path (Join-Path $tempRoot 'fail') -Force
    $failXml = $passXml.Replace('number="2" hits="1"', 'number="2" hits="0"').Replace('coverage="50% (1/2)"', 'coverage="0% (0/2)"').Replace('coverage="100%"', 'coverage="0%"')
    Set-Content -LiteralPath (Join-Path $failDirectory 'coverage.cobertura.xml') -Value $failXml
    $fail = Invoke-CoverageFixture $failDirectory
    Assert-True ($fail.ExitCode -ne 0) 'Expected below-floor fixture to fail.'
    Assert-True ($fail.Output -match 'below') "Expected a concise below-floor failure: $($fail.Output)"

    $malformedDirectory = New-Item -ItemType Directory -Path (Join-Path $tempRoot 'malformed') -Force
    Set-Content -LiteralPath (Join-Path $malformedDirectory 'coverage.cobertura.xml') -Value '<coverage>'
    Assert-True ((Invoke-CoverageFixture $malformedDirectory).ExitCode -ne 0) 'Expected malformed XML to fail.'

    $missingDirectory = New-Item -ItemType Directory -Path (Join-Path $tempRoot 'missing') -Force
    Assert-True ((Invoke-CoverageFixture $missingDirectory).ExitCode -ne 0) 'Expected missing reports to fail.'

    $noProductionDirectory = New-Item -ItemType Directory -Path (Join-Path $tempRoot 'no-production') -Force
    $noProductionXml = $passXml.Replace('/repo/src/Product/Service.cs', '/repo/tests/Product.Tests/ServiceTests.cs')
    Set-Content -LiteralPath (Join-Path $noProductionDirectory 'coverage.cobertura.xml') -Value $noProductionXml
    $noProduction = Invoke-CoverageFixture $noProductionDirectory
    Assert-True ($noProduction.ExitCode -ne 0) 'Expected a report without production data to fail.'
    Assert-True ($noProduction.Output -match 'no usable production line data') "Expected a no-production-data failure: $($noProduction.Output)"

    $global:LASTEXITCODE = 99
    & (Join-Path $PSScriptRoot 'Enforce-Coverage.ps1') -ResultsDirectory $passDirectory
    Assert-True ($LASTEXITCODE -eq 0) 'A passing gate must clear a prior native-command failure code.'

    Write-Host 'Coverage script fixtures passed: threshold, duplicate, below-floor, malformed, missing-report, and no-production-data cases.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

exit 0
