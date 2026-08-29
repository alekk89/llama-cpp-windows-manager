namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    private static long ReadModelSizeBytes(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}
