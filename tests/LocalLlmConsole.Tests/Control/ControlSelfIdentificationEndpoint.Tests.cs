using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlSelfIdentificationEndpointTests
{
    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("app.localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.5.5.5", true)]
    [InlineData("[::1]", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("api.anthropic.com", false)]
    [InlineData("192.0.2.10", false)]
    [InlineData("", false)]
    public void ThisMachineMeansLoopbackLocalNamesAndOwnAddresses(string host, bool expected)
        => Assert.Equal(expected, ControlSelfIdentification.IsThisMachine(host));

    [Fact]
    public void ThisMachineIncludesItsOwnName()
        => Assert.True(ControlSelfIdentification.IsThisMachine(Environment.MachineName));
}
