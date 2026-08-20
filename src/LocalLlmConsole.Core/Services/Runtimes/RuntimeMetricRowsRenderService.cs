namespace LocalLlmConsole.Services;

public sealed record RuntimeMetricRowsRenderPlan(
    IReadOnlyList<PrometheusSample> Samples,
    UiRow? LeadingRow);

public sealed class RuntimeMetricRowsRenderService
{
    public RuntimeMetricRowsRenderPlan FromSamples(IReadOnlyList<PrometheusSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return new RuntimeMetricRowsRenderPlan(samples, null);
    }

    public RuntimeMetricRowsRenderPlan Unavailable(string error, IReadOnlyList<PrometheusSample> lastKnownSamples)
    {
        ArgumentNullException.ThrowIfNull(lastKnownSamples);

        if (lastKnownSamples.Count > 0)
            return new RuntimeMetricRowsRenderPlan(lastKnownSamples, LastKnownStatusRow(error));

        return new RuntimeMetricRowsRenderPlan(
            [new PrometheusSample(
                "metrics_error",
                "",
                double.NaN,
                error,
                "error",
                "llama.cpp has not returned metrics yet.")],
            null);
    }

    private static UiRow LastKnownStatusRow(string error)
        => new()
        {
            C1 = "metrics_status",
            C2 = "",
            C3 = $"Last known values; refresh paused ({error})",
            C4 = "status",
            C5 = "The runtime did not return fresh metrics on the last poll."
        };
}
