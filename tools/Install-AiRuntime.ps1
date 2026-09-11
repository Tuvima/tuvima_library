[CmdletBinding()]
param(
    [string]$RuntimeDirectory = $env:TUVIMA_AI_RUNTIME_DIR,
    [string]$RuntimeIdentifier = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [switch]$Cuda,
    [switch]$Whisper,
    [switch]$SkipRestore
)
$ErrorActionPreference = 'Stop'
if (-not $RuntimeDirectory -or $RuntimeDirectory -eq 'bundled') {
    throw 'Specify -RuntimeDirectory with the absolute shared native-runtime folder.'
}
if (-not [IO.Path]::IsPathFullyQualified($RuntimeDirectory)) { throw 'RuntimeDirectory must be absolute.' }
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$repoPath = Split-Path $PSScriptRoot -Parent
$packageProject = Join-Path $PSScriptRoot 'AiRuntimePackages/AiRuntimePackages.csproj'
if (-not $SkipRestore) {
    & dotnet restore $packageProject "-p:InstallCuda=$($Cuda.IsPresent.ToString().ToLowerInvariant())" "-p:InstallWhisper=$($Whisper.IsPresent.ToString().ToLowerInvariant())"
    if ($LASTEXITCODE -ne 0) { throw 'AI runtime package restore failed.' }
}
$assets = Get-Content (Join-Path $PSScriptRoot 'AiRuntimePackages/obj/project.assets.json') -Raw | ConvertFrom-Json
[xml]$versions = Get-Content (Join-Path $repoPath 'Directory.Packages.props')
function Get-Version([string]$name) {
    return ($versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq $name).Version
}
function Get-Package([string]$name, [string]$version) {
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder "$($name.ToLowerInvariant())/$version"
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Package not restored: $name $version"
}
function Install-Component([string]$component, [string]$version, [object[]]$sources) {
    $destination = Join-Path $RuntimeDirectory "$component/$version"
    $manifestPath = Join-Path $destination 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.version -ne $version -or $manifest.rid -ne $RuntimeIdentifier) { throw "Runtime manifest mismatch: $manifestPath" }
        if ($Cuda -and $component -eq 'llamasharp' -and -not $manifest.cuda) { throw 'Existing CPU-only installation needs an explicit upgrade; keep the old version until no process uses it.' }
        foreach ($file in $manifest.files) {
            $path = [IO.Path]::GetFullPath((Join-Path $destination $file.path))
            if (-not $path.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest escaped its installation.' }
            if ((Get-Item -LiteralPath $path).Length -ne $file.size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Runtime verification failed: $path" }
        }
        Write-Output "Reused verified $component $version ($RuntimeIdentifier)"
        return
    }
    if (Test-Path -LiteralPath $destination) { throw "Incomplete destination already exists: $destination. Inspect it before retrying." }
    $stage = Join-Path $RuntimeDirectory "staging/$component-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($source in $sources) {
        $target = Join-Path $stage $source.Target
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $source.Path -Force) {
            Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse
        }
    }
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($stage, $_.FullName); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    if ($files.Count -eq 0) { throw "No native assets found for $RuntimeIdentifier" }
    @{ version=$version; rid=$RuntimeIdentifier; cuda=($Cuda.IsPresent -and $component -eq 'llamasharp'); files=$files; installedUtc=[DateTime]::UtcNow.ToString('O') } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    # Both absolute paths are beneath the requested root and on the same volume.
    foreach ($path in @($stage, $destination)) {
        if (-not ([IO.Path]::GetFullPath($path)).StartsWith($RuntimeDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installation path escaped runtime root.' }
    }
    Move-Item -LiteralPath $stage -Destination $destination
    Write-Output "Installed $component $version ($RuntimeIdentifier): $destination"
}
New-Item -ItemType Directory -Path $RuntimeDirectory -Force | Out-Null
$installLock = [IO.File]::Open((Join-Path $RuntimeDirectory '.install.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $llamaVersion = Get-Version 'LLamaSharp'
    $cpu = Get-Package 'LLamaSharp.Backend.Cpu' $llamaVersion
    $llamaSources = @(@{ Path=(Join-Path $cpu "LLamaSharpRuntimes/$RuntimeIdentifier"); Target="runtimes/$RuntimeIdentifier" })
    if ($Cuda) {
        $cudaPackage = if ($RuntimeIdentifier -eq 'win-x64') { 'LLamaSharp.Backend.Cuda12.Windows' } elseif ($RuntimeIdentifier -eq 'linux-x64') { 'LLamaSharp.Backend.Cuda12.Linux' } else { throw 'CUDA 12 installation supports win-x64 and linux-x64.' }
        $cudaPath = Get-Package $cudaPackage $llamaVersion
        $llamaSources += @{ Path=(Join-Path $cudaPath "LLamaSharpRuntimes/$RuntimeIdentifier/native/cuda12"); Target="runtimes/$RuntimeIdentifier/native/cuda12" }
    }
    Install-Component 'llamasharp' $llamaVersion $llamaSources
    if ($Cuda -and $RuntimeIdentifier -eq 'win-x64') {
        & (Join-Path $PSScriptRoot 'Install-NvidiaRuntime.ps1') -RuntimeDirectory $RuntimeDirectory
    }
    if ($Whisper) {
        $whisperVersion = Get-Version 'Whisper.net'
        $whisperPath = Get-Package 'Whisper.net.Runtime' $whisperVersion
        Install-Component 'whisper' $whisperVersion @(@{ Path=(Join-Path $whisperPath "build/$RuntimeIdentifier"); Target="runtimes/$RuntimeIdentifier" })
    }
} finally { $installLock.Dispose() }
