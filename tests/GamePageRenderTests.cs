using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The game page drawn (spec "Setup UI > GamePage"): it builds from real services, carries the switches the smoke walks find
/// by AutomationId, keeps Start reading when Ur Score opens, holds Battle's clan search inside Battle's row, and dims that
/// search when Battle is turned off.
/// </summary>
[Collection(WpfCollection.Name)]
public class GamePageRenderTests
{
    [Fact]
    public void ThePageDrawsTheSwitchesTheStartBoxAndBattlesClanSearch() => UiThread.RunInApp(() =>
    {
        using var dir = TempDir.Create("urscore-game-page");
        using var services = Services(dir.Path);
        var page = new GamePage(services, "pet-sim-99", focusSlug: "pet-sim-99-clan-battle-points");
        // As SetupWindow does: a change in the services redraws the page.
        services.Changed += page.Refresh;
        var window = Show(page);
        try
        {
            Assert.True(page.ActualWidth > 0 && page.ActualHeight > 0, "the page never laid out");

            var game = Find<CheckBox>(page, "GameSwitch");
            Assert.Equal("Pet Sim 99", AutomationProperties.GetName(game));
            Assert.True(game.IsChecked);

            var battle = Find<CheckBox>(page, "ModeSwitch_battle");
            var profile = Find<CheckBox>(page, "ModeSwitch_profile");
            Assert.Equal(("Battle", "Profile"), (AutomationProperties.GetName(battle), AutomationProperties.GetName(profile)));
            Assert.True(battle.IsChecked == true && profile.IsChecked == true);

            var start = Find<CheckBox>(page, "StartOnOpenBox");
            Assert.Equal("Start reading when Ur Score opens", AutomationProperties.GetName(start));

            // The clan search is Battle's: inside its section, and Profile, which asks nothing, has none.
            var clans = page.ClansOf("battle");
            Assert.NotNull(clans);
            Assert.Null(page.ClansOf("profile"));

            // And inside Battle's card, the same border as its switch, not floating below it.
            Border? card = null;
            for (DependencyObject? at = clans; at is not null && card is null; at = VisualTreeHelper.GetParent(at)) card = at as Border;
            while (card is not null && !IsWithin(battle, card)) card = VisualTreeHelper.GetParent(card) is { } up ? FirstBorder(up) : null;
            Assert.NotNull(card);
            var search = Find<ClanSearchBox>(clans!, "MainClanSearch");
            Assert.True(clans!.IsEnabled);

            // First run's focus (A11): the main clan search holds the page's logical focus once it is drawn.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.True(search.IsKeyboardFocusWithin || IsWithin(FocusManager.GetFocusedElement(window) as DependencyObject, search),
                "the main clan search did not take focus");

            // Lines from the model, not typed: Battle's hosts line, Profile's note.
            var texts = Texts(page);
            Assert.Contains("Reads ps99.biggamesapi.io, thumbnails.roblox.com, tr.rbxcdn.com every 3 min", texts);
            Assert.Contains("Needs each account linked on db.biggames.io with its Profile view public.", texts);
            Assert.DoesNotContain("Turn Battle on to read these clans.", texts);
            Snap(page, "game-page.png");

            // Battle off: the switch saves through the services, and the clans stay in view, dimmed, with the line.
            battle.IsChecked = false;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.False(services.Switches.IsOn("pet-sim-99/battle"));
            Assert.False(clans.IsEnabled);
            Assert.Contains("Turn Battle on to read these clans.", Texts(page));
            Assert.Contains("Battle is off, so nothing is read.", Texts(page));
            Assert.DoesNotContain(Texts(page), t => t.Contains("times an hour", StringComparison.Ordinal));
            Snap(page, "game-page-battle-off.png");
        }
        finally
        {
            window.Close();
        }
    });

    private static AppServices Services(string folder) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(folder), new StubHost(reachable: false), new NullTransport(),
            new ManualTime(new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero)), Path.Combine(folder, "metric-rules.json"));

    private static Window Show(FrameworkElement page)
    {
        // Foreground as SetupWindow sets it, so the snapshot shows what Setup shows.
        var window = new Window
        {
            Content = new ScrollViewer { Content = page }, Width = 900, Height = 1400, Left = -4000, Top = -4000, ShowInTaskbar = false,
            Foreground = (Brush)Application.Current.Resources["WhiteBrush"],
        };
        window.Show();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// The page drawn on the theme's background into artifacts/smoke (ignored by git), for eyes: the walks that would show it
    /// move the real data folder and wait for the owner (testing.md rule 4).
    /// </summary>
    private static void Snap(FrameworkElement page, string name)
    {
        page.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        const int scale = 2;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle((Brush)Application.Current.Resources["BgBrush"], null, new Rect(0, 0, page.ActualWidth + 48, page.ActualHeight + 48));
            drawing.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.None }, null, new Rect(24, 24, page.ActualWidth, page.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap((int)((page.ActualWidth + 48) * scale), (int)((page.ActualHeight + 48) * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var folder = Path.Combine(dir.FullName, "artifacts", "smoke");
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }

    /// <summary>A control by the id UI Automation reads: its AutomationId, else its x:Name (WPF's default).</summary>
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id || e.Name == id)
        ?? throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} with the id {id}.");

    private static List<string> Texts(DependencyObject root) =>
        [.. Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text)];

    private static Border? FirstBorder(DependencyObject from)
    {
        for (var at = from; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is Border border) return border;
        }

        return null;
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, ancestor)) return true;
        }

        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private sealed class NullTransport : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken) =>
            Task.FromResult(new FetchResult(404, "{}", null));
    }
}
