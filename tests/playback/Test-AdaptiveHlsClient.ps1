param(
    [Parameter(Mandatory = $true)]
    [string] $DashboardBaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $HlsUrl,

    [DateTimeOffset] $ExpiresAt,

    [switch] $WaitForExpiry,

    [string] $FfmpegPath,

    [string] $FfprobePath
)

$ErrorActionPreference = 'Stop'

$baseUri = [Uri]$DashboardBaseUrl
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -notin @('http', 'https')) {
    throw 'DashboardBaseUrl must be an absolute HTTP or HTTPS URL.'
}

$hostName = $baseUri.DnsSafeHost
$parsedAddress = $null
$isIpAddress = [Net.IPAddress]::TryParse($hostName, [ref]$parsedAddress)
if ($hostName -eq 'localhost' -or ($isIpAddress -and [Net.IPAddress]::IsLoopback($parsedAddress))) {
    throw 'The adaptive HLS exit gate must target a non-loopback Dashboard address.'
}

if ([Uri]::IsWellFormedUriString($HlsUrl, [UriKind]::Absolute)) {
    $playbackUri = [Uri]$HlsUrl
}
else {
    $path = $HlsUrl
    if ($path.StartsWith('/stream/hls/', [StringComparison]::OrdinalIgnoreCase)) {
        $path = '/engine-hls/' + $path.Substring('/stream/hls/'.Length)
    }
    $playbackUri = [Uri]::new($baseUri, $path)
}

if ($playbackUri.Host -ne $baseUri.Host) {
    throw 'The HLS URL must use the Dashboard host.'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $FfmpegPath) {
    $packaged = Join-Path $repoRoot 'tools/ffmpeg/ffmpeg.exe'
    $FfmpegPath = if (Test-Path -LiteralPath $packaged) { $packaged } else { 'ffmpeg' }
}
if (-not $FfprobePath) {
    $packaged = Join-Path $repoRoot 'tools/ffmpeg/ffprobe.exe'
    $FfprobePath = if (Test-Path -LiteralPath $packaged) { $packaged } else { 'ffprobe' }
}

Write-Host "Fetching signed master playlist from $playbackUri" -ForegroundColor Cyan
$master = Invoke-WebRequest -Uri $playbackUri -UseBasicParsing
if ($master.StatusCode -ne 200 -or $master.Content -notmatch '#EXT-X-STREAM-INF') {
    throw 'The signed URL did not return an adaptive HLS master playlist.'
}
if ($master.Headers.'Cache-Control' -notmatch 'no-store') {
    throw 'The HLS response did not prohibit signed-path caching.'
}

& $FfprobePath -v error -show_entries 'stream=index,codec_type,codec_name' -of json $playbackUri.AbsoluteUri
if ($LASTEXITCODE -ne 0) { throw 'FFprobe could not open the adaptive stream.' }

& $FfmpegPath -hide_banner -loglevel error -i $playbackUri.AbsoluteUri -t 2 -f null -
if ($LASTEXITCODE -ne 0) { throw 'The native HLS client could not play from the beginning.' }

& $FfmpegPath -hide_banner -loglevel error -ss 2 -i $playbackUri.AbsoluteUri -t 1 -f null -
if ($LASTEXITCODE -ne 0) { throw 'The native HLS client could not seek.' }

& $FfmpegPath -hide_banner -loglevel error -ss 1 -i $playbackUri.AbsoluteUri -t 1 -f null -
if ($LASTEXITCODE -ne 0) { throw 'The native HLS client could not resume at a saved position.' }

if ($WaitForExpiry) {
    if ($ExpiresAt -eq [DateTimeOffset]::MinValue) {
        throw 'ExpiresAt is required when WaitForExpiry is selected.'
    }
    while ([DateTimeOffset]::UtcNow -le $ExpiresAt.AddSeconds(2)) {
        Start-Sleep -Seconds ([Math]::Min(30, [Math]::Max(1, [Math]::Ceiling(($ExpiresAt - [DateTimeOffset]::UtcNow).TotalSeconds + 2))))
    }
    try {
        Invoke-WebRequest -Uri $playbackUri -UseBasicParsing | Out-Null
        throw 'The signed HLS URL remained usable after its expiry.'
    }
    catch {
        if ([int]$_.Exception.Response.StatusCode -ne 403) { throw }
    }
}

Write-Host 'Adaptive HLS play, seek, and resume gate passed.' -ForegroundColor Green
