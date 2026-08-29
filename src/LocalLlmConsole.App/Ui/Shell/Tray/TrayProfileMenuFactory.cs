using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfPath = System.Windows.Shapes.Path;

namespace LocalLlmConsole;

public static class TrayProfileMenuFactory
{
    private const double MenuRowWidth = 286;
    private const double InlineProfileIndent = 14;

    public static ContextMenu Create(
        TrayProfileMenuSnapshot snapshot,
        TrayProfileMenuActions actions)
        => CreateView(snapshot, actions).Menu;

    public static TrayProfileMenuView CreateView(
        TrayProfileMenuSnapshot snapshot,
        TrayProfileMenuActions actions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(actions);

        var menu = new ContextMenu
        {
            StaysOpen = false,
            FlowDirection = Loc.IsRightToLeft(Loc.CurrentLanguage)
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight,
            MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 48)
        };

        InlineModelExpansion? expansion = null;
        void Refresh(TrayProfileMenuSnapshot next)
        {
            var expandedModelId = expansion?.ExpandedModelId ?? "";
            menu.Items.Clear();
            menu.Items.Add(Section(Loc.T("Tray.Favorites")));
            if (next.Favorites.Count == 0)
            {
                menu.Items.Add(Disabled(Loc.T("Tray.NoFavorites")));
            }
            else
            {
                foreach (var favorite in next.Favorites)
                    menu.Items.Add(ProfileItem(favorite, $"{favorite.Model.Name} · {favorite.Profile.Name}", actions));
            }

            menu.Items.Add(new Separator());
            expansion = null;
            if (next.Models.Count == 0)
            {
                menu.Items.Add(Disabled(Loc.T("Tray.NoProfiles")));
            }
            else
            {
                expansion = new InlineModelExpansion(menu, actions);
                foreach (var model in next.Models)
                    menu.Items.Add(expansion.CreateModelItem(model));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(Command(Loc.T("Tray.ShowWindow"), actions.ShowWindow));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command(Loc.T("Tray.Exit"), actions.Exit));
            expansion?.RestoreExpandedModel(expandedModelId);
        }

        var view = new TrayProfileMenuView(menu, Refresh);
        view.Refresh(snapshot);
        return view;
    }

    private static MenuItem ProfileItem(
        TrayProfileMenuEntry entry,
        string label,
        TrayProfileMenuActions actions,
        bool showModelName = true,
        bool inline = false)
    {
        var actionLabel = ActionLabel(entry.Action);
        var item = Compact(new MenuItem
        {
            Header = ProfileRow(entry, label, actionLabel, actions, showModelName, inline),
            StaysOpenOnClick = true,
            ToolTip = $"{label} — {actionLabel}"
        });
        AutomationProperties.SetName(item, label);
        AutomationProperties.SetHelpText(item, actionLabel);
        return item;
    }

    private static FrameworkElement ModelHeader(TrayProfileMenuModel model, out TextBlock expansionGlyph)
    {
        var grid = new Grid
        {
            Width = MenuRowWidth,
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        expansionGlyph = new TextBlock
        {
            Text = "+",
            Width = 16,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        expansionGlyph.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft");
        Grid.SetColumn(expansionGlyph, 0);

        var name = new TextBlock
        {
            Text = model.Model.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(3, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 1);

        var count = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = model.Profiles.Count.ToString(System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            }
        };
        count.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        Grid.SetColumn(count, 2);
        grid.Children.Add(expansionGlyph);
        grid.Children.Add(name);
        grid.Children.Add(count);
        return grid;
    }

    private static FrameworkElement ProfileRow(
        TrayProfileMenuEntry entry,
        string label,
        string actionLabel,
        TrayProfileMenuActions actions,
        bool showModelName,
        bool inline)
    {
        var grid = RowGrid(inline);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock
        {
            Text = showModelName ? entry.Model.Name : entry.Profile.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 216
        };
        var description = new TextBlock
        {
            Text = showModelName ? $"{entry.Profile.Name} · {actionLabel}" : actionLabel,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 216
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft");
        copy.Children.Add(title);
        copy.Children.Add(description);
        Grid.SetColumn(copy, 0);

        var button = new WpfButton
        {
            Content = ActionIcon(entry.Action),
            ContentTemplate = null,
            Width = 28,
            Height = 26,
            MinHeight = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = entry.CanExecute,
            ToolTip = actionLabel,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (entry.Action == TrayProfileActionKind.Stop)
            VisualRole.SetButtonRole(button, VisualRole.Danger);
        else if (entry.CanExecute)
            VisualRole.SetButtonRole(button, VisualRole.Primary);
        else
            VisualRole.SetButtonRole(button, VisualRole.Quiet);
        AutomationProperties.SetName(button, $"{actionLabel}: {label}");
        button.Click += async (_, args) =>
        {
            args.Handled = true;
            await actions.ExecuteProfileAsync(entry);
        };
        Grid.SetColumn(button, 1);

        grid.Children.Add(copy);
        grid.Children.Add(button);
        return grid;
    }

    private static Grid RowGrid(bool inline = false)
    {
        var grid = new Grid
        {
            Width = inline ? MenuRowWidth - InlineProfileIndent : MenuRowWidth,
            MinHeight = 28,
            Margin = inline ? new Thickness(InlineProfileIndent, 0, 0, 0) : new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (inline)
        {
            var guide = new Border
            {
                Width = 2,
                Margin = new Thickness(-8, 2, 0, 2),
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                CornerRadius = new CornerRadius(1)
            };
            guide.SetResourceReference(Border.BackgroundProperty, "PanelBorderStrong");
            Grid.SetColumnSpan(guide, 2);
            grid.Children.Add(guide);
        }
        return grid;
    }

    private static MenuItem Section(string label)
    {
        var item = Disabled(label);
        item.FontWeight = FontWeights.SemiBold;
        return item;
    }

    private static MenuItem Disabled(string label)
        => Compact(new MenuItem { Header = label, IsEnabled = false });

    private static MenuItem Command(string label, Action action)
    {
        var item = Compact(new MenuItem { Header = label });
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Compact(MenuItem item)
    {
        item.Padding = new Thickness(5, 2, 5, 2);
        item.MinHeight = 24;
        return item;
    }

    private sealed class InlineModelExpansion
    {
        private readonly ContextMenu _menu;
        private readonly TrayProfileMenuActions _actions;
        private readonly List<MenuItem> _profileItems = [];
        private readonly Dictionary<string, (MenuItem Item, TextBlock Glyph, TrayProfileMenuModel Model)> _models
            = new(StringComparer.OrdinalIgnoreCase);
        private MenuItem? _expandedModel;
        private TextBlock? _expandedGlyph;

        public string ExpandedModelId { get; private set; } = "";

        public InlineModelExpansion(ContextMenu menu, TrayProfileMenuActions actions)
        {
            _menu = menu;
            _actions = actions;
        }

        public MenuItem CreateModelItem(TrayProfileMenuModel model)
        {
            var header = ModelHeader(model, out var glyph);
            var item = Compact(new MenuItem
            {
                Header = header,
                StaysOpenOnClick = true,
                ToolTip = model.Model.Name
            });
            AutomationProperties.SetName(item, model.Model.Name);
            item.Click += (_, args) =>
            {
                args.Handled = true;
                Toggle(item, glyph, model);
            };
            _models[model.Model.Id] = (item, glyph, model);
            return item;
        }

        public void RestoreExpandedModel(string modelId)
        {
            if (!string.IsNullOrWhiteSpace(modelId)
                && _models.TryGetValue(modelId, out var model))
                Toggle(model.Item, model.Glyph, model.Model);
        }

        private void Toggle(MenuItem item, TextBlock glyph, TrayProfileMenuModel model)
        {
            var collapseOnly = ReferenceEquals(_expandedModel, item);
            Collapse();
            if (collapseOnly) return;

            var insertionIndex = _menu.Items.IndexOf(item) + 1;
            foreach (var profile in model.Profiles)
            {
                var profileItem = ProfileItem(
                    profile,
                    profile.Profile.Name,
                    _actions,
                    showModelName: false,
                    inline: true);
                _menu.Items.Insert(insertionIndex++, profileItem);
                _profileItems.Add(profileItem);
            }

            glyph.Text = "−";
            _expandedModel = item;
            _expandedGlyph = glyph;
            ExpandedModelId = model.Model.Id;
        }

        private void Collapse()
        {
            foreach (var profileItem in _profileItems)
                _menu.Items.Remove(profileItem);
            _profileItems.Clear();
            if (_expandedGlyph is not null)
                _expandedGlyph.Text = "+";
            _expandedModel = null;
            _expandedGlyph = null;
            ExpandedModelId = "";
        }
    }

    private static FrameworkElement ActionIcon(TrayProfileActionKind action)
    {
        if (action == TrayProfileActionKind.Start)
        {
            var play = new WpfPath
            {
                Data = Geometry.Parse("M 1,0 L 9,5 L 1,10 Z"),
                Width = 9,
                Height = 10,
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };
            play.SetBinding(Shape.FillProperty, ForegroundBinding());
            return play;
        }

        if (action == TrayProfileActionKind.Stop)
        {
            var stop = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(1),
                SnapsToDevicePixels = true
            };
            stop.SetBinding(Border.BackgroundProperty, ForegroundBinding());
            return stop;
        }

        return new TextBlock
        {
            Text = action == TrayProfileActionKind.Switch ? "↻" : "…",
            FontFamily = new WpfFontFamily("Segoe UI Symbol"),
            FontSize = action == TrayProfileActionKind.Switch ? 14 : 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, -1, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static WpfBinding ForegroundBinding()
        => new(nameof(WpfControl.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WpfButton), 1)
        };

    private static string ActionLabel(TrayProfileActionKind action)
        => action switch
        {
            TrayProfileActionKind.Stop => Loc.T("Tray.StopProfile"),
            TrayProfileActionKind.Switch => Loc.T("Tray.SwitchProfile"),
            TrayProfileActionKind.Loading => Loc.T("Tray.ProfileLoading"),
            TrayProfileActionKind.Stopping => Loc.T("Tray.ProfileStopping"),
            _ => Loc.T("Tray.StartProfile")
        };
}
