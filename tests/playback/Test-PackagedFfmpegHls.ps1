$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ffmpeg = Join-Path $repoRoot 'tools/ffmpeg/ffmpeg.exe'
$ffprobe = Join-Path $repoRoot 'tools/ffmpeg/ffprobe.exe'
if (-not (Test-Path -LiteralPath $ffmpeg) -or -not (Test-Path -LiteralPath $ffprobe)) {
    throw 'Run tools/Download-FFmpeg.ps1 before the packaged FFmpeg HLS gate.'
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryBase ('tuvima-hls-media-' + [Guid]::NewGuid().ToString('N'))
$resolvedRoot = [IO.Path]::GetFullPath($testRoot)
if (-not $resolvedRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The temporary HLS test path is outside the system temporary directory.'
}

try {
    New-Item -ItemType Directory -Path $resolvedRoot -Force | Out-Null
    foreach ($name in @('v0', 'a0', 'a1')) {
        New-Item -ItemType Directory -Path (Join-Path $resolvedRoot $name) -Force | Out-Null
    }

    $source = Join-Path $resolvedRoot 'source.mp4'
    & $ffmpeg -y -hide_banner -loglevel error `
        -f lavfi -i 'testsrc2=size=640x360:rate=24:duration=8' `
        -f lavfi -i 'sine=frequency=440:duration=8' `
        -f lavfi -i 'sine=frequency=880:duration=8' `
        -map 0:v:0 -map 1:a:0 -map 2:a:0 `
        -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $source
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }

    $videoPlaylist = Join-Path $resolvedRoot 'v0/index.m3u8'
    & $ffmpeg -y -hide_banner -loglevel warning -i $source `
        -map 0:v:0 -an -sn -vf 'scale=-2:360' `
        -preset veryfast -profile:v main -c:v libx264 `
        -b:v 900k -maxrate 1000k -bufsize 1800k `
        -sc_threshold 0 -force_key_frames 'expr:gte(t,n_forced*2)' `
        -f hls -hls_time 2 -hls_playlist_type vod -hls_flags independent_segments `
        -hls_segment_filename (Join-Path $resolvedRoot 'v0/segment_%05d.ts') $videoPlaylist
    if ($LASTEXITCODE -ne 0) { throw 'Video HLS generation failed.' }

    foreach ($index in @(0, 1)) {
        $audioRoot = Join-Path $resolvedRoot "a$index"
        & $ffmpeg -y -hide_banner -loglevel warning -i $source `
            -map "0:a:$index" -vn -sn -c:a aac -b:a 160k -ac 2 `
            -f hls -hls_time 2 -hls_playlist_type vod `
            -hls_segment_filename (Join-Path $audioRoot 'segment_%05d.ts') `
            (Join-Path $audioRoot 'index.m3u8')
        if ($LASTEXITCODE -ne 0) { throw "Audio HLS generation failed for stream $index." }
    }

    & $ffprobe -v error -show_entries 'stream=codec_name,codec_type' -of json $videoPlaylist
    if ($LASTEXITCODE -ne 0) { throw 'FFprobe could not inspect the video HLS rendition.' }
    & $ffmpeg -hide_banner -loglevel error -ss 4 -i $videoPlaylist -t 1 -f null -
    if ($LASTEXITCODE -ne 0) { throw 'The packaged FFmpeg build could not seek in HLS.' }

    $segments = (Get-ChildItem -LiteralPath (Join-Path $resolvedRoot 'v0') -Filter '*.ts').Count
    if ($segments -lt 2) { throw 'The HLS encoder did not produce multiple seekable segments.' }
    Write-Host "Packaged FFmpeg HLS gate passed: $segments video segments, 2 audio renditions, seek passed." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $resolvedRoot) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
