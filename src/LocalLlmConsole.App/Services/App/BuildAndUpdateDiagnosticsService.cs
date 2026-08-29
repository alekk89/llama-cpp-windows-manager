using System.Reflection;

namespace LocalLlmConsole.Services;

public static class BuildAndUpdateDiagnosticsService
{
    public static BuildAndUpdateDiagnostics Capture(AppUpdateService appUpdates)
    {
        ArgumentNullException.ThrowIfNull(appUpdates);
        var assembly = Assembly.GetEntryAssembly() ?? typeof(BuildAndUpdateDiagnosticsService).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Value ?? "", StringComparer.OrdinalIgnoreCase);
        var executable = Environment.ProcessPath ?? "";
        var signatureStatus = SignatureStatus(executable);
        var lastUpdate = appUpdates.VerificationDiagnostics().LastOrDefault();
        return new BuildAndUpdateDiagnostics(
            metadata.GetValueOrDefault("RepositoryCommit", "unknown"),
            metadata.GetValueOrDefault("ReleaseChannel", "development"),
            Directory.Exists(Path.Combine(AppContext.BaseDirectory, "data")) ? "portable" : "installed",
            signatureStatus,
            metadata.Keys.Any(key => key.StartsWith("ReleaseManifestKey.", StringComparison.Ordinal)) ? "trust-keys-configured" : "no-trust-key",
            lastUpdate is null ? "not-recorded" : $"{lastUpdate.Code}:{lastUpdate.Outcome}");
    }

    private static string SignatureStatus(string executable)
        => AuthenticodeUpdateSignatureVerifier.InspectTrust(executable) switch
        {
            AuthenticodeTrustState.Valid => "valid",
            AuthenticodeTrustState.Invalid => "invalid",
            _ => "unsigned"
        };
}
