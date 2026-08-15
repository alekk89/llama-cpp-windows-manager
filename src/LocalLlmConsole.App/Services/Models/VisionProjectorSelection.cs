namespace LocalLlmConsole.Services;

public static class VisionProjectorSelection
{
    public const string EmbeddedToken = "<embedded>";

    public static bool IsAuto(string? value)
        => string.IsNullOrWhiteSpace(value);

    public static bool IsEmbedded(string? value)
    {
        var normalized = (value ?? "").Trim();
        return string.Equals(normalized, EmbeddedToken, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "embedded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "model-bundled", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExternal(string? value)
        => !IsAuto(value) && !IsEmbedded(value);

    public static bool IsEmbeddedOrMainModel(string modelPath, string? value)
    {
        if (IsEmbedded(value)) return true;
        if (!IsExternal(value)) return false;

        try
        {
            return string.Equals(Path.GetFullPath(modelPath), Path.GetFullPath(value!.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string DisplayText(string? value)
    {
        if (IsEmbedded(value)) return "Embedded vision";
        if (IsAuto(value)) return "Auto-detect vision head";

        var fileName = Path.GetFileName(value!.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "External vision head" : fileName;
    }

    public static string Tooltip(string? value)
    {
        if (IsEmbedded(value))
            return "Omit --mmproj and use multimodal data bundled in the main GGUF. This is for compatible forks or specially packaged models; upstream llama.cpp normally uses a separate mmproj file.";
        if (IsAuto(value))
            return "Find a compatible mmproj/projector GGUF in the selected model's folder. Automatic discovery never scans parent or child folders.";

        return $"External vision head: {value!.Trim()}{Environment.NewLine}Click to change the vision head source.";
    }
}
