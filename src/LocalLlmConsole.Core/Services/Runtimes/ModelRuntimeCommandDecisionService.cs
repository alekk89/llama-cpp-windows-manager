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

public enum ModelRuntimeCommandStatus
{
    None,
    SelectModelFirst,
    ChooseModelFirst,
    ModelAlreadyActive,
    LoadBeforeRestart,
    AppStarting,
    ChooseLoadedModelToUnload
}

public sealed record ModelRuntimeLoadCommand(
    ModelRuntimeLoadCommandKind Kind,
    ModelRuntimeCommandStatus Status = ModelRuntimeCommandStatus.None);

public sealed record ModelRuntimeUnloadCommand(
    ModelRuntimeUnloadCommandKind Kind,
    ModelRuntimeCommandStatus Status = ModelRuntimeCommandStatus.None);

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
            return Status(ModelRuntimeCommandStatus.SelectModelFirst);
        if (!restart && modelActive)
            return Status(ModelRuntimeCommandStatus.ModelAlreadyActive);
        if (restart && !modelLoaded)
            return Status(ModelRuntimeCommandStatus.LoadBeforeRestart);
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
            return Status(ModelRuntimeCommandStatus.ChooseModelFirst);
        if (modelActive && selectedProfileLoaded)
            return Status(ModelRuntimeCommandStatus.ModelAlreadyActive);
        if (modelLoaded && selectedProfileLoaded)
            return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.SwitchLoaded);
        if (!appReady)
            return Status(ModelRuntimeCommandStatus.AppStarting);

        return new ModelRuntimeLoadCommand(ModelRuntimeLoadCommandKind.Continue);
    }

    public ModelRuntimeUnloadCommand PlanOverviewUnload(ModelRecord? model, bool modelLoaded)
    {
        if (model is null || !modelLoaded)
            return new ModelRuntimeUnloadCommand(
                ModelRuntimeUnloadCommandKind.Status,
                ModelRuntimeCommandStatus.ChooseLoadedModelToUnload);

        return new ModelRuntimeUnloadCommand(ModelRuntimeUnloadCommandKind.Stop);
    }

    private static ModelRuntimeLoadCommand Status(ModelRuntimeCommandStatus status)
        => new(ModelRuntimeLoadCommandKind.Status, status);
}
