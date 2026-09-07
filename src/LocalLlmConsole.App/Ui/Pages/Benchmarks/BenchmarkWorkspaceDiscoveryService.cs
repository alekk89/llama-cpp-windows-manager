using LocalLlmConsole.Models;

namespace LocalLlmConsole;

public static class BenchmarkWorkspaceDiscoveryService
{
    public static async Task DiscoverAsync(BenchmarksPageState? page, Func<BenchmarksPageState?> currentPage, BenchmarkApplicationService benchmarks, RuntimeLaunchOptionDiscoveryService discovery, string wslDistro)
    {
        if (page?.Workspace is not { } workspace) return;
        var runtime = page.SelectedRuntime;
        if (runtime is null) { workspace.RuntimeOptions.SetStatus("Choose a runtime first."); return; }
        var direct = page.ExecutionMode?.SelectedItem is BenchmarkModeItem { Mode: BenchmarkExecutionMode.LlamaBench };
        workspace.RuntimeOptions.SetStatus($"Reading {runtime.Name} options…");
        try
        {
            IReadOnlyList<RuntimeLaunchOptionDefinition> options;
            if (direct)
            {
                var capability = (await benchmarks.RuntimeCapabilitiesAsync(runtime.Id, wslDistro)).Single();
                if (!capability.IsAvailable) throw new InvalidOperationException(capability.Error);
                options = capability.OptionDefinitions.Where(option => BenchmarkCommandBuilder.CanOfferAdditionalOption(option.Name)).ToArray();
            }
            else
                options = await discovery.DiscoverAsync(
                    new ViewModels.RuntimeChoice(runtime.Id, runtime.Name, runtime.Backend, runtime.Mode, runtime.ExecutablePath, runtime.Name), wslDistro);
            if (page == currentPage() && page.SelectedRuntime?.Id == runtime.Id
                && direct == (page.ExecutionMode?.SelectedItem is BenchmarkModeItem { Mode: BenchmarkExecutionMode.LlamaBench }))
                workspace.RuntimeOptions.SetOptions(options);
        }
        catch (Exception error)
        {
            if (page == currentPage()) workspace.RuntimeOptions.SetStatus(error.Message);
        }
    }
}
