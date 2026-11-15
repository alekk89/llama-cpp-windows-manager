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
        var contextSizes = Picker(Loc.T("Benchmarks.Dimension.ContextLength"), BenchmarkValuePicker.PositiveInteger, "4096", "8192", "16384", "32768", "65536", "131072", "262144");
        var batches = Picker(Loc.T("Benchmarks.Dimension.BatchSize"), BenchmarkValuePicker.PositiveInteger, "256", "512", "1024", "2048", "4096", "8192", "16384", "32768");
        var microBatches = Picker(Loc.T("Benchmarks.Dimension.MicroBatchSize"), BenchmarkValuePicker.PositiveInteger, "128", "256", "512", "1024", "2048", "4096", "8192");
        var logicalProcessors = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        var halfProcessors = Math.Max(Environment.ProcessorCount / 2, 1).ToString(CultureInfo.InvariantCulture);
        var threads = Picker(Loc.T("Benchmarks.Dimension.ThreadCount"), BenchmarkValuePicker.PositiveInteger, halfProcessors, logicalProcessors);
        var gpuLayers = Picker(Loc.T("Benchmarks.Dimension.GpuLayer"), BenchmarkValuePicker.GpuLayerCount, "-1", "0");
        var flashAttention = Picker(Loc.T("Benchmarks.Dimension.FlashAttention"), BenchmarkValuePicker.OneOf("auto", "on", "off"), "auto", "on", "off");
        var cacheKv = Picker(Loc.T("Benchmarks.Dimension.CacheType"), null, [.. RuntimeDefinedChoicePolicy.CacheTypes]);
        var kvOffload = Picker(Loc.T("Benchmarks.Dimension.KvOffload"), BenchmarkValuePicker.OneOf("on", "off"), "on", "off");
        var gpuConfigurations = new BenchmarkGpuConfigurationPicker();
        var speculativeConfigurations = new BenchmarkSpeculativeConfigurationPicker();
        var compareContexts = Check(Loc.T("Benchmarks.Variables.CompareContextLengths"));
        var compareBatches = Check(Loc.T("Benchmarks.Variables.CompareBatchSizes"));
        var compareMicroBatches = Check(Loc.T("Benchmarks.Variables.CompareMicroBatchSizes"));
        var compareThreads = Check(Loc.T("Benchmarks.Variables.CompareThreadCounts"));
        var compareGpuLayers = Check(Loc.T("Benchmarks.Variables.CompareGpuOffload"));
        var compareFlashAttention = Check(Loc.T("Benchmarks.Variables.CompareFlashAttention"));
        var compareCacheKv = Check(Loc.T("Benchmarks.Variables.CompareCacheTypes"));
        var compareKvOffload = Check(Loc.T("Benchmarks.Variables.CompareKvOffload"));
        var compareGpuConfigurations = Check(Loc.T("Benchmarks.Variables.CompareMultiGpu"));
        var compareSpeculativeConfigurations = Check(Loc.T("Benchmarks.Variables.CompareSpeculative"));
        var summary = Muted(Loc.T("Benchmarks.Variables.AllInherited"));
        summary.TextWrapping = TextWrapping.Wrap;
        var variables = GuidedVariablesGrid();
        AddGuidedVariable(variables, 0, Loc.T("Benchmarks.Variable.ContextLength"), Loc.T("Benchmarks.Variable.ContextLength.Inherited"),
            compareContexts, contextSizes, Loc.T("Benchmarks.Variable.ContextLength.Help"));
        AddGuidedVariable(variables, 1, Loc.T("Benchmarks.Variable.BatchSize"), Loc.T("Benchmarks.Variable.BatchSize.Inherited"),
            compareBatches, batches, Loc.T("Benchmarks.Variable.BatchSize.Help"));
        AddGuidedVariable(variables, 2, Loc.T("Benchmarks.Variable.MicroBatchSize"), Loc.T("Benchmarks.Variable.MicroBatchSize.Inherited"),
            compareMicroBatches, microBatches, Loc.T("Benchmarks.Variable.MicroBatchSize.Help"));
        AddGuidedVariable(variables, 3, Loc.T("Benchmarks.Variable.FlashAttention"), Loc.T("Benchmarks.Variable.FlashAttention.Inherited"),
            compareFlashAttention, flashAttention, Loc.T("Benchmarks.Variable.FlashAttention.Help"));
        AddGuidedVariable(variables, 4, Loc.T("Benchmarks.Variable.CacheType"), Loc.T("Benchmarks.Variable.CacheType.Inherited"),
            compareCacheKv, cacheKv, Loc.T("Benchmarks.Variable.CacheType.Help"));
        AddGuidedVariable(variables, 5, Loc.T("Benchmarks.Variable.GpuLayers"), Loc.T("Benchmarks.Variable.GpuLayers.Inherited"),
            compareGpuLayers, gpuLayers, Loc.T("Benchmarks.Variable.GpuLayers.Help"));
        AddGuidedVariable(variables, 6, Loc.T("Benchmarks.Variable.Threads"), Loc.T("Benchmarks.Variable.Threads.Inherited"),
            compareThreads, threads, Loc.T("Benchmarks.Variable.Threads.Help"));
        AddGuidedVariable(variables, 7, Loc.T("Benchmarks.Variable.KvOffload"), Loc.T("Benchmarks.Variable.KvOffload.Inherited"),
            compareKvOffload, kvOffload, Loc.T("Benchmarks.Variable.KvOffload.Help"));
        AddGuidedVariable(variables, 8, Loc.T("Benchmarks.Variable.MultiGpu"), Loc.T("Benchmarks.Variable.MultiGpu.Inherited"),
            compareGpuConfigurations, gpuConfigurations, Loc.T("Benchmarks.Variable.MultiGpu.Help"));
        AddGuidedVariable(variables, 9, Loc.T("Benchmarks.Variable.Speculative"), Loc.T("Benchmarks.Variable.Speculative.Inherited"),
            compareSpeculativeConfigurations, speculativeConfigurations, Loc.T("Benchmarks.Variable.Speculative.Help"));

        var selectedValues = new WrapPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(selectedValues, Loc.T("Benchmarks.Variables.SelectedValues"));
        var pickerLabels = new (string Label, BenchmarkValuePicker Picker)[]
        {
            (Loc.T("Benchmarks.Variable.ContextLength"), contextSizes),
            (Loc.T("Benchmarks.Variable.BatchSize"), batches),
            (Loc.T("Benchmarks.Variable.MicroBatchSize"), microBatches),
            (Loc.T("Benchmarks.Variable.FlashAttention"), flashAttention),
            (Loc.T("Benchmarks.Variable.CacheType"), cacheKv),
            (Loc.T("Benchmarks.Variable.GpuLayers"), gpuLayers),
            (Loc.T("Benchmarks.Variable.Threads"), threads),
            (Loc.T("Benchmarks.Variable.KvOffload"), kvOffload)
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
        var description = Muted(Loc.T("Benchmarks.Variables.Description"));
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

    private static BenchmarkValuePicker Picker(string name, Func<string, string?>? validate, params string[] values)
        => new(name, values, validate: validate);
}
