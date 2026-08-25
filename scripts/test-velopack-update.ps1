param(
    [string]$BaseVersion = '5.2.0',
    [string]$UpdateVersion = '5.2.1',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$smokeRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'release\velopack-smoke'))
$repoPrefix = $repoRoot.TrimEnd('\') + '\'
if (-not $smokeRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Smoke-test output must stay under the repository: $smokeRoot"
}
$installRoot = Join-Path $smokeRoot 'install'
$baseFeed = Join-Path $smokeRoot 'base-feed'
$updateFeed = Join-Path $smokeRoot 'update-feed'
$resultFile = Join-Path $smokeRoot 'result.json'
$packId = 'CuePilotUpdaterSmoke'
$packTitle = 'CuePilot Updater Smoke'

function Assert-SmokePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $smokePrefix = $smokeRoot.TrimEnd('\') + '\'
    if ($full -ne $smokeRoot -and -not $full.StartsWith($smokePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe updater smoke-test path: $full"
    }
}

function New-SmokeStage([string]$Version, [string]$Path, [string]$ShellExe, [string]$EngineExe) {
    Assert-SmokePath $Path
    New-Item -ItemType Directory -Force -Path (Join-Path $Path 'resources\engine') | Out-Null
    Copy-Item -LiteralPath $ShellExe -Destination (Join-Path $Path 'cuepilot-ui.exe')
    Copy-Item -LiteralPath $EngineExe -Destination (Join-Path $Path 'resources\engine\CuePilot.exe')
    Set-Content -LiteralPath (Join-Path $Path 'smoke-version.txt') -Encoding ascii -Value $Version
}

function Invoke-SmokePack([string]$Version, [string]$Stage, [string]$Output) {
    Assert-SmokePath $Stage
    Assert-SmokePath $Output
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    & vpk pack -u $packId -v $Version -p $Stage -e 'cuepilot-ui.exe' -c win `
        --packTitle $packTitle --packAuthors 'Blazzer' --noPortable --skipVeloAppCheck -o $Output
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed for smoke version $Version" }
}

function Stop-SmokeProcesses {
    $processes = Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
    }
    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

foreach ($path in @($smokeRoot, $installRoot, $baseFeed, $updateFeed)) { Assert-SmokePath $path }

if (Test-Path -LiteralPath (Join-Path $installRoot 'Update.exe')) {
    $cleanup = Start-Process -FilePath (Join-Path $installRoot 'Update.exe') `
        -ArgumentList 'uninstall --silent' -WindowStyle Hidden -PassThru -Wait
    if ($cleanup.ExitCode -ne 0) { throw "Prior smoke uninstall failed with exit code $($cleanup.ExitCode)" }
    Start-Sleep -Seconds 2
}
Stop-SmokeProcesses
if (Test-Path -LiteralPath $smokeRoot) {
    Remove-Item -LiteralPath $smokeRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null

$previousFeed = $env:CUEPILOT_UPDATE_FEED
try {
    if (-not $SkipBuild) {
        Push-Location $repoRoot
        try {
            & npm --prefix ui run engine:stage:release
            if ($LASTEXITCODE -ne 0) { throw 'Release engine staging failed' }
            & npm --prefix ui run tauri -- build --no-bundle --features update-test-feed --ci
            if ($LASTEXITCODE -ne 0) { throw 'Feature-gated Tauri smoke build failed' }
        } finally {
            Pop-Location
        }
    }

    $metadata = & cargo metadata --manifest-path (Join-Path $repoRoot 'ui\src-tauri\Cargo.toml') --format-version 1 --no-deps | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'cargo metadata failed' }
    $releaseRoot = Join-Path ([string]$metadata.target_directory) 'release'
    $shellExe = Join-Path $releaseRoot 'cuepilot-ui.exe'
    $engineExe = Join-Path $releaseRoot 'resources\engine\CuePilot.exe'
    foreach ($required in @($shellExe, $engineExe)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Smoke payload is missing: $required" }
    }

    $baseStage = Join-Path $smokeRoot 'stage-base'
    $updateStage = Join-Path $smokeRoot 'stage-update'
    New-SmokeStage $BaseVersion $baseStage $shellExe $engineExe
    New-SmokeStage $UpdateVersion $updateStage $shellExe $engineExe
    Invoke-SmokePack $BaseVersion $baseStage $baseFeed

    New-Item -ItemType Directory -Force -Path $updateFeed | Out-Null
    Copy-Item -LiteralPath (Join-Path $baseFeed "$packId-$BaseVersion-full.nupkg") -Destination $updateFeed
    Invoke-SmokePack $UpdateVersion $updateStage $updateFeed

    $setup = Get-Item -LiteralPath (Join-Path $baseFeed "$packId-win-Setup.exe")
    $installArguments = "--silent --installto `"$installRoot`""
    # -Wait follows the full installer process tree. Invoking Setup.exe
    # directly can return after it spawns its worker, while that worker is still
    # finishing shortcuts/hooks and closing processes in the install directory.
    $installer = Start-Process -FilePath $setup.FullName -ArgumentList $installArguments `
        -WindowStyle Hidden -PassThru -Wait
    if ($installer.ExitCode -ne 0) { throw "Smoke installer failed with exit code $($installer.ExitCode)" }

    # Velopack's stable root launcher follows the configured main executable
    # name; packTitle controls shortcuts and Apps & Features presentation.
    $stub = Join-Path $installRoot 'cuepilot-ui.exe'
    $installDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $installDeadline -and -not (Test-Path -LiteralPath $stub -PathType Leaf)) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $stub -PathType Leaf)) { throw "Installed launcher is missing: $stub" }
    $installedBase = (Get-Content -Raw -LiteralPath (Join-Path $installRoot 'current\smoke-version.txt')).Trim()
    if ($installedBase -ne $BaseVersion) { throw "Expected installed base $BaseVersion, found $installedBase" }

    $env:CUEPILOT_UPDATE_FEED = $updateFeed
    $argumentLine = "--velopack-smoke `"$resultFile`" $UpdateVersion"
    Start-Process -FilePath $stub -ArgumentList $argumentLine -WindowStyle Hidden | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    $result = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $resultFile -PathType Leaf) {
            try {
                $candidate = Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json
                if ($candidate.phase -in @('complete', 'error')) {
                    $result = $candidate
                    break
                }
            } catch {
                # The updater may be replacing the small result file between polls.
            }
        }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $result) { throw 'Timed out waiting for the installed updater smoke test' }
    if (-not $result.success) { throw "Updater smoke failed: $($result | ConvertTo-Json -Compress)" }
    if ($result.currentVersion -ne $UpdateVersion -or $result.installedMarker -ne $UpdateVersion -or -not $result.sidecarStopped) {
        throw "Updater smoke result is incomplete: $($result | ConvertTo-Json -Compress)"
    }

    $currentMarker = (Get-Content -Raw -LiteralPath (Join-Path $installRoot 'current\smoke-version.txt')).Trim()
    if ($currentMarker -ne $UpdateVersion) { throw "Installed payload did not advance to $UpdateVersion" }
    Write-Host "Velopack installed update passed: $BaseVersion -> $UpdateVersion" -ForegroundColor Green
    Write-Host "  identity: $packId"
    Write-Host "  sidecar:  stopped before apply"
    Write-Host "  relaunch: reported $($result.currentVersion)"
} finally {
    $env:CUEPILOT_UPDATE_FEED = $previousFeed
    Stop-SmokeProcesses
    $updater = Join-Path $installRoot 'Update.exe'
    if (Test-Path -LiteralPath $updater -PathType Leaf) {
        for ($attempt = 1; $attempt -le 3 -and (Test-Path -LiteralPath $updater -PathType Leaf); $attempt++) {
            $cleanup = Start-Process -FilePath $updater -ArgumentList 'uninstall --silent' `
                -WindowStyle Hidden -PassThru -Wait
            if ($cleanup.ExitCode -eq 0) { break }
            Write-Warning "Smoke uninstall attempt $attempt returned exit code $($cleanup.ExitCode)"
            Start-Sleep -Seconds 2
        }
        Start-Sleep -Seconds 2
    }
}

$leftovers = Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
}
if ($leftovers) { throw 'Updater smoke test left an installed process running' }
if (Test-Path -LiteralPath (Join-Path $installRoot 'current')) { throw "Updater smoke install was not removed: $installRoot" }
