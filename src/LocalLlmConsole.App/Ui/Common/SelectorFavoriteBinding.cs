using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public static class SelectorFavoriteBinding
{
    public static OverviewPageControls ConfigureOverview(
        OverviewPageControls controls,
        Func<StateStore?> stateStore,
        Action<string>? reportError = null)
    {
        Configure(controls.ModelCombo, stateStore, SelectorFavoriteKind.Model, reportError);
        Configure(controls.LaunchProfileCombo, stateStore, SelectorFavoriteKind.LaunchProfile, reportError);
        return controls;
    }

    public static BenchmarksPageControls ConfigureBenchmarks(
        BenchmarksPageControls controls,
        Func<StateStore?> stateStore,
        Action<string>? reportError = null)
    {
        Configure(controls.Model, stateStore, SelectorFavoriteKind.Model, reportError);
        Configure(controls.Profile, stateStore, SelectorFavoriteKind.LaunchProfile, reportError);
        Configure(controls.Runtime, stateStore, SelectorFavoriteKind.Runtime, reportError);
        return controls;
    }

    public static LifetimePageBuildResult ConfigureLifetime(
        LifetimePageBuildResult page,
        Func<StateStore?> stateStore,
        Action<string>? reportError = null)
    {
        ConfigureLifetimeControls(page.Controls, stateStore, reportError);
        return page;
    }

    private static void ConfigureLifetimeControls(
        LifetimePageControls controls,
        Func<StateStore?> stateStore,
        Action<string>? reportError = null)
    {
        Configure(controls.ModelFilter, stateStore, SelectorFavoriteKind.Model, reportError);
        Configure(controls.ProfileFilter, stateStore, SelectorFavoriteKind.LaunchProfile, reportError);
        Configure(controls.RuntimeFilter, stateStore, SelectorFavoriteKind.Runtime, reportError);
    }

    public static LaunchSettingsPanelControls ConfigureLaunchSettings(
        LaunchSettingsPanelControls panel,
        Func<StateStore?> stateStore,
        Action<string>? reportError = null)
    {
        Configure(panel.RuntimeCombo, stateStore, SelectorFavoriteKind.Runtime, reportError);
        return panel;
    }

    public static void Configure(
        WpfComboBox combo,
        Func<StateStore?> stateStore,
        SelectorFavoriteKind kind,
        Action<string>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(combo);
        ArgumentNullException.ThrowIfNull(stateStore);
        if (combo is not SearchableComboBox searchable) return;

        searchable.LoadFavoriteKeysAsync = () => Store().ListSelectorFavoriteIdsAsync(kind);
        searchable.ToggleFavoriteAsync = itemId => Store().ToggleSelectorFavoriteAsync(kind, itemId);
        searchable.FavoriteOperationFailed = error => reportError?.Invoke(error.Message);
        return;

        StateStore Store()
            => stateStore() ?? throw new InvalidOperationException("The application state store is not available.");
    }
}
