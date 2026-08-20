param(
  [Parameter(Mandatory = $true)]
  [string] $InstallerPath
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

function Invoke-CheckedProcess {
  param(
    [Parameter(Mandatory = $true)][string] $FilePath,
    [Parameter(Mandatory = $true)][string[]] $ArgumentList,
    [Parameter(Mandatory = $true)][string] $Label
  )
  $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru -WindowStyle Hidden
  if ($process.ExitCode -ne 0) {
    throw "$Label failed with exit code $($process.ExitCode)."
  }
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

try {
  New-Item -ItemType Directory -Path $SmokeRoot -Force | Out-Null
  $installArgs = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/TASKS=",
    "/DIR=`"$InstallDir`"",
    "/LOG=`"$InstallLog`""
  )
  Invoke-CheckedProcess -FilePath $Installer -ArgumentList $installArgs -Label "Silent clean install"
  Assert-InstalledFiles

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
  Invoke-CheckedProcess -FilePath $Installer -ArgumentList $repairArgs -Label "Silent repair install"
  Assert-InstalledFiles
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
    if (Test-Path -LiteralPath $resolvedSmokeRoot) {
      Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
  } else {
    throw "Refusing to clean installer smoke data outside its verified temporary root: $resolvedSmokeRoot"
  }
}

Write-Host "Silent installer, repair, sidecar bootstrap, and uninstall smoke checks passed." -ForegroundColor Green
