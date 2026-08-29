namespace LocalLlmConsole.Services;

public sealed partial class AppUpdateService
{
    private static string UpdaterScript() => """
param(
  [int] $ParentPid,
  [string] $SourceExe,
  [string] $TargetExe,
  [string] $ObsoleteExe,
  [string] $SourceCli,
  [string] $TargetCli,
  [string] $NoticeSource,
  [string] $NoticeTarget,
  [string] $WorkingDirectory,
  [switch] $SkipRestart
)
$ErrorActionPreference = "Stop"

function Remove-UpdateArtifact {
  param([string] $Path)
  if (-not $Path) { return }
  for ($attempt = 0; $attempt -lt 50; $attempt++) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
      [System.IO.File]::SetAttributes($Path, [System.IO.FileAttributes]::Normal)
      [System.IO.File]::Delete($Path)
      if (-not (Test-Path -LiteralPath $Path)) { return }
    } catch {
      if ($attempt -eq 49) {
        Write-Warning ("Could not remove update artifact '{0}': {1}" -f $Path, $_.Exception.Message)
      }
    }
    if ($attempt -lt 49) { Start-Sleep -Milliseconds 100 }
  }
}

function Get-UpdateFileSha256 {
  param([string] $Path)
  $stream = [System.IO.File]::OpenRead($Path)
  try {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
      return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace("-", "")
    } finally {
      $sha256.Dispose()
    }
  } finally {
    $stream.Dispose()
  }
}

function New-VerifiedStage {
  param([string] $Source, [string] $Target)
  if (-not $Source -or -not $Target -or -not (Test-Path -LiteralPath $Source -PathType Leaf)) { return $null }
  $targetDirectory = Split-Path -Parent $Target
  New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
  $temporary = Join-Path $targetDirectory ("." + (Split-Path -Leaf $Target) + "." + [Guid]::NewGuid().ToString("N") + ".new")
  try {
    Copy-Item -LiteralPath $Source -Destination $temporary
    $sourceHash = Get-UpdateFileSha256 -Path $Source
    $stagedHash = Get-UpdateFileSha256 -Path $temporary
    if ($sourceHash -ne $stagedHash) {
      throw "Staged update verification failed for $Target"
    }
    return [pscustomobject]@{ Target = $Target; Temporary = $temporary; Backup = ""; HadOriginal = (Test-Path -LiteralPath $Target) }
  } catch {
    Remove-UpdateArtifact -Path $temporary
    throw
  }
}

function Commit-VerifiedStage {
  param($Stage)
  if ($null -eq $Stage) { return }
  if ($Stage.HadOriginal) {
    $Stage.Backup = Join-Path (Split-Path -Parent $Stage.Target) ("." + (Split-Path -Leaf $Stage.Target) + "." + [Guid]::NewGuid().ToString("N") + ".bak")
    [System.IO.File]::Replace($Stage.Temporary, $Stage.Target, $Stage.Backup, $true)
  } else {
    [System.IO.File]::Move($Stage.Temporary, $Stage.Target)
  }
}

function Restore-CommittedStage {
  param($Stage)
  if ($null -eq $Stage) { return }
  if ($Stage.HadOriginal -and $Stage.Backup -and (Test-Path -LiteralPath $Stage.Backup)) {
    if (Test-Path -LiteralPath $Stage.Target) {
      $discard = $Stage.Target + "." + [Guid]::NewGuid().ToString("N") + ".rollback"
      [System.IO.File]::Replace($Stage.Backup, $Stage.Target, $discard, $true)
      Remove-UpdateArtifact -Path $discard
    } else {
      [System.IO.File]::Move($Stage.Backup, $Stage.Target)
    }
  } elseif (-not $Stage.HadOriginal -and (Test-Path -LiteralPath $Stage.Target)) {
    Remove-Item -LiteralPath $Stage.Target -Force
  }
}

try { Wait-Process -Id $ParentPid -Timeout 90 } catch {}
Start-Sleep -Milliseconds 500
$stages = @()
$committed = @()
try {
  $appStage = New-VerifiedStage -Source $SourceExe -Target $TargetExe
  if ($null -eq $appStage) { throw "The staged application executable is missing." }
  $stages += $appStage
  $cliStage = New-VerifiedStage -Source $SourceCli -Target $TargetCli
  if ($null -ne $cliStage) { $stages += $cliStage }
  foreach ($stage in $stages) {
    Commit-VerifiedStage -Stage $stage
    $committed += $stage
  }
} catch {
  for ($index = $committed.Count - 1; $index -ge 0; $index--) {
    try { Restore-CommittedStage -Stage $committed[$index] } catch {}
  }
  throw
} finally {
  foreach ($stage in $stages) {
    Remove-UpdateArtifact -Path $stage.Temporary
    Remove-UpdateArtifact -Path $stage.Backup
  }
}
try {
  if ($ObsoleteExe -and
      -not [string]::Equals($ObsoleteExe, $TargetExe, [System.StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $ObsoleteExe)) {
    try { Remove-Item -LiteralPath $ObsoleteExe -Force }
    catch { Write-Warning ("Could not remove obsolete executable '{0}': {1}" -f $ObsoleteExe, $_.Exception.Message) }
  }
  if (Test-Path -LiteralPath $NoticeSource) {
    try {
      New-Item -ItemType Directory -Path (Split-Path -Parent $NoticeTarget) -Force | Out-Null
      Copy-Item -LiteralPath $NoticeSource -Destination $NoticeTarget -Force
    } catch {
      Write-Warning ("Could not publish the installed-update notice: {0}" -f $_.Exception.Message)
    }
  }
} finally {
  if (-not $SkipRestart) {
    Start-Process -FilePath $TargetExe -WorkingDirectory $WorkingDirectory | Out-Null
  }
}
""";
}
