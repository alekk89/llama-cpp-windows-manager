param(
  [string] $RepoUrl = "https://github.com/ggml-org/llama.cpp.git",
  [string] $Branch = "",
  [Parameter(Mandatory = $true)]
  [string] $SourceDir,
  [Parameter(Mandatory = $true)]
  [string] $BuildDir,
  [Parameter(Mandatory = $true)]
  [string] $InstallDir,
  [ValidateSet("wsl", "native")]
  [string] $Runtime = "wsl",
  [string] $WslDistro = "Ubuntu-24.04",
  [string] $WslExe = "",
  [string] $GitExe = "",
  [string] $CMakeExe = "",
  [string] $ProcessMarker = "",
  [switch] $Cuda,
  [switch] $Vulkan,
  [switch] $Sycl,
  [switch] $Clean,
  [switch] $NoUpdate
)

$ErrorActionPreference = "Stop"
$BackendSwitchCount = @($Cuda, $Vulkan, $Sycl) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($BackendSwitchCount -gt 1) { throw "Choose only one of -Cuda, -Vulkan, or -Sycl." }

function ConvertTo-WslPath {
  param([string] $Path)
  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
  $normalized = $Path -replace '/', '\'
  if ($normalized -match '^([A-Za-z]):\\(.*)$') {
    $drive = $Matches[1].ToLowerInvariant()
    $rest = $Matches[2] -replace '\\', '/'
    return "/mnt/$drive/$rest"
  }
  return ($Path -replace '\\', '/')
}

function Quote-Bash {
  param([string] $Value)
  return "'" + ($Value -replace "'", "'\''") + "'"
}

function Normalize-Id {
  param([string] $Value)
  $text = $Value.ToLowerInvariant() -replace '[^a-z0-9_.-]+', '-'
  $text = $text.Trim('-')
  if ([string]::IsNullOrWhiteSpace($text)) { return "llama-cpp-runtime" }
  return $text
}

function Resolve-Executable {
  param([string] $PathOrName, [string] $FallbackName)
  $candidate = if ([string]::IsNullOrWhiteSpace($PathOrName)) { $FallbackName } else { $PathOrName }
  if ([System.IO.Path]::IsPathRooted($candidate)) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Executable not found: $candidate" }
    return $candidate
  }
  $pathValue = if ([string]::IsNullOrEmpty($env:PATH)) { "" } else { $env:PATH }
  foreach ($entry in ($pathValue -split [System.IO.Path]::PathSeparator)) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $expanded = [Environment]::ExpandEnvironmentVariables($entry.Trim().Trim('"'))
    if (-not [System.IO.Path]::IsPathRooted($expanded)) { continue }
    $path = Join-Path $expanded $candidate
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      return [System.IO.Path]::GetFullPath($path)
    }
  }
  throw "Executable not found on PATH: $candidate"
}

function Resolve-WslExe {
  param([string] $PathOrName)
  if (-not [string]::IsNullOrWhiteSpace($PathOrName)) {
    return Resolve-Executable $PathOrName "wsl.exe"
  }

  $candidates = @()
  if (-not [string]::IsNullOrWhiteSpace($env:SystemRoot)) {
    $candidates += (Join-Path $env:SystemRoot "System32\wsl.exe")
    $candidates += (Join-Path $env:SystemRoot "Sysnative\wsl.exe")
  }
  if (-not [string]::IsNullOrWhiteSpace($env:WINDIR)) {
    $candidates += (Join-Path $env:WINDIR "System32\wsl.exe")
    $candidates += (Join-Path $env:WINDIR "Sysnative\wsl.exe")
  }
  foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
      return [System.IO.Path]::GetFullPath($candidate)
    }
  }
  throw "wsl.exe was not found in the Windows system directory."
}

function Get-WslDistroNames {
  param([string] $ResolvedWslExe)
  try {
    $raw = & $ResolvedWslExe --list --quiet 2>$null
    return @($raw | ForEach-Object { ($_ -replace "`0", "").Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^(docker-desktop|docker-desktop-data)$' })
  } catch {
    return @()
  }
}

function Resolve-WslDistroName {
  param([string] $ResolvedWslExe, [string] $RequestedDistro)
  $requested = if ([string]::IsNullOrWhiteSpace($RequestedDistro)) { "Ubuntu-24.04" } else { $RequestedDistro.Trim() }
  $distros = Get-WslDistroNames $ResolvedWslExe
  if ($distros.Count -eq 0) { return $requested }
  if ($distros | Where-Object { $_ -ieq $requested } | Select-Object -First 1) { return $requested }
  if ($requested -ine "Ubuntu-24.04") { return $requested }

  foreach ($preferred in @("Ubuntu-24.04", "Ubuntu-22.04", "Ubuntu")) {
    $match = $distros | Where-Object { $_ -ieq $preferred } | Select-Object -First 1
    if ($match) { return $match }
  }
  $ubuntu = $distros | Where-Object { $_ -like "*Ubuntu*" } | Select-Object -First 1
  if ($ubuntu) { return $ubuntu }
  return $requested
}

function Redact-Argument {
  param([string] $Value)
  $uri = $null
  if ([System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri) -and -not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
    $builder = [System.UriBuilder]::new($uri)
    $builder.UserName = "redacted"
    $builder.Password = "redacted"
    return $builder.Uri.AbsoluteUri
  }
  return $Value
}

function Invoke-Logged {
  param([string] $FilePath, [string[]] $Arguments)
  $SafeArguments = ($Arguments | ForEach-Object { Redact-Argument $_ }) -join ' '
  Write-Host "> $(Redact-Argument $FilePath) $SafeArguments"
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$FilePath failed with exit code $LASTEXITCODE"
  }
}

function Invoke-GitLogged {
  param([string[]] $Arguments)
  Invoke-Logged $GitExe (@("-c", "core.longpaths=true") + $Arguments)
}

function Get-OneApiRootCandidates {
  $roots = @()
  if (-not [string]::IsNullOrWhiteSpace($env:ONEAPI_ROOT)) { $roots += $env:ONEAPI_ROOT }
  foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
    if ([string]::IsNullOrWhiteSpace($base)) { continue }
    $roots += (Join-Path $base "Intel\oneAPI")
  }
  return @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique)
}

function Get-OneApiPathEntries {
  $relative = @(
    "compiler\latest\bin",
    "compiler\latest\windows\bin",
    "mkl\latest\bin",
    "dnnl\latest\bin",
    "tbb\latest\bin"
  )
  $entries = @()
  foreach ($root in (Get-OneApiRootCandidates)) {
    foreach ($part in $relative) {
      $candidate = Join-Path $root $part
      if (Test-Path -LiteralPath $candidate -PathType Container) { $entries += $candidate }
    }
  }
  return @($entries | Select-Object -Unique)
}

function Find-OneApiSetvarsBat {
  foreach ($root in (Get-OneApiRootCandidates)) {
    $candidate = Join-Path $root "setvars.bat"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [System.IO.Path]::GetFullPath($candidate) }
  }
  return ""
}

function Import-OneApiEnvironment {
  $setvars = Find-OneApiSetvarsBat
  if ([string]::IsNullOrWhiteSpace($setvars)) {
    throw "Intel oneAPI setvars.bat was not found. Install Intel oneAPI Base Toolkit from the Windows page first."
  }
  Write-Host "> $setvars --force"
  $cmd = "call `"$setvars`" --force >nul && set"
  $output = & cmd.exe /s /c $cmd
  if ($LASTEXITCODE -ne 0) { throw "Intel oneAPI setvars.bat failed with exit code $LASTEXITCODE" }
  foreach ($line in $output) {
    $idx = $line.IndexOf("=")
    if ($idx -le 0) { continue }
    $name = $line.Substring(0, $idx)
    $value = $line.Substring($idx + 1)
    [Environment]::SetEnvironmentVariable($name, $value, [EnvironmentVariableTarget]::Process)
  }
}

function Resolve-OneApiExecutable {
  param([string[]] $Names)
  foreach ($entry in ((Get-OneApiPathEntries) + (($env:PATH -split [System.IO.Path]::PathSeparator) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }))) {
    $expanded = [Environment]::ExpandEnvironmentVariables($entry.Trim().Trim('"'))
    if (-not [System.IO.Path]::IsPathRooted($expanded)) { continue }
    foreach ($name in $Names) {
      $candidate = Join-Path $expanded $name
      if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [System.IO.Path]::GetFullPath($candidate) }
    }
  }
  return ""
}

function Initialize-NativeSyclToolchain {
  Import-OneApiEnvironment
  $icx = Resolve-OneApiExecutable @("icx.exe")
  $icpx = Resolve-OneApiExecutable @("icpx.exe", "icx.exe")
  $syclLs = Resolve-OneApiExecutable @("sycl-ls.exe")
  if ([string]::IsNullOrWhiteSpace($icx)) { throw "Intel oneAPI compiler icx.exe was not found after setvars.bat." }
  if ([string]::IsNullOrWhiteSpace($icpx)) { throw "Intel oneAPI DPC++ compiler was not found after setvars.bat." }
  if ([string]::IsNullOrWhiteSpace($syclLs)) { throw "sycl-ls.exe was not found after setvars.bat." }
  Write-Host "> $icpx --version"
  $compilerVersion = (& $icpx --version | Select-Object -First 1)
  if (-not [string]::IsNullOrWhiteSpace($compilerVersion)) { Write-Host $compilerVersion }
  Write-Host "> $syclLs"
  $syclOutput = & $syclLs 2>&1
  foreach ($line in $syclOutput) { Write-Host $line }
  if (-not ($syclOutput -match "level_zero.*gpu")) {
    throw "sycl-ls did not report a Level Zero GPU. Install/update the Intel Arc driver and Intel oneAPI runtime, then retry."
  }
  return [pscustomobject]@{
    CCompiler = $icx
    CxxCompiler = $icpx
  }
}

function Assert-SafeDirectoryRemovalTarget {
  param([string] $Path, [string] $Label)
  $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  $root = ([System.IO.Path]::GetPathRoot($full)).TrimEnd('\', '/')
  if ([string]::IsNullOrWhiteSpace($full) -or $full -ieq $root) {
    throw "Refusing to clean unsafe $Label path: $Path"
  }
  if (-not (Test-Path -LiteralPath $full)) { return }
  $item = Get-Item -LiteralPath $full -Force
  if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to clean $Label because it is a symlink or junction: $full"
  }
  $reparse = Get-ChildItem -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
    Select-Object -First 1
  if ($reparse) {
    throw "Refusing to clean $Label because it contains a symlink or junction: $($reparse.FullName)"
  }
}

function Test-AllowedGitSource {
  param([string] $Value)
  if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
  if (Test-Path -LiteralPath $Value -PathType Container) { return $true }
  $uri = $null
  if ([System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri)) {
    return @("https", "file", "ssh") -contains $uri.Scheme.ToLowerInvariant()
  }
  return $false
}

function Test-SafeGitRefName {
  param([string] $Value)
  if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
  if ($Value.StartsWith("-") -or $Value.Contains("..") -or $Value.EndsWith(".")) { return $false }
  return $Value -notmatch '[\x00-\x20~^:?*\[\\]'
}

if ($Runtime -eq "wsl") {
  $WslExe = Resolve-WslExe $WslExe
  $WslDistro = Resolve-WslDistroName $WslExe $WslDistro
} else {
  $GitExe = Resolve-Executable $GitExe "git.exe"
  $CMakeExe = Resolve-Executable $CMakeExe "cmake.exe"
  $NativeSyclToolchain = $null
  if ($Sycl) { $NativeSyclToolchain = Initialize-NativeSyclToolchain }
}
if (-not (Test-AllowedGitSource $RepoUrl)) { throw "Only HTTPS, SSH, file, or existing local Git repository sources are allowed." }
if (-not (Test-SafeGitRefName $Branch)) { throw "Unsafe Git branch/ref name: $Branch" }

$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)
$BuildDir = [System.IO.Path]::GetFullPath($BuildDir)
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
if ($Clean) { Assert-SafeDirectoryRemovalTarget $BuildDir "build directory" }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SourceDir), (Split-Path -Parent $BuildDir), (Split-Path -Parent $InstallDir) | Out-Null

if ($Runtime -eq "wsl") {
  $WslSource = ConvertTo-WslPath $SourceDir
  $WslBuild = ConvertTo-WslPath $BuildDir
  $WslInstall = ConvertTo-WslPath $InstallDir
  $Repo = Quote-Bash $RepoUrl
  $UseDefaultBranch = [string]::IsNullOrWhiteSpace($Branch)
  $BranchQ = if ($UseDefaultBranch) { "" } else { Quote-Bash $Branch }
  $SourceQ = Quote-Bash $WslSource
  $BuildQ = Quote-Bash $WslBuild
  $InstallQ = Quote-Bash $WslInstall
  $MarkerExport = if ([string]::IsNullOrWhiteSpace($ProcessMarker)) { ":" } else { "export LLAMA_CPP_WINDOWS_MANAGER_BUILD_MARKER=$(Quote-Bash $ProcessMarker); export LLAMA_CPP_CONSOLE_BUILD_MARKER=$(Quote-Bash $ProcessMarker); export LOCAL_LLM_CONSOLE_BUILD_MARKER=$(Quote-Bash $ProcessMarker)" }
  $CudaPreflight = if ($Cuda) {
@'
if command -v nvcc >/dev/null 2>&1; then
  cuda_nvcc=$(command -v nvcc)
elif [ -x /usr/local/cuda/bin/nvcc ]; then
  cuda_nvcc=/usr/local/cuda/bin/nvcc
else
  cuda_nvcc=
  for candidate in /usr/local/cuda*/bin/nvcc; do
    if [ -x "$candidate" ]; then
      cuda_nvcc="$candidate"
      break
    fi
  done
fi
if [ -n "$cuda_nvcc" ]; then
  "$cuda_nvcc" --version | head -n 4
  cuda_root=$(cd "$(dirname "$cuda_nvcc")/.." && pwd)
  export CUDA_HOME="$cuda_root"
  export CUDAToolkit_ROOT="$cuda_root"
  export PATH="$cuda_root/bin:$PATH"
  if [ -d "$cuda_root/targets/x86_64-linux/lib" ]; then
    export LD_LIBRARY_PATH="$cuda_root/targets/x86_64-linux/lib:${LD_LIBRARY_PATH:-}"
  fi
  cuda_cmake_args=(-DCUDAToolkit_ROOT="$cuda_root" -DCMAKE_CUDA_COMPILER="$cuda_nvcc")
else
  cuda_cmake_args=()
fi
cuda_lib=$(find /usr/local/cuda* /usr/lib /usr/lib/x86_64-linux-gnu -maxdepth 5 \( -name 'libcudart.so' -o -name 'libcudart.so.*' \) 2>/dev/null | head -n 1 || true)
if [ -z "$cuda_lib" ] && command -v ldconfig >/dev/null 2>&1; then
  cuda_lib=$(ldconfig -p | awk '/libcudart\.so/{print $NF; exit}')
fi
if [ -z "$cuda_nvcc" ] || [ -z "$cuda_lib" ]; then
  if command -v nvidia-smi >/dev/null 2>&1; then nvidia-smi -L 2>/dev/null || true; fi
  if [ -z "$cuda_nvcc" ]; then echo "CUDA compiler nvcc was not found inside this WSL distro." >&2; fi
  if [ -z "$cuda_lib" ]; then echo "CUDA runtime library libcudart was not found inside this WSL distro." >&2; fi
  echo "CPU build tools do not include CUDA. Use WSL Linux > Install CUDA, or install the NVIDIA CUDA Toolkit/runtime development packages in Ubuntu/WSL manually, then retry." >&2
  exit 2
fi
'@
  } else {
    ":"
  }
  $VulkanPreflight = if ($Vulkan) {
@'
missing_vulkan=()
if command -v glslc >/dev/null 2>&1; then
  vulkan_glslc=$(command -v glslc)
else
  vulkan_glslc=
  missing_vulkan+=("glslc")
fi
if command -v vulkaninfo >/dev/null 2>&1; then :; else missing_vulkan+=("vulkaninfo"); fi
if [ -f /usr/include/vulkan/vulkan.h ]; then :; else missing_vulkan+=("libvulkan-dev"); fi
if [ -d /usr/include/spirv ] || [ -d /usr/include/SPIRV ]; then :; else missing_vulkan+=("spirv-headers"); fi
vulkan_lib=$(ldconfig -p 2>/dev/null | awk '/libvulkan\.so/{print $NF; exit}')
if [ -z "$vulkan_lib" ] && [ -f /usr/lib/x86_64-linux-gnu/libvulkan.so ]; then
  vulkan_lib=/usr/lib/x86_64-linux-gnu/libvulkan.so
fi
if [ -z "$vulkan_lib" ]; then missing_vulkan+=("libvulkan.so"); fi
if [ ${#missing_vulkan[@]} -ne 0 ]; then
  echo "Vulkan build dependencies were not complete inside this WSL distro: ${missing_vulkan[*]}" >&2
  echo "Use WSL Linux > Install Vulkan, or install libvulkan-dev, glslc, spirv-headers, vulkan-tools, and a Vulkan driver/device manually, then retry." >&2
  exit 2
fi
if ! vulkaninfo --summary; then
  echo "Vulkan tools are installed, but vulkaninfo could not see a usable Vulkan driver/device inside this WSL distro." >&2
  echo "Install or update the Windows GPU driver with WSL Vulkan support, then retry." >&2
  exit 2
fi
vulkan_cmake_args=(-DVulkan_GLSLC_EXECUTABLE="$vulkan_glslc")
if [ -n "$vulkan_lib" ]; then vulkan_cmake_args+=(-DVulkan_LIBRARY="$vulkan_lib"); fi
'@
  } else {
    ":"
  }
  $SyclPreflight = if ($Sycl) {
@'
if [ -f /opt/intel/oneapi/setvars.sh ]; then
  source /opt/intel/oneapi/setvars.sh --force >/dev/null 2>&1 || true
fi
if ! command -v icx >/dev/null 2>&1; then
  echo "Intel oneAPI C compiler icx was not found. Use WSL Linux > Install oneAPI from the app." >&2
  exit 2
fi
if ! command -v icpx >/dev/null 2>&1; then
  echo "Intel oneAPI DPC++ compiler icpx was not found. Use WSL Linux > Install oneAPI from the app." >&2
  exit 2
fi
if ! command -v sycl-ls >/dev/null 2>&1; then
  echo "sycl-ls was not found after sourcing oneAPI environment." >&2
  exit 2
fi
icpx --version | head -n 1
if ! sycl-ls 2>/dev/null | grep -qi 'level_zero.*gpu'; then
  echo "No Level Zero Intel GPU device is visible to sycl-ls. Install Intel GPU runtime packages inside WSL and update the Intel graphics driver if needed." >&2
  exit 2
fi
sycl-ls
export ONEAPI_DEVICE_SELECTOR=level_zero:gpu
export ZES_ENABLE_SYSMAN=1
export SYCL_CACHE_PERSISTENT=1
export UR_L0_ENABLE_RELAXED_ALLOCATION_LIMITS=1
sycl_cmake_args=(-DCMAKE_C_COMPILER=icx -DCMAKE_CXX_COMPILER=icpx)
'@
  } else {
@'
sycl_cmake_args=()
'@
  }
  $Defs = @(
    "-DCMAKE_BUILD_TYPE=Release",
    "-DLLAMA_BUILD_SERVER=ON",
    "-DLLAMA_BUILD_TOOLS=ON",
    "-DLLAMA_TOOLS_INSTALL=ON",
    "-DLLAMA_BUILD_TESTS=OFF",
    "-DLLAMA_TESTS_INSTALL=OFF",
    "-DLLAMA_BUILD_EXAMPLES=OFF",
    "-DLLAMA_BUILD_APP=OFF",
    "-DCMAKE_INSTALL_PREFIX=$WslInstall"
  )
  if ($Cuda) { $Defs += "-DGGML_CUDA=ON" }
  if ($Vulkan) { $Defs += "-DGGML_VULKAN=ON" }
  if ($Sycl) { $Defs += @("-DGGML_SYCL=ON", "-DGGML_SYCL_F16=ON") }
  $DefArgs = ($Defs | ForEach-Object { Quote-Bash $_ }) -join " "
  $CleanBlock = if ($Clean) { "rm -rf $BuildQ" } else { ":" }
  $CloneBlock = if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    "test -d $SourceQ || { echo 'SourceDir does not exist and RepoUrl is empty.' >&2; exit 2; }"
  } elseif ($UseDefaultBranch) {
    "if [ ! -d $SourceQ ]; then git clone --depth 1 $Repo $SourceQ; fi"
  } else {
    "if [ ! -d $SourceQ ]; then git clone --depth 1 --branch $BranchQ $Repo $SourceQ; fi"
  }
  $UpdateBlock = if ($NoUpdate -or [string]::IsNullOrWhiteSpace($RepoUrl)) {
    ":"
  } elseif ($UseDefaultBranch) {
    "if [ -d $SourceQ/.git ]; then git -C $SourceQ fetch --all --tags && git -C $SourceQ pull --ff-only; fi"
  } else {
    "if [ -d $SourceQ/.git ]; then git -C $SourceQ fetch --all --tags && git -C $SourceQ checkout $BranchQ && git -C $SourceQ pull --ff-only; fi"
  }

$Script = @"
set -e
$MarkerExport
$CudaPreflight
$VulkanPreflight
$SyclPreflight
$CloneBlock
$UpdateBlock
$CleanBlock
mkdir -p $BuildQ $InstallQ
generator_args=()
if command -v ninja >/dev/null 2>&1; then
  generator_args=(-G Ninja)
elif command -v ninja-build >/dev/null 2>&1; then
  generator_args=(-G Ninja -DCMAKE_MAKE_PROGRAM="`$(command -v ninja-build)")
fi
if [ `${#generator_args[@]} -gt 0 ]; then echo "Using CMake generator: Ninja"; fi
cmake -S $SourceQ -B $BuildQ "`${generator_args[@]}" $DefArgs "`${cuda_cmake_args[@]}" "`${vulkan_cmake_args[@]}" "`${sycl_cmake_args[@]}"
build_status=0
cmake --build $BuildQ --config Release --target install --parallel `$(nproc) || build_status=`$?
server_path=$InstallQ/bin/llama-server
bench_path=$InstallQ/bin/llama-bench
server_lib=$InstallQ/lib
if [ ! -f "`$server_path" ]; then
  echo "Build finished but llama-server was not installed at `$server_path." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
if [ ! -f "`$bench_path" ]; then
  echo "Build finished but llama-bench was not installed at `$bench_path." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
probe_ld_path="`$server_lib:`${LD_LIBRARY_PATH:-}"
if ! LD_LIBRARY_PATH="`$probe_ld_path" "`$server_path" --version >/dev/null 2>&1; then
  echo "Build finished but installed llama-server failed a startup smoke test." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
if ! LD_LIBRARY_PATH="`$probe_ld_path" "`$bench_path" --help >/dev/null 2>&1; then
  echo "Build finished but installed llama-bench failed a startup smoke test." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
if [ "`$build_status" -ne 0 ]; then
  echo "Build command returned exit code `$build_status after installing and validating llama-server; continuing." >&2
fi
"@
  New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
  $BuildScriptPath = Join-Path $BuildDir ".local-llm-build.sh"
  $BuildScriptText = ($Script -replace "`r`n", "`n") -replace "`r", "`n"
  [System.IO.File]::WriteAllText($BuildScriptPath, $BuildScriptText, (New-Object System.Text.UTF8Encoding $false))
  $WslBuildScript = ConvertTo-WslPath $BuildScriptPath
  $BuildExitCode = 0
  try {
    Write-Host "> $(Redact-Argument $WslExe) -d $WslDistro -- bash <build>"
    & $WslExe -d $WslDistro -- bash $WslBuildScript
    $BuildExitCode = $LASTEXITCODE
  } finally {
    Remove-Item -LiteralPath $BuildScriptPath -Force -ErrorAction SilentlyContinue
  }
  if ($BuildExitCode -ne 0) {
    [Console]::Error.WriteLine("WSL build failed with exit code $BuildExitCode")
    exit $BuildExitCode
  }
  $Commit = (& $WslExe -d $WslDistro -- bash -lc "git -C $(Quote-Bash $WslSource) rev-parse --short=12 HEAD 2>/dev/null || echo latest").Trim()
  $RuntimePath = (Join-Path $InstallDir "bin") -replace '\\', '/'
  $WslRuntimePath = "$WslInstall/bin"
} else {
  if (-not (Test-Path -LiteralPath $SourceDir)) {
    if ([string]::IsNullOrWhiteSpace($RepoUrl)) { throw "SourceDir does not exist and RepoUrl is empty." }
    $CloneArgs = @("clone", "--depth", "1")
    if (-not [string]::IsNullOrWhiteSpace($Branch)) { $CloneArgs += @("--branch", $Branch) }
    $CloneArgs += @($RepoUrl, $SourceDir)
    Invoke-GitLogged $CloneArgs
  }
  if (-not $NoUpdate -and -not [string]::IsNullOrWhiteSpace($RepoUrl) -and (Test-Path -LiteralPath (Join-Path $SourceDir ".git"))) {
    Invoke-GitLogged @("-C", $SourceDir, "fetch", "--all", "--tags")
    if (-not [string]::IsNullOrWhiteSpace($Branch)) { Invoke-GitLogged @("-C", $SourceDir, "checkout", $Branch) }
    Invoke-GitLogged @("-C", $SourceDir, "pull", "--ff-only")
  }
  if ($Clean -and (Test-Path -LiteralPath $BuildDir)) {
    Remove-Item -LiteralPath $BuildDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $BuildDir, $InstallDir | Out-Null
  $Defs = @(
    "-DCMAKE_BUILD_TYPE=Release",
    "-DLLAMA_BUILD_SERVER=ON",
    "-DLLAMA_BUILD_TOOLS=ON",
    "-DLLAMA_TOOLS_INSTALL=ON",
    "-DLLAMA_BUILD_TESTS=OFF",
    "-DLLAMA_TESTS_INSTALL=OFF",
    "-DLLAMA_BUILD_EXAMPLES=OFF",
    "-DLLAMA_BUILD_APP=OFF",
    "-DCMAKE_INSTALL_PREFIX=$InstallDir"
  )
  if ($Cuda) { $Defs += "-DGGML_CUDA=ON" }
  if ($Vulkan) { $Defs += "-DGGML_VULKAN=ON" }
  if ($Sycl) {
    $Defs += @(
      "-DGGML_SYCL=ON",
      "-DGGML_SYCL_F16=ON",
      "-DCMAKE_C_COMPILER=$($NativeSyclToolchain.CCompiler)",
      "-DCMAKE_CXX_COMPILER=$($NativeSyclToolchain.CxxCompiler)"
    )
  }
  $ConfigureArgs = @("-S", $SourceDir, "-B", $BuildDir) + $Defs
  Invoke-Logged $CMakeExe $ConfigureArgs
  Invoke-Logged $CMakeExe @("--build", $BuildDir, "--config", "Release", "--target", "install", "--parallel", [string][Environment]::ProcessorCount)
  $ServerExe = Join-Path $InstallDir "bin\llama-server.exe"
  if (-not (Test-Path -LiteralPath $ServerExe)) { throw "Build finished but llama-server.exe was not found at $ServerExe" }
  $BenchmarkExe = Join-Path $InstallDir "bin\llama-bench.exe"
  if (-not (Test-Path -LiteralPath $BenchmarkExe)) { throw "Build finished but llama-bench.exe was not found at $BenchmarkExe" }
  Invoke-Logged $ServerExe @("--version")
  Invoke-Logged $BenchmarkExe @("--help")
  try {
    $Commit = (& $GitExe -c core.longpaths=true -C $SourceDir rev-parse --short=12 HEAD 2>$null).Trim()
  } catch {
    $Commit = "latest"
  }
  if ([string]::IsNullOrWhiteSpace($Commit)) { $Commit = "latest" }
  $RuntimePath = (Join-Path $InstallDir "bin") -replace '\\', '/'
  $WslRuntimePath = ""
}

$Flavor = if ($Cuda) { "cuda" } elseif ($Vulkan) { "vulkan" } elseif ($Sycl) { "sycl" } else { "cpu" }
$Id = Normalize-Id "llama-cpp-$Commit-$Runtime-$Flavor"
$Metadata = [ordered]@{
  id = $Id
  name = "llama.cpp $Commit $Runtime $Flavor"
  runtime = $Runtime
  wslDistro = if ($Runtime -eq "wsl") { $WslDistro } else { "" }
  path = $RuntimePath
  wslPath = $WslRuntimePath
  repoUrl = $RepoUrl
  branch = $Branch
  sourcePath = ($SourceDir -replace '\\', '/')
  buildPath = ($BuildDir -replace '\\', '/')
  commit = $Commit
  build = "$Runtime-$Flavor"
  versionText = $Commit
  tags = @("built", $Runtime, $Flavor)
  requiredFlags = @()
}

$MetadataPath = Join-Path $InstallDir "local-llm-runtime.json"
$Metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $MetadataPath -Encoding UTF8
Write-Host "Wrote runtime metadata: $MetadataPath"
