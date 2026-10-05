using System.Text.Json.Nodes;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// The first 0.7.0 start over a 0.6.3 install (prd "Upgrading from 0.6.3", spec A5, A8): the whole app composed over the
/// synthesized fixture, through the seams (a host that isn't there, a transport that answers nothing). The upgrade keeps
/// every source, tick and metric id, stops reading only the orphan, writes nothing but the modes map, and the score book
/// runs on as one series across the reader text changing under it.
/// </summary>
public class UpgradeFrom063Tests
{
    private const string BattleSlug = "pet-sim-99-clan-battle-points";
    private const string ProfileSlug = "pet-sim-99-profile";
    private const string TopSlug = "pet-sim-99-top-clans";
    private const string RobloxSlug = "roblox-followers";
    private const string Main = "s-a0000001";

    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static AppServices Compose(TempDir.Scope dir) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(dir.Path), new StubHost(reachable: false), new NothingTransport(),
            new ManualTime(Now), Path.Combine(dir.Path, "metric-rules.json"));

    /// <summary>Every file under a folder with its bytes.</summary>
    private static Dictionary<string, string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToBase64String(File.ReadAllBytes(f)), StringComparer.OrdinalIgnoreCase);

    private static (string, string, string, SourceRole, bool)[] Shape(IEnumerable<Source> sources) =>
        [.. sources.Select(s => (s.Id, s.Recipe, s.InputsKey, s.Role, s.Enabled))];

    [Fact]
    public async Task EverySourceKeepsItsClanItsRoleAndItsEnabledState()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var before = new SourceStore(new AppPaths(dir.Path).Sources).Load();

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.Equal(6, before.Count);
        Assert.Equal(Shape(before), Shape(services.Sources));
    }

    /// <summary>Alert rules key on metric ids: every one is what 0.6.3 sent under, the legacy override's included.</summary>
    [Fact]
    public void EveryTickAndEveryMetricIdIsUnchanged()
    {
        using var dir = OldInstallFixtureTests.Copy();

        using var services = Compose(dir);

        InstalledRecipe Reader(string slug) => Assert.Single(services.Installed, i => i.Recipe.Slug == slug);

        var battle = Reader(BattleSlug);
        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "clan.battle.points.mine"), Assert.Single(battle.State.StatChoices).Value);
        Assert.Equal(["clan.battle.points.mine"], battle.State.SentStats(battle.Recipe).Select(s => s.MetricId));

        var profile = Reader(ProfileSlug);
        Assert.Equal(
            [("diamonds", true, true, "ps99.diamonds"), ("eggs", true, true, "ps99.eggs-hatched"), ("rank", true, false, "ps99.rank")],
            profile.State.StatChoices.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value.Show, kv.Value.Send, kv.Value.MetricId)));

        Assert.Equal(["gap-above", "place", "points"], Reader(TopSlug).State.FieldMetricKeys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The hand-imported recipe no mode names stops reading, and everything of it stays: its recipe and state files, its
    /// source, and its book, byte for byte. The other five sources each have their watch.
    /// </summary>
    [Fact]
    public async Task TheOrphanIsNotReadAndItsFilesStayAsTheyWere()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var robloxBefore = Files(dir.Path).Where(f => f.Key.Contains(RobloxSlug, StringComparison.Ordinal)).ToList();

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.Equal(RobloxSlug, Assert.Single(services.Orphans).Recipe.Slug);
        Assert.Contains(services.Sources, s => s.Id == "s-a0000006");
        Assert.DoesNotContain(services.ActiveSources, s => s.Id == "s-a0000006");
        Assert.Null(services.Runner.WatchFor("s-a0000006"));
        Assert.All(services.Sources.Where(s => s.Recipe != RobloxSlug), s => Assert.NotNull(services.Runner.WatchFor(s.Id)));

        Assert.Equal(4, robloxBefore.Count);
        Assert.Equal(robloxBefore, Files(dir.Path).Where(f => f.Key.Contains(RobloxSlug, StringComparison.Ordinal)).ToList());
    }

    /// <summary>
    /// The upgrade writes settings.json and nothing else: the explicit modes map (both modes on, every reader having been
    /// installed), with everything else in the file as it was. The recipes folder, sources, boards and book are untouched.
    /// </summary>
    [Fact]
    public async Task AfterStartTheOnlyFileWrittenIsSettingsWithTheExplicitModesMap()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var before = Files(dir.Path);

        using (var services = Compose(dir))
        {
            await services.LoadBookAsync();
            Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
            Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        }

        var after = Files(dir.Path);
        Assert.Equal(before.Keys.Order(StringComparer.OrdinalIgnoreCase), after.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["settings.json"], after.Where(f => before[f.Key] != f.Value).Select(f => f.Key));

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(dir.Path, "settings.json")))!.AsObject();
        var modes = settings["modes"]!.AsObject();
        Assert.Equal(
            [("pet-sim-99", true), ("pet-sim-99/battle", true), ("pet-sim-99/profile", true)],
            modes.Select(kv => (kv.Key, kv.Value!.GetValue<bool>())).OrderBy(x => x.Key, StringComparer.Ordinal));
        Assert.Equal(BattleSlug, settings["activeRecipe"]!.GetValue<string>());
        // Version 3 is what makes it decided (review round 2): the map alone no longer is.
        Assert.Equal(Settings.CurrentVersion, settings["settingsVersion"]!.GetValue<int>());

        // Decided once: the second start finds the map and writes nothing at all.
        var settled = Files(dir.Path);
        using (var again = Compose(dir)) await again.LoadBookAsync();
        Assert.Equal(settled, Files(dir.Path));
    }

    /// <summary>
    /// A 0.6.3 player who read only Battle stays on Battle: Profile comes up off and its source is not read, and nothing
    /// is added for it. The decoy is real: the fixture's profile source is enabled and would read if the gate missed it.
    /// </summary>
    [Fact]
    public async Task ABattleOnlyInstallDoesNotStartReadingProfiles()
    {
        using var dir = OldInstallFixtureTests.Copy();
        File.Delete(Path.Combine(new AppPaths(dir.Path).Recipes, ProfileSlug + ".recipe.json"));
        var sourcesBefore = File.ReadAllText(new AppPaths(dir.Path).Sources);

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.False(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
        Assert.True(Assert.Single(services.Sources, s => s.Recipe == ProfileSlug).Enabled);
        Assert.Null(services.Runner.WatchFor("s-a0000005"));
        Assert.NotNull(services.Runner.WatchFor(Main));
        Assert.Equal(sourcesBefore, File.ReadAllText(new AppPaths(dir.Path).Sources));
        Assert.Contains(services.Boards, b => b.Follows == "battle");
        Assert.DoesNotContain(services.Boards, b => b.Follows == "alts");
    }

    /// <summary>
    /// A5's other half: a Battle-only 0.6.3 install had neither the profile recipe nor its source, and an input-less
    /// reader gets a source when it is new, so the gate has to be on that too or the upgrade writes sources.json with a
    /// profile source in it the first time it starts.
    /// </summary>
    [Fact]
    public async Task ABattleOnlyInstallGainsNoProfileSource()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var paths = new AppPaths(dir.Path);
        File.Delete(Path.Combine(paths.Recipes, ProfileSlug + ".recipe.json"));
        File.Delete(Path.Combine(paths.Recipes, ProfileSlug + ".state.json"));
        new SourceStore(paths.Sources).Save([.. new SourceStore(paths.Sources).Load().Where(s => s.Recipe != ProfileSlug)]);
        var sourcesBefore = File.ReadAllText(paths.Sources);

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.DoesNotContain(services.Sources, s => s.Recipe == ProfileSlug);
        Assert.Equal(sourcesBefore, File.ReadAllText(paths.Sources));
    }

    /// <summary>
    /// The other side of A5's "a fresh install writes no map": the first start of a fresh install writes sources.json (the
    /// two sources every install gets), so on the SECOND start the folder looks exactly like an upgrade with no recipe
    /// files, and the upgrade rule would turn every mode off. The fresh start records that the decision was made with an
    /// empty map (no switch written, every mode at its default), and the second start keeps reading.
    /// </summary>
    [Fact]
    public async Task AFreshInstallStillReadsEveryDefaultModeOnItsSecondStart()
    {
        using var dir = TempDir.Create("urscore-fresh-twice");

        using (var first = Compose(dir))
        {
            await first.LoadBookAsync();
            Assert.True(first.Switches.IsOn("pet-sim-99/battle"));
        }

        Assert.True(File.Exists(new AppPaths(dir.Path).Sources));
        using var second = Compose(dir);
        await second.LoadBookAsync();

        Assert.True(second.Switches.IsOn("pet-sim-99/battle"));
        Assert.True(second.Switches.IsOn("pet-sim-99/profile"));
        Assert.Empty(second.Settings.Modes!);
        Assert.NotNull(second.Runner.WatchFor(Assert.Single(second.Sources, s => s.Recipe == ProfileSlug).Id));
    }

    /// <summary>
    /// Score book continuity (spec "Score book continuity"): the 0.6.3 install's clan-battle text was an older snapshot, so
    /// its lines carry that text's hash, and 0.7.0's reads carry the built-in text's. The older snapshot is made here: the
    /// built-in recipe with a different credit line, its lines and version file re-hashed. (The fixture's own snapshot equalled
    /// the built-in text until 0.7.0's clan recipe gained its members list; this test no longer leans on that either way.) Then the running app appends two readings and a final under the built-in hash.
    /// Every reader of the book the board's panels use sees ONE series for the main clan: the series behind Pace and
    /// Standing, the records (highest, biggest day, best rank across the two finals), and the past periods.
    /// </summary>
    [Fact]
    public async Task TheBookIsOneSeriesAcrossTheOldSnapshotsHashAndTheBuiltInOne()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var paths = new AppPaths(dir.Path);
        var builtIn = BuiltInRecipes.Find(BattleSlug)!.Text;
        var newHash = BookFiles.Hash(builtIn);
        var oldText = builtIn.Replace("\"credit\": \"Data from", "\"credit\": \"Old data from", StringComparison.Ordinal);
        var oldHash = BookFiles.Hash(oldText);
        Assert.NotEqual(oldHash, newHash);

        // The install as 0.6.3 left it under the older snapshot: its recipe file, its version file, its lines, and one
        // final for a battle before, ranked second.
        File.WriteAllText(Path.Combine(paths.Recipes, BattleSlug + ".recipe.json"), oldText);
        File.Delete(BookFiles.RecipeFile(paths.Book, BattleSlug, newHash));
        File.WriteAllText(BookFiles.RecipeFile(paths.Book, BattleSlug, oldHash), oldText);
        var month = Path.Combine(paths.Book, BattleSlug, "2026-09.jsonl");
        var oldLines = File.ReadAllLines(month).Select(l => BookJson.TryParse(l)!).ToList();
        Assert.Single(oldLines.Select(l => l.Recipe.Hash).Distinct());
        var template = oldLines[^1];
        BookLine Final(string hash, string period, DateTimeOffset t, double value, int rank) => template with
        {
            Kind = BookLine.KindFinal, T = t, Trigger = BookLine.TriggerEnded, Recipe = new BookRecipeRef(BattleSlug, hash),
            Period = new BookPeriod(period),
            Accounts = new Dictionary<string, BookAccount>
            {
                ["900000001"] = new(new Dictionary<string, double> { ["value"] = value }, new Dictionary<string, int> { ["value"] = rank }, 12),
            },
        };
        File.WriteAllLines(month,
        [
            BookJson.Serialize(Final(oldHash, "OldBattle", new DateTimeOffset(2026, 9, 18, 18, 0, 0, TimeSpan.Zero), 30000, 2)),
            .. oldLines.Select(l => BookJson.Serialize(l with { Recipe = new BookRecipeRef(BattleSlug, oldHash) })),
        ]);

        using var services = Compose(dir);
        await services.LoadBookAsync();
        Assert.Equal(builtIn, Assert.Single(services.Installed, i => i.Recipe.Slug == BattleSlug).Text);

        BookLine Read(DateTimeOffset t, double points, double mine) => template with
        {
            T = t, Recipe = new BookRecipeRef(BattleSlug, newHash),
            Headline = new Dictionary<string, double> { ["clan-place"] = 7, ["clan-points"] = points },
            Accounts = new Dictionary<string, BookAccount>
            {
                ["900000001"] = new(new Dictionary<string, double> { ["value"] = mine }, new Dictionary<string, int> { ["value"] = 1 }, 12),
            },
        };
        services.Book.Append(Read(new DateTimeOffset(2026, 9, 20, 18, 15, 0, TimeSpan.Zero), 1_227_000, 41_500), builtIn);
        services.Book.Append(Read(new DateTimeOffset(2026, 9, 20, 18, 30, 0, TimeSpan.Zero), 1_236_000, 46_000), builtIn);
        services.Book.Append(Final(newHash, "PrevBattle", new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero), 52000, 1), builtIn);
        services.Book.Flush();
        await services.ReloadBookAsync();

        var hashes = BookFiles.ReadAll(paths.Book, BattleSlug).Where(l => l.Source == Main).Select(l => l.Recipe.Hash).Distinct();
        Assert.Equal(new[] { oldHash, newHash }.Order(StringComparer.Ordinal), hashes.Order(StringComparer.Ordinal));
        Assert.True(File.Exists(BookFiles.RecipeFile(paths.Book, BattleSlug, newHash)));

        var reader = services.Reader;

        // The series behind Standing and Pace: five readings, three under the old hash and two under the new, in order.
        var points = reader.HeadlineSeries(Main, "clan-points", "TestBattle");
        Assert.Equal([1_200_000d, 1_209_000, 1_218_000, 1_227_000, 1_236_000], points.Select(p => p.Value));
        // A pace window opening under the old hash and closing under the new: 27,000 points from 18:03 to 18:30. On the
        // new hash's readings alone it would be 9,000 in the 15 minutes from 18:15, a different number.
        var pace = Pace.Over(points, new DateTimeOffset(2026, 9, 20, 18, 3, 0, TimeSpan.Zero));
        Assert.NotNull(pace);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 18, 3, 0, TimeSpan.Zero), pace.From);
        Assert.Equal(60_000d, pace.PerHour, 3);

        var mine = reader.Series(Main, 900000001, "value", "TestBattle", DateTimeOffset.MinValue);
        Assert.Equal([40_000d, 40_500, 41_000, 41_500, 46_000], mine.Select(p => p.Value));

        // Records: the highest is the new hash's, the biggest day spans both, and the best rank is the new final's
        // while the best period is the old one's, each found only by reading both hashes.
        var records = Records.For(reader, BattleSlug, "clan=testclan", Main, 900000001, "value", new ManualTime(Now));
        Assert.Equal(2, records.PeriodsPlayed);
        Assert.Equal((52_000d, "PrevBattle"), (records.BestPeriodValue!.Value, records.BestPeriod!));
        Assert.Equal((1, "PrevBattle"), (records.BestRank!.Value, records.BestRankPeriod!));
        Assert.Equal(52_000d, records.Highest);
        Assert.Equal(6_000d, records.BiggestDay);

        // Past periods: both finals, one per hash.
        Assert.Equal(["OldBattle", "PrevBattle"], reader.Finals(BattleSlug, "clan=testclan").Select(f => f.Period).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Review round 2: the upgrade reads exactly what 0.6.3 read, sources included. A 0.6.3 player who imported the
    /// clan-battle recipe and never the clans list had Battle, so Battle comes up on, but the clans list was never
    /// installed: the input-less source every NEW reader gets is not added on this start, and sources.json is not written.
    /// Turning Battle off and on later is a choice, and that one still adds it.
    /// </summary>
    [Fact]
    public async Task AClanBattleOnlyInstallGainsNoClansListSourceOnItsUpgrade()
    {
        using var dir = OldInstallFixtureTests.Copy();
        var paths = new AppPaths(dir.Path);
        foreach (var slug in new[] { TopSlug, ProfileSlug })
        {
            File.Delete(Path.Combine(paths.Recipes, slug + ".recipe.json"));
            File.Delete(Path.Combine(paths.Recipes, slug + ".state.json"));
        }

        new SourceStore(paths.Sources).Save([.. new SourceStore(paths.Sources).Load().Where(s => s.Recipe is not (TopSlug or ProfileSlug))]);
        var sourcesBefore = File.ReadAllText(paths.Sources);

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
        Assert.DoesNotContain(services.Sources, s => s.Recipe == TopSlug);
        Assert.Equal(sourcesBefore, File.ReadAllText(paths.Sources));

        services.SetSwitch("pet-sim-99/battle", false);
        services.SetSwitch("pet-sim-99/battle", true);
        Assert.Contains(services.Sources, s => s.Recipe == TopSlug && s.Role == SourceRole.Watch);
    }

    /// <summary>The fixture with Battle's readers alone on disk: the install whose upgrade turns Profile off.</summary>
    private static TempDir.Scope BattleOnly()
    {
        var dir = OldInstallFixtureTests.Copy();
        File.Delete(Path.Combine(new AppPaths(dir.Path).Recipes, ProfileSlug + ".recipe.json"));
        return dir;
    }

    /// <summary>
    /// Review round 2, HIGH: a settings.json that can't be read is not an install that had nothing. It used to load as the
    /// defaults with no map, so the upgrade ran over the recipe files and wrote its map on top of the broken file (here:
    /// Profile off, the player's file gone). Now the file stays byte for byte, every mode runs on its default, and a save
    /// is refused with the reason, as sources.json's is.
    /// </summary>
    [Fact]
    public async Task AnUnreadableSettingsFileIsLeftAloneAndEveryModeRunsOnItsDefault()
    {
        using var dir = BattleOnly();
        var path = new AppPaths(dir.Path).Settings;
        File.WriteAllText(path, "{ \"modes\": { broken");
        var bytes = File.ReadAllBytes(path);

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
        Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.NotNull(services.Runner.WatchFor("s-a0000005"));
        Assert.Contains(services.Trail, line => line.Contains("SETTINGS NOT READ", StringComparison.Ordinal));
        var refused = Assert.Throws<InvalidOperationException>(() => services.SetSwitch("pet-sim-99/profile", false));
        Assert.Equal(AppServices.SettingsNotWritten, refused.Message);
        Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>
    /// A deleted settings.json in a folder 0.6.3 used is not an upgrade either: there is nothing to say what was decided,
    /// so the defaults stand (every mode on), written as version 3. The old rule saw a used folder with no map and ran
    /// the upgrade over the recipe files, turning Profile off here.
    /// </summary>
    [Fact]
    public async Task AMissingSettingsFileInAUsedFolderTakesTheDefaultsAsVersionThree()
    {
        using var dir = BattleOnly();
        var path = new AppPaths(dir.Path).Settings;
        File.Delete(path);

        using (var services = Compose(dir))
        {
            await services.LoadBookAsync();
            Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
            Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        }

        var written = Settings.Load(path);
        Assert.Equal(Settings.CurrentVersion, written.SettingsVersion);
        Assert.Empty(written.Modes!);
    }

    /// <summary>A version 3 file was decided, map or not: no map means the defaults, and the recipe files are never consulted again.</summary>
    [Fact]
    public async Task AVersionThreeFileWithNoMapIsNotUpgradedAgain()
    {
        using var dir = BattleOnly();
        var path = new AppPaths(dir.Path).Settings;
        File.WriteAllText(path, """{ "resolveNames": true, "startOnOpen": true, "settingsVersion": 3 }""");
        var bytes = File.ReadAllBytes(path);

        using var services = Compose(dir);
        await services.LoadBookAsync();

        Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.DoesNotContain(services.Trail, line => line.Contains("MODES: set from what was installed", StringComparison.Ordinal));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>
    /// A fresh first start whose settings write failed (here a folder sits where the file goes) still wrote sources.json.
    /// The second start then saw a used folder with no recipe files and no map, which the old rule read as "an install
    /// that had nothing" and switched every mode off. Now a missing file is never an upgrade, so both modes read.
    /// </summary>
    [Fact]
    public async Task AFirstStartWhoseSettingsWriteFailedStillReadsEveryModeOnTheNextStart()
    {
        using var dir = TempDir.Create("urscore-settings-failed");
        var paths = new AppPaths(dir.Path);
        Directory.CreateDirectory(paths.Settings);

        using (var first = Compose(dir)) await first.LoadBookAsync();

        Assert.True(File.Exists(paths.Sources));
        Directory.Delete(paths.Settings);
        using var second = Compose(dir);
        await second.LoadBookAsync();

        Assert.True(second.Switches.IsOn("pet-sim-99/battle"));
        Assert.True(second.Switches.IsOn("pet-sim-99/profile"));
        Assert.NotNull(second.Runner.WatchFor(Assert.Single(second.Sources, s => s.Recipe == ProfileSlug).Id));
    }

    /// <summary>Answers every fetch with a 404: these tests compose the app and never start a read.</summary>
    private sealed class NothingTransport : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken) =>
            Task.FromResult(new FetchResult(404, "{}", null));
    }
}
