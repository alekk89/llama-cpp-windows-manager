param(
  [string] $Configuration = "Release",
  [string] $Filter = ""
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$TestProject = Join-Path $AppDir "tests\LocalLlmConsole.Tests\LocalLlmConsole.Tests.csproj"
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
  throw ".NET runtime is installed, but no SDK was found. Install the .NET 10 SDK to test the WPF app."
}
if (-not (Test-Path -LiteralPath $TestProject)) {
  throw "Test project not found: $TestProject"
}

$args = @("test", $TestProject, "-c", $Configuration)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
  $args += @("--filter", $Filter)
}

& $Dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
