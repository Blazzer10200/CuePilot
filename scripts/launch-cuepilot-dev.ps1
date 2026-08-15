$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $projectRoot "ui\scripts\run-dev-inspectable.ps1"

if (-not (Test-Path -LiteralPath $launcher)) {
    throw "CuePilot development launcher was not found at $launcher"
}

Push-Location $projectRoot
try {
    & pwsh -NoProfile -File $launcher -WaitForCdp
    if ($LASTEXITCODE -ne 0) {
        throw "CuePilot Dev exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
