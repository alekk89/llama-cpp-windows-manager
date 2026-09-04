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
  [string] $ExpectedPublisher = "",
  [string] $ReleaseManifestKeyId = "",
  [string] $ReleaseManifestPublicKeySpki = "",
  [string] $ReleaseManifestNextKeyId = "",
  [string] $ReleaseManifestNextPublicKeySpki = "",
  [string] $RepositoryCommit = "",
  [ValidateSet("development", "stable", "preview", "nightly")]
  [string] $ReleaseChannel = "development",
  [switch] $RequireSigned
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot

function Test-CertificatePublisher {
  param(
    [Parameter(Mandatory = $true)] $Certificate,
    [Parameter(Mandatory = $true)][string] $Publisher
  )

  $simpleName = $Certificate.GetNameInfo(
    [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false)
  return [string]::Equals($simpleName, $Publisher, [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($Certificate.Subject, $Publisher, [System.StringComparison]::OrdinalIgnoreCase)
}

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

  $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
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

function Assert-TrustedSignature {
  param(
    [Parameter(Mandatory = $true)][string] $Path,
    [Parameter(Mandatory = $true)][string] $Label
  )
  if (-not $RequireSigned) { return }
  $signature = Get-AuthenticodeSignature -FilePath $Path
  if ($signature.Status -ne "Valid") { throw "$Label is not Authenticode-valid: $Path" }
  if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
      ($null -eq $signature.SignerCertificate -or
       -not (Test-CertificatePublisher -Certificate $signature.SignerCertificate -Publisher $ExpectedPublisher))) {
    throw "$Label is not signed by expected publisher '$ExpectedPublisher': $Path"
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
  $license = Join-Path $publishDir "LICENSE"
  $thirdPartyNotices = Join-Path $publishDir "THIRD-PARTY-NOTICES.md"
  $sbom = Join-Path $publishDir "sbom.spdx.json"
  $apacheLicense = Join-Path $publishDir "licenses\Apache-2.0.txt"
  $dotnetNotices = Join-Path $publishDir "licenses\dotnet\ThirdPartyNotices.txt"
  $zipPath = Join-Path $RepoRoot "dist\LlamaCppWindowsManager-$Runtime.zip"

  if (-not (Test-Path -LiteralPath $publishDir -PathType Container)) {
    throw "Publish folder was not produced: $publishDir"
  }
  if (Test-Path -LiteralPath (Join-Path $publishDir "LlamaCppConsole.exe")) {
    throw "Publish folder contains the removed legacy executable alias."
  }

  Assert-HashCompanion -Path $appExe
  Assert-HashCompanion -Path $controlCli
  Assert-TrustedSignature -Path $appExe -Label "Published application"
  Assert-TrustedSignature -Path $controlCli -Label "Published control CLI"
  Assert-FileExists -Path $agentsGuide -Label "Agent instruction sidecar"
  Assert-FileExists -Path $quickGuide -Label "Quick agent instruction sidecar"
  Assert-FileExists -Path $controlApiGuide -Label "Control API reference sidecar"
  Assert-FileExists -Path $license -Label "Project license"
  Assert-FileExists -Path $thirdPartyNotices -Label "Third-party notices"
  Assert-FileExists -Path $sbom -Label "SPDX software bill of materials"
  Assert-FileExists -Path $apacheLicense -Label "Apache License 2.0"
  Assert-FileExists -Path $dotnetNotices -Label ".NET third-party notices"
  if (Test-Path -LiteralPath $zipPath) { throw "Obsolete portable ZIP remains in dist: $zipPath" }

  $pdbs = @(Get-ChildItem -LiteralPath $publishDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue)
  if ($pdbs.Count -ne 0) {
    throw "Publish folder contains PDB files: $($pdbs[0].FullName)"
  }

  # Verify the actual standalone download restores its embedded sidecars.
  $bootstrapRoot = Join-Path $RepoRoot ("workspace/portable-bootstrap-" + [Guid]::NewGuid().ToString("N"))
  New-Item -ItemType Directory -Path $bootstrapRoot -Force | Out-Null
  try {
    $bootstrapExe = Join-Path $bootstrapRoot "LlamaCppWindowsManager.exe"
    Copy-Item -LiteralPath $appExe -Destination $bootstrapExe
    $process = Start-Process -FilePath $bootstrapExe -ArgumentList "--bootstrap-agent-sidecars-only" -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Standalone EXE sidecar bootstrap failed: $($process.ExitCode)" }
    foreach ($relative in @("llwmctl.exe", "AGENTS.md", "agent.md", "docs/CONTROL_API.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "licenses/Apache-2.0.txt", "licenses/dotnet/LICENSE.txt", "licenses/dotnet/ThirdPartyNotices.txt")) {
      $restored = Join-Path $bootstrapRoot $relative
      Assert-FileExists -Path $restored -Label "Restored standalone sidecar"
      if ((Get-FileHash -LiteralPath $restored).Hash -ne (Get-FileHash -LiteralPath (Join-Path $publishDir $relative)).Hash) {
        throw "Standalone sidecar differs from published input: $relative"
      }
    }
  } finally {
    $resolvedBootstrap = [IO.Path]::GetFullPath($bootstrapRoot)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $RepoRoot "workspace")).TrimEnd('\') + '\'
    if (-not $resolvedBootstrap.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid bootstrap cleanup path" }
    Remove-Item -LiteralPath $resolvedBootstrap -Recurse -Force
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
  Assert-TrustedSignature -Path $installerPath -Label "Installer"
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

Invoke-GateStep "Check code shape" {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check-code-shape.ps1")
}

$buildArgs = @(
  "-NoProfile",
  "-ExecutionPolicy",
  "Bypass",
  "-File",
  (Join-Path $PSScriptRoot "build-app.ps1"),
  "-Configuration",
  $Configuration
)
if (-not $SkipRestore) {
  $buildArgs += @("-Restore", "-LockedRestore")
}

Invoke-GateStep "Build app" {
  & powershell.exe @buildArgs
}

Invoke-GateStep "Verify updater handoff without instrumentation" {
  & $dotnet test --project (Join-Path $RepoRoot "tests\LocalLlmConsole.Tests") --no-restore --filter-class '*UpdateHandoffTests'
}

Invoke-GateStep "Run tests and enforce coverage" {
  # Release binaries deliberately omit PDBs. Coverage is collected from the same
  # source in Debug while the separate build step enforces Release compilation.
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-coverage.ps1") -Configuration Debug
}

Invoke-GateStep "Verify formatting" {
  & $dotnet format (Join-Path $RepoRoot "LocalLlmConsole.sln") --verify-no-changes --verbosity minimal
}

Invoke-GateStep "Check diff whitespace" {
  & git -C $RepoRoot diff --check
}

Invoke-GateStep "Validate documentation links and commands" {
  & (Join-Path $RepoRoot "scripts\test-docs.ps1")
}

Invoke-GateStep "Audit package vulnerabilities" {
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-vulnerabilities.ps1") -Configuration $Configuration
}

if ($IncludePublish) {
  $publishArgs = @(
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    (Join-Path $PSScriptRoot "publish-app.ps1"),
    "-Runtime",
    $Runtime,
    "-Configuration",
    $Configuration
  )
  if ($RepositoryCommit) {
    $publishArgs += @("-RepositoryCommit", $RepositoryCommit)
  }
  $publishArgs += @("-ReleaseChannel", $ReleaseChannel)
  if ($CertificateThumbprint) {
    $publishArgs += @("-CertificateThumbprint", $CertificateThumbprint, "-TimestampServer", $TimestampServer)
  }
  if ($ExpectedPublisher) {
    $publishArgs += @("-ExpectedPublisher", $ExpectedPublisher)
  }
  if ($ReleaseManifestKeyId) {
    $publishArgs += @("-ReleaseManifestKeyId", $ReleaseManifestKeyId)
  }
  if ($ReleaseManifestPublicKeySpki) {
    $publishArgs += @("-ReleaseManifestPublicKeySpki", $ReleaseManifestPublicKeySpki)
  }
  if ($ReleaseManifestNextKeyId) {
    $publishArgs += @("-ReleaseManifestNextKeyId", $ReleaseManifestNextKeyId)
  }
  if ($ReleaseManifestNextPublicKeySpki) {
    $publishArgs += @("-ReleaseManifestNextPublicKeySpki", $ReleaseManifestNextPublicKeySpki)
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
    (Join-Path $PSScriptRoot "build-installer.ps1"),
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
  if ($ExpectedPublisher) {
    $installerArgs += @("-ExpectedPublisher", $ExpectedPublisher)
  }
  if ($ReleaseManifestKeyId) {
    $installerArgs += @("-ReleaseManifestKeyId", $ReleaseManifestKeyId)
  }
  if ($ReleaseManifestPublicKeySpki) {
    $installerArgs += @("-ReleaseManifestPublicKeySpki", $ReleaseManifestPublicKeySpki)
  }
  if ($ReleaseManifestNextKeyId) {
    $installerArgs += @("-ReleaseManifestNextKeyId", $ReleaseManifestNextKeyId)
  }
  if ($ReleaseManifestNextPublicKeySpki) {
    $installerArgs += @("-ReleaseManifestNextPublicKeySpki", $ReleaseManifestNextPublicKeySpki)
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

  Invoke-GateStep "Test installer install, repair, and uninstall" {
    $appVersion = Read-ProjectVersion
    $installerPath = Join-Path $RepoRoot "dist\installer\LlamaCppWindowsManager-Setup-$appVersion-$Runtime.exe"
    $smokeArgs = @("-InstallerPath", $installerPath)
    if ($RequireSigned) { $smokeArgs += "-RequireSigned" }
    if ($ExpectedPublisher) { $smokeArgs += @("-ExpectedPublisher", $ExpectedPublisher) }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-installer-smoke.ps1") @smokeArgs
  }
}

Write-Host ""
Write-Host "Release gate passed." -ForegroundColor Green
