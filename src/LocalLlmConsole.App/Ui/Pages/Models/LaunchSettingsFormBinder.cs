using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class LaunchSettingsFormControls
{
    private readonly IReadOnlyDictionary<string, FrameworkElement> _editors;

    public LaunchSettingsFormControls(
        IReadOnlyDictionary<string, FrameworkElement>? editors = null,
        LaunchRuntimeOptionsPanel? runtimeOptions = null)
    {
        _editors = editors ?? new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        RuntimeOptions = runtimeOptions;
    }

    public LaunchRuntimeOptionsPanel? RuntimeOptions { get; }

    public WpfTextBox? LaunchPortBox => Text(nameof(AppSettings.Port));
    public WpfTextBox? ContextSizeBox => Text(nameof(AppSettings.ContextSize));
    public WpfTextBox? GpuLayersBox => Text(nameof(AppSettings.GpuLayers));
    public WpfTextBox? GpuDevicesBox => Text(nameof(AppSettings.GpuDevices));
    public WpfTextBox? GpuSplitBox => Text(nameof(AppSettings.GpuSplit));
    public WpfTextBox? ParallelSlotsBox => Text(nameof(AppSettings.ParallelSlots));
    public WpfTextBox? BatchSizeBox => Text(nameof(AppSettings.BatchSize));
    public WpfTextBox? MicroBatchSizeBox => Text(nameof(AppSettings.MicroBatchSize));
    public WpfTextBox? ThreadsBox => Text(nameof(AppSettings.Threads));
    public WpfTextBox? ReasoningBudgetBox => Text(nameof(AppSettings.ReasoningBudget));
    public WpfTextBox? ReasoningBudgetMessageBox => Text(nameof(AppSettings.ReasoningBudgetMessage));
    public WpfTextBox? VisionProjectorPathBox => Text(nameof(AppSettings.VisionProjectorPath));
    public WpfButton? VisionProjectorButton => Button(nameof(AppSettings.VisionProjectorPath));
    public WpfTextBox? VisionImageMinTokensBox => Text(nameof(AppSettings.VisionImageMinTokens));
    public WpfTextBox? VisionImageMaxTokensBox => Text(nameof(AppSettings.VisionImageMaxTokens));
    public WpfTextBox? TemperatureBox => Text(nameof(AppSettings.Temperature));
    public WpfTextBox? TopKBox => Text(nameof(AppSettings.TopK));
    public WpfTextBox? TopPBox => Text(nameof(AppSettings.TopP));
    public WpfTextBox? MinPBox => Text(nameof(AppSettings.MinP));
    public WpfTextBox? MaxTokensBox => Text(nameof(AppSettings.MaxTokens));
    public WpfTextBox? SeedBox => Text(nameof(AppSettings.Seed));
    public WpfTextBox? RepeatLastNBox => Text(nameof(AppSettings.RepeatLastN));
    public WpfTextBox? RepeatPenaltyBox => Text(nameof(AppSettings.RepeatPenalty));
    public WpfTextBox? PresencePenaltyBox => Text(nameof(AppSettings.PresencePenalty));
    public WpfTextBox? FrequencyPenaltyBox => Text(nameof(AppSettings.FrequencyPenalty));
    public WpfTextBox? RopeScaleBox => Text(nameof(AppSettings.RopeScale));
    public WpfTextBox? RopeFreqBaseBox => Text(nameof(AppSettings.RopeFreqBase));
    public WpfTextBox? RopeFreqScaleBox => Text(nameof(AppSettings.RopeFreqScale));
    public WpfTextBox? SpecDraftModelPathBox => Text(nameof(AppSettings.SpecDraftModelPath));
    public WpfButton? SpecDraftModelButton => Button(nameof(AppSettings.SpecDraftModelPath));
    public WpfTextBox? MtpHeadPathBox => Text(nameof(AppSettings.MtpHeadPath));
    public WpfButton? MtpHeadButton => Button(nameof(AppSettings.MtpHeadPath));
    public WpfTextBox? SpecDraftGpuLayersBox => Text(nameof(AppSettings.SpecDraftGpuLayers));
    public WpfTextBox? SpecDraftMinTokensBox => Text(nameof(AppSettings.SpecDraftMinTokens));
    public WpfTextBox? SpecDraftMaxTokensBox => Text(nameof(AppSettings.SpecDraftMaxTokens));
    public WpfTextBox? SpecDraftPSplitBox => Text(nameof(AppSettings.SpecDraftPSplit));
    public WpfTextBox? SpecDraftPMinBox => Text(nameof(AppSettings.SpecDraftPMin));
    public WpfTextBox? CustomParametersBox => Text(nameof(AppSettings.CustomParameters));

    public WpfComboBox? MetricsCombo => Combo(nameof(AppSettings.EnableMetrics));
    public WpfComboBox? GpuModeCombo => Combo(nameof(AppSettings.GpuMode));
    public WpfComboBox? ReasoningCombo => Combo(nameof(AppSettings.ReasoningMode));
    public WpfComboBox? ReasoningFormatCombo => Combo(nameof(AppSettings.ReasoningFormat));
    public WpfComboBox? ReasoningEffortCombo => Combo(nameof(AppSettings.ReasoningEffort));
    public WpfComboBox? ReasoningPreserveCombo => Combo(nameof(AppSettings.ReasoningPreserve));
    public WpfComboBox? VisionCombo => Combo(nameof(AppSettings.VisionMode));
    public WpfComboBox? FlashAttentionCombo => Combo(nameof(AppSettings.FlashAttention));
    public WpfComboBox? CacheTypeKCombo => Combo(nameof(AppSettings.CacheTypeK));
    public WpfComboBox? CacheTypeVCombo => Combo(nameof(AppSettings.CacheTypeV));
    public WpfComboBox? KvOffloadCombo => Combo(nameof(AppSettings.KvOffload));
    public WpfComboBox? KvUnifiedCombo => Combo(nameof(AppSettings.KvUnified));
    public WpfComboBox? PromptCacheCombo => Combo(nameof(AppSettings.PromptCacheMode));
    public WpfTextBox? PromptCacheRamMbBox => Text(nameof(AppSettings.PromptCacheRamMb));
    public WpfComboBox? ContextCheckpointsCombo => Combo(nameof(AppSettings.ContextCheckpointsMode));
    public WpfTextBox? ContextCheckpointCountBox => Text(nameof(AppSettings.ContextCheckpointCount));
    public WpfTextBox? ContextCheckpointEveryNTokensBox => Text(nameof(AppSettings.ContextCheckpointEveryNTokens));
    public WpfComboBox? ContinuousBatchingCombo => Combo(nameof(AppSettings.ContinuousBatching));
    public WpfComboBox? JinjaCombo => Combo(nameof(AppSettings.JinjaMode));
    public WpfComboBox? MmapCombo => Combo(nameof(AppSettings.MmapMode));
    public WpfComboBox? MlockCombo => Combo(nameof(AppSettings.MlockMode));
    public WpfComboBox? RopeScalingCombo => Combo(nameof(AppSettings.RopeScaling));
    public WpfComboBox? SpeculativeTypeCombo => Combo(nameof(AppSettings.SpeculativeType));
    public WpfComboBox? SpecDraftCacheTypeKCombo => Combo(nameof(AppSettings.SpecDraftCacheTypeK));
    public WpfComboBox? SpecDraftCacheTypeVCombo => Combo(nameof(AppSettings.SpecDraftCacheTypeV));

    private WpfTextBox? Text(string id) => _editors.GetValueOrDefault(id) as WpfTextBox;
    private WpfComboBox? Combo(string id) => _editors.GetValueOrDefault(id) as WpfComboBox;
    private WpfButton? Button(string id) => _editors.GetValueOrDefault(id + ".button") as WpfButton;

    public IEnumerable<WpfTextBox?> TextBoxes =>
    [
        LaunchPortBox, ContextSizeBox, GpuLayersBox, GpuDevicesBox, GpuSplitBox, ParallelSlotsBox, BatchSizeBox, MicroBatchSizeBox,
        ThreadsBox, ReasoningBudgetBox, ReasoningBudgetMessageBox, VisionProjectorPathBox, VisionImageMinTokensBox, VisionImageMaxTokensBox,
        TemperatureBox, TopKBox, TopPBox, MinPBox, MaxTokensBox, SeedBox, RepeatLastNBox,
        RepeatPenaltyBox, PresencePenaltyBox, FrequencyPenaltyBox, RopeScaleBox, RopeFreqBaseBox,
        RopeFreqScaleBox, SpecDraftModelPathBox, MtpHeadPathBox, SpecDraftGpuLayersBox, SpecDraftMinTokensBox,
        SpecDraftMaxTokensBox, SpecDraftPSplitBox, SpecDraftPMinBox, PromptCacheRamMbBox,
        ContextCheckpointCountBox, ContextCheckpointEveryNTokensBox, CustomParametersBox
    ];

    public IEnumerable<WpfComboBox?> ComboBoxes =>
    [
        MetricsCombo, GpuModeCombo, ReasoningCombo, ReasoningFormatCombo, ReasoningEffortCombo, ReasoningPreserveCombo, VisionCombo, FlashAttentionCombo,
        CacheTypeKCombo, CacheTypeVCombo, KvOffloadCombo, KvUnifiedCombo, PromptCacheCombo,
        ContextCheckpointsCombo, ContinuousBatchingCombo, JinjaCombo, MmapCombo, MlockCombo, RopeScalingCombo, SpeculativeTypeCombo,
        SpecDraftCacheTypeKCombo, SpecDraftCacheTypeVCombo
    ];
}

public static class LaunchSettingsFormBinder
{
    public static AppSettings Read(AppSettings baseSettings, LaunchSettingsFormControls controls)
    {
        var next = baseSettings with
        {
            Port = ReadInt(controls.LaunchPortBox, "Port", min: 1, max: 65535),
            ContextSize = ReadContextSize(controls.ContextSizeBox),
            GpuLayers = ReadInt(controls.GpuLayersBox, "GPU layers", min: 0),
            GpuMode = ComboValue(controls.GpuModeCombo),
            GpuDevices = controls.GpuDevicesBox?.Text.Trim() ?? "",
            GpuSplit = controls.GpuSplitBox?.Text.Trim() ?? "",
            ParallelSlots = ReadInt(controls.ParallelSlotsBox, "Parallel slots", min: 1),
            BatchSize = ReadInt(controls.BatchSizeBox, "Batch size", min: 1),
            MicroBatchSize = ReadInt(controls.MicroBatchSizeBox, "Micro batch size", min: 1),
            Threads = ReadInt(controls.ThreadsBox, "Threads", min: 0),
            ReasoningMode = ComboValue(controls.ReasoningCombo),
            ReasoningFormat = ComboValue(controls.ReasoningFormatCombo),
            ReasoningEffort = ComboValue(controls.ReasoningEffortCombo),
            ReasoningBudget = ReadInt(controls.ReasoningBudgetBox, "Reasoning budget", min: -1),
            ReasoningBudgetMessage = controls.ReasoningBudgetMessageBox?.Text.Trim() ?? "",
            ReasoningPreserve = ComboValue(controls.ReasoningPreserveCombo),
            VisionMode = ComboValue(controls.VisionCombo),
            VisionProjectorPath = controls.VisionProjectorPathBox?.Text.Trim() ?? "",
            VisionImageMinTokens = ReadInt(controls.VisionImageMinTokensBox, "Image min tokens", min: 0),
            VisionImageMaxTokens = ReadInt(controls.VisionImageMaxTokensBox, "Image max tokens", min: 0),
            FlashAttention = ComboValue(controls.FlashAttentionCombo),
            CacheTypeK = ComboValue(controls.CacheTypeKCombo),
            CacheTypeV = ComboValue(controls.CacheTypeVCombo),
            KvOffload = ComboValue(controls.KvOffloadCombo),
            KvUnified = ComboValue(controls.KvUnifiedCombo),
            PromptCacheMode = ComboValue(controls.PromptCacheCombo),
            PromptCacheRamMb = ReadInt(controls.PromptCacheRamMbBox, "Prompt cache MB", min: -1),
            ContextCheckpointsMode = ComboValue(controls.ContextCheckpointsCombo),
            ContextCheckpointCount = ReadInt(controls.ContextCheckpointCountBox, "Checkpoint count", min: 0),
            ContextCheckpointEveryNTokens = ReadInt(controls.ContextCheckpointEveryNTokensBox, "Checkpoint spacing", min: -1),
            ContinuousBatching = ComboValue(controls.ContinuousBatchingCombo),
            JinjaMode = ComboValue(controls.JinjaCombo),
            MmapMode = ComboValue(controls.MmapCombo),
            MlockMode = ComboValue(controls.MlockCombo),
            EnableMetrics = ComboValue(controls.MetricsCombo) == "on",
            Temperature = ReadDouble(controls.TemperatureBox, "Temperature", min: 0),
            TopK = ReadInt(controls.TopKBox, "Top K", min: 0),
            TopP = ReadDouble(controls.TopPBox, "Top P", min: 0, max: 1),
            MinP = ReadDouble(controls.MinPBox, "Min P", min: 0, max: 1),
            MaxTokens = ReadInt(controls.MaxTokensBox, "Max tokens", min: -1),
            Seed = ReadInt(controls.SeedBox, "Seed", min: -1),
            RepeatLastN = ReadInt(controls.RepeatLastNBox, "Repeat window", min: -1),
            RepeatPenalty = ReadDouble(controls.RepeatPenaltyBox, "Repeat penalty", min: 0),
            PresencePenalty = ReadDouble(controls.PresencePenaltyBox, "Presence penalty", min: -10, max: 10),
            FrequencyPenalty = ReadDouble(controls.FrequencyPenaltyBox, "Frequency penalty", min: -10, max: 10),
            RopeScaling = ComboValue(controls.RopeScalingCombo),
            RopeScale = ReadDouble(controls.RopeScaleBox, "RoPE scale", min: 0),
            RopeFreqBase = ReadDouble(controls.RopeFreqBaseBox, "RoPE base", min: 0),
            RopeFreqScale = ReadDouble(controls.RopeFreqScaleBox, "RoPE frequency scale", min: 0),
            SpeculativeType = ComboValue(controls.SpeculativeTypeCombo),
            SpecDraftModelPath = controls.SpecDraftModelPathBox?.Text.Trim() ?? "",
            MtpHeadPath = controls.MtpHeadPathBox?.Text.Trim() ?? "",
            SpecDraftGpuLayers = ReadInt(controls.SpecDraftGpuLayersBox, "Draft GPU layers", min: -1),
            SpecDraftMinTokens = ReadInt(controls.SpecDraftMinTokensBox, "Draft min tokens", min: 0),
            SpecDraftMaxTokens = ReadInt(controls.SpecDraftMaxTokensBox, "Draft max tokens", min: 0),
            SpecDraftPSplit = ReadDouble(controls.SpecDraftPSplitBox, "Draft split probability", min: -1, max: 1),
            SpecDraftPMin = ReadDouble(controls.SpecDraftPMinBox, "Draft min probability", min: -1, max: 1),
            SpecDraftCacheTypeK = ComboValue(controls.SpecDraftCacheTypeKCombo),
            SpecDraftCacheTypeV = ComboValue(controls.SpecDraftCacheTypeVCombo),
            CustomParameters = controls.RuntimeOptions?.BuildCustomParameters()
                ?? controls.CustomParametersBox?.Text.Trim()
                ?? ""
        };

        ValidateCrossFieldRules(next);
        return next;
    }

    public static void Apply(LaunchSettingsFormControls controls, AppSettings settings)
    {
        SetText(controls.LaunchPortBox, settings.Port);
        SetText(controls.ContextSizeBox, settings.ContextSize);
        SetText(controls.GpuLayersBox, settings.GpuLayers);
        SetText(controls.GpuDevicesBox, settings.GpuDevices);
        SetText(controls.GpuSplitBox, settings.GpuSplit);
        SetText(controls.ParallelSlotsBox, settings.ParallelSlots);
        SetText(controls.BatchSizeBox, settings.BatchSize);
        SetText(controls.MicroBatchSizeBox, settings.MicroBatchSize);
        SetText(controls.ThreadsBox, settings.Threads);
        SetText(controls.ReasoningBudgetBox, settings.ReasoningBudget);
        SetText(controls.ReasoningBudgetMessageBox, settings.ReasoningBudgetMessage);
        SetText(controls.VisionProjectorPathBox, settings.VisionProjectorPath);
        SetText(controls.VisionImageMinTokensBox, settings.VisionImageMinTokens);
        SetText(controls.VisionImageMaxTokensBox, settings.VisionImageMaxTokens);
        SetText(controls.TemperatureBox, settings.Temperature);
        SetText(controls.TopKBox, settings.TopK);
        SetText(controls.TopPBox, settings.TopP);
        SetText(controls.MinPBox, settings.MinP);
        SetText(controls.MaxTokensBox, settings.MaxTokens);
        SetText(controls.SeedBox, settings.Seed);
        SetText(controls.RepeatLastNBox, settings.RepeatLastN);
        SetText(controls.RepeatPenaltyBox, settings.RepeatPenalty);
        SetText(controls.PresencePenaltyBox, settings.PresencePenalty);
        SetText(controls.FrequencyPenaltyBox, settings.FrequencyPenalty);
        SetText(controls.RopeScaleBox, settings.RopeScale);
        SetText(controls.RopeFreqBaseBox, settings.RopeFreqBase);
        SetText(controls.RopeFreqScaleBox, settings.RopeFreqScale);
        SetText(controls.SpecDraftModelPathBox, settings.SpecDraftModelPath);
        SetText(controls.MtpHeadPathBox, settings.MtpHeadPath);
        SetText(controls.SpecDraftGpuLayersBox, settings.SpecDraftGpuLayers);
        SetText(controls.SpecDraftMinTokensBox, settings.SpecDraftMinTokens);
        SetText(controls.SpecDraftMaxTokensBox, settings.SpecDraftMaxTokens);
        SetText(controls.SpecDraftPSplitBox, settings.SpecDraftPSplit);
        SetText(controls.SpecDraftPMinBox, settings.SpecDraftPMin);
        SetText(controls.CustomParametersBox, settings.CustomParameters);
        controls.RuntimeOptions?.ImportRawParameters(notify: false);
        SetCombo(controls.MetricsCombo, settings.EnableMetrics ? "on" : "off");
        SetCombo(controls.GpuModeCombo, LocalLlmConsole.Services.LaunchSettingMetadataService.NormalizeGpuMode(settings.GpuMode));
        SetCombo(controls.ReasoningCombo, settings.ReasoningMode);
        SetCombo(controls.ReasoningFormatCombo, settings.ReasoningFormat);
        SetCombo(controls.ReasoningEffortCombo, settings.ReasoningEffort);
        SetCombo(controls.ReasoningPreserveCombo, settings.ReasoningPreserve);
        SetCombo(controls.VisionCombo, settings.VisionMode);
        SetCombo(controls.FlashAttentionCombo, settings.FlashAttention);
        SetCombo(controls.CacheTypeKCombo, settings.CacheTypeK);
        SetCombo(controls.CacheTypeVCombo, settings.CacheTypeV);
        SetCombo(controls.KvOffloadCombo, settings.KvOffload);
        SetCombo(controls.KvUnifiedCombo, settings.KvUnified);
        SetCombo(controls.PromptCacheCombo, settings.PromptCacheMode);
        SetText(controls.PromptCacheRamMbBox, settings.PromptCacheRamMb);
        SetCombo(controls.ContextCheckpointsCombo, settings.ContextCheckpointsMode);
        SetText(controls.ContextCheckpointCountBox, settings.ContextCheckpointCount);
        SetText(controls.ContextCheckpointEveryNTokensBox, settings.ContextCheckpointEveryNTokens);
        SetCombo(controls.ContinuousBatchingCombo, settings.ContinuousBatching);
        SetCombo(controls.JinjaCombo, settings.JinjaMode);
        SetCombo(controls.MmapCombo, settings.MmapMode);
        SetCombo(controls.MlockCombo, settings.MlockMode);
        SetCombo(controls.RopeScalingCombo, settings.RopeScaling);
        SetCombo(controls.SpeculativeTypeCombo, LocalLlmConsole.Services.LaunchSettingMetadataService.NormalizeSpeculativeType(settings.SpeculativeType));
        SetCombo(controls.SpecDraftCacheTypeKCombo, settings.SpecDraftCacheTypeK);
        SetCombo(controls.SpecDraftCacheTypeVCombo, settings.SpecDraftCacheTypeV);
    }

    public static void AttachChangeHandlers(LaunchSettingsFormControls controls, Action changed, RoutedEventHandler contextSizeLostFocus)
    {
        if (controls.ContextSizeBox is not null)
            controls.ContextSizeBox.LostFocus += contextSizeLostFocus;

        foreach (var box in controls.TextBoxes.Where(box => box is not null))
            box!.TextChanged += (_, _) => changed();

        foreach (var combo in controls.ComboBoxes.Where(combo => combo is not null))
            combo!.SelectionChanged += (_, _) => changed();

        if (controls.RuntimeOptions is not null)
            controls.RuntimeOptions.Changed += changed;
    }

    public static void ValidateCrossFieldRules(AppSettings next)
    {
        if (next.SpecDraftPSplit < 0 && Math.Abs(next.SpecDraftPSplit + 1) > 0.000_001)
            throw new InvalidOperationException("Draft split probability must be -1 for default or between 0 and 1.");
        if (next.SpecDraftPMin < 0 && Math.Abs(next.SpecDraftPMin + 1) > 0.000_001)
            throw new InvalidOperationException("Draft min probability must be -1 for default or between 0 and 1.");
        if (string.Equals(next.PromptCacheMode, "on", StringComparison.OrdinalIgnoreCase) && next.PromptCacheRamMb == 0)
            throw new InvalidOperationException("Prompt cache MB must be -1 or greater than 0 when prompt cache is on.");
        if (string.Equals(next.ContextCheckpointsMode, "on", StringComparison.OrdinalIgnoreCase) && next.ContextCheckpointCount < 1)
            throw new InvalidOperationException("Checkpoint count must be at least 1 when checkpoints are on.");
        if (string.Equals(next.ContextCheckpointsMode, "on", StringComparison.OrdinalIgnoreCase) && next.ContextCheckpointEveryNTokens < 1)
            throw new InvalidOperationException("Checkpoint spacing must be at least 1 when checkpoints are on.");
        if (next.SpecDraftMaxTokens > 0 && next.SpecDraftMinTokens > next.SpecDraftMaxTokens)
            throw new InvalidOperationException("Draft min tokens cannot be larger than draft max tokens.");
        if (next.VisionImageMaxTokens > 0 && next.VisionImageMinTokens > next.VisionImageMaxTokens)
            throw new InvalidOperationException("Image min tokens cannot be larger than image max tokens.");
        var gpuErrors = LocalLlmConsole.Services.LaunchSettingMetadataService.ValidateGpuSettings(
            next.GpuMode,
            next.GpuDevices,
            next.GpuSplit);
        if (gpuErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", gpuErrors));
        _ = LocalLlmConsole.Services.CustomLaunchParameterParser.Parse(next.CustomParameters);
    }

    private static void SetText(WpfTextBox? box, int value) => SetText(box, value.ToString(CultureInfo.InvariantCulture));

    private static void SetText(WpfTextBox? box, double value) => SetText(box, value.ToString("0.###", CultureInfo.InvariantCulture));

    private static void SetText(WpfTextBox? box, string value)
    {
        if (box is not null) box.Text = value;
    }

    private static void SetCombo(WpfComboBox? combo, string value)
    {
        if (combo is null) return;
        var match = combo.Items.Cast<object>().Select(item => item.ToString() ?? "").FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = string.IsNullOrWhiteSpace(match) ? combo.Items[0] : match;
    }

    private static string ComboValue(WpfComboBox? combo)
        => (combo?.SelectedItem?.ToString() ?? combo?.Text ?? "").Trim().ToLowerInvariant();

    private static int ReadContextSize(WpfTextBox? box)
        => LaunchSettingParser.ReadContextSize(box?.Text.Trim() ?? "");

    private static int ReadInt(WpfTextBox? box, string label, int min, int? max = null)
        => LaunchSettingParser.ReadInt(box?.Text.Trim() ?? "", label, min, max);

    private static double ReadDouble(WpfTextBox? box, string label, double min, double? max = null)
        => LaunchSettingParser.ReadDouble(box?.Text.Trim() ?? "", label, min, max);
}
