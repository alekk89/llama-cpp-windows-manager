namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor : IDisposable
{
    internal ProcessStartInfo CreateProcessStartInfo(
        RuntimeRecord runtime,
        AppSettings settings,
        string executable,
        IReadOnlyList<string> arguments)
    {
        var startInfo = runtime.Mode == RuntimeMode.Wsl
            ? CreateWslStartInfo(runtime, settings, executable, arguments)
            : CreateNativeStartInfo(runtime, arguments);
        ConfigureSharedStartInfo(startInfo, runtime.Mode);
        if (runtime.Mode == RuntimeMode.Native)
            RuntimeVulkanEnvironment.ApplyNative(startInfo, runtime.Backend, settings.VulkanAllocationBlockSizeMiB);
        return startInfo;
    }

    private ProcessStartInfo CreateWslStartInfo(
        RuntimeRecord runtime,
        AppSettings settings,
        string executable,
        IReadOnlyList<string> arguments)
    {
        var executableDir = WslDirectoryName(executable);
        var runtimeLibDir = WslSiblingDirectory(executableDir, "lib");
        var libraryPath = string.IsNullOrWhiteSpace(executableDir)
            ? "$LD_LIBRARY_PATH"
            : $"{BashQuote(executableDir)}:{BashQuote(runtimeLibDir)}:${{LD_LIBRARY_PATH:-}}";
        var argv0 = string.IsNullOrWhiteSpace(_lastWslProcessMarker)
            ? ""
            : $" -a {BashQuote(_lastWslProcessMarker)}";
        var syclEnvironment = WslSyclEnvironmentPrefix(runtime.Backend);
        var command = RuntimeVulkanEnvironment.WslPrefix(runtime.Backend, settings.VulkanAllocationBlockSizeMiB)
                      + $"{syclEnvironment}export LD_LIBRARY_PATH={libraryPath}; "
                      + $"cd {BashQuote(string.IsNullOrWhiteSpace(executableDir) ? "/" : executableDir)}; "
                      + $"exec{argv0} {BashQuote(executable)} {string.Join(" ", arguments.Select(BashQuote))}";
        var startInfo = new ProcessStartInfo(HostExecutableResolver.WslExe());
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(settings.WslDistro);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private ProcessStartInfo CreateNativeStartInfo(
        RuntimeRecord runtime,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(runtime.ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(runtime.ExecutablePath) ?? Environment.CurrentDirectory
        };
        if (runtime.Backend == RuntimeBackend.Sycl)
            ApplyNativeSyclEnvironment(startInfo);

        // A job object guarantees that a native llama-server is terminated if the Manager exits unexpectedly.
        _jobObject?.Dispose();
        _jobObject = new ProcessJobObjectService();
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private void ConfigureSharedStartInfo(ProcessStartInfo startInfo, RuntimeMode mode)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        // Keep the API key out of process command lines visible through Task Manager or WMI.
        if (string.IsNullOrWhiteSpace(_lastApiKey)) return;
        startInfo.Environment["LLAMA_API_KEY"] = _lastApiKey;
        if (mode == RuntimeMode.Wsl)
            startInfo.Environment["WSLENV"] = "LLAMA_API_KEY";
    }

    private static string? ResolveMtpHeadPath(string modelPath, string configuredHeadPath, string speculativeType)
        => ModelCatalogService.ResolveMtpHeadPath(modelPath, configuredHeadPath, speculativeType);
}
