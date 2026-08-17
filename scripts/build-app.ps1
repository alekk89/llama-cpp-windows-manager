param(
  [switch] $Restore,
  [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $AppDir "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj"
$CliProject = Join-Path $AppDir "src\LocalLlmConsole.ControlCli\LocalLlmConsole.ControlCli.csproj"
$BundledDotnet = Join-Path (Split-Path -Parent $AppDir) ".dotnet-sdk-10\dotnet.exe"
$Dotnet = if ($env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET) {
  $env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET
} elseif ($env:LLAMA_CPP_CONSOLE_DOTNET) {
  $env:LLAMA_CPP_CONSOLE_DOTNET
} elseif ($env:LOCAL_LLM_CONSOLE_DOTNET) {
  $env:LOCAL_LLM_CONSOLE_DOTNET
} elseif (Test-Path -LiteralPath $BundledDotnet) {
  $BundledDotnet
} else {
  (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue).Source
}
if (-not $Dotnet) {
  throw ".NET SDK was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0."
}
if (-not (Test-Path -LiteralPath $Dotnet)) {
  throw "Configured dotnet path was not found: $Dotnet"
}

$Info = & $Dotnet --info
if ($Info -match "No SDKs were found") {
  throw ".NET runtime is installed, but no SDK was found. Install the .NET 10 SDK to build the WPF app."
}
if (-not (Test-Path -LiteralPath $Project)) {
  throw "WPF project not found: $Project"
}
if (-not (Test-Path -LiteralPath $CliProject)) {
  throw "Control CLI project not found: $CliProject"
}

if ($Restore) {
  $restoreArgs = @("restore", (Join-Path $AppDir "LocalLlmConsole.sln"))
  & $Dotnet @restoreArgs
  if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
}

$buildArgs = @("build", $Project, "-c", $Configuration, "--no-restore")
& $Dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
$cliBuildArgs = @("build", $CliProject, "-c", $Configuration, "--no-restore")
& $Dotnet @cliBuildArgs
if ($LASTEXITCODE -ne 0) { throw "llwmctl build failed." }

Write-Host "Built llama.cpp Windows Manager app and llwmctl." -ForegroundColor Green
