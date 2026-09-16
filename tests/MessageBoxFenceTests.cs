namespace UrScore.Tests;

/// <summary>
/// Every popup Ur Score raises is its own, in RoRoRo's theme: a stock Windows box is a grey dialog in the middle
/// of a dark app, and the owner has seen one (owner rule, backlog V3-S.10). A question goes through
/// <c>ConfirmWindow</c>; something that just went wrong is said in place, where the eye already is, the way
/// Setup › Alerts says a failed write on the card. There is no allow list: the last two came out when the smoke
/// helpers learned the themed window, and a new one anywhere under src fails here.
/// </summary>
public class MessageBoxFenceTests
{
    [Fact]
    public void NothingUnderSrcRaisesAStockMessageBox()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).ToList();

        // Vacuity floor: the sweep must actually be reading the app.
        Assert.True(files.Count > 50, $"Only {files.Count} source files were swept; the fence is looking in the wrong place.");

        var found = files
            .Select(f => (File: Path.GetRelativePath(src, f), Count: Count(File.ReadAllText(f), "MessageBox.Show")))
            .Where(x => x.Count > 0)
            .OrderBy(x => x.File, StringComparer.Ordinal)
            .Select(x => $"{x.File}: {x.Count}")
            .ToArray();

        // Equality against nothing, so this stays a ratchet: a stock box added anywhere shows up here by name.
        // If one ever has to stay, say why in this file rather than loosening the sweep.
        Assert.Equal(Array.Empty<string>(), found);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
