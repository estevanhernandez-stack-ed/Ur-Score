namespace UrScore.Tests;

/// <summary>
/// Backlog S1-13.14. A colour on a number is a claim about it: cyan is this app's good news, magenta its warning. Standing's
/// change was cyan whichever way the clan went, so a fall during a battle read as a rise to anyone glancing at it. The model
/// knows which way it went (<c>StandingModel.ChangeDirection</c>); these pin that the panel paints from that and from nothing
/// fixed. No test process can build the panel (App.xaml's resources don't load here), so this reads the markup.
/// </summary>
public class ChangeColourFenceTests
{
    [Fact]
    public void StandingsChangeIsCyanOnlyWhenItRoseAndMagentaWhenItFell()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "StandingPanel.xaml"));
        var change = Between(text, "<TextBlock DockPanel.Dock=\"Right\" Text=\"{Binding Change}\"", "</TextBlock>", "the Change value in StandingPanel.xaml");
        var opening = change[..change.IndexOf('>')];

        // A colour written on the element itself would win over every trigger: that was the defect.
        Assert.DoesNotContain("Foreground=", opening, StringComparison.Ordinal);

        var rose = Between(change, "<DataTrigger Binding=\"{Binding ChangeDirection}\" Value=\"{x:Static book:ChangeDirection.Up}\">", "</DataTrigger>",
            "the change's Up trigger");
        var fell = Between(change, "<DataTrigger Binding=\"{Binding ChangeDirection}\" Value=\"{x:Static book:ChangeDirection.Down}\">", "</DataTrigger>",
            "the change's Down trigger");
        Assert.Contains("Value=\"{DynamicResource CyanBrush}\"", rose, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource MagentaBrush}\"", fell, StringComparison.Ordinal);

        // No change, or none known, is neither good nor bad news: it keeps the text colour, so nothing else sets one.
        Assert.Equal(2, Count(change, "Property=\"Foreground\""));
    }

    private static int Count(string text, string what)
    {
        var count = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>The markup from <paramref name="opens"/> up to the next <paramref name="closes"/> after it.</summary>
    private static string Between(string text, string opens, string closes, string what)
    {
        var start = text.IndexOf(opens, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {what}, so this fence is looking in the wrong place.");

        var end = text.IndexOf(closes, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{what} is never closed.");
        return text[start..end];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
