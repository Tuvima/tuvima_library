[CmdletBinding()]
param(
    [switch]$InstallDependencies,
    [string]$Address = "127.0.0.1:8000"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-PythonCommand {
    $candidates = @(
        @{ Command = "py"; Prefix = @("-3") },
        @{ Command = "python"; Prefix = @() },
        @{ Command = "python3"; Prefix = @() }
    )

    foreach ($candidate in $candidates) {
        if (Get-Command $candidate.Command -ErrorAction SilentlyContinue) {
            return $candidate
        }
    }

    throw "Python 3 is required to preview the documentation. Install Python 3, then rerun this script."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$requirementsPath = Join-Path $repoRoot "requirements-docs.txt"
$python = Get-PythonCommand

Push-Location $repoRoot
try {
    if ($InstallDependencies) {
        & $python.Command @($python.Prefix + @("-m", "pip", "install", "-r", $requirementsPath))
    }

    & $python.Command @($python.Prefix + @("-m", "mkdocs", "serve", "--dev-addr", $Address))
}
finally {
    Pop-Location
}
