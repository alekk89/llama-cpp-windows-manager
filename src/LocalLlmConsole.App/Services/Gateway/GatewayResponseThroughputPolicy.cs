namespace LocalLlmConsole.Services;

public static class GatewayResponseThroughputPolicy
{
    public static double? Calculate(
        double? completionTokens,
        TimeSpan? timeToFirstData,
        TimeSpan responseDuration,
        string? mediaType)
    {
        if (completionTokens is not { } tokens || tokens < 0) return null;

        var isStreaming = string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        var activeDuration = isStreaming && timeToFirstData is { } firstData
            ? responseDuration - firstData
            : responseDuration;
        return activeDuration > TimeSpan.Zero ? tokens / activeDuration.TotalSeconds : null;
    }
}
