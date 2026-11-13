namespace LocalLlmConsole.Services;

/// <summary>
/// Returned by an operation executor when the requested action was refused,
/// cancelled, or not applied. The operation endpoint reports it as
/// <c>ok: false</c> with HTTP 409 while keeping <see cref="Result"/> as the
/// operation's normal <c>result</c> payload.
/// </summary>
public sealed record ControlOperationFailure(string Outcome, string Error, object Result)
{
    public const int StatusCode = 409;

    public static ControlOperationFailure FromOutcome(string operation, string outcome, string? reason, object result)
        => new(
            outcome,
            string.IsNullOrWhiteSpace(reason)
                ? $"Operation '{operation}' did not complete (outcome: {outcome})."
                : $"Operation '{operation}' did not complete (outcome: {outcome}): {reason.Trim()}",
            result);

    /// <summary>Gateway stop always applies; a restart that leaves the gateway stopped is a failure.</summary>
    public static object GatewayResult(bool restart, bool applied, bool enabled, int port)
    {
        var result = new { action = restart ? "restart" : "stop", applied };
        return applied
            ? result
            : FromOutcome(
                restart ? "gateway.restart" : "gateway.stop",
                "NotStarted",
                $"The gateway is not running (enabled: {enabled}, port: {port}). Check the application status and log.",
                result);
    }
}

/// <summary>Remembers the latest status message an application workflow reported during one operation.</summary>
internal sealed class ControlOperationStatus
{
    private readonly Action<string> _forward;

    public ControlOperationStatus(Action<string> forward)
        => _forward = forward ?? throw new ArgumentNullException(nameof(forward));

    public string Last { get; private set; } = "";

    public void Set(string message)
    {
        Last = message ?? "";
        _forward(Last);
    }
}
