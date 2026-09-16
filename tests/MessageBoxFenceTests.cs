namespace UrScore.Tests;

/// <summary>
/// Every popup Ur Score raises is its own, in RoRoRo's theme: a stock Windows box is a grey dialog in the middle
/// of a dark app, and the owner has seen one (owner rule, backlog V3-S.10). A question goes through
/// <see cref="Labs626.UrScore.UI.ConfirmWindow"/>; something that just went wrong is said in place, where the eye
/// already is, the way Setup › Alerts says a failed write on the card.
///
/// Two calls are still stock, and both are held here by name rather than by weakening the fence. Each is driven by
/// a smoke script in tools/smoke that closes and reads it through Win32 (<c>Close-MessageBox</c> posts BM_CLICK to
/// a control's window handle, <c>Get-MessageBoxText</c> reads a Win32 <c>Static</c>). A WPF window has no handle
/// per control and no <c>Static</c>, so converting either one needs the matching smoke script changed in the same
/// commit, and those are not ours to edit here:
///   • <c>UI\BoardWindow.xaml.cs</c> — Delete board, driven by tools/smoke/walk-board-editing.ps1 step 9.
///   • <c>UI\Setup\ImportFlow.cs</c> — the refused-import warning, driven by tools/smoke/window-smoke.ps1 step 2.
/// When a script learns the themed window, delete that file's row here and the fence closes over all of src.
/// </summary>
public class MessageBoxFenceTests
{
    /// <summary>File (relative to src) to the number of stock message boxes it is still allowed to raise.</summary>
    private static readonly Dictionary<string, int> StillStock = new(StringComparer.Ordinal)
    {
        [Path.Combine("UI", "BoardWindow.xaml.cs")] = 1,
        [Path.Combine("UI", "Setup", "ImportFlow.cs")] = 1,
    };

    [Fact]
    public void NothingUnderSrcRaisesAStockMessageBoxExceptTheTwoTheSmokeScriptsDrive()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).ToList();

        // Vacuity floor: the sweep must actually be reading the app.
        Assert.True(files.Count > 50, $"Only {files.Count} source files were swept; the fence is looking in the wrong place.");

        var found = files
            .Select(f => (File: Path.GetRelativePath(src, f), Count: Count(File.ReadAllText(f), "MessageBox.Show")))
            .Where(x => x.Count > 0)
            .OrderBy(x => x.File, StringComparer.Ordinal)
            .ToDictionary(x => x.File, x => x.Count, StringComparer.Ordinal);

        Assert.Equal(StillStock.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(), found.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>The allow list names files that exist, so a rename can't quietly retire a row.</summary>
    [Fact]
    public void EveryFileHeldBackStillExists()
    {
        var src = Path.Combine(RepoRoot(), "src");
        Assert.All(StillStock.Keys, file => Assert.True(File.Exists(Path.Combine(src, file)), file));
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
