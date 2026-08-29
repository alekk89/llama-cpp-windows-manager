using System.Globalization;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static class BenchmarksPagePlanService
{
    public static void Apply(
        BenchmarksPageState page,
        BenchmarkPlan plan,
        IReadOnlyList<NamedModelLaunchProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(plan);
        if (page.Preset is not null) page.Preset.SelectedItem = "Custom";
        if (page.Name is not null) page.Name.Text = plan.Name;
        SelectMode(page.ExecutionMode, plan.ExecutionMode);
        SetBoolean(page.Warmup, plan.Warmup);
        Set(page.RepeatEquivalentProfiles, plan.RepeatEquivalentProfiles);
        Select(page.Model, plan.ModelIds.FirstOrDefault());
        page.SetProfileItems(profiles);
        Select(page.Profile, plan.ProfileIds.FirstOrDefault());
        Select(page.Runtime, plan.RuntimeIds.FirstOrDefault());
        page.ApplyScope(plan);
        Text(page.PromptSizes, Join(plan.PromptSizes));
        Text(page.GenerationSizes, Join(plan.GenerationSizes));
        Set(page.CompareContextSizes, plan.Serving.ContextSizes.Count > 0);
        PickerText(page.ContextSizes, Join(plan.Serving.ContextSizes));
        Text(page.PromptGenerationPairs, string.Join(',', plan.PromptGenerationPairs.Select(pair => $"{pair.PromptTokens}/{pair.GenerationTokens}")));
        Text(page.Depths, Join(plan.Depths));
        Text(page.Repetitions, plan.Repetitions.ToString(CultureInfo.InvariantCulture));
        Text(page.DelaySeconds, plan.DelaySeconds.ToString(CultureInfo.InvariantCulture));
        Text(page.Concurrencies, Join(plan.Serving.Concurrencies));
        Text(page.ReadyTimeoutSeconds, plan.Serving.ReadyTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        Text(page.RequestTimeoutSeconds, plan.Serving.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        SetBoolean(page.RequireSpeculativeMetrics, plan.Serving.RequireSpeculativeMetrics);
        Text(page.CooldownSeconds, plan.CooldownSeconds.ToString(CultureInfo.InvariantCulture));
        if (page.FailurePolicy is not null) page.FailurePolicy.SelectedItem = plan.FailurePolicy;
        Set(page.CompareThreads, plan.Options.Threads.Count > 0);
        PickerText(page.Threads, Join(plan.Options.Threads));
        Set(page.CompareBatchSizes, plan.Options.BatchSizes.Count > 0);
        PickerText(page.BatchSizes, Join(plan.Options.BatchSizes));
        Set(page.CompareMicroBatchSizes, plan.Options.MicroBatchSizes.Count > 0);
        PickerText(page.MicroBatchSizes, Join(plan.Options.MicroBatchSizes));
        Set(page.CompareGpuLayers, plan.Options.GpuLayers.Count > 0);
        PickerText(page.GpuLayers, Join(plan.Options.GpuLayers));
        Text(page.CpuMoeLayers, Join(plan.Options.CpuMoeLayers));
        Set(page.CompareFlashAttention, plan.Options.FlashAttention.Count > 0);
        PickerText(page.FlashAttention, Join(plan.Options.FlashAttention));
        var cacheTypesKv = plan.Options.CacheTypesKv.Count > 0
            ? plan.Options.CacheTypesKv
            : plan.Options.CacheTypesK.SequenceEqual(plan.Options.CacheTypesV, StringComparer.OrdinalIgnoreCase)
                ? plan.Options.CacheTypesK
                : [];
        Set(page.CompareCacheTypesK, cacheTypesKv.Count > 0);
        PickerText(page.CacheTypesK, Join(cacheTypesKv));
        Set(page.CompareKvOffload, plan.Options.KvOffload.Count > 0);
        PickerText(page.KvOffload, Join(plan.Options.KvOffload));
        var gpuConfigurations = plan.Options.GpuConfigurations.Count > 0
            ? plan.Options.GpuConfigurations
            : LegacyGpuConfigurations(plan.Options.SplitModes, plan.Options.TensorSplits);
        Set(page.CompareGpuConfigurations, gpuConfigurations.Count > 0);
        page.GpuConfigurations?.SetValues(gpuConfigurations);
        var speculativeConfigurations = plan.Serving.SpeculativeConfigurations.Count > 0
            ? plan.Serving.SpeculativeConfigurations
            : LegacySpeculativeConfigurations(plan.Serving.SpeculativeTypes, plan.Serving.SpeculativeCompanionModes);
        Set(page.CompareSpeculativeConfigurations, speculativeConfigurations.Count > 0);
        page.SpeculativeConfigurations?.SetValues(speculativeConfigurations);
        Text(page.MainGpus, Join(plan.Options.MainGpus));
        Text(page.Devices, Join(plan.Options.Devices));
        ComboText(page.LoadModes, Join(plan.Options.LoadModes));
        Text(page.FitTargetsMiB, Join(plan.Options.FitTargetsMiB));
        Text(page.FitContexts, Join(plan.Options.FitContexts));
        ComboText(page.NumaModes, Join(plan.Options.NumaModes));
        ComboText(page.Priorities, Join(plan.Options.Priorities));
        Text(page.CpuMasks, Join(plan.Options.CpuMasks));
        ComboText(page.CpuStrict, Join(plan.Options.CpuStrict));
        Text(page.PollValues, Join(plan.Options.PollValues));
        ComboText(page.Embeddings, Join(plan.Options.Embeddings));
        ComboText(page.NoOpOffload, Join(plan.Options.NoOpOffload));
        ComboText(page.NoHost, Join(plan.Options.NoHost));
        Text(page.TensorOverrides, Join(plan.Options.TensorOverrides));
        Text(page.AdditionalArguments, string.Join(Environment.NewLine, plan.Options.AdditionalArguments));
    }

    public static BenchmarkPlan Build(BenchmarksPageState page, string wslDistro)
    {
        ArgumentNullException.ThrowIfNull(page);
        var scope = page.ScopeRows;
        if (scope.Count == 0) throw new InvalidOperationException("Add at least one model profile to the benchmark scope.");
        return new BenchmarkPlan
        {
            Name = page.Name?.Text.Trim() is { Length: > 0 } name ? name : "Benchmark run",
            ExecutionMode = page.ExecutionMode?.SelectedItem is BenchmarkModeItem mode ? mode.Mode : BenchmarkExecutionMode.ProfileServing,
            AllModels = false,
            ModelIds = scope.Select(row => row.ModelId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AllProfiles = false,
            ProfileIds = scope.Select(row => row.ProfileId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AllRuntimes = false,
            RuntimeIds = scope.Where(row => !string.IsNullOrWhiteSpace(row.RuntimeId)).Select(row => row.RuntimeId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            UseProfileRuntime = scope.Any(row => string.IsNullOrWhiteSpace(row.RuntimeId)),
            ScopeSelections = scope.Select(row => new BenchmarkScopeSelection(row.ModelId, row.ProfileId, row.RuntimeId)).ToArray(),
            WslDistro = wslDistro,
            PromptSizes = ParseIntegerList(page.PromptSizes?.Text, "PP sizes", allowEmpty: true),
            GenerationSizes = ParseIntegerList(page.GenerationSizes?.Text, "TG sizes", allowEmpty: true),
            PromptGenerationPairs = ParsePairs(page.PromptGenerationPairs?.Text),
            Depths = ParseIntegerList(page.Depths?.Text, "depths", allowEmpty: false),
            Repetitions = ParseInteger(page.Repetitions?.Text, "repetitions"),
            Warmup = BooleanValue(page.Warmup, true),
            DelaySeconds = ParseInteger(page.DelaySeconds?.Text, "delay"),
            CooldownSeconds = ParseInteger(page.CooldownSeconds?.Text, "cooldown"),
            FailurePolicy = page.FailurePolicy?.SelectedItem is BenchmarkFailurePolicy policy ? policy : BenchmarkFailurePolicy.Stop,
            RepeatEquivalentProfiles = page.RepeatEquivalentProfiles?.IsChecked == true,
            StopActiveSessions = page.StopActiveSessions,
            PreventSystemSleep = page.PreventSystemSleep,
            Serving = new BenchmarkServingOptions
            {
                ContextSizes = page.CompareContextSizes?.IsChecked == true
                    ? ParseIntegerList(page.ContextSizes?.Text, "context sizes", false)
                    : [],
                SpeculativeConfigurations = page.CompareSpeculativeConfigurations?.IsChecked == true
                    ? page.SpeculativeConfigurations?.Values.ToArray() ?? []
                    : [],
                SpeculativeTypes = [],
                SpeculativeCompanionModes = [],
                Concurrencies = ParseIntegerList(page.Concurrencies?.Text, "concurrencies", false),
                ReadyTimeoutSeconds = ParseInteger(page.ReadyTimeoutSeconds?.Text, "ready timeout"),
                RequestTimeoutSeconds = ParseInteger(page.RequestTimeoutSeconds?.Text, "request timeout"),
                RequireSpeculativeMetrics = BooleanValue(page.RequireSpeculativeMetrics, true),
                Seed = 42,
                Temperature = 0
            },
            Options = new BenchmarkOptionSet
            {
                Threads = page.CompareThreads?.IsChecked == true
                    ? ParseIntegerList(page.Threads?.Text, "threads", false)
                    : [],
                BatchSizes = page.CompareBatchSizes?.IsChecked == true
                    ? ParseIntegerList(page.BatchSizes?.Text, "batch sizes", false)
                    : [],
                MicroBatchSizes = page.CompareMicroBatchSizes?.IsChecked == true
                    ? ParseIntegerList(page.MicroBatchSizes?.Text, "micro-batch sizes", false)
                    : [],
                GpuLayers = page.CompareGpuLayers?.IsChecked == true
                    ? ParseIntegerList(page.GpuLayers?.Text, "GPU layers", false)
                    : [],
                CpuMoeLayers = ParseIntegerList(page.CpuMoeLayers?.Text, "CPU MoE layers", true),
                FlashAttention = page.CompareFlashAttention?.IsChecked == true ? ParseStringList(PickerText(page.FlashAttention)) : [],
                CacheTypesKv = page.CompareCacheTypesK?.IsChecked == true ? ParseStringList(PickerText(page.CacheTypesK)) : [],
                KvOffload = page.CompareKvOffload?.IsChecked == true ? ParseStringList(PickerText(page.KvOffload)) : [],
                GpuConfigurations = page.CompareGpuConfigurations?.IsChecked == true
                    ? page.GpuConfigurations?.Values.ToArray() ?? []
                    : [],
                SplitModes = [],
                MainGpus = ParseIntegerList(page.MainGpus?.Text, "main GPUs", true),
                Devices = ParseStringList(page.Devices?.Text),
                TensorSplits = [],
                LoadModes = ParseStringList(ComboText(page.LoadModes)),
                FitTargetsMiB = ParseIntegerList(page.FitTargetsMiB?.Text, "fit targets", true),
                FitContexts = ParseIntegerList(page.FitContexts?.Text, "fit contexts", true),
                NumaModes = ParseStringList(ComboText(page.NumaModes)),
                Priorities = ParseIntegerList(ComboText(page.Priorities), "priorities", true),
                CpuMasks = ParseStringList(page.CpuMasks?.Text),
                CpuStrict = ParseStringList(ComboText(page.CpuStrict)),
                PollValues = ParseIntegerList(page.PollValues?.Text, "poll values", true),
                Embeddings = ParseStringList(ComboText(page.Embeddings)),
                NoOpOffload = ParseStringList(ComboText(page.NoOpOffload)),
                NoHost = ParseStringList(ComboText(page.NoHost)),
                TensorOverrides = ParseStringList(page.TensorOverrides?.Text),
                AdditionalArguments = ParseArgumentLines(page.AdditionalArguments?.Text)
            }
        };
    }

    private static void SelectMode(ComboBox? combo, BenchmarkExecutionMode mode)
    {
        if (combo is null) return;
        combo.SelectedItem = combo.Items.Cast<BenchmarkModeItem>().FirstOrDefault(item => item.Mode == mode);
    }

    private static IReadOnlyList<int> ParseIntegerList(string? value, string name, bool allowEmpty)
    {
        var parts = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 && allowEmpty) return [];
        if (parts.Length == 0) throw new InvalidOperationException($"Enter at least one {name} value.");
        var result = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new InvalidOperationException($"'{part}' is not a valid {name} value.");
            result.Add(parsed);
        }
        return result;
    }

    private static int ParseInteger(string? value, string name)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Enter a valid {name} value.");

    private static IReadOnlyList<BenchmarkPromptGenerationPair> ParsePairs(string? value)
    {
        var result = new List<BenchmarkPromptGenerationPair>();
        foreach (var pair in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var prompt)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
                throw new InvalidOperationException($"PG pair '{pair}' must use prompt/generation format, for example 4096/128.");
            result.Add(new BenchmarkPromptGenerationPair(prompt, generation));
        }
        return result;
    }

    private static IReadOnlyList<string> ParseStringList(string? value)
        => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<BenchmarkGpuConfiguration> LegacyGpuConfigurations(
        IReadOnlyList<string> modes,
        IReadOnlyList<string> splits)
    {
        if (modes.Count == 0) return [];
        if (splits.Count == 0)
            return modes.Select(mode => new BenchmarkGpuConfiguration(mode)).ToArray();
        if (modes.Count == splits.Count)
            return modes.Select((mode, index) => Pair(mode, splits[index])).ToArray();
        if (modes.Count == 1)
            return splits.Select(split => Pair(modes[0], split)).ToArray();
        if (splits.Count == 1)
            return modes.Select(mode => Pair(mode, splits[0])).ToArray();
        return modes.SelectMany(mode => splits.Select(split => Pair(mode, split))).ToArray();

        static BenchmarkGpuConfiguration Pair(string mode, string split)
            => new(mode, mode is "none" or "single" ? "" : split);
    }

    private static IReadOnlyList<BenchmarkSpeculativeConfiguration> LegacySpeculativeConfigurations(
        IReadOnlyList<string> types,
        IReadOnlyList<string> heads)
    {
        if (types.Count == 0) return [];
        if (heads.Count == 0)
            return types.Select(type => new BenchmarkSpeculativeConfiguration(type)).ToArray();
        if (types.Count == heads.Count)
            return types.Select((type, index) => new BenchmarkSpeculativeConfiguration(type, heads[index])).ToArray();
        if (types.Count == 1)
            return heads.Select(head => new BenchmarkSpeculativeConfiguration(types[0], head)).ToArray();
        if (heads.Count == 1)
            return types.Select(type => new BenchmarkSpeculativeConfiguration(type, heads[0])).ToArray();
        return types.SelectMany(type => heads.Select(head => new BenchmarkSpeculativeConfiguration(type, head))).ToArray();
    }

    private static IReadOnlyList<string> ParseArgumentLines(string? value)
        => (value ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Set(CheckBox? checkBox, bool value)
    {
        if (checkBox is not null) checkBox.IsChecked = value;
    }

    private static void SetBoolean(ComboBox? comboBox, bool value)
    {
        if (comboBox is null) return;
        comboBox.SelectedItem = comboBox.Items.Cast<BenchmarkBooleanItem>().FirstOrDefault(item => item.Value == value);
    }

    private static bool BooleanValue(ComboBox? comboBox, bool fallback)
        => comboBox?.SelectedItem is BenchmarkBooleanItem item ? item.Value : fallback;

    private static void Select(ComboBox? comboBox, string? id)
    {
        if (comboBox is null || string.IsNullOrWhiteSpace(id)) return;
        comboBox.SelectedItem = comboBox.ItemsSource?.Cast<BenchmarkSelectionItem>()
            .FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static void Text(TextBox? textBox, string value)
    {
        if (textBox is not null) textBox.Text = value;
    }

    private static void ComboText(ComboBox? comboBox, string value)
    {
        if (comboBox is not null) comboBox.Text = value;
    }

    private static string ComboText(ComboBox? comboBox) => comboBox?.Text ?? "";

    private static void PickerText(BenchmarkValuePicker? picker, string value)
    {
        if (picker is not null) picker.Text = value;
    }

    private static string PickerText(BenchmarkValuePicker? picker) => picker?.Text ?? "";

    private static string Join<T>(IEnumerable<T> values)
        => string.Join(',', values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
}
