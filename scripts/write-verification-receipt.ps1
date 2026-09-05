param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidateSet('passed','failed')][string]$Status,
    [string[]]$Commands = @(),
    [double]$DurationSeconds = 0
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
Push-Location $repo
try {
    $files = @(& git ls-files --modified --others --exclude-standard) | Sort-Object -Unique
    $fingerprintInput = foreach ($file in $files) {
        if (Test-Path -LiteralPath $file -PathType Leaf) { "$file $((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())" }
    }
    $fingerprintText = $fingerprintInput -join "`n"
    $fingerprint = if ($fingerprintText) { ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($fingerprintText)) | ForEach-Object { $_.ToString('x2') }) -join '' } else { $null }
    $versions = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'project-status.ps1') -AsJson | ConvertFrom-Json
    $receipt = [ordered]@{
        schemaVersion = 1; generatedUtc = [DateTime]::UtcNow.ToString('o'); status = $Status; durationSeconds = $DurationSeconds
        commands = $Commands; gitHead = (& git rev-parse HEAD).Trim(); dirty = $versions.dirty
        dirtyContentFingerprint = $fingerprint; dirtyFiles = $files; versions = $versions.versions
        note = 'This receipt applies only to the listed source identity and dirty-content fingerprint.'
    }
    $parent = Split-Path -Parent $Output
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
    Write-Host "Verification receipt: $Output"
} finally { Pop-Location }
