using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static class TextBoxTailPresenter
{
    public static bool SetText(
        WpfTextBox? textBox,
        string? text,
        bool followTail,
        bool forceFollowTail = false)
    {
        if (textBox is null) return false;

        text ??= "";
        if (string.Equals(textBox.Text, text, StringComparison.Ordinal)) return false;

        var scrollViewer = VisualDescendant<ScrollViewer>(textBox);
        var wasAtEnd = forceFollowTail
            || scrollViewer is null
            || scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset <= 1;
        var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
        var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;

        textBox.Text = text;
        if (followTail && wasAtEnd)
        {
            textBox.CaretIndex = textBox.Text.Length;
            textBox.ScrollToEnd();
            return true;
        }

        var clampedStart = Math.Min(selectionStart, textBox.Text.Length);
        textBox.Select(
            clampedStart,
            Math.Min(selectionLength, textBox.Text.Length - clampedStart));
        if (scrollViewer is null) return true;

        textBox.UpdateLayout();
        scrollViewer.ScrollToVerticalOffset(Math.Min(verticalOffset, scrollViewer.ScrollableHeight));
        scrollViewer.ScrollToHorizontalOffset(Math.Min(horizontalOffset, scrollViewer.ScrollableWidth));
        return true;
    }

    private static T? VisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var descendant = VisualDescendant<T>(child);
            if (descendant is not null) return descendant;
        }

        return null;
    }
}
