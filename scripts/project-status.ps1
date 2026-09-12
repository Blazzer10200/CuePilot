param(
    [switch]$AsJson,
    [string]$RepositoryRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'project-package.ps1')
. (Join-Path $PSScriptRoot 'project-version.ps1')

$repoRoot = if ($RepositoryRoot) {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
} else {
    (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
}
Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    $changes = @(& git status --short)
    $tagOutput = @(& git describe --tags --abbrev=0 2>$null)
    $tag = if ($tagOutput.Count -gt 0) { $tagOutput[0].Trim() } else { '' }
    $versionInfo = Get-ProjectVersionInfo -RepositoryRoot $repoRoot
    $tauriVersion = $versionInfo.tauri
    $versions = @($versionInfo.values | Sort-Object -Unique)
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $installedShell = Join-Path $localAppData 'CuePilotDesktop\current\cuepilot-ui.exe'
    $legacyShell = Join-Path $localAppData 'CuePilot\cuepilot-ui.exe'
    $package = Get-ProjectPackage -RepoRoot $repoRoot -Version $tauriVersion

    if ($AsJson) {
        [ordered]@{
            schemaVersion = 1; generatedUtc = [DateTime]::UtcNow.ToString('o'); branch = $branch; tag = $tag
            dirty = $changes.Count -gt 0; changeCount = $changes.Count; changes = $changes
            versions = $versionInfo
            installed = if (Test-Path -LiteralPath $installedShell -PathType Leaf) { [ordered]@{ present = $true; version = (Get-Item -LiteralPath $installedShell).VersionInfo.FileVersion } } else { [ordered]@{ present = $false; version = $null } }
            legacyPresent = Test-Path -LiteralPath $legacyShell -PathType Leaf
            handoffHeading = if (Test-Path -LiteralPath 'HANDOFF.md') { Get-Content 'HANDOFF.md' -TotalCount 1 } else { $null }
            package = if ($package) { $package.manifest } else { $null }
            packageManifestPath = if ($package) { $package.path } else { $null }
            packageMatchesSource = if ($package) { $package.manifest.appVersion -eq $tauriVersion -and $versions.Count -eq 1 } else { $null }
        } | ConvertTo-Json -Depth 8
        return
    }
    Write-Host 'CuePilot project status' -ForegroundColor Cyan
    Write-Host "  branch:  $branch"
    Write-Host "  tag:     $tag"
    Write-Host "  changes: $($changes.Count)"
    Write-Host "  version: $($versions -join ', ')$(if ($versions.Count -eq 1) { ' (synchronized)' } else { ' (MISMATCH)' })"
    if (Test-Path -LiteralPath $installedShell -PathType Leaf) {
        $installedVersion = (Get-Item -LiteralPath $installedShell).VersionInfo.FileVersion
        Write-Host "  installed: v$installedVersion (Velopack)"
    } else {
        Write-Host '  installed: not installed'
    }
    Write-Host "  legacy:  $(if (Test-Path -LiteralPath $legacyShell -PathType Leaf) { 'present' } else { 'absent' })"

    if (Test-Path -LiteralPath 'HANDOFF.md') {
        $handoffHeading = Get-Content 'HANDOFF.md' -TotalCount 1
        Write-Host "  handoff: $handoffHeading"
    } else {
        Write-Host '  handoff: MISSING' -ForegroundColor Yellow
    }

    if ($package) {
        $manifest = $package.manifest
        $packageState = if ($manifest.appVersion -eq $tauriVersion -and $versions.Count -eq 1) { 'matches source version' } else { 'DIFFERS FROM SOURCE' }
        Write-Host "  package: v$($manifest.appVersion) · $($manifest.artifacts.setup.name) ($packageState)"
        Write-Host "           $($package.path)"
    } else {
        Write-Host '  package: not built in this checkout'
    }

    Write-Host ''
    Write-Host 'Fast routes' -ForegroundColor Cyan
    Write-Host '  docs index    docs/README.md'
    Write-Host '  current work  HANDOFF.md'
    Write-Host '  task map      docs/code-map.md'
    Write-Host '  backlog       docs/product-backlog.md'
    Write-Host '  full gate     pwsh -NoProfile -File scripts/verify.ps1 -All'
    Write-Host '  docs gate     pwsh -NoProfile -File scripts/verify.ps1 -Docs'
    Write-Host '  package       pwsh -NoProfile -File scripts/package-velopack.ps1'
    Write-Host '  cleanup       pwsh -NoProfile -File scripts/clean-workspace.ps1'
    Write-Host '  live UI       npm --prefix ui run cdp:dev'

    if ($changes.Count -gt 0) {
        Write-Host ''
        Write-Host 'Working tree' -ForegroundColor Cyan
        $changes | ForEach-Object { Write-Host "  $_" }
    }
} finally {
    Pop-Location
}
