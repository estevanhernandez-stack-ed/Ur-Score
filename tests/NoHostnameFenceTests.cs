using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// Spec §2 and §10: hosts live in recipe files, not in Ur Score. One named exemption, the Roblox
/// username lookup, which serves every recipe's leaderboard and is switchable through resolveNames.
/// </summary>
public partial class NoHostnameFenceTests
{
    [GeneratedRegex(@"\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|io|net|org|gg|dev|app)\b")]
    private static partial Regex Hostname();

    private static readonly string NameClient = Path.Combine("Source", "NameClient.cs");

    [Fact]
    public void NoFileInSrcNamesAHostExceptTheUsernameLookup()
    {
        var src = Path.Combine(RepoRoot(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .Where(f => !string.Equals(f.Relative, NameClient, StringComparison.Ordinal))
            .SelectMany(f => Hostname().Matches(f.Text).Select(m => $"{f.Relative}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files name a host: {string.Join(", ", offenders)}. A source's address belongs in a recipe "
            + "file, so Ur Score itself knows no game and no vendor.");
    }

    [Fact]
    public void TheUsernameLookupNamesOnlyRoblox()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", NameClient));
        var hosts = Hostname().Matches(text).Select(m => m.Value).Distinct().ToList();

        Assert.Equal(new[] { "users.roblox.com" }, hosts);
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
