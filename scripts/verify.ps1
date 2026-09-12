param(
    [switch]$DotNet,
    [switch]$Ui,
    [switch]$Browser,
    [switch]$Rust,
    [switch]$Docs,
    [switch]$All
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Assert-NativeSuccess {
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($All) {
    $DotNet = $true
    $Ui = $true
    $Browser = $true
    $Rust = $true
    $Docs = $true
}

if (-not ($DotNet -or $Ui -or $Browser -or $Rust -or $Docs)) {
    $DotNet = $true
    $Ui = $true
    $Browser = $true
    $Rust = $true
    $Docs = $true
}

if ($Docs) {
    Write-Host 'docs: local links and repository-tool regressions'
    node scripts/check-docs.cjs
    Assert-NativeSuccess
    node --test scripts/check-docs.test.cjs
    Assert-NativeSuccess
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'project-package.test.ps1')
    Assert-NativeSuccess
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'project-status.test.ps1')
    Assert-NativeSuccess
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'write-verification-receipt.test.ps1')
    Assert-NativeSuccess
    & pwsh -NoProfile -File (Join-Path $repoRoot 'ui/scripts/run-dev-inspectable.Tests.ps1')
    Assert-NativeSuccess
}

if ($DotNet) {
    Write-Host "dotnet: restore"
    dotnet restore CuePilot.sln
    Assert-NativeSuccess

    Write-Host "dotnet: build"
    dotnet build CuePilot.sln -c Release --no-restore
    Assert-NativeSuccess

    Write-Host "dotnet: test"
    dotnet test tests/CuePilot.Tests/CuePilot.Tests.csproj -c Release --no-build --no-restore
    Assert-NativeSuccess

    Write-Host "dotnet: headless self-test"
    & (Join-Path $repoRoot "bin/Release/net8.0-windows10.0.19041.0/win-x64/CuePilot.exe") --self-test
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Ui) {
    Write-Host "ui: test + check + build"
    Push-Location (Join-Path $repoRoot "ui")
    if (-not (Test-Path "node_modules")) {
        Write-Host "node_modules missing: npm install"
        npm install
        Assert-NativeSuccess
    }
    npm test
    Assert-NativeSuccess
    npm run check
    Assert-NativeSuccess
    npm run build
    Assert-NativeSuccess
    Pop-Location
}

if ($Browser) {
    Write-Host 'browser: Playwright end-to-end scenarios (installed Microsoft Edge)'
    Push-Location (Join-Path $repoRoot 'ui')
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host 'node_modules missing: npm install'
            npm install
            Assert-NativeSuccess
        }
        npm run test:e2e
        Assert-NativeSuccess
    }
    finally {
        Pop-Location
    }
}

if ($Rust) {
    Write-Host "rust: stage engine sidecar"
    & (Join-Path $repoRoot "ui/scripts/build-engine.ps1") -Release
    Assert-NativeSuccess

    Write-Host "rust: format check"
    cargo fmt --manifest-path (Join-Path $repoRoot "ui/src-tauri/Cargo.toml") -- --check
    Assert-NativeSuccess

    Write-Host "rust: clippy"
    cargo clippy --manifest-path (Join-Path $repoRoot "ui/src-tauri/Cargo.toml") --all-targets -- -D warnings
    Assert-NativeSuccess

    Write-Host "rust: cargo test"
    cargo test --manifest-path (Join-Path $repoRoot "ui/src-tauri/Cargo.toml")
    Assert-NativeSuccess
}
