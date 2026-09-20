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
        FinalsIndex? finals = null, TimeProvider? time = null, Recipe? recipe = null, string? text = null,
        IReadOnlyList<FieldMetric>? field = null, IReadOnlySet<string>? myGroups = null) =>
        new(engine, host, new NoKeys(), new ReportPolicy([Points], new HashSet<Guid> { Alt }, field), recipe ?? Clan, Inputs,
            tracked ?? new HashSet<string> { "value" },
            book, source, shared, text ?? ClanText, claims, finals, time,
            myGroups is null ? null : () => myGroups);

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

        var first = await Watch(engine, host, book, SourceOf(SourceRole.Main, "s-00000001"), claims: claims).RunOnceAsync(CancellationToken.None);
        var second = await Watch(engine, host, book, SourceOf(SourceRole.Mine, "s-00000002"), claims: claims).RunOnceAsync(CancellationToken.None);

        Assert.Empty(first.ClaimConflicts);
        Assert.Equal(new KeyValuePair<long, string>(111, "s-00000001"), Assert.Single(second.ClaimConflicts));
        Assert.Single(host.Reported);
        Assert.Equal(new[] { "111" }, book.Lines[0].Accounts.Keys.ToArray());
        Assert.Empty(book.Lines[1].Accounts);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task AnotherSourceCanKeepAndSendAnAccountWhenItsClaimExpires(int boundaryOffsetSeconds, bool expired)
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var claims = new AccountClaims(time);
        var host = new StubHost(true, AltAccount);
        var firstBook = new MemoryBook();
        var secondBook = new MemoryBook();
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200)));

        await Watch(engine, host, firstBook, SourceOf(SourceRole.Main, "s-00000001"), claims: claims, time: time)
            .RunOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "111" }, Assert.Single(firstBook.Lines).Accounts.Keys);
        Assert.Single(host.Reported);

        time.Advance(TimeSpan.FromSeconds(Clan.EffectiveEverySeconds * 2 + boundaryOffsetSeconds));
        var second = await Watch(engine, host, secondBook, SourceOf(SourceRole.Mine, "s-00000002"), claims: claims, time: time)
            .RunOnceAsync(CancellationToken.None);

        if (expired) Assert.Empty(second.ClaimConflicts);
        else Assert.Equal("s-00000001", second.ClaimConflicts[111]);
        var secondLine = Assert.Single(secondBook.Lines);
        Assert.Equal(expired ? new[] { "111" } : Array.Empty<string>(), secondLine.Accounts.Keys);
        Assert.Equal(expired ? 2 : 1, host.Reported.Count);

        if (expired)
        {
            var returningBook = new MemoryBook();
            await Watch(engine, host, returningBook, SourceOf(SourceRole.Main, "s-00000001"), claims: claims, time: time)
                .RunOnceAsync(CancellationToken.None);

            Assert.Empty(Assert.Single(returningBook.Lines).Accounts);
            Assert.Equal(2, host.Reported.Count);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictsAreOwnOnlyPerReadAndDoNotNeedAScoreBook(bool attachBook)
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var claims = new AccountClaims(time);
        var window = TimeSpan.FromSeconds(Clan.EffectiveEverySeconds * 2);
        Assert.True(claims.TryClaim(Clan.Slug, 111, "owner", window));
        Assert.True(claims.TryClaim(Clan.Slug, 222, "foreign-owner", window));
        var host = new StubHost(true, AltAccount);
        var book = new MemoryBook();
        var reading = Reading(EngineRow(111, 4200), EngineRow(222, 10));
        var watch = new RecipeWatch(new StubEngine(() => reading), host, new NoKeys(),
            new ReportPolicy([Points], new HashSet<Guid> { Alt }), Clan, Inputs, new HashSet<string> { "value" },
            book: attachBook ? book : null, source: SourceOf(SourceRole.Mine, "reader"), recipeText: ClanText, claims: claims, time: time);

        var blocked = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(new KeyValuePair<long, string>(111, "owner"), Assert.Single(blocked.ClaimConflicts));
        Assert.Contains("already claimed by another source", blocked.Detail);
        Assert.Empty(host.Reported);

        time.Advance(window);
        var transferred = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Empty(transferred.ClaimConflicts);
        Assert.Single(host.Reported);
        Assert.Equal("owner", blocked.ClaimConflicts[111]);

        reading = RecipeReading.Stop(ReadingOutcome.Idle, "No battle running");
        Assert.Empty((await watch.RunOnceAsync(CancellationToken.None)).ClaimConflicts);
        if (!attachBook) Assert.Empty(book.Lines);
    }

    /// <summary>
    /// Backlog S1-6.8. Your account WAS in this read: another source of the recipe read it first and keeps it (ruling R6).
    /// "None of your accounts were in this read." named a cause that wasn't the cause. A read that really had none still says so.
    /// </summary>
    [Fact]
    public async Task AReadWhoseAccountsAnotherSourceReadFirstSaysSoRatherThanThatNoneWereThere()
    {
        var claims = new AccountClaims(TimeProvider.System);
        var host = new StubHost(true, AltAccount);
        // No headline: with nothing of its own to keep, a read whose account was claimed elsewhere keeps no line at all.
        static StubEngine Reads(params RecipeRow[] rows) => new(() =>
            new RecipeReading(ReadingOutcome.Read, null, rows, [], "battle=B", rows.Length) { Period = new ReadingPeriod("B", null, null) });

        var first = await Watch(Reads(EngineRow(111, 4200)), host, new MemoryBook(), SourceOf(SourceRole.Main, "s-00000001"), claims: claims)
            .RunOnceAsync(CancellationToken.None);
        var second = await Watch(Reads(EngineRow(111, 4200)), host, new MemoryBook(), SourceOf(SourceRole.Mine, "s-00000002"), claims: claims)
            .RunOnceAsync(CancellationToken.None);
        var nobody = await Watch(Reads(EngineRow(222, 10)), host, new MemoryBook(), SourceOf(SourceRole.Mine, "s-00000003"), claims: claims)
            .RunOnceAsync(CancellationToken.None);

        Assert.True(first.Recorded);
        Assert.False(second.Recorded);
        Assert.Equal("Your accounts in this read are kept by another source of this recipe, which read them first.", second.NotRecordingReason);
        Assert.False(nobody.Recorded);
        Assert.Equal("None of your accounts were in this read.", nobody.NotRecordingReason);
    }

    [Theory]
    [InlineData(SourceRole.Watch)]
    [InlineData(SourceRole.Mine)]
    [InlineData(SourceRole.Main)]
    public async Task AGroupListKeepsTheFieldsNumbersAndNoClansName(SourceRole role)
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var book = new MemoryBook();
        var reading = new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 2)
        {
            Groups = [new GroupRow("Aurelian", new Dictionary<string, double> { ["value"] = 1 }, 1), new GroupRow("SkyHarbor", new Dictionary<string, double> { ["value"] = 2 }, 2)],
        };
        var engine = new StubEngine(() => reading);
        var host = new StubHost(true, AltAccount);

        var snapshot = await Watch(engine, host, book,
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), role), recipe: recipe, text: text)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, engine.Calls);
        Assert.Empty(engine.LastIds);
        Assert.Empty(host.Reported);
        Assert.True(snapshot.Recorded);
        Assert.Null(snapshot.NotRecordingReason);
        Assert.Equal(WatchState.Showing, snapshot.State);
        Assert.Equal(2, snapshot.Groups.Count);

        // One line: the field's shape, the clans a chart can draw (owner's ruling, 2026-09-20), and no account.
        var line = Assert.Single(book.Lines);
        Assert.Equal(
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["field-leader"] = 2, ["field-top10"] = 1.5, ["field-avg"] = 1.5, ["field-bottom10"] = 1.5, ["field-clans"] = 2,
            },
            line.Headline);
        Assert.Empty(line.Accounts);
        Assert.Empty(line.Stats);
        Assert.Null(line.Unavail);
        Assert.Equal(new Dictionary<string, double>(StringComparer.Ordinal) { ["SkyHarbor"] = 2, ["Aurelian"] = 1 }, line.Groups);

        // Clans by name, never a player: a group list matches no account, so none can ride along.
        var written = BookJson.Serialize(line);
        Assert.Contains("SkyHarbor", written, StringComparison.Ordinal);
        Assert.DoesNotContain("UserID", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A clans list has no account to send for and never will — and that is exactly why these may go: they are
    /// about the position your clan holds, not about anyone's account, so they carry no subject at all. The
    /// owner asked for them on 2026-09-20 so a clan can be told one name to set an alert on.
    /// </summary>
    [Fact]
    public async Task AWatchedClansListStillSendsTheClanNumbersYouTicked()
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var host = new StubHost(true, AltAccount);
        var clock = new ManualTime(new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero));
        var ends = clock.Now.AddHours(10);

        double mine = 8_000_000_000, above = 8_900_000_000;
        var engine = new StubEngine(() => new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 3)
        {
            Period = new ReadingPeriod("B", null, ends),
            Groups =
            [
                new GroupRow("UN0", new Dictionary<string, double> { ["value"] = 21_000_000_000 }, 1),
                new GroupRow("V1LN", new Dictionary<string, double> { ["value"] = above }, 2),
                new GroupRow("K0i2", new Dictionary<string, double> { ["value"] = mine, ["members"] = 74, ["capacity"] = 75 }, 3),
            ],
        });

        var watch = Watch(engine, host, new MemoryBook(),
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch),
            time: clock, recipe: recipe, text: text,
            field: FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.PaceNeeded, FieldMetrics.FreeSlots]),
            myGroups: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "K0i2" });

        // First read: no pace yet, so what it would take is not guessed at.
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(["clan.standing.points", "clan.standing.free-slots"], host.Reported.Select(r => r.MetricId));
        Assert.All(host.Reported, r => Assert.Equal(Guid.Empty, r.Subject));

        // Half an hour on, both clans have moved, and now there is a pace for each of them.
        host.Reported.Clear();
        clock.Advance(TimeSpan.FromMinutes(30));
        mine += 130_000_000;
        above += 100_000_000;

        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(
            ["clan.standing.points", "clan.standing.pace-needed", "clan.standing.free-slots"],
            host.Reported.Select(r => r.MetricId));
        Assert.All(host.Reported, r => Assert.Equal(Guid.Empty, r.Subject));

        // 870M behind with 9.5 hours left, while they make 200M an hour: about 291.6M an hour.
        var needed = host.Reported.Single(r => r.MetricId == "clan.standing.pace-needed").Value;
        Assert.Equal(200_000_000 + (870_000_000 / 9.5), needed, 1);
        Assert.Contains("Sent 3 clan number(s)", snapshot.Detail, StringComparison.Ordinal);
    }

    /// <summary>Ticking nothing is the default, and it is silence: the read is still kept.</summary>
    [Fact]
    public async Task AClansListWithNoClanNumbersTickedSendsNothing()
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var host = new StubHost(true, AltAccount);
        var book = new MemoryBook();
        var engine = new StubEngine(() => new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 1)
        {
            Groups = [new GroupRow("K0i2", new Dictionary<string, double> { ["value"] = 8e9 }, 1)],
        });

        var snapshot = await Watch(engine, host, book,
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch),
            recipe: recipe, text: text, myGroups: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "K0i2" })
            .RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
        Assert.True(snapshot.Recorded);
        Assert.Single(book.Lines);
    }

    /// <summary>
    /// The place above is a position, not a clan. Gain a place and its points fall, because somebody else holds it
    /// now — so the series starts again rather than averaging two clans into one pace.
    /// </summary>
    [Fact]
    public async Task GainingAPlaceStartsThePlaceAbovesHistoryAgain()
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var host = new StubHost(true, AltAccount);
        var clock = new ManualTime(new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero));
        var ends = clock.Now.AddHours(10);

        // Third of three throughout, until the last read, where the two above us have both been overtaken by
        // other clans and the place above us is held by one with fewer points than the last holder.
        var mine = 8_000_000_000d;
        double first = 21e9, second = 8.9e9;
        var engine = new StubEngine(() => new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 3)
        {
            Period = new ReadingPeriod("B", null, ends),
            Groups =
            [
                new GroupRow("A", new Dictionary<string, double> { ["value"] = first }, 1),
                new GroupRow("B", new Dictionary<string, double> { ["value"] = second }, 2),
                new GroupRow("K0i2", new Dictionary<string, double> { ["value"] = mine }, 3),
            ],
        });

        var watch = Watch(engine, host, new MemoryBook(),
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch),
            time: clock, recipe: recipe, text: text,
            field: FieldMetrics.Offered([FieldMetrics.PaceNeeded]),
            myGroups: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "K0i2" });

        await watch.RunOnceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(30));
        mine += 130_000_000;
        second += 100_000_000;
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Single(host.Reported);

        // We take a place: the position above us is now held by a clan with fewer points than the last one.
        host.Reported.Clear();
        clock.Advance(TimeSpan.FromMinutes(30));
        mine += 130_000_000;
        first = 8.9e9;
        second = 8.5e9;
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);

        // And it really is a restart, not a one-read gap: review on 2026-09-20 deleted series.Clear() and this
        // test still passed, because the read after a fall is refused by Pace either way. Half an hour on there
        // are two readings of the NEW holder and a pace again -- with the old holder's history still in the
        // series there would be a fall inside the window and no pace at all.
        clock.Advance(TimeSpan.FromMinutes(30));
        mine += 130_000_000;
        second = 8.6e9;
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Single(host.Reported);
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
