param(
  [Parameter(Mandatory = $true)]
  [string] $Version,
  [Parameter(Mandatory = $true)]
  [string] $Tag,
  [Parameter(Mandatory = $true)]
  [string] $Commit,
  [Parameter(Mandatory = $true)]
  [string] $BuildTimestampUtc,
  [Parameter(Mandatory = $true)]
  [string] $ExpectedPublisher,
  [Parameter(Mandatory = $true)]
  [string] $KeyId,
  [Parameter(Mandatory = $true)]
  [string] $SigningPrivateKeyPath,
  [Parameter(Mandatory = $true)]
  [string] $ExpectedPublicKeySpki,
  [ValidateSet("stable", "preview", "nightly")]
  [string] $ReleaseChannel = "stable",
  [ValidateRange(1, 3650)]
  [int] $ValidityDays = 730,
  [string] $Runtime = "win-x64",
  [string] $OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$DistRoot = Join-Path $RepoRoot "dist"
$PublishDir = Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime"
$InstallerDir = Join-Path $DistRoot "installer"
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
  Join-Path $DistRoot "release"
} else {
  [System.IO.Path]::GetFullPath($OutputDirectory)
}

function Resolve-ReleaseFile([string] $Path, [string] $Label) {
  $resolved = [System.IO.Path]::GetFullPath($Path)
  if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
    throw "$Label was not found: $resolved"
  }
  return $resolved
}

function New-Artifact([string] $Path, [string] $Role, [string] $MediaType) {
  $item = Get-Item -LiteralPath $Path
  return [ordered]@{
    name = $item.Name
    role = $Role
    mediaType = $MediaType
    size = [long]$item.Length
    sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

$normalizedVersion = $Version.Trim().TrimStart('v')
if ($Tag -ne "v$normalizedVersion") {
  throw "Release tag '$Tag' does not match version '$normalizedVersion'."
}
if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
  throw "Release commit must be a full 40-character Git SHA."
}
$parsedBuildTimestamp = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse($BuildTimestampUtc, [ref]$parsedBuildTimestamp)) {
  throw "BuildTimestampUtc is not a valid timestamp: $BuildTimestampUtc"
}
$builtAt = $parsedBuildTimestamp.ToUniversalTime()
$privateKeyPath = Resolve-ReleaseFile $SigningPrivateKeyPath "Release-manifest signing key"

$appExe = Resolve-ReleaseFile (Join-Path $PublishDir "LlamaCppWindowsManager.exe") "Published application"
$appHash = Resolve-ReleaseFile "$appExe.sha256" "Application checksum"
$cliExe = Resolve-ReleaseFile (Join-Path $PublishDir "llwmctl.exe") "Published control CLI"
$cliHash = Resolve-ReleaseFile "$cliExe.sha256" "Control CLI checksum"
$installer = Resolve-ReleaseFile (Join-Path $InstallerDir "LlamaCppWindowsManager-Setup-$normalizedVersion-$Runtime.exe") "Installer"
$installerHash = Resolve-ReleaseFile "$installer.sha256" "Installer checksum"
$sbom = Resolve-ReleaseFile (Join-Path $PublishDir "sbom.spdx.json") "SPDX SBOM"

$artifacts = @(
  New-Artifact $appExe "application" "application/vnd.microsoft.portable-executable"
  New-Artifact $appHash "checksum" "text/plain"
  New-Artifact $cliExe "control-cli" "application/vnd.microsoft.portable-executable"
  New-Artifact $cliHash "checksum" "text/plain"
  New-Artifact $installer "installer" "application/vnd.microsoft.portable-executable"
  New-Artifact $installerHash "checksum" "text/plain"
  New-Artifact $sbom "sbom" "application/spdx+json"
)

$manifest = [ordered]@{
  schemaVersion = 1
  applicationVersion = $normalizedVersion
  tag = $Tag
  commit = $Commit.ToLowerInvariant()
  repository = "alekk89/llama-cpp-windows-manager"
  releaseChannel = $ReleaseChannel
  builtAtUtc = $builtAt.ToString("O")
  expiresAtUtc = $builtAt.AddDays($ValidityDays).ToString("O")
  minimumSecureUpdaterVersion = "2.5.0"
  signingKeyId = $KeyId
  expectedWindowsPublisher = $ExpectedPublisher
  artifacts = $artifacts
  sbom = [ordered]@{
    name = (Get-Item -LiteralPath $sbom).Name
    sha256 = (Get-FileHash -LiteralPath $sbom -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$manifestPath = Join-Path $OutputRoot "release-manifest.json"
$signaturePath = Join-Path $OutputRoot "release-manifest.json.sig"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $manifestJson + "`n", $utf8)

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
  $ecdsa.ImportFromPem([System.IO.File]::ReadAllText($privateKeyPath))
  $derivedPublicKey = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
  if (-not [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
      [Convert]::FromBase64String($derivedPublicKey),
      [Convert]::FromBase64String($ExpectedPublicKeySpki))) {
    throw "The release-manifest private key does not match the configured public key."
  }

  $manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
  $signature = $ecdsa.SignData(
    $manifestBytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
  $envelope = [ordered]@{
    schemaVersion = 1
    keyId = $KeyId
    algorithm = "ECDSA_P256_SHA256"
    signature = [Convert]::ToBase64String($signature)
  }
  [System.IO.File]::WriteAllText(
    $signaturePath,
    ($envelope | ConvertTo-Json -Depth 3) + "`n",
    $utf8)
} finally {
  $ecdsa.Dispose()
}

Write-Host "Wrote signed release manifest to $manifestPath" -ForegroundColor Green
Write-Host "Wrote detached manifest signature to $signaturePath" -ForegroundColor Green
