[CmdletBinding()]
param([string]$ModelsDirectory = $env:TUVIMA_MODELS_DIR, [string]$RuntimeDirectory = $env:TUVIMA_AI_RUNTIME_DIR)
$ErrorActionPreference = 'Stop'
if (-not $ModelsDirectory) { $ModelsDirectory = [Environment]::GetEnvironmentVariable('TUVIMA_MODELS_DIR','User') }
if (-not $RuntimeDirectory) { $RuntimeDirectory = [Environment]::GetEnvironmentVariable('TUVIMA_AI_RUNTIME_DIR','User') }
$repoPath = Split-Path $PSScriptRoot -Parent
$locations = [ordered]@{}
if ($ModelsDirectory) {
    $locations['Text model folder'] = Join-Path $ModelsDirectory 'llama'
    $locations['Audio model folder'] = Join-Path $ModelsDirectory 'whisper'
    $locations['Model staging'] = Join-Path $ModelsDirectory '.tuvima/staging'
}
if ($RuntimeDirectory -and $RuntimeDirectory -ne 'bundled') { $locations['Shared AI runtimes'] = $RuntimeDirectory }
$locations['Git data'] = Join-Path $repoPath '.git'
foreach ($entry in $locations.GetEnumerator()) {
    $bytes = if (Test-Path -LiteralPath $entry.Value) { (Get-ChildItem -LiteralPath $entry.Value -File -Recurse -Force | Measure-Object Length -Sum).Sum } else { 0 }
    [pscustomobject]@{Area=$entry.Key;GiB=[math]::Round($bytes/1GB,3);Path=$entry.Value}
}
$worktrees = @(& git -C $repoPath worktree list --porcelain | Where-Object { $_ -like 'worktree *' })
Write-Information "Registered temporary worktrees: $([math]::Max(0,$worktrees.Count - 1)); policy maximum: 2." -InformationAction Continue
if ($worktrees.Count -gt 3) { Write-Warning 'Retire completed worktrees; temporary-worktree policy exceeded.' }
