using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// A picture beside a row is only ever one of YOUR OWN accounts'. Other members' ids are read off a public leaderboard,
/// compared against your accounts and dropped (README, "What leaves your machine"); asking Roblox for their pictures
/// would send those ids back out and leave a file of each one on this PC. These stand where a refactor would cross that
/// line quietly.
/// </summary>
public class AvatarFenceTests
{
    [Fact]
    public void OnlyYourOwnAccountsEverHaveAPicture()
    {
        var live = Live([], [], new Dictionary<string, RecipeSnapshot>()) with
        {
            Avatars = new Dictionary<long, string> { [Main.RobloxUserId] = "mine.png", [999] = "someone-else.png" },
        };

        Assert.Equal("mine.png", live.AvatarFor(Main.RobloxUserId));
        Assert.Null(live.AvatarFor(999));
        Assert.Null(live.AvatarFor(0));
        Assert.Null(Live([], [], new Dictionary<string, RecipeSnapshot>()).AvatarFor(Main.RobloxUserId));
    }

    [Fact]
    public void TheLeaderboardAndTheTopHaveNowhereToPutAPicture()
    {
        // Structural, not a convention: these rows have no such member, so no template can bind one.
        Assert.Null(typeof(LeaderRow).GetProperty("Avatar"));
        Assert.Null(typeof(TopRow).GetProperty("Avatar"));

        foreach (var file in new[] { "LiveLeaderboardPanel.xaml", "TopPanel.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", file));
            Assert.DoesNotContain("Avatar", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThePictureLookupLivesInOneFileAndIsOnlyEverAskedWithYourOwnAccountIds()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .ToList();

        Assert.Equal(
            new[] { Path.Combine("Source", "AvatarBook.cs"), Path.Combine("Source", "IconClient.cs") },
            files.Where(f => f.Text.Contains("HeadshotsAsync", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            new[] { Path.Combine("Source", "IconClient.cs") },
            files.Where(f => f.Text.Contains("avatar-headshot", StringComparison.Ordinal)).Select(f => f.Relative).ToArray());

        Assert.Equal(
            new[] { Path.Combine("Composition", "AppServices.cs") },
            files.Where(f => f.Text.Contains(".AskAsync(", StringComparison.Ordinal)).Select(f => f.Relative).ToArray());

        // The argument, not just the expression somewhere in the file: AvatarFileFor names it too, so "contains" alone
        // would still pass if the ask were changed to send some other set.
        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.Contains("var yours = LiveBoard.UserIdsOf(KnownAccounts);", app, StringComparison.Ordinal);
        Assert.Contains("AskAsync(yours", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// Plan A26. The avatar slot is laid out whether or not a picture is there, so a picture that arrives late, fails or
    /// never comes doesn't move a single pixel of a row. <c>Collapsed</c> in that one style would reflow the name column
    /// on all five surfaces the moment a fetch landed, and no other test reads that value.
    /// </summary>
    [Fact]
    public void ThePictureSlotIsLaidOutWhetherOrNotAPictureIsThere()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "App.xaml"));
        var opens = app.IndexOf("<Style x:Key=\"AccountAvatar\"", StringComparison.Ordinal);
        Assert.True(opens >= 0, "App.xaml no longer holds the shared AccountAvatar style.");

        var closes = app.IndexOf("</Style>", opens, StringComparison.Ordinal);
        Assert.True(closes > opens, "The AccountAvatar style is not closed.");
        var style = app[opens..closes];

        Assert.Contains("Value=\"Hidden\"", style, StringComparison.Ordinal);
        Assert.DoesNotContain("Collapsed", style, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySurfaceThatDrawsAPictureDrawsTheSharedOne()
    {
        // One style, so a change to the size, the ring or the Hidden above cannot reach four surfaces and miss the fifth.
        string[] surfaces =
        [
            Path.Combine("Panels", "AccountsTablePanel.xaml"),
            Path.Combine("Panels", "MyAccountsPanel.xaml"),
            Path.Combine("Panels", "PromotionCheckPanel.xaml"),
            Path.Combine("Panels", "AccountCardPanel.xaml"),
            Path.Combine("Setup", "AccountsPage.xaml"),
        ];

        foreach (var surface in surfaces)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", surface));
            Assert.Contains("{StaticResource AccountAvatar}", text, StringComparison.Ordinal);
        }
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
