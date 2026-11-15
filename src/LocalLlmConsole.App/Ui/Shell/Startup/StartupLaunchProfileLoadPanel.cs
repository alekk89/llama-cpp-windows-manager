using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace LocalLlmConsole;

/// <summary>
/// Sidebar status panel that runs the configured startup profiles in the background and shows
/// per-profile progress. Cancel skips the profiles that have not started; a model that is
/// already loading continues. <see cref="Abort"/> also cancels the in-flight load (shutdown).
/// </summary>
public sealed class StartupLaunchProfileLoadPanel : Border
{
    private readonly TextBlock _title;
    private readonly WpfProgressBar _progress;
    private readonly StackPanel _rows;
    private readonly WpfButton _actionButton;
    private readonly List<Entry> _entries = [];
    private CancellationTokenSource? _skipRemaining;
    private CancellationTokenSource? _abort;

    public StartupLaunchProfileLoadPanel()
    {
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 8, 0, 0);
        Padding = new Thickness(0, 8, 0, 0);
        BorderThickness = new Thickness(0, 1, 0, 0);
        SetResourceReference(BorderBrushProperty, "PanelBorder");
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        AutomationProperties.SetLiveSetting(_title, AutomationLiveSetting.Polite);
        _progress = new WpfProgressBar { Height = 4, Margin = new Thickness(0, 6, 0, 6), Minimum = 0 };
        _rows = new StackPanel();
        _actionButton = new WpfButton { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        VisualRole.SetButtonRole(_actionButton, VisualRole.Quiet);
        ToolTipService.SetShowOnDisabled(_actionButton, true);
        _actionButton.Click += (_, _) => OnActionClick();
        var content = new StackPanel();
        content.Children.Add(_title);
        content.Children.Add(_progress);
        content.Children.Add(_rows);
        content.Children.Add(_actionButton);
        Child = content;
    }

    public bool IsLoading => _skipRemaining is not null;

    public bool IsCancelling => _skipRemaining?.IsCancellationRequested == true;

    public IReadOnlyList<StartupLaunchProfileLoadState> States => _entries.Select(entry => entry.State).ToArray();

    public async Task<StartupLaunchProfileLoadResult?> RunAsync(
        Func<StartupLaunchProfileLoadActions, CancellationToken, Task<StartupLaunchProfileLoadResult>> loadConfiguredAsync,
        StartupLaunchProfileLoadActions actions)
    {
        ArgumentNullException.ThrowIfNull(loadConfiguredAsync);
        ArgumentNullException.ThrowIfNull(actions);
        if (IsLoading) return null;

        using var skipRemaining = new CancellationTokenSource();
        using var abort = new CancellationTokenSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(skipRemaining.Token, abort.Token);
        _skipRemaining = skipRemaining;
        _abort = abort;
        _entries.Clear();
        _rows.Children.Clear();
        var monitored = actions with
        {
            // Cancel only skips queued profiles; the in-flight load observes shutdown aborts.
            LoadAsync = (model, profile, _) => actions.LoadAsync(model, profile, abort.Token),
            ReportProgress = progress =>
            {
                actions.ReportProgress?.Invoke(progress);
                OnUiThread(() => Apply(progress));
            }
        };
        try
        {
            var result = await loadConfiguredAsync(monitored, stop.Token);
            _skipRemaining = null;
            _abort = null;
            OnUiThread(() => Complete(result));
            return result;
        }
        catch
        {
            OnUiThread(() => Visibility = Visibility.Collapsed);
            throw;
        }
        finally
        {
            _skipRemaining = null;
            _abort = null;
        }
    }

    public void CancelRemaining()
    {
        if (_skipRemaining is not { IsCancellationRequested: false } skipRemaining) return;
        skipRemaining.Cancel();
        Render();
    }

    public void Abort()
    {
        _skipRemaining?.Cancel();
        _abort?.Cancel();
    }

    private void OnActionClick()
    {
        if (IsLoading)
            CancelRemaining();
        else
            Visibility = Visibility.Collapsed;
    }

    private void Apply(StartupLaunchProfileLoadProgress progress)
    {
        while (_entries.Count < progress.Total)
            _entries.Add(new Entry(null, StartupLaunchProfileLoadState.Pending, null));
        _entries[progress.Index] = new Entry(progress.Choice, progress.State, progress.Error);
        if (progress.State == StartupLaunchProfileLoadState.Loading)
            Visibility = Visibility.Visible;
        Render();
    }

    private void Complete(StartupLaunchProfileLoadResult result)
    {
        if (result.Failures.Count == 0 && !result.Cancelled)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        Render(result.StatusMessage);
    }

    private void Render(string? summary = null)
    {
        var finished = _entries.Count(entry => entry.State is not StartupLaunchProfileLoadState.Pending
            and not StartupLaunchProfileLoadState.Loading);
        _title.Text = summary ?? (IsCancelling
            ? Loc.T("StartupProfiles.Cancelling")
            : Loc.T("StartupProfiles.Title", finished, _entries.Count));
        _progress.Maximum = Math.Max(1, _entries.Count);
        _progress.Value = finished;
        _progress.Visibility = summary is null ? Visibility.Visible : Visibility.Collapsed;
        _rows.Children.Clear();
        foreach (var entry in _entries.Where(entry => entry.Choice is not null))
            _rows.Children.Add(Row(entry));

        var actionText = IsLoading ? Loc.T("Common.Cancel") : Loc.T("Common.Close");
        _actionButton.Content = actionText;
        _actionButton.IsEnabled = !IsCancelling;
        AutomationProperties.SetName(_actionButton, actionText);
        _actionButton.ToolTip = IsLoading ? Loc.T("StartupProfiles.CancelToolTip") : null;
    }

    private static TextBlock Row(Entry entry)
    {
        var row = new TextBlock
        {
            Text = Loc.T("StartupProfiles.Row", entry.Choice!.ModelName, entry.Choice.ProfileName, StateText(entry.State)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
            ToolTip = entry.Error?.Message
        };
        row.SetResourceReference(TextBlock.ForegroundProperty, entry.State switch
        {
            StartupLaunchProfileLoadState.Failed => "Danger",
            StartupLaunchProfileLoadState.Loading => "TextMain",
            _ => "TextMuted"
        });
        return row;
    }

    private static string StateText(StartupLaunchProfileLoadState state)
        => state switch
        {
            StartupLaunchProfileLoadState.Loading => Loc.T("StartupProfiles.State.Loading"),
            StartupLaunchProfileLoadState.Loaded => Loc.T("StartupProfiles.State.Loaded"),
            StartupLaunchProfileLoadState.AlreadyRunning => Loc.T("StartupProfiles.State.AlreadyRunning"),
            StartupLaunchProfileLoadState.Failed => Loc.T("StartupProfiles.State.Failed"),
            StartupLaunchProfileLoadState.Skipped => Loc.T("StartupProfiles.State.Skipped"),
            _ => Loc.T("StartupProfiles.State.Pending")
        };

    private void OnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private sealed record Entry(
        StartupLaunchProfileChoice? Choice,
        StartupLaunchProfileLoadState State,
        Exception? Error);
}
