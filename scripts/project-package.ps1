function Get-ProjectPackage {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Version)

    # Do not scan smoke-test feeds or choose by modification time. Prefer the
    # explicitly versioned output, then the conventional packaging directory.
    $candidates = @(foreach ($relative in @("release/velopack-$Version/release-manifest.json", 'release/velopack/release-manifest.json')) {
        $manifestPath = Join-Path $RepoRoot $relative
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            [pscustomobject]@{
                path = $relative
                manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            }
        }
    })
    $matching = @($candidates | Where-Object { $_.manifest.appVersion -eq $Version })
    if ($matching.Count -gt 0) { return $matching[0] }
    if ($candidates.Count -gt 0) { return $candidates[0] }
    return $null
}
