using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public enum SettingsRowActionOutcome
{
    Ignored,
    CacheCleared,
    ApiKeyGenerated,
    FolderSelected,
    FolderSelectionCanceled
}

public enum SettingsSecretActionOutcome
{
    Ignored,
    Revealed,
    Hidden,
    Copied,
    Empty
}

public sealed record SettingsRowActionApplicationActions(
    Func<Task> ClearCacheAsync,
    Func<string, string?> PickFolder,
    Action<string> SetStatus);

public sealed record SettingsSecretCopyApplicationActions(
    Action<string> CopyToClipboard,
    Action<string> SetStatus);

public sealed class SettingsRowActionApplicationService
{
    private const string CacheKey = "cache";
    private const string ModelApiKeyKey = "modelApiKey";
    private const string FolderType = "folder";
    private const string SecretType = "secret";
    private readonly Func<string> _generateApiKey;

    public SettingsRowActionApplicationService(Func<string>? generateApiKey = null)
    {
        _generateApiKey = generateApiKey ?? (() => ApiSecurity.GenerateHexToken(32));
    }

    public async Task<SettingsRowActionOutcome> RunActionAsync(
        EditableSettingRow? row,
        SettingsRowActionApplicationActions actions)
    {
        Validate(actions);
        if (row is null)
            return SettingsRowActionOutcome.Ignored;

        if (string.Equals(row.Key, CacheKey, StringComparison.Ordinal))
        {
            await actions.ClearCacheAsync();
            return SettingsRowActionOutcome.CacheCleared;
        }

        if (string.Equals(row.Key, ModelApiKeyKey, StringComparison.Ordinal))
        {
            row.Value = _generateApiKey();
            actions.SetStatus("New model API key generated; applying automatically.");
            return SettingsRowActionOutcome.ApiKeyGenerated;
        }

        if (!string.Equals(row.Type, FolderType, StringComparison.Ordinal))
            return SettingsRowActionOutcome.Ignored;

        var folder = actions.PickFolder(row.Value);
        if (folder is null)
            return SettingsRowActionOutcome.FolderSelectionCanceled;

        row.Value = Path.GetFullPath(folder);
        actions.SetStatus($"{row.Label} folder selected; applying automatically.");
        return SettingsRowActionOutcome.FolderSelected;
    }

    public SettingsSecretActionOutcome ToggleSecret(
        EditableSettingRow? row,
        Action<string> setStatus)
    {
        ArgumentNullException.ThrowIfNull(setStatus);
        if (row is null || !string.Equals(row.Type, SecretType, StringComparison.Ordinal))
            return SettingsSecretActionOutcome.Ignored;

        row.IsSecretVisible = !row.IsSecretVisible;
        setStatus(row.IsSecretVisible ? "API key is visible in Settings." : "API key hidden.");
        return row.IsSecretVisible
            ? SettingsSecretActionOutcome.Revealed
            : SettingsSecretActionOutcome.Hidden;
    }

    public SettingsSecretActionOutcome CopySecret(
        EditableSettingRow? row,
        SettingsSecretCopyApplicationActions actions)
    {
        Validate(actions);
        if (row is null || !string.Equals(row.Type, SecretType, StringComparison.Ordinal))
            return SettingsSecretActionOutcome.Ignored;

        var value = (row.Value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            actions.SetStatus("No API key is available to copy.");
            return SettingsSecretActionOutcome.Empty;
        }

        actions.CopyToClipboard(value);
        actions.SetStatus("API key copied to clipboard.");
        return SettingsSecretActionOutcome.Copied;
    }

    private static void Validate(SettingsRowActionApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.ClearCacheAsync);
        ArgumentNullException.ThrowIfNull(actions.PickFolder);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static void Validate(SettingsSecretCopyApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.CopyToClipboard);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
