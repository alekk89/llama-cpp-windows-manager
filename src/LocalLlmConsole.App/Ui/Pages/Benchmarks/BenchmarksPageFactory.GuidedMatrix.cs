using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static partial class BenchmarksPageFactory
{
    private sealed record GuidedBenchmarkMatrix(
        StackPanel Panel,
        TextBlock Summary,
        BenchmarkValuePicker ContextSizes,
        BenchmarkValuePicker BatchSizes,
        BenchmarkValuePicker MicroBatchSizes,
        BenchmarkValuePicker Threads,
        BenchmarkValuePicker GpuLayers,
        BenchmarkValuePicker FlashAttention,
        BenchmarkValuePicker CacheTypesKv,
        BenchmarkValuePicker KvOffload,
        BenchmarkGpuConfigurationPicker GpuConfigurations,
        BenchmarkSpeculativeConfigurationPicker SpeculativeConfigurations,
        CheckBox CompareContextSizes,
        CheckBox CompareBatchSizes,
        CheckBox CompareMicroBatchSizes,
        CheckBox CompareThreads,
        CheckBox CompareGpuLayers,
        CheckBox CompareFlashAttention,
        CheckBox CompareCacheTypesKv,
        CheckBox CompareKvOffload,
        CheckBox CompareGpuConfigurations,
        CheckBox CompareSpeculativeConfigurations);

    private static GuidedBenchmarkMatrix CreateGuidedBenchmarkMatrix()
    {
        var contextSizes = Picker("context length", "4096", "8192", "16384", "32768", "65536", "131072", "262144");
        var batches = Picker("batch size", "256", "512", "1024", "2048", "4096", "8192", "16384", "32768");
        var microBatches = Picker("micro-batch size", "128", "256", "512", "1024", "2048", "4096", "8192");
        var logicalProcessors = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        var halfProcessors = Math.Max(Environment.ProcessorCount / 2, 1).ToString(CultureInfo.InvariantCulture);
        var threads = Picker("thread count", halfProcessors, logicalProcessors);
        var gpuLayers = Picker("GPU-layer", "-1", "0");
        var flashAttention = Picker("Flash Attention", "auto", "on", "off");
        var cacheKv = Picker("K/V cache type", "f16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "f32", "bf16");
        var kvOffload = Picker("KV offload", "on", "off");
        var gpuConfigurations = new BenchmarkGpuConfigurationPicker();
        var speculativeConfigurations = new BenchmarkSpeculativeConfigurationPicker();
        var compareContexts = Check("Compare custom context lengths");
        var compareBatches = Check("Compare custom batch sizes");
        var compareMicroBatches = Check("Compare micro-batch sizes");
        var compareThreads = Check("Compare thread counts");
        var compareGpuLayers = Check("Compare GPU offload");
        var compareFlashAttention = Check("Compare Flash Attention");
        var compareCacheKv = Check("Compare K/V cache types");
        var compareKvOffload = Check("Compare KV offload");
        var compareGpuConfigurations = Check("Compare multi-GPU configurations");
        var compareSpeculativeConfigurations = Check("Compare speculative configurations");
        var summary = Muted("1 launch configuration per profile · all launch settings inherited.");
        summary.TextWrapping = TextWrapping.Wrap;
        var variables = GuidedVariablesGrid();
        AddGuidedVariable(variables, 0, "Context length", "When empty, uses each profile's saved context.",
            compareContexts, contextSizes, "Enter one or more values separated by commas, for example 8192, 16384.");
        AddGuidedVariable(variables, 1, "Batch size", "When empty, uses each profile's saved batch size.",
            compareBatches, batches, "Enter one or more values separated by commas, for example 1024, 2048.");
        AddGuidedVariable(variables, 2, "Micro-batch size", "When empty, uses the saved micro-batch.",
            compareMicroBatches, microBatches, "Suggested values: 128, 256, 512, 1024. Every micro-batch must be no larger than its tested batch size.");
        AddGuidedVariable(variables, 3, "Flash Attention", "When empty, uses the saved Auto/On/Off choice.",
            compareFlashAttention, flashAttention, "Enter auto, on, off, or a comma-separated combination.");
        AddGuidedVariable(variables, 4, "K/V cache type", "When empty, uses the profile's saved K and V formats.",
            compareCacheKv, cacheKv, "Each value is applied to K and V together, producing matched pairs such as q8_0/q8_0 rather than a cross-product. Availability depends on the runtime.");
        AddGuidedVariable(variables, 5, "GPU offload layers", "When empty, uses the saved GPU-layer count.",
            compareGpuLayers, gpuLayers, "Use 0 for CPU-only, -1 for all layers, or one or more explicit layer counts.");
        AddGuidedVariable(variables, 6, "Threads", "When empty, uses the saved thread count.",
            compareThreads, threads, "Most useful for CPU and hybrid profiles. Enter positive thread counts separated by commas.");
        AddGuidedVariable(variables, 7, "KV offload", "When empty, uses the saved KV placement.",
            compareKvOffload, kvOffload, "Enter on, off, or both.");
        AddGuidedVariable(variables, 8, "Multi-GPU configuration", "When empty, uses the profile's saved GPU mode and split together.",
            compareGpuConfigurations, gpuConfigurations, "Choose a mode and either Automatic or an explicit distribution. Single GPU never accepts a split. Each added mode/distribution pair is one benchmark configuration.");
        AddGuidedVariable(variables, 9, "Speculative decoding", "When empty, uses the profile's saved speculative type and companion/head together.",
            compareSpeculativeConfigurations, speculativeConfigurations, "Choose the speculative type and its head source, then click +. Profile head reuses the saved compatible companion; Automatic resolves a compatible companion beside the model or embedded draft-MTP tensors. Each added type/head pair is one configuration.");

        var selectedValues = new WrapPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(selectedValues, "Selected launch setting values");
        var pickerLabels = new (string Label, BenchmarkValuePicker Picker)[]
        {
            ("Context length", contextSizes),
            ("Batch size", batches),
            ("Micro-batch size", microBatches),
            ("Flash Attention", flashAttention),
            ("K/V cache type", cacheKv),
            ("GPU offload layers", gpuLayers),
            ("Threads", threads),
            ("KV offload", kvOffload)
        };
        void UpdateSelectedValuesVisibility()
            => selectedValues.Visibility = pickerLabels.Any(item => item.Picker.Values.Count > 0)
                                              || gpuConfigurations.Values.Count > 0
                                              || speculativeConfigurations.Values.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        foreach (var (label, picker) in pickerLabels)
        {
            picker.UseSharedSelectionHost(selectedValues, label);
            picker.Changed += (_, _) => UpdateSelectedValuesVisibility();
        }
        gpuConfigurations.UseSharedSelectionHost(selectedValues);
        gpuConfigurations.Changed += (_, _) => UpdateSelectedValuesVisibility();
        speculativeConfigurations.UseSharedSelectionHost(selectedValues);
        speculativeConfigurations.Changed += (_, _) => UpdateSelectedValuesVisibility();

        var panel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var description = Muted("Select one or more values to override a setting for this benchmark. Leave a row empty to inherit each profile's saved value. Multiple populated rows are combined automatically. Multi-GPU mode/distribution and speculative type/head are each added as exact pairs.");
        description.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(description);
        panel.Children.Add(summary);
        panel.Children.Add(variables);
        panel.Children.Add(selectedValues);
        return new GuidedBenchmarkMatrix(panel, summary, contextSizes, batches, microBatches, threads, gpuLayers,
            flashAttention, cacheKv, kvOffload, gpuConfigurations, speculativeConfigurations, compareContexts,
            compareBatches, compareMicroBatches, compareThreads, compareGpuLayers, compareFlashAttention,
            compareCacheKv, compareKvOffload, compareGpuConfigurations, compareSpeculativeConfigurations);
    }

    private static BenchmarkValuePicker Picker(string name, params string[] values)
        => new(name, values);
}
