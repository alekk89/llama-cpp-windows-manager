param(
  [ValidateSet("win-x64")]
  [string] $Runtime = "win-x64",
  [string] $Configuration = "Release",
  [string] $InnoSetupPath = "",
  [string] $CertificateThumbprint = "",
  [string] $TimestampServer = "https://timestamp.digicert.com",
  [string] $ExpectedPublisher = "",
  [string] $ReleaseManifestKeyId = "",
  [string] $ReleaseManifestPublicKeySpki = "",
  [string] $ReleaseManifestNextKeyId = "",
  [string] $ReleaseManifestNextPublicKeySpki = "",
  [switch] $RequireSigned,
  [switch] $RequireCleanTree,
  [switch] $SkipPublish
)

$ErrorActionPreference = "Stop"

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

function Resolve-InnoSetupCompiler([string] $ConfiguredPath) {
  $candidates = @()
  if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
    $candidates += $ConfiguredPath
  }
  if (-not [string]::IsNullOrWhiteSpace($env:LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP)) {
    $candidates += $env:LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP
  }
  if (-not [string]::IsNullOrWhiteSpace($env:LLAMA_CPP_CONSOLE_INNO_SETUP)) {
    $candidates += $env:LLAMA_CPP_CONSOLE_INNO_SETUP
  }

  $pathCommand = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
  if ($pathCommand) {
    $candidates += $pathCommand.Source
  }

  $programFilesX86 = ${env:ProgramFiles(x86)}
  if ($programFilesX86) {
    $candidates += (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe")
  }
  if ($env:ProgramFiles) {
    $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
  }
  if ($env:LOCALAPPDATA) {
    $candidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
  }

  foreach ($candidate in $candidates) {
    if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
      return (Get-Item -LiteralPath $candidate).FullName
    }
  }

  throw "Inno Setup 6 compiler was not found. Install Inno Setup 6 or pass -InnoSetupPath / set LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP to ISCC.exe."
}

function Read-ProjectVersion([string] $ProjectPath) {
  [xml] $project = Get-Content -LiteralPath $ProjectPath
  $version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Project version was not found in $ProjectPath"
  }
  return $version
}

function Sign-FileIfRequested([string] $PathToSign, [string] $Thumbprint, [string] $Timestamp, [bool] $RequireValidSignature, [string] $Publisher) {
  if ($Thumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
      Where-Object { $_.Thumbprint -replace '\s', '' -ieq ($Thumbprint -replace '\s', '') } |
      Select-Object -First 1
    if (-not $cert) {
      throw "Code-signing certificate was not found in CurrentUser or LocalMachine certificate stores: $Thumbprint"
    }
    if (-not [string]::IsNullOrWhiteSpace($Publisher) -and
        -not (Test-CertificatePublisher -Certificate $cert -Publisher $Publisher)) {
      throw "Code-signing certificate subject '$($cert.Subject)' does not match expected publisher '$Publisher'."
    }

    $signature = Set-AuthenticodeSignature -FilePath $PathToSign -Certificate $cert -TimestampServer $Timestamp
    if ($signature.Status -ne "Valid") {
      throw "Code signing failed for $PathToSign`: $($signature.Status) $($signature.StatusMessage)"
    }
  }

  $publishedSignature = Get-AuthenticodeSignature -FilePath $PathToSign
  if ($RequireValidSignature -and $publishedSignature.Status -ne "Valid") {
    throw "Installer artifact is not signed: $PathToSign"
  }
  if ($RequireValidSignature -and -not [string]::IsNullOrWhiteSpace($Publisher) -and
      ($null -eq $publishedSignature.SignerCertificate -or
       -not (Test-CertificatePublisher -Certificate $publishedSignature.SignerCertificate -Publisher $Publisher))) {
    throw "Installer artifact is not signed by expected publisher '$Publisher': $PathToSign"
  }
  if ($publishedSignature.Status -ne "Valid") {
    Write-Warning "Installer artifact is not signed. Use -CertificateThumbprint and -RequireSigned for public release builds."
  }
}

function Assert-SignedIfRequired([string] $PathToCheck, [bool] $RequireValidSignature, [string] $ArtifactLabel, [string] $Publisher) {
  if (-not $RequireValidSignature) {
    return
  }

  $signature = Get-AuthenticodeSignature -FilePath $PathToCheck
  if ($signature.Status -ne "Valid") {
    throw "$ArtifactLabel is not signed: $PathToCheck"
  }
  if (-not [string]::IsNullOrWhiteSpace($Publisher) -and
      ($null -eq $signature.SignerCertificate -or
       -not (Test-CertificatePublisher -Certificate $signature.SignerCertificate -Publisher $Publisher))) {
    throw "$ArtifactLabel is not signed by expected publisher '$Publisher': $PathToCheck"
  }
}

$AppDir = Split-Path -Parent $PSScriptRoot
if ($RequireCleanTree) {
  Assert-CleanGitTree -Path $AppDir
}

$Project = Join-Path $AppDir "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj"
$PublishScript = Join-Path $PSScriptRoot "publish-app.ps1"
$InstallerScript = Join-Path $AppDir "installer\LlamaCppWindowsManager.iss"
$DistRoot = [System.IO.Path]::GetFullPath((Join-Path $AppDir "dist"))
$PublishDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime"))
$OutputDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot "installer"))
$PublishedExe = Join-Path $PublishDir "LlamaCppWindowsManager.exe"
$AppVersion = Read-ProjectVersion $Project
$ExpectedInstaller = Join-Path $OutputDir "LlamaCppWindowsManager-Setup-$AppVersion-$Runtime.exe"

if (-not (Test-Path -LiteralPath $InstallerScript)) {
  throw "Installer script not found: $InstallerScript"
}
if (-not ($OutputDir.StartsWith($DistRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
  throw "Refusing to write installer outside the dist folder: $OutputDir"
}

if (-not $SkipPublish) {
  $publishArgs = @{
    Runtime = $Runtime
    Configuration = $Configuration
  }
  if ($CertificateThumbprint) {
    $publishArgs.CertificateThumbprint = $CertificateThumbprint
    $publishArgs.TimestampServer = $TimestampServer
  }
  if ($ExpectedPublisher) {
    $publishArgs.ExpectedPublisher = $ExpectedPublisher
  }
  if ($ReleaseManifestKeyId) {
    $publishArgs.ReleaseManifestKeyId = $ReleaseManifestKeyId
  }
  if ($ReleaseManifestPublicKeySpki) {
    $publishArgs.ReleaseManifestPublicKeySpki = $ReleaseManifestPublicKeySpki
  }
  if ($ReleaseManifestNextKeyId) {
    $publishArgs.ReleaseManifestNextKeyId = $ReleaseManifestNextKeyId
  }
  if ($ReleaseManifestNextPublicKeySpki) {
    $publishArgs.ReleaseManifestNextPublicKeySpki = $ReleaseManifestNextPublicKeySpki
  }
  if ($RequireSigned) {
    $publishArgs.RequireSigned = $true
  }
  if ($RequireCleanTree) {
    $publishArgs.RequireCleanTree = $true
  }

  & $PublishScript @publishArgs
  if ($LASTEXITCODE -ne 0) {
    throw "publish-app.ps1 failed."
  }
}

if (-not (Test-Path -LiteralPath $PublishedExe)) {
  throw "Published executable not found. Run publish-app.ps1 first or omit -SkipPublish: $PublishedExe"
}
Assert-SignedIfRequired $PublishedExe $RequireSigned.IsPresent "Published executable" $ExpectedPublisher

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
if (Test-Path -LiteralPath $ExpectedInstaller) {
  Remove-Item -LiteralPath $ExpectedInstaller -Force
}

$Iscc = Resolve-InnoSetupCompiler $InnoSetupPath
$isccArgs = @(
  "/DSourceDir=$PublishDir",
  "/DOutputDir=$OutputDir",
  "/DAppVersion=$AppVersion",
  $InstallerScript
)
& $Iscc @isccArgs
if ($LASTEXITCODE -ne 0) {
  throw "Inno Setup compiler failed."
}
if (-not (Test-Path -LiteralPath $ExpectedInstaller)) {
  throw "Expected installer was not created: $ExpectedInstaller"
}

Sign-FileIfRequested $ExpectedInstaller $CertificateThumbprint $TimestampServer $RequireSigned.IsPresent $ExpectedPublisher

$InstallerHash = (Get-FileHash -LiteralPath $ExpectedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$InstallerHashPath = "$ExpectedInstaller.sha256"
Set-Content -LiteralPath $InstallerHashPath -Value "$InstallerHash  $(Split-Path -Leaf $ExpectedInstaller)" -Encoding ascii

Write-Host "Built llama.cpp Windows Manager installer at $ExpectedInstaller" -ForegroundColor Green
Write-Host "Wrote SHA-256 companion file to $InstallerHashPath" -ForegroundColor Green
