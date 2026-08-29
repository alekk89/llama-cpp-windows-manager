using System.Text;
using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = new Arguments(args);
            if (command.Positionals.Count == 0 || command.Has("help") || command.Positionals[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine(ControlCliHelp.Text);
                return 0;
            }

            var connection = ControlCliDiscovery.Discover(command);
            using var http = new HttpClient
            {
                BaseAddress = new Uri(connection.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(65)
            };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                ControlCliDiscovery.Unprotect(connection.ProtectedToken));

            var request = ControlCliRequestFactory.Build(command);
            await ControlCliSelfSafety.EnsureAllowedAsync(http, command, request);
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path.TrimStart('/'));
            if (request.Body is not null)
                message.Content = new StringContent(request.Body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(message);
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode && ControlCliBenchmarkWaiter.ShouldWait(command))
            {
                var waited = await ControlCliBenchmarkWaiter.WaitAsync(http, text, command);
                ControlCliOutput.WriteResponse(waited.Text, command.Has("compact"));
                return waited.ExitCode;
            }

            ControlCliOutput.WriteResponse(text, command.Has("compact"));
            return response.IsSuccessStatusCode ? 0 : Math.Clamp((int)response.StatusCode, 1, 255);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(
                new ControlCliError(false, ex.Message),
                ControlCliJsonContext.Default.ControlCliError));
            return 1;
        }
    }
}
