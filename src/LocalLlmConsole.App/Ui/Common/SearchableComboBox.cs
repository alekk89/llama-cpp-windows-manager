using System.Collections;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed partial class SearchableComboBox : WpfComboBox
{
    private static readonly object FavoriteDecoration = new();
    private readonly HashSet<string> _favoriteKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, int> _sourceOrder = new(ReferenceEqualityComparer.Instance);
    private readonly IComparer _favoriteComparer;
    private object? _selectionBeforeSearch;
    private System.Windows.Controls.ScrollViewer? _listScroller;
    private WpfTextBox? _searchBox;
    private string _query = "";
    private bool _searchActive;
    private bool _pinListToTop;
    private bool _syncingSearchBox;
    private bool _updatingFilter;

    public SearchableComboBox()
    {
        _favoriteComparer = new FavoriteFirstComparer(this);
        SetResourceReference(StyleProperty, typeof(WpfComboBox));
        IsEditable = false;
        IsTextSearchEnabled = false;
        StaysOpenOnEdit = true;
        Unloaded += (_, _) => ClearFilter();
    }

    public Func<object?, string> SearchTextSelector { get; set; }
        = item => item?.ToString() ?? "";

    public Func<object?, string> FavoriteKeySelector { get; set; }
        = _ => "";

    public Func<Task<IReadOnlySet<string>>>? LoadFavoriteKeysAsync { get; set; }

    public Func<string, Task<bool>>? ToggleFavoriteAsync { get; set; }

    public Action<Exception>? FavoriteOperationFailed { get; set; }

    public string SearchQuery => _query;

    public bool IsUpdatingSearchFilter => _updatingFilter;

    public override void OnApplyTemplate()
    {
        if (_searchBox is not null)
            _searchBox.TextChanged -= SearchBoxTextChanged;
        if (_listScroller is not null)
        {
            _listScroller.ScrollChanged -= ListScrollerScrollChanged;
            _listScroller.LayoutUpdated -= ListScrollerLayoutUpdated;
        }
        _listScroller = null;
        _searchBox = null;
        base.OnApplyTemplate();
        InstallPopupSearchBox();
    }

    protected override void PrepareContainerForItemOverride(
        System.Windows.DependencyObject element,
        object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not WpfComboBoxItem container) return;
        container.Loaded -= FavoriteContainerLoaded;
        container.Loaded += FavoriteContainerLoaded;
        _ = container.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => DecorateFavoriteContainer(container)));
    }

    protected override void ClearContainerForItemOverride(
        System.Windows.DependencyObject element,
        object item)
    {
        if (element is WpfComboBoxItem container)
            container.Loaded -= FavoriteContainerLoaded;
        base.ClearContainerForItemOverride(element, item);
    }

    protected override void OnSelectionChanged(System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingFilter) return;
        base.OnSelectionChanged(e);
    }

    protected override void OnPropertyChanged(System.Windows.DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != IsDropDownOpenProperty) return;
        if (e.NewValue is true)
            BeginSearch();
        else
            EndSearch();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (IsDropDownOpen && !string.IsNullOrEmpty(e.Text) && !ReferenceEquals(e.OriginalSource, _searchBox))
        {
            SetQuery(_query + e.Text);
            e.Handled = true;
            return;
        }
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            _pinListToTop = false;
        if (ReferenceEquals(e.OriginalSource, _searchBox))
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        if (IsDropDownOpen && e.Key == Key.Back && _query.Length > 0)
        {
            SetQuery(_query[..^1]);
            e.Handled = true;
            return;
        }
        if (IsDropDownOpen && e.Key == Key.Delete && _query.Length > 0)
        {
            SetQuery("");
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        _pinListToTop = false;
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        _pinListToTop = false;
        base.OnPreviewMouseWheel(e);
    }

    private void InstallPopupSearchBox()
    {
        if (GetTemplateChild("PART_Popup") is not Popup { Child: System.Windows.Controls.Border border }
            || border.Child is not System.Windows.Controls.ScrollViewer listScroller)
            return;

        var queryBox = new WpfTextBox
        {
            MinHeight = 28,
            Margin = new System.Windows.Thickness(6),
            Padding = new System.Windows.Thickness(8, 3, 8, 3),
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            ToolTip = LocalLlmConsole.Localization.Loc.T("Selector.SearchTooltip")
        };
        AutomationProperties.SetName(queryBox, LocalLlmConsole.Localization.Loc.T("Selector.SearchLabel"));
        queryBox.TextChanged += SearchBoxTextChanged;
        _searchBox = queryBox;
        _listScroller = listScroller;
        listScroller.ScrollChanged += ListScrollerScrollChanged;
        listScroller.LayoutUpdated += ListScrollerLayoutUpdated;

        var popupGrid = new System.Windows.Controls.Grid();
        popupGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        popupGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        popupGrid.Children.Add(queryBox);
        border.Child = null;
        System.Windows.Controls.Grid.SetRow(listScroller, 1);
        popupGrid.Children.Add(listScroller);
        border.Child = popupGrid;
    }

    private void BeginSearch()
    {
        if (_searchActive) return;
        _searchActive = true;
        _pinListToTop = true;
        _query = "";
        SyncSearchBox();
        ClearFilter();
        _selectionBeforeSearch = SelectedItem;
        CaptureSourceOrder();
        _ = RefreshFavoriteKeysAsync();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _searchBox?.Focus();
            _searchBox?.SelectAll();
        }));
        QueueScrollToTop();
    }

    private void EndSearch()
    {
        if (!_searchActive) return;
        _searchActive = false;
        _pinListToTop = false;
        _query = "";
        SyncSearchBox();
        ClearFilter();
        if (SelectedItem is null && _selectionBeforeSearch is not null)
        {
            _updatingFilter = true;
            try
            {
                SelectedItem = _selectionBeforeSearch;
            }
            finally
            {
                _updatingFilter = false;
            }
        }
        _selectionBeforeSearch = null;
    }

    private void SearchBoxTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingSearchBox || _searchBox is null) return;
        _query = _searchBox.Text;
        ApplyFilterAndSort();
        QueueScrollToTop();
    }

    private void SetQuery(string query)
    {
        _query = query;
        SyncSearchBox();
        ApplyFilterAndSort();
    }

    private void SyncSearchBox()
    {
        if (_searchBox is null || _searchBox.Text == _query) return;
        _syncingSearchBox = true;
        try
        {
            _searchBox.Text = _query;
            _searchBox.CaretIndex = _searchBox.Text.Length;
        }
        finally
        {
            _syncingSearchBox = false;
        }
    }

    private async Task RefreshFavoriteKeysAsync()
    {
        if (LoadFavoriteKeysAsync is null) return;
        try
        {
            var favoriteKeys = await LoadFavoriteKeysAsync();
            _favoriteKeys.Clear();
            _favoriteKeys.UnionWith(favoriteKeys);
            ApplyFilterAndSort();
            RefreshFavoriteButtons();
            QueueScrollToTop();
        }
        catch (Exception ex)
        {
            FavoriteOperationFailed?.Invoke(ex);
        }
    }

    private void QueueScrollToTop()
    {
        _pinListToTop = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            ScrollListToTop();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ScrollListToTop));
        }));
    }

    private void ListScrollerScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (!_pinListToTop || e.VerticalOffset == 0) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollListToTop));
    }

    private void ListScrollerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_pinListToTop && _listScroller is { VerticalOffset: not 0 })
            _listScroller.ScrollToTop();
    }

    private void ScrollListToTop()
    {
        _listScroller?.UpdateLayout();
        _listScroller?.ScrollToTop();
    }

    private void CaptureSourceOrder()
    {
        _sourceOrder.Clear();
        if (ItemsSource is not IEnumerable source) return;
        var index = 0;
        foreach (var item in source)
            if (item is not null && !_sourceOrder.ContainsKey(item))
                _sourceOrder[item] = index++;
    }

    private void ApplyFilterAndSort()
    {
        _updatingFilter = true;
        try
        {
            Items.Filter = string.IsNullOrEmpty(_query)
                ? null
                : item => SearchTextSelector(item).Contains(_query, StringComparison.OrdinalIgnoreCase);
            if (ItemsSource is not null
                && CollectionViewSource.GetDefaultView(ItemsSource) is ListCollectionView listView)
                listView.CustomSort = LoadFavoriteKeysAsync is null ? null : _favoriteComparer;
            Items.Refresh();
        }
        finally
        {
            _updatingFilter = false;
        }
    }

    private void ClearFilter()
    {
        _updatingFilter = true;
        try
        {
            Items.Filter = null;
            Items.Refresh();
        }
        finally
        {
            _updatingFilter = false;
        }
    }

}
