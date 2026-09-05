param(
  [Parameter(Mandatory = $true)] [string] $ResultsRoot,
  [double] $MinimumServiceLineCoverage = 80.0,
  [double] $MinimumModelLineCoverage = 95.0,
  [double] $MinimumCliLineCoverage = 80.0
)

$ErrorActionPreference = "Stop"
$CoverageFiles = @(
  Get-ChildItem -LiteralPath $ResultsRoot -Filter coverage-*.cobertura.xml -File
)
if ($CoverageFiles.Count -eq 0) { throw "No coverage reports were found in $ResultsRoot." }

# Parse each report once and retain project identity when merging overlapping coverage.
$FileCoverage = @{}
foreach ($CoverageFile in $CoverageFiles) {
  [xml] $Coverage = Get-Content -LiteralPath $CoverageFile.FullName
  foreach ($class in $Coverage.coverage.packages.package.classes.class) {
    $source = ([string] $class.filename).Replace('\', '/')
    if ($source -notmatch '(?:^|/)(LocalLlmConsole\.(?:App|Core|ControlCli))/(.+)$') { continue }
    $project = $Matches[1]
    $file = $Matches[2]
    if ($file -match '^(?:obj|bin)/' -or $file -match '\.g(?:\.i)?\.cs$') { continue }
    $key = "$project/$file"
    if (-not $FileCoverage.ContainsKey($key)) {
      $FileCoverage[$key] = [pscustomobject]@{ Project = $project; File = $file; Hits = @{} }
    }
    $hits = $FileCoverage[$key].Hits
    foreach ($line in @($class.lines.line)) {
      $number = [string] $line.number
      $count = [int] $line.hits
      if (-not $hits.ContainsKey($number) -or $count -gt $hits[$number]) { $hits[$number] = $count }
    }
  }
}

$FileSummary = @(
  foreach ($entry in $FileCoverage.Values) {
    $total = $entry.Hits.Count
    if ($total -eq 0) { continue }
    $covered = @($entry.Hits.Values | Where-Object { $_ -gt 0 }).Count
    [pscustomobject]@{
      Project = $entry.Project; File = $entry.File; Covered = $covered; Lines = $total
      Missed = $total - $covered; Percent = [Math]::Round(100.0 * $covered / $total, 1)
    }
  }
)
$FileSummary | Sort-Object Project, File | Export-Csv -LiteralPath (Join-Path $ResultsRoot "coverage-by-file.csv") -NoTypeInformation

function Measure-LineCoverage {
  param(
    [Parameter(Mandatory = $true)] [string] $Name,
    [Parameter(Mandatory = $true)] [scriptblock] $Include
  )
  $matched = @($FileSummary | Where-Object { & $Include $_.File $_.Project })
  $total = ($matched | Measure-Object Lines -Sum).Sum
  if (-not $total) { throw "Coverage scope '$Name' matched no source lines." }
  $covered = ($matched | Measure-Object Covered -Sum).Sum
  return [pscustomobject]@{ Name = $Name; Covered = $covered; Lines = $total; Percent = [Math]::Round(100.0 * $covered / $total, 1) }
}

$Services = Measure-LineCoverage -Name "Services" -Include { param($file) $file.StartsWith("Services/", [StringComparison]::OrdinalIgnoreCase) }
$Models = Measure-LineCoverage -Name "Models + ViewModels" -Include {
  param($file)
  $file.StartsWith("Models/", [StringComparison]::OrdinalIgnoreCase) -or
    $file.StartsWith("ViewModels/", [StringComparison]::OrdinalIgnoreCase)
}

$Cli = Measure-LineCoverage -Name "Control CLI" -Include { param($file, $project) $project -eq "LocalLlmConsole.ControlCli" }

$Services, $Models, $Cli | Format-Table Name, Covered, Lines, Percent -AutoSize
if ($Services.Percent -lt $MinimumServiceLineCoverage) {
  throw "Service line coverage $($Services.Percent)% is below the required $MinimumServiceLineCoverage%."
}
if ($Models.Percent -lt $MinimumModelLineCoverage) {
  throw "Model/view-model line coverage $($Models.Percent)% is below the required $MinimumModelLineCoverage%."
}

if ($Cli.Percent -lt $MinimumCliLineCoverage) {
  throw "Control CLI line coverage $($Cli.Percent)% is below the required $MinimumCliLineCoverage%."
}

Write-Host "Per-file coverage: $(Join-Path $ResultsRoot 'coverage-by-file.csv')"
Write-Host "Coverage thresholds passed." -ForegroundColor Green
