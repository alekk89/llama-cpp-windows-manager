param(
  [Parameter(Mandatory = $true)]
  [string] $InstallerPath,
  [switch] $RequireSigned,
  [string] $ExpectedPublisher = "",
  [ValidateRange(30, 900)]
  [int] $ProcessTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$Installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
  throw "Installer was not found: $Installer"
}

$SmokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("llwm-installer-smoke-" + [Guid]::NewGuid().ToString("N"))
$InstallDir = Join-Path $SmokeRoot "Install path with spaces - δ"
$InstallLog = Join-Path $SmokeRoot "install.log"
$RepairLog = Join-Path $SmokeRoot "repair.log"
$UninstallLog = Join-Path $SmokeRoot "uninstall.log"
$SmokeRootFull = [System.IO.Path]::GetFullPath($SmokeRoot).TrimEnd('\') + '\'
$InstallerRegistrationName = "{5C6D440C-0EE0-4FEC-8D86-6AADEAA24620}_is1"
$InstallerRegistrationPaths = @(
  "Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName",
  "Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName",
  "Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$InstallerRegistrationName"
)
$ProductionShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\llama.cpp Windows Manager\llama.cpp Windows Manager.lnk"

function Assert-NoExistingInstallerIdentity {
  foreach ($registrationPath in $InstallerRegistrationPaths) {
    if (Test-Path -LiteralPath $registrationPath) {
      throw "Refusing installer smoke testing because the production installer identity is already registered. Run this test in a clean disposable Windows environment: $registrationPath"
    }
  }
  if (Test-Path -LiteralPath $ProductionShortcut -PathType Leaf) {
    throw "Refusing installer smoke testing because the production Start Menu shortcut already exists. Run this test in a clean disposable Windows environment: $ProductionShortcut"
  }
}

function Test-CertificatePublisher {
  param(
    [Parameter(Mandatory = $true)] $Certificate,
    [Parameter(Mandatory = $true)][string] $Publisher
  )

  $simpleName = $Certificate.GetNameInfo(
    [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false)
  return [string]::Equals($simpleName, $Publisher, [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($Certificate.Subject, $Publisher, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-CheckedProcess {
  param(
    [Parameter(Mandatory = $true)][string] $FilePath,
    [Parameter(Mandatory = $true)][string[]] $ArgumentList,
    [Parameter(Mandatory = $true)][string] $Label
  )
  Write-Host "$Label..."
  $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
  $startInfo.Arguments = $ArgumentList -join ' '
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
  $process = [Diagnostics.Process]::Start($startInfo)
  Write-Host "$Label started as PID $($process.Id)."
  try {
    if (-not $process.WaitForExit($ProcessTimeoutSeconds * 1000)) {
      Write-Warning "$Label timed out; terminating PID $($process.Id)."
      $killInfo = [Diagnostics.ProcessStartInfo]::new('taskkill.exe', "/PID $($process.Id) /T /F")
      $killInfo.UseShellExecute = $false
      $killInfo.CreateNoWindow = $true
      $killer = [Diagnostics.Process]::Start($killInfo)
      try {
        if (-not $killer.WaitForExit(10000)) { $killer.Kill() }
      } finally { $killer.Dispose() }
      throw "$Label exceeded the $ProcessTimeoutSeconds-second timeout."
    }
    if ($process.ExitCode -ne 0) {
      throw "$Label failed with exit code $($process.ExitCode)."
    }
  } catch {
    foreach ($log in @(Get-ChildItem -LiteralPath $SmokeRoot -Filter *.log -File -ErrorAction SilentlyContinue)) {
      Write-Host "Installer diagnostic: $($log.Name)"
      Get-Content -LiteralPath $log.FullName -Tail 80
    }
    throw
  } finally {
    $process.Dispose()
  }
  Write-Host "$Label completed."
}

function Assert-InstalledFiles {
  foreach ($relative in @(
    "LlamaCppWindowsManager.exe",
    "llwmctl.exe",
    "AGENTS.md",
    "agent.md",
    "docs\CONTROL_API.md",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md",
    "sbom.spdx.json"
  )) {
    $path = Join-Path $InstallDir $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      throw "Installed application is missing $relative`: $path"
    }
  }
}

function Assert-TrustedSignature([string] $Path, [string] $Label) {
  if (-not $RequireSigned) { return }
  $signature = Get-AuthenticodeSignature -FilePath $Path
  if ($signature.Status -ne "Valid") { throw "$Label is not Authenticode-valid: $Path" }
  if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
      ($null -eq $signature.SignerCertificate -or
       -not (Test-CertificatePublisher -Certificate $signature.SignerCertificate -Publisher $ExpectedPublisher))) {
    throw "$Label is not signed by expected publisher '$ExpectedPublisher': $Path"
  }
}

try {
  Assert-NoExistingInstallerIdentity
  Assert-TrustedSignature -Path $Installer -Label "Installer"
  New-Item -ItemType Directory -Path $SmokeRoot -Force | Out-Null
  $runnerArgs = @()
  if ($env:GITHUB_ACTIONS -eq "true") {
    # A completed sidecar bootstrap can leave provjobd.exe visible to Restart
    # Manager. Preserve hosted runner processes during both install and repair.
    Write-Host "Preserving GitHub runner processes during installer smoke checks."
    $runnerArgs = @("/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/RESTARTEXITCODE=3010")
  }
  $installArgs = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/TASKS=",
    "/DIR=`"$InstallDir`"",
    "/LOG=`"$InstallLog`""
  )
  Invoke-CheckedProcess -FilePath $Installer -ArgumentList ($installArgs + $runnerArgs) -Label "Silent clean install"
  Assert-InstalledFiles
  Assert-TrustedSignature -Path (Join-Path $InstallDir "LlamaCppWindowsManager.exe") -Label "Installed application"
  Assert-TrustedSignature -Path (Join-Path $InstallDir "llwmctl.exe") -Label "Installed control CLI"

  $app = Join-Path $InstallDir "LlamaCppWindowsManager.exe"
  Invoke-CheckedProcess -FilePath $app -ArgumentList @("--bootstrap-agent-sidecars-only") -Label "Installed sidecar bootstrap"

  $dataDir = Join-Path $InstallDir "data"
  New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
  $sentinel = Join-Path $dataDir "installer-smoke-preserve.txt"
  Set-Content -LiteralPath $sentinel -Value "preserve" -Encoding ascii

  $repairArgs = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/TASKS=",
    "/DIR=`"$InstallDir`"",
    "/LOG=`"$RepairLog`""
  )
  Invoke-CheckedProcess -FilePath $Installer -ArgumentList ($repairArgs + $runnerArgs) -Label "Silent repair install"
  Assert-InstalledFiles
  Assert-TrustedSignature -Path (Join-Path $InstallDir "LlamaCppWindowsManager.exe") -Label "Repaired application"
  Assert-TrustedSignature -Path (Join-Path $InstallDir "llwmctl.exe") -Label "Repaired control CLI"
  if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf)) {
    throw "Silent repair removed application data."
  }

  $uninstaller = Join-Path $InstallDir "unins000.exe"
  if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw "Uninstaller was not created: $uninstaller"
  }
  Invoke-CheckedProcess -FilePath $uninstaller -ArgumentList @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/LOG=`"$UninstallLog`""
  ) -Label "Silent uninstall"

  if (Test-Path -LiteralPath $app -PathType Leaf) {
    throw "Silent uninstall left the application executable behind."
  }
  if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf)) {
    throw "Normal uninstall removed application data that should be preserved by default."
  }
}
finally {
  $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($SmokeRoot)
  if ($resolvedSmokeRoot.StartsWith($SmokeRootFull, [System.StringComparison]::OrdinalIgnoreCase) -or
      ($resolvedSmokeRoot + '\').Equals($SmokeRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    $cleanupUninstallFailed = $false
    $temporaryUninstaller = Join-Path $InstallDir "unins000.exe"
    if (Test-Path -LiteralPath $temporaryUninstaller -PathType Leaf) {
      try {
        Invoke-CheckedProcess -FilePath $temporaryUninstaller -ArgumentList @(
          "/VERYSILENT",
          "/SUPPRESSMSGBOXES",
          "/NORESTART"
        ) -Label "Installer smoke cleanup uninstall"
      } catch {
        $cleanupUninstallFailed = $true
        Write-Warning "The temporary installer registration may require manual cleanup: $($_.Exception.Message)"
      }
    }
    if ($cleanupUninstallFailed) {
      Write-Warning "Preserving the temporary installer test root so its uninstaller remains available: $resolvedSmokeRoot"
    } elseif (Test-Path -LiteralPath $resolvedSmokeRoot) {
      Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
  } else {
    throw "Refusing to clean installer smoke data outside its verified temporary root: $resolvedSmokeRoot"
  }
}

Write-Host "Silent installer, repair, sidecar bootstrap, and uninstall smoke checks passed." -ForegroundColor Green
