using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// Mode-off is a gate above a source's own Enabled, applied by the composition (spec "Composition", decision 3, A3): an
/// off mode's sources have no watch, a read in flight when the switch flips records and reports nothing, and on brings
/// back the same reading. The whole app composed through its seams.
/// </summary>
public class ModeOffTests
{
    private const string Battle = "pet-sim-99/battle";
    private const string Game = "pet-sim-99";
    private const string ClanSlug = "pet-sim-99-clan-battle-points";
    private const string ProfileSlug = "pet-sim-99-profile";

    private static readonly DateTimeOffset Start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private const string BattleResponse = """{ "status": "ok", "data": { "configName": "B" } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": { "B": {
            "Place": 3, "Points": 999,
            "PointContributions": [ { "UserID": 111, "Points": 4200 } ]
        } } } }
        """;

    private static AppServices Compose(TempDir.Scope dir, IHostClient host, IRecipeTransport transport) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(dir.Path), host, transport, new ManualTime(Start), Path.Combine(dir.Path, "metric-rules.json"));

    [Fact]
    public async Task TurningBattleOffDropsTheWatchOfEveryBattleSourceAndOnBringsThemBack()
    {
        using var dir = OldInstallFixtureTests.Copy();
        using var services = Compose(dir, new StubHost(reachable: false), new Transport());
        await services.LoadBookAsync();
        var battleIds = services.Sources.Where(s => services.Catalog.ModeOf(s.Recipe)?.Key == Battle).Select(s => s.Id).ToList();
        var sourcesBefore = File.ReadAllText(new AppPaths(dir.Path).Sources);
        Assert.Equal(4, battleIds.Count);
        Assert.All(battleIds, id => Assert.NotNull(services.Runner.WatchFor(id)));

        services.SetSwitch(Battle, false);

        Assert.False(services.Switches.IsOn(Battle));
        Assert.All(battleIds, id => Assert.Null(services.Runner.WatchFor(id)));
        Assert.NotNull(services.Runner.WatchFor("s-a0000005"));
        Assert.DoesNotContain(services.ActiveSources, s => battleIds.Contains(s.Id));
        // A gate, not a flip: every source is still there and still enabled, and the file is as it was.
        Assert.All(services.Sources, s => Assert.True(s.Enabled));
        Assert.Equal(sourcesBefore, File.ReadAllText(new AppPaths(dir.Path).Sources));
        Assert.False(Settings.Load(new AppPaths(dir.Path).Settings).Modes![Battle]);
        Assert.Contains(services.Trail, line => line.EndsWith("MODES: Battle is off.", StringComparison.Ordinal));
        Assert.Equal("Battle", services.OffModeName("battle"));
        Assert.Equal(BoardEmpty.ModeOff, StarterBoards.Named(StarterBoards.All(services.Installed, services.Sources, services.OffModeName), "battle")!.Empty);

        services.SetSwitch(Battle, true);

        Assert.All(battleIds, id => Assert.NotNull(services.Runner.WatchFor(id)));
        Assert.Contains(services.Trail, line => line.EndsWith("MODES: Battle is on.", StringComparison.Ordinal));
    }

    /// <summary>The game switch masks every mode under it, and each mode's own switch is untouched by it.</summary>
    [Fact]
    public async Task TheGameSwitchMasksBothModesAndRemembersThem()
    {
        using var dir = OldInstallFixtureTests.Copy();
        using var services = Compose(dir, new StubHost(reachable: false), new Transport());
        await services.LoadBookAsync();

        services.SetSwitch(Game, false);

        Assert.Empty(services.ActiveSources);
        Assert.All(services.Sources, s => Assert.Null(services.Runner.WatchFor(s.Id)));
        Assert.True(services.Switches.IsModeSet(Battle));
        var starters = StarterBoards.All(services.Installed, services.Sources, services.OffModeName);
        Assert.All(starters, s => Assert.Equal(BoardEmpty.ModeOff, s.Empty));
        Assert.Equal(["Battle", "Profile"], starters.Select(s => s.ModeName));

        services.SetSwitch(Game, true);

        Assert.Equal(5, services.ActiveSources.Count);
    }

    /// <summary>
    /// The edge case the PRD names: a read in flight when its mode goes off is not recorded, sent or shown after the
    /// switch. The clan's fetch is held until the switch has flipped. The control is the same read with the mode on, which
    /// writes its line and sends its stat, so the empty book here is the switch's doing and not the fixture's.
    /// </summary>
    [Fact]
    public async Task AReadInFlightWhenItsModeGoesOffRecordsAndReportsNothing()
    {
        using var dir = TempDir.Create("urscore-mode-off");
        var paths = new AppPaths(dir.Path);
        var clan = BuiltInRecipes.BySlug[ClanSlug];
        new RecipeStore(paths.Recipes).SaveState(clan, new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points"),
        }));
        var source = new Source("s-00000001", ClanSlug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main);
        var host = new StubHost(reachable: true, Alt);
        var transport = new Transport();
        using var services = Compose(dir, host, transport);
        services.SaveSources([.. services.Sources, source]);
        await services.LoadBookAsync();
        Assert.NotNull(services.Runner.WatchFor(source.Id));

        var held = transport.Hold("https://ps99.biggamesapi.io/api/clan/K0i2", ClanResponse);
        var reading = services.TestNowAsync();
        await held.Asked.WaitAsync(TimeSpan.FromSeconds(10));

        services.SetSwitch(Battle, false);
        held.Answer();
        try
        {
            await reading.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            // A cancelled read may surface as cancellation; either way nothing may have been kept.
        }

        services.Book.Flush();
        Assert.Empty(BookFiles.ReadAll(paths.Book, ClanSlug));
        Assert.Empty(host.Reported);
        Assert.DoesNotContain(source.Id, services.Latest.Keys);

        // The control: the same read with Battle on again writes its line and sends its one stat.
        services.SetSwitch(Battle, true);
        await services.TestNowAsync();
        services.Book.Flush();
        Assert.Equal(source.Id, Assert.Single(BookFiles.ReadAll(paths.Book, ClanSlug)).Source);
        Assert.Equal("clan.battle.points", Assert.Single(host.Reported).MetricId);
    }

    /// <summary>
    /// A1 through the composition: a fresh install, then the one thing first run asks for (your clan, as Main), and the
    /// Battle starter is a board, not "No stats turned on yet". Before the clan it asks for one.
    /// </summary>
    [Fact]
    public void AFreshInstallWithOneMainClanHasABattleBoard()
    {
        using var dir = TempDir.Create("urscore-mode-fresh");
        using var services = Compose(dir, new StubHost(reachable: false), new Transport());
        StarterBoard BattleStarter() => StarterBoards.Named(StarterBoards.All(services.Installed, services.Sources, services.OffModeName), "battle")!;

        Assert.Equal(BoardEmpty.NoSources, BattleStarter().Empty);

        services.SaveSources(SourceRules.Add(services.Sources, ClanSlug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main));

        var battle = BattleStarter();
        Assert.Equal(BoardEmpty.None, battle.Empty);
        Assert.NotEmpty(battle.Panels);
        Assert.Contains(services.Boards, b => b.Follows == "battle" && b.Panels.Count > 0);
        Assert.Contains(services.Sources, s => s.Recipe == ProfileSlug);
    }

    /// <summary>Answers the battle step, 404s everything else, and holds one URL until told.</summary>
    private sealed class Transport : IRecipeTransport
    {
        private Held? _held;

        public Held Hold(string urlStart, string body) => _held = new Held(urlStart, body);

        public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            if (_held is { } held && url.AbsoluteUri.StartsWith(held.UrlStart, StringComparison.Ordinal))
            {
                _held = null;
                held.Signal();
                await held.Released.WaitAsync(TimeSpan.FromSeconds(10));
                return new FetchResult(200, held.Body, null);
            }

            if (url.AbsoluteUri.StartsWith("https://ps99.biggamesapi.io/api/activeClanBattle", StringComparison.Ordinal)) return new FetchResult(200, BattleResponse, null);
            if (url.AbsoluteUri.StartsWith("https://ps99.biggamesapi.io/api/clan/K0i2", StringComparison.Ordinal)) return new FetchResult(200, ClanResponse, null);
            return new FetchResult(404, "{}", null);
        }
    }

    private sealed class Held(string urlStart, string body)
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string UrlStart => urlStart;

        public string Body => body;

        public Task Asked => _asked.Task;

        public Task Released => _released.Task;

        public void Signal() => _asked.TrySetResult();

        public void Answer() => _released.TrySetResult();
    }
}
