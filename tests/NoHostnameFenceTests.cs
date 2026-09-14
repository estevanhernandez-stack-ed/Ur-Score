using System.Text.RegularExpressions;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

/// <summary>
/// Spec §2 and §10, stats design §8: hosts live in recipe files, not in Ur Score. Two named
/// exemptions, both Roblox's own services behind Ur Score features rather than stat sources: the
/// username lookup, switchable through resolveNames, and the icon lookup.
/// </summary>
public partial class NoHostnameFenceTests
{
    [GeneratedRegex(@"\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|io|net|org|gg|dev|app)\b")]
    private static partial Regex Hostname();

    private static readonly string NameClientFile = Path.Combine("Source", "NameClient.cs");

    private static readonly string IconClientFile = Path.Combine("Source", "IconClient.cs");

    [Fact]
    public void NoFileInSrcNamesAHostExceptTheUsernameAndIconLookups()
    {
        var src = Path.Combine(RepoRoot(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .Where(f => !string.Equals(f.Relative, NameClientFile, StringComparison.Ordinal)
                        && !string.Equals(f.Relative, IconClientFile, StringComparison.Ordinal))
            .SelectMany(f => Hostname().Matches(f.Text).Select(m => $"{f.Relative}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files name a host: {string.Join(", ", offenders)}. A source's address belongs in a recipe "
            + "file, so Ur Score itself knows no game and no vendor.");
    }

    [Fact]
    public void TheUsernameLookupNamesOnlyRoblox()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", NameClientFile));
        var hosts = Hostname().Matches(text).Select(m => m.Value).Distinct().ToList();

        Assert.Equal(new[] { "users.roblox.com" }, hosts);
    }

    [Fact]
    public void TheIconLookupNamesOnlyRobloxsThumbnailsService()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", IconClientFile));
        var hosts = Hostname().Matches(text).Select(m => m.Value).Distinct().ToList();

        Assert.Equal(new[] { "thumbnails.roblox.com" }, hosts);
    }

    [Fact]
    public void ThePictureDomainIsRobloxsAndIsNeverWrittenAsAHost()
    {
        // Written in parts in IconClient.cs so the fence above cannot see it; pinned here instead.
        Assert.Equal("rbxcdn.com", IconClient.PictureDomain);
        Assert.Equal("tr.rbxcdn.com", IconClient.PictureHostShown);
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
