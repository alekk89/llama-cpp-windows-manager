param(
  [ValidateSet("win-x64")]
  [string] $Runtime = "win-x64",
  [string] $Configuration = "Release",
  [string] $CertificateThumbprint = "",
  [string] $TimestampServer = "https://timestamp.digicert.com",
  [switch] $RequireSigned,
  [switch] $RequireCleanTree
)

$ErrorActionPreference = "Stop"

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

$AppDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
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

$Info = & $Dotnet --info
if ($Info -match "No SDKs were found") {
  throw ".NET runtime is installed, but no SDK was found. Install the .NET 10 SDK to publish the self-contained app."
}

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
  $CliSignature = Set-AuthenticodeSignature -FilePath $CliExe -Certificate $Cert -TimestampServer $TimestampServer
  if ($CliSignature.Status -ne "Valid") { throw "llwmctl code signing failed: $($CliSignature.Status) $($CliSignature.StatusMessage)" }
}

$PublishedCliSignature = Get-AuthenticodeSignature -FilePath $CliExe
if ($RequireSigned -and $PublishedCliSignature.Status -ne "Valid") {
  throw "Published control CLI is not signed. Pass -CertificateThumbprint or sign $CliExe before release."
}

New-Item -ItemType Directory -Path (Join-Path $BundleStageDir "docs") -Force | Out-Null
$BundleFiles = @(
  @{ Path = "llwmctl.exe"; Source = $CliExe },
  @{ Path = "AGENTS.md"; Source = (Join-Path $AppDir "AGENTS.md") },
  @{ Path = "agent.md"; Source = (Join-Path $AppDir "agent.md") },
  @{ Path = "docs/CONTROL_API.md"; Source = (Join-Path $AppDir "docs\CONTROL_API.md") }
)
foreach ($BundleFile in $BundleFiles) {
  if (-not (Test-Path -LiteralPath $BundleFile.Source -PathType Leaf)) {
    throw "Agent-sidecar source file was not found: $($BundleFile.Source)"
  }
  $BundleTarget = Join-Path $BundleStageDir ($BundleFile.Path -replace '/', '\')
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
  "-c",
  $Configuration,
  "-r",
  $Runtime,
  "--self-contained",
  "true",
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:EnableCompressionInSingleFile=true",
  "-p:AgentBootstrapBundlePath=$BundleZip",
  "-o",
  $PublishDir
)
& $Dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Copy-Item -LiteralPath $CliExe -Destination (Join-Path $PublishDir "llwmctl.exe") -Force
Copy-Item -LiteralPath (Join-Path $BundleStageDir "AGENTS.md") -Destination (Join-Path $PublishDir "AGENTS.md") -Force
Copy-Item -LiteralPath (Join-Path $BundleStageDir "agent.md") -Destination (Join-Path $PublishDir "agent.md") -Force
New-Item -ItemType Directory -Path (Join-Path $PublishDir "docs") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $BundleStageDir "docs\CONTROL_API.md") -Destination (Join-Path $PublishDir "docs\CONTROL_API.md") -Force

Get-ChildItem -Path $PublishDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue |
  Remove-Item -Force

$Exe = Join-Path $PublishDir "LlamaCppWindowsManager.exe"
$CliExe = Join-Path $PublishDir "llwmctl.exe"
if ($CertificateThumbprint) {
  $Signature = Set-AuthenticodeSignature -FilePath $Exe -Certificate $Cert -TimestampServer $TimestampServer
  if ($Signature.Status -ne "Valid") { throw "Code signing failed: $($Signature.Status) $($Signature.StatusMessage)" }
}

$PublishedSignature = Get-AuthenticodeSignature -FilePath $Exe
if ($RequireSigned -and $PublishedSignature.Status -ne "Valid") {
  throw "Published executable is not signed. Pass -CertificateThumbprint or sign $Exe before release."
}
$PublishedCliSignature = Get-AuthenticodeSignature -FilePath $CliExe
if ($RequireSigned -and $PublishedCliSignature.Status -ne "Valid") {
  throw "Published control CLI is not signed. Pass -CertificateThumbprint or sign $CliExe before release."
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
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -Force
$ZipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$ZipHashPath = "$ZipPath.sha256"
Set-Content -LiteralPath $ZipHashPath -Value "$ZipHash  $(Split-Path -Leaf $ZipPath)" -Encoding ascii

Remove-DistPath -Path $CliPublishDir -Label "temporary llwmctl publish folder" -Recurse
Remove-DistPath -Path $BundleStageDir -Label "temporary agent-sidecar folder" -Recurse
Remove-DistPath -Path $BundleZip -Label "temporary agent-sidecar archive"

Write-Host "Published llama.cpp Windows Manager self-contained app to $PublishDir" -ForegroundColor Green
Write-Host "Wrote SHA-256 companion file to $ExeHashPath" -ForegroundColor Green
Write-Host "Wrote llwmctl SHA-256 companion file to $CliExeHashPath" -ForegroundColor Green
Write-Host "Wrote portable release archive to $ZipPath" -ForegroundColor Green
