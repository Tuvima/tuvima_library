# ──────────────────────────────────────────────────────────────────────────────
# Download-FFmpeg.ps1
# Downloads the latest FFmpeg Windows build and places ffmpeg.exe + ffprobe.exe
# in tools/ffmpeg/ so the Engine can auto-detect them without any system install.
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File tools/Download-FFmpeg.ps1
# ──────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'

$url  = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'
$zip  = Join-Path $env:TEMP 'ffmpeg-download.zip'
$out  = Join-Path $env:TEMP 'ffmpeg-extract'
$dest = Join-Path $PSScriptRoot 'ffmpeg'

Write-Host "Downloading FFmpeg from GitHub..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

Write-Host "Extracting..." -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $out -Force

$ffmpeg  = Get-ChildItem -Path $out -Recurse -Filter 'ffmpeg.exe'  | Select-Object -First 1
$ffprobe = Get-ChildItem -Path $out -Recurse -Filter 'ffprobe.exe' | Select-Object -First 1

if (-not $ffmpeg -or -not $ffprobe) {
    Write-Error "Could not find ffmpeg.exe / ffprobe.exe in the downloaded archive."
    exit 1
}

New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item $ffmpeg.FullName  -Destination $dest -Force
Copy-Item $ffprobe.FullName -Destination $dest -Force

Write-Host "FFmpeg installed to: $dest" -ForegroundColor Green
& "$dest\ffmpeg.exe" -version 2>&1 | Select-Object -First 1
