using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

/// <summary>
/// Non-blocking notice shown over the page area, for example when an action is refused because
/// another task is running. It hides itself after <see cref="Duration"/> or when closed.
/// </summary>
public sealed class ShellNoticeBar : Border
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(6);

    private readonly TextBlock _message;
    private readonly WpfButton _closeButton;
    private readonly DispatcherTimer _timer;

    public ShellNoticeBar()
        : this(DefaultDuration)
    {
    }

    public ShellNoticeBar(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Visibility = Visibility.Collapsed;
        MaxWidth = 560;
        Padding = new Thickness(12, 8, 6, 8);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "WarningSoft");
        SetResourceReference(BorderBrushProperty, "Warning");
        _message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Assertive);
        _closeButton = new WpfButton
        {
            Content = "",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        VisualRole.SetButtonRole(_closeButton, VisualRole.Quiet);
        _closeButton.Click += (_, _) => Hide();
        var layout = new DockPanel();
        DockPanel.SetDock(_closeButton, Dock.Right);
        layout.Children.Add(_closeButton);
        layout.Children.Add(_message);
        Child = layout;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = duration };
        _timer.Tick += (_, _) => Hide();
    }

    public TimeSpan Duration => _timer.Interval;

    public string Message => _message.Text;

    public bool IsShowing => Visibility == Visibility.Visible;

    public void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _message.Text = message;
        var closeText = Loc.T("Common.Close");
        AutomationProperties.SetName(_closeButton, closeText);
        _closeButton.ToolTip = closeText;
        Visibility = Visibility.Visible;
        _timer.Stop();
        _timer.Start();
        UIElementAutomationPeer.FromElement(_message)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public void Hide()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }
}
