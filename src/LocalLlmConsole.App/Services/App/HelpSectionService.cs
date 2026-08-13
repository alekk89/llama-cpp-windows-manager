namespace LocalLlmConsole.Services;

public sealed record HelpSectionDefinition(
    string Key,
    string Label,
    string Title,
    string Summary);

public sealed class HelpSectionService
{
    public const string FirstSteps = "first-steps";

    private static readonly HelpSectionDefinition[] DefaultSections =
    [
        new(FirstSteps, "First Steps", "First Steps", "Install a runtime, download a model, save launch settings, and load it."),
        new("overview", "Overview", "Overview", "Load models, inspect endpoints, and switch model status from active sessions."),
        new("models", "Models", "Models", "Find GGUF files, manage local models, and tune launch settings."),
        new("runtimes", "Runtimes", "Runtimes", "Install official llama.cpp packages or use advanced Windows and WSL tooling."),
        new("settings", "Settings", "Settings", "Configure app behavior, network access, the gateway, logs, and secrets."),
        new("maintenance", "Logs & Updates", "Logs & Updates", "Inspect logs, lifetime counters, runtime jobs, and app updates.")
    ];

    private string _activeSection = FirstSteps;

    public IReadOnlyList<HelpSectionDefinition> Sections => DefaultSections;

    public string ActiveSection => _activeSection;

    public HelpSectionDefinition Select(string sectionKey)
    {
        var definition = DefinitionFor(sectionKey);
        _activeSection = definition.Key;
        return definition;
    }

    public HelpSectionDefinition DefinitionFor(string sectionKey)
        => DefaultSections.FirstOrDefault(section => string.Equals(section.Key, sectionKey, StringComparison.Ordinal))
            ?? DefaultSections[0];
}
