# ──────────────────────────────────────────────────────────────────────────────
# Download-FFmpeg.ps1
# Downloads the pinned FFmpeg Windows build and places ffmpeg.exe + ffprobe.exe
# in tools/ffmpeg/ so the Engine can auto-detect them without any system install.
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File tools/Download-FFmpeg.ps1
# ──────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = 'Stop'

$release = 'autobuild-2026-08-28-17-08'
$archive = 'ffmpeg-n9.0.1-11-ge47273f4d9-win64-gpl-9.0.zip'
$archiveSha256 = 'DEE63142094F79F6A50CDECE65384B7793181EAB3B6DB2EC907834981BB8AB10'
$ffmpegSha256 = '989A60089B9B1A98896A5BD99EE793AB6841724E1B2441D5EF3E5D17DB0B0938'
$ffprobeSha256 = '001D80FDDF67BC303E91C6B8ECCDF53AF29A5F87ECF3837056B391CC3DD3F7B4'
$url  = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$release/$archive"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("tuvima-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
$zip  = Join-Path $tempRoot $archive
$out  = Join-Path $tempRoot 'extract'
$dest = Join-Path $PSScriptRoot 'ffmpeg'

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Write-Host "Downloading pinned FFmpeg $release from GitHub..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    $actualArchiveSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
    if ($actualArchiveSha256 -ne $archiveSha256) {
        throw "FFmpeg archive checksum mismatch. Expected $archiveSha256; received $actualArchiveSha256."
    }

    Write-Host "Archive checksum verified. Extracting..." -ForegroundColor Cyan
    Expand-Archive -LiteralPath $zip -DestinationPath $out -Force

    $ffmpeg  = Get-ChildItem -Path $out -Recurse -Filter 'ffmpeg.exe'  | Select-Object -First 1
    $ffprobe = Get-ChildItem -Path $out -Recurse -Filter 'ffprobe.exe' | Select-Object -First 1
    $license = Get-ChildItem -Path $out -Recurse -File |
        Where-Object { $_.Name -in @('LICENSE.txt', 'COPYING.GPLv3') } |
        Select-Object -First 1

    if (-not $ffmpeg -or -not $ffprobe) {
        throw 'Could not find ffmpeg.exe and ffprobe.exe in the downloaded archive.'
    }

    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item -LiteralPath $ffmpeg.FullName  -Destination $dest -Force
    Copy-Item -LiteralPath $ffprobe.FullName -Destination $dest -Force
    if ($license) {
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $dest 'LICENSE.txt') -Force
    }

    $actualFfmpegSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $dest 'ffmpeg.exe')).Hash
    $actualFfprobeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $dest 'ffprobe.exe')).Hash
    if ($actualFfmpegSha256 -ne $ffmpegSha256 -or $actualFfprobeSha256 -ne $ffprobeSha256) {
        throw 'Extracted FFmpeg executable checksum mismatch.'
    }

    Write-Host "FFmpeg installed to: $dest" -ForegroundColor Green
    & "$dest\ffmpeg.exe" -version 2>&1 | Select-Object -First 1
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
