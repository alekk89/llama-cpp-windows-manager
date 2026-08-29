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

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedValue is not string lang || lang == Localization.Loc.CurrentLanguage)
            return;

        Localization.Loc.LoadLanguage(lang);
        _settings = _settings with { UiCulture = lang };

        ApplyLocalizedXamlStrings();
        RefreshCurrentPage();
        if (_stateStore is null) return;
        try
        {
            await _stateStore.SaveAppSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Could not persist language selection: {ex}");
            SetStatus(Localization.Loc.T("Status.LanguageSaveFailed", ex.Message));
        }
    }

    private void ApplyLocalizedXamlStrings()
    {
        var culture = Localization.Loc.FormatCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        FlowDirection = Localization.Loc.IsRightToLeft(Localization.Loc.CurrentLanguage)
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        ApplyStaticButtonToolTips();
        System.Windows.Automation.AutomationProperties.SetName(LanguageCombo, Localization.Loc.T("Status.Language"));
        System.Windows.Automation.AutomationProperties.SetHelpText(LanguageCombo, Localization.Loc.T("Status.Language"));
        System.Windows.Automation.AutomationProperties.SetName(AppStatusText, Localization.Loc.T("Status.CurrentActionLabel"));

        // Navigation buttons
        OverviewNavButton.Content = Localization.Loc.T("Nav.Overview");
        ModelsNavButton.Content = Localization.Loc.T("Nav.Models");
        RuntimesNavButton.Content = Localization.Loc.T("Nav.Runtimes");
        BenchmarksNavButton.Content = Localization.Loc.T("Nav.Benchmarks");
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

        _trayProfileMenu?.Close();
    }
}
