param(
  [switch] $AllDist,
  [switch] $AllGenerated
)

$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Remove-RepoPath {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Path,
    [Parameter(Mandatory = $true)]
    [string] $Label
  )

  $full = [System.IO.Path]::GetFullPath($Path)
  $root = $RepoRoot.TrimEnd('\', '/')
  $inside = $full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
  if (-not $inside) {
    throw "Refusing to remove $Label outside the repository: $full"
  }
  if (-not (Test-Path -LiteralPath $full)) {
    return
  }

  $item = Get-Item -LiteralPath $full -Force
  if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to remove $Label because it is a symlink or junction: $full"
  }

  Remove-Item -LiteralPath $full -Recurse -Force
  Write-Host "Removed $Label`: $full"
}

$roots = @(
  (Join-Path $RepoRoot "src"),
  (Join-Path $RepoRoot "tests")
) | Where-Object { Test-Path -LiteralPath $_ }

$generatedDirs = foreach ($root in $roots) {
  Get-ChildItem -LiteralPath $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("bin", "obj", "TestResults") }
}

foreach ($dir in ($generatedDirs | Sort-Object { $_.FullName.Length } -Descending)) {
  Remove-RepoPath -Path $dir.FullName -Label "generated directory"
}

Remove-RepoPath -Path (Join-Path $RepoRoot "TestResults") -Label "root test-results folder"

if ($AllGenerated) {
  foreach ($name in @("diagnostics", "publish", "publish-pr7", "workspace")) {
    Remove-RepoPath -Path (Join-Path $RepoRoot $name) -Label "$name folder"
  }
}

$distRoot = Join-Path $RepoRoot "dist"
if ($AllDist -or $AllGenerated) {
  Remove-RepoPath -Path $distRoot -Label "dist folder"
} elseif (Test-Path -LiteralPath $distRoot) {
  Get-ChildItem -LiteralPath $distRoot -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "LocalLlmConsole-*" } |
    ForEach-Object { Remove-RepoPath -Path $_.FullName -Label "stale dist folder" }

  Get-ChildItem -LiteralPath $distRoot -Recurse -File -Filter *.pdb -Force -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-RepoPath -Path $_.FullName -Label "debug symbol" }
}

Write-Host "Repository cleanup complete."
