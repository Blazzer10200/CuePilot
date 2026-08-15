[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$BuildOutputs,
    [switch]$Dependencies,
    [switch]$Captures
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$repoPrefix = $repoRoot + '\'
$captureDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "ui/scripts/cdp/.tmp")).TrimEnd('\')

if (-not ($BuildOutputs -or $Dependencies -or $Captures)) {
    $BuildOutputs = $true
}

$targets = [System.Collections.Generic.List[string]]::new()
if ($BuildOutputs) {
    @(
        "tmp",
        "bin",
        "obj",
        "release",
        "ui/dist",
        "ui/src-tauri/target"
    ) | ForEach-Object { $targets.Add((Join-Path $repoRoot $_)) }

    Get-ChildItem -LiteralPath $repoRoot -Directory -Filter "publish*" -ErrorAction SilentlyContinue |
        ForEach-Object { $targets.Add($_.FullName) }

    $testsPath = Join-Path $repoRoot "tests"
    if (Test-Path -LiteralPath $testsPath -PathType Container) {
        Get-ChildItem -LiteralPath $testsPath -Directory -Recurse |
            Where-Object { $_.Name -in @("bin", "obj") } |
            ForEach-Object { $targets.Add($_.FullName) }
    }
}

if ($Dependencies) {
    $targets.Add((Join-Path $repoRoot "ui/node_modules"))
}

if ($Captures) {
    $targets.Add($captureDirectory)
}

$existingTargets = @($targets) |
    Sort-Object -Unique |
    Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
    ForEach-Object {
        $resolved = (Resolve-Path -LiteralPath $_).Path.TrimEnd('\')
        if (-not $resolved.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Cleanup target escaped the repository: $resolved"
        }
        if ($resolved.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean the repository root."
        }
        $resolved
    }

if (-not $existingTargets) {
    Write-Host "No selected cleanup targets found."
    return
}

$summary = foreach ($target in $existingTargets) {
    $files = Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Path = $target
        Files = ($files | Measure-Object).Count
        Megabytes = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
    }
}

$summary | Format-Table -AutoSize
if (-not $Apply) {
    Write-Host "Preview only. Add -Apply to remove the selected targets."
    Write-Host "Dependency caches are preserved unless -Dependencies is supplied."
    return
}

foreach ($target in $existingTargets) {
    if ($target.Equals($captureDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Clearing generated captures: $target"
        foreach ($item in Get-ChildItem -LiteralPath $target -Force) {
            $itemPath = [System.IO.Path]::GetFullPath($item.FullName)
            if (-not $itemPath.StartsWith($captureDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Capture cleanup target escaped its directory: $itemPath"
            }
            Remove-Item -LiteralPath $itemPath -Recurse -Force
        }
    }
    else {
        Write-Host "Removing: $target"
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

Write-Host "Cleanup complete."
