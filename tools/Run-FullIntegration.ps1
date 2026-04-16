[CmdletBinding()]
param(
    [string]$EngineUrl = "http://localhost:61495",
    [ValidateSet(1, 12, 123)]
    [int]$Stages = 123,
    [string[]]$Types = @("books", "audiobooks", "movies", "tv", "music", "comics"),
    [string]$OutputPath = "",
    [switch]$NoOpen
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$reportsDir = Join-Path $scriptRoot "reports"
if (-not (Test-Path $reportsDir)) {
    New-Item -ItemType Directory -Path $reportsDir | Out-Null
}

if (-not $OutputPath) {
    $timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
    $OutputPath = Join-Path $reportsDir "integration-test-$timestamp.html"
}

$requestedTypes = @($Types | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.ToLowerInvariant() })
$query = @("stages=$Stages")
if ($requestedTypes.Count -gt 0) {
    $query += "types=$([uri]::EscapeDataString(($requestedTypes -join ',')))"
}

$uri = "$EngineUrl/dev/integration-test"
if ($query.Count -gt 0) {
    $uri += "?" + ($query -join "&")
}

Write-Host ""
Write-Host "Tuvima Library integration harness" -ForegroundColor White
Write-Host "Engine : $EngineUrl" -ForegroundColor DarkGray
Write-Host "Stages : $Stages" -ForegroundColor DarkGray
Write-Host "Types  : $($requestedTypes -join ', ')" -ForegroundColor DarkGray
Write-Host ""

Write-Host "Checking engine status..." -ForegroundColor Cyan
Invoke-WebRequest -Uri "$EngineUrl/system/status" -UseBasicParsing | Out-Null
Write-Host "Engine reachable." -ForegroundColor Green

Write-Host ""
Write-Host "Running full integration test. This will wipe the active runtime's database, watch folders, and organized library." -ForegroundColor Yellow
Write-Host "Saving report to: $OutputPath" -ForegroundColor DarkGray

Invoke-WebRequest -Method Post -Uri $uri -OutFile $OutputPath -UseBasicParsing | Out-Null

Write-Host ""
Write-Host "Report saved to $OutputPath" -ForegroundColor Green

if (-not $NoOpen) {
    Start-Process $OutputPath | Out-Null
}
