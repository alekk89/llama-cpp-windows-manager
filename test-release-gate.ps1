param(
  [ValidateSet("win-x64")]
  [string] $Runtime = "win-x64",
  [string] $Configuration = "Release",
  [switch] $SkipRestore,
  [switch] $RequireCleanTree,
  [switch] $IncludePublish,
  [switch] $IncludeInstaller,
  [string] $InnoSetupPath = "",
  [string] $CertificateThumbprint = "",
  [string] $TimestampServer = "https://timestamp.digicert.com",
  [switch] $RequireSigned
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition

function Resolve-Dotnet {
  $bundledDotnet = Join-Path (Split-Path -Parent $RepoRoot) ".dotnet-sdk-10\dotnet.exe"
  if ($env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET) {
    return $env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET
  }
  if ($env:LLAMA_CPP_CONSOLE_DOTNET) {
    return $env:LLAMA_CPP_CONSOLE_DOTNET
  }
  if ($env:LOCAL_LLM_CONSOLE_DOTNET) {
    return $env:LOCAL_LLM_CONSOLE_DOTNET
  }
  if (Test-Path -LiteralPath $bundledDotnet) {
    return $bundledDotnet
  }
  $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }
  return ""
}

function Invoke-GateStep {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Name,
    [Parameter(Mandatory = $true)]
    [scriptblock] $Action
  )

  Write-Host ""
  Write-Host "==> $Name" -ForegroundColor Cyan
  & $Action
  if ($LASTEXITCODE -ne 0) {
    throw "$Name failed."
  }
}

function Assert-CleanGitTree {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Path
  )

  $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue
  if (-not $git) {
    throw "Git was not found. Install Git or omit -RequireCleanTree."
  }

  & $git.Source -C $Path rev-parse --is-inside-work-tree | Out-Null
  if ($LASTEXITCODE -ne 0) {
    throw "Clean-tree check requires a Git worktree: $Path"
  }

  $status = @(& $git.Source -C $Path status --porcelain --untracked-files=all)
  if ($LASTEXITCODE -ne 0) {
    throw "git status failed while checking the release worktree."
  }
  if ($status.Count -ne 0) {
    throw "Release requires a clean Git worktree. Commit, stash, or remove changes before retrying:`n$($status -join [Environment]::NewLine)"
  }
}

function Assert-FileExists {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Path,
    [Parameter(Mandatory = $true)]
    [string] $Label
  )

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "$Label was not produced: $Path"
  }
}

function Assert-HashCompanion {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Path
  )

  Assert-FileExists -Path $Path -Label "Release artifact"
  $hashPath = "$Path.sha256"
  Assert-FileExists -Path $hashPath -Label "SHA-256 companion file"

  $expected = (Get-Content -LiteralPath $hashPath -Raw).Trim()
  if ($expected -notmatch "^(?<hash>[0-9a-fA-F]{64})\s+") {
    throw "SHA-256 companion file is malformed: $hashPath"
  }

  $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  $expectedHash = $Matches["hash"].ToLowerInvariant()
  if ($actualHash -ne $expectedHash) {
    throw "SHA-256 companion file does not match $Path"
  }
}

function Assert-ZipContainsEntry {
  param(
    [Parameter(Mandatory = $true)]
    [string[]] $Entries,
    [Parameter(Mandatory = $true)]
    [string] $ExpectedEntry,
    [Parameter(Mandatory = $true)]
    [string] $ZipPath
  )

  if (-not ($Entries -contains $ExpectedEntry)) {
    throw "Release archive is missing $ExpectedEntry`: $ZipPath"
  }
}

function Assert-PublishArtifacts {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Runtime
  )

  $publishDir = Join-Path $RepoRoot "dist\LlamaCppWindowsManager-$Runtime"
  $appExe = Join-Path $publishDir "LlamaCppWindowsManager.exe"
  $controlCli = Join-Path $publishDir "llwmctl.exe"
  $agentsGuide = Join-Path $publishDir "AGENTS.md"
  $quickGuide = Join-Path $publishDir "agent.md"
  $controlApiGuide = Join-Path $publishDir "docs\CONTROL_API.md"
  $zipPath = Join-Path $RepoRoot "dist\LlamaCppWindowsManager-$Runtime.zip"

  if (-not (Test-Path -LiteralPath $publishDir -PathType Container)) {
    throw "Publish folder was not produced: $publishDir"
  }

  Assert-HashCompanion -Path $appExe
  Assert-HashCompanion -Path $controlCli
  Assert-FileExists -Path $agentsGuide -Label "Agent instruction sidecar"
  Assert-FileExists -Path $quickGuide -Label "Quick agent instruction sidecar"
  Assert-FileExists -Path $controlApiGuide -Label "Control API reference sidecar"
  Assert-HashCompanion -Path $zipPath

  $pdbs = @(Get-ChildItem -LiteralPath $publishDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue)
  if ($pdbs.Count -ne 0) {
    throw "Publish folder contains PDB files: $($pdbs[0].FullName)"
  }

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
  try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName -replace "\\", "/" })
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "LlamaCppWindowsManager.exe" -ZipPath $zipPath
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "llwmctl.exe" -ZipPath $zipPath
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "llwmctl.exe.sha256" -ZipPath $zipPath
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "AGENTS.md" -ZipPath $zipPath
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "agent.md" -ZipPath $zipPath
    Assert-ZipContainsEntry -Entries $entries -ExpectedEntry "docs/CONTROL_API.md" -ZipPath $zipPath
    if ($entries -contains "LlamaCppConsole.exe") {
      throw "Release archive contains the removed legacy executable alias: $zipPath"
    }
    $pdbEntry = $entries | Where-Object { $_.EndsWith(".pdb", [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($pdbEntry) {
      throw "Release archive contains a PDB file: $pdbEntry"
    }
  } finally {
    $zip.Dispose()
  }
}

function Read-ProjectVersion {
  $projectPath = Join-Path $RepoRoot "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj"
  [xml] $project = Get-Content -LiteralPath $projectPath
  $version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Project version was not found in $projectPath"
  }
  return $version
}

function Assert-InstallerArtifacts {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Runtime
  )

  $appVersion = Read-ProjectVersion
  $installerPath = Join-Path $RepoRoot "dist\installer\LlamaCppWindowsManager-Setup-$appVersion-$Runtime.exe"
  Assert-HashCompanion -Path $installerPath
}

$dotnet = Resolve-Dotnet
if (-not $dotnet) {
  throw ".NET SDK was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0."
}
if (-not (Test-Path -LiteralPath $dotnet)) {
  throw "Configured dotnet path was not found: $dotnet"
}

if ($RequireCleanTree) {
  Invoke-GateStep "Verify clean Git worktree" {
    Assert-CleanGitTree -Path $RepoRoot
  }
}

$buildArgs = @(
  "-NoProfile",
  "-ExecutionPolicy",
  "Bypass",
  "-File",
  (Join-Path $RepoRoot "build-app.ps1"),
  "-Configuration",
  $Configuration
)
if (-not $SkipRestore) {
  $buildArgs += "-Restore"
}

Invoke-GateStep "Build app" {
  & powershell.exe @buildArgs
}

Invoke-GateStep "Run tests and enforce coverage" {
  # Release binaries deliberately omit PDBs. Coverage is collected from the same
  # source in Debug while the separate build step enforces Release compilation.
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "test-coverage.ps1") -Configuration Debug
}

Invoke-GateStep "Verify formatting" {
  & $dotnet format (Join-Path $RepoRoot "LocalLlmConsole.sln") --verify-no-changes --verbosity minimal
}

Invoke-GateStep "Check diff whitespace" {
  & git -C $RepoRoot diff --check
}

Invoke-GateStep "Audit package vulnerabilities" {
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "test-vulnerabilities.ps1") -Configuration $Configuration
}

if ($IncludePublish) {
  $publishArgs = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    (Join-Path $RepoRoot "publish-app.ps1"),
    "-Runtime",
    $Runtime,
    "-Configuration",
    $Configuration
  )
  if ($CertificateThumbprint) {
    $publishArgs += @("-CertificateThumbprint", $CertificateThumbprint, "-TimestampServer", $TimestampServer)
  }
  if ($RequireSigned) {
    $publishArgs += "-RequireSigned"
  }
  if ($RequireCleanTree) {
    $publishArgs += "-RequireCleanTree"
  }

  Invoke-GateStep "Publish app" {
    & powershell.exe @publishArgs
  }

  Invoke-GateStep "Verify publish artifacts" {
    Assert-PublishArtifacts -Runtime $Runtime
  }
}

if ($IncludeInstaller) {
  $installerArgs = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    (Join-Path $RepoRoot "build-installer.ps1"),
    "-Runtime",
    $Runtime,
    "-Configuration",
    $Configuration
  )
  if ($InnoSetupPath) {
    $installerArgs += @("-InnoSetupPath", $InnoSetupPath)
  }
  if ($CertificateThumbprint) {
    $installerArgs += @("-CertificateThumbprint", $CertificateThumbprint, "-TimestampServer", $TimestampServer)
  }
  if ($RequireSigned) {
    $installerArgs += "-RequireSigned"
  }
  if ($RequireCleanTree) {
    $installerArgs += "-RequireCleanTree"
  }
  if ($IncludePublish) {
    $installerArgs += "-SkipPublish"
  }

  Invoke-GateStep "Build installer" {
    & powershell.exe @installerArgs
  }

  if (-not $IncludePublish) {
    Invoke-GateStep "Verify publish artifacts" {
      Assert-PublishArtifacts -Runtime $Runtime
    }
  }

  Invoke-GateStep "Verify installer artifacts" {
    Assert-InstallerArtifacts -Runtime $Runtime
  }
}

Write-Host ""
Write-Host "Release gate passed." -ForegroundColor Green
