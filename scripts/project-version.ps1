function Get-ProjectVersionInfo {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $tauri = (Get-Content -Raw (Join-Path $RepositoryRoot 'ui\src-tauri\tauri.conf.json') | ConvertFrom-Json).version
    $npm = (Get-Content -Raw (Join-Path $RepositoryRoot 'ui\package.json') | ConvertFrom-Json).version
    $npmLock = Get-Content -Raw (Join-Path $RepositoryRoot 'ui\package-lock.json') | ConvertFrom-Json -AsHashtable
    $npmLockVersion = $npmLock['version']
    $npmLockRoot = $npmLock['packages']['']['version']
    $cargo = (Select-String -Path (Join-Path $RepositoryRoot 'ui\src-tauri\Cargo.toml') -Pattern '^version = "([^"]+)"$').Matches[0].Groups[1].Value
    $cargoLockMatch = [regex]::Match((Get-Content -Raw (Join-Path $RepositoryRoot 'ui\src-tauri\Cargo.lock')), '(?ms)^\[\[package\]\]\s+name = "cuepilot-ui"\s+version = "([^"]+)"')
    if (-not $cargoLockMatch.Success) { throw 'Could not find the cuepilot-ui package version in ui/src-tauri/Cargo.lock.' }
    $cargoLock = $cargoLockMatch.Groups[1].Value
    $dotnet = (Select-String -Path (Join-Path $RepositoryRoot 'CuePilot.csproj') -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
    $values = @($tauri, $npm, $npmLockVersion, $npmLockRoot, $cargo, $cargoLock, $dotnet)

    [pscustomobject]@{
        tauri = $tauri; npm = $npm; npmLock = $npmLockVersion; npmLockRoot = $npmLockRoot
        cargo = $cargo; cargoLock = $cargoLock; dotnet = $dotnet
        values = $values; synchronized = (@($values | Sort-Object -Unique).Count -eq 1)
    }
}
