using System.IO;

namespace UrScore.Tests;

/// <summary>
/// The fence around <c>boards.json</c>: one writer, and it writes only what the privacy rule has been through.
/// <para>
/// R17 says an account id is kept in <c>boards.json</c> only when it is one of yours, and
/// <c>BoardDefs.Sanitize</c> is the whole of that rule. A rule enforced at one call site is only worth
/// something while there is one call site — a second writer added later would not fail a test, would not look
/// wrong in review, and would put a stranger's id on disk quietly. This is the test that makes adding one
/// visible (S2-P.11).
/// </para>
/// <para>
/// WHAT THIS BUYS, stated plainly for the same reason <c>ReportPolicy</c> states it: this catches an ACCIDENT,
/// a later change that saves boards from somewhere closer to the UI because the single writer was not obvious.
/// It does not stop a determined author, since a name reached through reflection defeats a source scan. The
/// adversary here is a future refactor, not a hostile contributor.
/// </para>
/// </summary>
public class BoardsFileFenceTests
{
    [Fact]
    public void OnlyAppServicesWritesTheBoardsFileAndOnlySanitizedBoards()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .ToList();

        // The type itself, anywhere: its own file, and the one service allowed to hold one.
        Assert.Equal(
            new[] { Path.Combine("Board", "BoardsFile.cs"), Path.Combine("Composition", "AppServices.cs") },
            files.Where(f => f.Text.Contains("BoardsFile", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        // And the rule itself: where it is declared, and the single place it is applied.
        Assert.Equal(
            new[] { Path.Combine("Board", "BoardDefs.cs"), Path.Combine("Composition", "AppServices.cs") },
            files.Where(f => f.Text.Contains("Sanitize", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        // The ARGUMENT, not merely both names somewhere in the same file. AppServices mentions Sanitize and calls
        // Save, and "contains both" would still pass if the save were changed to write the unsanitized boards —
        // which is exactly the regression this exists to catch (V3-S.37 rule 2: the decoy must be distinguishable).
        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.Contains("var clean = BoardDefs.Sanitize(", app, StringComparison.Ordinal);
        Assert.Contains("_boardsFile.Save(clean,", app, StringComparison.Ordinal);
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
