param(
    [string]$Version,
    [string]$OutputDirectory = 'release\velopack',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'project-version.ps1')

$repoRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $outputRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Velopack output must stay under the repository: $outputRoot"
}
$staging = Join-Path $outputRoot '.staging'
if (-not $staging.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging path: $staging"
}

$versionInfo = Get-ProjectVersionInfo -RepositoryRoot $repoRoot
if (-not $Version) { $Version = [string]$versionInfo.tauri }
$versions = @(@($versionInfo.values + $Version) | Sort-Object -Unique)
if ($versions.Count -ne 1) {
    throw "CuePilot versions are not synchronized: $($versions -join ', ')"
}

$vpkCommand = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpkCommand) { throw 'Velopack CLI is missing. Install exact version 1.2.0 with: dotnet tool install -g vpk --version 1.2.0' }
$vpkBanner = (& vpk -h 2>&1 | Select-Object -First 3) -join "`n"
$vpkMatch = [regex]::Match($vpkBanner, 'Velopack CLI ([0-9.]+)')
if (-not $vpkMatch.Success -or $vpkMatch.Groups[1].Value -ne '1.2.0') {
    throw "vpk must be exactly 1.2.0 to match the Rust crate. Found: $vpkBanner"
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        & npm --prefix ui run tauri:build:portable
        if ($LASTEXITCODE -ne 0) { throw 'Tauri portable build failed' }
    }

    $cargoMetadata = & cargo metadata --manifest-path ui/src-tauri/Cargo.toml --format-version 1 --no-deps | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'cargo metadata failed' }
    $releaseRoot = Join-Path ([string]$cargoMetadata.target_directory) 'release'
    $shellExe = Join-Path $releaseRoot 'cuepilot-ui.exe'
    $engineExe = Join-Path $releaseRoot 'resources\engine\CuePilot.exe'
    foreach ($required in @($shellExe, $engineExe, (Join-Path $repoRoot 'assets\branding\cuepilot.ico'), (Join-Path $repoRoot 'LICENSE'))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required packaged file is missing: $required" }
    }

    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $staging 'resources\engine') | Out-Null

    Copy-Item -LiteralPath $shellExe -Destination (Join-Path $staging 'cuepilot-ui.exe')
    Copy-Item -LiteralPath $engineExe -Destination (Join-Path $staging 'resources\engine\CuePilot.exe')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'assets\branding\cuepilot.ico') -Destination (Join-Path $staging 'cuepilot.ico')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $staging 'LICENSE')

    # A rerun for the same version must replace its own generated packages but
    # preserve older full packages in the output directory as delta baselines.
    Get-ChildItem -LiteralPath $outputRoot -File -Filter "CuePilotDesktop-$Version-*.nupkg" -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $changelog = Get-Content -Raw (Join-Path $repoRoot 'CHANGELOG.md')
    $escapedVersion = [regex]::Escape($Version)
    $notesMatch = [regex]::Match($changelog, "(?ms)^## $escapedVersion[^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)")
    if (-not $notesMatch.Success) {
        $notesMatch = [regex]::Match($changelog, '(?ms)^## Unreleased[^\r\n]*\r?\n(?<body>.*?)(?=^## |\z)')
    }
    $notesFile = New-TemporaryFile
    try {
        $notes = if ($notesMatch.Success) { $notesMatch.Groups['body'].Value.Trim() } else { "CuePilot $Version" }
        Set-Content -LiteralPath $notesFile.FullName -Value $notes -Encoding utf8NoBOM
        $packArgs = @(
            'pack',
            # The legacy NSIS installer used %LOCALAPPDATA%\CuePilot for both
            # binaries and user data. A distinct pack ID prevents Velopack's
            # replace/uninstall operations from touching settings/diagnostics.
            '-u', 'CuePilotDesktop',
            '-v', $Version,
            '-p', $staging,
            '-e', 'cuepilot-ui.exe',
            '-c', 'win',
            '--packTitle', 'CuePilot',
            '--packAuthors', 'Blazzer',
            '--icon', (Join-Path $staging 'cuepilot.ico'),
            '--framework', 'webview2',
            '--instLicense', (Join-Path $repoRoot 'LICENSE'),
            '--releaseNotes', $notesFile.FullName,
            '-o', $outputRoot
        )
        & vpk @packArgs
        if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
    } finally {
        Remove-Item -LiteralPath $notesFile.FullName -Force -ErrorAction SilentlyContinue
    }

    $setup = @(Get-ChildItem -LiteralPath $outputRoot -File -Filter 'CuePilotDesktop-win-Setup.exe')
    $fullPackage = @(Get-ChildItem -LiteralPath $outputRoot -File -Filter "CuePilotDesktop-$Version-full.nupkg")
    $feed = Join-Path $outputRoot 'releases.win.json'
    if ($setup.Count -ne 1 -or $fullPackage.Count -ne 1 -or -not (Test-Path -LiteralPath $feed)) {
        throw "Velopack output is incomplete (setup=$($setup.Count), full=$($fullPackage.Count), feed=$(Test-Path -LiteralPath $feed))"
    }

    $setupHash = (Get-FileHash -LiteralPath $setup[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $fullHash = (Get-FileHash -LiteralPath $fullPackage[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$($setup[0].FullName).sha256" -Encoding ascii -Value "$setupHash  $($setup[0].Name)"

    $manifest = [ordered]@{
        schemaVersion = 1
        appVersion = $Version
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        velopackVersion = '1.2.0'
        shellFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($shellExe).FileVersion
        engineFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($engineExe).FileVersion
        artifacts = [ordered]@{
            setup = [ordered]@{ name = $setup[0].Name; sizeBytes = $setup[0].Length; sha256 = $setupHash }
            fullPackage = [ordered]@{ name = $fullPackage[0].Name; sizeBytes = $fullPackage[0].Length; sha256 = $fullHash }
            feed = 'releases.win.json'
        }
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'release-manifest.json') -Encoding utf8NoBOM

    Write-Host "Velopack package ready: $outputRoot" -ForegroundColor Green
    Write-Host "  installer: $($setup[0].Name) ($([Math]::Round($setup[0].Length / 1MB, 1)) MB)"
    Write-Host "  package:   $($fullPackage[0].Name) ($([Math]::Round($fullPackage[0].Length / 1MB, 1)) MB)"
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    Pop-Location
}
