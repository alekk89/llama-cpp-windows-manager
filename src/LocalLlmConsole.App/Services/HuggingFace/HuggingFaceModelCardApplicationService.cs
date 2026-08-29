using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public enum HuggingFaceModelCardApplicationOutcome
{
    Blocked,
    Opened
}

public sealed record HuggingFaceModelCardApplicationActions(
    Action<string> OpenUrl,
    Action<string> SetStatus);

public sealed class HuggingFaceModelCardApplicationService
{
    public HuggingFaceModelCardApplicationOutcome OpenFromRow(
        HuggingFaceSearchRow? row,
        HuggingFaceModelCardApplicationActions actions)
        => Open(RepoFromSearchRow(row), actions);

    public HuggingFaceModelCardApplicationOutcome Open(
        string repo,
        HuggingFaceModelCardApplicationActions actions)
    {
        Validate(actions);

        if (!HuggingFaceService.TryCreateModelCardUrl(repo, out var url))
        {
            actions.SetStatus("The selected row does not contain a valid Hugging Face repository.");
            return HuggingFaceModelCardApplicationOutcome.Blocked;
        }

        actions.OpenUrl(url);
        actions.SetStatus($"Opened Hugging Face model card: {repo}");
        return HuggingFaceModelCardApplicationOutcome.Opened;
    }

    public static string RepoFromSearchRow(HuggingFaceSearchRow? row)
        => row?.Repo ?? "";

    private static void Validate(HuggingFaceModelCardApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.OpenUrl);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
