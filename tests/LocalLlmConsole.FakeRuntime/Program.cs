using System.Net;
using System.Text;
using System.Text.Json;

var options = ParseArguments(args);
if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("llama-bench fake: --model --offline --output --progress --repetitions --delay --no-warmup --list-devices --n-prompt --n-gen -pg --n-depth --threads --batch-size --ubatch-size --n-gpu-layers --n-cpu-moe --flash-attn --cache-type-k --cache-type-v --no-kv-offload --split-mode --main-gpu --device --tensor-split --load-mode --fit-target --fit-ctx --numa --prio --cpu-mask --cpu-strict --poll --embeddings --no-op-offload --no-host --override-tensor --fake-delay-ms");
    return 0;
}
if (args.Contains("--list-devices", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("CPU: Fake CPU device");
    return 0;
}
if (Value(options, "--output", "").Equals("jsonl", StringComparison.OrdinalIgnoreCase))
{
    await EmitBenchmarkAsync(options);
    return 0;
}
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

static async Task EmitBenchmarkAsync(IReadOnlyDictionary<string, string> options)
{
    var modelPath = Value(options, "--model", "fake-model.gguf");
    var prompts = Integers(Value(options, "--n-prompt", "512"));
    var generations = Integers(Value(options, "--n-gen", "128"));
    var depths = Integers(Value(options, "--n-depth", "0"));
    var rows = prompts.Where(value => value > 0).Select(value => (Prompt: value, Generation: 0))
        .Concat(generations.Where(value => value > 0).Select(value => (Prompt: 0, Generation: value)))
        .ToArray();
    foreach (var depth in depths.DefaultIfEmpty(0))
        foreach (var row in rows)
        {
            Console.Error.WriteLine($"progress {row.Prompt}/{row.Generation} depth {depth}");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                build_commit = "fake-commit",
                build_number = 1,
                cpu_info = "Fake CPU",
                gpu_info = "",
                backends = "CPU",
                model_filename = modelPath,
                model_type = "fake",
                model_size = 1L,
                model_n_params = 1L,
                n_batch = Integer(options, "--batch-size", 2048),
                n_ubatch = Integer(options, "--ubatch-size", 512),
                n_threads = Integer(options, "--threads", 4),
                type_k = "f16",
                type_v = "f16",
                n_gpu_layers = Integer(options, "--n-gpu-layers", -1),
                n_cpu_moe = 0,
                split_mode = "layer",
                main_gpu = 0,
                no_kv_offload = false,
                flash_attn = "auto",
                devices = "",
                tensor_split = "",
                load_mode = "mmap",
                n_prompt = row.Prompt,
                n_gen = row.Generation,
                n_depth = depth,
                test_time = DateTimeOffset.UtcNow.ToString("O"),
                avg_ns = 1_000_000L,
                stddev_ns = 10_000L,
                avg_ts = row.Prompt > 0 ? 1000.0 : 50.0,
                stddev_ts = 1.0
            }));
            await Task.Delay(Integer(options, "--fake-delay-ms", 10));
        }
}

static int[] Integers(string value)
    => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(token => int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

static int Integer(IReadOnlyDictionary<string, string> options, string key, int fallback)
    => int.TryParse(Value(options, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), out var value) ? value : fallback;

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
            case "/v1/chat/completions":
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    var body = await reader.ReadToEndAsync();
                    using var document = JsonDocument.Parse(body);
                    var requested = document.RootElement.TryGetProperty("max_tokens", out var maxTokens)
                                    && maxTokens.TryGetInt32(out var parsedMaxTokens)
                        ? Math.Max(parsedMaxTokens, 1)
                        : 16;
                    await WriteJsonAsync(context.Response, new
                    {
                        id = "fake-completion",
                        @object = "chat.completion",
                        model,
                        choices = new[] { new { index = 0, message = new { role = "assistant", content = "fake" }, finish_reason = "length" } },
                        usage = new { prompt_tokens = 32, completion_tokens = requested, total_tokens = 32 + requested },
                        timings = new
                        {
                            prompt_n = 32,
                            predicted_n = requested,
                            prompt_per_second = 1000.0,
                            predicted_per_second = 50.0,
                            draft_n = requested * 2,
                            draft_n_accepted = requested
                        }
                    });
                }
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
