namespace LocalLlmConsole.Services;

public sealed record ControlAdmissionIdentity(
    bool Identified,
    string SessionId = "",
    string ModelId = "",
    string ModelName = "",
    string RuntimeId = "",
    string RuntimeName = "",
    RuntimeMode? Mode = null,
    string Confidence = "unknown",
    string MatchedBy = "")
{
    public static ControlAdmissionIdentity Unknown { get; } = new(false);

    public static ControlAdmissionIdentity FromSession(
        LoadedModelSessionSnapshot session,
        string confidence = "explicit",
        string matchedBy = "session")
        => new(
            true,
            session.SessionId,
            session.ModelId,
            session.ModelName,
            session.RuntimeId,
            session.RuntimeName,
            session.Mode,
            confidence,
            matchedBy);
}

public sealed record ControlAdmissionContext(
    bool EnforceSelfSafety,
    ControlAdmissionIdentity Identity,
    bool AllowSelfStop = false,
    bool RequireIdentityForDestructiveRequests = true)
{
    public static ControlAdmissionContext ExternalClient { get; } = new(false, ControlAdmissionIdentity.Unknown);

    public static ControlAdmissionContext Visual(
        ControlAdmissionIdentity? identity,
        bool allowSelfStop = false)
        => new(true, identity ?? ControlAdmissionIdentity.Unknown, allowSelfStop);
}

public sealed class ControlRequestAdmissionService
{
    private readonly Func<string, CancellationToken, Task<string>> _resolveModelIdAsync;

    public ControlRequestAdmissionService(LocalControlDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _resolveModelIdAsync = async (identifier, _) =>
        {
            var models = await dependencies.StateStore.ListModelsAsync();
            return ModelGatewayRequestResolver.ResolveModel(models, identifier)?.Id ?? identifier;
        };
    }

    public ControlRequestAdmissionService(Func<string, CancellationToken, Task<string>> resolveModelIdAsync)
        => _resolveModelIdAsync = resolveModelIdAsync ?? throw new ArgumentNullException(nameof(resolveModelIdAsync));

    public async Task EnsureAllowedAsync(
        LocalControlRequest request,
        ControlAdmissionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.EnforceSelfSafety || context.AllowSelfStop) return;

        var consequence = await ConsequenceAsync(request, context.Identity, cancellationToken);
        if (!consequence.Destructive || consequence.DryRun) return;
        if (!context.Identity.Identified)
        {
            if (!context.RequireIdentityForDestructiveRequests) return;
            throw new ControlAdmissionException(
                "The current agent session has not been identified. Identify or select the protected session before this destructive request, or explicitly authorize self-stop once.");
        }

        if (!consequence.StopsProtectedSession) return;
        throw new ControlAdmissionException(
            $"Refusing {consequence.Description} because it can stop the current agent session " +
            $"'{context.Identity.ModelName}' ({context.Identity.SessionId}). Explicitly authorize self-stop once only when that consequence is intended.");
    }

    private async Task<ControlRequestConsequence> ConsequenceAsync(
        LocalControlRequest request,
        ControlAdmissionIdentity identity,
        CancellationToken cancellationToken)
    {
        var method = request.Method.ToUpperInvariant();
        var segments = request.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length < 3
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase))
            return ControlRequestConsequence.Safe;

        if (segments[2].Equals("operations", StringComparison.OrdinalIgnoreCase)
            && method == "POST" && segments.Length == 4)
            return OperationConsequence(segments[3], request.Body, identity);

        if (!segments[2].Equals("models", StringComparison.OrdinalIgnoreCase) || segments.Length < 4)
            return ControlRequestConsequence.Safe;

        var action = method == "DELETE" && segments.Length == 4
            ? "delete"
            : method == "POST" && segments.Length == 5
                ? segments[4].ToLowerInvariant()
                : "";
        var unloadOthers = request.Body?["unloadOthers"]?.GetValue<bool>() ?? false;
        var restart = action == "restart" || (action == "load" && (request.Body?["restart"]?.GetValue<bool>() ?? false));
        var destructive = action is "delete" or "unload" || restart || unloadOthers;
        if (!destructive) return ControlRequestConsequence.Safe;

        cancellationToken.ThrowIfCancellationRequested();
        var target = await _resolveModelIdAsync(segments[3], cancellationToken);
        var targetsProtectedModel = identity.Identified
            && target.Equals(identity.ModelId, StringComparison.OrdinalIgnoreCase);
        var stopsProtected = action is "delete" or "unload" || restart
            ? targetsProtectedModel
            : unloadOthers && !targetsProtectedModel;
        return new(true, false, stopsProtected, $"model {action}");
    }

    private static ControlRequestConsequence OperationConsequence(
        string operation,
        JsonObject? body,
        ControlAdmissionIdentity identity)
    {
        var dryRun = body?["dryRun"]?.GetValue<bool>() ?? false;
        var name = operation.ToLowerInvariant();
        var destructive = name is "app.shutdown" or "updates.install" or "runtime.delete"
            || name == "wsl.setup" && (body?["action"]?.ToString().StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!destructive) return ControlRequestConsequence.Safe;

        var stopsProtected = name switch
        {
            "app.shutdown" or "updates.install" => identity.Identified,
            "runtime.delete" => identity.Identified && RuntimeMatches(identity, body?["runtime"]?.ToString()),
            "wsl.setup" => identity.Identified && identity.Mode == RuntimeMode.Wsl,
            _ => false
        };
        return new(true, dryRun, stopsProtected, $"operation '{operation}'");
    }

    private static bool RuntimeMatches(ControlAdmissionIdentity identity, string? value)
        => !string.IsNullOrWhiteSpace(value)
            && (identity.RuntimeId.Equals(value, StringComparison.OrdinalIgnoreCase)
                || identity.RuntimeName.Equals(value, StringComparison.OrdinalIgnoreCase));

    private sealed record ControlRequestConsequence(
        bool Destructive,
        bool DryRun,
        bool StopsProtectedSession,
        string Description)
    {
        public static ControlRequestConsequence Safe { get; } = new(false, false, false, "request");
    }
}

public sealed class ControlAdmissionException : InvalidOperationException
{
    public ControlAdmissionException(string message) : base(message)
    {
    }
}
