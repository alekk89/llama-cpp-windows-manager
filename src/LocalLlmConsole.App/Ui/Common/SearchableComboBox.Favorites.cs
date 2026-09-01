using System.Collections;
using System.Windows.Automation;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace LocalLlmConsole;

public sealed partial class SearchableComboBox
{
    private void FavoriteContainerLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is WpfComboBoxItem container)
            DecorateFavoriteContainer(container);
    }

    private void DecorateFavoriteContainer(WpfComboBoxItem container)
    {
        container.ApplyTemplate();
        if (container.Template.FindName("ItemChrome", container) is not System.Windows.Controls.Border chrome)
            return;

        WpfButton favoriteButton;
        if (chrome.Child is System.Windows.Controls.Grid { Tag: var tag } existingGrid
            && ReferenceEquals(tag, FavoriteDecoration))
        {
            favoriteButton = existingGrid.Children.OfType<WpfButton>().Single();
        }
        else if (chrome.Child is System.Windows.Controls.ContentPresenter presenter)
        {
            chrome.Child = null;
            var row = new System.Windows.Controls.Grid
            {
                Tag = FavoriteDecoration,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            favoriteButton = FavoriteButton();
            row.Children.Add(favoriteButton);
            presenter.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            System.Windows.Controls.Grid.SetColumn(presenter, 1);
            row.Children.Add(presenter);
            chrome.Child = row;
        }
        else
        {
            return;
        }

        var item = container.Content;
        var key = FavoriteKeySelector(item);
        favoriteButton.Tag = item;
        favoriteButton.Visibility = LoadFavoriteKeysAsync is not null
                                    && ToggleFavoriteAsync is not null
                                    && !string.IsNullOrWhiteSpace(key)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        UpdateFavoriteButton(favoriteButton, key);
        chrome.BorderThickness = new System.Windows.Thickness(0, 0, 0, EndsFavoriteGroup(item) ? 1 : 0);
        chrome.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "PanelBorder");
    }

    private WpfButton FavoriteButton()
    {
        var button = new WpfButton();
        InlineGlyphButtonVisual.Configure(button);
        button.Click += FavoriteButtonClick;
        return button;
    }

    private async void FavoriteButtonClick(object sender, System.Windows.RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not WpfButton { Tag: { } item } button || ToggleFavoriteAsync is null) return;
        var key = FavoriteKeySelector(item);
        if (string.IsNullOrWhiteSpace(key)) return;
        button.IsEnabled = false;
        try
        {
            var favorite = await ToggleFavoriteAsync(key);
            if (favorite)
                _favoriteKeys.Add(key);
            else
                _favoriteKeys.Remove(key);
            ApplyFilterAndSort();
            RefreshFavoriteButtons();
        }
        catch (Exception ex)
        {
            FavoriteOperationFailed?.Invoke(ex);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void RefreshFavoriteButtons()
    {
        foreach (var item in Items.Cast<object>())
            if (ItemContainerGenerator.ContainerFromItem(item) is WpfComboBoxItem container)
                DecorateFavoriteContainer(container);
    }

    private void UpdateFavoriteButton(WpfButton button, string key)
    {
        var favorite = !string.IsNullOrWhiteSpace(key) && _favoriteKeys.Contains(key);
        button.Content = favorite ? "★" : "☆";
        button.ToolTip = LocalLlmConsole.Localization.Loc.T(
            favorite ? "Selector.RemoveFavoriteTooltip" : "Selector.AddFavoriteTooltip");
        AutomationProperties.SetName(button, button.ToolTip.ToString() ?? "");
        button.SetResourceReference(ForegroundProperty, favorite ? "Accent" : "TextSoft");
    }

    private bool EndsFavoriteGroup(object item)
    {
        var key = FavoriteKeySelector(item);
        if (string.IsNullOrWhiteSpace(key) || !_favoriteKeys.Contains(key)) return false;
        var visible = Items.Cast<object>().ToArray();
        var index = Array.IndexOf(visible, item);
        if (index < 0 || index >= visible.Length - 1) return false;
        var nextKey = FavoriteKeySelector(visible[index + 1]);
        return string.IsNullOrWhiteSpace(nextKey) || !_favoriteKeys.Contains(nextKey);
    }

    private sealed class FavoriteFirstComparer(SearchableComboBox owner) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            var xFavorite = IsFavorite(x);
            var yFavorite = IsFavorite(y);
            if (xFavorite != yFavorite) return xFavorite ? -1 : 1;
            var xOrder = owner._sourceOrder.GetValueOrDefault(x!, int.MaxValue);
            var yOrder = owner._sourceOrder.GetValueOrDefault(y!, int.MaxValue);
            var order = xOrder.CompareTo(yOrder);
            return order != 0
                ? order
                : StringComparer.OrdinalIgnoreCase.Compare(owner.SearchTextSelector(x), owner.SearchTextSelector(y));
        }

        private bool IsFavorite(object? item)
        {
            var key = owner.FavoriteKeySelector(item);
            return !string.IsNullOrWhiteSpace(key) && owner._favoriteKeys.Contains(key);
        }
    }
}
