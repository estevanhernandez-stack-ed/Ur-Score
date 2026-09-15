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

        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.Contains("LiveBoard.UserIdsOf(KnownAccounts)", app, StringComparison.Ordinal);
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
