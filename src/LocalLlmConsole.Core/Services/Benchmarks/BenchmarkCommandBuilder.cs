using System.Globalization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class BenchmarkCommandBuilder
{
    private static readonly HashSet<string> AppOwnedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "-m", "--model", "-o", "--output", "-oe", "--output-err", "--progress", "--offline",
        "-r", "--repetitions", "--delay", "--no-warmup"
    };
    private static readonly HashSet<string> ForbiddenOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "-rpc", "--rpc", "-hf", "-hfr", "--hf-repo", "-hff", "--hf-file", "-hft", "--hf-token"
    };
    private static readonly IReadOnlyDictionary<string, string> Aliases = AliasGroups()
        .SelectMany(group => group.Select(alias => (alias, canonical: group[0])))
        .ToDictionary(item => item.alias, item => item.canonical, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Build(BenchmarkPlan plan, BenchmarkWorkItem item, string runtimeVisibleModelPath)
    {
        var args = new List<string>
        {
            "--model", runtimeVisibleModelPath,
            "--offline",
            "--output", "jsonl",
            "--progress",
            "--repetitions", plan.Repetitions.ToString(CultureInfo.InvariantCulture),
            "--delay", plan.DelaySeconds.ToString(CultureInfo.InvariantCulture)
        };
        if (!plan.Warmup) args.Add("--no-warmup");
        AddValues(args, "--n-prompt", plan.PromptSizes, emptyValue: "0");
        AddValues(args, "--n-gen", plan.GenerationSizes, emptyValue: "0");
        foreach (var pair in plan.PromptGenerationPairs)
        {
            args.Add("-pg");
            args.Add($"{pair.PromptTokens.ToString(CultureInfo.InvariantCulture)},{pair.GenerationTokens.ToString(CultureInfo.InvariantCulture)}");
        }
        AddValues(args, "--n-depth", plan.Depths.Count > 0 ? plan.Depths : [0]);
        AddValues(args, "--threads", item.Options.Threads);
        AddValues(args, "--batch-size", item.Options.BatchSizes);
        AddValues(args, "--ubatch-size", item.Options.MicroBatchSizes);
        AddValues(args, "--n-gpu-layers", item.Options.GpuLayers);
        AddValues(args, "--n-cpu-moe", item.Options.CpuMoeLayers);
        AddValues(args, "--flash-attn", item.Options.FlashAttention);
        AddValues(args, "--cache-type-k", item.Options.CacheTypesK);
        AddValues(args, "--cache-type-v", item.Options.CacheTypesV);
        AddValues(args, "--no-kv-offload", item.Options.KvOffload.Select(value => value.Equals("off", StringComparison.OrdinalIgnoreCase) ? "1" : "0").ToArray());
        AddValues(args, "--split-mode", item.Options.SplitModes);
        AddValues(args, "--main-gpu", item.Options.MainGpus);
        AddValues(args, "--device", item.Options.Devices);
        AddValues(args, "--tensor-split", item.Options.TensorSplits);
        AddValues(args, "--load-mode", item.Options.LoadModes);
        AddValues(args, "--fit-target", item.Options.FitTargetsMiB);
        AddValues(args, "--fit-ctx", item.Options.FitContexts);
        AddValues(args, "--numa", item.Options.NumaModes);
        AddValues(args, "--prio", item.Options.Priorities);
        AddValues(args, "--cpu-mask", item.Options.CpuMasks);
        AddValues(args, "--cpu-strict", item.Options.CpuStrict);
        AddValues(args, "--poll", item.Options.PollValues);
        AddValues(args, "--embeddings", item.Options.Embeddings);
        AddValues(args, "--no-op-offload", item.Options.NoOpOffload);
        AddValues(args, "--no-host", item.Options.NoHost);
        AddValues(args, "--override-tensor", item.Options.TensorOverrides);
        ValidateAdditionalArguments(item.Options.AdditionalArguments);
        var structured = args.Where(IsOptionToken).Select(Canonical).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var token in item.Options.AdditionalArguments.Where(IsOptionToken))
            if (structured.Contains(Canonical(token)))
                throw new InvalidOperationException($"Benchmark option '{OptionName(token)}' conflicts with a structured Manager option.");
        args.AddRange(item.Options.AdditionalArguments);
        if (args.Sum(argument => argument.Length + 1) > 24_000)
            throw new InvalidOperationException("The expanded benchmark command is too long. Reduce matrix values or expert arguments.");
        return args;
    }

    public static string EffectiveSignature(BenchmarkPlan plan, BenchmarkEffectiveOptions options)
    {
        var parts = new List<string>
        {
            $"pp={Join(plan.PromptSizes)}", $"tg={Join(plan.GenerationSizes)}",
            $"pg={string.Join(';', plan.PromptGenerationPairs.Select(pair => $"{pair.PromptTokens},{pair.GenerationTokens}"))}",
            $"d={Join(plan.Depths)}", $"r={plan.Repetitions}", $"warmup={plan.Warmup}", $"delay={plan.DelaySeconds}",
            $"t={Join(options.Threads)}", $"b={Join(options.BatchSizes)}", $"ub={Join(options.MicroBatchSizes)}",
            $"ngl={Join(options.GpuLayers)}", $"fa={Join(options.FlashAttention)}", $"ctk={Join(options.CacheTypesK)}",
            $"ctv={Join(options.CacheTypesV)}", $"kvo={Join(options.KvOffload)}", $"sm={Join(options.SplitModes)}",
            $"mg={Join(options.MainGpus)}", $"dev={Join(options.Devices)}", $"ts={Join(options.TensorSplits)}",
            $"lm={Join(options.LoadModes)}", $"fit={Join(options.FitTargetsMiB)}", $"fitc={Join(options.FitContexts)}",
            $"numa={Join(options.NumaModes)}", $"prio={Join(options.Priorities)}", $"mask={Join(options.CpuMasks)}",
            $"strict={Join(options.CpuStrict)}", $"poll={Join(options.PollValues)}", $"embd={Join(options.Embeddings)}",
            $"nopo={Join(options.NoOpOffload)}", $"nohost={Join(options.NoHost)}", $"ot={Join(options.TensorOverrides)}",
            $"ncmoe={Join(options.CpuMoeLayers)}", $"extra={Join(options.AdditionalArguments)}"
        };
        return BenchmarkPlanService.StableHash(string.Join('|', parts));
    }

    public static void ValidateAdditionalArguments(IReadOnlyList<string> arguments)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (token.Contains('\0')) throw new InvalidOperationException("Benchmark arguments cannot contain null bytes.");
            if (!IsOptionToken(token)) continue;
            var name = OptionName(token);
            if (AppOwnedOptions.Contains(name))
                throw new InvalidOperationException($"Benchmark option '{name}' is owned by the Manager and cannot be overridden.");
            if (ForbiddenOptions.Contains(name))
                throw new InvalidOperationException($"Benchmark option '{name}' is outside the local benchmark safety boundary.");
            if (!seen.Add(Canonical(name)))
                throw new InvalidOperationException($"Benchmark option '{name}' duplicates or aliases another expert option.");
        }
    }

    private static string OptionName(string token) => token.Split('=', 2)[0];
    private static string Canonical(string token)
    {
        var name = OptionName(token);
        return Aliases.TryGetValue(name, out var canonical) ? canonical : name;
    }
    private static bool IsOptionToken(string token)
        => token.StartsWith("-", StringComparison.Ordinal)
           && !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static IReadOnlyList<string[]> AliasGroups() =>
    [
        ["--model", "-m"], ["--output", "-o"], ["--output-err", "-oe"], ["--repetitions", "-r"],
        ["--n-prompt", "-p"], ["--n-gen", "-n"], ["-pg"], ["--n-depth", "-d"],
        ["--batch-size", "-b"], ["--ubatch-size", "-ub"], ["--cache-type-k", "-ctk"],
        ["--cache-type-v", "-ctv"], ["--threads", "-t"], ["--cpu-mask", "-C"],
        ["--n-gpu-layers", "-ngl"], ["--n-cpu-moe", "-ncmoe"], ["--split-mode", "-sm"],
        ["--load-mode", "-lm"], ["--main-gpu", "-mg"], ["--no-kv-offload", "-nkvo"],
        ["--flash-attn", "-fa"], ["--device", "-dev"], ["--tensor-split", "-ts"],
        ["--fit-target", "-fitt"], ["--fit-ctx", "-fitc"], ["--rpc", "-rpc"],
        ["--embeddings", "-embd"], ["--override-tensor", "-ot"], ["--no-op-offload", "-nopo"]
    ];

    private static void AddValues<T>(ICollection<string> args, string option, IReadOnlyList<T> values, string? emptyValue = null)
    {
        if (values.Count == 0)
        {
            if (emptyValue is null) return;
            args.Add(option);
            args.Add(emptyValue);
            return;
        }
        args.Add(option);
        args.Add(string.Join(',', values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))));
    }

    private static string Join<T>(IEnumerable<T> values)
        => string.Join(',', values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
}
