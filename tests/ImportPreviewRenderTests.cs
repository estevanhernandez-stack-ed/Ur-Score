using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// <see cref="ImportPreviewWindow"/> drawn for eyes (design 2026-09-22, §3): the plan's groups with headings, a
/// tick on every change, a row greyed for a clan whose recipe isn't coming, both buttons, and the two lines that
/// say sends arrive off and the old setup is kept aside. Ur Score's own window, never a stock dialog.
/// </summary>
[Collection(WpfCollection.Name)]
public class ImportPreviewRenderTests
{
    [Fact]
    public void TheWindowDrawsEveryGroupATickAGreyedRowAndBothButtons() => UiThread.RunInApp(() =>
    {
        var plan = ThePlan();
        var manifest = new BookPackManifest(BookPack.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "0.5.6", 40, 2, true);
        var window = new ImportPreviewWindow(plan, manifest, "ur-score-stats-2026-09-22.zip") { Left = -4000, Top = -4000, ShowInTaskbar = false };
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            var content = (FrameworkElement)window.Content;
            Assert.True(content.ActualWidth > 0 && content.ActualHeight > 0, "the window never laid out");

            // The DockPanel itself has no automation peer (a plain layout panel, like Grid or StackPanel); the
            // window does, and walking from it reaches every descendant that does have one, flattening through
            // the panels that don't — the same as a real UIA client sees.
            var all = Flatten(UIElementAutomationPeer.CreatePeerForElement(window)).ToList();
            var names = all.Select(p => p.GetName()).ToList();
            Assert.Contains("Import CCGP", names);
            Assert.Contains("Import K0i2", names);
            Assert.Contains("RECIPES", names);
            Assert.Contains("CLANS", names);
            Assert.Contains("KEYS TO ENTER AGAIN", names);
            Assert.Contains("STATS", names);
            Assert.Contains("Import ticked", names);
            Assert.Contains("Cancel", names);
            Assert.Contains(ImportPreviewModel.NothingSent, names);
            // The two settings come with the file whatever is ticked, and the aside copy takes settings.json with
            // the rest: both are said here, or the preview under-promises what importing does (final review).
            Assert.Contains(ImportPreviewModel.SettingsNote, names);
            Assert.Contains(ImportPreviewModel.AsideNote, names);

            // The greyed row's own tick, not just its text: found in the VISUAL tree (not the automation peer tree)
            // so this checks the real CheckBox control's real IsEnabled, the thing App.xaml's disabled trigger and
            // the row's dimming both key off — not a proxy for it.
            var ghostRunTick = FindCheckBoxNamed(window, "Import GhostRun");
            Assert.NotNull(ghostRunTick);
            Assert.False(ghostRunTick!.IsEnabled, "the greyed row's tick must be disabled, not just described as such");

            // The DockPanel paints only its children, not a background of its own — that's the Window's job, and
            // RenderTargetBitmap never sees the window's own fill, only what the content visual draws. Painted here
            // instead, so the picture shows the theme it is actually read against, not white text on nothing.
            // The DockPanel's own Margin sits OUTSIDE its render bounds (ActualWidth/Height exclude it), so the
            // canvas is padded by that margin on every side too, or the picture would crop the real window's edges.
            const int scale = 2;
            var margin = content.Margin;
            var totalWidth = content.ActualWidth + margin.Left + margin.Right;
            var totalHeight = content.ActualHeight + margin.Top + margin.Bottom;
            var width = (int)(totalWidth * scale);
            var height = (int)(totalHeight * scale);
            var page = new DrawingVisual();
            using (var drawing = page.RenderOpen())
            {
                drawing.DrawRectangle(Brush("BgBrush"), null, new Rect(0, 0, totalWidth, totalHeight));
                drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None }, null, new Rect(margin.Left, margin.Top, content.ActualWidth, content.ActualHeight));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(page);

            var folder = Path.Combine(RepoRoot(), "artifacts", "smoke");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "import-preview.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
            Assert.True(new FileInfo(file).Length > 0);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// <see cref="PlanWithARecipeAndItsClan"/>-shaped (one Add recipe, one Add clan under it) plus a Replace clan
    /// (a role difference on K0i2), a Kept clan (Z9, only here), a clan greyed because its recipe (a profile,
    /// GhostRun) is in neither the file's recipes nor installed here, a key to enter again, and the stats row.
    /// </summary>
    private static SetupMergePlan ThePlan()
    {
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var file = new SetupPack(
            [new SetupRecipe(Clan.Slug, Clan.Name, text, new RecipeState(), [])],
            [
                new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main),
                new Source("s-file0002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine),
                new Source("s-file0003", Profile.Slug, new Dictionary<string, string> { ["clan"] = "GhostRun" }, SourceRole.Mine),
            ],
            [], Settings.Defaults, [new SetupKey("ps99", "PS99 key", Profile.Slug, Profile.Name)]);

        var here = new SetupHere(
            [],
            [
                new Source("s-local0002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Watch),
                new Source("s-local0003", Clan.Slug, new Dictionary<string, string> { ["clan"] = "Z9" }, SourceRole.Mine),
            ],
            [], [], Settings.Defaults);

        return SetupMerge.Plan(file, here, 40, 2);
    }

    /// <summary>
    /// Every peer under <paramref name="peer"/>, depth first. <see cref="AutomationPeer.GetChildren"/> can hand back
    /// a null entry for a visual child with no automation involvement of its own (a plain <c>Border</c>, a
    /// collapsed element) — skipped here rather than followed, which is what a real UIA client does too.
    /// </summary>
    private static IEnumerable<AutomationPeer> Flatten(AutomationPeer peer)
    {
        yield return peer;
        foreach (var child in peer.GetChildren() ?? [])
        {
            if (child is null) continue;
            foreach (var descendant in Flatten(child)) yield return descendant;
        }
    }

    /// <summary>The real <see cref="CheckBox"/> named <paramref name="name"/> by <c>AutomationProperties.Name</c>, found by
    /// walking the visual tree directly — not the automation peer tree — so <c>IsEnabled</c> is read off the control
    /// itself, not a peer's own idea of it.</summary>
    private static CheckBox? FindCheckBoxNamed(DependencyObject root, string name)
    {
        if (root is CheckBox box && AutomationProperties.GetName(box) == name) return box;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindCheckBoxNamed(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        }

        return null;
    }

    private static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];

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
