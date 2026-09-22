using System.Windows;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// A clan's icon: whose it is, where it may come from, and what its absence costs. No test process can build a panel or the
/// board (App.xaml's resources don't load here), so the markup and the wiring are read as text, the way the avatar fences do.
/// </summary>
public class SourceIconFenceTests
{
    /// <summary>
    /// Both places a clan's picture is drawn: the Standing panel (its own clan's) and a recipe's row in Setup (its main clan's).
    /// Each draws through the shared decoder, which never locks the cache file a later fetch replaces, and is named for whose
    /// picture it is.
    /// </summary>
    public static TheoryData<string, string, string> Slots => new()
    {
        { Path.Combine("Panels", "StandingPanel.xaml"), "<Image x:Name=\"StandingIcon\"", "Icon" },
        { Path.Combine("Setup", "RecipesPage.xaml"), "<Image x:Name=\"RecipeIcon\"", "IconFile" },
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public void AClansPictureIsDrawnThroughTheDecoderAndNamedForWhoseItIs(string file, string opens, string property)
    {
        var image = Picture(file, opens);

        Assert.Contains($"Source=\"{{Binding {property}, Converter={{StaticResource Picture}}}}\"", image, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding IconName}\"", image, StringComparison.Ordinal);

        // A player's picture has no business here: this slot is a clan's.
        Assert.DoesNotContain("Avatar", image, StringComparison.Ordinal);
    }

    /// <summary>
    /// A26 reaches the clan's picture. While it isn't there yet, or the file doesn't decode, the slot keeps its space, so the
    /// place (or the recipe's name) beside it stays put when the picture lands. Only a recipe that names no icon at all gives no
    /// slot, and that trigger comes last, because the later of two matching triggers wins.
    /// </summary>
    [Theory]
    [MemberData(nameof(Slots))]
    public void AClansPictureKeepsItsSpaceWhileItIsNotThere(string file, string opens, string property)
    {
        var image = Picture(file, opens);
        var missing = $"<DataTrigger Binding=\"{{Binding {property}, Converter={{StaticResource Picture}}}}\" Value=\"{{x:Null}}\">";
        const string noSlot = "<DataTrigger Binding=\"{Binding HasIconSlot}\" Value=\"False\">";

        var whileMissing = Between(image, missing, "</DataTrigger>", "the picture's missing trigger");
        Assert.Contains("Property=\"Visibility\" Value=\"Hidden\"", whileMissing, StringComparison.Ordinal);
        Assert.DoesNotContain("Collapsed", whileMissing, StringComparison.Ordinal);

        var withoutSlot = Between(image, noSlot, "</DataTrigger>", "the no-slot trigger");
        Assert.Contains("Property=\"Visibility\" Value=\"Collapsed\"", withoutSlot, StringComparison.Ordinal);

        Assert.True(image.IndexOf(missing, StringComparison.Ordinal) < image.IndexOf(noSlot, StringComparison.Ordinal),
            "The no-slot trigger must come after the missing-picture one, or a recipe with no icon keeps an empty slot.");
        Assert.Equal(1, Count(image, "Value=\"Collapsed\""));

        // Visibility is the triggers' alone: a value on the element would beat them both.
        Assert.DoesNotContain("Visibility=", image[..image.IndexOf('>')], StringComparison.Ordinal);
    }

    /// <summary>The window's own picture follows the same rule: a slot while the main clan's picture is on its way, none without a main.</summary>
    [Theory]
    [InlineData(true, true, Visibility.Visible)]
    [InlineData(true, false, Visibility.Hidden)]
    [InlineData(false, false, Visibility.Collapsed)]
    [InlineData(false, true, Visibility.Visible)]
    public void APictureSlotHidesWhileThePictureIsNotThereAndCollapsesOnlyWhenNoneBelongs(bool hasSlot, bool hasPicture, Visibility expected)
    {
        Assert.Equal(expected, PictureFile.SlotVisibility(hasSlot, hasPicture));
    }

    [Fact]
    public void TheTopBarPictureIsNamedFromWhoseItIsAndHasNoFixedName()
    {
        var board = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));
        var image = Between(board, "<Image x:Name=\"BoardIcon\"", "/>", "BoardIcon in BoardWindow.xaml");
        Assert.DoesNotContain("AutomationProperties.Name", image, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml.cs"));
        Assert.Contains("AutomationProperties.SetName(BoardIcon, icon.Name ?? \"\");", code, StringComparison.Ordinal);

        // The window takes the icon at open (V3-S.6), not only when it changes.
        Assert.Contains("ApplyIcon(_services.WindowIcon);", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Privacy. A clan's icon is a public picture, but exactly one thing may be asked about it: the icon text a read of that
    /// source brought back. The lookup is reached only through <c>SourceIcons</c>, and <c>AppServices</c> hands it only
    /// <c>snapshot.IconText</c>, for the source that was read. What the thumbnails request carries is pinned by
    /// <c>IconClientTests.AnAssetIdIsLookedUpWithRobloxThenFetchedAndCached</c>: the asset id, a size and a format.
    /// </summary>
    [Fact]
    public void TheIconLookupIsOnlyEverAskedWithTheIconTextAReadOfThatSourceBroughtBack()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .ToList();

        Assert.Equal(
            new[] { Path.Combine("Fetch", "IconClient.cs"), Path.Combine("Fetch", "SourceIcons.cs") },
            files.Where(f => f.Text.Contains("CachedFile(", StringComparison.Ordinal) || f.Text.Contains("IIconSource", StringComparison.Ordinal))
                .Select(f => f.Relative).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(
            new[] { Path.Combine("Composition", "AppServices.cs") },
            files.Where(f => f.Text.Contains(".ApplyAsync(", StringComparison.Ordinal)).Select(f => f.Relative).ToArray());

        var app = files.Single(f => string.Equals(f.Relative, Path.Combine("Composition", "AppServices.cs"), StringComparison.Ordinal)).Text;
        Assert.DoesNotContain("_icons.ResolveAsync", app, StringComparison.Ordinal);
        Assert.Contains("if (snapshot.IconText is not { } iconText) return;", app, StringComparison.Ordinal);
        Assert.Contains("_sourceIcons.ApplyAsync(sourceId, iconText, hosts,", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// V3-S.6 and S1-14.1 are decided in <c>SourceIcons</c> and <c>IconChoice</c> and tested there. These pin that
    /// <c>AppServices</c> actually asks: each source's picture restored at start, before the window exists; what no longer has
    /// an icon forgotten whenever sources or recipes change; and each panel handed the pictures by source.
    /// </summary>
    [Fact]
    public void TheAppRestoresPicturesAtStartForgetsTheOnesThatNoLongerBelongAndHandsPanelsEachSourcesOwn()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Composition", "AppServices.cs"));

        Assert.Contains("_sourceIcons.Restore(IconHostsFor);", app, StringComparison.Ordinal);
        Assert.Contains("_sourceIcons.Keep(IconChoice.SourcesWithIcons(Sources, Installed));", app, StringComparison.Ordinal);
        Assert.Contains("_avatars.Files, _remembered, _sourceIcons.Files);", app, StringComparison.Ordinal);

        // Restored in the constructor, which runs before BoardWindow reads WindowIcon at open.
        var constructor = Between(app, "public AppServices(Dispatcher ui)", "// ---- ISetupServices ----", "the AppServices constructor");
        Assert.Contains("_sourceIcons.Restore(IconHostsFor);", constructor, StringComparison.Ordinal);
    }

    private static string Picture(string file, string opens)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", file));
        return Between(text, opens, "</Image>", $"the clan's picture in {file}");
    }

    private static int Count(string text, string what)
    {
        var count = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>The markup from <paramref name="opens"/> up to the next <paramref name="closes"/> after it.</summary>
    private static string Between(string text, string opens, string closes, string what)
    {
        var start = text.IndexOf(opens, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {what}, so this fence is looking in the wrong place.");

        var end = text.IndexOf(closes, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{what} is never closed.");
        return text[start..end];
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
