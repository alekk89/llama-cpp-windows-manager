using System.Text.RegularExpressions;

namespace LocalLlmConsole.Services;

public static partial class RuntimeOutOfMemoryClassifier
{
    public static bool IsOutOfMemory(string? statusReason, string? logTail)
    {
        var text = $"{statusReason}\n{logTail}";
        return !string.IsNullOrWhiteSpace(text) && OutOfMemoryPattern().IsMatch(text);
    }

    [GeneratedRegex(@"(?:out[ -]of[ -](?:device[ -])?memory|hipErrorOutOfMemory|VK_ERROR_OUT_OF_DEVICE_MEMORY|ZE_RESULT_ERROR_OUT_OF_DEVICE_MEMORY|insufficient (?:GPU |VRAM |device )?memory|failed to allocate[^\r\n]*(?:CUDA|GPU|VRAM|device|buffer))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutOfMemoryPattern();
}
