using System.Globalization;
using System.Text.RegularExpressions;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static partial class ProfileFitOutputParser
{
    public static ProfileFitResult Parse(ProfileFitRequest request, string stdout, string stderr)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokens = ShellArgumentTokenizer.Tokenize(LastArgumentLine(stdout), ShellTokenizationMode.StrictArguments);
        var context = ReadInt(tokens, "-c", "--ctx-size");
        var gpuLayers = ReadInt(tokens, "-ngl", "--n-gpu-layers");
        var split = ReadString(tokens, "-ts", "--tensor-split");
        var overrides = ReadString(tokens, "-ot", "--override-tensor");
        if (context is null || gpuLayers is null)
            return Failed(stdout, stderr, "llama-fit-params did not return context and GPU-layer arguments.");

        var proposedContext = Math.Min(context.Value, request.DesiredMaximumContext);
        if (proposedContext < request.MinimumContext)
            return Failed(stdout, stderr, $"The model only fits at context {proposedContext:N0}, below the requested minimum of {request.MinimumContext:N0}.");

        var warnings = new List<string>();
        if (proposedContext < request.CurrentProfile.ContextSize) warnings.Add("Context was reduced.");
        if (gpuLayers.Value < request.CurrentProfile.GpuLayers) warnings.Add("Some model layers will run on the CPU.");
        if (!string.IsNullOrWhiteSpace(overrides)) warnings.Add("Some tensors will use alternate buffers and may run more slowly.");
        warnings.Add("Available VRAM can change while other GPU applications are running.");

        return new ProfileFitResult(
            true,
            new ProfileFitProposal(proposedContext, gpuLayers.Value, split, overrides),
            ParseDeviceEstimates(stderr),
            warnings,
            LastArgumentLine(stdout),
            stderr.Trim(),
            "");
    }

    private static ProfileFitResult Failed(string stdout, string stderr, string error)
        => new(false, null, ParseDeviceEstimates(stderr), [], LastArgumentLine(stdout), stderr.Trim(), error);

    private static string LastArgumentLine(string value)
        => (value ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.Contains("-c", StringComparison.Ordinal) && line.Contains("-ngl", StringComparison.Ordinal)) ?? "";

    private static int? ReadInt(IReadOnlyList<string> tokens, params string[] names)
    {
        var value = ReadString(tokens, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string ReadString(IReadOnlyList<string> tokens, params string[] names)
    {
        for (var i = 0; i + 1 < tokens.Count; i++)
            if (names.Contains(tokens[i], StringComparer.OrdinalIgnoreCase)) return tokens[i + 1].Trim();
        return "";
    }

    private static IReadOnlyList<ProfileFitDeviceEstimate> ParseDeviceEstimates(string stderr)
        => DeviceMemoryPattern().Matches(stderr ?? "")
            .Select(match => new ProfileFitDeviceEstimate(
                match.Groups[1].Value.Trim(),
                long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .GroupBy(item => item.Device, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    [GeneratedRegex(@"(?im)^.*?((?:CUDA|Vulkan|SYCL|Metal|HIP|MUSA)\d+)\b.*?([0-9]+)\s+MiB\s+used,\s+([0-9]+)\s+MiB\s+free")]
    private static partial Regex DeviceMemoryPattern();
}
