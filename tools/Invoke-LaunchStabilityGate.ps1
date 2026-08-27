[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepresentativeDatabasePath,

    [Parameter(Mandatory)]
    [string]$RepresentativeLibraryRoot,

    [ValidateRange(1, 100)]
    [int]$ConsecutiveRuns = 10,

    [ValidateRange(1024, 65535)]
    [int]$Port = 61595,

    [string]$ApiKey = "",

    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$engineProject = Join-Path $repoRoot "src/MediaEngine.Api/MediaEngine.Api.csproj"
$engineDll = Join-Path $repoRoot "src/MediaEngine.Api/bin/Debug/net10.0/MediaEngine.Api.dll"
$gateProject = Join-Path $repoRoot "tools/MediaEngine.LaunchGate/MediaEngine.LaunchGate.csproj"
$sourceConfig = Join-Path $repoRoot "config"
$representativeDb = (Resolve-Path -LiteralPath $RepresentativeDatabasePath).Path
$representativeRoot = (Resolve-Path -LiteralPath $RepresentativeLibraryRoot).Path
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$runRoot = Join-Path $temporaryBase ("tuvima-launch-gate-" + [Guid]::NewGuid().ToString("N"))
$runRoot = [IO.Path]::GetFullPath($runRoot)
if (-not $runRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Launch-gate workspace must remain inside the operating-system temporary directory."
}

$priorEnvironment = @{}
foreach ($name in @("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "ASPNETCORE_URLS", "TUVIMA_CONFIG_DIR", "TUVIMA_DB_PATH", "TUVIMA_LIBRARY_ROOT", "TUVIMA_WATCH_FOLDER", "TUVIMA_MODELS_DIR")) {
    $priorEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Set-GateEnvironment([string]$configPath, [string]$databasePath, [string]$libraryRoot, [string]$watchRoot, [string]$modelsRoot) {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
    $env:TUVIMA_CONFIG_DIR = $configPath
    $env:TUVIMA_DB_PATH = $databasePath
    $env:TUVIMA_LIBRARY_ROOT = $libraryRoot
    $env:TUVIMA_WATCH_FOLDER = $watchRoot
    $env:TUVIMA_MODELS_DIR = $modelsRoot
}

function Wait-ForReadiness([Diagnostics.Process]$process, [string]$baseUrl, [string]$outputLog, [string]$errorLog) {
    $deadline = [DateTimeOffset]::Now.AddSeconds(120)
    while ([DateTimeOffset]::Now -lt $deadline) {
        if ($process.HasExited) {
            throw "Engine exited before readiness. Output: $outputLog Error: $errorLog"
        }
        try {
            $report = Invoke-RestMethod -Uri "$baseUrl/health/ready" -TimeoutSec 3
            if ($report.status -in @("healthy", "degraded")) { return $report }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    throw "Engine did not become ready within 120 seconds. Output: $outputLog Error: $errorLog"
}

function Invoke-Scenario([string]$name, [string]$databasePath, [string]$libraryRoot, [string]$watchRoot, [string]$configPath, [string]$modelsRoot) {
    $baseUrl = "http://127.0.0.1:$Port"
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $headers["X-Api-Key"] = $ApiKey }

    for ($iteration = 1; $iteration -le $ConsecutiveRuns; $iteration++) {
        $outputLog = Join-Path $runRoot "$name-$iteration.out.log"
        $errorLog = Join-Path $runRoot "$name-$iteration.err.log"
        Set-GateEnvironment $configPath $databasePath $libraryRoot $watchRoot $modelsRoot
        $process = Start-Process -FilePath "dotnet" -ArgumentList @($engineDll) -PassThru -WindowStyle Hidden -WorkingDirectory $repoRoot -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog
        try {
            $null = Wait-ForReadiness $process $baseUrl $outputLog $errorLog
            $first = Invoke-RestMethod -Method Post -Uri "$baseUrl/ingestion/reconcile" -Headers $headers -TimeoutSec 300
            $second = Invoke-RestMethod -Method Post -Uri "$baseUrl/ingestion/reconcile" -Headers $headers -TimeoutSec 300
            if ($second.missing_count -ne 0 -or $second.duplicate_read_works_merged -ne 0 -or $second.audiobook_authors_aligned -ne 0) {
                throw "$name iteration $iteration was not idempotent on its second reconciliation pass."
            }
            $null = Wait-ForReadiness $process $baseUrl $outputLog $errorLog
        }
        finally {
            if ($process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                $process.WaitForExit(10000) | Out-Null
            }
        }

        & dotnet run --project $gateProject --no-build -- validate --db $databasePath
        if ($LASTEXITCODE -ne 0) { throw "$name iteration $iteration failed durable-state validation." }

        $logText = ((Get-Content -LiteralPath $outputLog -Raw -ErrorAction SilentlyContinue) + "`n" + (Get-Content -LiteralPath $errorLog -Raw -ErrorAction SilentlyContinue))
        if ($logText -match "UNIQUE constraint failed: entity_assets|integrity_check failed|foreign_key_check found|Quarantined poison.*model.*unavailable|Unhandled API exception") {
            throw "$name iteration $iteration emitted a launch-gate error. See $outputLog and $errorLog"
        }
        Write-Host "$name start $iteration/$ConsecutiveRuns passed" -ForegroundColor Green
    }
}

New-Item -ItemType Directory -Path $runRoot | Out-Null
try {
    & dotnet build $engineProject --no-restore --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet restore $gateProject
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet build $gateProject --no-restore --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $configCopy = Join-Path $runRoot "config"
    Copy-Item -LiteralPath $sourceConfig -Destination $configCopy -Recurse
    $aiPath = Join-Path $configCopy "ai.json"
    $ai = Get-Content -LiteralPath $aiPath -Raw | ConvertFrom-Json
    $ai.dev_skip_download = $true
    $ai | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $aiPath -Encoding utf8

    $modelsRoot = Join-Path $runRoot "models"
    New-Item -ItemType Directory -Path $modelsRoot | Out-Null

    $emptyRoot = Join-Path $runRoot "empty-library"
    $emptyWatch = Join-Path $runRoot "empty-watch"
    New-Item -ItemType Directory -Path $emptyRoot, $emptyWatch | Out-Null
    Invoke-Scenario "empty" (Join-Path $runRoot "empty.db") $emptyRoot $emptyWatch $configCopy $modelsRoot

    $populatedRoot = Join-Path $runRoot "representative-library"
    $populatedWatch = Join-Path $runRoot "representative-watch"
    New-Item -ItemType Directory -Path $populatedRoot, $populatedWatch | Out-Null
    Copy-Item -Path (Join-Path $representativeRoot "*") -Destination $populatedRoot -Recurse -Force
    $populatedDb = Join-Path $runRoot "representative.db"
    & dotnet run --project $gateProject --no-build -- snapshot --db $representativeDb --to $populatedDb
    if ($LASTEXITCODE -ne 0) { throw "Representative database snapshot failed." }
    & dotnet run --project $gateProject --no-build -- prepare --db $populatedDb --from $representativeRoot --to $populatedRoot
    if ($LASTEXITCODE -ne 0) { throw "Representative database path rebasing failed." }
    Invoke-Scenario "representative" $populatedDb $populatedRoot $populatedWatch $configCopy $modelsRoot

    Write-Host "Launch gate passed: $ConsecutiveRuns consecutive empty and representative starts." -ForegroundColor Green
}
finally {
    foreach ($name in $priorEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $priorEnvironment[$name], "Process")
    }
    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $runRoot)) {
        $resolvedRunRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $runRoot).Path)
        if (-not $resolvedRunRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove launch-gate artifacts outside the temporary directory."
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $runRoot) {
        Write-Host "Launch-gate artifacts retained at $runRoot"
    }
}
