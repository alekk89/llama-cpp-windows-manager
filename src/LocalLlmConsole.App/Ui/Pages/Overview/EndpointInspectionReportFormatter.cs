using System.Globalization;
using System.Text;

namespace LocalLlmConsole;

public static class EndpointInspectionReportFormatter
{
    public static string Format(EndpointInspectionReport report, bool apiKeyConfigured)
    {
        ArgumentNullException.ThrowIfNull(report);
        var output = new StringBuilder();
        Line(output, report.Kind == EndpointInspectionKind.Gateway
            ? Loc.T("EndpointInspection.GatewayReport")
            : Loc.T("EndpointInspection.DirectReport"));
        Field(output, Loc.T("EndpointInspection.Endpoint"), report.Endpoint);
        Field(output, Loc.T("EndpointInspection.Protocol"), Loc.T("EndpointInspection.ProtocolValue"));
        Field(output, Loc.T("EndpointInspection.Authentication"), apiKeyConfigured
            ? Loc.T("EndpointInspection.ApiKeyConfigured")
            : Loc.T("EndpointInspection.ApiKeyMissing"));
        Field(output, Loc.T("EndpointInspection.Health"), report.Health);
        Field(output, Loc.T("EndpointInspection.Inspected"), report.InspectedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));

        Section(output, report.Kind == EndpointInspectionKind.Gateway
            ? Loc.T("EndpointInspection.AdvertisedModels")
            : Loc.T("EndpointInspection.EndpointModel"));
        if (report.Models.Count == 0)
            Line(output, Loc.T("EndpointInspection.NoModels"));
        foreach (var model in report.Models)
        {
            var context = report.Kind == EndpointInspectionKind.Gateway
                ? model.ConfiguredContext
                : model.TrainingContext;
            var contextLabel = report.Kind == EndpointInspectionKind.Gateway
                ? Loc.T("EndpointInspection.ContextSize")
                : Loc.T("EndpointInspection.TrainingContext");
            var details = new[]
            {
                model.NameOrId(),
                Value(Loc.T("EndpointInspection.Profile"), model.Profile),
                Value(Loc.T("EndpointInspection.Owner"), model.Owner),
                context.HasValue ? Value(contextLabel, context.Value.ToString("N0", CultureInfo.InvariantCulture)) : "",
                model.ParameterCount.HasValue ? Value(Loc.T("EndpointInspection.Parameters"), model.ParameterCount.Value.ToString("N0", CultureInfo.InvariantCulture)) : "",
                model.SizeBytes.HasValue ? Value(Loc.T("Models.Col.Size"), DisplayFormatService.Bytes(model.SizeBytes.Value)) : ""
            };
            Line(output, $"- {string.Join(" | ", details.Where(value => !string.IsNullOrWhiteSpace(value)))}");
        }

        if (report.Defaults is { } defaults)
        {
            Section(output, Loc.T("EndpointInspection.ServerDefaults"));
            Field(output, Loc.T("EndpointInspection.ModelFile"), defaults.ModelFile);
            Field(output, Loc.T("EndpointInspection.ContextSize"), Number(defaults.ContextSize));
            Field(output, Loc.T("EndpointInspection.ParallelSlots"), Number(defaults.ParallelSlots));
            Field(output, Loc.T("EndpointInspection.DefaultMaxOutput"), Number(defaults.MaximumOutputTokens));
            Field(output, Loc.T("EndpointInspection.Reasoning"), defaults.Reasoning);
            Field(output, Loc.T("EndpointInspection.ReasoningFormat"), defaults.ReasoningFormat);
            Field(output, Loc.T("EndpointInspection.Vision"), defaults.Vision);
            Field(output, Loc.T("EndpointInspection.Speculative"), Boolean(defaults.Speculative));
            Field(output, Loc.T("EndpointInspection.Build"), defaults.Build);
        }

        if (report.Kind == EndpointInspectionKind.DirectModel)
        {
            Section(output, Loc.T("EndpointInspection.CurrentSlots"));
            if (report.Slots.Count == 0)
                Line(output, Loc.T("EndpointInspection.NoSlotState"));
            foreach (var slot in report.Slots)
            {
                var id = slot.Id?.ToString(CultureInfo.InvariantCulture) ?? "—";
                var state = slot.IsProcessing ? Loc.T("EndpointInspection.Processing") : Loc.T("EndpointInspection.Idle");
                Line(output, $"- {Loc.T("EndpointInspection.Slot")} {id} | {state} | {Loc.T("EndpointInspection.Context")}: {Number(slot.ContextSize)} | {Loc.T("EndpointInspection.MaxOutput")}: {Number(slot.MaximumOutputTokens)}");
            }
        }
        else
        {
            Section(output, Loc.T("EndpointInspection.ManagerRouting"));
            Field(output, Loc.T("EndpointInspection.Policy"), report.GatewayPolicy);
            Field(output, Loc.T("EndpointInspection.Exposure"), report.GatewayExposure);
            Section(output, Loc.T("EndpointInspection.LoadedThroughManager"));
            if (report.RunningModels.Count == 0)
                Line(output, Loc.T("EndpointInspection.NoLoadedRuntime"));
            foreach (var model in report.RunningModels)
                Line(output, $"- {Value(Loc.T("Overview.SessionsCol.Model"), string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name)} | {Value(Loc.T("Overview.SessionsCol.State"), model.Status)} | {Value(Loc.T("Overview.SessionsCol.Runtime"), model.Runtime)} | {Value(Loc.T("EndpointInspection.DirectEndpoint"), model.Endpoint)}");
        }

        if (report.UnavailableSources.Count > 0)
        {
            Section(output, Loc.T("EndpointInspection.UnavailableDetails"));
            foreach (var unavailable in report.UnavailableSources)
                Line(output, $"- {unavailable}");
        }

        return output.ToString().TrimEnd();
    }

    private static void Section(StringBuilder output, string title)
    {
        output.AppendLine();
        Line(output, title);
    }

    private static void Field(StringBuilder output, string label, string value)
        => Line(output, $"{label}: {Empty(value)}");

    private static string Value(string label, string value)
        => string.IsNullOrWhiteSpace(value) ? "" : $"{label}: {value}";

    private static string Number(int? value)
        => value?.ToString("N0", CultureInfo.InvariantCulture) ?? Loc.T("EndpointInspection.NotReported");

    private static string Boolean(bool? value)
        => value.HasValue ? value.Value ? Loc.T("Pref.Yes") : Loc.T("Pref.No") : Loc.T("EndpointInspection.NotReported");

    private static string Empty(string value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static void Line(StringBuilder output, string value)
        => output.AppendLine(value);

    private static string NameOrId(this EndpointInspectionModel model)
        => !string.IsNullOrWhiteSpace(model.Name)
            ? $"{model.Name} ({model.Id})"
            : Empty(model.Id);
}
