# Starts the CuePilot Tauri development app with a loopback-only
# WebView2 DevTools endpoint. The separate WebView profile prevents an existing
# runtime singleton from silently dropping the remote-debugging arguments.

[CmdletBinding()]
param(
    [switch]$WaitForCdp,
    [switch]$NoKill
)

$ErrorActionPreference = 'Stop'
$uiRoot = Split-Path -Parent $PSScriptRoot
$cdpPort = 9322
$profilePath = Join-Path $env:LOCALAPPDATA 'CuePilot Dev\EBWebView-Inspect'
$batchPath = Join-Path $env:TEMP 'cuepilot-inspect-dev.bat'
$taskName = 'CuePilotInspectDev'

function Test-Elevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-CuePilotDevExecutable {
    param([string]$ExecutablePath)

    if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
        return $false
    }

    $normalized = $ExecutablePath.Replace('/', '\')
    $repoTargets = @(
        (Join-Path $uiRoot 'src-tauri\target\debug\cuepilot-ui.exe')
    )
    if ($repoTargets | Where-Object { $normalized.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }) {
        return $true
    }

    if ($normalized -match '(?i)\\cargo-targets\\debug\\cuepilot-ui\.exe$') {
        return $true
    }

    if ($env:CARGO_TARGET_DIR) {
        $configuredTargets = @(
            (Join-Path $env:CARGO_TARGET_DIR 'debug\cuepilot-ui.exe')
        )
        if ($configuredTargets | Where-Object { $normalized.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }) {
            return $true
        }
    }

    return $false
}

function Stop-StaleCuePilotDev {
    $processes = @(Get-CimInstance Win32_Process)
    $devApps = @($processes | Where-Object {
        $_.Name -eq 'cuepilot-ui.exe' -and (Test-CuePilotDevExecutable $_.ExecutablePath)
    })

    $ownedIds = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($app in $devApps) {
        [void]$ownedIds.Add([int]$app.ProcessId)
    }

    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($process in $processes) {
            if ($ownedIds.Contains([int]$process.ParentProcessId) -and -not $ownedIds.Contains([int]$process.ProcessId)) {
                [void]$ownedIds.Add([int]$process.ProcessId)
                $changed = $true
            }
        }
    }

    $stopped = 0
    foreach ($processId in @($ownedIds)) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        $stopped++
    }

    $uiNeedle = $uiRoot.ToLowerInvariant()
    $devNodeProcesses = @($processes | Where-Object {
        $_.Name -eq 'node.exe' -and
        $_.CommandLine -and
        $_.CommandLine.ToLowerInvariant().Contains($uiNeedle) -and
        ($_.CommandLine -match '(?i)(tauri\.js.*\bdev\b|vite(?:\.js)?\b)')
    })
    foreach ($process in $devNodeProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        $stopped++
    }

    $inspectWebViews = @($processes | Where-Object {
        $_.Name -eq 'msedgewebview2.exe' -and
        $_.CommandLine -and
        $_.CommandLine.Contains($profilePath, [System.StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($process in $inspectWebViews) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        $stopped++
    }

    $debugSidecars = @($processes | Where-Object {
        $_.Name -eq 'CuePilot.exe' -and
        $_.ExecutablePath -and
        $_.CommandLine -match '(?i)--ui-bridge' -and
        ($_.ExecutablePath -match '(?i)\\cargo-targets\\debug\\resources\\engine\\CuePilot\.exe$' -or
            $_.ExecutablePath.StartsWith((Join-Path $uiRoot 'src-tauri\target\debug'), [System.StringComparison]::OrdinalIgnoreCase))
    })
    foreach ($process in $debugSidecars) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
        $stopped++
    }

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $remaining = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object {
            $_.CommandLine -and $_.CommandLine.Contains($profilePath, [System.StringComparison]::OrdinalIgnoreCase)
        }).Count
        if ($remaining -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 350
    }

    if ($stopped -gt 0) {
        Write-Output "[cdp:dev] stopped $stopped repository-scoped development process(es)."
    }
    else {
        Write-Output '[cdp:dev] no stale CuePilot development processes found.'
    }
}

if (-not $NoKill) {
    Stop-StaleCuePilotDev
}

$devCommand = @"
@echo off
set "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=$cdpPort --remote-debugging-address=127.0.0.1 --remote-allow-origins=*"
set "WEBVIEW2_USER_DATA_FOLDER=$profilePath"
cd /d "$uiRoot"
call npm run tauri:dev
"@
Set-Content -LiteralPath $batchPath -Value $devCommand -Encoding ASCII

if (Test-Elevated) {
    Write-Output '[cdp:dev] elevated shell detected; launching the WebView at medium integrity.'
    $userName = "$env:USERDOMAIN\$env:USERNAME"
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    schtasks /Delete /TN $taskName /F *>$null
    schtasks /Create /TN $taskName /TR "cmd.exe /c `"$batchPath`"" /SC ONCE /ST 00:00 /RL LIMITED /F /RU $userName *>$null
    if ($LASTEXITCODE -ne 0) {
        $ErrorActionPreference = $previousPreference
        throw "Could not create the medium-integrity launch task (exit $LASTEXITCODE)."
    }
    schtasks /Run /TN $taskName *>$null
    if ($LASTEXITCODE -ne 0) {
        $ErrorActionPreference = $previousPreference
        throw "Could not run the medium-integrity launch task (exit $LASTEXITCODE)."
    }
    Start-Sleep -Seconds 2
    schtasks /Delete /TN $taskName /F *>$null
    $ErrorActionPreference = $previousPreference
    Write-Output '[cdp:dev] inspectable app launched; temporary scheduled task removed.'
}
else {
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$batchPath`"" -WindowStyle Hidden
    Write-Output '[cdp:dev] inspectable app launched in the background.'
}

if ($WaitForCdp) {
    Write-Output "[cdp:dev] waiting for WebView2 CDP on 127.0.0.1:$cdpPort ..."
    $readyCount = 0
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$cdpPort/json/version" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                $readyCount++
                if ($readyCount -ge 2) {
                    break
                }
            }
        }
        catch {
            $readyCount = 0
        }
        Start-Sleep -Seconds 2
    }

    if ($readyCount -lt 2) {
        throw "WebView2 CDP did not bind within 120 seconds. Run 'npm run cdp:doctor' for the next check."
    }
    Write-Output "[cdp:dev] WebView2 CDP is ready on 127.0.0.1:$cdpPort."
}
