using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LocalLlmConsole;

public sealed record ProfileTransferUiActions(
    Func<ModelRecord, string?, bool, Task> Export,
    Func<ModelRecord, bool, Task> Import,
    Func<ModelRecord, NamedModelLaunchProfile, Task> CopyToModel);

public sealed class ProfileTransferUiController
{
    private readonly Window _owner;
    private readonly Func<ProfileTransferWorkflowService> _workflow;
    private readonly Func<StateStore> _store;
    private readonly Func<AppSettings> _defaults;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _status;
    private readonly Func<string, Func<Task>, Task> _run;

    public ProfileTransferUiController(Window owner, Func<ProfileTransferWorkflowService> workflow,
        Func<StateStore> store, Func<AppSettings> defaults, Func<Task> refresh, Action<string> status, Func<string, Func<Task>, Task> run)
    {
        _owner = owner; _workflow = workflow; _store = store; _defaults = defaults; _refresh = refresh; _status = status; _run = run;
    }

    public ProfileTransferUiActions Actions => new(
        (model, profile, clipboard) => _run(Loc.T("Profiles.Transfer.ExportBusy"), () => ExportAsync(model, profile, clipboard)),
        (model, clipboard) => _run(Loc.T("Profiles.Transfer.ImportBusy"), () => ImportAsync(model, clipboard)),
        (model, profile) => _run(Loc.T("Profiles.Transfer.CopyBusy"), () => CopyToModelAsync(model, profile)));

    private async Task ExportAsync(ModelRecord model, string? profileId, bool clipboard)
    {
        var bundle = await _workflow().ExportAsync(model, profileId);
        if (clipboard) System.Windows.Clipboard.SetText(ProfileBundleCodec.Write(bundle));
        else
        {
            var picker = new Microsoft.Win32.SaveFileDialog { Title = Loc.T("Profiles.Transfer.ExportTitle"), Filter = "Profile bundle (*.jsonc)|*.jsonc|JSON (*.json)|*.json", FileName = "profiles.jsonc" };
            if (picker.ShowDialog(_owner) != true) return;
            await ProfileBundleFileService.WriteAsync(picker.FileName, bundle);
        }
        _status(Loc.T("Profiles.Transfer.ExportStatus", bundle.Profiles.Count, model.Name));
    }

    private async Task ImportAsync(ModelRecord model, bool clipboard)
    {
        ProfileBundle bundle;
        if (clipboard) bundle = ProfileBundleCodec.Read(System.Windows.Clipboard.GetText());
        else
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Profiles.Transfer.ImportTitle"), Filter = "Profile bundles (*.jsonc;*.json)|*.jsonc;*.json|All files (*.*)|*.*" };
            if (picker.ShowDialog(_owner) != true) return;
            bundle = await ProfileBundleFileService.ReadAsync(picker.FileName);
        }
        await ReviewAsync(model, bundle);
    }

    private async Task CopyToModelAsync(ModelRecord source, NamedModelLaunchProfile profile)
    {
        var models = await _store().ListModelsAsync();
        var target = ProfileImportDialog.ChooseModel(_owner, models.Where(model => model.Id != source.Id).ToArray());
        if (target is null) return;
        await ReviewAsync(target, await _workflow().ExportAsync(source, profile.Id));
    }

    private async Task ReviewAsync(ModelRecord model, ProfileBundle bundle)
    {
        var workflow = _workflow();
        var preview = await workflow.PreviewAsync(model, bundle, _defaults());
        var runtimes = await _store().ListRuntimesAsync();
        var result = ProfileImportDialog.Show(_owner, model, bundle, preview, runtimes,
            choices => workflow.PreviewAsync(model, bundle, _defaults(), choices));
        if (result is null) return;
        var imported = await workflow.ApplyAsync(model, bundle, _defaults(), result.Choices, result.Preview);
        await _refresh();
        _status(Loc.T("Profiles.Transfer.ImportStatus", imported.Count, model.Name));
    }
}
