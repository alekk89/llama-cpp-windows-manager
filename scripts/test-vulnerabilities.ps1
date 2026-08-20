param(
  [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Resolve-Dotnet {
  $appDir = Split-Path -Parent $PSScriptRoot
  $bundledDotnet = Join-Path (Split-Path -Parent $appDir) ".dotnet-sdk-10\dotnet.exe"
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

function Count-Vulnerabilities($node) {
  if ($null -eq $node) { return 0 }

  $count = 0
  if ($node -is [System.Collections.IDictionary]) {
    foreach ($key in $node.Keys) {
      $value = $node[$key]
      if ($key -eq "vulnerabilities" -and $value -is [System.Collections.IEnumerable] -and -not ($value -is [string])) {
        $count += @($value).Count
      } else {
        $count += Count-Vulnerabilities $value
      }
    }
    return $count
  }

  if ($node -is [pscustomobject]) {
    foreach ($property in $node.PSObject.Properties) {
      $value = $property.Value
      if ($property.Name -eq "vulnerabilities" -and $value -is [System.Collections.IEnumerable] -and -not ($value -is [string])) {
        $count += @($value).Count
      } else {
        $count += Count-Vulnerabilities $value
      }
    }
    return $count
  }

  if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string])) {
    foreach ($item in $node) {
      $count += Count-Vulnerabilities $item
    }
  }

  return $count
}

function Count-Properties($node, [string] $propertyName) {
  if ($null -eq $node) { return 0 }

  $count = 0
  if ($node -is [pscustomobject]) {
    foreach ($property in $node.PSObject.Properties) {
      $value = $property.Value
      if ($property.Name -eq $propertyName -and $null -ne $value) {
        if ($value -is [System.Collections.IEnumerable] -and -not ($value -is [string])) {
          if (@($value).Count -gt 0) { $count++ }
        } elseif (-not [string]::IsNullOrWhiteSpace([string] $value)) {
          $count++
        }
      } else {
        $count += Count-Properties $value $propertyName
      }
    }
    return $count
  }

  if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string])) {
    foreach ($item in $node) {
      $count += Count-Properties $item $propertyName
    }
  }

  return $count
}

$appDir = Split-Path -Parent $PSScriptRoot
$projects = @(
  (Join-Path $appDir "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj"),
  (Join-Path $appDir "src\LocalLlmConsole.Core\LocalLlmConsole.Core.csproj"),
  (Join-Path $appDir "src\LocalLlmConsole.ControlCli\LocalLlmConsole.ControlCli.csproj"),
  (Join-Path $appDir "tests\LocalLlmConsole.Tests\LocalLlmConsole.Tests.csproj"),
  (Join-Path $appDir "tests\LocalLlmConsole.UiTests\LocalLlmConsole.UiTests.csproj")
)

$dotnet = Resolve-Dotnet
if (-not $dotnet) {
  throw ".NET SDK was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0."
}
if (-not (Test-Path -LiteralPath $dotnet)) {
  throw "Configured dotnet path was not found: $dotnet"
}

$info = & $dotnet --info
if ($info -match "No SDKs were found") {
  throw ".NET runtime is installed, but no SDK was found. Install the .NET 10 SDK to audit packages."
}

$totalVulnerabilities = 0
$totalOutdated = 0
$totalDeprecated = 0
foreach ($project in $projects) {
  if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
  }

  & $dotnet restore $project
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed for $project."
  }

  $jsonText = & $dotnet list $project package --vulnerable --include-transitive --format json
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet package vulnerability audit failed for $project."
  }

  $json = $jsonText | ConvertFrom-Json
  $count = Count-Vulnerabilities $json
  $totalVulnerabilities += $count
  if ($count -gt 0) {
    Write-Host "Vulnerable packages found in $project" -ForegroundColor Red
    $jsonText | Write-Host
  } else {
    Write-Host "No vulnerable packages found in $project" -ForegroundColor Green
  }

  $outdatedText = & $dotnet list $project package --outdated --format json
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet outdated package audit failed for $project."
  }
  $outdatedCount = Count-Properties ($outdatedText | ConvertFrom-Json) "latestVersion"
  $totalOutdated += $outdatedCount
  if ($outdatedCount -gt 0) {
    Write-Host "Outdated direct packages found in $project" -ForegroundColor Red
    $outdatedText | Write-Host
  }

  $deprecatedText = & $dotnet list $project package --deprecated --include-transitive --format json
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet deprecated package audit failed for $project."
  }
  $deprecatedCount = Count-Properties ($deprecatedText | ConvertFrom-Json) "deprecationReasons"
  $totalDeprecated += $deprecatedCount
  if ($deprecatedCount -gt 0) {
    Write-Host "Deprecated packages found in $project" -ForegroundColor Red
    $deprecatedText | Write-Host
  }
}

if ($totalVulnerabilities -gt 0) {
  throw "Package vulnerability audit failed: $totalVulnerabilities vulnerable package reference(s) found."
}
if ($totalOutdated -gt 0) {
  throw "Package currency audit failed: $totalOutdated outdated direct package reference(s) found."
}
if ($totalDeprecated -gt 0) {
  throw "Package deprecation audit failed: $totalDeprecated deprecated package reference(s) found."
}

Write-Host "Package audit passed: no known vulnerabilities, outdated direct packages, or deprecated dependencies." -ForegroundColor Green
