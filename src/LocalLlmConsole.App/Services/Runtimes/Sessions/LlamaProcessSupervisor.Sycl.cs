namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor
{
    private static string WslSyclEnvironmentPrefix(RuntimeBackend backend)
        => backend == RuntimeBackend.Sycl
            ? "source /opt/intel/oneapi/setvars.sh --force >/dev/null 2>&1 || true; export ONEAPI_DEVICE_SELECTOR=level_zero:gpu; export ZES_ENABLE_SYSMAN=1; export SYCL_CACHE_PERSISTENT=1; export UR_L0_ENABLE_RELAXED_ALLOCATION_LIMITS=1; "
            : "";

    private static void ApplyNativeSyclEnvironment(ProcessStartInfo psi)
    {
        psi.Environment["ONEAPI_DEVICE_SELECTOR"] = "level_zero:gpu";
        psi.Environment["ZES_ENABLE_SYSMAN"] = "1";
        psi.Environment["SYCL_CACHE_PERSISTENT"] = "1";
        psi.Environment["UR_L0_ENABLE_RELAXED_ALLOCATION_LIMITS"] = "1";

        var oneApiPaths = WindowsEnvironmentService.OneApiPathEntries();
        if (oneApiPaths.Count == 0) return;
        var currentPath = psi.Environment.TryGetValue("PATH", out var path) ? path : Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = string.Join(Path.PathSeparator, oneApiPaths.Concat([currentPath]).Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
