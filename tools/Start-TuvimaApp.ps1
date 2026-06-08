param(
    [ValidateSet("Both", "Engine", "Dashboard")]
    [string]$Role = "Both",

    [switch]$NoBuild,
    [switch]$NoStop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ConfigDir = Join-Path $RepoRoot "config"
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

    foreach ($dotnet in $dotnetProcesses) {
        $process = Get-Process -Id $dotnet.ProcessId -ErrorAction SilentlyContinue
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

if ($Role -eq "Both") {
    if (-not $NoStop) {
        Stop-TuvimaProcesses
    }

    $commonArgs = @(
        "-NoExit",
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $PSCommandPath
    )

    $engineArgs = $commonArgs + @("-Role", "Engine")
    $dashboardArgs = $commonArgs + @("-Role", "Dashboard")
    if ($NoBuild) {
        $engineArgs += "-NoBuild"
        $dashboardArgs += "-NoBuild"
    }

    Start-Process powershell -ArgumentList $engineArgs -WindowStyle Hidden
    Start-Sleep -Seconds 5
    Start-Process powershell -ArgumentList $dashboardArgs -WindowStyle Hidden

    Write-Host "Tuvima Library starting from repo config."
    Write-Host "Config dir: $ConfigDir"
    Write-Host "Engine:     http://localhost:61495"
    Write-Host "Dashboard:  http://localhost:5016"
    return
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
