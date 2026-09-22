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
        IReadOnlyList<FieldMetric>? field = null, IReadOnlyList<string>? myGroups = null,
        Func<FieldMetric, string, bool>? writeLabel = null) =>
        new(engine, host, new NoKeys(), new ReportPolicy([Points], new HashSet<Guid> { Alt }, field), recipe ?? Clan, Inputs,
            tracked ?? new HashSet<string> { "value" },
            book, source, shared, text ?? ClanText, claims, finals, time,
            myGroups is null ? null : () => myGroups, writeLabel);

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
            myGroups: ["K0i2"]);

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
            recipe: recipe, text: text, myGroups: ["K0i2"])
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
            myGroups: ["K0i2"]);

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

    /// <summary>
    /// A battle with one clan behind us, and the two effects whose ORDER is the whole invariant recorded in one
    /// list as they happen: <c>label:{text}</c> when Ur Score writes a managed rule's label, and
    /// <c>report:{metric id}</c> when a number reaches RoRoRo. Asserting only that each happened would pass under
    /// report-then-rename, which is exactly the bug: the host captures a rule — label included — at the
    /// observation and words the push from that captured object up to five seconds later (design §1), so a number
    /// reported before its label is written ships under the PREVIOUS chaser's name.
    /// </summary>
    private sealed class ChaseFixture
    {
        private readonly RecipeWatch _watch;

        public ChaseFixture(
            string chaser, Func<FieldMetric, string, bool>? writeLabel, IReadOnlyList<FieldMetric> field,
            IReadOnlyList<string>? mine = null, string tag = "", List<string>? order = null)
        {
            Order = order ?? [];
            var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
            var recipe = RecipeParser.Parse(text).Recipe!;
            Host = new StubHost(true, AltAccount) { OnReport = metricId => Record(tag + "report:" + metricId) };

            var engine = new StubEngine(() => new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", ChaserIsInTheList ? 3 : 2)
            {
                Period = new ReadingPeriod("B", null, Ends),
                Groups = ChaserIsInTheList
                    ? [Group("UN0", 21e9, 1), Group("K0i2", Mine, 2), Group(chaser, Theirs, 3)]
                    : [Group("UN0", 21e9, 1), Group("K0i2", Mine, 2)],
            });

            _watch = Watch(engine, Host, new MemoryBook(),
                new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch),
                time: Clock, recipe: recipe, text: text, field: field,
                myGroups: mine ?? ["K0i2"],
                // Wrapped rather than passed straight through, so the label write lands in the same list as the
                // report and the two can be compared by position. The test's own allow-or-refuse is untouched.
                writeLabel: writeLabel is null ? null : (metric, label) =>
                {
                    Record(tag + "label:" + label);
                    return writeLabel(metric, label);
                });
        }

        /// <summary>
        /// Shared with a second fixture when two watches are being run against each other, so the order below is
        /// the order the two watches really interleaved in. Locked for that case; a single fixture never contends.
        /// </summary>
        public List<string> Order { get; }

        public StubHost Host { get; }

        /// <summary>What the last read said, for the tests that care what the user is shown rather than what went out.</summary>
        public RecipeSnapshot? Last { get; set; }

        public ManualTime Clock { get; } = new(new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero));

        /// <summary>Ten hours after the first read, so a crossing inside the fixture is never cut off by the clock.</summary>
        public DateTimeOffset Ends { get; } = new(2026, 9, 21, 4, 0, 0, TimeSpan.Zero);

        public double Mine { get; private set; } = 8_000_000_000;

        public double Theirs { get; private set; } = 7_500_000_000;

        /// <summary>Whether the chaser is in this read at all: a clan can drop out of the band between reads.</summary>
        public bool ChaserIsInTheList { get; set; } = true;

        public IEnumerable<string> ReportedIds => Host.Reported.Select(r => r.MetricId);

        public IEnumerable<string> Labels => Order.Where(e => e.StartsWith("label:", StringComparison.Ordinal));

        public double ValueOf(string metricId) =>
            Host.Reported.Last(r => string.Equals(r.MetricId, metricId, StringComparison.Ordinal)).Value;

        /// <summary>The counters the Alerts page reads, so a suppression can be checked as a number and not only as a sentence.</summary>
        public ReportPolicy Policy => _watch.Policy;

        public Task<RecipeSnapshot> ReadAsync() => _watch.RunOnceAsync(CancellationToken.None);

        /// <summary>Half an hour on, with what each clan made in it, then another read.</summary>
        public Task<RecipeSnapshot> HalfAnHourOnAsync(double mineGain, double theirGain)
        {
            Clock.Advance(TimeSpan.FromMinutes(30));
            Mine += mineGain;
            Theirs += theirGain;
            return ReadAsync();
        }

        private static GroupRow Group(string name, double value, int rank) =>
            new(name, new Dictionary<string, double> { ["value"] = value }, rank);

        private void Record(string entry)
        {
            lock (Order) Order.Add(entry);
        }
    }

    /// <summary>
    /// Two reads half an hour apart, so both clans have a pace: K0i2 (ours) goes 8.00B to 8.05B, 100M an hour, and
    /// the chaser goes 7.50B to 7.80B, 600M an hour. By the second read it is 250M behind and closing at 500M an
    /// hour, so it takes our place in half an hour — well inside the 9.5 hours the battle has left, which is what
    /// keeps the end-of-battle cap out of these tests.
    /// </summary>
    private static async Task<ChaseFixture> BattleWithChaser(
        string chaser, Func<FieldMetric, string, bool>? writeLabel, IReadOnlyList<FieldMetric>? field = null,
        IReadOnlyList<string>? mine = null)
    {
        var chase = new ChaseFixture(chaser, writeLabel, field ?? FieldMetrics.Offered([FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]), mine);

        // One reading is no pace, so no threat is invented from it and no label is written for one either.
        await chase.ReadAsync();
        chase.Last = await chase.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 300_000_000);
        return chase;
    }

    /// <summary>
    /// THE ORDERING RULE (design §1, read out of the host's source rather than assumed). RoRoRo captures a rule —
    /// label included — at the observation and words the push from that captured object up to five seconds later,
    /// so a number reported before its label is written ships under the PREVIOUS chaser's name. The ORDER of the
    /// two effects is what is asserted, not the fact of each: a pair of "did it happen" assertions passes under
    /// exactly the bug this test exists to catch.
    /// </summary>
    [Fact]
    public async Task TheLabelIsWrittenBeforeEachThreatNumberIsReported()
    {
        var chase = await BattleWithChaser("H8ER", (_, _) => true);

        Assert.Equal(
            [
                "label:H8ER catching K0i2", "report:clan.standing.threat-gap",
                "label:H8ER catching K0i2", "report:clan.standing.threat-hours",
            ],
            chase.Order);
    }

    /// <summary>Each threat number carries its own half of the same threat, so a swap of the two would show.</summary>
    [Fact]
    public async Task TheTwoThreatNumbersCarryTheGapAndTheHoursTheRightWayRound()
    {
        var chase = await BattleWithChaser("H8ER", (_, _) => true);

        // 8.05B against 7.80B is 250M behind, and 500M an hour of closing eats that in half an hour.
        Assert.Equal(250_000_000d, chase.ValueOf("clan.standing.threat-gap"), 1);
        Assert.Equal(0.5, chase.ValueOf("clan.standing.threat-hours"), 6);
    }

    /// <summary>
    /// With several clans of your own the label names THE FIRST of them, in the order
    /// <see cref="SourceRules.MyClanNames"/> returns — the same first <c>AlertsPage.Clan()</c> names on the Alerts
    /// page, so a phone and the page agree. The order is the whole point: the watch used to be handed a
    /// <c>HashSet</c> and take its <c>FirstOrDefault</c>, which agreed with the page by coincidence of runtime
    /// behaviour rather than by contract (final review of this branch, 2026-09-20).
    /// <para>
    /// ZZZZ is first here and is NOT the clan in the read — K0i2 is, and the numbers are measured from K0i2. So
    /// this also pins the known limitation that comes with the guess: the label may name a different clan of
    /// yours than the numbers are about. The THREAT's name, which is what the label exists to carry, is right
    /// either way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheLabelNamesTheFirstOfYourClansInTheOrderTheyAreSetUp()
    {
        var chase = await BattleWithChaser("H8ER", (_, _) => true, mine: ["ZZZZ", "K0i2"]);

        Assert.Equal(["label:H8ER catching ZZZZ", "label:H8ER catching ZZZZ"], chase.Labels);
        Assert.Equal(250_000_000d, chase.ValueOf("clan.standing.threat-gap"), 1);
    }

    /// <summary>
    /// NO LABEL, NO SEND — the other half of the ordering rule. <see cref="RulesFile.ChangeLabel"/> answers
    /// CantWrite rather than guess when a row it cannot edit safely would leave the wrong name on the rule; that
    /// refusal is only worth something if the call site honours it. A threat number under a stale name is a
    /// confident false statement on a phone mid-battle, which is worse than silence (owner's ruling, 2026-09-20).
    /// </summary>
    [Fact]
    public async Task AThreatNumberWhoseLabelCannotBeWrittenIsNotReportedAtAll()
    {
        var chase = await BattleWithChaser("H8ER", (_, _) => false,
            FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]));

        Assert.DoesNotContain(chase.ReportedIds, id => id.StartsWith("clan.standing.threat", StringComparison.Ordinal));

        // The write was tried and refused, not skipped — and the refusal is scoped to the numbers that carry a
        // name. A number nobody's name rides on is unaffected by a rules file that cannot be written.
        Assert.Equal(["label:H8ER catching K0i2", "label:H8ER catching K0i2"], chase.Labels);
        Assert.Contains("clan.standing.points", chase.ReportedIds);
    }

    /// <summary>
    /// SILENCE IS RIGHT; SILENCE WITH NO TRACE IS THE BUG. Refusing to send a threat number whose label could not
    /// be written is the owner's ruling and stays. What was wrong until 2026-09-21 is that the refusal was
    /// invisible: the send is skipped before <see cref="ReportPolicy"/> is ever called, so neither Sent nor
    /// Dropped moved, and the line the user reads said only what DID go. A rules file with a duplicate key means
    /// both threat numbers never fire again, forever, with nothing on screen saying why (V3-S.35).
    /// <para>
    /// Two counted, not one: each threat number writes its own label and is held on its own refusal.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AThreatNumberHeldBackForItsLabelIsCountedAndSaidOutLoud()
    {
        var chase = await BattleWithChaser("H8ER", (_, _) => false,
            FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]));

        Assert.Equal(2, chase.Policy.Held);
        Assert.Contains("held back", chase.Last!.Detail ?? "");

        // And the count is a count of the held, not of everything that did not go out: the number nobody's name
        // rides on went out fine, and a read with no threat at all holds nothing back.
        Assert.Contains("clan.standing.points", chase.ReportedIds);
    }

    /// <summary>
    /// The trace is for a refusal, not for quiet. A watch whose chaser has no readable pace yet sends no threat
    /// number and holds nothing back — a counter that also counted "there was nothing to say" would be noise on
    /// every first read of every battle, and a number worth looking at has to stay worth looking at.
    /// </summary>
    [Fact]
    public async Task NothingToSayIsNotSomethingHeldBack()
    {
        var chase = new ChaseFixture(
            "H8ER", (_, _) => false, FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.ThreatGap]));

        chase.Last = await chase.ReadAsync();

        Assert.Equal(0, chase.Policy.Held);
        Assert.DoesNotContain("held back", chase.Last.Detail ?? "");
    }

    /// <summary>
    /// A build with no label writer wired at all is the same refusal, for the same reason: the name cannot be got
    /// onto the rule, so the number does not go. Never a threat number under whatever name the rule happens to
    /// carry — that is the failure this whole feature exists to prevent.
    /// </summary>
    [Fact]
    public async Task WithNoLabelWriterAtAllTheThreatNumbersStayQuiet()
    {
        var chase = await BattleWithChaser("H8ER", null,
            FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]));

        Assert.DoesNotContain(chase.ReportedIds, id => id.StartsWith("clan.standing.threat", StringComparison.Ordinal));
        Assert.Empty(chase.Labels);
        Assert.Contains("clan.standing.points", chase.ReportedIds);
    }

    /// <summary>
    /// NotThere is not a failed write. With no Ur Score rule on the metric there is no label to keep current and
    /// no alert that can fire from it, so nothing can reach a phone under a stale name and the number goes as it
    /// always did. Driven through the real <see cref="RulesFile.ChangeLabel"/> rather than a stand-in, because
    /// what is being pinned is which of its answers mean "in place" — the mapping the composition root has to
    /// make when it wires the rules path in.
    /// </summary>
    [Fact]
    public async Task NoRuleOfOursToRenameIsNotAFailedWriteAndTheNumberStillGoes()
    {
        // A path with no file behind it: ChangeLabel finds no rules, none of them ours, and writes nothing at all.
        var path = Path.Combine(Path.GetTempPath(), $"ur-score-no-rules-{Guid.NewGuid():n}.json");

        var chase = await BattleWithChaser("H8ER", (metric, label) =>
            RulesFile.ChangeLabel(path, metric.MetricId, AlertKind.Level, label) is RuleWrite.Done or RuleWrite.NotThere);

        Assert.Contains("clan.standing.threat-hours", chase.ReportedIds);
        Assert.False(File.Exists(path), "NotThere means there was nothing to rewrite, so nothing should have been written.");
    }

    /// <summary>
    /// The six older clan numbers are shipped and correct, and nothing here may change them. A clan behind us that
    /// is losing ground is no threat at all, so those numbers go exactly as before and no label is written, there
    /// being no name to write.
    /// </summary>
    [Fact]
    public async Task TheOlderClanNumbersStillSendWhenNoClanIsClosing()
    {
        var chase = new ChaseFixture("H8ER", (_, _) => true,
            FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.GapAbove, FieldMetrics.PaceNeeded, FieldMetrics.ThreatHours]));

        await chase.ReadAsync();

        // 20M an hour against our 100M: it is falling further behind, not closing.
        await chase.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 10_000_000);

        Assert.Equal(
            [
                "clan.standing.points", "clan.standing.gap-above",
                "clan.standing.points", "clan.standing.gap-above", "clan.standing.pace-needed",
            ],
            chase.ReportedIds);
        Assert.Empty(chase.Labels);
    }

    /// <summary>
    /// WRITE-THEN-REPORT IS NOT ATOMIC ACROSS WATCHES, and this is the fence that makes it so within one process.
    /// <para>
    /// <c>AppServices.CreateWatch</c> builds one watch per source and the threat metric ids are fixed rather than
    /// per source, so two enabled clans-list recipes rewrite the SAME rule row on their own timers. Watch B's
    /// label write landing between watch A's write and A's report is the whole failure this feature exists to
    /// prevent: the host words A's number under B's rival's name (design §1). Found by the final review of this
    /// branch, 2026-09-20.
    /// </para>
    /// <para>
    /// The interleave is FORCED rather than hoped for: A's own label write starts B's read and then waits for B
    /// to reach its label write. Without the gate that wait returns as soon as B writes, and B's write is in the
    /// list before A's report. With it, B is held out until A's pair is finished and the wait simply times out.
    /// The assertion is the ORDER — every report immediately after its own watch's label — not that each thing
    /// happened; and both watches are checked to have got all the way through, so a B that never ran cannot pass
    /// this by being absent.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OneWatchsLabelWriteCannotLandInsideAnothersWriteThenReport()
    {
        var order = new List<string>();
        var field = FieldMetrics.Offered([FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]);
        var secondWatchWrote = new TaskCompletionSource();
        Func<FieldMetric, string, bool> firstWatchWrites = (_, _) => true;

        var first = new ChaseFixture("H8ER", (m, l) => firstWatchWrites(m, l), field, tag: "A:", order: order);
        var second = new ChaseFixture("R0W", (_, _) => { secondWatchWrote.TrySetResult(); return true; }, field, tag: "B:", order: order);

        // One read each: no pace yet, so no threat, no label and no report from either.
        await first.ReadAsync();
        await second.ReadAsync();
        Assert.Empty(order);

        Task? secondRun = null;
        firstWatchWrites = (_, _) =>
        {
            if (secondRun is null)
            {
                secondRun = Task.Run(() => second.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 300_000_000));

                // Long enough for the other watch to be scheduled and reach its own label write many times over,
                // which is what makes the unguarded case fail rather than race. Guarded, this always times out.
                secondWatchWrote.Task.Wait(TimeSpan.FromSeconds(1));
            }

            return true;
        };

        await first.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 300_000_000);
        await secondRun!;

        List<string> happened;
        lock (order) happened = [.. order];

        // Both watches got all the way through, so neither passes this by having done nothing.
        Assert.Contains("A:report:clan.standing.threat-hours", happened);
        Assert.Contains("B:report:clan.standing.threat-hours", happened);
        Assert.Contains("A:label:H8ER catching K0i2", happened);
        Assert.Contains("B:label:R0W catching K0i2", happened);

        for (var i = 0; i < happened.Count; i++)
        {
            if (happened[i].Split(':') is not [var watch, "report", _]) continue;

            Assert.True(
                i > 0 && happened[i - 1].StartsWith(watch + ":label:", StringComparison.Ordinal),
                $"{happened[i]} was not reported immediately after its own watch's label write: {string.Join(" | ", happened)}");
        }
    }

    /// <summary>
    /// A chaser is tracked by NAME, and a name that leaves the band is dropped rather than left to pair up with a
    /// reading half an hour later as though nothing had happened (controller ruling 3, design §4). The gap here is
    /// well inside the two hours a current pace may reach back over, so it is the drop that has to silence the
    /// number: with the old readings still in the store, the one either side of the gap makes a pace and a clan
    /// that was out of the band comes back already a threat.
    /// </summary>
    [Fact]
    public async Task AChaserThatLeavesTheBandIsForgottenRatherThanResumed()
    {
        // Points rides along as the control: the last read still sends it, so the threat's absence is the threat's
        // and not a read that quietly stopped happening.
        var chase = await BattleWithChaser("H8ER", (_, _) => true,
            FieldMetrics.Offered([FieldMetrics.Points, FieldMetrics.ThreatGap, FieldMetrics.ThreatHours]));
        Assert.Contains("clan.standing.threat-hours", chase.ReportedIds);

        chase.ChaserIsInTheList = false;
        await chase.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 60_000_000);

        chase.ChaserIsInTheList = true;
        chase.Host.Reported.Clear();
        await chase.HalfAnHourOnAsync(mineGain: 50_000_000, theirGain: 60_000_000);

        Assert.Equal(["clan.standing.points"], chase.ReportedIds);
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

    /// <summary>
    /// A line is filed under the inputs the READ used, not the inputs its source carries. The two are separate
    /// fields updated by separate calls — <c>UpdateSource</c> swaps the source, <c>UpdateRecipe</c> swaps the
    /// inputs — and the composition root makes them one after the other, so a cycle starting between them reads
    /// with the old inputs and, until now, wrote the source's new ones on its line (S1-6.4). That is the state
    /// set up here: the source now names another clan and the inputs do not yet, so the read is of K0i2 and the
    /// line has to say so. (A source swapped MID-read does not reach the line at all — a cycle takes its source
    /// and inputs together at the top — which is why the first draft of this test passed before the fix.)
    /// </summary>
    [Fact]
    public async Task ALineIsFiledUnderTheInputsTheReadUsedNotTheOnesItsSourceCarries()
    {
        var book = new MemoryBook();
        var watch = Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine));

        watch.UpdateSource(SourceOf(SourceRole.Mine) with { Inputs = new Dictionary<string, string> { ["clan"] = "Other Clan" } });
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal("K0i2", Assert.Single(book.Lines).Inputs["clan"]);
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
