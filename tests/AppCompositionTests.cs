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
    /// <para>
    /// Since the runtime fence (RuntimeFenceTests, port of K0ii's fe8d103) a test process can't resolve
    /// <c>AppPaths.Default</c> at all, so the nine can no longer be compared with it as strings. What stays provable here:
    /// every one of them reads it, because every one refuses exactly when it does, with its message. A default that
    /// worked its folder out on its own again would resolve here instead of throwing.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDefaultPathIsTheOneAppPathsNames()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => AppPaths.Default).Message;

        Func<object>[] defaults =
        [
            () => KeyStore.DefaultPath, () => RecipeStore.DefaultDirectory, () => Settings.DefaultPath, () => AccountsCache.DefaultPath,
            () => SourceStore.DefaultPath, () => BoardsFile.DefaultPath, () => BookFiles.DefaultRoot, () => IconClient.DefaultCacheDirectory,
        ];
        Assert.All(defaults, resolve => Assert.Equal(refusal, Assert.Throws<InvalidOperationException>(resolve).Message));
    }

    /// <summary>
    /// An empty folder composes to the readers built into the app (games and modes: nothing is installed by hand any
    /// more), each input-less one with its source as an import gave it (the clans list watched, the profile yours),
    /// an empty modes map (a fresh install writes no switch and takes the manifest defaults, A5; empty records that it
    /// decided), nothing listed, the starter boards following, and a book that loads empty — and every file the composition makes is under that folder. RoRoRo is a
    /// stub that is not there, so the account list is the saved one, which is none.
    /// </summary>
    [Fact]
    public async Task AnEmptyFolderComposesToTheBuiltInReadersAndWritesOnlyThere()
    {
        using var dir = TempDir.Create("urscore-app");
        var host = new StubHost(reachable: false);
        using var services = Compose(dir, host, new FakeTransport());

        Assert.Equal(["pet-sim-99-clan-battle-points", "pet-sim-99-top-clans", "pet-sim-99-profile"], services.Installed.Select(i => i.Recipe.Slug));
        Assert.Equal(
            [("pet-sim-99-top-clans", SourceRole.Watch), ("pet-sim-99-profile", SourceRole.Mine)],
            services.Sources.Select(s => (s.Recipe, s.Role)));
        Assert.Empty(services.Settings.Modes!);
        Assert.Empty(services.Orphans);
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
    /// <para>
    /// Since games and modes the folder's recipe file is read only for its slug (the text is the built-in one, which this
    /// fixture's happens to equal) and, there being a version 2 settings.json, as a 0.6.3 install: Battle on because its reader
    /// was installed, Profile off because it wasn't (A5), and Battle's clans list gets the one source a mode's input-less
    /// reader always gets. The settings file is what makes it an upgrade (review round 2): every 0.6.3 start wrote one, and
    /// a folder without one takes the defaults.
    /// </para>
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
        Settings.Save(new Settings(StartOnOpen: true, SettingsVersion: Settings.StartOnOpenVersion), paths.Settings);
        var host = new StubHost(reachable: true, Alt);
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/K0i2", 200, ClanResponse);
        using var services = Compose(dir, host, transport);

        Assert.Equal(BuiltInRecipes.Find(recipe.Slug)!.Text, Assert.Single(services.Installed, i => i.Recipe.Slug == recipe.Slug).Text);
        Assert.True(services.Switches.IsOn("pet-sim-99/battle"));
        Assert.False(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.Equal(
            [("s-00000001", recipe.Slug), (null, "pet-sim-99-top-clans")],
            services.Sources.Select(s => (s.Id == "s-00000001" ? s.Id : null, s.Recipe)));
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
    /// other clan is named as not set up.
    /// <para>
    /// Since games and modes the second PC, having a sources.json, starts as a 0.6.3 install with Battle on, so Battle's
    /// clans list has its watched source there too and the first PC's list readings land under it: two thirds of the
    /// readings arrive (Clan0's and the list's), where before the list's were named as having no recipe here.
    /// </para>
    /// <para>
    /// Since 2026-09-22 the file may also carry this PC's setup, so a <c>setup/</c> folder is allowed here — though
    /// there is none in this one, <c>first</c> installing no recipe and saving no source before exporting
    /// (<see cref="APristinePcsExportCarriesNoSetupAndOpensAsAStatsOnlyFile"/> pins that). What a setup pack may and
    /// may never carry is <c>SetupPackTests</c>' job. What still must never happen, from any PC in any state, is the
    /// real key store reaching the file at all.
    /// </para>
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
            var manifest = exporter.ExportStats(file).Manifest;
            Assert.Equal((written.Lines - written.Finals, written.Finals), (manifest.Readings, manifest.Finals));
        }

        using var zip = System.IO.Compression.ZipFile.OpenRead(file);
        Assert.All(zip.Entries, entry => Assert.True(
            entry.FullName == BookPack.ManifestName
            || entry.FullName.StartsWith("scorebook/", StringComparison.Ordinal)
            || entry.FullName.StartsWith("setup/", StringComparison.Ordinal),
            entry.FullName));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.EndsWith("keys.dat", StringComparison.Ordinal));

        // The second PC follows Clan0 under an id of its own; Clan1 it does not follow.
        var paths = new AppPaths(second.Path);
        new RecipeStore(paths.Recipes).Save(recipe, text, new RecipeState());
        new SourceStore(paths.Sources).Save([new Source("s-0000beef", recipe.Slug, new Dictionary<string, string> { ["clan"] = "Clan0" }, SourceRole.Mine)]);
        using var importer = Compose(second, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();

        var outcome = BookImport.RunFile(file, importer);

        Assert.Equal("", outcome.Problem);
        // The generator writes two clan sources and a clans list, a reading each per tick: a third of the readings are Clan0's
        // and a third are the list's.
        var third = (written.Lines - written.Finals) / 3;
        Assert.Equal(2 * third, outcome.Added);
        Assert.Contains("Clan1", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Not part of any mode", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(third, BookFiles.ReadAll(paths.Book, BookGenerator.ListSlug).Count());
        var lines = BookFiles.ReadAll(paths.Book, recipe.Slug).ToList();
        Assert.Equal(third, lines.Count);
        Assert.All(lines, line => Assert.Equal("s-0000beef", line.Source));
        Assert.All(lines, line => Assert.Equal("Clan0", line.Inputs["clan"]));
    }

    /// <summary>
    /// What a PC with nothing set up exports: nothing. <c>setup: false</c>, no <c>setup/</c> folder, opened as a
    /// stats-only file, so no preview offers a person their own empty setup back (spec §1, final review's ruling on
    /// finding 3; <see cref="SetupPack.IsEmpty"/>). Since games and modes a pristine PC holds the shipped readers and the
    /// two sources every install gets, so "empty" is "nothing the person made" (item 9): no changed tick, no clan, no
    /// board of their own, no switch away from its default. The file is still a v:2 stats file 0.6.3 reads.
    /// </summary>
    [Fact]
    public void APristinePcsExportCarriesNoSetupAndOpensAsAStatsOnlyFile()
    {
        using var dir = TempDir.Create("urscore-app-pristine");
        BookGenerator.Write(new AppPaths(dir.Path).Book, clanSources: 1, days: 1);
        var file = Path.Combine(dir.Path, BookPack.FileName(Start));
        using (var exporter = Compose(dir, new StubHost(reachable: false), new FakeTransport()))
        {
            var exported = exporter.ExportStats(file);

            Assert.False(exported.Manifest.Setup);
            Assert.Equal(BookPack.StatsOnlyVersion, exported.Manifest.V);
            Assert.Null(exported.Setup);
        }

        using (var zip = System.IO.Compression.ZipFile.OpenRead(file))
        {
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("setup/", StringComparison.Ordinal));
        }

        var opened = BookPack.Open(file);
        try
        {
            Assert.False(opened.Manifest!.Setup);
            Assert.Null(opened.Setup);
        }
        finally
        {
            BookPack.Discard(opened);
        }
    }

    /// <summary>
    /// The whole setup, PC to PC, through the real composition on both sides (spec §6). A has two recipes, three
    /// clans, an edited board and a generated book; B starts with an unreadable boards.json and is otherwise
    /// pristine. After the import with everything ticked, B's clans have B's own ids, B's board points at them,
    /// every send is off, B's own starter tabs are still there beside the imported board (spec §2: an import
    /// deletes nothing), the unreadable boards file does not survive the import, and the stats travel too, through
    /// the same book merge the stats-only import uses. Then into a B that already WATCHES one of the clans: the
    /// plan says Replace, and afterward B has one such clan, not two.
    /// <para>
    /// Since games and modes both PCs read the built-in text, and since item 9 the file carries no text at all: a reader
    /// travels as its ticks and B compares them by state (A7). A's clan-battle reader has a tick changed from the seed, so
    /// it is one Update (one mode, was 2 recipes); the clans list holds nothing but a sent field list, which never
    /// travels, so it is not in the file. B, a fresh install, already watches the clans list (the source every install
    /// gets), so that clan is "same as here": 2 clans applied, was 3. B ends with the three built-in readers (was 2)
    /// and four sources: its own two plus A's two clans (was 3).
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheWholeSetupTravelsAndArrivesUnderTheOtherPcsOwnIds()
    {
        using var a = TempDir.Create("urscore-app-a");
        using var b = TempDir.Create("urscore-app-b");
        try
        {
            var clanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
            var clan = RecipeParser.Parse(clanText).Recipe!;
            var topText = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
            var top = RecipeParser.Parse(topText).Recipe!;
            var pathsA = new AppPaths(a.Path);
            new RecipeStore(pathsA.Recipes).Save(clan, clanText, new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points.mine") }));   // a pinned id away from the seed
            new RecipeStore(pathsA.Recipes).Save(top, topText, new RecipeState(SentFieldMetrics: [FieldMetrics.ThreatGap]));
            var sourcesA = new List<Source>
            {
                new("s-000000a1", clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main),
                new("s-000000a2", clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Mine),
                new("s-000000a3", top.Slug, new Dictionary<string, string>(), SourceRole.Watch),
            };
            new SourceStore(pathsA.Sources).Save(sourcesA);
            var written = BookGenerator.Write(pathsA.Book, clanSources: 2, days: 1);
            var file = Path.Combine(a.Path, "everything.zip");
            using (var exporter = Compose(a, new StubHost(reachable: false), new FakeTransport()))
            {
                exporter.SaveImportedBoards([new BoardDef("b-a1", "Rivals", [new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(clan.Slug, SourceId: "s-000000a1"))])]);
                var manifest = exporter.ExportStats(file).Manifest;
                Assert.True(manifest.Setup);
                Assert.Equal((written.Lines - written.Finals, written.Finals), (manifest.Readings, manifest.Finals));
            }

            // B's boards.json is there but unreadable (R3) — the import must still land, and the trouble must clear.
            File.WriteAllText(Path.Combine(b.Path, "boards.json"), "{ not valid json");

            using var importer = Compose(b, new StubHost(reachable: false), new FakeTransport());
            Assert.NotNull(importer.BoardsProblem);
            await importer.LoadBookAsync();
            var opened = BookPack.Open(file);
            Assert.NotNull(opened.Setup);
            var plan = SetupMerge.Plan(opened.Setup!, importer.SetupWriter.Here, opened.Manifest!.Readings, opened.Manifest.Finals);
            var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), importer.SetupWriter, Start);

            Assert.Null(applied.FailedStep);
            Assert.Equal((1, 2, 1), (applied.Recipes, applied.Clans, applied.Boards));
            Assert.Equal(SetupOutcome.Update, Assert.Single(plan.Items, i => i.Kind == SetupKind.Recipe).Outcome);
            var arrived = importer.Installed.Single(i => i.Recipe.Slug == clan.Slug).State.StatChoices["value"];
            Assert.Equal((true, false, "clan.battle.points.mine"), (arrived.Show, arrived.Send, arrived.MetricId));                // the tick travelled; the send did not
            Assert.Equal(3, importer.Installed.Count);
            Assert.All(importer.Installed.SelectMany(i => i.State.StatChoices.Values), choice => Assert.False(choice.Send));
            // The second send list too: A's clans list ticks a clan-and-field number, and B must arrive with
            // none — PolicyFor hands FieldMetricKeys to every ReportPolicy whatever the clan's role.
            Assert.All(importer.Installed, i => Assert.Empty(i.State.FieldMetricKeys));
            Assert.Equal(4, importer.Sources.Count);
            Assert.All(importer.Sources, s => Assert.DoesNotContain(s.Id, sourcesA.Select(x => x.Id)));
            var k0i2 = Assert.Single(importer.Sources, s => s.InputsKey == "clan=k0i2");
            Assert.NotNull(importer.Runner.WatchFor(k0i2.Id));
            var rivals = Assert.Single(importer.SavedBoards, bd => bd.Name == "Rivals");
            Assert.Equal(k0i2.Id, rivals.Panels[0].Settings.SourceId);
            Assert.Null(importer.BoardsProblem);
            Assert.Contains(importer.Trail, line => line.Contains("BOARDS: the unreadable boards file was kept as", StringComparison.Ordinal));
            Assert.True(Directory.GetDirectories(b.Path, "626labs.ur-score.before-import-*").Length == 1 || Directory.GetDirectories(Path.GetDirectoryName(b.Path)!, Path.GetFileName(b.Path) + ".before-import-*").Length == 1);

            // The starter tabs are still there beside the imported one: an import adds and replaces, it never removes.
            Assert.Contains(importer.Boards, board => board.Name == "Rivals");
            Assert.Contains(importer.Boards, board => board.Follows is not null);

            // The setup's own stats travel too, through the same merge the stats-only import uses (BookImport.Run).
            // B has no source named Clan0 or Clan1 — only the top-clans watch, whose empty inputs match on both
            // sides — so exactly the list source's readings land, and the two clan sources are named, not guessed at.
            var bookOutcome = BookImport.Run(opened.Folder!, importer);
            BookPack.Discard(opened);
            var expectedListReadings = (written.Lines - written.Finals) / 3;
            Assert.Equal(expectedListReadings, bookOutcome.Added);
            Assert.Contains("Clan0", bookOutcome.Message, StringComparison.Ordinal);
            Assert.Equal(expectedListReadings, BookFiles.ReadAll(importer.Book.Root, top.Slug).Count());

            // A B that already watches K0i2: Replace, not a second clan.
            using var c = TempDir.Create("urscore-app-c");
            var pathsC = new AppPaths(c.Path);
            new RecipeStore(pathsC.Recipes).Save(clan, clanText, new RecipeState());
            new SourceStore(pathsC.Sources).Save([new Source("s-000000c1", clan.Slug, new Dictionary<string, string> { ["clan"] = "k0i2" }, SourceRole.Watch)]);
            using var watcher = Compose(c, new StubHost(reachable: false), new FakeTransport());
            await watcher.LoadBookAsync();
            var openedAgain = BookPack.Open(file);
            var planC = SetupMerge.Plan(openedAgain.Setup!, watcher.SetupWriter.Here, 0, 0);
            Assert.Equal(SetupOutcome.Replace, Assert.Single(planC.Items, i => i.Kind == SetupKind.Clan && i.Name == "K0i2").Outcome);
            SetupMerge.Apply(planC, planC.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), watcher.SetupWriter, Start);
            BookPack.Discard(openedAgain);

            var k0i2C = Assert.Single(watcher.Sources, s => s.InputsKey == "clan=k0i2");
            Assert.Equal(("s-000000c1", SourceRole.Main), (k0i2C.Id, k0i2C.Role));
        }
        finally
        {
            // The aside folder lands BESIDE the data root under %TEMP% (AsideFolder), not inside a/b/c's own scope,
            // so it survives a failed assertion above unless this cleanup runs whatever the outcome (finding 5c).
            foreach (var aside in Directory.GetDirectories(Path.GetTempPath(), "urscore-app-*.before-import-*")) Directory.Delete(aside, true);
        }
    }

    /// <summary>
    /// A 0.6.3 export holds a .recipe.json beside each state. Its text is ignored (B reads its own copy) and its ticks
    /// apply; the same ticks between two PCs that both carry the identical embedded text are an Update by state, which
    /// comparing by text could never see (A7).
    /// </summary>
    [Fact]
    public async Task AnOldExportWithRecipeTextImportsItsTicksAndIgnoresTheText()
    {
        using var a = TempDir.Create("urscore-app-a");
        using var b = TempDir.Create("urscore-app-b");
        var file = Path.Combine(a.Path, "old.zip");
        BookGenerator.Write(new AppPaths(a.Path).Book, clanSources: 1, days: 1);
        using (var exporter = Compose(a, new StubHost(reachable: false), new FakeTransport()))
        {
            var ticked = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: false, Send: true, MetricId: "clan.battle.points") });
            exporter.SaveRecipeState(exporter.Installed.Single(i => i.Recipe.Slug == "pet-sim-99-clan-battle-points").Recipe, ticked);
            Assert.True(exporter.ExportStats(file).Manifest.Setup);
        }

        // Make it the old shape: the sender's text beside the state, and an activeRecipe, as 0.6.3 wrote.
        using (var zip = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Update))
        {
            Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".recipe.json", StringComparison.Ordinal));
            using var writer = new StreamWriter(zip.CreateEntry("setup/recipes/pet-sim-99-clan-battle-points.recipe.json").Open());
            writer.Write("this text is the old sender's and must never be read");
        }

        using var importer = Compose(b, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();
        var opened = BookPack.Open(file);
        try
        {
            Assert.Equal("", opened.Problem);
            var plan = SetupMerge.Plan(opened.Setup!, importer.SetupWriter.Here, 0, 0);
            Assert.Equal(SetupOutcome.Update, Assert.Single(plan.Items, i => i.Kind == SetupKind.Recipe).Outcome);

            var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), importer.SetupWriter, Start);

            Assert.Null(applied.FailedStep);
            Assert.False(importer.Installed.Single(i => i.Recipe.Slug == "pet-sim-99-clan-battle-points").State.StatChoices["value"].Show);
            Assert.False(File.Exists(Path.Combine(new AppPaths(b.Path).Recipes, "pet-sim-99-clan-battle-points.recipe.json")), "no recipe text is ever saved by a merge");
        }
        finally
        {
            BookPack.Discard(opened);
            foreach (var aside in Directory.GetDirectories(Path.GetTempPath(), "urscore-app-*.before-import-*")) Directory.Delete(aside, true);
        }
    }

    /// <summary>modes.json round trips and applies live: the receiving PC's switch is off at once, saved, and its watch is gone.</summary>
    [Fact]
    public async Task ModeSwitchesTravelAndApplyLiveOnTheOtherPc()
    {
        using var a = TempDir.Create("urscore-app-a");
        using var b = TempDir.Create("urscore-app-b");
        var file = Path.Combine(a.Path, "modes.zip");
        BookGenerator.Write(new AppPaths(a.Path).Book, clanSources: 1, days: 1);
        using (var exporter = Compose(a, new StubHost(reachable: false), new FakeTransport()))
        {
            exporter.SetSwitch("pet-sim-99/profile", false);
            var exported = exporter.ExportStats(file);
            Assert.True(exported.Manifest.Setup);
            Assert.Equal(false, exported.Setup!.Settings.Modes!["pet-sim-99/profile"]);
        }

        using var importer = Compose(b, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();
        Assert.True(importer.Switches.IsOn("pet-sim-99/profile"));
        var opened = BookPack.Open(file);
        try
        {
            var plan = SetupMerge.Plan(opened.Setup!, importer.SetupWriter.Here, 0, 0);
            Assert.Equal(SetupOutcome.Update, Assert.Single(plan.Items, i => i.Kind == SetupKind.Mode).Outcome);

            var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), importer.SetupWriter, Start);

            Assert.Null(applied.FailedStep);
            Assert.True(applied.ModesApplied);
            Assert.False(importer.Switches.IsOn("pet-sim-99/profile"));                                   // live, without a restart
            Assert.False(Settings.Load(new AppPaths(b.Path).Settings).Modes!["pet-sim-99/profile"]);      // and saved
        }
        finally
        {
            BookPack.Discard(opened);
            foreach (var aside in Directory.GetDirectories(Path.GetTempPath(), "urscore-app-*.before-import-*")) Directory.Delete(aside, true);
        }
    }

    /// <summary>A book for a reader no mode names is skipped with the new line, not "No recipe here for".</summary>
    [Fact]
    public async Task ABookForAnOrphanReaderIsSkippedAsNotPartOfAnyMode()
    {
        using var a = TempDir.Create("urscore-app-a");
        using var b = TempDir.Create("urscore-app-b");
        var book = new AppPaths(a.Path).Book;
        BookGenerator.Write(book, clanSources: 1, days: 1);
        var orphan = Path.Combine(book, "roblox-followers");
        Directory.CreateDirectory(orphan);
        foreach (var month in Directory.EnumerateFiles(Path.Combine(book, BookGenerator.ClanSlug), "*.jsonl")) File.Copy(month, Path.Combine(orphan, Path.GetFileName(month)));

        using var importer = Compose(b, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();

        var outcome = BookImport.Run(Path.GetDirectoryName(book)!, importer);

        Assert.Contains("Not part of any mode: roblox-followers.", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("No recipe here", outcome.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(new AppPaths(b.Path).Book, "roblox-followers")));
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
