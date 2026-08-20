namespace LocalLlmConsole.Services;

public static class RuntimeMetricIdentity
{
    public static string RuntimeKey(LoadedModelSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"{session.ModelId}|{session.RuntimeId}|{session.LaunchSettings.Port}";
    }
}
