namespace LocalLlmConsole.Services;

public sealed partial class AppUpdateService
{
    private static string UpdaterScript() => """
param(
  [int] $ParentPid,
  [string] $HandoffName,
  [string] $SourceExe,
  [string] $TargetExe,
  [string] $ObsoleteExe,
  [string] $SourceCli,
  [string] $TargetCli,
  [string] $NoticeSource,
  [string] $NoticeTarget,
  [string] $WorkingDirectory,
  [string] $ExpectedSourceSha256,
  [string] $ExpectedCliSha256,
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
  param([string] $Source, [string] $Target, [string] $ExpectedSha256)
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
    if ($ExpectedSha256 -and -not [string]::Equals($stagedHash, $ExpectedSha256.Trim(), [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Staged update SHA-256 does not match the verified release for $Target"
    }
    return [pscustomobject]@{ Target = $Target; Temporary = $temporary; Sha256 = $stagedHash; Backup = ""; HadOriginal = (Test-Path -LiteralPath $Target) }
  } catch {
    Remove-UpdateArtifact -Path $temporary
    throw
  }
}

function Commit-VerifiedStage {
  param($Stage)
  if ($null -eq $Stage) { return }
  # The staged copy waited for the Manager to exit; verify it again immediately before replacement.
  if ((Get-UpdateFileSha256 -Path $Stage.Temporary) -ne $Stage.Sha256) {
    throw "Staged update changed before replacement: $($Stage.Target)"
  }
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

function Write-UpdateFailureNotice {
  param([string] $Message)
  if (-not $NoticeTarget) { return }
  try {
    $version = ""
    $releaseName = ""
    if ($NoticeSource -and (Test-Path -LiteralPath $NoticeSource)) {
      $pending = Get-Content -LiteralPath $NoticeSource -Raw | ConvertFrom-Json
      $version = [string] $pending.Version
      $releaseName = [string] $pending.ReleaseName
    }
    $notice = [ordered]@{
      Version = $version
      ReleaseName = $releaseName
      ReleaseNotes = ""
      InstalledAt = [DateTimeOffset]::UtcNow.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
      FailureMessage = $Message
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $NoticeTarget) -Force | Out-Null
    [System.IO.File]::WriteAllText($NoticeTarget, ($notice | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
  } catch {
    Write-Warning ("Could not record the failed-update notice: {0}" -f $_.Exception.Message)
  }
}

$stages = @()
$committed = @()
$updateFailure = $null
$managerExited = $false
$handoffReady = $null
$handoffProceed = $null
$handoffFailed = $null
$replacementSucceeded = $false
try {
  if ($HandoffName) {
    $handoffReady = [System.Threading.EventWaitHandle]::OpenExisting($HandoffName + ".ready")
    $handoffProceed = [System.Threading.EventWaitHandle]::OpenExisting($HandoffName + ".proceed")
    $handoffFailed = [System.Threading.EventWaitHandle]::OpenExisting($HandoffName + ".failed")
  }
  $appStage = New-VerifiedStage -Source $SourceExe -Target $TargetExe -ExpectedSha256 $ExpectedSourceSha256
  if ($null -eq $appStage) { throw "The staged application executable is missing." }
  $stages += $appStage
  $cliStage = New-VerifiedStage -Source $SourceCli -Target $TargetCli -ExpectedSha256 $ExpectedCliSha256
  if ($SourceCli -and $null -eq $cliStage) { throw "The staged control executable is missing." }
  if ($null -ne $cliStage) { $stages += $cliStage }
  if ($handoffReady) {
    $handoffReady.Set() | Out-Null
    if (-not $handoffProceed.WaitOne(30000)) { throw "The Manager did not authorize the update handoff." }
  }
  $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
  if ($parent -and -not $parent.WaitForExit(90000)) { throw "The Manager did not exit; replacement was cancelled." }
  $managerExited = $true
  Start-Sleep -Milliseconds 500
  foreach ($stage in $stages) {
    Commit-VerifiedStage -Stage $stage
    $committed += $stage
  }
  $replacementSucceeded = $true
} catch {
  $updateFailure = $_
  if ($handoffFailed) { $handoffFailed.Set() | Out-Null }
  for ($index = $committed.Count - 1; $index -ge 0; $index--) {
    try { Restore-CommittedStage -Stage $committed[$index] }
    catch { Write-Warning ("Rollback failed; preserve backup '{0}': {1}" -f $committed[$index].Backup, $_.Exception.Message) }
  }
} finally {
  foreach ($event in @($handoffReady, $handoffProceed, $handoffFailed)) {
    if ($event) { $event.Dispose() }
  }
  foreach ($stage in $stages) {
    Remove-UpdateArtifact -Path $stage.Temporary
    if ($replacementSucceeded) { Remove-UpdateArtifact -Path $stage.Backup }
  }
}
if ($null -ne $updateFailure) {
  # A Manager that already exited must not stay closed: restart the restored app and
  # leave a notice it shows on startup. A Manager that is still running reports the error itself.
  if ($managerExited) {
    Write-UpdateFailureNotice -Message $updateFailure.Exception.Message
    if (-not $SkipRestart -and (Test-Path -LiteralPath $TargetExe)) {
      try {
        Start-Process -FilePath $TargetExe -WorkingDirectory $WorkingDirectory | Out-Null
        Write-Output "Restarted the previous application after the failed update."
      } catch {
        Write-Warning ("Could not restart the previous application: {0}" -f $_.Exception.Message)
      }
    }
  }
  throw $updateFailure
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
