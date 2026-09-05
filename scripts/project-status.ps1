param([switch]$AsJson)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
Push-Location $repoRoot
try {
    $branch = (& git branch --show-current).Trim()
    $changes = @(& git status --short)
    $tag = (& git describe --tags --abbrev=0 2>$null).Trim()
    $tauriVersion = (Get-Content -Raw 'ui\src-tauri\tauri.conf.json' | ConvertFrom-Json).version
    $npmVersion = (Get-Content -Raw 'ui\package.json' | ConvertFrom-Json).version
    $cargoVersion = (Select-String -Path 'ui\src-tauri\Cargo.toml' -Pattern '^version = "([^"]+)"$').Matches[0].Groups[1].Value
    $dotnetVersion = (Select-String -Path 'CuePilot.csproj' -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
    $versions = @(@($tauriVersion, $npmVersion, $cargoVersion, $dotnetVersion) | Sort-Object -Unique)
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $installedShell = Join-Path $localAppData 'CuePilotDesktop\current\cuepilot-ui.exe'
    $legacyShell = Join-Path $localAppData 'CuePilot\cuepilot-ui.exe'

    if ($AsJson) {
        [ordered]@{
            schemaVersion = 1; generatedUtc = [DateTime]::UtcNow.ToString('o'); branch = $branch; tag = $tag
            dirty = $changes.Count -gt 0; changeCount = $changes.Count; changes = $changes
            versions = [ordered]@{ tauri = $tauriVersion; npm = $npmVersion; cargo = $cargoVersion; dotnet = $dotnetVersion; synchronized = $versions.Count -eq 1 }
            installed = if (Test-Path -LiteralPath $installedShell -PathType Leaf) { [ordered]@{ present = $true; version = (Get-Item -LiteralPath $installedShell).VersionInfo.FileVersion } } else { [ordered]@{ present = $false; version = $null } }
            legacyPresent = Test-Path -LiteralPath $legacyShell -PathType Leaf
            handoffHeading = if (Test-Path -LiteralPath 'HANDOFF.md') { Get-Content 'HANDOFF.md' -TotalCount 1 } else { $null }
            package = if (Test-Path -LiteralPath 'release\velopack\release-manifest.json') { Get-Content -Raw 'release\velopack\release-manifest.json' | ConvertFrom-Json } else { $null }
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

    $releaseManifest = 'release\velopack\release-manifest.json'
    if (Test-Path -LiteralPath $releaseManifest) {
        $manifest = Get-Content -Raw $releaseManifest | ConvertFrom-Json
        Write-Host "  package: v$($manifest.appVersion) · $($manifest.artifacts.setup.name)"
    } else {
        Write-Host '  package: not built in this checkout'
    }

    Write-Host ''
    Write-Host 'Fast routes' -ForegroundColor Cyan
    Write-Host '  current work  HANDOFF.md'
    Write-Host '  task map      docs/code-map.md'
    Write-Host '  backlog       docs/product-backlog.md'
    Write-Host '  full gate     pwsh -NoProfile -File scripts/verify.ps1 -All'
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
