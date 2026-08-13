[CmdletBinding()]
param(
    [switch]$Apply
)

$repoRoot = Split-Path -Parent $PSScriptRoot

$rootTargets = @(
    (Join-Path $repoRoot "tmp"),
    (Join-Path $repoRoot "bin"),
    (Join-Path $repoRoot "obj"),
    (Join-Path $repoRoot "release"),
    (Join-Path $repoRoot "ui/node_modules"),
    (Join-Path $repoRoot "ui/dist"),
    (Join-Path $repoRoot "ui/src-tauri/target")
)

$nestedTargets = @()
$testsPath = Join-Path $repoRoot "tests"
if (Test-Path $testsPath) {
    $nestedTargets = Get-ChildItem -Path $testsPath -Directory -Recurse |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Select-Object -ExpandProperty FullName
}

$targets = @($rootTargets + $nestedTargets) | Where-Object { Test-Path $_ } | Sort-Object -Unique

if (-not $targets) {
    Write-Host "No cleanup targets found."
    return
}

if (-not $Apply) {
    Write-Host "Preview only. Use -Apply to remove these paths:"
    $targets | ForEach-Object { Write-Host " - $_" }
    return
}

$targets | ForEach-Object {
    Write-Host "Removing: $_"
    Remove-Item -Recurse -Force $_
}

Write-Host "Cleanup complete."
