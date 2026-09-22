using System.IO;

namespace UrScore.Tests;

/// <summary>
/// The fence around turning boards into JSON: two named writers, and each writes only what the privacy rule
/// has been through.
/// <para>
/// R17 says an account id is kept in written boards only when it is one of yours, and <c>BoardDefs.Sanitize</c>
/// is the whole of that rule. <c>BoardsFile</c> is the score book's one <c>boards.json</c> writer;
/// <c>BoardJson</c> is the same shape held on its own, so a setup folder's copy of your boards
/// (<c>SetupPack</c>) can be written without naming <c>BoardsFile</c> at all (setup-transfer design
/// 2026-09-22, §1). Two ways to name a writer means two places the rule could be skipped, and a third one
/// added later — naming either type from somewhere new — would not fail a test, would not look wrong in
/// review, and would put a stranger's id on disk quietly. This is the test that makes adding one visible
/// (S2-P.11, widened when <c>BoardJson</c> gave boards a second name to write under).
/// </para>
/// <para>
/// WHAT THIS BUYS, stated plainly for the same reason <c>ReportPolicy</c> states it: this catches an ACCIDENT,
/// a later change that saves boards from somewhere closer to the UI because a writer's name was not obvious.
/// It does not stop a determined author, since a name reached through reflection defeats a source scan. The
/// adversary here is a future refactor, not a hostile contributor.
/// </para>
/// </summary>
public class BoardsFileFenceTests
{
    [Fact]
    public void BoardsJsonIsWrittenFromTwoNamedPlacesAndBothSanitize()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: Code(File.ReadAllLines(f))))
            .ToList();

        // Either type's name, anywhere: its own file, and the one place each is used to turn boards into JSON.
        Assert.Equal(
            new[]
            {
                Path.Combine("Board", "BoardJson.cs"), Path.Combine("Board", "BoardsFile.cs"),
                Path.Combine("Composition", "AppServices.cs"), Path.Combine("Core", "SetupPack.cs"),
            },
            files.Where(f => f.Text.Contains("BoardsFile", StringComparison.Ordinal) || f.Text.Contains("BoardJson", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        // And the rule itself: where it is declared, and the two places it is applied — boards.json's one
        // writer, and SetupPack, which applies it again on the way into a setup folder rather than trust that
        // the boards it was handed are already clean.
        Assert.Equal(
            new[] { Path.Combine("Board", "BoardDefs.cs"), Path.Combine("Composition", "AppServices.cs"), Path.Combine("Core", "SetupPack.cs") },
            files.Where(f => f.Text.Contains("Sanitize", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        // The ARGUMENT, not merely both names somewhere in the same file. AppServices mentions Sanitize and calls
        // Save, and "contains both" would still pass if the save were changed to write the unsanitized boards —
        // which is exactly the regression this exists to catch (V3-S.37 rule 2: the decoy must be distinguishable).
        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.Contains("var clean = BoardDefs.Sanitize(", app, StringComparison.Ordinal);
        Assert.Contains("_boardsFile.Save(clean,", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file's lines with the comment-only ones dropped, so the scan below reads code rather than prose.
    /// <para>
    /// Learned the hard way, 2026-09-21: a comment in <c>PanelViews.cs</c> explaining that <c>BoardsFile</c>
    /// rejects an unknown panel type failed this test. Naming a type is not using it, and a fence that cannot
    /// tell the difference punishes exactly the thing this codebase wants more of — a comment saying why. Crude
    /// on purpose still: it drops whole-line comments, not trailing ones, because a line with code on it is a
    /// line worth reading whatever follows the slashes.
    /// </para>
    /// </summary>
    private static string Code(IEnumerable<string> lines) =>
        string.Join(
            Environment.NewLine,
            lines.Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("*", StringComparison.Ordinal);
            }));

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
