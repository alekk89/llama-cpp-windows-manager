param()

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$violations = [Collections.Generic.List[string]]::new()
$markdownFiles = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.md -File |
  Where-Object { $_.FullName -notmatch '[\\/](bin|data|obj|dist|TestResults|workspace)[\\/]' }

[xml] $appProject = Get-Content -LiteralPath (Join-Path $RepoRoot "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj")
$currentVersion = @($appProject.Project.PropertyGroup.Version | Where-Object { $_ })[0].Trim()
$currentTag = "v$currentVersion"

foreach ($file in $markdownFiles) {
  $text = Get-Content -LiteralPath $file.FullName -Raw
  $lines = @(Get-Content -LiteralPath $file.FullName)
  $relativePath = $file.FullName.Substring($RepoRoot.Length + 1)
  $insideFence = $false
  $previousHeadingLevel = 0
  for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
    $line = $lines[$lineIndex]
    if ($line -match '[ \t]+$') {
      $violations.Add("${relativePath}:$($lineIndex + 1): trailing whitespace")
    }
    if ($line.Contains("`t")) {
      $violations.Add("${relativePath}:$($lineIndex + 1): tab character")
    }
    if ($line -match '^\s*```') {
      $insideFence = -not $insideFence
      continue
    }
    if (-not $insideFence -and $line -match '^(?<marks>#{1,6})\s+\S') {
      $headingLevel = $Matches.marks.Length
      if ($previousHeadingLevel -gt 0 -and $headingLevel -gt $previousHeadingLevel + 1) {
        $violations.Add("${relativePath}:$($lineIndex + 1): heading level jumps from $previousHeadingLevel to $headingLevel")
      }
      $previousHeadingLevel = $headingLevel
    }
  }
  if ($insideFence) {
    $violations.Add("${relativePath}: unclosed fenced code block")
  }
  foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^\)\s]+)')) {
    $target = $match.Groups['target'].Value.Trim('<', '>')
    if ($target -match '^(https?://|mailto:|#)') { continue }
    $pathPart = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
    if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathPart))
    if (-not (Test-Path -LiteralPath $resolved)) {
      $violations.Add("${relativePath}: missing link target '$target'")
    }
  }
  if ($text -match '(?m)^\s*(?:\.\\)?LlamaCppConsole\.exe\s') {
    $violations.Add("${relativePath}: stale executable command example")
  }
}

$mainWindowStatePath = "src\LocalLlmConsole.App\Ui\Shell\MainWindow\Core\MainWindow.State.cs"
$mainWindowState = Get-Content -LiteralPath (Join-Path $RepoRoot $mainWindowStatePath) -Raw
if ($mainWindowState -notmatch [regex]::Escape("private const string AppVersionLabel = `"$currentTag`";")) {
  $violations.Add("${mainWindowStatePath}: AppVersionLabel does not match project version $currentVersion")
}

$issueTemplate = Get-Content -LiteralPath (Join-Path $RepoRoot ".github\ISSUE_TEMPLATE\bug_report.yml") -Raw
if ($issueTemplate -notmatch "(?m)^\s*placeholder:\s*$([regex]::Escape($currentTag))\s*$") {
  $violations.Add(".github/ISSUE_TEMPLATE/bug_report.yml: version placeholder does not match $currentTag")
}

$releaseNotes = Join-Path $RepoRoot "docs\releases\$currentTag.md"
if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) {
  $violations.Add("docs/releases/$currentTag.md: current release notes are missing")
}

if ($violations.Count -gt 0) { throw "Documentation validation failed:`n$($violations -join "`n")" }
Write-Host "Documentation formatting, links, executable examples, and current-version surfaces are valid." -ForegroundColor Green
