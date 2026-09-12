$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Set-FixtureVersions {
    param([string]$Root)
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'ui\src-tauri') | Out-Null
    Set-Content -LiteralPath (Join-Path $Root 'CuePilot.csproj') -Value '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    Set-Content -LiteralPath (Join-Path $Root 'ui\package.json') -Value '{"version":"1.2.3"}'
    Set-Content -LiteralPath (Join-Path $Root 'ui\package-lock.json') -Value '{"version":"1.2.3","packages":{"":{"version":"1.2.3"}}}'
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\tauri.conf.json') -Value '{"version":"1.2.3"}'
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\Cargo.toml') -Value "[package]`nname = `"cuepilot-ui`"`nversion = `"1.2.3`""
    Set-Content -LiteralPath (Join-Path $Root 'ui\src-tauri\Cargo.lock') -Value "[[package]]`nname = `"cuepilot-ui`"`nversion = `"1.2.3`""
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cuepilot-receipt-" + [guid]::NewGuid())
$receiptOutputRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cuepilot-receipt-output-" + [guid]::NewGuid())
$temporaryRoot = [System.IO.Path]::GetFullPath(([System.IO.Path]::GetTempPath())).TrimEnd('\') + '\'
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    New-Item -ItemType Directory -Path $receiptOutputRoot | Out-Null
    Push-Location $fixtureRoot
    git init -q
    git config user.email 'test@cuepilot.invalid'
    git config user.name 'CuePilot test'
    Set-FixtureVersions -Root $fixtureRoot
    Set-Content -LiteralPath 'tracked file.txt' -Value 'base'
    Set-Content -LiteralPath 'removed file.txt' -Value 'base'
    Set-Content -LiteralPath 'unstaged file.txt' -Value 'base'
    git add .
    git commit -qm fixture
    Pop-Location

    $receiptScript = Join-Path $PSScriptRoot 'write-verification-receipt.ps1'
    $cleanPath = Join-Path $receiptOutputRoot 'receipt-clean.json'
    & pwsh -NoProfile -File $receiptScript -RepositoryRoot $fixtureRoot -Output $cleanPath -Status passed
    $clean = Get-Content -LiteralPath $cleanPath -Raw | ConvertFrom-Json
    Assert-True ($clean.stagedFiles -is [array] -and $clean.unstagedFiles -is [array] -and $clean.untrackedFiles -is [array]) 'Empty path lists must remain JSON arrays.'
    Assert-True ($null -eq $clean.dirtyContentFingerprint) 'A clean tree produced a dirty fingerprint.'

    Push-Location $fixtureRoot
    Set-Content -LiteralPath 'tracked file.txt' -Value 'staged content'
    Set-Content -LiteralPath 'unstaged file.txt' -Value 'unstaged content'
    git add -- 'tracked file.txt'
    Pop-Location
    $singlePath = Join-Path $receiptOutputRoot 'receipt-single.json'
    & pwsh -NoProfile -File $receiptScript -RepositoryRoot $fixtureRoot -Output $singlePath -Status passed
    $single = Get-Content -LiteralPath $singlePath -Raw | ConvertFrom-Json
    Assert-True ($single.stagedFiles -is [array] -and $single.stagedFiles.Count -eq 1 -and $single.unstagedFiles -is [array] -and $single.unstagedFiles.Count -eq 1) 'Single-file path lists must remain JSON arrays.'
    Assert-True ($single.dirtyFiles.Count -eq 2 -and $single.dirtyFiles -contains 'tracked file.txt' -and $single.dirtyFiles -contains 'unstaged file.txt') 'Single staged/unstaged paths were concatenated instead of combined.'

    Push-Location $fixtureRoot
    Set-Content -LiteralPath 'tracked file.txt' -Value 'staged content'
    Remove-Item -LiteralPath 'removed file.txt'
    Set-Content -LiteralPath 'unstaged file.txt' -Value 'unstaged content'
    git add -- 'tracked file.txt'
    git add -u -- 'removed file.txt'
    Set-Content -LiteralPath 'tracked file.txt' -Value 'staged then unstaged content'
    Set-Content -LiteralPath 'untracked ü file.txt' -Value 'untracked content'
    Pop-Location

    $receiptScript = Join-Path $PSScriptRoot 'write-verification-receipt.ps1'
    $firstPath = Join-Path $receiptOutputRoot 'receipt-one.json'
    & pwsh -NoProfile -File $receiptScript -RepositoryRoot $fixtureRoot -Output $firstPath -Status passed
    $first = Get-Content -LiteralPath $firstPath -Raw | ConvertFrom-Json
    Assert-True ($first.schemaVersion -eq 3) 'Receipt schema was not updated for working-tree identity.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($first.dirtyContentFingerprint)) 'Working-tree changes did not produce a fingerprint.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($first.stagedContentFingerprint)) 'Staged changes did not produce a fingerprint.'
    Assert-True ($first.stagedFiles -contains 'tracked file.txt') 'A staged path with spaces was omitted.'
    Assert-True ($first.stagedFiles -contains 'removed file.txt') 'A staged deletion was omitted.'
    Assert-True ($first.unstagedFiles -contains 'tracked file.txt') 'A mixed staged/unstaged path was omitted from the unstaged list.'
    Assert-True ($first.unstagedFiles -contains 'unstaged file.txt') 'An unstaged tracked edit was omitted.'
    Assert-True ($first.untrackedFiles -contains 'untracked ü file.txt') 'An untracked Unicode path was omitted.'
    Assert-True ($first.dirtyFiles -contains 'removed file.txt') 'A staged deletion was omitted from dirty files.'

    $repeatPath = Join-Path $receiptOutputRoot 'receipt-repeat.json'
    & pwsh -NoProfile -File $receiptScript -RepositoryRoot $fixtureRoot -Output $repeatPath -Status passed
    $repeat = Get-Content -LiteralPath $repeatPath -Raw | ConvertFrom-Json
    Assert-True ($first.dirtyContentFingerprint -eq $repeat.dirtyContentFingerprint) 'An unchanged working tree produced a different fingerprint.'

    Set-Content -LiteralPath (Join-Path $fixtureRoot 'unstaged file.txt') -Value 'different unstaged content'
    $secondPath = Join-Path $receiptOutputRoot 'receipt-two.json'
    & pwsh -NoProfile -File $receiptScript -RepositoryRoot $fixtureRoot -Output $secondPath -Status passed
    $second = Get-Content -LiteralPath $secondPath -Raw | ConvertFrom-Json
    Assert-True ($first.stagedContentFingerprint -eq $second.stagedContentFingerprint) 'An unstaged edit changed the staged fingerprint.'
    Assert-True ($first.dirtyContentFingerprint -ne $second.dirtyContentFingerprint) 'An unstaged edit did not change the working-tree fingerprint.'

    Write-Output 'write-verification-receipt fixture tests passed (16 assertions).'
}
finally {
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove fixture outside the temporary root: $resolvedFixture" }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
    $resolvedOutput = [System.IO.Path]::GetFullPath($receiptOutputRoot)
    if (-not $resolvedOutput.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove receipt output outside the temporary root: $resolvedOutput" }
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
}
