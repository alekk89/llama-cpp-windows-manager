using System.Windows;
using System.Windows.Media;

namespace LocalLlmConsole;

public static class VisualTreeTraversal
{
    public static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = Parent(current))
            if (current is T match)
                return match;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject current)
        => current is FrameworkContentElement content
            ? content.Parent
            : VisualTreeHelper.GetParent(current);
}
