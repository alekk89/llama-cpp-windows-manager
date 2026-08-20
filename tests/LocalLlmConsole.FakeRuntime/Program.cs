using System.Net;
using System.Text;
using System.Text.Json;

var options = ParseArguments(args);
if (options.TryGetValue("--fake-exit-code", out var configuredExitCode)
    && int.TryParse(configuredExitCode, out var exitCode))
{
    Console.Error.WriteLine($"fake runtime deliberately exited with code {exitCode}");
    return exitCode;
}
var host = Value(options, "--host", "127.0.0.1");
var port = int.Parse(Value(options, "--port", "8081"), System.Globalization.CultureInfo.InvariantCulture);
var modelPath = Value(options, "--model", "fake-model.gguf");
var model = Path.GetFileNameWithoutExtension(modelPath);
var apiKey = (Environment.GetEnvironmentVariable("LLAMA_API_KEY") ?? "").Trim();

using var listener = new HttpListener();
listener.Prefixes.Add($"http://{host}:{port}/");

try
{
    listener.Start();
}
catch (HttpListenerException ex)
{
    Console.Error.WriteLine($"fake runtime could not listen on {host}:{port}: {ex.Message}");
    return 2;
}

Console.WriteLine($"HTTP server listening on http://{host}:{port}");

while (listener.IsListening)
{
    HttpListenerContext context;
    try
    {
        context = await listener.GetContextAsync();
    }
    catch (HttpListenerException) when (!listener.IsListening)
    {
        break;
    }
    catch (ObjectDisposedException)
    {
        break;
    }

    _ = Task.Run(() => RespondAsync(context, apiKey, model));
}

return 0;

static async Task RespondAsync(HttpListenerContext context, string apiKey, string model)
{
    try
    {
        if (!string.IsNullOrWhiteSpace(apiKey)
            && !string.Equals(context.Request.Headers["Authorization"], $"Bearer {apiKey}", StringComparison.Ordinal))
        {
            await WriteAsync(context.Response, 401, "application/json", "{\"error\":\"unauthorized\"}");
            return;
        }

        var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
        switch (path)
        {
            case "/health":
                await WriteAsync(context.Response, 200, "application/json", "{\"status\":\"ok\"}");
                break;
            case "/v1/models":
                await WriteJsonAsync(context.Response, new
                {
                    @object = "list",
                    data = new[] { new { id = model, @object = "model", owned_by = "fake-runtime" } }
                });
                break;
            case "/props":
                await WriteJsonAsync(context.Response, new { model_path = model, total_slots = 1 });
                break;
            case "/slots":
                await WriteJsonAsync(context.Response, new[] { new { id = 0, state = "idle" } });
                break;
            case "/metrics":
                await WriteAsync(context.Response, 200, "text/plain; version=0.0.4", "llamacpp:requests_processing 0\n");
                break;
            default:
                await WriteAsync(context.Response, 404, "application/json", "{\"error\":\"not found\"}");
                break;
        }
    }
    catch
    {
        try { context.Response.Abort(); }
        catch { }
    }
}

static Task WriteJsonAsync(HttpListenerResponse response, object value)
    => WriteAsync(response, 200, "application/json", JsonSerializer.Serialize(value));

static async Task WriteAsync(HttpListenerResponse response, int statusCode, string contentType, string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    response.StatusCode = statusCode;
    response.ContentType = contentType;
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
    response.Close();
}

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
            continue;
        if (index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            values[argument] = arguments[++index];
        else
            values[argument] = "true";
    }
    return values;
}

static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback)
    => values.TryGetValue(key, out var value) ? value : fallback;
