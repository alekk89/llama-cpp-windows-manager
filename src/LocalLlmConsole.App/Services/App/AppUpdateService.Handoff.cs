namespace LocalLlmConsole.Services;

public sealed partial class AppUpdateService
{
    public async Task StartInstallerAsync(AppUpdateInstallPlan plan, int currentProcessId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handoff = $@"Local\LLWM.Update.{Guid.NewGuid():N}";
        using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, handoff + ".ready");
        using var proceed = new EventWaitHandle(false, EventResetMode.AutoReset, handoff + ".proceed");
        using var failed = new EventWaitHandle(false, EventResetMode.AutoReset, handoff + ".failed");
        var psi = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(plan.TargetExe) ?? AppContext.BaseDirectory
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", plan.ScriptPath,
            "-HandoffName", handoff,
            "-ParentPid", currentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-SourceExe", plan.SourceExe,
            "-TargetExe", plan.TargetExe,
            "-ObsoleteExe", plan.ObsoleteExe,
            "-SourceCli", plan.SourceCli,
            "-TargetCli", plan.TargetCli,
            "-NoticeSource", Path.Combine(Path.GetDirectoryName(plan.ScriptPath) ?? "", "installed-update.json"),
            "-NoticeTarget", plan.NoticePath,
            "-WorkingDirectory", Path.GetDirectoryName(plan.TargetExe) ?? AppContext.BaseDirectory
        })
        {
            psi.ArgumentList.Add(arg);
        }
        AddOptionalArgument(psi, "-ExpectedSourceSha256", plan.SourceExeSha256);
        AddOptionalArgument(psi, "-ExpectedCliSha256", plan.SourceCliSha256);

        _startProcess(psi);
        var outcome = await Task.Run(() => WaitHandle.WaitAny(
            [ready, failed, cancellationToken.WaitHandle], TimeSpan.FromSeconds(30)), CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome != 0)
        {
            RecordDiagnostic("LLWM-UPDATE-HANDOFF", "rejected", "helper-did-not-acknowledge-staging");
            throw new InvalidOperationException("The update helper did not acknowledge verified staging. The Manager will remain open; check the update files and try again.");
        }
        proceed.Set();
        RecordDiagnostic("LLWM-UPDATE-HANDOFF", "success", "helper-acknowledged-staging");
    }

    private static void AddOptionalArgument(ProcessStartInfo psi, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        psi.ArgumentList.Add(name);
        psi.ArgumentList.Add(value);
    }

}
