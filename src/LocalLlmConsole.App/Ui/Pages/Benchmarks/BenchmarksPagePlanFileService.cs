using System.Text;
using System.Text.Json;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using Forms = System.Windows.Forms;

namespace LocalLlmConsole;

public static class BenchmarksPagePlanFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<BenchmarkPlan?> ImportAsync()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = Loc.T("Benchmarks.ImportPlanTitle"),
            Filter = Loc.T("Benchmarks.PlanFilter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
        await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<BenchmarkPlan>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The selected benchmark plan is empty.");
    }

    public static async Task<string?> ExportAsync(BenchmarkPlan plan)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = Loc.T("Benchmarks.ExportPlanTitle"),
            Filter = Loc.T("Benchmarks.PlanFilter"),
            FileName = "benchmark-plan.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await File.WriteAllTextAsync(dialog.FileName, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return dialog.FileName;
    }
}
