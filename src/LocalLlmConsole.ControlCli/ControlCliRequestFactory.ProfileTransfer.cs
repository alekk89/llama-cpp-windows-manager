using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static partial class ControlCliRequestFactory
{
    private static ControlRequest ProfileImportRequest(string model, Arguments args)
    {
        var path = Required(args, "file");
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidOperationException("Profile bundle exceeds 4 MiB.");
        var conflict = (args.Value("conflict") ?? "keep-both").ToLowerInvariant() switch
        {
            "keep-both" => 0,
            "replace" => 1,
            "skip" => 2,
            _ => throw new InvalidOperationException("Use --conflict keep-both|replace|skip.")
        };
        var text = File.ReadAllText(path);
        var document = JsonNode.Parse(text, documentOptions: new System.Text.Json.JsonDocumentOptions
        { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        var count = document["profiles"]?.AsArray().Count ?? 0;
        var choices = new JsonArray();
        for (var index = 0; index < count; index++)
            choices.Add(new JsonObject { ["index"] = index, ["action"] = conflict, ["runtimeId"] = args.Value("runtime") });
        return Post($"/api/v1/models/{model}/profiles/import", new JsonObject
        {
            ["text"] = text,
            ["dryRun"] = !args.Has("apply") || args.Has("dry-run"),
            ["choices"] = choices
        });
    }
}
