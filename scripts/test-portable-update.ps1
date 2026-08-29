param(
  [Parameter(Mandatory = $true)][string] $CandidateArchivePath,
  [Parameter(Mandatory = $true)][string] $ManifestPath,
  [Parameter(Mandatory = $true)][string] $SignaturePath,
  [Parameter(Mandatory = $true)][string] $ManifestPublicKeySpki,
  [Parameter(Mandatory = $true)][string] $ExpectedPublisher,
  [string] $BaselinePath = "tests/release-baselines/v2.4.0.json",
  [switch] $RequireSigned
)

$ErrorActionPreference = "Stop"

function Test-CertificatePublisher {
  param(
    [Parameter(Mandatory = $true)] $Certificate,
    [Parameter(Mandatory = $true)][string] $Publisher
  )

  $simpleName = $Certificate.GetNameInfo(
    [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false)
  return [string]::Equals($simpleName, $Publisher, [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($Certificate.Subject, $Publisher, [StringComparison]::OrdinalIgnoreCase)
}
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$CandidateArchive = [IO.Path]::GetFullPath($CandidateArchivePath)
$ManifestFile = [IO.Path]::GetFullPath($ManifestPath)
$SignatureFile = [IO.Path]::GetFullPath($SignaturePath)
$BaselineFile = [IO.Path]::GetFullPath((Join-Path $RepoRoot $BaselinePath))
foreach ($required in @($CandidateArchive, $ManifestFile, $SignatureFile, $BaselineFile)) {
  if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Portable-update input was not found: $required" }
}

$manifestBytes = [IO.File]::ReadAllBytes($ManifestFile)
$signature = Get-Content -LiteralPath $SignatureFile -Raw | ConvertFrom-Json
if ($signature.algorithm -ne "ECDSA_P256_SHA256") { throw "Unsupported release-manifest signature algorithm." }
$ecdsa = [Security.Cryptography.ECDsa]::Create()
try {
  $read = 0
  $ecdsa.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($ManifestPublicKeySpki), [ref]$read)
  if (-not $ecdsa.VerifyData(
      $manifestBytes,
      [Convert]::FromBase64String($signature.signature),
      [Security.Cryptography.HashAlgorithmName]::SHA256,
      [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
    throw "Release-manifest signature verification failed."
  }
} finally { $ecdsa.Dispose() }

$manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
$candidateItem = Get-Item -LiteralPath $CandidateArchive
$artifact = @($manifest.artifacts | Where-Object { $_.name -eq $candidateItem.Name -and $_.role -eq "portable-archive" })
if ($artifact.Count -ne 1) { throw "Candidate archive is not uniquely listed by the signed manifest." }
if ([long]$artifact[0].size -ne $candidateItem.Length) { throw "Candidate portable archive size mismatch." }
if ($artifact[0].sha256 -ne (Get-FileHash -LiteralPath $CandidateArchive -Algorithm SHA256).Hash.ToLowerInvariant()) {
  throw "Candidate portable archive hash mismatch."
}

$baseline = Get-Content -LiteralPath $BaselineFile -Raw | ConvertFrom-Json
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("llwm-portable-update-" + [Guid]::NewGuid().ToString("N"))
$rootPrefix = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
$oldArchive = Join-Path $testRoot $baseline.portableArchive.name
$oldRoot = Join-Path $testRoot "old"
$candidateRoot = Join-Path $testRoot "candidate"
try {
  New-Item -ItemType Directory -Path $testRoot, $oldRoot, $candidateRoot -Force | Out-Null
  Invoke-WebRequest -Uri $baseline.portableArchive.url -OutFile $oldArchive -UseBasicParsing
  if ((Get-Item -LiteralPath $oldArchive).Length -ne [long]$baseline.portableArchive.size) { throw "Pinned old portable size mismatch." }
  if ((Get-FileHash -LiteralPath $oldArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $baseline.portableArchive.sha256) {
    throw "Pinned old portable hash mismatch."
  }
  Expand-Archive -LiteralPath $oldArchive -DestinationPath $oldRoot
  Expand-Archive -LiteralPath $CandidateArchive -DestinationPath $candidateRoot
  $sourceExe = Get-ChildItem -LiteralPath $candidateRoot -Recurse -Filter LlamaCppWindowsManager.exe -File | Select-Object -First 1
  $sourceCli = Get-ChildItem -LiteralPath $candidateRoot -Recurse -Filter llwmctl.exe -File | Select-Object -First 1
  $targetExe = Get-ChildItem -LiteralPath $oldRoot -Recurse -Filter LlamaCppWindowsManager.exe -File | Select-Object -First 1
  $targetCli = Get-ChildItem -LiteralPath $oldRoot -Recurse -Filter llwmctl.exe -File | Select-Object -First 1
  if ($null -eq $sourceExe -or $null -eq $sourceCli -or $null -eq $targetExe -or $null -eq $targetCli) {
    throw "Portable package layout is missing the app or control CLI."
  }
  if ($RequireSigned) {
    foreach ($file in @($sourceExe, $sourceCli)) {
      $authenticode = Get-AuthenticodeSignature -FilePath $file.FullName
      if ($authenticode.Status -ne "Valid" -or $null -eq $authenticode.SignerCertificate -or
          -not (Test-CertificatePublisher -Certificate $authenticode.SignerCertificate -Publisher $ExpectedPublisher)) {
        throw "Candidate portable file has an invalid or unexpected publisher: $($file.Name)"
      }
    }
  }

  $dataRoot = Join-Path $oldRoot "data"
  New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
  $sentinel = Join-Path $dataRoot "portable-update-preserve.canary"
  Set-Content -LiteralPath $sentinel -Value "preserve" -Encoding ascii
  $appAssembly = Join-Path $RepoRoot "src\LocalLlmConsole.App\bin\Release\net10.0-windows\win-x64\LlamaCppWindowsManager.dll"
  $assembly = [Reflection.Assembly]::LoadFrom($appAssembly)
  $serviceType = $assembly.GetType("LocalLlmConsole.Services.AppUpdateService", $true)
  $updater = $serviceType.GetMethod("UpdaterScript", [Reflection.BindingFlags]"NonPublic,Static").Invoke($null, $null)
  $script = Join-Path $testRoot "apply-update.ps1"
  [IO.File]::WriteAllText($script, $updater, [Text.UTF8Encoding]::new($false))
  $noticeSource = Join-Path $testRoot "notice-source.json"
  $noticeTarget = Join-Path $dataRoot "pending-update-notice.json"
  [IO.File]::WriteAllText($noticeSource, "{}", [Text.UTF8Encoding]::new($false))
  $updaterOutput = & powershell.exe @(
    "-NoProfile", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-File", $script,
    "-ParentPid", "999999", "-SourceExe", $sourceExe.FullName, "-TargetExe", $targetExe.FullName,
    "-SourceCli", $sourceCli.FullName, "-TargetCli", $targetCli.FullName,
    "-NoticeSource", $noticeSource, "-NoticeTarget", $noticeTarget,
    "-WorkingDirectory", $oldRoot, "-SkipRestart") 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "Real portable update helper failed with exit code $LASTEXITCODE`: $($updaterOutput -join [Environment]::NewLine)"
  }
  if ((Get-FileHash $targetExe.FullName -Algorithm SHA256).Hash -ne (Get-FileHash $sourceExe.FullName -Algorithm SHA256).Hash) {
    throw "Updated application does not match the verified candidate."
  }
  if ((Get-FileHash $targetCli.FullName -Algorithm SHA256).Hash -ne (Get-FileHash $sourceCli.FullName -Algorithm SHA256).Hash) {
    throw "Updated control CLI does not match the verified candidate."
  }
  if (-not (Test-Path -LiteralPath $sentinel)) { throw "Portable update removed retained settings/data." }
  Write-Host "Pinned previous portable-to-candidate update validation passed." -ForegroundColor Green
}
finally {
  $resolved = [IO.Path]::GetFullPath($testRoot)
  if (($resolved + '\').StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
