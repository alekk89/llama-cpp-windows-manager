param(
  [string] $Configuration = "Debug",
  [double] $MinimumServiceLineCoverage = 80.0,
  [double] $MinimumModelLineCoverage = 95.0
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
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
  & $Dotnet test $TestProject -c $Configuration --collect:"XPlat Code Coverage" --logger:"trx;LogFileName=tests-$testIndex.trx" --results-directory $ResultsRoot
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

$CoverageFiles = @(
  Get-ChildItem -LiteralPath $ResultsRoot -Recurse -Filter coverage.cobertura.xml -File |
    Where-Object { $_.Directory.Parent.FullName -eq $ResultsRoot }
)
if ($CoverageFiles.Count -ne $TestProjects.Count) { throw "The coverage test run did not produce one coverage report per test project." }

function Measure-LineCoverage {
  param(
    [Parameter(Mandatory = $true)] [string] $Name,
    [Parameter(Mandatory = $true)] [scriptblock] $Include
  )

  $lines = @{}
  foreach ($CoverageFile in $CoverageFiles) {
    [xml] $Coverage = Get-Content -LiteralPath $CoverageFile.FullName
    foreach ($class in $Coverage.coverage.packages.package.classes.class) {
      $file = ([string] $class.filename).Replace('\', '/')
      foreach ($sourceMarker in @("/src/LocalLlmConsole.App/", "/src/LocalLlmConsole.ControlCli/")) {
        $markerIndex = $file.IndexOf($sourceMarker, [StringComparison]::OrdinalIgnoreCase)
        if ($markerIndex -ge 0) {
          $file = $file.Substring($markerIndex + $sourceMarker.Length)
          break
        }
      }
      foreach ($projectPrefix in @("LocalLlmConsole.App/", "LocalLlmConsole.ControlCli/")) {
        if ($file.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
          $file = $file.Substring($projectPrefix.Length)
          break
        }
      }
      if (-not (& $Include $file)) { continue }
      foreach ($line in @($class.lines.line)) {
        $key = "$file|$($line.number)"
        $hits = [int] $line.hits
        if (-not $lines.ContainsKey($key) -or $hits -gt $lines[$key]) { $lines[$key] = $hits }
      }
    }
  }

  if ($lines.Count -eq 0) { throw "Coverage scope '$Name' matched no source lines." }
  $covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
  $percent = [Math]::Round(100.0 * $covered / $lines.Count, 1)
  return [pscustomobject]@{ Name = $Name; Covered = $covered; Lines = $lines.Count; Percent = $percent }
}

$Services = Measure-LineCoverage -Name "Services" -Include { param($file) $file.StartsWith("Services/", [StringComparison]::OrdinalIgnoreCase) }
$Models = Measure-LineCoverage -Name "Models + ViewModels" -Include {
  param($file)
  $file.StartsWith("Models/", [StringComparison]::OrdinalIgnoreCase) -or
    $file.StartsWith("ViewModels/", [StringComparison]::OrdinalIgnoreCase)
}

$Services, $Models | Format-Table Name, Covered, Lines, Percent -AutoSize
if ($Services.Percent -lt $MinimumServiceLineCoverage) {
  throw "Service line coverage $($Services.Percent)% is below the required $MinimumServiceLineCoverage%."
}
if ($Models.Percent -lt $MinimumModelLineCoverage) {
  throw "Model/view-model line coverage $($Models.Percent)% is below the required $MinimumModelLineCoverage%."
}

Write-Host "Coverage thresholds passed; skipped tests: 0." -ForegroundColor Green
