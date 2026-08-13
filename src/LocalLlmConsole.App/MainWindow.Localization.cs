using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private void PopulateLanguageSelector()
    {
        var languages = Localization.Loc.AvailableLanguages()
            .Select(code => new LanguageItem(code, Localization.Loc.LanguageDisplayName(code)))
            .ToList();
        LanguageCombo.ItemsSource = languages;
        LanguageCombo.DisplayMemberPath = "Name";
        LanguageCombo.SelectedValuePath = "Code";
        LanguageCombo.SelectedValue = Localization.Loc.CurrentLanguage;
    }

    sealed class LanguageItem
    {
        public string Code { get; }
        public string Name { get; }
        public LanguageItem(string code, string name) => (Code, Name) = (code, name);
        public override string ToString() => Name;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedValue is not string lang || lang == Localization.Loc.CurrentLanguage)
            return;

        Localization.Loc.LoadLanguage(lang);
        _settings = _settings with { UiCulture = lang };

        // Persist immediately (fire-and-forget — non-critical)
        if (_stateStore is not null)
            _ = _stateStore.SaveAppSettingsAsync(_settings);

        ApplyLocalizedXamlStrings();
        RefreshCurrentPage();
    }

    private void ApplyLocalizedXamlStrings()
    {
        // Navigation buttons
        OverviewNavButton.Content = Localization.Loc.T("Nav.Overview");
        ModelsNavButton.Content = Localization.Loc.T("Nav.Models");
        RuntimesNavButton.Content = Localization.Loc.T("Nav.Runtimes");
        SettingsNavButton.Content = Localization.Loc.T("Nav.Settings");
        LifetimeNavButton.Content = Localization.Loc.T("Nav.Lifetime");
        LogsNavButton.Content = Localization.Loc.T("Nav.Logs");
        ToolsNavLabel.Text = Localization.Loc.T("Nav.Tools");
        WindowsNavButton.Content = Localization.Loc.T("Nav.Windows");
        WslLinuxNavButton.Content = Localization.Loc.T("Nav.WslLinux");
        UpdatesNavButton.Content = Localization.Loc.T("Nav.CheckForUpdates");
        HelpNavButton.Content = Localization.Loc.T("Nav.Help");

        // Status panel
        CurrentStatusLabel.Text = Localization.Loc.T("Status.CurrentActionLabel");

        // Title bar
        Title = $"{Localization.Loc.T("App.Title")} {AppVersionText.Text}";
    }
}
