using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// A 0.6.3 install, loaded by today's code (games-and-modes spec A6). The fixture under
/// <c>Fixtures/old-install-0.6.3</c> is synthesized (no real handle, clan or account id) in the shape the stores
/// wrote it: the old top-clans text with no <c>groupsAreClans</c>, a legacy <c>metricIdOverride</c> state, a state
/// with no stats, pinned <c>ps99.*</c> ids with Send on, <c>sentFieldMetrics</c> on the clans list, an orphan
/// roblox-followers recipe with a source and a small book, Main/Mine/Watch sources, a settings file at version 2 with
/// no <c>modes</c>, a following Battle tab beside one edited board, and a book written under the old recipe text's hash.
/// <para>
/// These pin what loads BEFORE the modes work changes it. A later item that moves one of these facts edits the
/// assertion in its own diff, so the change is visible where it happens. Facts only, no incidental ordering.
/// </para>
/// </summary>
public class OldInstallFixtureTests
{
    private const string BattleSlug = "pet-sim-99-clan-battle-points";
    private const string ProfileSlug = "pet-sim-99-profile";
    private const string TopSlug = "pet-sim-99-top-clans";
    private const string RobloxSlug = "roblox-followers";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A copy of the fixture in a folder of the test's own: loading writes (settings, sources migration), the fixture never changes.</summary>
    private static TempDir.Scope Copy()
    {
        var dir = TempDir.Create("urscore-old-install");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "old-install-0.6.3");
        Assert.True(Directory.Exists(source), "the old-install fixture was not copied to the test output");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir.Path, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return dir;
    }

    private static InstalledRecipe Installed(RecipeStoreLoad load, string slug) => Assert.Single(load.Recipes, r => r.Recipe.Slug == slug);

    [Fact]
    public void TheStoresReadTheFixtureWithoutProblems()
    {
        using var dir = Copy();
        var paths = new AppPaths(dir.Path);

        var recipes = new RecipeStore(paths.Recipes).LoadAll();
        Assert.Empty(recipes.Problems);
        Assert.Equal(new[] { BattleSlug, ProfileSlug, TopSlug, RobloxSlug }, recipes.Recipes.Select(r => r.Recipe.Slug).Order(StringComparer.Ordinal));

        var sources = new SourceStore(paths.Sources).LoadResult();
        Assert.True(sources.Exists);
        Assert.True(sources.Readable);
        Assert.Equal(6, sources.Sources.Count);

        var boards = new BoardsFile(paths.Boards, new ManualTime(Now)).Load();
        Assert.True(boards.Exists);
        Assert.True(boards.Readable);
        Assert.Equal(3, boards.Boards.Count);
    }

    [Fact]
    public void TheOldTopClansTextHasNoGroupsAreClansSoItIsAListThatKeepsNoNames()
    {
        using var dir = Copy();
        var top = Installed(new RecipeStore(new AppPaths(dir.Path).Recipes).LoadAll(), TopSlug);

        Assert.DoesNotContain("groupsAreClans", top.Text, StringComparison.Ordinal);
        Assert.True(top.Recipe.IsGroupList);
        Assert.False(top.Recipe.GroupsAreClans);
        Assert.False(top.Recipe.KeepsGroupNames);
    }

    [Fact]
    public void TheLegacyMetricIdOverrideSurfacesAsLegacyChoicesAndNoStats()
    {
        using var dir = Copy();
        var state = Installed(new RecipeStore(new AppPaths(dir.Path).Recipes).LoadAll(), BattleSlug).State;

        Assert.Null(state.Stats);
        Assert.Empty(state.StatChoices);
        var legacy = Assert.Single(state.LegacyStatChoices!);
        Assert.Equal("value", legacy.Key);
        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "clan.battle.points.mine"), legacy.Value);
        Assert.Equal(legacy.Value, Assert.Single(state.ChoicesForUpdate).Value);
        Assert.Equal("TestClan", state.InputValues["clan"]);
        Assert.Single(state.Excluded);
    }

    [Fact]
    public void AStateWithNoStatsHasNoLegacyChoicesEither()
    {
        using var dir = Copy();
        var state = Installed(new RecipeStore(new AppPaths(dir.Path).Recipes).LoadAll(), RobloxSlug).State;

        Assert.Null(state.Stats);
        Assert.Null(state.LegacyStatChoices);
        Assert.Empty(state.ChoicesForUpdate);
    }

    [Fact]
    public void ThePinnedPs99IdsKeepSendOnAndTheClansListKeepsItsFieldMetrics()
    {
        using var dir = Copy();
        var load = new RecipeStore(new AppPaths(dir.Path).Recipes).LoadAll();

        var profile = Installed(load, ProfileSlug);
        var sent = profile.State.SentStats(profile.Recipe);
        Assert.Equal(new[] { "ps99.diamonds", "ps99.eggs-hatched" }, sent.Select(s => s.MetricId).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "diamonds", "eggs", "rank" }, profile.State.StatChoices.Keys.Order(StringComparer.Ordinal));
        Assert.False(profile.State.StatChoices["rank"].Send);

        var top = Installed(load, TopSlug);
        Assert.Null(top.State.Stats);
        Assert.Equal(new[] { "gap-above", "place", "points" }, top.State.FieldMetricKeys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SettingsAreVersionTwoWithNoModesAndTheActiveRecipeStillThere()
    {
        using var dir = Copy();
        var path = new AppPaths(dir.Path).Settings;
        Assert.DoesNotContain("modes", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

        var settings = Settings.Load(path);

        Assert.Equal(2, settings.SettingsVersion);
        Assert.True(settings.StartOnOpen);
        Assert.True(settings.ResolveNames);
        Assert.Equal(BattleSlug, settings.ActiveRecipe);
    }

    [Fact]
    public void TheSourcesCarryMainMineAndWatchAndTheOrphanKeepsItsSource()
    {
        using var dir = Copy();
        var sources = new SourceStore(new AppPaths(dir.Path).Sources).Load();

        Assert.Single(sources, s => s.Recipe == BattleSlug && s.Role == SourceRole.Main);
        Assert.Contains(sources, s => s.Recipe == BattleSlug && s.Role == SourceRole.Mine && s.Inputs["clan"] == "TestClanAlts");
        Assert.Contains(sources, s => s.Recipe == BattleSlug && s.Role == SourceRole.Watch);
        Assert.Contains(sources, s => s.Recipe == TopSlug && s.Role == SourceRole.Watch);
        Assert.Contains(sources, s => s.Recipe == ProfileSlug && s.Role == SourceRole.Mine);
        Assert.Contains(sources, s => s.Recipe == RobloxSlug && s.Role == SourceRole.Mine);
        Assert.All(sources, s => Assert.True(s.Enabled));
    }

    [Fact]
    public void BoardsHoldFollowingBattleAndAltsTabsAndOneEditedBoard()
    {
        using var dir = Copy();
        var boards = new BoardsFile(new AppPaths(dir.Path).Boards, new ManualTime(Now)).Load().Boards;

        // A real 0.6.3 save writes one following entry per following starter.
        Assert.Equal(new[] { "battle", "alts" }, boards.Where(b => b.Follows is not null).Select(b => b.Follows));
        Assert.All(boards.Where(b => b.Follows is not null), b => Assert.Empty(b.Panels));

        var edited = Assert.Single(boards, b => b.Follows is null);
        Assert.Equal("My watch", edited.Name);
        Assert.Equal(new[] { PanelType.Standing, PanelType.PastPeriods }, edited.Panels.Select(p => p.Type));
    }

    [Fact]
    public void TheBookWasWrittenUnderTheOldTextsHashAndReadsBack()
    {
        using var dir = Copy();
        var paths = new AppPaths(dir.Path);
        var battleText = File.ReadAllText(Path.Combine(paths.Recipes, BattleSlug + ".recipe.json"));

        var lines = BookFiles.ReadAll(paths.Book, BattleSlug).ToList();

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(BookFiles.Hash(battleText), l.Recipe.Hash));
        Assert.All(lines, l => Assert.Equal("s-a0000001", l.Source));
        Assert.True(File.Exists(BookFiles.RecipeFile(paths.Book, BattleSlug, BookFiles.Hash(battleText))));
        Assert.Equal(2, BookFiles.ReadAll(paths.Book, RobloxSlug).Count());
        Assert.Equal(new[] { BattleSlug, RobloxSlug }, BookFiles.Slugs(paths.Book));
    }

    /// <summary>
    /// The whole app composed over the fixture, as the seams constructor allows (a stub host that is not there, a fake
    /// transport that answers nothing): what it installs, which sources it holds, the boards the window would show, and a
    /// book the reader has loaded. Nothing in the fixture is a problem under today's code.
    /// </summary>
    [Fact]
    public async Task ComposedOverTheFixtureTheAppHoldsEverythingAndTheBookLoads()
    {
        using var dir = Copy();
        using var services = new AppServices(Dispatcher.CurrentDispatcher, new AppPaths(dir.Path), new StubHost(reachable: false),
            new NothingTransport(), new ManualTime(Now), Path.Combine(dir.Path, "metric-rules.json"));

        Assert.Equal(new[] { BattleSlug, ProfileSlug, TopSlug, RobloxSlug }, services.Installed.Select(i => i.Recipe.Slug).Order(StringComparer.Ordinal));
        Assert.Empty(services.RecipeProblems);
        Assert.Equal(6, services.Sources.Count);
        Assert.Contains(services.Sources, s => s.Role == SourceRole.Main);
        Assert.Equal(3, services.SavedBoards.Count);

        // The saved following Battle tab is NOT shown today: its starter builds from ticked stats, and the legacy state
        // ticks none (Stats is null; the legacy choices are read only on an update), so Battle comes out empty and
        // hidden (spec A8 changes this). The following Alts tab shows: the profile recipe has ticked stats. The edited
        // board shows as it was saved.
        Assert.DoesNotContain(services.Boards, b => b.Follows == "battle");
        Assert.Contains(services.Boards, b => b.Follows == "alts");
        Assert.Contains(services.Boards, b => b.Name == "My watch");
        Assert.Equal(BattleSlug, services.Settings.ActiveRecipe);

        await services.LoadBookAsync();

        Assert.True(services.ReaderLoaded);
        Assert.Equal(3, services.Reader.Readings(BattleSlug));
        Assert.Equal(2, services.Reader.Readings(RobloxSlug));
        Assert.Equal("TestClan", services.Reader.LastReading("s-a0000001")!.Inputs["clan"]);
        Assert.All(Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories),
            file => Assert.StartsWith(dir.Path, file, StringComparison.Ordinal));
    }

    /// <summary>Answers every fetch with a 404: this test composes the app and never starts a read.</summary>
    private sealed class NothingTransport : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken) =>
            Task.FromResult(new FetchResult(404, "{}", null));
    }
}
