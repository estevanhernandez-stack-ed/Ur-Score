using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWatchBookTests
{
    private static readonly Guid Alt = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private static readonly HostAccount AltAccount = new(Alt, 111, "Alt One");

    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe Clan => RecipeParser.Parse(ClanText).Recipe!;

    private static readonly Dictionary<string, string> Inputs = new() { ["clan"] = "K0i2" };

    private static readonly SentStat Points = new("value", "Points", "clan.battle.points");

    private static Source SourceOf(SourceRole role, string id = "s-00000001") => new(id, Clan.Slug, Inputs, role);

    private static RecipeReading Reading(params RecipeRow[] rows) =>
        new(ReadingOutcome.Read, null, rows, [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], "battle=B", rows.Length)
        {
            Period = new ReadingPeriod("B", null, null),
        };

    private static RecipeWatch Watch(
        IRecipeEngine engine, StubHost host, IScoreBook book, Source source,
        SharedAccounts? shared = null, AccountClaims? claims = null, IReadOnlySet<string>? tracked = null,
        FinalsIndex? finals = null, TimeProvider? time = null, Recipe? recipe = null, string? text = null) =>
        new(engine, host, new NoKeys(), new ReportPolicy([Points], new HashSet<Guid> { Alt }), recipe ?? Clan, Inputs,
            tracked ?? new HashSet<string> { "value" },
            book, source, shared, text ?? ClanText, claims, finals, time);

    [Fact]
    public async Task AReadIsKeptWithOnlyYourAccountsAndStillSent()
    {
        var book = new MemoryBook();
        var host = new StubHost(true, AltAccount);
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200), EngineRow(222, 10)));

        var snapshot = await Watch(engine, host, book, SourceOf(SourceRole.Mine)).RunOnceAsync(CancellationToken.None, BookLine.TriggerManual);

        var line = Assert.Single(book.Lines);
        Assert.Equal((BookLine.KindRead, BookLine.TriggerManual, "s-00000001"), (line.Kind, line.Trigger, line.Source));
        Assert.Equal(new[] { "111" }, line.Accounts.Keys.ToArray());
        Assert.Equal(BookFiles.Hash(ClanText), line.Recipe.Hash);
        Assert.Equal(ClanText, Assert.Single(book.RecipeTexts));
        Assert.True(snapshot.Recorded);
        Assert.Null(snapshot.NotRecordingReason);
        Assert.Equal("s-00000001", snapshot.SourceId);
        Assert.Single(host.Reported);
    }

    private static RecipeRow EngineRow(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    [Fact]
    public async Task WithRoRoRoClosedTheSavedAccountsKeepRecordingAndNothingIsSent()
    {
        using var dir = TempDir.Create("urscore-watch");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([AltAccount]);
        var host = new StubHost(false);
        var book = new MemoryBook();
        var shared = new SharedAccounts(host, cache, TimeProvider.System);

        var snapshot = await Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), host, book, SourceOf(SourceRole.Mine), shared)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.HostDown, snapshot.State);
        Assert.Equal(new[] { "111" }, Assert.Single(book.Lines).Accounts.Keys.ToArray());
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task AStoppedReadKeepsNothingAndSaysWhy()
    {
        var book = new MemoryBook();

        var snapshot = await Watch(new StubEngine(() => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running")),
            new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine)).RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.False(snapshot.Recorded);
        Assert.Equal("No clan battle running", snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task AnIdleReadStillBacksItsFinishedBattlesIntoTheBookWithOnlyYourAccount()
    {
        // V3-S.1. The other rows are read (the engine hands over everyone's, exactly as on a good read)
        // and then dropped here: nobody else's id or number is written to the book.
        const long stranger = 987654321;
        var book = new MemoryBook();
        var past = new PastPeriodReading("Arcade2026", [EngineRow(111, 4200), EngineRow(stranger, 86420)],
            [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], RowsReadable: true);
        var engine = new StubEngine(() => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running") with { Past = [past] });

        var snapshot = await Watch(engine, new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine), finals: new FinalsIndex())
            .RunOnceAsync(CancellationToken.None);

        var line = Assert.Single(book.Lines);
        Assert.Equal((BookLine.KindFinal, BookLine.TriggerBackfill, "Arcade2026"), (line.Kind, line.Trigger, line.Period!.Value));
        Assert.Equal(new[] { "111" }, line.Accounts.Keys.ToArray());

        var written = BookJson.Serialize(line);
        Assert.Contains("4200", written, StringComparison.Ordinal);
        Assert.DoesNotContain(stranger.ToString(), written, StringComparison.Ordinal);
        Assert.DoesNotContain("86420", written, StringComparison.Ordinal);

        Assert.Equal(WatchState.SourceIdle, snapshot.State);
        Assert.Equal("No clan battle running", snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task AWatchSourceSendsNothingAndKeepsNoAccount()
    {
        var book = new MemoryBook();
        var host = new StubHost(true, AltAccount);

        var snapshot = await Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), host, book, SourceOf(SourceRole.Watch))
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Showing, snapshot.State);
        Assert.Equal(RecipeWatch.WatchOnlyDetail, snapshot.Detail);
        Assert.Empty(Assert.Single(book.Lines).Accounts);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task TwoSourcesThatSeeOneAccountKeepAndSendItOnce()
    {
        var claims = new AccountClaims(TimeProvider.System);
        var host = new StubHost(true, AltAccount);
        var book = new MemoryBook();
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200)));

        await Watch(engine, host, book, SourceOf(SourceRole.Main, "s-00000001"), claims: claims).RunOnceAsync(CancellationToken.None);
        await Watch(engine, host, book, SourceOf(SourceRole.Mine, "s-00000002"), claims: claims).RunOnceAsync(CancellationToken.None);

        Assert.Single(host.Reported);
        Assert.Equal(new[] { "111" }, book.Lines[0].Accounts.Keys.ToArray());
        Assert.Empty(book.Lines[1].Accounts);
    }

    [Fact]
    public async Task AGroupListIsShownAndNeverKept()
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var book = new MemoryBook();
        var reading = new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 2)
        {
            Groups = [new GroupRow("Aurelian", new Dictionary<string, double> { ["value"] = 1 }, 1), new GroupRow("SkyHarbor", new Dictionary<string, double> { ["value"] = 2 }, 2)],
        };

        var snapshot = await Watch(new StubEngine(() => reading), new StubHost(true, AltAccount), book,
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch), recipe: recipe, text: text)
            .RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(RecipeWatch.NotRecordingGroups, snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task ChangingASourcesRoleNeedsNoNewWatch()
    {
        var book = new MemoryBook();
        var watch = Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine));

        watch.UpdateSource(SourceOf(SourceRole.Watch));
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(SourceRole.Watch, watch.Source!.Role);
        Assert.Empty(Assert.Single(book.Lines).Accounts);
    }

    // Fix round 1, finding 1: RecipeWatch.Record wrote _previousPeriod without checking whether the
    // recipe or inputs it read with had since been replaced, so a mid-cycle UpdateRecipe could
    // resurrect the old recipe's period as "previous" for the new one. The fix guards that write the
    // same way Remember already guards its own write. The exact window the finding names — between
    // the top-of-cycle RecipeChanged check and Record's write, with no await between them — cannot be
    // hit by a synchronous stub engine the way the mid-read window below can: by the time Record could
    // run, the read has already completed, so there is no callback hook left to interleave on a single
    // thread. This test instead proves the guard that already exists (the top-of-cycle check, which
    // fires first and keeps Record from running at all here) still holds after the change, and the new
    // guard inside Record is otherwise covered only by inspection and the comment beside it.
    [Fact]
    public async Task ARecipeChangedDuringTheReadRecordsNothingAndKeepsNoLine()
    {
        var book = new MemoryBook();
        var followers = RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;
        RecipeWatch? watch = null;
        var engine = new StubEngine(() =>
        {
            watch!.UpdateRecipe(followers, new Dictionary<string, string>(), new HashSet<string> { "value" });
            return Reading(EngineRow(111, 4200));
        });
        watch = Watch(engine, new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine));

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.False(snapshot.Recorded);
        Assert.Null(snapshot.NotRecordingReason);
        Assert.Equal(RecipeWatch.RecipeChangedDetail, snapshot.Detail);
    }

    // Fix round 1, finding 2, part 1: a book attached to a watch that was never given the recipe's
    // text (the default empty string) must not hash and write "" as if it were real text.
    [Fact]
    public async Task NoRecipeTextRecordsNothingAndSaysWhy()
    {
        var book = new MemoryBook();

        var snapshot = await Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), new StubHost(true, AltAccount), book,
            SourceOf(SourceRole.Mine), text: "").RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.False(snapshot.Recorded);
        Assert.Equal(RecipeWatch.NotRecordingNoText, snapshot.NotRecordingReason);
    }

    // Fix round 1, finding 2, part 2: switching to a different recipe without also supplying its text
    // must not keep lining the new slug under the old recipe's text and hash; UpdateRecipe clears the
    // text on a slug change, and Record then keeps nothing until the caller supplies the new text.
    [Fact]
    public async Task SwitchingRecipesWithoutNewTextRecordsNothingUntilTextArrives()
    {
        var book = new MemoryBook();
        var followers = RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;
        var watch = Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine));

        watch.UpdateRecipe(followers, new Dictionary<string, string>(), new HashSet<string> { "value" });
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.False(snapshot.Recorded);
        Assert.Equal(RecipeWatch.NotRecordingNoText, snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task FinalsComeFirstAndReadingsStopOnceTheBattleHasEnded()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var book = new MemoryBook();
        var reading = new RecipeReading(ReadingOutcome.Read, null, [EngineRow(111, 4200)],
            [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], "battle=B", 1)
        {
            Period = new ReadingPeriod("B", null, time.Now.AddMinutes(-10)),
            Past =
            [
                new PastPeriodReading("A", [EngineRow(111, 300)], [new HeadlineValue("Clan place", "40") { Id = "clan-place", Number = 40 }], true),
                new PastPeriodReading("B", [EngineRow(111, 4200)], [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], true),
            ],
        };

        var snapshot = await Watch(new StubEngine(() => reading), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine),
            finals: new FinalsIndex(), time: time).RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { ("A", BookLine.TriggerBackfill), ("B", BookLine.TriggerEnded) }, book.Lines.Select(l => (l.Period!.Value, l.Trigger)).ToArray());
        Assert.All(book.Lines, l => Assert.Equal(BookLine.KindFinal, l.Kind));
        Assert.False(snapshot.Recorded);
        Assert.Equal(RecipeWatch.NotRecordingEnded, snapshot.NotRecordingReason);
    }
}
