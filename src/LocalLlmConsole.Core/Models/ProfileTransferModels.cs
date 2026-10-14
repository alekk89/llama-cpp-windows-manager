namespace LocalLlmConsole.Models;

public sealed record ProfileAnnotations(string Notes = "", IReadOnlyDictionary<string, string>? SettingNotes = null);

public sealed record ProfileRuntimeHint(string Name = "", string Backend = "", string Mode = "");
public sealed record ProfileModelHint(string Name = "", string FileName = "", string Architecture = "");
public sealed record PortableLaunchProfile(
    string Name,
    JsonObject Settings,
    ProfileRuntimeHint? Runtime = null,
    ProfileAnnotations? Annotations = null,
    IReadOnlyList<string>? GpuNames = null);

public sealed record ProfileBundle(int SchemaVersion, ProfileModelHint Model, IReadOnlyList<PortableLaunchProfile> Profiles);

public enum ProfileConflictAction { KeepBoth, Replace, Skip }
public sealed record ProfileImportChoice(int Index, ProfileConflictAction Action = ProfileConflictAction.KeepBoth,
    string? Name = null, string? RuntimeId = null, JsonObject? Settings = null);
public sealed record ProfileImportRow(int Index, string Name, string? ReplaceId, ModelLaunchSettings Settings,
    ProfileAnnotations? Annotations, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);
public sealed record ProfileImportPreview(long CatalogRevision, string ModelId, IReadOnlyList<ProfileImportRow> Rows)
{
    public bool CanApply => Rows.Count > 0 && Rows.All(row => row.Errors.Count == 0);
}
