using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfLabel = System.Windows.Controls.Label;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public sealed record ModelLaunchProfileCopyDialogResult(
    ModelRecord? TargetModel,
    string Name,
    string RuntimeId,
    bool Accepted);

public static partial class ModelLaunchProfileCopyDialogFactory
{
    public static ModelLaunchProfileCopyDialogResult Show(
        WpfWindow owner,
        ModelRecord sourceModel,
        NamedModelLaunchProfile sourceProfile,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<RuntimeChoice> runtimeChoices,
        Func<StateStore?> stateStore)
    {
        var accepted = false;
        ModelRecord? targetModel = null;
        var name = sourceProfile.Name;
        var runtimeId = sourceProfile.Settings.RuntimeId;

        var dialog = Dialog(owner, Loc.T("Launch.CopyProfileToAnotherModel.Title"), 520);
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerPanel = Header(
            Loc.T("Launch.CopyProfileToAnotherModel.Heading", sourceModel.Name, sourceProfile.Name),
            Loc.T("Launch.CopyProfileToAnotherModel.Description"));
        Grid.SetColumn(headerPanel, 0);
        headerRow.Children.Add(headerPanel);
        var closeButton = new WpfButton
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            FontSize = 13,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeButton.Click += (_, _) => { dialog.DialogResult = false; };
        Grid.SetColumn(closeButton, 1);
        headerRow.Children.Add(closeButton);
        Grid.SetRow(headerRow, 0);
        rootGrid.Children.Add(headerRow);

        var layout = new Grid();
        for (var i = 0; i < 7; i++)
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(layout, 1);
        rootGrid.Children.Add(layout);

        var modelChoices = models
            .Where(model => !model.Id.Equals(sourceModel.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Select(model => new ModelChoice(model.Id, model.Name, model.ModelPath))
            .ToArray();
        var modelCombo = new WpfComboBox
        {
            ItemsSource = modelChoices,
            MinWidth = 360,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            ItemTemplate = ModelItemTemplate()
        };
        Grid.SetRow(modelCombo, 1);
        layout.Children.Add(modelCombo);

        var nameBox = new WpfTextBox
        {
            Text = name,
            MinWidth = 360,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            MaxLength = 80
        };
        nameBox.TextChanged += (_, _) => { name = nameBox.Text; };
        Grid.SetRow(nameBox, 3);
        layout.Children.Add(nameBox);

        var validRuntimes = runtimeChoices
            .Where(r => !r.Label.StartsWith("Missing runtime", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runtimeCombo = new SearchableComboBox
        {
            ItemsSource = validRuntimes,
            ItemTemplate = RuntimeItemTemplate(),
            SearchTextSelector = item => (item as RuntimeChoice)?.DisplayName ?? "",
            FavoriteKeySelector = item => (item as RuntimeChoice)?.Id ?? "",
            SelectedValuePath = nameof(RuntimeChoice.Id),
            MinWidth = 360,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Height = 28,
            MinHeight = 28
        };
        SelectorFavoriteBinding.Configure(runtimeCombo, stateStore, SelectorFavoriteKind.Runtime);
        if (validRuntimes.Any(r => r.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase)))
            runtimeCombo.SelectedItem = validRuntimes.First(r => r.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase));
        Grid.SetRow(runtimeCombo, 5);
        layout.Children.Add(runtimeCombo);

        var modelLabel = FieldLabel("Launch.CopyProfileToAnotherModel.ModelLabel");
        Grid.SetRow(modelLabel, 0);
        layout.Children.Add(modelLabel);
        var nameLabel = FieldLabel("Launch.CopyProfileToAnotherModel.NameLabel");
        Grid.SetRow(nameLabel, 2);
        layout.Children.Add(nameLabel);
        var runtimeLabel = FieldLabel("Launch.RuntimeLabel");
        Grid.SetRow(runtimeLabel, 4);
        layout.Children.Add(runtimeLabel);

        var footer = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var save = Button(Loc.T("Common.Save"), primary: true);
        var cancel = Button(Loc.T("Common.Cancel"));
        save.IsDefault = true;
        cancel.IsCancel = true;
        save.Click += (_, _) =>
        {
            var selectedChoice = modelChoices.FirstOrDefault(choice => choice.Id.Equals((modelCombo.SelectedItem as ModelChoice)?.Id, StringComparison.OrdinalIgnoreCase));
            targetModel = models.FirstOrDefault(model => model.Id.Equals(selectedChoice?.Id, StringComparison.OrdinalIgnoreCase));
            if (targetModel is null) return;
            name = nameBox.Text.Trim();
            runtimeId = (runtimeCombo.SelectedItem as RuntimeChoice)?.Id ?? "";
            accepted = true;
            dialog.DialogResult = true;
        };
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 6);
        layout.Children.Add(footer);

        dialog.Content = Frame(rootGrid);
        dialog.ShowDialog();

        return new ModelLaunchProfileCopyDialogResult(targetModel, name, runtimeId, accepted);
    }

    private static DataTemplate ModelItemTemplate()
    {
        var template = new DataTemplate();
        var factory = new System.Windows.FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, WpfOrientation.Horizontal);
        var nameBlock = new System.Windows.FrameworkElementFactory(typeof(TextBlock));
        nameBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ModelChoice.Name)));
        nameBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        nameBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        nameBlock.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
        nameBlock.SetValue(TextBlock.ForegroundProperty, (WpfBrush)WpfApplication.Current.Resources["TextMain"]);
        var pathBlock = new System.Windows.FrameworkElementFactory(typeof(TextBlock));
        pathBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ModelChoice.Path)));
        pathBlock.SetValue(TextBlock.FontSizeProperty, 11.0);
        pathBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        pathBlock.SetValue(TextBlock.ForegroundProperty, (WpfBrush)WpfApplication.Current.Resources["TextSoft"]);
        pathBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        factory.AppendChild(nameBlock);
        factory.AppendChild(pathBlock);
        template.VisualTree = factory;
        return template;
    }

    private static TextBlock FieldLabel(string key)
    {
        var label = new TextBlock
        {
            Text = Loc.T(key),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            Margin = new Thickness(0, 0, 0, 4)
        };
        return label;
    }

    private static DataTemplate RuntimeItemTemplate()
    {
        var template = new DataTemplate();
        var factory = new System.Windows.FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(RuntimeChoice.DisplayName)));
        factory.SetValue(TextBlock.FontSizeProperty, 12.0);
        factory.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe UI"));
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        template.VisualTree = factory;
        return template;
    }

    private static FrameworkElement Header(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            Margin = new Thickness(0, 3, 0, 0)
        });
        return panel;
    }

    private static WpfWindow Dialog(WpfWindow owner, string title, double width)
        => new()
        {
            Owner = owner,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            MinWidth = 460,
            MinHeight = 200,
            MaxHeight = SystemParameters.WorkArea.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false,
            FlowDirection = owner.FlowDirection
        };

    private static WpfBorder Frame(UIElement child)
        => new()
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBack"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = child
        };

    private static WpfButton Button(string text, bool primary = false)
    {
        var button = new WpfButton
        {
            Content = text,
            MinWidth = 74,
            Height = 29,
            MinHeight = 29,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(6, 0, 0, 0)
        };
        if (primary) VisualRole.SetButtonRole(button, VisualRole.Primary);
        return button;
    }
}

public sealed record ModelChoice(
    string Id,
    string Name,
    string Path);


