using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed record UpdatesPageActions(
    Func<Task> PrimaryActionAsync,
    Action OpenRepository);

public sealed record UpdatesPageRequest(
    UpdatesPageViewModel ViewModel,
    UpdatesPageActions Actions);

public sealed record UpdatesPageBuildResult(
    StackPanel Content);

public static class UpdatesPageFactory
{
    public static UpdatesPageBuildResult Create(UpdatesPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ViewModel);
        ArgumentNullException.ThrowIfNull(request.Actions);
        ArgumentNullException.ThrowIfNull(request.Actions.PrimaryActionAsync);
        ArgumentNullException.ThrowIfNull(request.Actions.OpenRepository);

        var root = new StackPanel { Margin = new Thickness(16) };

        var actions = Bar();
        actions.Children.Add(Button(request.ViewModel.ActionText, request.Actions.PrimaryActionAsync, VisualRole.Primary));
        actions.Children.Add(Button(Loc.T("Updates.OpenGitHubButton"), () =>
        {
            request.Actions.OpenRepository();
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        root.Children.Add(PageSectionFactory.FramedSection(Loc.T("Updates.StatusSectionTitle"), SoftText(request.ViewModel.StatusDetails)));

        if (request.ViewModel.LatestUpdate is { IsAvailable: true })
            root.Children.Add(PageSectionFactory.FramedSection(Loc.T("Updates.LatestReleaseSectionTitle"), SoftText(request.ViewModel.LatestReleaseText)));

        return new UpdatesPageBuildResult(root);
    }

    private static WrapPanel Bar() => new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static TextBlock SoftText(string text)
        => new()
        {
            Text = text,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            TextWrapping = TextWrapping.Wrap
        };

    private static WpfButton Button(string text, Func<Task> click, string visualRole = "")
    {
        var button = new WpfButton { Content = text };
        VisualRole.SetButtonRole(button, visualRole);
        button.Click += async (_, _) => await click();
        return button;
    }
}
