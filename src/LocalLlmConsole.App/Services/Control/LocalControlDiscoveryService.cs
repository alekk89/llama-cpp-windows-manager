namespace LocalLlmConsole.Services;

public sealed class LocalControlDiscoveryService
{
    private readonly string _workspaceRoot;
    private readonly string _userLocatorPath;
    private readonly string _workspaceLocatorPath;

    public LocalControlDiscoveryService(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _workspaceLocatorPath = Path.Combine(_workspaceRoot, "state", "control.json");
        _userLocatorPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "llama.cpp Windows Manager",
            "control.json");
    }

    public IReadOnlyList<string> LocatorPaths => [_workspaceLocatorPath, _userLocatorPath];

    public void Publish(Uri baseUri, string sessionToken)
    {
        var document = new LocalControlDiscoveryDocument(
            Version: 1,
            ProcessId: Environment.ProcessId,
            BaseUrl: baseUri.ToString().TrimEnd('/'),
            ProtectedToken: SecretProtector.ProtectSetting(sessionToken),
            WorkspaceRoot: _workspaceRoot,
            StartedAt: DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });

        foreach (var path in LocatorPaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + $".{Environment.ProcessId}.tmp";
            File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
    }

    public void Remove()
    {
        foreach (var path in LocatorPaths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var document = JsonSerializer.Deserialize<LocalControlDiscoveryDocument>(File.ReadAllText(path));
                if (document?.ProcessId == Environment.ProcessId)
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not remove local control discovery file '{path}': {ex.Message}");
            }
        }
    }
}
