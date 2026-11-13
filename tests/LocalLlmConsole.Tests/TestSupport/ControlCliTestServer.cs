using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LocalLlmConsole.Tests;

public abstract partial class ManagerRegressionTestBase
{
    /// <summary>
    /// Runs llwmctl against a loopback stand-in for the control API and returns
    /// the exit code with every request the CLI sent, in order.
    /// </summary>
    protected static async Task<(int ExitCode, IReadOnlyList<string> Requests)> RunControlCliAsync(
        string[] command,
        Func<string, string> respond,
        Func<string, int>? status = null)
    {
        var root = CreateTempRoot();
        var connectionPath = Path.Combine(root, "control.json");
        var port = FreeTcpPort();
        File.WriteAllText(connectionPath, JsonSerializer.Serialize(new
        {
            version = 1,
            processId = Environment.ProcessId,
            baseUrl = $"http://127.0.0.1:{port}",
            protectedToken = "test-token",
            workspaceRoot = root,
            startedAt = DateTimeOffset.UtcNow
        }));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new ConcurrentQueue<string>();
        var stopping = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    var path = context.Request.Url?.AbsolutePath ?? "";
                    received.Enqueue($"{context.Request.HttpMethod} {path}");
                    var bytes = Encoding.UTF8.GetBytes(respond(path));
                    context.Response.StatusCode = status?.Invoke(path) ?? 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (Exception) when (Volatile.Read(ref stopping) == 1)
            {
                // Stop() can race the accept: depending on timing it surfaces as
                // HttpListenerException, ObjectDisposedException, or an invalid-handle
                // ArgumentException. Failures before teardown still propagate.
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var args = command.Concat(["--connection", connectionPath, "--compact"]).ToArray();
            var exitCode = await LocalLlmConsole.ControlCli.Program.Main(args);
            return (exitCode, received.ToArray());
        }
        finally
        {
            Volatile.Write(ref stopping, 1);
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        static int FreeTcpPort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }
}
