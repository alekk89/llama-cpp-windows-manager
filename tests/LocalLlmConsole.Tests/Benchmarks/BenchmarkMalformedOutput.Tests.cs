using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class BenchmarkMalformedOutputTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"123\"")]
    [InlineData("1e999")]
    [InlineData("-1")]
    public void MalformedRequiredNumbersAreRejectedAndFollowingRowsRemainParseable(string value)
    {
        foreach (var field in new[] { "n_prompt", "n_gen", "avg_ts" })
        {
            var row = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["n_prompt"] = System.Text.Json.JsonSerializer.SerializeToElement(128),
                ["n_gen"] = System.Text.Json.JsonSerializer.SerializeToElement(32),
                ["avg_ts"] = System.Text.Json.JsonSerializer.SerializeToElement(100)
            };
            row[field] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
            Assert.False(Parse(System.Text.Json.JsonSerializer.Serialize(row), out var error));
            Assert.NotEmpty(error);
            Assert.True(Parse("{\"n_prompt\":128,\"n_gen\":32,\"avg_ts\":100}", out error), error);
        }
    }

    [Fact]
    public void OptionalWrongKindsDoNotTerminateParsingAndLargeWorkloadsDoNotOverflow()
    {
        Assert.True(Parse("""{"n_prompt":2147483647,"n_gen":1,"avg_ts":100,"n_threads":[],"model_size":{},"stddev_ts":"bad"}""", out var error), error);
    }

    private static bool Parse(string row, out string error)
        => BenchmarkResultService.TryParse(row, "model", "command", RuntimeMode.Native, RuntimeBackend.Cpu, out _, out error);
}
