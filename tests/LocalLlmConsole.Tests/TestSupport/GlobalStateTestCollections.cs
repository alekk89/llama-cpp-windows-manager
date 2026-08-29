namespace LocalLlmConsole.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationStateTestCollection
{
    public const string Name = "Localization state";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentStateTestCollection
{
    public const string Name = "Process environment state";
}
