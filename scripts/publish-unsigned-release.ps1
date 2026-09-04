param(
  [Parameter(Mandatory = $true)][string] $Tag,
  [Parameter(Mandatory = $true)][string] $AssetDirectory,
  [switch] $Publish
)

$ErrorActionPreference = "Stop"
$repository = "alekk89/llama-cpp-windows-manager"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Expected a stable vX.Y.Z tag." }
$version = $Tag.Substring(1)
$notes = Join-Path $repoRoot "docs/releases/$Tag.md"
if (-not (Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Final release notes are missing." }
$dirty = @(& git -C $repoRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw "Publish from a clean, reviewed Git checkout." }
$headCommit = & git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "Could not resolve HEAD." }
$tagType = & git -C $repoRoot cat-file -t "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0 -or $tagType -ne "tag") { throw "An annotated release tag is required." }
$tagCommit = & git -C $repoRoot rev-parse "$Tag^{commit}"
if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $headCommit) { throw "Release tag must identify the checked-out, validated commit." }

# v1.0/v1.1 clients fall back to the first EXE when the old filename is absent.
# GitHub sorts assets by filename; the Setup- prefix keeps the portable EXE first.
# Verify the returned order as well as the uploaded bytes before publication.
$names = @("LlamaCppWindowsManager.exe", "LlamaCppWindowsManager.exe.sha256",
  "Setup-LlamaCppWindowsManager-$version-win-x64.exe", "Setup-LlamaCppWindowsManager-$version-win-x64.exe.sha256")
$files = @($names | ForEach-Object { Join-Path ([IO.Path]::GetFullPath($AssetDirectory)) $_ })
foreach ($file in $files) {
  if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing release asset: $file" }
}
foreach ($file in @($files[0], $files[2])) {
  $expected = ((Get-Content -LiteralPath "$file.sha256" -Raw).Trim() -split '\s+')[0]
  if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) { throw "Checksum mismatch: $file" }
  if ((Get-AuthenticodeSignature -FilePath $file).Status -ne "NotSigned") { throw "Expected unsigned community artifact: $file" }
}
$productVersion = (Get-Item -LiteralPath $files[0]).VersionInfo.ProductVersion.Trim()
if ($productVersion -ne "$Tag+$headCommit") { throw "Portable EXE must be built from tagged commit $headCommit; found $productVersion." }
$releasesJson = & gh api "repos/$repository/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw "Could not read GitHub releases." }
$existing = @($releasesJson | ConvertFrom-Json | Where-Object { $_.tag_name -eq $Tag })
if ($existing.Count -gt 0 -and -not $existing[0].draft) { throw "Release is already published; never replace its assets." }
if ($existing.Count -eq 0) {
  & gh release create $Tag --repo $repository --verify-tag --draft --title "llama.cpp Windows Manager $Tag" --notes-file $notes
  if ($LASTEXITCODE -ne 0) { throw "Could not create release draft." }
  foreach ($file in $files) {
    & gh release upload $Tag $file --repo $repository
    if ($LASTEXITCODE -ne 0) { throw "Asset upload failed; draft remains unpublished." }
  }
}
# The by-tag REST endpoint does not resolve unpublished drafts. Find the draft
# in the authenticated release list, then verify it through its numeric ID.
$draftsJson = & gh api "repos/$repository/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw "Could not locate the release draft." }
$drafts = @($draftsJson | ConvertFrom-Json | Where-Object { $_.tag_name -eq $Tag -and $_.draft })
if ($drafts.Count -ne 1) { throw "Expected exactly one unpublished release draft for $Tag." }
$releaseJson = & gh api "repos/$repository/releases/$($drafts[0].id)"
if ($LASTEXITCODE -ne 0) { throw "Could not verify the draft assets." }
$release = $releaseJson | ConvertFrom-Json
if (-not $release.draft -or $release.assets.Count -ne 4) { throw "Expected an unpublished draft containing exactly four assets." }
$firstExe = @($release.assets | Where-Object { $_.name.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase) })[0]
if ($firstExe.name -ne $names[0]) { throw "Legacy clients would select the wrong executable; refusing to publish." }
foreach ($file in $files) {
  $item = Get-Item -LiteralPath $file
  $asset = @($release.assets | Where-Object { $_.name -ceq $item.Name })
  $digest = 'sha256:' + (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($asset.Count -ne 1 -or $asset[0].size -ne $item.Length -or $asset[0].digest -ne $digest) { throw "Published draft asset does not match validated local file: $($item.Name)" }
}
if ($Publish) {
  & gh release edit $Tag --repo $repository --draft=false --latest --notes-file $notes
  if ($LASTEXITCODE -ne 0) { throw "Could not publish the verified release draft." }
} else {
  Write-Host "Draft assets verified. After all release gates pass, rerun with -Publish to publish."
}
