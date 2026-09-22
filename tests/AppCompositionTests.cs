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
/// The composition root, composed for real (S1-14.9): every service the app runs on, built from a folder of the
/// test's own, with a stub where RoRoRo's pipe would be and a fake where the network would be. Everything else is
/// the real thing — the stores, the book, the reader, the watches, the policy, the boards. Until 2026-09-22 this was
/// the largest uncovered piece in the suite, because <c>AppServices</c> built every path from the user's data
/// folder and its client from the pipe, so the only way to run it was to run the app.
/// <para>
/// A test here that leaves the host or the transport null has built the app FOR REAL — a dev build is
/// indistinguishable from the installed plugin to RoRoRo (V3-S.43) — so none does, and the first test says so.
/// </para>
/// </summary>
public class AppCompositionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private const string Battle = """{ "status": "ok", "data": { "configName": "B" } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": { "B": {
            "Place": 3, "Points": 999,
            "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ]
        } } } }
        """;

    private static AppServices Compose(TempDir.Scope dir, IHostClient host, IRecipeTransport transport) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(dir.Path), host, transport, new ManualTime(Start), Path.Combine(dir.Path, "metric-rules.json"));

    /// <summary>
    /// The nine places that each worked out the data folder for themselves now read one record, and the record
    /// says what they said. A default that drifted from the app's own composition would be a file the app wrote
    /// somewhere it never reads.
    /// </summary>
    [Fact]
    public void EveryDefaultPathIsTheOneAppPathsNames()
    {
        var paths = AppPaths.Default;

        Assert.EndsWith(AppPaths.FolderName, paths.Root, StringComparison.Ordinal);
        Assert.Equal(paths.Keys, KeyStore.DefaultPath);
        Assert.Equal(paths.Recipes, RecipeStore.DefaultDirectory);
        Assert.Equal(paths.Settings, Settings.DefaultPath);
        Assert.Equal(paths.Accounts, AccountsCache.DefaultPath);
        Assert.Equal(paths.Sources, SourceStore.DefaultPath);
        Assert.Equal(paths.Boards, BoardsFile.DefaultPath);
        Assert.Equal(paths.Book, BookFiles.DefaultRoot);
        Assert.Equal(paths.IconCache, IconClient.DefaultCacheDirectory);
    }

    /// <summary>
    /// An empty folder composes to nothing installed, nothing listed, the starter boards following, and a book that
    /// loads empty — and every file the composition makes is under that folder. RoRoRo is a stub that is not there,
    /// so the account list is the saved one, which is none.
    /// </summary>
    [Fact]
    public async Task AnEmptyFolderComposesToNothingInstalledAndWritesOnlyThere()
    {
        using var dir = TempDir.Create("urscore-app");
        var host = new StubHost(reachable: false);
        using var services = Compose(dir, host, new FakeTransport());

        Assert.Empty(services.Installed);
        Assert.Empty(services.Sources);
        Assert.Empty(services.KnownAccounts);
        Assert.False(services.ReaderLoaded);
        Assert.NotEmpty(services.Boards);
        Assert.All(services.Boards, board => Assert.NotNull(board.Follows));

        await services.LoadBookAsync();

        Assert.True(services.ReaderLoaded);
        Assert.Equal(0, host.AccountCalls);
        Assert.Empty(host.Reported);
        Assert.Contains(services.Trail, line => line.Contains("BOOK: loaded from " + Path.Combine(dir.Path, "scorebook"), StringComparison.Ordinal));
        Assert.All(Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories),
            file => Assert.StartsWith(dir.Path, file, StringComparison.Ordinal));
    }

    /// <summary>
    /// An installed recipe and a source compose to a watch, and a read through it goes the whole way: the fake
    /// transport answers the recipe's two steps, the engine reads the clan, the watch matches the stub host's one
    /// account, the book keeps the line on disk under the test's folder, the reader has it, the window's snapshot
    /// names the source — and nothing is sent, because the state ticks the stat to show and not to send. This is the
    /// startup the app does, done by a test.
    /// </summary>
    [Fact]
    public async Task ARecipeAndASourceComposeToAWatchThatReadsAndKeepsALine()
    {
        using var dir = TempDir.Create("urscore-app");
        var paths = new AppPaths(dir.Path);
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        new RecipeStore(paths.Recipes).Save(recipe, text, new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["value"] = new(Show: true, Send: false, MetricId: "clan.battle.points"),
        }));
        var source = new Source("s-00000001", recipe.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine);
        new SourceStore(paths.Sources).Save([source]);
        var host = new StubHost(reachable: true, Alt);
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/K0i2", 200, ClanResponse);
        using var services = Compose(dir, host, transport);

        Assert.Equal(recipe.Slug, Assert.Single(services.Installed).Recipe.Slug);
        Assert.Equal("s-00000001", Assert.Single(services.Sources).Id);
        await services.LoadBookAsync();
        Assert.NotNull(services.Runner.WatchFor("s-00000001"));

        var snapshot = await services.ReadOnceAsync("s-00000001", CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("s-00000001", snapshot.SourceId);
        Assert.True(snapshot.Recorded, snapshot.NotRecordingReason);
        Assert.Equal(2, snapshot.RowsSeen);
        Assert.Equal(Alt, Assert.Single(services.KnownAccounts));
        Assert.Equal(2, transport.Requests.Count);

        // The book writes on its own thread; wait for the line to reach the disk. (Its word to the reader goes through
        // the window's dispatcher, which nothing pumps here, so the reader is not what this test reads.)
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!BookFiles.ReadAll(paths.Book, recipe.Slug).Any())
        {
            Assert.True(DateTime.UtcNow < until, "the book never wrote the line");
            await Task.Delay(10);
        }

        var line = Assert.Single(BookFiles.ReadAll(paths.Book, recipe.Slug));
        Assert.Equal((BookLine.KindRead, BookLine.TriggerManual, "s-00000001"), (line.Kind, line.Trigger, line.Source));
        Assert.Equal(4200d, line.Accounts["111"].V["value"]);
        Assert.DoesNotContain("222", line.Accounts.Keys);
        Assert.Same(snapshot, services.Latest["s-00000001"]);
        Assert.Empty(host.Reported);
        Assert.Contains(services.Boards, board => board.Panels.Any(p => p.Settings.SourceId == "s-00000001"));
    }

    /// <summary>
    /// Export stats on one PC, Import stats on another, through the real composition on both sides: the first PC's
    /// book (a generated one, under the clan-battle recipe) goes into one file; the second PC, with the same recipe
    /// and a source for one of the clans, imports it and keeps that clan's readings under its OWN source id while the
    /// other clan is named as not set up. Nothing but the score book is in the file — no key, no source, no board.
    /// </summary>
    [Fact]
    public async Task StatsExportedOnOnePcImportOnAnotherUnderItsOwnSourceIds()
    {
        using var first = TempDir.Create("urscore-app-a");
        using var second = TempDir.Create("urscore-app-b");
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var written = BookGenerator.Write(new AppPaths(first.Path).Book, clanSources: 2, days: 1);
        var file = Path.Combine(first.Path, BookPack.FileName(Start));
        using (var exporter = Compose(first, new StubHost(reachable: false), new FakeTransport()))
        {
            var manifest = exporter.ExportStats(file);
            Assert.Equal((written.Lines - written.Finals, written.Finals), (manifest.Readings, manifest.Finals));
        }

        using var zip = System.IO.Compression.ZipFile.OpenRead(file);
        Assert.All(zip.Entries, entry => Assert.True(entry.FullName == BookPack.ManifestName || entry.FullName.StartsWith("scorebook/", StringComparison.Ordinal), entry.FullName));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("keys", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("sources.json", StringComparison.Ordinal));

        // The second PC follows Clan0 under an id of its own; Clan1 it does not follow.
        var paths = new AppPaths(second.Path);
        new RecipeStore(paths.Recipes).Save(recipe, text, new RecipeState());
        new SourceStore(paths.Sources).Save([new Source("s-0000beef", recipe.Slug, new Dictionary<string, string> { ["clan"] = "Clan0" }, SourceRole.Mine)]);
        using var importer = Compose(second, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();

        var outcome = BookImport.RunFile(file, importer);

        Assert.Equal("", outcome.Problem);
        // The generator writes two clan sources and a clans list, a reading each per tick: a third of the readings are Clan0's.
        Assert.Equal((written.Lines - written.Finals) / 3, outcome.Added);
        Assert.Contains("Clan1", outcome.Message, StringComparison.Ordinal);
        Assert.Contains($"No recipe here for: {BookGenerator.ListSlug}", outcome.Message, StringComparison.Ordinal);
        var lines = BookFiles.ReadAll(paths.Book, recipe.Slug).ToList();
        Assert.Equal(outcome.Added, lines.Count);
        Assert.All(lines, line => Assert.Equal("s-0000beef", line.Source));
        Assert.All(lines, line => Assert.Equal("Clan0", line.Inputs["clan"]));
    }

    private sealed class FakeTransport : IRecipeTransport
    {
        private readonly List<(string UrlStart, FetchResult Result)> _routes = [];

        public List<Uri> Requests { get; } = [];

        public FakeTransport On(string urlStart, int status, string body)
        {
            _routes.Add((urlStart, new FetchResult(status, body, null)));
            return this;
        }

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            Requests.Add(url);
            var route = _routes.FirstOrDefault(r => url.AbsoluteUri.StartsWith(r.UrlStart, StringComparison.Ordinal));
            return Task.FromResult(route.Result ?? new FetchResult(404, "{}", null));
        }
    }
}
