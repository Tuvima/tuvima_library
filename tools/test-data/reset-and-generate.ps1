param(
    [switch]$SkipApiStop,
    [switch]$SkipLibraryWipe,
    [switch]$Large,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$DbPath = Join-Path $RepoRoot "src/MediaEngine.Api/library.db"
$WatchDir = "C:\temp\tuvima-watch"
$LibraryDir = "C:\temp\tuvima-library"
$GeneratorProject = Join-Path $RepoRoot "tools/GenerateTestEpubs"

function Write-Step([string]$msg) { Write-Host "`n  --  $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg) { Write-Host "  OK  $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  WARN  $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg) { Write-Host "  ERR  $msg" -ForegroundColor Red }
function Write-Dry([string]$msg) { Write-Host "  ~  [DRY RUN] $msg" -ForegroundColor DarkGray }

function Remove-DirectorySafely([string]$Path, [string]$AllowedRoot) {
    if (-not (Test-Path $Path)) {
        Write-Ok "$Path does not exist"
        return
    }

    $resolvedPath = (Resolve-Path $Path).Path.TrimEnd('\')
    $resolvedRoot = (Resolve-Path $AllowedRoot).Path.TrimEnd('\')
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$resolvedPath'; it is outside '$resolvedRoot'."
    }

    if ($DryRun) {
        Write-Dry "Remove-Item $resolvedPath -Recurse -Force"
    } else {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
        Write-Ok "Cleared $resolvedPath"
    }
}

Write-Host ""
Write-Host "  Tuvima Library - Dev Environment Reset" -ForegroundColor White
Write-Host "  =======================================" -ForegroundColor DarkGray
if ($DryRun) { Write-Host "  DRY RUN - no changes will be made`n" -ForegroundColor Yellow }

if (-not $SkipApiStop) {
    Write-Step "Stopping Engine and Dashboard processes"
    $procs = @(Get-Process -Name "MediaEngine.Api","MediaEngine.Web" -ErrorAction SilentlyContinue)
    $dotnetProcs = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*MediaEngine.Api*" -or $_.CommandLine -like "*MediaEngine.Web*" }
    foreach ($dotnet in $dotnetProcs) {
        $process = Get-Process -Id $dotnet.ProcessId -ErrorAction SilentlyContinue
        if ($process) { $procs += $process }
    }

    $procs = @($procs | Sort-Object Id -Unique)
    if ($procs) {
        foreach ($p in $procs) {
            if ($DryRun) { Write-Dry "Stop process PID $($p.Id)" }
            else { Stop-Process -Id $p.Id -Force; Write-Ok "Stopped PID $($p.Id)" }
        }
    } else {
        Write-Ok "Engine and Dashboard are not running"
    }
} else {
    Write-Warn "Skipping process stop"
    Write-Warn "If the Engine is running, library.db may be locked."
}

Write-Step "Deleting database: $DbPath"
foreach ($path in @($DbPath, "$DbPath-wal", "$DbPath-shm")) {
    if (Test-Path $path) {
        if ($DryRun) { Write-Dry "Delete $path" }
        else { Remove-Item -LiteralPath $path -Force; Write-Ok "Deleted $(Split-Path $path -Leaf)" }
    }
}

Write-Step "Clearing watch folder: $WatchDir"
if (-not (Test-Path "C:\temp")) {
    if ($DryRun) { Write-Dry "Create C:\temp" } else { New-Item -ItemType Directory -Path "C:\temp" | Out-Null }
}
Remove-DirectorySafely -Path $WatchDir -AllowedRoot "C:\temp"

if (-not $SkipLibraryWipe) {
    Write-Step "Clearing library folder: $LibraryDir"
    Remove-DirectorySafely -Path $LibraryDir -AllowedRoot "C:\temp"
} else {
    Write-Warn "Skipping library wipe"
}

$corpusLabel = if ($Large) { "large stress corpus" } else { "standard corpus" }
Write-Step "Running file generator ($corpusLabel -> $WatchDir)"
if ($DryRun) {
    $largeArg = if ($Large) { " --large" } else { "" }
    Write-Dry "dotnet run --project $GeneratorProject -- $WatchDir$largeArg"
} else {
    Push-Location $RepoRoot
    try {
        if ($Large) {
            & dotnet run --project $GeneratorProject -- $WatchDir --large
        } else {
            & dotnet run --project $GeneratorProject -- $WatchDir
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Err "Generator exited with code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    } finally {
        Pop-Location
    }
    Write-Ok "Generator completed"
}

Write-Host ""
Write-Host "  Reset complete." -ForegroundColor White
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Start the Engine: dotnet run --project src/MediaEngine.Api"
Write-Host "    2. Start the Dashboard: dotnet run --project src/MediaEngine.Web"
Write-Host "    3. The Engine will pick up files from $WatchDir automatically."
Write-Host "    4. Monitor ingestion at http://localhost:5016/settings/ingestion"
Write-Host ""
