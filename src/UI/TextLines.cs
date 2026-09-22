using System.Windows;
using System.Windows.Controls;

namespace Labs626.UrScore.UI;

/// <summary>
/// The one way a page says a line, or says nothing: an empty line takes no space. Eight files each carried their
/// own copy of these three lines (S1-10.1); they now take this one with <c>using static</c>, so a call reads
/// <c>ShowLine(line, text)</c> and there is one place the rule lives. Not <c>Show</c>: a window's own
/// <c>Show()</c> hides a static import of that name, and <c>ImportWindow</c> is a window.
/// </summary>
internal static class TextLines
{
    /// <summary>An empty line takes no space.</summary>
    public static void ShowLine(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
