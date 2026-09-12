$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'project-package.ps1')

# Substitute an in-memory manifest store: no installer, process, or filesystem changes.
$script:manifests = @{}
function Test-Path { param($LiteralPath, $PathType) return $script:manifests.ContainsKey($LiteralPath) }
function Get-Content { param($LiteralPath, [switch]$Raw) return $script:manifests[$LiteralPath] }
function Assert-Equal($Actual, $Expected, $Message) {
    if ($Actual -ne $Expected) { throw "$Message (expected '$Expected', got '$Actual')" }
}
$root = $PSScriptRoot
$standard = Join-Path $root 'release/velopack/release-manifest.json'
$versioned = Join-Path $root 'release/velopack-5.3.5/release-manifest.json'
Assert-Equal (Get-ProjectPackage -RepoRoot $root -Version '5.3.5') $null 'Missing packages stay absent'
$script:manifests[$standard] = '{"appVersion":"5.3.4"}'
Assert-Equal (Get-ProjectPackage -RepoRoot $root -Version '5.3.5').manifest.appVersion '5.3.4' 'Old package remains available as fallback'
$script:manifests[$versioned] = '{"appVersion":"5.3.5"}'
Assert-Equal (Get-ProjectPackage -RepoRoot $root -Version '5.3.5').path 'release/velopack-5.3.5/release-manifest.json' 'Matching versioned package wins'
$script:manifests[$versioned] = '{"appVersion":"5.3.3"}'
$script:manifests[$standard] = '{"appVersion":"5.3.5"}'
Assert-Equal (Get-ProjectPackage -RepoRoot $root -Version '5.3.5').path 'release/velopack/release-manifest.json' 'Manifest content wins over directory name'
Write-Host 'Package selection: 4 scenarios passed.'
