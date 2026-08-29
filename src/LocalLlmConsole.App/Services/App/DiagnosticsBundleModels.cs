namespace LocalLlmConsole.Services;

public sealed record DiagnosticProbeRecord(
    string Name,
    string Version,
    string Provider,
    bool Attempted,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string Classification,
    string ExitCodeCategory,
    string ParserResult,
    string StandardOutputExcerpt,
    string StandardErrorExcerpt,
    IReadOnlyDictionary<string, bool>? CapabilityFlags = null,
    string ToolVersion = "");

public sealed record SessionLifecycleDiagnosticEvent(
    string SessionId,
    string ModelId,
    string RuntimeId,
    string PreviousState,
    string NewState,
    DateTimeOffset TimestampUtc,
    string InitiatingActor,
    string ReasonCode,
    string ProcessExitCategory,
    string ReadinessResult,
    string StopVerificationResult);

public sealed record BuildAndUpdateDiagnostics(
    string BuildCommit,
    string ReleaseChannel,
    string InstallMode,
    string WindowsSignatureStatus,
    string ManifestVerificationStatus,
    string LastUpdateCheckResult);

public sealed record AppUpdateVerificationDiagnostic(
    DateTimeOffset TimestampUtc,
    string Code,
    string Outcome,
    string Message);

public static class DiagnosticErrorCodes
{
    public const string WindowsProbe = "LLWM-PROBE-WINDOWS";
    public const string WslProbe = "LLWM-PROBE-WSL";
    public const string GpuProbe = "LLWM-PROBE-GPU";
    public const string CpuProbe = "LLWM-PROBE-CPU";
    public const string SessionUnexpectedExit = "LLWM-SESSION-UNEXPECTED-EXIT";
    public const string SessionStopUnverified = "LLWM-SESSION-STOP-UNVERIFIED";
}

public sealed class DiagnosticProbeHistory
{
    public const int Capacity = 32;

    private readonly object _sync = new();
    private readonly Queue<DiagnosticProbeRecord> _records = new();

    public void Add(DiagnosticProbeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_sync)
        {
            _records.Enqueue(record);
            while (_records.Count > Capacity)
                _records.Dequeue();
        }
    }

    public IReadOnlyList<DiagnosticProbeRecord> Snapshot()
    {
        lock (_sync)
            return _records.ToArray();
    }
}
