namespace LocalLlmConsole.Services;

public enum VramAdmissionDecision
{
    Allow,
    Warn,
    Block
}

public sealed record VramMemorySnapshot(double FreeGiB, double TotalGiB);

public sealed record VramAdmissionResult(VramAdmissionDecision Decision, string Message, double EstimatedRequiredGiB, double FreeGiB);

public sealed class VramAdmissionService
{
    private const double SafetyReserveGiB = 1.0;

    public VramAdmissionResult Assess(ModelRecord model, RuntimeRecord runtime, AppSettings settings, VramMemorySnapshot? memory)
    {
        if (runtime.Backend != RuntimeBackend.Cuda && runtime.Backend != RuntimeBackend.Vulkan && runtime.Backend != RuntimeBackend.Sycl)
            return new VramAdmissionResult(VramAdmissionDecision.Allow, "Runtime is not GPU-backed.", 0, memory?.FreeGiB ?? 0);

        if (settings.GpuLayers == 0)
            return new VramAdmissionResult(VramAdmissionDecision.Allow, "GPU layers are disabled for this launch profile.", 0, memory?.FreeGiB ?? 0);

        var estimate = EstimateRequiredGiB(model, settings);
        if (memory is null)
        {
            return new VramAdmissionResult(
                VramAdmissionDecision.Warn,
                $"Could not read free VRAM. Estimated requirement is about {estimate:0.0} GiB.",
                estimate,
                0);
        }

        var availableAfterReserve = Math.Max(0, memory.FreeGiB - SafetyReserveGiB);
        if (estimate > memory.FreeGiB)
        {
            return new VramAdmissionResult(
                VramAdmissionDecision.Block,
                $"Estimated VRAM requirement is {estimate:0.0} GiB, but only {memory.FreeGiB:0.0} GiB is free.",
                estimate,
                memory.FreeGiB);
        }

        if (estimate > availableAfterReserve)
        {
            return new VramAdmissionResult(
                VramAdmissionDecision.Warn,
                $"Estimated VRAM requirement is {estimate:0.0} GiB with {memory.FreeGiB:0.0} GiB free. This is inside the {SafetyReserveGiB:0.0} GiB safety reserve.",
                estimate,
                memory.FreeGiB);
        }

        return new VramAdmissionResult(
            VramAdmissionDecision.Allow,
            $"Estimated VRAM requirement is {estimate:0.0} GiB with {memory.FreeGiB:0.0} GiB free.",
            estimate,
            memory.FreeGiB);
    }

    public static double EstimateRequiredGiB(ModelRecord model, AppSettings settings)
    {
        var modelGiB = 0.0;
        try
        {
            if (File.Exists(model.ModelPath))
                modelGiB = new FileInfo(model.ModelPath).Length / 1024.0 / 1024.0 / 1024.0;
        }
        catch
        {
        }

        var offloadFactor = settings.GpuLayers < 0 || settings.GpuLayers >= AppSettings.DefaultGpuLayers
            ? 1.0
            : Math.Clamp(settings.GpuLayers / 80.0, 0.15, 1.0);
        var kvGiB = Math.Max(0.25, settings.ContextSize / 131072.0 * Math.Max(1, settings.ParallelSlots) * 0.75);
        return modelGiB * offloadFactor + kvGiB;
    }
}
