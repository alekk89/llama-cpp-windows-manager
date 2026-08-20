namespace LocalLlmConsole.Models;

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
