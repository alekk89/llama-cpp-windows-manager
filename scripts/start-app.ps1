param(
  [switch] $PublishIfMissing
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $PSScriptRoot
$PublishedExe = Join-Path $AppDir "dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe"
$BuildExe = Join-Path $AppDir "src\LocalLlmConsole.App\bin\Release\net10.0-windows\win-x64\LlamaCppWindowsManager.exe"
$PowerShellExe = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $PowerShellExe)) {
  $PowerShellExe = Join-Path $env:WINDIR "Sysnative\WindowsPowerShell\v1.0\powershell.exe"
}
if (-not (Test-Path -LiteralPath $PowerShellExe)) {
  throw "System Windows PowerShell was not found."
}

if (-not (Test-Path -LiteralPath $PublishedExe) -and $PublishIfMissing) {
  & $PowerShellExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "publish-app.ps1")
}

$Exe = if (Test-Path -LiteralPath $PublishedExe) { $PublishedExe } elseif (Test-Path -LiteralPath $BuildExe) { $BuildExe } else { "" }
if ([string]::IsNullOrWhiteSpace($Exe)) {
  throw "llama.cpp Windows Manager executable not found. Run .\scripts\publish-app.ps1 after installing the .NET 10 SDK."
}

Start-Process -FilePath $Exe -WorkingDirectory $AppDir | Out-Null
Write-Host "Started llama.cpp Windows Manager app: $Exe"
