# Runtime-aligned packages ship one major version per .NET release. While a
# project targets net<N>, version N.x servicing updates are required, but the
# next .NET major is a target-framework migration rather than a routine update.
$RuntimeAlignedPackagePattern = '^(System\.|Microsoft\.Data\.Sqlite|Microsoft\.Extensions\.|Microsoft\.Win32\.)'

function Get-VersionMajor([string] $version) {
  if ($version -match '^\s*\[?(\d+)\.') { return [int] $Matches[1] }
  return -1
}

function Get-FrameworkMajor([string] $framework) {
  if ($framework -match '^net(\d+)\.') { return [int] $Matches[1] }
  return -1
}

function Get-OutdatedPackages($json) {
  $outdated = @()
  foreach ($project in @($json.projects)) {
    foreach ($framework in @($project.frameworks)) {
      $frameworkMajor = Get-FrameworkMajor ([string] $framework.framework)
      foreach ($package in @($framework.topLevelPackages)) {
        if ($null -eq $package -or [string]::IsNullOrWhiteSpace([string] $package.latestVersion)) { continue }
        $resolvedMajor = Get-VersionMajor ([string] $package.resolvedVersion)
        $latestMajor = Get-VersionMajor ([string] $package.latestVersion)
        $isNextRuntimeMajor = [string] $package.id -match $RuntimeAlignedPackagePattern `
          -and $resolvedMajor -eq $frameworkMajor `
          -and $latestMajor -gt $resolvedMajor
        if ($isNextRuntimeMajor) {
          Write-Host "Ignoring $($package.id) $($package.latestVersion): next .NET major for $($framework.framework)." -ForegroundColor DarkGray
          continue
        }
        $outdated += $package
      }
    }
  }
  return ,$outdated
}
