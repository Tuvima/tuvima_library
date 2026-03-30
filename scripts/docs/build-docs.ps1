[CmdletBinding()]
param(
    [switch]$InstallDependencies
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

    throw "Python 3 is required to build the documentation. Install Python 3, then rerun this script."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$requirementsPath = Join-Path $repoRoot "requirements-docs.txt"
$python = Get-PythonCommand

Push-Location $repoRoot
try {
    if ($InstallDependencies) {
        & $python.Command @($python.Prefix + @("-m", "pip", "install", "-r", $requirementsPath))
    }

    & $python.Command @($python.Prefix + @("-m", "mkdocs", "build", "--strict"))
}
finally {
    Pop-Location
}
