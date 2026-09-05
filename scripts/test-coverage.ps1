param(
  [string] $Configuration = "Debug",
  [double] $MinimumServiceLineCoverage = 80.0,
  [double] $MinimumModelLineCoverage = 95.0,
  [double] $MinimumCliLineCoverage = 80.0
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TestProjects = @(
  (Join-Path $RepoRoot "tests\LocalLlmConsole.Tests\LocalLlmConsole.Tests.csproj"),
  (Join-Path $RepoRoot "tests\LocalLlmConsole.UiTests\LocalLlmConsole.UiTests.csproj")
)
$BundledDotnet = Join-Path (Split-Path -Parent $RepoRoot) ".dotnet-sdk-10\dotnet.exe"
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
if (-not $Dotnet -or -not (Test-Path -LiteralPath $Dotnet)) {
  throw ".NET 10 SDK was not found."
}

$ResultsRoot = Join-Path $RepoRoot ("TestResults\coverage-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $ResultsRoot -Force | Out-Null
$testIndex = 0
foreach ($TestProject in $TestProjects) {
  $testIndex++
  & $Dotnet test --project $TestProject -c $Configuration `
    --results-directory $ResultsRoot `
    --minimum-expected-tests 1 `
    --fail-skips on `
    --coverage `
    --coverage-output "coverage-$testIndex.cobertura.xml" `
    --coverage-output-format cobertura `
    --report-xunit-trx `
    --report-xunit-trx-filename "tests-$testIndex.trx"
  if ($LASTEXITCODE -ne 0) { throw "Coverage test run failed for $TestProject." }
}

$TrxFiles = @(Get-ChildItem -LiteralPath $ResultsRoot -Recurse -Filter *.trx -File)
if ($TrxFiles.Count -ne $TestProjects.Count) { throw "The coverage test run did not produce one TRX result per test project." }
$Skipped = 0
foreach ($Trx in $TrxFiles) {
  [xml] $TrxDocument = Get-Content -LiteralPath $Trx.FullName
  $Counters = $TrxDocument.TestRun.ResultSummary.Counters
  if ($null -ne $Counters.notExecuted) { $Skipped += [int] $Counters.notExecuted }
}
if ($Skipped -ne 0) { throw "Skipped or not-executed tests are not allowed: $Skipped" }

$CoverageFiles = @(Get-ChildItem -LiteralPath $ResultsRoot -Filter coverage-*.cobertura.xml -File)
if ($CoverageFiles.Count -ne $TestProjects.Count) { throw "The coverage test run did not produce one coverage report per test project." }
& (Join-Path $PSScriptRoot "measure-test-coverage.ps1") -ResultsRoot $ResultsRoot `
  -MinimumServiceLineCoverage $MinimumServiceLineCoverage `
  -MinimumModelLineCoverage $MinimumModelLineCoverage `
  -MinimumCliLineCoverage $MinimumCliLineCoverage
Write-Host "Skipped tests: 0." -ForegroundColor Green
