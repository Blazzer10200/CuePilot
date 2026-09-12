param(
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidateSet('passed','failed')][string]$Status,
    [string[]]$Commands = @(),
    [double]$DurationSeconds = 0,
    [string]$RepositoryRoot
)
$ErrorActionPreference = 'Stop'
$repo = if ($RepositoryRoot) {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
} else {
    (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
}

function Invoke-GitBytes {
    param([string[]]$Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $repo
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stream = [System.IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($stream)
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "git $($Arguments -join ' ') failed: $stderr" }
    return ,$stream.ToArray()
}

function Get-NulDelimitedGitPaths {
    param([byte[]]$Bytes)

    if ($Bytes.Length -eq 0) { return @() }
    $text = [Text.Encoding]::UTF8.GetString($Bytes)
    return @($text.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries))
}

function Get-SortedUniquePaths {
    param([byte[]]$Bytes)

    $paths = @(Get-NulDelimitedGitPaths -Bytes $Bytes | Sort-Object -Unique)
    return $paths
}

function Add-FingerprintSection {
    param([System.IO.MemoryStream]$Stream, [string]$Name, [byte[]]$Bytes)

    $nameBytes = [Text.Encoding]::UTF8.GetBytes($Name)
    $Stream.Write([BitConverter]::GetBytes($nameBytes.Length), 0, 4)
    $Stream.Write($nameBytes, 0, $nameBytes.Length)
    $Stream.Write([BitConverter]::GetBytes($Bytes.Length), 0, 4)
    if ($Bytes.Length -gt 0) { $Stream.Write($Bytes, 0, $Bytes.Length) }
}

function Get-UntrackedFingerprintBytes {
    param([string[]]$Paths)

    $stream = [System.IO.MemoryStream]::new()
    foreach ($path in $Paths) {
        $fullPath = Join-Path $repo $path
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Untracked file disappeared while fingerprinting: $path" }
        $pathBytes = [Text.Encoding]::UTF8.GetBytes($path)
        $hashBytes = [Text.Encoding]::ASCII.GetBytes((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant())
        Add-FingerprintSection -Stream $stream -Name 'path' -Bytes $pathBytes
        Add-FingerprintSection -Stream $stream -Name 'sha256' -Bytes $hashBytes
    }
    return ,$stream.ToArray()
}

Push-Location $repo
try {
    $stagedPatch = Invoke-GitBytes -Arguments @('diff', '--cached', '--binary', '--full-index', '--no-ext-diff')
    $workingPatch = Invoke-GitBytes -Arguments @('diff', '--binary', '--full-index', '--no-ext-diff')
    $stagedFiles = @(Get-SortedUniquePaths -Bytes (Invoke-GitBytes -Arguments @('diff', '--cached', '--name-only', '-z')))
    $unstagedFiles = @(Get-SortedUniquePaths -Bytes (Invoke-GitBytes -Arguments @('diff', '--name-only', '-z')))
    $untrackedFiles = @(Get-SortedUniquePaths -Bytes (Invoke-GitBytes -Arguments @('ls-files', '--others', '--exclude-standard', '-z')))
    $dirtyFiles = @($stagedFiles + $unstagedFiles + $untrackedFiles | Sort-Object -Unique)
    $stagedFingerprint = if ($stagedPatch.Length -gt 0) {
        ([Security.Cryptography.SHA256]::HashData($stagedPatch) | ForEach-Object { $_.ToString('x2') }) -join ''
    } else { $null }
    $untrackedContent = Get-UntrackedFingerprintBytes -Paths $untrackedFiles
    $fingerprint = if ($stagedPatch.Length -gt 0 -or $workingPatch.Length -gt 0 -or $untrackedFiles.Count -gt 0) {
        $fingerprintInput = [System.IO.MemoryStream]::new()
        Add-FingerprintSection -Stream $fingerprintInput -Name 'staged' -Bytes $stagedPatch
        Add-FingerprintSection -Stream $fingerprintInput -Name 'unstaged' -Bytes $workingPatch
        Add-FingerprintSection -Stream $fingerprintInput -Name 'untracked' -Bytes $untrackedContent
        ([Security.Cryptography.SHA256]::HashData($fingerprintInput.ToArray()) | ForEach-Object { $_.ToString('x2') }) -join ''
    } else { $null }
    $versions = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'project-status.ps1') -AsJson -RepositoryRoot $repo | ConvertFrom-Json
    $receipt = [ordered]@{
        schemaVersion = 3; generatedUtc = [DateTime]::UtcNow.ToString('o'); status = $Status; durationSeconds = $DurationSeconds
        commands = $Commands; gitHead = (& git rev-parse HEAD).Trim(); dirty = $versions.dirty
        dirtyContentFingerprint = $fingerprint; dirtyFiles = $dirtyFiles
        stagedContentFingerprint = $stagedFingerprint; stagedFiles = $stagedFiles; unstagedFiles = $unstagedFiles; untrackedFiles = $untrackedFiles
        versions = $versions.versions
        note = 'This receipt applies only to the recorded staged diff, unstaged diff, and untracked-file identity.'
    }
    $parent = Split-Path -Parent $Output
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding utf8NoBOM
    Write-Host "Verification receipt: $Output"
} finally { Pop-Location }
