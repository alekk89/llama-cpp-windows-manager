namespace LocalLlmConsole;

/// <summary>Resolves localized benchmark field labels and their help text from a field identifier.</summary>
internal static class BenchmarkFieldDescriptions
{
    public static string Label(string field) => Loc.T($"Benchmarks.Field.{field}");

    public static string Get(string field) => Loc.T($"Benchmarks.Field.{field}.Help");
}
