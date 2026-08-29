using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliBenchmarkWaiter
{
    internal static bool ShouldWait(Arguments args)
    {
        var group = args.Positionals.ElementAtOrDefault(0)?.ToLowerInvariant();
        var action = args.Positionals.ElementAtOrDefault(1)?.ToLowerInvariant();
        return group is "benchmarks" or "benchmark"
            && (action == "wait" || (action is "run" or "start" && args.Has("wait") && !args.Has("dry-run")));
    }

    internal static async Task<(string Text, int ExitCode)> WaitAsync(
        HttpClient http,
        string initialText,
        Arguments args)
    {
        var text = initialText;
        var timeoutSeconds = int.TryParse(args.Value("timeout"), out var parsedTimeout) ? parsedTimeout : 86_400;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 604_800));
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            using var document = JsonDocument.Parse(text);
            var run = FindProperty(document.RootElement, "run");
            var job = FindProperty(run, "job");
            var payload = FindProperty(run, "payload");
            var id = StringProperty(job, "id");
            var status = BenchmarkStatus(job);
            var revision = LongProperty(payload, "revision");
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Benchmark response did not contain a run id.");
            if (status is "Completed" or "Failed" or "Cancelled" or "Interrupted")
                return (text, status == "Completed" ? 0 : 2);
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException($"Timed out waiting for benchmark '{id}'. The Manager-owned run continues in the background.");
            var remaining = Math.Max(1, Math.Min(30, (int)(deadline - DateTimeOffset.UtcNow).TotalSeconds));
            using var response = await http.GetAsync($"api/v1/benchmarks/{Uri.EscapeDataString(id)}/wait?afterRevision={revision}&timeoutSeconds={remaining}");
            text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return (text, Math.Clamp((int)response.StatusCode, 1, 255));
        }
    }

    private static JsonElement FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string StringProperty(JsonElement element, string name)
    {
        var value = FindProperty(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static long LongProperty(JsonElement element, string name)
    {
        var value = FindProperty(element, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : -1;
    }

    private static string BenchmarkStatus(JsonElement job)
    {
        var value = FindProperty(job, "status");
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number switch
            {
                0 => "Queued",
                1 => "Running",
                2 => "Paused",
                3 => "Cancelled",
                4 => "Failed",
                5 => "Completed",
                6 => "Interrupted",
                _ => ""
            };
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
