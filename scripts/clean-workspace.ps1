[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$BuildOutputs,
    [switch]$StaleReleaseArtifacts,
    [switch]$ReleaseArtifacts,
    [switch]$Dependencies,
    [switch]$Captures,
    [switch]$CargoAppArtifacts
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$repoPrefix = $repoRoot + '\'
$captureDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "ui/scripts/cdp/.tmp")).TrimEnd('\')
$engineResourceDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "ui/src-tauri/resources/engine")).TrimEnd('\')

if (-not ($BuildOutputs -or $StaleReleaseArtifacts -or $ReleaseArtifacts -or $Dependencies -or $Captures -or $CargoAppArtifacts)) {
    $BuildOutputs = $true
}

$targets = [System.Collections.Generic.List[string]]::new()
if ($BuildOutputs) {
    @(
        "bin",
        "obj",
        "ui/dist",
        "ui/src-tauri/resources/engine"
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

if ($ReleaseArtifacts) {
    $targets.Add((Join-Path $repoRoot "release"))
}
elseif ($StaleReleaseArtifacts) {
    $releaseRoot = Join-Path $repoRoot "release"
    $targets.Add((Join-Path $releaseRoot "velopack-smoke"))
    $targets.Add((Join-Path $releaseRoot "velopack\.staging"))
    if (Test-Path -LiteralPath $releaseRoot -PathType Container) {
        Get-ChildItem -LiteralPath $releaseRoot -Directory -Filter "audit-*" -ErrorAction SilentlyContinue |
            ForEach-Object { $targets.Add($_.FullName) }
    }
}

if ($Dependencies) {
    $targets.Add((Join-Path $repoRoot "ui/node_modules"))
}

if ($Captures) {
    $targets.Add($captureDirectory)
}

$cargoAppFiles = @()
if ($CargoAppArtifacts) {
    if ([string]::IsNullOrWhiteSpace($env:CARGO_TARGET_DIR)) {
        throw "CARGO_TARGET_DIR must be set before selecting Cargo app artifacts."
    }

    $cargoTargetRoot = [System.IO.Path]::GetFullPath($env:CARGO_TARGET_DIR).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $cargoTargetRoot -PathType Container)) {
        throw "Cargo target directory does not exist: $cargoTargetRoot"
    }
    $cargoTargetRoot = (Resolve-Path -LiteralPath $cargoTargetRoot).Path.TrimEnd('\')
    $cargoTargetPrefix = $cargoTargetRoot + '\'
    if ([System.IO.Path]::GetPathRoot($cargoTargetRoot).TrimEnd('\').Equals($cargoTargetRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a drive root."
    }

    $cargoPatterns = @(
        @{ Directory = "release\bundle\nsis"; Filter = "CuePilot_*-setup.exe" },
        @{ Directory = "debug\deps"; Filter = "cuepilot_ui-*.exe" }
    )
    foreach ($pattern in $cargoPatterns) {
        $directory = Join-Path $cargoTargetRoot $pattern.Directory
        if (Test-Path -LiteralPath $directory -PathType Container) {
            $cargoAppFiles += Get-ChildItem -LiteralPath $directory -Filter $pattern.Filter -File -ErrorAction SilentlyContinue
        }
    }

    @(
        "release\cuepilot-ui.exe",
        "release\deps\cuepilot_ui.exe",
        "release\resources\engine\CuePilot.exe",
        "debug\cuepilot-ui.exe",
        "debug\deps\cuepilot_ui.exe",
        "debug\resources\engine\CuePilot.exe"
    ) | ForEach-Object {
        $path = Join-Path $cargoTargetRoot $_
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $cargoAppFiles += Get-Item -LiteralPath $path
        }
    }

    $cargoAppFiles = @($cargoAppFiles) | Sort-Object FullName -Unique | ForEach-Object {
        $resolved = [System.IO.Path]::GetFullPath($_.FullName)
        if (-not $resolved.StartsWith($cargoTargetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Cargo app artifact escaped the target directory: $resolved"
        }
        Get-Item -LiteralPath $resolved
    }
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

if (-not $existingTargets -and -not $cargoAppFiles) {
    Write-Host "No selected cleanup targets found."
    return
}

$summary = foreach ($target in $existingTargets) {
    $files = Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Path = [System.IO.Path]::GetRelativePath($repoRoot, $target)
        Files = ($files | Measure-Object).Count
        Megabytes = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
    }
}
$summary += foreach ($file in $cargoAppFiles) {
    [pscustomobject]@{
        Path = $file.FullName
        Files = 1
        Megabytes = [math]::Round(($file.Length / 1MB), 1)
    }
}

$summary | Sort-Object Megabytes -Descending | Format-Table Path, Files, Megabytes -AutoSize
$totalMegabytes = ($summary | Measure-Object Megabytes -Sum).Sum
Write-Host "Selected total: $([math]::Round($totalMegabytes, 1)) MB"
if (-not $Apply) {
    Write-Host "Preview only. Add -Apply to remove the selected targets."
    Write-Host "The current release is preserved unless -ReleaseArtifacts is supplied."
    Write-Host "Use -StaleReleaseArtifacts to select smoke and audit packages without selecting release/velopack."
    Write-Host "Dependency caches are preserved unless -Dependencies is supplied."
    Write-Host "tmp/ is preserved because it may contain user-data backups, replay evidence, and the development Cargo cache."
    Write-Host "Cargo dependency caches are preserved; -CargoAppArtifacts selects only runnable CuePilot binaries and legacy installers."
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
    elseif ($target.Equals($engineResourceDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Clearing staged engine payload: $target"
        foreach ($item in Get-ChildItem -LiteralPath $target -Force) {
            if ($item.Name -eq '.gitkeep') { continue }
            $itemPath = [System.IO.Path]::GetFullPath($item.FullName)
            if (-not $itemPath.StartsWith($engineResourceDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Engine cleanup target escaped its directory: $itemPath"
            }
            Remove-Item -LiteralPath $itemPath -Recurse -Force
        }
    }
    else {
        Write-Host "Removing: $target"
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

foreach ($file in $cargoAppFiles) {
    Write-Host "Removing Cargo app artifact: $($file.FullName)"
    Remove-Item -LiteralPath $file.FullName -Force
}

Write-Host "Cleanup complete."
