[CmdletBinding()]
param([Parameter(Mandatory)][string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = [IO.Path]::GetFullPath($RuntimeDirectory)
$destination = Join-Path $root 'nvidia/12.8.1'
if (Test-Path -LiteralPath (Join-Path $destination 'manifest.json')) {
    $manifest = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne '12.8.1' -or $manifest.rid -ne 'win-x64' -or $manifest.files.Count -eq 0) { throw 'NVIDIA installation manifest is invalid.' }
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $destination $file.path))
        if (-not $path.StartsWith($destination + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'NVIDIA manifest escaped installation.' }
        if ((Get-Item -LiteralPath $path).Length -ne $file.size -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256) { throw "NVIDIA runtime verification failed: $path" }
    }
    Write-Output "Reused verified NVIDIA CUDA redistributables: $destination"
    return
}
if (Test-Path -LiteralPath $destination) { throw "Inspect incomplete installation: $destination" }
# NVIDIA's official redistribution checksums, pinned with the CUDA 12.8.1 manifest.
$packages = @(
    @{ Path='cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.8.90-archive.zip'; Hash='4a39058fd8519444a81cfc7ae055d136f48d1a31ffa41ae255b35b2edd61e13b' },
    @{ Path='libcublas/windows-x86_64/libcublas-windows-x86_64-12.8.4.1-archive.zip'; Hash='57a470112cec7e112c95253dde8b3c7184d795dbd92b0bde77a4cb7f8c94c8aa' }
)
$stage = Join-Path $root "staging/nvidia-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $stage 'bin') -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    $archivePath = Join-Path $stage ([IO.Path]::GetFileName($package.Path))
    Invoke-WebRequest "https://developer.download.nvidia.com/compute/cuda/redist/$($package.Path)" -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $package.Hash) { throw 'NVIDIA package checksum mismatch.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '/bin/[^/]+\.dll$') {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage "bin/$($entry.Name)"), $false)
            } elseif ($entry.Name -match '^(LICENSE|EULA)') {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage "$([IO.Path]::GetFileNameWithoutExtension($archivePath))-$($entry.Name)"), $false)
            }
        }
    } finally { $archive.Dispose() }
    # Verified, explicitly created archive; retain installed DLLs and license, not another archive copy.
    Remove-Item -LiteralPath $archivePath
}
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    @{path=[IO.Path]::GetRelativePath($stage,$_.FullName);size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
@{version='12.8.1';rid='win-x64';files=$files;packages=$packages} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
foreach ($path in @($stage,$destination)) {
    if (-not ([IO.Path]::GetFullPath($path)).StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped runtime root.' }
}
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
Move-Item -LiteralPath $stage -Destination $destination
Write-Output "Installed NVIDIA CUDA runtime and cuBLAS: $destination"
