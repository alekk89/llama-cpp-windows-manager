using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalLlmConsole.Models;

public enum OverviewEndpointKind
{
    Session,
    Gateway
}

public sealed class OverviewSessionRow
{
    public required OverviewEndpointKind Kind { get; init; }
    public required string ModelName { get; init; }
    public required string ProfileName { get; init; }
    public required string Size { get; init; }
    public required string State { get; init; }
    public required string Endpoint { get; init; }
    public required string Runtime { get; init; }
    public required string Backend { get; init; }
    public string ActionLabel { get; init; } = "";
    public bool CanUnload { get; init; }
    public bool CanInspect { get; init; }
    public string SessionId { get; init; } = "";
    public string ModelId { get; init; } = "";
}

public sealed class RuntimeMetricRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _labels = "";
    private string _value = "";
    private string _type = "";
    private string _help = "";

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Labels { get => _labels; set => Set(ref _labels, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Type { get => _type; set => Set(ref _type, value); }
    public string Help { get => _help; set => Set(ref _help, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(RuntimeMetricRow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Name = source.Name;
        Labels = source.Labels;
        Value = source.Value;
        Type = source.Type;
        Help = source.Help;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        value ??= "";
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class LogFileRow
{
    public required string Type { get; init; }
    public required string FileName { get; init; }
    public string Related { get; init; } = "";
    public string Updated { get; init; } = "";
    public string Size { get; init; } = "";
    public required string FullPath { get; init; }
    public string OpenAction { get; init; } = "";
    public string DeleteAction { get; init; } = "";
    public string OpenToolTip { get; init; } = "";
    public string DeleteToolTip { get; init; } = "";
    public bool CanOpen { get; init; } = true;
    public bool CanDelete { get; init; } = true;
}

public enum LifetimeMetricRowKind
{
    Model,
    Total
}

public sealed class LifetimeMetricRow
{
    public LifetimeMetricRowKind Kind { get; init; } = LifetimeMetricRowKind.Model;
    public string ModelId { get; init; } = "";
    public required string ModelName { get; init; }
    public string Requests { get; init; } = "";
    public string InputTokens { get; init; } = "";
    public string CachedTokens { get; init; } = "";
    public string OutputTokens { get; init; } = "";
    public string TotalTokens { get; init; } = "";
    public string Share { get; init; } = "";
    public string GenerationRate { get; init; } = "";
    public string ResetAction { get; init; } = "";
    public string ResetToolTip { get; init; } = "";
    public bool CanReset { get; init; } = true;
}

public sealed class WslDistroRow
{
    public string Name { get; init; } = "";
    public string State { get; init; } = "";
    public string WslVersion { get; init; } = "";
    public string Notes { get; init; } = "";
    public string ActionLabel { get; init; } = "";
    public string ActionToolTip { get; init; } = "";
    public bool CanSelect { get; init; }
    public bool IsDefault { get; init; }
    public bool IsUbuntu { get; init; }
}

public sealed class WindowsToolRow
{
    public required string Toolchain { get; init; }
    public required string Status { get; init; }
    public string Details { get; init; } = "";
    public string Driver { get; init; } = "";
}

public sealed class HuggingFaceSearchRow : INotifyPropertyChanged
{
    private string _downloadAction = "";
    private bool _canDownload;

    public required HuggingFaceFile File { get; init; }
    public required string Repo { get; init; }
    public required string FilePath { get; init; }
    public required string Quant { get; init; }
    public required string Size { get; init; }
    public required string Downloads { get; init; }
    public required string Signals { get; init; }
    public string DownloadAction
    {
        get => _downloadAction;
        set => Set(ref _downloadAction, value);
    }
    public string CardAction { get; init; } = "Card";
    public string DownloadToolTip { get; init; } = "";
    public string CardToolTip { get; init; } = "";
    public bool CanDownload
    {
        get => _canDownload;
        set => Set(ref _canDownload, value);
    }
    public bool CanOpenCard { get; init; } = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class HuggingFaceDownloadRow : INotifyPropertyChanged
{
    private string _status = "";
    private string _model = "";
    private string _progress = "";
    private string _size = "";
    private string _updated = "";
    private string _destination = "";
    private string _startAction = "";
    private bool _canStart;
    private bool _canPause;
    private bool _canStop;
    private JobRecord _job = null!;

    public required JobRecord Job { get => _job; init => _job = value; }
    public string JobId => Job.Id;
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string Progress { get => _progress; set => Set(ref _progress, value); }
    public string Size { get => _size; set => Set(ref _size, value); }
    public string Updated { get => _updated; set => Set(ref _updated, value); }
    public string Destination { get => _destination; set => Set(ref _destination, value); }
    public string StartAction { get => _startAction; set => Set(ref _startAction, value); }
    public string PauseAction { get; init; } = "Pause";
    public string StopAction { get; init; } = "Stop";
    public string DeleteAction { get; init; } = "Delete";
    public string StartToolTip { get; init; } = "Resume or restart this model download.";
    public string PauseToolTip { get; init; } = "Pause this active model download.";
    public string StopToolTip { get; init; } = "Stop this model download and keep resumable partial data.";
    public string DeleteToolTip { get; init; } = "Delete this download history entry and any incomplete partial file.";
    public bool CanStart { get => _canStart; set => Set(ref _canStart, value); }
    public bool CanPause { get => _canPause; set => Set(ref _canPause, value); }
    public bool CanStop { get => _canStop; set => Set(ref _canStop, value); }
    public bool CanDelete { get; init; } = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(HuggingFaceDownloadRow source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _job = source.Job;
        OnPropertyChanged(nameof(Job));
        OnPropertyChanged(nameof(JobId));
        Status = source.Status;
        Model = source.Model;
        Progress = source.Progress;
        Size = source.Size;
        Updated = source.Updated;
        Destination = source.Destination;
        StartAction = source.StartAction;
        CanStart = source.CanStart;
        CanPause = source.CanPause;
        CanStop = source.CanStop;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        value ??= "";
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ModelGridRow
{
    public required string Name { get; init; }
    public required string Quant { get; init; }
    public required string Size { get; init; }
    public string Group { get; init; } = "";
    public string GroupAction { get; init; } = "";
    public string GroupToolTip { get; init; } = "";
    public string BaseModel { get; init; } = "";
    public string Port { get; init; } = "";
    public string Description { get; init; } = "";
    public string OpenFolderAction { get; init; } = "Open Folder";
    public string DeleteAction { get; init; } = "Delete";
    public string OpenFolderToolTip { get; init; } = "Open the folder containing this model file.";
    public string DeleteToolTip { get; init; } = "Delete this model from disk and remove it from the catalog.";
    public bool CanOpenFolder { get; init; } = true;
    public bool CanDelete { get; init; } = true;
    public bool CanLoad { get; init; } = true;
    public bool IsMissing { get; init; }
    public bool CanAssignGroup { get; init; }
    public bool IsTrayFavorite { get; init; }
    public required ModelRecord Model { get; init; }
    public NamedModelLaunchProfile? LaunchProfile { get; init; }
}

public enum RuntimeCatalogRowKind
{
    Runtime,
    Source
}

public sealed class RuntimeCatalogRow
{
    public RuntimeCatalogRowKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Backend { get; init; }
    public required string State { get; init; }
    public required string Location { get; init; }
    public required string Details { get; init; }
    public string Vendor { get; init; } = "";
    public string Platform { get; init; } = "";
    public string BuildAction { get; init; } = "";
    public string BuildToolTip { get; init; } = "";
    public string VerifyAction { get; init; } = "Verify";
    public string VerifyToolTip { get; init; } = "";
    public string DeleteAction { get; init; } = "Delete";
    public string DeleteToolTip { get; init; } = "";
    public bool CanBuild { get; init; }
    public bool CanVerify { get; init; }
    public bool CanDelete { get; init; }
    public RuntimeRecord? Runtime { get; init; }
    public RuntimeSourceEntry? Source { get; init; }
}

public enum RuntimeSourceRowActionKind
{
    None,
    Add,
    Check,
    Download,
    Build
}

[Flags]
public enum RuntimeDownloadDeleteKind
{
    None = 0,
    Package = 1,
    Source = 2
}

public sealed class RuntimeBuildPresetRow
{
    public string Label { get; set; } = "";
    public string Backend { get; set; } = "";
    public string LocalStatus { get; set; } = "";
    public string LatestLocal { get; set; } = "";
    public string Source { get; set; } = "";
    public string DownloadAction { get; set; } = "";
    public string CheckAction { get; set; } = "";
    public string DeleteAction { get; set; } = "";
    public string DownloadToolTip { get; set; } = "";
    public string CheckToolTip { get; set; } = "";
    public string DeleteToolTip { get; set; } = "";
    public bool CanDownload { get; set; }
    public bool CanCheck { get; set; }
    public bool CanDelete { get; set; }
    public bool IsCustomAdd { get; init; }
    public RuntimeBuildPreset? Preset { get; init; }
}

public sealed class RuntimePackagePresetRow
{
    public string Label { get; set; } = "";
    public string Backend { get; set; } = "";
    public string LocalStatus { get; set; } = "";
    public string LatestRelease { get; set; } = "";
    public string Assets { get; set; } = "";
    public string BuildSourceAction { get; set; } = "";
    public string InstallAction { get; set; } = "";
    public string CheckAction { get; set; } = "Check";
    public string DeleteAction { get; set; } = "Delete All";
    public string InstallToolTip { get; set; } = "";
    public string BuildSourceToolTip { get; set; } = "";
    public string CheckToolTip { get; set; } = "";
    public string DeleteToolTip { get; set; } = "";
    public bool CanInstall { get; set; }
    public bool CanBuildSource { get; set; }
    public bool CanCheck { get; set; } = true;
    public bool CanDelete { get; set; }
    public string Vendor { get; set; } = "";
    public string Platform { get; set; } = "";
    public RuntimeSourceRowActionKind SourceActionKind { get; set; }
    public RuntimeBuildPreset? SourcePreset { get; set; }
    public RuntimeSourceEntry? DownloadedSource { get; set; }
    public RuntimeDownloadDeleteKind DeleteKind { get; set; }
    public RuntimePackagePreset? Preset { get; init; }
}
