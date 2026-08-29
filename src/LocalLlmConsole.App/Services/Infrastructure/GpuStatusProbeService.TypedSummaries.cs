namespace LocalLlmConsole.Services;

public sealed partial class GpuStatusProbeService
{
    public async Task<AcceleratorProbeSummary> NvidiaAcceleratorsAsync(
        CancellationToken cancellationToken = default)
        => AcceleratorProbeSummary.Parse(await SummaryAsync(cancellationToken));

    public async Task<AcceleratorProbeSummary> NvidiaPowerAcceleratorsAsync(
        CancellationToken cancellationToken = default)
        => AcceleratorProbeSummary.Parse(await NvidiaPowerSummaryAsync(cancellationToken));

    public async Task<AcceleratorProbeSummary> AmdAcceleratorsAsync(
        CancellationToken cancellationToken = default)
        => AcceleratorProbeSummary.Parse(await AmdSmiSummaryAsync(cancellationToken));

    public async Task<AcceleratorProbeSummary> IntelAcceleratorsAsync(
        CancellationToken cancellationToken = default)
        => AcceleratorProbeSummary.Parse(await IntelXpuSmiSummaryAsync(cancellationToken));

    public async Task<AcceleratorProbeSummary> WindowsAcceleratorsAsync(
        CancellationToken cancellationToken = default)
        => AcceleratorProbeSummary.Parse(await WindowsSummaryAsync(cancellationToken));
}
