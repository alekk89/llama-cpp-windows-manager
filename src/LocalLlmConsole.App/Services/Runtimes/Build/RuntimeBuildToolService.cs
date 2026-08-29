
namespace LocalLlmConsole.Services;

public static class RuntimeBuildToolService
{
    public static ProcessStartInfo CreateBuildProcessStartInfo(
        string powershellExe,
        string scriptPath,
        string sourceDir,
        string buildDir,
        string installDir,
        RuntimeBuildPreset preset,
        RuntimeMode mode,
        string wslDistro,
        string processMarker,
        string wslExe,
        string gitExe,
        string cmakeExe,
        bool noUpdate)
    {
        var psi = new ProcessStartInfo(powershellExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-RepoUrl", preset.RepoUrl })
            psi.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(preset.Branch))
        {
            psi.ArgumentList.Add("-Branch");
            psi.ArgumentList.Add(preset.Branch);
        }
        foreach (var arg in new[] { "-SourceDir", sourceDir, "-BuildDir", buildDir, "-InstallDir", installDir, "-Runtime", RuntimeBuildCatalogService.ModeKey(mode), "-Clean" })
            psi.ArgumentList.Add(arg);
        if (RuntimeBuildCatalogService.NormalizeBuildMode(mode) == RuntimeMode.Wsl)
        {
            foreach (var arg in new[] { "-WslDistro", wslDistro, "-ProcessMarker", processMarker, "-WslExe", wslExe })
                psi.ArgumentList.Add(arg);
        }
        else
        {
            foreach (var arg in new[] { "-GitExe", gitExe, "-CMakeExe", cmakeExe })
                psi.ArgumentList.Add(arg);
        }
        var backend = RuntimeBuildCatalogService.BuildBackend(preset);
        if (backend == RuntimeBackend.Cuda) psi.ArgumentList.Add("-Cuda");
        if (backend == RuntimeBackend.Vulkan) psi.ArgumentList.Add("-Vulkan");
        if (backend == RuntimeBackend.Sycl) psi.ArgumentList.Add("-Sycl");
        if (noUpdate) psi.ArgumentList.Add("-NoUpdate");
        return psi;
    }
}
