using System.IO;
using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// The fence around the trail: a line about an exception names its TYPE and nothing more.
/// <para>
/// The trail is shown on the Diagnostics page and copied from there to the clipboard, so it is a path out of the
/// process. An exception's message can carry a file path with the Windows user name in it, an address with a
/// key in it, or RoRoRo's own words; its <c>ToString()</c> carries all of that and a stack. Most trail lines
/// already said <c>ex.GetType().Name</c> and said why; four still wrote the message or the whole exception
/// (S1-14.14). This is the test that makes a fifth visible.
/// </para>
/// <para>
/// A source scan, so it catches an accident and not a determined author, like every fence here. The pattern is
/// deliberately narrow: an <c>AddTrail</c> call whose text interpolates <c>{ex}</c>, <c>{ex.Message}</c> or
/// <c>{ex.ToString()}</c>. Screen-facing text goes through <c>Redactor.Redact</c> and is a different rule.
/// </para>
/// </summary>
public class TrailFenceTests
{
    private static readonly Regex Offending = new(@"AddTrail\(.*\{\s*ex(\.Message|\.ToString\(\))?\s*\}", RegexOptions.Compiled);

    [Fact]
    public void ATrailLineAboutAnExceptionNamesOnlyItsType()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offending = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(src, f), Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && Offending.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(offending.Count == 0, "Trail lines carrying more than the exception's type:" + Environment.NewLine + string.Join(Environment.NewLine, offending));
    }

    /// <summary>The pattern itself, so the fence is known to see what it is for — a fence that matches nothing passes for free.</summary>
    [Theory]
    [InlineData("AddTrail($\"EXCEPTION: {ex}\");", true)]
    [InlineData("AddTrail($\"NOT SAVED: {ex.Message}\");", true)]
    [InlineData("_services.AddTrail($\"BOOK NOT LOADED: {ex.ToString()}\");", true)]
    [InlineData("AddTrail($\"NOT SAVED: {ex.GetType().Name}\");", false)]
    [InlineData("Show(line, Redactor.Redact($\"Could not add that: {ex.Message}\"));", false)]
    public void TheFenceSeesTheShapesItIsFor(string line, bool offends) => Assert.Equal(offends, Offending.IsMatch(line));

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
