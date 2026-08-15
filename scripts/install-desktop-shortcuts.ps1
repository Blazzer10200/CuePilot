param(
    [Parameter(Mandatory = $true)]
    [string]$OfficialExecutable
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$desktop = [Environment]::GetFolderPath("Desktop")
$officialPath = (Resolve-Path -LiteralPath $OfficialExecutable).Path
$officialIconSource = (Resolve-Path -LiteralPath (Join-Path $projectRoot "assets\branding\cuepilot.ico")).Path
$devIconSource = (Resolve-Path -LiteralPath (Join-Path $projectRoot "assets\branding\cuepilot-dev.ico")).Path
$devLauncher = (Resolve-Path -LiteralPath (Join-Path $projectRoot "scripts\launch-cuepilot-dev.ps1")).Path
$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$brandingDirectory = Join-Path $env:LOCALAPPDATA "CuePilot\branding"

[System.IO.Directory]::CreateDirectory($brandingDirectory) | Out-Null
$officialIcon = Join-Path $brandingDirectory "cuepilot.ico"
$devIcon = Join-Path $brandingDirectory "cuepilot-dev.ico"
Copy-Item -LiteralPath $officialIconSource -Destination $officialIcon -Force
Copy-Item -LiteralPath $devIconSource -Destination $devIcon -Force

$shell = New-Object -ComObject WScript.Shell

$officialShortcutPath = Join-Path $desktop "CuePilot.lnk"
$officialShortcut = $shell.CreateShortcut($officialShortcutPath)
$officialShortcut.TargetPath = $officialPath
$officialShortcut.WorkingDirectory = Split-Path -Parent $officialPath
$officialShortcut.IconLocation = "$officialIcon,0"
$officialShortcut.Description = "CuePilot official release build"
$officialShortcut.Save()

$devShortcutPath = Join-Path $desktop "CuePilot Dev.lnk"
$devShortcut = $shell.CreateShortcut($devShortcutPath)
$devShortcut.TargetPath = $pwshPath
$devShortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$devLauncher`""
$devShortcut.WorkingDirectory = $projectRoot
$devShortcut.IconLocation = "$devIcon,0"
$devShortcut.Description = "Launch the CuePilot development app and local dev server"
$devShortcut.WindowStyle = 7
$devShortcut.Save()

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class CuePilotShellRefresh
{
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
"@
[CuePilotShellRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Created $officialShortcutPath"
Write-Host "Created $devShortcutPath"
