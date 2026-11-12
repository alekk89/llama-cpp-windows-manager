using System.Text.RegularExpressions;

namespace LocalLlmConsole.Tests;

public sealed class ReleaseWorkflowSecretsTests : ManagerRegressionTestBase
{
    private static readonly string[] CredentialFreeCheckoutWorkflows =
        ["release.yml", "prepare-unsigned-release.yml", "signpath-test.yml"];

    private static readonly Regex SecretReference = new(@"secrets\.(?<name>[A-Z0-9_]+)", RegexOptions.CultureInvariant);

    [Fact]
    public void WorkflowSecretsAreScopedToConsumingSteps()
    {
        var workflowRoot = Path.GetDirectoryName(FindRepositoryFile(".github", "workflows", "release.yml"))!;
        foreach (var path in Directory.EnumerateFiles(workflowRoot, "*.yml"))
        {
            var lines = File.ReadAllLines(path);
            var steps = ParseSteps(lines);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("secrets.", StringComparison.Ordinal))
                    continue;
                Assert.True(
                    IsInsideStep(steps, index),
                    $"{Path.GetFileName(path)}:{index + 1} exposes a secret outside a single step.");
            }
        }
    }

    [Fact]
    public void ReleaseWorkflowMapsEachSecretOnlyIntoItsConsumer()
    {
        var steps = ParseSteps(File.ReadAllLines(FindRepositoryFile(".github", "workflows", "release.yml")));
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RELEASE_TAG_SIGNING_PUBLIC_KEY_BASE64"] = "Verify signed annotated tag and release identity",
            ["RELEASE_MANIFEST_SIGNING_KEY_PEM_BASE64"] = "Import code-signing certificate and manifest key",
            ["WINDOWS_SIGNING_PFX_BASE64"] = "Import code-signing certificate and manifest key",
            ["WINDOWS_SIGNING_PFX_PASSWORD"] = "Import code-signing certificate and manifest key",
        };

        var consumers = steps
            .SelectMany(step => step.Lines.SelectMany(line => SecretReference.Matches(line).Select(match => (Secret: match.Groups["name"].Value, step.Name))))
            .ToArray();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), consumers.Select(item => item.Secret).Distinct().Order(StringComparer.Ordinal));
        foreach (var (secret, step) in consumers)
            Assert.Equal(expected[secret], step);

        foreach (var step in steps.Where(step => step.Name != expected["WINDOWS_SIGNING_PFX_BASE64"]))
            Assert.DoesNotContain(step.Lines, line => line.Contains("RELEASE_MANIFEST_SIGNING_KEY_PEM_BASE64", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseWorkflowCheckoutsDoNotPersistCredentials()
    {
        foreach (var workflow in CredentialFreeCheckoutWorkflows)
        {
            var steps = ParseSteps(File.ReadAllLines(FindRepositoryFile(".github", "workflows", workflow)));
            var checkouts = steps.Where(step => step.Lines.Any(line => line.Contains("actions/checkout@", StringComparison.Ordinal))).ToArray();
            Assert.NotEmpty(checkouts);
            Assert.All(checkouts, step => Assert.Contains(
                step.Lines,
                line => line.Trim().Equals("persist-credentials: false", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void StepParserRejectsJobLevelSecretFixture()
    {
        string[] fixture =
        [
            "jobs:",
            "  release:",
            "    env:",
            "      KEY: ${{ secrets.KEY }}",
            "    steps:",
            "      - name: Use key",
            "        env:",
            "          KEY: ${{ secrets.KEY }}",
            "        run: echo",
        ];
        var steps = ParseSteps(fixture);
        Assert.False(IsInsideStep(steps, 3));
        Assert.True(IsInsideStep(steps, 7));
        Assert.Equal("Use key", Assert.Single(steps).Name);
    }

    private static bool IsInsideStep(IReadOnlyList<WorkflowStep> steps, int lineIndex) =>
        steps.Any(step => lineIndex >= step.Start && lineIndex < step.Start + step.Lines.Count);

    private static List<WorkflowStep> ParseSteps(string[] lines)
    {
        var steps = new List<WorkflowStep>();
        var stepsIndent = -1;
        WorkflowStep? current = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                current?.Lines.Add(line);
                continue;
            }

            if (trimmed == "steps:")
            {
                stepsIndent = indent;
                current = null;
                continue;
            }

            if (stepsIndent >= 0 && indent <= stepsIndent)
            {
                stepsIndent = -1;
                current = null;
            }

            if (stepsIndent >= 0 && trimmed.StartsWith("- ", StringComparison.Ordinal) && indent > stepsIndent
                && (current is null || indent <= current.Indent))
            {
                current = new WorkflowStep(index, indent, string.Empty, []);
                steps.Add(current);
            }

            if (current is null)
                continue;
            current.Lines.Add(line);
            var key = trimmed.StartsWith("- ", StringComparison.Ordinal) ? trimmed[2..] : trimmed;
            if (current.Name.Length == 0 && key.StartsWith("name:", StringComparison.Ordinal))
                steps[^1] = current = current with { Name = key["name:".Length..].Trim() };
        }

        return steps;
    }

    private sealed record WorkflowStep(int Start, int Indent, string Name, List<string> Lines);
}
