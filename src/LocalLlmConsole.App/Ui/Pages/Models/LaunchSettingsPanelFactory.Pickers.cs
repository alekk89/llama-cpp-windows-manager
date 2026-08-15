using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static partial class LaunchSettingsPanelFactory
{
    private static Grid VisionProjectorPicker(WpfTextBox textBox, Func<Task> chooseAsync, out WpfButton button)
    {
        var grid = new Grid();
        textBox.Visibility = Visibility.Collapsed;
        textBox.Width = 0;
        textBox.MinWidth = 0;
        textBox.IsTabStop = false;
        grid.Children.Add(textBox);

        WpfButton? pickerButton = null;
        pickerButton = DropDownPickerButton(VisionProjectorSelection.DisplayText(textBox.Text), () => OpenPickerMenu(pickerButton));
        var finalButton = pickerButton;
        finalButton.MinWidth = 156;
        finalButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        finalButton.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        finalButton.ContextMenu = VisionProjectorMenu(textBox, chooseAsync);
        UpdateVisionProjectorButton(finalButton, textBox.Text);
        textBox.TextChanged += (_, _) => UpdateVisionProjectorButton(finalButton, textBox.Text);
        button = finalButton;
        grid.Children.Add(finalButton);
        return grid;
    }

    private static ContextMenu VisionProjectorMenu(WpfTextBox textBox, Func<Task> chooseAsync)
    {
        var menu = PickerContextMenu();
        var auto = new MenuItem { Header = Loc.T("Picker.Vision.AutoDetectHead") };
        auto.Click += (_, _) => textBox.Text = "";
        var embedded = new MenuItem { Header = Loc.T("Picker.Vision.Embedded") };
        embedded.Click += (_, _) => textBox.Text = VisionProjectorSelection.EmbeddedToken;
        var choose = new MenuItem { Header = Loc.T("Picker.ChooseGgufFile") };
        choose.Click += async (_, _) => await chooseAsync();

        menu.Items.Add(auto);
        menu.Items.Add(embedded);
        menu.Items.Add(new Separator());
        menu.Items.Add(choose);
        return menu;
    }

    private static void UpdateVisionProjectorButton(WpfButton button, string value)
    {
        button.Content = VisionProjectorSelection.DisplayText(value);
        button.ToolTip = TooltipText(VisionProjectorSelection.Tooltip(value));
    }

    private static Grid DraftModelPicker(WpfTextBox textBox, Func<Task> chooseAsync, out WpfButton button)
    {
        var grid = new Grid();
        textBox.Visibility = Visibility.Collapsed;
        textBox.Width = 0;
        textBox.MinWidth = 0;
        textBox.IsTabStop = false;
        grid.Children.Add(textBox);

        WpfButton? pickerButton = null;
        pickerButton = DropDownPickerButton(DraftModelButtonText(textBox.Text), () => OpenPickerMenu(pickerButton));
        var finalButton = pickerButton;
        finalButton.MinWidth = 156;
        finalButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        finalButton.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        finalButton.ContextMenu = DraftModelMenu(textBox, chooseAsync);
        UpdateDraftModelButton(finalButton, textBox.Text);
        textBox.TextChanged += (_, _) => UpdateDraftModelButton(finalButton, textBox.Text);
        button = finalButton;
        grid.Children.Add(finalButton);
        return grid;
    }

    private static ContextMenu DraftModelMenu(WpfTextBox textBox, Func<Task> chooseAsync)
    {
        var menu = PickerContextMenu();
        var auto = new MenuItem { Header = Loc.T("Picker.Draft.AutoDetect") };
        auto.Click += (_, _) => textBox.Text = "";
        var choose = new MenuItem { Header = Loc.T("Picker.ChooseGgufFile") };
        choose.Click += async (_, _) => await chooseAsync();

        menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        menu.Items.Add(choose);
        return menu;
    }

    private static void UpdateDraftModelButton(WpfButton button, string value)
    {
        button.Content = DraftModelButtonText(value);
        button.ToolTip = TooltipText(DraftModelTooltip(value));
    }

    private static string DraftModelButtonText(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return Loc.T("Picker.Draft.DefaultText");
        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? Loc.T("Picker.Draft.SelectedText") : fileName;
    }

    private static string DraftModelTooltip(string value)
    {
        var trimmed = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? Loc.T("Tooltip.Current.DraftAuto")
            : $"{Loc.T("Tooltip.DraftModelPath")} {trimmed}{Environment.NewLine}{Loc.T("Tooltip.DraftModelClickChange")}";
    }

    private static Grid MtpHeadPicker(WpfTextBox textBox, Func<Task> chooseAsync, out WpfButton button)
    {
        var grid = new Grid();
        textBox.Visibility = Visibility.Collapsed;
        textBox.Width = 0;
        textBox.MinWidth = 0;
        textBox.IsTabStop = false;
        grid.Children.Add(textBox);

        WpfButton? pickerButton = null;
        pickerButton = DropDownPickerButton(MtpHeadButtonText(textBox.Text), () => OpenPickerMenu(pickerButton));
        var finalButton = pickerButton;
        finalButton.MinWidth = 156;
        finalButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        finalButton.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
        finalButton.ContextMenu = MtpHeadMenu(textBox, chooseAsync);
        UpdateMtpHeadButton(finalButton, textBox.Text);
        textBox.TextChanged += (_, _) => UpdateMtpHeadButton(finalButton, textBox.Text);
        button = finalButton;
        grid.Children.Add(finalButton);
        return grid;
    }

    private static ContextMenu MtpHeadMenu(WpfTextBox textBox, Func<Task> chooseAsync)
    {
        var menu = PickerContextMenu();
        var auto = new MenuItem { Header = Loc.T("Picker.Mtp.AutoDetectHead") };
        auto.Click += (_, _) => textBox.Text = "";
        var choose = new MenuItem { Header = Loc.T("Picker.ChooseGgufFile") };
        choose.Click += async (_, _) => await chooseAsync();

        menu.Items.Add(auto);
        menu.Items.Add(new Separator());
        menu.Items.Add(choose);
        return menu;
    }

    private static void UpdateMtpHeadButton(WpfButton button, string value)
    {
        button.Content = MtpHeadButtonText(value);
        button.ToolTip = TooltipText(MtpHeadTooltip(value));
    }

    private static string MtpHeadButtonText(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return Loc.T("Picker.Mtp.DefaultText");
        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? Loc.T("Picker.Mtp.SelectedText") : fileName;
    }

    private static string MtpHeadTooltip(string value)
    {
        var trimmed = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? Loc.T("Tooltip.MtpHeadDefault")
            : $"{Loc.T("Tooltip.MtpHeadPath")} {trimmed}{Environment.NewLine}{Loc.T("Tooltip.MtpHeadClickChange")}";
    }

    private static WpfButton DropDownPickerButton(string text, Func<Task> click)
    {
        var button = Button(text, click);
        button.Style = (Style)WpfApplication.Current.Resources["DropDownPickerButton"];
        button.Height = 28;
        button.MinHeight = 28;
        button.Margin = new Thickness(0);
        return CrispCompactControl(button);
    }

    private static Task OpenPickerMenu(WpfButton? button)
    {
        if (button?.ContextMenu is not { } menu) return Task.CompletedTask;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        if (button.ActualWidth > 0)
            menu.Width = button.ActualWidth;
        menu.IsOpen = true;
        return Task.CompletedTask;
    }

    private static ContextMenu PickerContextMenu()
        => new() { Placement = PlacementMode.Bottom };
}
