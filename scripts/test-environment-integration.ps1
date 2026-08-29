param(
  [ValidateSet("auto", "windows-no-gpu", "wsl-cpu", "wsl-nvidia", "intel", "amd")]
  [string] $Lane = "auto",
  [string] $OutputDirectory = "artifacts/environment-integration"
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDirectory))
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$result = [ordered]@{ schemaVersion = 1; lane = $Lane; classification = "probe-failure"; reason = "not-started"; timestampUtc = [DateTimeOffset]::UtcNow }

try {
  if (Get-Process -Name LlamaCppWindowsManager -ErrorAction SilentlyContinue) {
    throw "A Manager process is already running; the isolated environment test will not touch it."
  }
  $wslAvailable = $null -ne (Get-Command wsl.exe -ErrorAction SilentlyContinue)
  $nvidiaAvailable = $null -ne (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue)
  $intelAvailable = $null -ne (Get-Command sycl-ls.exe -ErrorAction SilentlyContinue)
  $amdAvailable = $null -ne (Get-Command rocm-smi.exe -ErrorAction SilentlyContinue)
  $wslNvidiaSmi = ""
  if ($wslAvailable -and $Lane -eq "wsl-nvidia") {
    $previousPreference = $ErrorActionPreference
    try {
      $ErrorActionPreference = "Continue"
      $probeOutput = & wsl.exe --exec sh -lc `
        'command -v nvidia-smi 2>/dev/null || { test -x /usr/lib/wsl/lib/nvidia-smi && printf %s /usr/lib/wsl/lib/nvidia-smi; }' 2>&1
      $probeExitCode = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $previousPreference
    }
    if ($probeExitCode -eq 0) {
      $wslNvidiaSmi = (@($probeOutput) -join "").Trim()
    }
  }
  $supported = switch ($Lane) {
    "windows-no-gpu" { -not ($nvidiaAvailable -or $intelAvailable -or $amdAvailable) }
    "wsl-cpu" { $wslAvailable }
    "wsl-nvidia" { $wslAvailable -and -not [string]::IsNullOrWhiteSpace($wslNvidiaSmi) }
    "intel" { $intelAvailable }
    "amd" { $amdAvailable }
    default { $true }
  }
  if (-not $supported) {
    $result.classification = "unsupported-capability"
    $result.reason = "Runner does not expose the capability required by lane '$Lane'."
    return
  }

  if ($Lane -like "wsl-*") {
    $wslOutput = & wsl.exe --status 2>&1
    if ($LASTEXITCODE -ne 0) { throw "WSL probe failed: $($wslOutput -join ' ')" }
  }
  if ($Lane -eq "wsl-nvidia") {
    $gpuOutput = & wsl.exe --exec $wslNvidiaSmi --query-gpu=name,driver_version --format=csv,noheader 2>&1
    if ($LASTEXITCODE -ne 0) { throw "WSL NVIDIA probe failed: $($gpuOutput -join ' ')" }
  }
  .\scripts\test-app.ps1
  $result.classification = "success"
  $result.reason = "Fake-runtime and requested capability probes passed."
}
catch {
  $result.classification = "probe-or-runtime-failure"
  $result.reason = $_.Exception.GetType().Name
  throw
}
finally {
  $json = $result | ConvertTo-Json -Depth 4
  [IO.File]::WriteAllText((Join-Path $OutputRoot "environment-result.json"), $json, [Text.UTF8Encoding]::new($false))
  Compress-Archive -Path (Join-Path $OutputRoot "environment-result.json") -DestinationPath (Join-Path $OutputRoot "reviewed-diagnostics.zip") -Force
}
