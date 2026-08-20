namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildPreset(
    string Id,
    string Label,
    string RepoUrl,
    string Branch,
    bool Cuda,
    bool Custom = false,
    string Backend = "",
    RuntimeMode Mode = RuntimeMode.Wsl);

public sealed record RuntimeSourceEntry(
    string PresetId,
    string Label,
    string RepoUrl,
    string Branch,
    bool Cuda,
    string SourceDir,
    string Commit,
    DateTimeOffset DownloadedAt,
    string Backend = "",
    RuntimeMode Mode = RuntimeMode.Wsl);

public sealed record RuntimeUpdateState(
    bool HasUpdate,
    string LocalCommit,
    string RemoteCommit,
    DateTimeOffset CheckedAt);
