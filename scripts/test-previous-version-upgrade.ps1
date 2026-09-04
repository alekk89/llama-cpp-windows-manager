param(
  [Parameter(Mandatory = $true)]
  [string] $CandidateInstallerPath,
  [string] $BaselinePath = "tests/release-baselines/v2.6.0.json"
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$Candidate = [System.IO.Path]::GetFullPath($CandidateInstallerPath)
$BaselineFile = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $BaselinePath))
if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) { throw "Candidate installer was not found: $Candidate" }
if (-not (Test-Path -LiteralPath $BaselineFile -PathType Leaf)) { throw "Release baseline was not found: $BaselineFile" }
$InstallerRegistrationName = "{5C6D440C-0EE0-4FEC-8D86-6AADEAA24620}_is1"
$InstallerRegistrationPaths = @(
  "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName",
  "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName",
  "Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName"
)
$ProductionShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\llama.cpp Windows Manager\llama.cpp Windows Manager.lnk"
foreach ($registrationPath in $InstallerRegistrationPaths) {
  if (Test-Path -LiteralPath $registrationPath) {
    throw "Refusing the upgrade test because the production installer identity is already registered. Run this test in a clean disposable Windows environment: $registrationPath"
  }
}
if (Test-Path -LiteralPath $ProductionShortcut -PathType Leaf) {
  throw "Refusing the upgrade test because the production Start Menu shortcut already exists. Run this test in a clean disposable Windows environment: $ProductionShortcut"
}
if (Get-Process -Name LlamaCppWindowsManager -ErrorAction SilentlyContinue) {
  throw "Refusing the isolated upgrade test while another Manager process is running."
}

$Baseline = Get-Content -LiteralPath $BaselineFile -Raw | ConvertFrom-Json
$TestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("llwm-upgrade-" + [Guid]::NewGuid().ToString("N"))
$InstallDir = Join-Path $TestRoot "application"
$Workspace = Join-Path $TestRoot "workspace"
$ExternalData = Join-Path $TestRoot "user-owned-external"
$PreviousInstaller = Join-Path $TestRoot $Baseline.installer.name
$ExpectedRoot = [System.IO.Path]::GetFullPath($TestRoot).TrimEnd('\') + '\'
$oldWorkspaceVariable = $env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE

function Invoke-CheckedProcess {
  param([string] $FilePath, [string[]] $ArgumentList, [string] $Label, [string] $LogPath = "")
  $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
  $startInfo.Arguments = $ArgumentList -join ' '
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
  $process = [Diagnostics.Process]::Start($startInfo)
  Write-Host "$Label started as PID $($process.Id)."
  $watch = [Diagnostics.Stopwatch]::StartNew()
  $logLineCount = 0
  $nextHeartbeat = 15
  try {
    do {
      $exited = $process.WaitForExit(250)
      if ($LogPath -and (Test-Path -LiteralPath $LogPath)) {
        $lines = @(Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue)
        for ($line = $logLineCount; $line -lt $lines.Count; $line++) { Write-Host $lines[$line] }
        $logLineCount = $lines.Count
      }
      if (-not $exited -and $watch.Elapsed.TotalSeconds -ge $nextHeartbeat) {
        Write-Host "$Label is still running after $([int]$watch.Elapsed.TotalSeconds) seconds (PID $($process.Id))."
        $nextHeartbeat += 15
      }
    } while (-not $exited -and $watch.Elapsed.TotalSeconds -lt 120)
    if (-not $exited) {
      $killInfo = [Diagnostics.ProcessStartInfo]::new('taskkill.exe', "/PID $($process.Id) /T /F")
      $killInfo.UseShellExecute = $false
      $killInfo.CreateNoWindow = $true
      $killer = [Diagnostics.Process]::Start($killInfo)
      try {
        if (-not $killer.WaitForExit(10000)) { $killer.Kill() }
      } finally { $killer.Dispose() }
      throw "$Label exceeded the 120-second timeout."
    }
    if ($process.ExitCode -ne 0) { throw "$Label failed with exit code $($process.ExitCode)." }
    Write-Host "$Label completed."
  } finally {
    $process.Dispose()
  }
}

function Start-IsolatedManager([string] $FilePath) {
  Write-Host "Starting isolated Manager: $FilePath"
  $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
  $startInfo.WorkingDirectory = $InstallDir
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
  $process = [Diagnostics.Process]::Start($startInfo)
  Write-Host "Isolated Manager started as PID $($process.Id)."
  return $process
}

function Invoke-Ctl {
  param([string] $Cli, [string[]] $Arguments, [string] $Label)
  $previousPreference = $ErrorActionPreference
  try {
    $ErrorActionPreference = "Continue"
    $output = & $Cli @Arguments 2>&1
    $exitCode = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $previousPreference
  }
  if ($exitCode -ne 0) { throw "$Label failed: $($output -join [Environment]::NewLine)" }
  return $output -join [Environment]::NewLine
}

function Wait-ForStatus([string] $Cli) {
  for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $previousPreference = $ErrorActionPreference
    try {
      $ErrorActionPreference = "Continue"
      $output = & $Cli status --workspace $Workspace 2>&1
      $exitCode = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -eq 0) {
      Write-Host 'Isolated Manager control API is ready.'
      return $output -join [Environment]::NewLine
    }
    Start-Sleep -Milliseconds 500
  }
  throw "The isolated Manager control API did not become ready."
}

function Invoke-FirstContact([string] $Cli, [int] $ProcessId, [string] $Label) {
  Invoke-Ctl $Cli @("capabilities", "--workspace", $Workspace) "$Label capabilities" | Out-Null
  Invoke-Ctl $Cli @("operations", "list", "--workspace", $Workspace) "$Label operation inventory" | Out-Null
  Invoke-Ctl $Cli @("self", "--process-id", $ProcessId.ToString(), "--workspace", $Workspace) "$Label identity check" | Out-Null
  Invoke-Ctl $Cli @("sessions", "list", "--workspace", $Workspace) "$Label session inventory" | Out-Null
}

function Assert-HardwareCardSetting([string] $Cli, [bool] $Expected, [string] $Label) {
  $settings = Invoke-Ctl $Cli @("settings", "get", "--workspace", $Workspace) "$Label settings read"
  $expectedJson = if ($Expected) { "true" } else { "false" }
  if ($settings -notmatch ('"showOverviewHardware"\s*:\s*' + $expectedJson)) {
    throw "$Label did not report showOverviewHardware=$expectedJson."
  }
}

function Stop-IsolatedManagers {
  $processes = @(Get-Process -Name LlamaCppWindowsManager -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($ExpectedRoot, [StringComparison]::OrdinalIgnoreCase) })
  foreach ($process in $processes) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  }
  foreach ($process in $processes) {
    try { $process.WaitForExit(15000) | Out-Null } catch { }
  }
}

function Remove-TestRootWithRetry([string] $Path) {
  for ($attempt = 0; $attempt -lt 20; $attempt++) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
      Remove-Item -LiteralPath $Path -Recurse -Force
      return
    } catch {
      if ($attempt -ge 19) { throw }
      Start-Sleep -Milliseconds 250
    }
  }
  throw "Could not remove the isolated upgrade-test root after waiting for process and scanner locks: $Path"
}

try {
  New-Item -ItemType Directory -Path $TestRoot, $Workspace, $ExternalData -Force | Out-Null
  Invoke-WebRequest -Uri $Baseline.installer.url -OutFile $PreviousInstaller -UseBasicParsing
  $actualSize = (Get-Item -LiteralPath $PreviousInstaller).Length
  if ($actualSize -ne [long]$Baseline.installer.size) { throw "Pinned previous installer size mismatch." }
  $actualHash = (Get-FileHash -LiteralPath $PreviousInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $Baseline.installer.sha256) { throw "Pinned previous installer hash mismatch." }

  $installLog = Join-Path $TestRoot "installer.log"
  $installArgs = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/TASKS=", "/DIR=`"$InstallDir`"", "/LOG=`"$installLog`"", "/LOGCLOSEAPPLICATIONS")
  Invoke-CheckedProcess $PreviousInstaller $installArgs "Previous-version install" $installLog
  $previousApp = Join-Path $InstallDir "LlamaCppWindowsManager.exe"
  $previousCli = Join-Path $InstallDir "llwmctl.exe"
  $env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE = $Workspace
  $oldProcess = Start-IsolatedManager $previousApp
  $oldStatus = Wait-ForStatus $previousCli
  Invoke-FirstContact $previousCli $oldProcess.Id "Previous Manager"
  $previousVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($previousApp).FileVersion
  if ($previousVersion -notlike "$($Baseline.version)*") { throw "Previous executable has unexpected version '$previousVersion'." }
  Invoke-Ctl $previousCli @("settings", "set", "--set", "showOverviewHardware=false", "--workspace", $Workspace) "Previous Manager settings seed" | Out-Null
  Assert-HardwareCardSetting $previousCli $false "Previous Manager"
  Invoke-Ctl $previousCli @("operations", "run", "app.shutdown", "--confirm", "--allow-self-stop", "--process-id", $oldProcess.Id.ToString(), "--workspace", $Workspace) "Previous Manager shutdown" | Out-Null
  if (-not $oldProcess.WaitForExit(15000)) { throw "Previous Manager did not complete its verified shutdown." }

  Set-Content -LiteralPath (Join-Path $Workspace "upgrade-preserve.canary") -Value "preserve" -Encoding ascii
  Set-Content -LiteralPath (Join-Path $ExternalData "external-preserve.canary") -Value "external" -Encoding ascii
  Invoke-CheckedProcess $Candidate $installArgs "Candidate upgrade install" $installLog
  if (-not (Test-Path -LiteralPath (Join-Path $Workspace "upgrade-preserve.canary"))) { throw "Upgrade removed workspace state." }
  if (-not (Test-Path -LiteralPath (Join-Path $ExternalData "external-preserve.canary"))) { throw "Upgrade removed external user data." }

  $candidateApp = Join-Path $InstallDir "LlamaCppWindowsManager.exe"
  $candidateCli = Join-Path $InstallDir "llwmctl.exe"
  $candidateProcess = Start-IsolatedManager $candidateApp
  $candidateStatus = Wait-ForStatus $candidateCli
  Invoke-FirstContact $candidateCli $candidateProcess.Id "Candidate Manager"
  $candidateVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidateApp).FileVersion
  if ($candidateVersion -like "$($Baseline.version)*") { throw "Candidate still has previous version '$candidateVersion'." }
  Assert-HardwareCardSetting $candidateCli $false "Candidate Manager after upgrade"
  Invoke-Ctl $candidateCli @("operations", "run", "app.shutdown", "--confirm", "--allow-self-stop", "--process-id", $candidateProcess.Id.ToString(), "--workspace", $Workspace) "Candidate Manager shutdown" | Out-Null
  if (-not $candidateProcess.WaitForExit(15000)) { throw "Candidate Manager did not complete its verified shutdown." }

  $candidateProcess = Start-IsolatedManager $candidateApp
  $candidateStatus = Wait-ForStatus $candidateCli
  Invoke-FirstContact $candidateCli $candidateProcess.Id "Restarted candidate Manager"
  Assert-HardwareCardSetting $candidateCli $false "Restarted candidate Manager"
  Invoke-Ctl $candidateCli @("operations", "run", "app.shutdown", "--confirm", "--allow-self-stop", "--process-id", $candidateProcess.Id.ToString(), "--workspace", $Workspace) "Restarted candidate Manager shutdown" | Out-Null
  if (-not $candidateProcess.WaitForExit(15000)) { throw "Restarted candidate Manager did not complete its verified shutdown." }

  $uninstaller = Join-Path $InstallDir "unins000.exe"
  Invoke-CheckedProcess $uninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") "Candidate uninstall"
  if (-not (Test-Path -LiteralPath (Join-Path $ExternalData "external-preserve.canary"))) { throw "Uninstall removed external user data." }
  Write-Host "Pinned $($Baseline.tag) to candidate installer upgrade validation passed." -ForegroundColor Green
}
finally {
  $env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE = $oldWorkspaceVariable
  Stop-IsolatedManagers
  $cleanupUninstallFailed = $false
  $temporaryUninstaller = Join-Path $InstallDir "unins000.exe"
  if (Test-Path -LiteralPath $temporaryUninstaller -PathType Leaf) {
    try {
      Invoke-CheckedProcess $temporaryUninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") "Upgrade-test cleanup uninstall"
    } catch {
      $cleanupUninstallFailed = $true
      Write-Warning "The temporary installer registration may require manual cleanup: $($_.Exception.Message)"
    }
  }
  $resolved = [System.IO.Path]::GetFullPath($TestRoot)
  if ($cleanupUninstallFailed) {
    Write-Warning "Preserving the temporary upgrade-test root so its uninstaller remains available: $resolved"
  } elseif (($resolved + '\').StartsWith($ExpectedRoot, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
    Remove-TestRootWithRetry $resolved
  }
}
