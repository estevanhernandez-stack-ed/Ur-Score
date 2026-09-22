using System.IO;
using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// Every test class that touches WPF sits in <see cref="WpfCollection"/>, so no two start WPF at the same instant
/// (the deadlock is described there). A class that starts an STA thread, or goes through <see cref="UiThread"/>,
/// and is not in the collection is named here — the failure this prevents is a hang, which names nothing.
/// </summary>
public partial class WpfCollectionFenceTests
{
    [GeneratedRegex(@"ApartmentState\.STA|UiThread\.Run(InApp)?\(")]
    private static partial Regex TouchesWpf();

    [GeneratedRegex(@"^\[Collection\(WpfCollection\.Name\)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex InTheCollection();

    [Fact]
    public void EveryClassThatTouchesWpfIsInTheWpfCollection()
    {
        var tests = Path.Combine(RepoRoot(), "tests");
        var outside = Directory.EnumerateFiles(tests, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .Where(f => f.Name is not ("UiThread.cs" or "WpfCollectionFenceTests.cs"))
            .Where(f => TouchesWpf().IsMatch(f.Text) && !InTheCollection().IsMatch(f.Text))
            .Select(f => f.Name)
            .ToList();

        Assert.True(outside.Count == 0,
            "These test files start WPF work outside WpfCollection: " + string.Join(", ", outside)
            + ". Add [Collection(WpfCollection.Name)] to the class, or the next quiet-machine hang is yours to diagnose.");
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
