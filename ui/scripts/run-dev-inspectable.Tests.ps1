$ErrorActionPreference = 'Stop'

$script:assertionCount = 0
$cargoTargetBeforeDefinitions = [Environment]::GetEnvironmentVariable('CARGO_TARGET_DIR', 'Process')
$launcher = Join-Path $PSScriptRoot 'run-dev-inspectable.ps1'
. $launcher -DefinitionsOnly

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:assertionCount++
    if (-not $Condition) {
        throw $Message
    }
}

function New-ProcessRecord {
    param(
        [int]$ProcessId,
        [int]$ParentProcessId,
        [string]$Name,
        [string]$CommandLine = '',
        [string]$ExecutablePath = ''
    )

    [pscustomobject]@{
        ProcessId = $ProcessId
        ParentProcessId = $ParentProcessId
        Name = $Name
        CommandLine = $CommandLine
        ExecutablePath = $ExecutablePath
    }
}

$uiRoot = 'C:\dev\CuePilot\ui'
$cargoTargetPath = 'C:\dev\CuePilot\tmp\cargo-dev'
$directViteBuild = New-ProcessRecord -ProcessId 99 -ParentProcessId 1 -Name 'node.exe' `
    -CommandLine 'node C:\dev\CuePilot\ui\node_modules\vite\bin\vite.js build --configLoader runner'
$independentBuild = New-ProcessRecord -ProcessId 100 -ParentProcessId 1 -Name 'node.exe' `
    -CommandLine 'node C:\dev\CuePilot\ui\node_modules\@tauri-apps\cli\tauri.js build'
$siblingCheckout = New-ProcessRecord -ProcessId 101 -ParentProcessId 1 -Name 'node.exe' `
    -CommandLine 'node C:\dev\CuePilot\ui-next\node_modules\@tauri-apps\cli\tauri.js dev'
$installedApp = New-ProcessRecord -ProcessId 102 -ParentProcessId 1 -Name 'cuepilot-ui.exe' `
    -ExecutablePath 'C:\Program Files\CuePilot\cuepilot-ui.exe'
$tauri = New-ProcessRecord -ProcessId 200 -ParentProcessId 1 -Name 'node.exe' `
    -CommandLine 'node C:\dev\CuePilot\ui\node_modules\@tauri-apps\cli\tauri.js dev'
$viteChild = New-ProcessRecord -ProcessId 201 -ParentProcessId 200 -Name 'node.exe' `
    -CommandLine 'node C:\dev\CuePilot\ui\node_modules\vite\bin\vite.js'
$owned = Get-CuePilotDevOwnedProcessIds -Processes @($viteChild, $installedApp, $siblingCheckout, $tauri, $independentBuild, $directViteBuild) `
    -UiRoot $uiRoot -CargoTargetPath $cargoTargetPath

Assert-True ($cargoTargetBeforeDefinitions -eq [Environment]::GetEnvironmentVariable('CARGO_TARGET_DIR', 'Process')) `
    'DefinitionsOnly changed CARGO_TARGET_DIR.'
Assert-True (-not $owned.Contains(99)) 'An independent Vite production build was selected.'
Assert-True (-not $owned.Contains(100)) 'A Tauri build in a dev-named path was selected.'
Assert-True (-not $owned.Contains(101)) 'A sibling checkout Tauri dev process was selected.'
Assert-True (-not $owned.Contains(102)) 'An installed CuePilot shell was selected.'
Assert-True ($owned.Contains(200)) 'The repository Tauri dev process was not selected.'
Assert-True ($owned.Contains(201)) 'The repository Tauri Vite child was not selected when inventory was reordered.'
Assert-True (-not (Test-CuePilotDevExecutable 'C:\dev\Other\tmp\cargo-dev\debug\cuepilot-ui.exe' $cargoTargetPath)) `
    'Another checkout cargo target was selected.'
Assert-True (-not (Test-CuePilotDevSidecarExecutable 'C:\dev\Other\tmp\cargo-dev\debug\resources\engine\CuePilot.exe' $cargoTargetPath)) `
    'Another checkout sidecar was selected.'
Assert-True (Test-CuePilotDevExecutable (Join-Path $cargoTargetPath 'debug\cuepilot-ui.exe') $cargoTargetPath) `
    'The configured repository development shell was not selected.'
Assert-True (Test-CuePilotDevSidecarExecutable (Join-Path $cargoTargetPath 'debug\resources\engine\CuePilot.exe') $cargoTargetPath) `
    'The configured repository development sidecar was not selected.'

Write-Output "run-dev-inspectable synthetic ownership tests passed ($script:assertionCount assertions)."
