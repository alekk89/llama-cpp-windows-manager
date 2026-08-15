namespace LocalLlmConsole.Services;

public enum ModelRuntimeLoadCommandKind
{
    Status,
    SwitchLoaded,
    RenderLaunchSettings,
    Continue
}

public enum ModelRuntimeUnloadCommandKind
{
    Status,
    Stop
}

public sealed record ModelRuntimeLoadCommand(
    ModelRuntimeLoadCommandKind Kind,
    string StatusMessage = "");

public sealed record ModelRuntimeUnloadCommand(
    ModelRuntimeUnloadCommandKind Kind,
    string StatusMessage = "");

public sealed class ModelRuntimeCommandDecisionService
{
    public ModelRuntimeLoadCommand PlanSelectedLoad(
        ModelRecord? model,
        bool restart,
        bool modelLoaded,
        bool modelActive,
        bool launchSettingsLoaded)
    {
        if (model is null)
            return Status("Select a model first.");
        if (!restart && modelActive)
            return Status("Selected model is already active.");
        if (restart && !modelLoaded)
            return Status("Load the selected model before restarting it.");
        if (!restart && modelLoaded)
            return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.SwitchLoaded);
        if (!launchSettingsLoaded)
            return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.RenderLaunchSettings);

        return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.Continue);
    }

    public ModelRuntimeLoadCommand PlanOverviewLoad(
        ModelRecord? model,
        bool modelLoaded,
        bool modelActive,
        bool appReady,
        bool selectedProfileLoaded)
    {
        if (model is null)
            return Status("Choose a model first.");
        if (modelActive && selectedProfileLoaded)
            return Status("Selected model is already active.");
        if (modelLoaded && selectedProfileLoaded)
            return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.SwitchLoaded);
        if (!appReady)
            return Status("App is still starting.");

        return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.Continue);
    }

    public ModelRuntimeUnloadCommand PlanSelectedUnload(ModelRecord? model, bool modelLoaded)
    {
        if (model is null || !modelLoaded)
            return new ModelRuntimeUnloadCommand(
                ModelRuntimeUnloadCommandKind.Status,
                "Select the loading or loaded model to unload it.");

        return new ModelRuntimeUnloadCommand(ModelRuntimeUnloadCommandKind.Stop);
    }

    public ModelRuntimeUnloadCommand PlanOverviewUnload(ModelRecord? model, bool modelLoaded)
    {
        if (model is null || !modelLoaded)
            return new ModelRuntimeUnloadCommand(
                ModelRuntimeUnloadCommandKind.Status,
                "Choose the loading or loaded model to unload it.");

        return new ModelRuntimeUnloadCommand(ModelRuntimeUnloadCommandKind.Stop);
    }

    private static ModelRuntimeLoadCommand Status(string message)
        => new(ModelRuntimeLoadCommandKind.Status, message);
}
