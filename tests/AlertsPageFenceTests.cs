namespace UrScore.Tests;

/// <summary>
/// Setup › Alerts says everything in the theme, on the card: no stock message box, no default tooltip (owner rule, backlog
/// V3-S.10), and no rule serialised to JSON for the screen (the alerts card design: "No JSON on screen").
/// </summary>
public class AlertsPageFenceTests
{
    [Fact]
    public void TheAlertsPageRaisesNoStockMessageBoxOrToolTipAndShowsNoJson()
    {
        var setup = Path.Combine(RepoRoot(), "src", "UI", "Setup");
        var page = File.ReadAllText(Path.Combine(setup, "AlertsPage.xaml")) + File.ReadAllText(Path.Combine(setup, "AlertsPage.xaml.cs"));

        Assert.DoesNotContain("MessageBox", page);
        Assert.DoesNotContain("ToolTip", page);
        Assert.DoesNotContain("ToJsonString", page);
        Assert.DoesNotContain("JsonSerializer", page);
        Assert.DoesNotContain("JsonNode", page);
        Assert.DoesNotContain("Preview", page);
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
