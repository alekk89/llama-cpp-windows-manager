param()

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot "src/LocalLlmConsole.App/LocalLlmConsole.App.csproj") -Raw
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid application version." }
$notes = Join-Path $repoRoot "docs/releases/v$version.md"
if (-not (Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Final release notes are missing: $notes" }
$app = Join-Path $repoRoot "dist/LlamaCppWindowsManager-win-x64/LlamaCppWindowsManager.exe"
$installer = Join-Path $repoRoot "dist/installer/LlamaCppWindowsManager-Setup-$version-win-x64.exe"
$output = Join-Path $repoRoot "dist/unsigned-v$version"
if (Test-Path -LiteralPath $output) {
  # Never blend a new upload set with previous artifacts.
  throw "Release staging folder already exists; choose a fresh build checkout: $output"
}
foreach ($file in @($app, $installer)) {
  if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or -not (Test-Path -LiteralPath "$file.sha256" -PathType Leaf)) {
    throw "Build the application, installer, and checksum companions first: $file"
  }
  $expected = ((Get-Content -LiteralPath "$file.sha256" -Raw).Trim() -split '\s+')[0]
  $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
  if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or $actual -ne $expected) { throw "Checksum mismatch: $file" }
  if ((Get-AuthenticodeSignature -FilePath $file).Status -ne "NotSigned") { throw "Expected explicitly unsigned artifact: $file" }
  $artifactVersion = ((Get-Item -LiteralPath $file).VersionInfo.ProductVersion -split '\+', 2)[0].Trim().TrimStart('v')
  if ($artifactVersion -ne $version) {
    throw "Artifact version does not match $version`: $file"
  }
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($file in @($app, "$app.sha256")) {
  Copy-Item -LiteralPath $file -Destination $output
}
# GitHub sorts release assets by name. Keep the portable EXE ahead of the
# installer for old clients that fall back to the first executable.
$publicInstallerName = "Setup-LlamaCppWindowsManager-$version-win-x64.exe"
$publicInstaller = Join-Path $output $publicInstallerName
Copy-Item -LiteralPath $installer -Destination $publicInstaller
$installerHash = (Get-FileHash -LiteralPath $publicInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$publicInstaller.sha256", "$installerHash  $publicInstallerName`n", [Text.UTF8Encoding]::new($false))
Write-Host "Staged four unsigned release assets in $output" -ForegroundColor Green
Write-Host "Release notes: $notes"
Write-Host "Staging does not publish or certify validation. Review the release gate and upgrade results before upload."
