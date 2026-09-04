param(
  [ValidateSet("win-x64")]
  [string] $Runtime = "win-x64",
  [string] $Configuration = "Release",
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
  [switch] $RequireSigned,
  [switch] $RequireCleanTree
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

function Remove-DistPath {
  param(
    [Parameter(Mandatory = $true)]
    [string] $Path,
    [Parameter(Mandatory = $true)]
    [string] $Label,
    [switch] $Recurse
  )

  $full = [System.IO.Path]::GetFullPath($Path)
  $root = $DistRoot.TrimEnd('\', '/')
  if (-not $full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove $Label outside the dist folder: $full"
  }
  if (-not (Test-Path -LiteralPath $full)) {
    return
  }

  $item = Get-Item -LiteralPath $full -Force
  if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to remove $Label because it is a symlink or junction: $full"
  }

  if ($Recurse) {
    Remove-Item -LiteralPath $full -Recurse -Force
  } else {
    Remove-Item -LiteralPath $full -Force
  }
}

function Add-AgentSidecarBundle {
  param(
    [Parameter(Mandatory = $true)]
    [string] $ExecutablePath,
    [Parameter(Mandatory = $true)]
    [string] $BundlePath
  )

  $marker = [System.Text.Encoding]::ASCII.GetBytes("LLWM_AGENT_SIDECARS_V1")
  $bundle = Get-Item -LiteralPath $BundlePath
  $length = [System.BitConverter]::GetBytes([long] $bundle.Length)
  if (-not [System.BitConverter]::IsLittleEndian) {
    [System.Array]::Reverse($length)
  }

  $output = [System.IO.File]::Open(
    $ExecutablePath,
    [System.IO.FileMode]::Append,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
  try {
    $input = [System.IO.File]::OpenRead($BundlePath)
    try {
      $input.CopyTo($output)
    } finally {
      $input.Dispose()
    }
    $output.Write($length, 0, $length.Length)
    $output.Write($marker, 0, $marker.Length)
    $output.Flush()
  } finally {
    $output.Dispose()
  }
}

$AppDir = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) {
  $candidateCommit = @(& git -C $AppDir rev-parse HEAD 2>$null)
  if ($LASTEXITCODE -eq 0 -and $candidateCommit.Count -gt 0) {
    $RepositoryCommit = $candidateCommit[0].Trim()
  }
}
if ($RepositoryCommit -notmatch '^[0-9a-fA-F]{40}$') { $RepositoryCommit = "unknown" }
if ($RequireCleanTree) {
  Assert-CleanGitTree -Path $AppDir
}

$Project = Join-Path $AppDir "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj"
$CliProject = Join-Path $AppDir "src\LocalLlmConsole.ControlCli\LocalLlmConsole.ControlCli.csproj"
$DistRoot = [System.IO.Path]::GetFullPath((Join-Path $AppDir "dist"))
$PublishDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime"))
$CliPublishDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot ".llwmctl-$Runtime"))
$BundleStageDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot ".agent-sidecars-$Runtime"))
$BundleZip = [System.IO.Path]::GetFullPath((Join-Path $DistRoot ".agent-sidecars-$Runtime.zip"))
if (-not ($PublishDir.StartsWith($DistRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
  throw "Refusing to publish outside the dist folder: $PublishDir"
}
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
$DotnetRoot = Split-Path -Parent ([System.IO.Path]::GetFullPath($Dotnet))
$DotnetLicense = Join-Path $DotnetRoot "LICENSE.txt"
$DotnetNotices = Join-Path $DotnetRoot "ThirdPartyNotices.txt"

$Info = & $Dotnet --info
if ($Info -match "No SDKs were found") {
  throw ".NET runtime is installed, but no SDK was found. Install the .NET 10 SDK to publish the self-contained app."
}

& $Dotnet restore $CliProject -r $Runtime --locked-mode -p:PublishAot=true -p:SelfContained=true -p:NuGetLockFilePath=packages.publish.lock.json
if ($LASTEXITCODE -ne 0) { throw "Locked restore failed for $CliProject" }
& $Dotnet restore $Project -r $Runtime --locked-mode -p:PublishSingleFile=true -p:SelfContained=true
if ($LASTEXITCODE -ne 0) { throw "Locked restore failed for $Project" }

if (Test-Path -LiteralPath $PublishDir) {
  Remove-DistPath -Path $PublishDir -Label "publish folder" -Recurse
}
if (Test-Path -LiteralPath $CliPublishDir) {
  Remove-DistPath -Path $CliPublishDir -Label "temporary llwmctl publish folder" -Recurse
}
if (Test-Path -LiteralPath $BundleStageDir) {
  Remove-DistPath -Path $BundleStageDir -Label "temporary agent-sidecar folder" -Recurse
}
if (Test-Path -LiteralPath $BundleZip) {
  Remove-DistPath -Path $BundleZip -Label "temporary agent-sidecar archive"
}

$cliPublishArgs = @(
  "publish",
  $CliProject,
  "--no-restore",
  "-c",
  $Configuration,
  "-r",
  $Runtime,
  "--self-contained",
  "true",
  "-p:PublishAot=true",
  "-p:NuGetLockFilePath=packages.publish.lock.json",
  "-o",
  $CliPublishDir
)
& $Dotnet @cliPublishArgs
if ($LASTEXITCODE -ne 0) { throw "llwmctl publish failed." }

$CliExe = Join-Path $CliPublishDir "llwmctl.exe"
if ($CertificateThumbprint) {
  $Cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -replace '\s', '' -ieq ($CertificateThumbprint -replace '\s', '') } |
    Select-Object -First 1
  if (-not $Cert) { throw "Code-signing certificate was not found in CurrentUser or LocalMachine certificate stores: $CertificateThumbprint" }
  if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
      -not (Test-CertificatePublisher $Cert $ExpectedPublisher)) {
    throw "Code-signing certificate subject '$($Cert.Subject)' does not match expected publisher '$ExpectedPublisher'."
  }
  $CliSignature = Set-AuthenticodeSignature -FilePath $CliExe -Certificate $Cert -TimestampServer $TimestampServer
  if ($CliSignature.Status -ne "Valid") { throw "llwmctl code signing failed: $($CliSignature.Status) $($CliSignature.StatusMessage)" }
}

$PublishedCliSignature = Get-AuthenticodeSignature -FilePath $CliExe
if ($RequireSigned -and $PublishedCliSignature.Status -ne "Valid") {
  throw "Published control CLI is not signed. Pass -CertificateThumbprint or sign $CliExe before release."
}

$BundleFiles = @(
  @{ Path = "llwmctl.exe"; Source = $CliExe },
  @{ Path = "AGENTS.md"; Source = (Join-Path $AppDir "AGENTS.md") },
  @{ Path = "agent.md"; Source = (Join-Path $AppDir "agent.md") },
  @{ Path = "docs/CONTROL_API.md"; Source = (Join-Path $AppDir "docs\CONTROL_API.md") },
  @{ Path = "LICENSE"; Source = (Join-Path $AppDir "LICENSE") },
  @{ Path = "THIRD-PARTY-NOTICES.md"; Source = (Join-Path $AppDir "THIRD-PARTY-NOTICES.md") },
  @{ Path = "licenses/Apache-2.0.txt"; Source = (Join-Path $AppDir "licenses\Apache-2.0.txt") },
  @{ Path = "licenses/dotnet/LICENSE.txt"; Source = $DotnetLicense },
  @{ Path = "licenses/dotnet/ThirdPartyNotices.txt"; Source = $DotnetNotices }
)
foreach ($BundleFile in $BundleFiles) {
  if (-not (Test-Path -LiteralPath $BundleFile.Source -PathType Leaf)) {
    throw "Agent-sidecar source file was not found: $($BundleFile.Source)"
  }
  $BundleTarget = Join-Path $BundleStageDir ($BundleFile.Path -replace '/', '\')
  New-Item -ItemType Directory -Path (Split-Path -Parent $BundleTarget) -Force | Out-Null
  Copy-Item -LiteralPath $BundleFile.Source -Destination $BundleTarget -Force
}

[xml] $ProjectXml = Get-Content -LiteralPath $Project -Raw
$AppVersion = @($ProjectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$ManifestFiles = @($BundleFiles | ForEach-Object {
  $BundlePath = Join-Path $BundleStageDir ($_.Path -replace '/', '\')
  [ordered]@{
    path = $_.Path
    size = (Get-Item -LiteralPath $BundlePath).Length
    sha256 = (Get-FileHash -LiteralPath $BundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
  }
})
$Manifest = [ordered]@{
  version = [string]$AppVersion
  files = $ManifestFiles
}
$Manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $BundleStageDir "manifest.json") -Encoding utf8
Compress-Archive -Path (Join-Path $BundleStageDir "*") -DestinationPath $BundleZip -Force

$publishArgs = @(
  "publish",
  $Project,
  "--no-restore",
  "-c",
  $Configuration,
  "-r",
  $Runtime,
  "--self-contained",
  "true",
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:EnableCompressionInSingleFile=true",
  "-o",
  $PublishDir
)
$publishArgs += "-p:RepositoryCommit=$RepositoryCommit"
$publishArgs += "-p:ReleaseChannel=$ReleaseChannel"
$signedUpdates = $RequireSigned.IsPresent -or -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
$publishArgs += "-p:RequireSignedUpdates=$($signedUpdates.ToString().ToLowerInvariant())"
if (-not [string]::IsNullOrWhiteSpace($ReleaseManifestKeyId)) {
  $publishArgs += "-p:ReleaseManifestKeyId=$ReleaseManifestKeyId"
}
if (-not [string]::IsNullOrWhiteSpace($ReleaseManifestPublicKeySpki)) {
  $publishArgs += "-p:ReleaseManifestPublicKeySpki=$ReleaseManifestPublicKeySpki"
}
if (-not [string]::IsNullOrWhiteSpace($ReleaseManifestNextKeyId)) {
  $publishArgs += "-p:ReleaseManifestNextKeyId=$ReleaseManifestNextKeyId"
}
if (-not [string]::IsNullOrWhiteSpace($ReleaseManifestNextPublicKeySpki)) {
  $publishArgs += "-p:ReleaseManifestNextPublicKeySpki=$ReleaseManifestNextPublicKeySpki"
}
& $Dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$Exe = Join-Path $PublishDir "LlamaCppWindowsManager.exe"
Add-AgentSidecarBundle -ExecutablePath $Exe -BundlePath $BundleZip

foreach ($BundleFile in $BundleFiles) {
  $BundleSource = Join-Path $BundleStageDir ($BundleFile.Path -replace '/', '\')
  $PublishTarget = Join-Path $PublishDir ($BundleFile.Path -replace '/', '\')
  New-Item -ItemType Directory -Path (Split-Path -Parent $PublishTarget) -Force | Out-Null
  Copy-Item -LiteralPath $BundleSource -Destination $PublishTarget -Force
}

$SbomPath = Join-Path $PublishDir "sbom.spdx.json"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "new-sbom.ps1") -OutputPath $SbomPath -Version ([string]$AppVersion)
if ($LASTEXITCODE -ne 0) { throw "SPDX SBOM generation failed." }

Get-ChildItem -Path $PublishDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue |
  Remove-Item -Force

$CliExe = Join-Path $PublishDir "llwmctl.exe"
if ($CertificateThumbprint) {
  $Signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $Cert -TimestampServer $TimestampServer
  if ($Signature.Status -ne "Valid") { throw "Code signing failed: $($Signature.Status) $($Signature.StatusMessage)" }
}

$PublishedSignature = Get-AuthenticodeSignature -FilePath $Exe
if ($RequireSigned -and $PublishedSignature.Status -ne "Valid") {
  throw "Published executable is not signed. Pass -CertificateThumbprint or sign $Exe before release."
}
if ($RequireSigned -and -not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
    ($null -eq $PublishedSignature.SignerCertificate -or
     -not (Test-CertificatePublisher $PublishedSignature.SignerCertificate $ExpectedPublisher))) {
  throw "Published executable is not signed by expected publisher '$ExpectedPublisher'."
}
$PublishedCliSignature = Get-AuthenticodeSignature -FilePath $CliExe
if ($RequireSigned -and $PublishedCliSignature.Status -ne "Valid") {
  throw "Published control CLI is not signed. Pass -CertificateThumbprint or sign $CliExe before release."
}
if ($RequireSigned -and -not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
    ($null -eq $PublishedCliSignature.SignerCertificate -or
     -not (Test-CertificatePublisher $PublishedCliSignature.SignerCertificate $ExpectedPublisher))) {
  throw "Published control CLI is not signed by expected publisher '$ExpectedPublisher'."
}
if ($PublishedSignature.Status -ne "Valid") {
  Write-Warning "Published executable is not signed. Use -CertificateThumbprint and -RequireSigned for public release builds."
}

$ExeHash = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
$ExeHashPath = "$Exe.sha256"
Set-Content -LiteralPath $ExeHashPath -Value "$ExeHash  $(Split-Path -Leaf $Exe)" -Encoding ascii
$CliExeHash = (Get-FileHash -LiteralPath $CliExe -Algorithm SHA256).Hash.ToLowerInvariant()
$CliExeHashPath = "$CliExe.sha256"
Set-Content -LiteralPath $CliExeHashPath -Value "$CliExeHash  $(Split-Path -Leaf $CliExe)" -Encoding ascii

$ZipPath = Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime.zip"
if (Test-Path -LiteralPath $ZipPath) {
  Remove-DistPath -Path $ZipPath -Label "portable release archive"
}
Remove-DistPath -Path "$ZipPath.sha256" -Label "obsolete portable archive checksum"

Remove-DistPath -Path $CliPublishDir -Label "temporary llwmctl publish folder" -Recurse
Remove-DistPath -Path $BundleStageDir -Label "temporary agent-sidecar folder" -Recurse
Remove-DistPath -Path $BundleZip -Label "temporary agent-sidecar archive"

Write-Host "Published llama.cpp Windows Manager self-contained app to $PublishDir" -ForegroundColor Green
Write-Host "Wrote SHA-256 companion file to $ExeHashPath" -ForegroundColor Green
Write-Host "Wrote llwmctl SHA-256 companion file to $CliExeHashPath" -ForegroundColor Green
Write-Host "Portable release download is the standalone executable (no ZIP)." -ForegroundColor Green
Write-Host "Wrote SPDX SBOM to $SbomPath" -ForegroundColor Green
