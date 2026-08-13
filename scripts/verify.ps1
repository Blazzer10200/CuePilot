param(
    [switch]$DotNet,
    [switch]$Ui,
    [switch]$Rust,
    [switch]$All
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($All) {
    $DotNet = $true
    $Ui = $true
    $Rust = $true
}

if (-not ($DotNet -or $Ui -or $Rust)) {
    $DotNet = $true
    $Ui = $true
    $Rust = $true
}

if ($DotNet) {
    Write-Host "dotnet: restore"
    dotnet restore

    Write-Host "dotnet: build"
    dotnet build WorkflowLooper.sln -c Release

    Write-Host "dotnet: test"
    dotnet test tests/WorkflowLooper.Tests/WorkflowLooper.Tests.csproj -c Release --no-build
}

if ($Ui) {
    Write-Host "ui: check + build"
    Push-Location (Join-Path $repoRoot "ui")
    if (-not (Test-Path "node_modules")) {
        Write-Host "node_modules missing: npm install"
        npm install
    }
    npm run check
    npm run build
    Pop-Location
}

if ($Rust) {
    Write-Host "rust: cargo check"
    cargo check --manifest-path (Join-Path $repoRoot "ui/src-tauri/Cargo.toml")
}
