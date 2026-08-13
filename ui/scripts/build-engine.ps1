param(
    [switch]$Release
)

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$configuration = if ($Release) { 'Release' } else { 'Debug' }
$destination = Join-Path $projectRoot 'ui\src-tauri\resources\engine'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
dotnet publish (Join-Path $projectRoot 'WorkflowLooper.csproj') -c $configuration -o $destination
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Engine sidecar staged at $destination"
