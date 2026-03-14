# ─────────────────────────────────────────────────────────────────────────────
# reset-and-generate.ps1
#
# Resets the Tuvima Library dev environment and regenerates all 20 test files.
#
# What it does:
#   1. Stops the Engine API process if running (warns if it cannot stop it)
#   2. Deletes the SQLite database  (src/MediaEngine.Api/library.db)
#   3. Clears the watch folder      (C:\temp\tuvima-watch\books\)
#   4. Clears the library folder    (C:\temp\tuvima-library\)
#   5. Runs the file generator      (tools/GenerateTestEpubs)
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File tools/test-data/reset-and-generate.ps1
#
# Options:
#   -SkipApiStop       Skip the step that tries to stop the Engine process
#   -SkipLibraryWipe   Skip clearing C:\temp\tuvima-library\ (keeps organised files)
#   -DryRun            Print what would happen without doing it
# ─────────────────────────────────────────────────────────────────────────────

param(
    [switch]$SkipApiStop,
    [switch]$SkipLibraryWipe,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot    = Resolve-Path (Join-Path $PSScriptRoot "../..")
$DbPath      = Join-Path $RepoRoot "src/MediaEngine.Api/library.db"
$WatchDir    = "C:\temp\tuvima-watch\books"
$LibraryDir  = "C:\temp\tuvima-library"
$GeneratorProject = Join-Path $RepoRoot "tools/GenerateTestEpubs"

function Write-Step([string]$msg) { Write-Host "`n  ──  $msg" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "  ✓  $msg"   -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  ⚠  $msg"   -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "  ✗  $msg"   -ForegroundColor Red }
function Write-Dry([string]$msg)  { Write-Host "  ~  [DRY RUN] $msg" -ForegroundColor DarkGray }

Write-Host ""
Write-Host "  Tuvima Library — Dev Environment Reset" -ForegroundColor White
Write-Host "  ════════════════════════════════════════" -ForegroundColor DarkGray
if ($DryRun) { Write-Host "  DRY RUN — no changes will be made`n" -ForegroundColor Yellow }

# ── Step 1: Stop Engine API ───────────────────────────────────────────────────

if (-not $SkipApiStop) {
    Write-Step "Stopping Engine API (dotnet MediaEngine.Api)"
    $procs = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue |
             Where-Object { $_.CommandLine -like "*MediaEngine.Api*" }
    if ($procs) {
        foreach ($p in $procs) {
            if ($DryRun) { Write-Dry "Stop process PID $($p.Id)" }
            else         { $p.Kill(); Write-Ok "Stopped PID $($p.Id)" }
        }
    } else {
        Write-Ok "Engine not running"
    }
} else {
    Write-Warn "Skipping API stop (--SkipApiStop)"
    Write-Warn "IMPORTANT: If the Engine is running, library.db may be locked."
}

# ── Step 2: Delete database ───────────────────────────────────────────────────

Write-Step "Deleting database: $DbPath"
if (Test-Path $DbPath) {
    if ($DryRun) { Write-Dry "Delete $DbPath" }
    else {
        try   { Remove-Item $DbPath -Force; Write-Ok "Deleted library.db" }
        catch { Write-Err "Could not delete library.db — is the Engine still running?  $_"; exit 1 }
    }
} else {
    Write-Ok "library.db does not exist — nothing to delete"
}

# Also remove WAL and SHM sidecar files if present
foreach ($ext in @("-wal", "-shm")) {
    $side = $DbPath + $ext
    if (Test-Path $side) {
        if ($DryRun) { Write-Dry "Delete $side" }
        else         { Remove-Item $side -Force; Write-Ok "Deleted $(Split-Path $side -Leaf)" }
    }
}

# ── Step 3: Clear watch folder ────────────────────────────────────────────────

Write-Step "Clearing watch folder: $WatchDir"
if (Test-Path $WatchDir) {
    if ($DryRun) { Write-Dry "Remove-Item $WatchDir -Recurse -Force" }
    else         { Remove-Item $WatchDir -Recurse -Force; Write-Ok "Cleared $WatchDir" }
} else {
    Write-Ok "Watch folder does not exist — will be created by generator"
}

# ── Step 4: Clear library folder ─────────────────────────────────────────────

if (-not $SkipLibraryWipe) {
    Write-Step "Clearing library folder: $LibraryDir"
    if (Test-Path $LibraryDir) {
        if ($DryRun) { Write-Dry "Remove-Item $LibraryDir -Recurse -Force" }
        else         { Remove-Item $LibraryDir -Recurse -Force; Write-Ok "Cleared $LibraryDir" }
    } else {
        Write-Ok "Library folder does not exist — nothing to clear"
    }
} else {
    Write-Warn "Skipping library wipe (--SkipLibraryWipe)"
}

# ── Step 5: Run file generator ────────────────────────────────────────────────

Write-Step "Running file generator (20 scenarios → $WatchDir)"
if ($DryRun) {
    Write-Dry "dotnet run --project $GeneratorProject -- $WatchDir"
} else {
    Push-Location $RepoRoot
    try {
        $exitCode = 0
        & dotnet run --project $GeneratorProject -- $WatchDir
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        Write-Err "Generator exited with code $exitCode"
        exit $exitCode
    }
    Write-Ok "Generator completed"
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host "  Reset complete." -ForegroundColor White
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Start the Engine:"
Write-Host "         dotnet run --project src/MediaEngine.Api"
Write-Host "    2. The Engine will pick up files from $WatchDir automatically."
Write-Host "    3. Check results in the Dashboard or at http://localhost:61495/swagger"
Write-Host "    4. Record results in tools/test-data/TEST-RESULTS.md"
Write-Host "       (use the checklist in tools/test-data/SCENARIOS.md)"
Write-Host ""
