namespace LocalLlmConsole.Services;

internal static class ModelRuntimeCommandLocalization
{
    public static string Message(ModelRuntimeCommandStatus status)
        => status switch
        {
            ModelRuntimeCommandStatus.SelectModelFirst => Loc.T("Status.ModelRuntime.SelectModelFirst"),
            ModelRuntimeCommandStatus.ChooseModelFirst => Loc.T("Status.ModelRuntime.ChooseModelFirst"),
            ModelRuntimeCommandStatus.ModelAlreadyActive => Loc.T("Status.ModelRuntime.ModelAlreadyActive"),
            ModelRuntimeCommandStatus.LoadBeforeRestart => Loc.T("Status.ModelRuntime.LoadBeforeRestart"),
            ModelRuntimeCommandStatus.AppStarting => Loc.T("Status.ModelRuntime.AppStarting"),
            ModelRuntimeCommandStatus.ChooseLoadedModelToUnload => Loc.T("Status.ModelRuntime.ChooseLoadedModelToUnload"),
            _ => ""
        };
}
