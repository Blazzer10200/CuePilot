param(
    [Parameter(Mandatory)][string]$Manifest,
    [string]$TargetColor = 'Yellow',
    [int[]]$AdvanceMilliseconds = @(8, 14, 17, 20),
    [string]$Output = 'tmp/pickpocket-benchmark.json'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$engine = Join-Path $root 'bin/Release/net8.0-windows10.0.19041.0/win-x64/CuePilot.exe'
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { throw 'Build the Release engine before running the benchmark.' }
$runs = foreach ($advance in $AdvanceMilliseconds) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $lines = @(& $engine --replay-pickpocket $manifestPath --target-color $TargetColor --advance-ms $advance 2>&1)
    $exit = $LASTEXITCODE
    $watch.Stop()
    $summary = $lines | Where-Object { $_ -match '^frames=' } | Select-Object -Last 1
    [ordered]@{ advanceMs = $advance; exitCode = $exit; durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2); summary = $summary; output = $lines }
}
$receipt = [ordered]@{
    schemaVersion = 1; generatedUtc = [DateTime]::UtcNow.ToString('o'); manifest = $manifestPath
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    targetColor = $TargetColor; machine = [Environment]::MachineName; os = [Environment]::OSVersion.VersionString
    engineVersion = (Get-Item -LiteralPath $engine).VersionInfo.FileVersion; warmup = 'Production replay performs detector warm-up per run.'; runs = $runs
    note = 'Replay timing is machine-local. Counterfactual plans do not change recorded outcomes or prove live game receipt.'
}
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
Write-Host "Pickpocket benchmark: $outputPath"
if ($runs.exitCode -contains 2) { exit 2 }
if ($runs.exitCode -contains 1) { exit 1 }
