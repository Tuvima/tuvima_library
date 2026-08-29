param(
    [ValidateSet("Both", "Engine", "Dashboard")]
    [string]$Role = "Both",

    [switch]$NoBuild,
    [switch]$NoStop,
    [switch]$NoBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ConfigDir = Join-Path $RepoRoot "config"
$LogDir = Join-Path $RepoRoot "logs"
$EngineProject = Join-Path $RepoRoot "src/MediaEngine.Api/MediaEngine.Api.csproj"
$DashboardProject = Join-Path $RepoRoot "src/MediaEngine.Web/MediaEngine.Web.csproj"
$EngineDll = Join-Path $RepoRoot "src/MediaEngine.Api/bin/Debug/net10.0/MediaEngine.Api.dll"
$DashboardDll = Join-Path $RepoRoot "src/MediaEngine.Web/bin/Debug/net10.0/MediaEngine.Web.dll"

function Stop-TuvimaProcesses {
    $processes = @()

    $processes += @(Get-Process -Name "MediaEngine.Api", "MediaEngine.Web" -ErrorAction SilentlyContinue)

    $dotnetProcesses = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -like "*MediaEngine.Api*" -or
            $_.CommandLine -like "*MediaEngine.Web*"
        }

    $launcherProcesses = Get-CimInstance Win32_Process -Filter "name = 'powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessId -ne $PID -and (
                $_.CommandLine -like "*Start-TuvimaApp.ps1*" -or
                $_.CommandLine -like "*dev-runtime*start-both.ps1*" -or
                $_.CommandLine -like "*dev-runtime*run-engine.ps1*" -or
                $_.CommandLine -like "*dev-runtime*run-dashboard.ps1*"
            )
        }

    foreach ($dotnet in $dotnetProcesses) {
        $process = Get-Process -Id $dotnet.ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            $processes += $process
        }
    }

    foreach ($launcher in $launcherProcesses) {
        $process = Get-Process -Id $launcher.ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            $processes += $process
        }
    }

    foreach ($process in @($processes | Sort-Object Id -Unique)) {
        Stop-Process -Id $process.Id -Force
        Write-Host "Stopped PID $($process.Id)"
    }
}

function Set-TuvimaEnvironment {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"
    $env:DOTNET_CLI_USE_MSBUILDNOINPROCNODE = "1"
    $env:TUVIMA_CONFIG_DIR = $ConfigDir

    foreach ($name in @(
        "TUVIMA_DB_PATH",
        "TUVIMA_WATCH_FOLDER",
        "TUVIMA_LIBRARY_ROOT",
        "TUVIMA_MODELS_DIR"
    )) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}

function Build-Project([string]$ProjectPath) {
    if ($NoBuild) {
        return
    }

    & dotnet build $ProjectPath --no-restore --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Wait-ForEngine {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$OutputLog,
        [string]$ErrorLog
    )

    $deadline = [DateTimeOffset]::Now.AddSeconds(90)
    while ([DateTimeOffset]::Now -lt $deadline) {
        if ($Process.HasExited) {
            Write-Host "Engine exited before becoming ready. Exit code: $($Process.ExitCode)"
            if (Test-Path $OutputLog) {
                Write-Host ""
                Write-Host "Engine output:"
                Get-Content -LiteralPath $OutputLog -Tail 80
            }
            if (Test-Path $ErrorLog) {
                Write-Host ""
                Write-Host "Engine errors:"
                Get-Content -LiteralPath $ErrorLog -Tail 80
            }
            exit $Process.ExitCode
        }

        try {
            # The detailed readiness report is administrator-only. The public
            # liveness endpoint is the safe startup signal for the local launcher;
            # Dashboard readiness then verifies that it can reach this endpoint.
            $status = Invoke-RestMethod -Uri "http://localhost:61495/health/live" -TimeoutSec 2
            if ([string]$status -eq "Healthy") {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    Write-Host "Timed out waiting for Engine liveness at http://localhost:61495/health/live."
    Write-Host "Engine output log: $OutputLog"
    Write-Host "Engine error log:  $ErrorLog"
    Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

function Wait-ForDashboard {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$OutputLog,
        [string]$ErrorLog
    )

    $deadline = [DateTimeOffset]::Now.AddSeconds(60)
    while ([DateTimeOffset]::Now -lt $deadline) {
        if ($Process.HasExited) {
            Write-Host "Dashboard exited before becoming ready. Exit code: $($Process.ExitCode)"
            if (Test-Path $OutputLog) {
                Write-Host ""
                Write-Host "Dashboard output:"
                Get-Content -LiteralPath $OutputLog -Tail 80
            }
            if (Test-Path $ErrorLog) {
                Write-Host ""
                Write-Host "Dashboard errors:"
                Get-Content -LiteralPath $ErrorLog -Tail 80
            }
            exit $Process.ExitCode
        }

        try {
            $status = Invoke-RestMethod -Uri "http://localhost:5016/health/ready" -TimeoutSec 2
            if ([string]$status -in @("healthy", "degraded")) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    Write-Host "Timed out waiting for Dashboard readiness at http://localhost:5016/health/ready."
    Write-Host "Dashboard output log: $OutputLog"
    Write-Host "Dashboard error log:  $ErrorLog"
    Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

if ($Role -eq "Both") {
    if (-not $NoStop) {
        Stop-TuvimaProcesses
    }

    Set-TuvimaEnvironment
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

    Write-Host "Starting Tuvima Library from repo config."
    Write-Host "Config dir: $ConfigDir"
    Write-Host "Engine:     http://localhost:61495"
    Write-Host "Dashboard:  http://localhost:5016"

    Build-Project $EngineProject
    Build-Project $DashboardProject

    $engineOut = Join-Path $LogDir "codex-run-engine.out.log"
    $engineErr = Join-Path $LogDir "codex-run-engine.err.log"
    $dashboardOut = Join-Path $LogDir "codex-run-dashboard.out.log"
    $dashboardErr = Join-Path $LogDir "codex-run-dashboard.err.log"
    Remove-Item -LiteralPath $engineOut, $engineErr, $dashboardOut, $dashboardErr -Force -ErrorAction SilentlyContinue

    $env:ASPNETCORE_URLS = "https://localhost:61494;http://localhost:61495"
    $engineProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @($EngineDll) `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $engineOut `
        -RedirectStandardError $engineErr `
        -WorkingDirectory $RepoRoot
    $dashboardProcess = $null

    try {
        Wait-ForEngine -Process $engineProcess -OutputLog $engineOut -ErrorLog $engineErr

        $env:TUVIMA_ENGINE_URL = "http://localhost:61495"
        $env:ASPNETCORE_URLS = "http://localhost:5016"
        $dashboardProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList @($DashboardDll) `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $dashboardOut `
            -RedirectStandardError $dashboardErr `
            -WorkingDirectory $RepoRoot

        Wait-ForDashboard -Process $dashboardProcess -OutputLog $dashboardOut -ErrorLog $dashboardErr

        Write-Host ""
        Write-Host "Tuvima Library is ready."
        Write-Host "Engine:    http://localhost:61495"
        Write-Host "Dashboard: http://localhost:5016"
        Write-Host "Logs:      $LogDir"
        Write-Host "Stop this command to stop both services."
        Write-Host ""

        if (-not $NoBrowser) {
            Start-Process "http://localhost:5016"
        }

        while (-not $engineProcess.HasExited -and -not $dashboardProcess.HasExited) {
            Start-Sleep -Seconds 1
        }

        if ($engineProcess.HasExited) {
            Write-Host "Engine stopped unexpectedly. Exit code: $($engineProcess.ExitCode)"
            exit $engineProcess.ExitCode
        }

        Write-Host "Dashboard stopped unexpectedly. Exit code: $($dashboardProcess.ExitCode)"
        exit $dashboardProcess.ExitCode
    }
    finally {
        if ($dashboardProcess -and -not $dashboardProcess.HasExited) {
            Stop-Process -Id $dashboardProcess.Id -Force -ErrorAction SilentlyContinue
            Write-Host "Stopped Dashboard PID $($dashboardProcess.Id)"
        }
        if ($engineProcess -and -not $engineProcess.HasExited) {
            Stop-Process -Id $engineProcess.Id -Force -ErrorAction SilentlyContinue
            Write-Host "Stopped Engine PID $($engineProcess.Id)"
        }
    }
}

Set-TuvimaEnvironment

if ($Role -eq "Engine") {
    $env:ASPNETCORE_URLS = "https://localhost:61494;http://localhost:61495"
    Build-Project $EngineProject
    & dotnet $EngineDll
    exit $LASTEXITCODE
}

$env:TUVIMA_ENGINE_URL = "http://localhost:61495"
$env:ASPNETCORE_URLS = "http://localhost:5016"
Build-Project $DashboardProject
& dotnet $DashboardDll
exit $LASTEXITCODE
