param(
  [Parameter(Mandatory = $true)]
  [string] $OutputPath,
  [string] $Version = "development"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LockFiles = @(
  (Join-Path $RepoRoot "src\LocalLlmConsole.App\packages.publish.lock.json"),
  (Join-Path $RepoRoot "src\LocalLlmConsole.ControlCli\packages.publish.lock.json")
)

$packages = @{}
foreach ($lockFile in $LockFiles) {
  if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) {
    throw "NuGet lock file was not found: $lockFile"
  }
  $lock = Get-Content -LiteralPath $lockFile -Raw | ConvertFrom-Json
  foreach ($target in $lock.dependencies.PSObject.Properties) {
    foreach ($dependency in $target.Value.PSObject.Properties) {
      $resolved = [string]$dependency.Value.resolved
      if ([string]::IsNullOrWhiteSpace($resolved)) { continue }
      $key = "$($dependency.Name)/$resolved"
      if (-not $packages.ContainsKey($key)) {
        $packages[$key] = [ordered]@{
          name = $dependency.Name
          version = $resolved
          direct = [string]$dependency.Value.type -eq "Direct"
        }
      } elseif ([string]$dependency.Value.type -eq "Direct") {
        $packages[$key].direct = $true
      }
    }
  }
}

function ConvertTo-SpdxId {
  param([Parameter(Mandatory = $true)][string] $Value)
  return "SPDXRef-" + ($Value -replace "[^A-Za-z0-9.-]", "-")
}

$created = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$documentNamespace = "https://github.com/alekk89/llama-cpp-windows-manager/sbom/$Version/$([Guid]::NewGuid().ToString('N'))"
$rootId = "SPDXRef-Package-LlamaCppWindowsManager"
$spdxPackages = @(
  [ordered]@{
    SPDXID = $rootId
    name = "llama.cpp Windows Manager"
    versionInfo = $Version
    downloadLocation = "https://github.com/alekk89/llama-cpp-windows-manager"
    filesAnalyzed = $false
    licenseConcluded = "MIT"
    licenseDeclared = "MIT"
    copyrightText = "NOASSERTION"
    supplier = "Organization: llama.cpp Windows Manager community project"
  }
)
$relationships = @()

foreach ($entry in $packages.Values | Sort-Object name, version) {
  $packageId = ConvertTo-SpdxId "Package-NuGet-$($entry.name)-$($entry.version)"
  $spdxPackages += [ordered]@{
    SPDXID = $packageId
    name = $entry.name
    versionInfo = $entry.version
    downloadLocation = "https://www.nuget.org/packages/$($entry.name)/$($entry.version)"
    filesAnalyzed = $false
    licenseConcluded = "NOASSERTION"
    licenseDeclared = "NOASSERTION"
    copyrightText = "NOASSERTION"
    externalRefs = @(
      [ordered]@{
        referenceCategory = "PACKAGE-MANAGER"
        referenceType = "purl"
        referenceLocator = "pkg:nuget/$([Uri]::EscapeDataString($entry.name))@$([Uri]::EscapeDataString($entry.version))"
      }
    )
    comment = if ($entry.direct) { "Direct NuGet dependency." } else { "Transitive NuGet dependency." }
  }
  $relationships += [ordered]@{
    spdxElementId = $rootId
    relationshipType = "DEPENDS_ON"
    relatedSpdxElement = $packageId
  }
}

$document = [ordered]@{
  spdxVersion = "SPDX-2.3"
  dataLicense = "CC0-1.0"
  SPDXID = "SPDXRef-DOCUMENT"
  name = "llama.cpp Windows Manager $Version"
  documentNamespace = $documentNamespace
  creationInfo = [ordered]@{
    created = $created
    creators = @("Tool: scripts/new-sbom.ps1")
    licenseListVersion = "3.25"
  }
  documentDescribes = @($rootId)
  packages = $spdxPackages
  relationships = $relationships
}

$parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
if (-not [string]::IsNullOrWhiteSpace($parent)) {
  New-Item -ItemType Directory -Path $parent -Force | Out-Null
}
$document | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Wrote SPDX SBOM to $OutputPath" -ForegroundColor Green
