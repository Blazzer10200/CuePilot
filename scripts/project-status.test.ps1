$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Set-FixtureVersions {
    param([string]$Root, [string]$LockRootVersion = '1.2.3', [string]$CargoLockVersion = '1.2.3')

    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'ui\src-tauri') | Out-Null
    Set-Content -LiteralPath (Join-Path $Root 'CuePilot.csproj') -Value '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    Set-Content -LiteralPath (Join-Path $Root 'ui\package.json') -Value '{"version":"1.2.3"}'
    Set-Content -LiteralPath (Join-Path $Root 'ui\package-lock.json') -Value "{`"version`":`"1.2.3`",`"packages`":{`"`":{`"version`":`"$LockRootVersion`"}}}"
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\tauri.conf.json') -Value '{"version":"1.2.3"}'
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\Cargo.toml') -Value "[package]`nname = `"cuepilot-ui`"`nversion = `"1.2.3`""
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\Cargo.lock') -Value "[[package]]`nname = `"cuepilot-ui`"`nversion = `"$CargoLockVersion`""
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cuepilot-project-status-" + [guid]::NewGuid())
$temporaryRoot = [System.IO.Path]::GetFullPath(([System.IO.Path]::GetTempPath())).TrimEnd('\') + '\'
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Push-Location $fixtureRoot
    git init -q
    git config user.email 'test@cuepilot.invalid'
    git config user.name 'CuePilot test'
    Set-FixtureVersions -Root $fixtureRoot
    git add .
    git commit -qm fixture
    Pop-Location

    $statusScript = Join-Path $PSScriptRoot 'project-status.ps1'
    $baseline = & pwsh -NoProfile -File $statusScript -AsJson -RepositoryRoot $fixtureRoot | ConvertFrom-Json
    Assert-True $baseline.versions.synchronized 'The synchronized fixture was reported as mismatched.'
    Assert-True ($baseline.versions.npmLock -eq '1.2.3' -and $baseline.versions.npmLockRoot -eq '1.2.3') 'Both npm lockfile version fields were not read.'
    Assert-True ($baseline.versions.cargoLock -eq '1.2.3') 'The cuepilot-ui Cargo.lock package version was not read.'

    Set-FixtureVersions -Root $fixtureRoot -LockRootVersion '1.2.4'
    $lockMismatch = & pwsh -NoProfile -File $statusScript -AsJson -RepositoryRoot $fixtureRoot | ConvertFrom-Json
    Assert-True (-not $lockMismatch.versions.synchronized) 'A package-lock root metadata mismatch was not reported.'

    Set-FixtureVersions -Root $fixtureRoot -CargoLockVersion '1.2.4'
    $cargoMismatch = & pwsh -NoProfile -File $statusScript -AsJson -RepositoryRoot $fixtureRoot | ConvertFrom-Json
    Assert-True (-not $cargoMismatch.versions.synchronized) 'A cuepilot-ui Cargo.lock mismatch was not reported.'

    Write-Output 'project-status fixture tests passed (5 assertions).'
}
finally {
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove fixture outside the temporary root: $resolvedFixture" }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
