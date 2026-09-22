using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The numbers the window shows before its first read. These pin what a remembered snapshot may carry (your own
/// accounts and the source's own headline, nothing else), what makes one refuse to be built, and that every one is
/// stamped — the stamp is the only thing downstream has to tell it from a read.
/// </summary>
public class RememberedTests
{
    private static readonly IReadOnlySet<long> Yours = LiveBoard.UserIdsOf(Accounts);

    private static BookLine Kept(DateTimeOffset at, params (long UserId, double Value)[] rows) =>
        Read(MainClan, at, "AutumnBattle", new Dictionary<string, double> { ["clan-points"] = 14_020_550 }, "value", rows);

    /// <summary>
    /// A kept reading carrying what the book keeps beside each value: the account's place among the rows the source read,
    /// how many rows that was (<c>of</c>), and how many of them had the stat the place was counted among (<c>ranked</c>, null
    /// for a line written before the book kept it). A null place is an account the reading kept no place for.
    /// </summary>
    private static BookLine Ranked(DateTimeOffset at, int of, int? ranked, params (long UserId, double Value, int? Rank)[] rows) =>
        Kept(at, [.. rows.Select(r => (r.UserId, r.Value))]) with
        {
            Accounts = rows.ToDictionary(
                r => r.UserId.ToString(CultureInfo.InvariantCulture),
                r => new BookAccount(
                    new Dictionary<string, double> { ["value"] = r.Value },
                    r.Rank is { } rank ? new Dictionary<string, int> { ["value"] = rank } : null,
                    of,
                    Ranked: r.Rank is not null && ranked is { } field ? new Dictionary<string, int> { ["value"] = field } : null),
                StringComparer.Ordinal),
        };

    [Fact]
    public void TheLastKeptReadingComesBackStampedWithItsOwnTime()
    {
        var snapshot = Remembered.From(Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours);

        Assert.NotNull(snapshot);
        Assert.Equal(Now.AddHours(-3), snapshot.RememberedAt);
        Assert.Equal(MainClan.Id, snapshot.SourceId);
        Assert.Equal("AutumnBattle", snapshot.Period?.Value);
        Assert.Equal(new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["value"] = 4200 }), Assert.Single(snapshot.Rows!));
        Assert.Equal(14_020_550, Assert.Single(snapshot.Headline!).Number);
    }

    [Fact]
    public void ItCarriesNothingTheBookNeverKept()
    {
        // The book's own privacy rules come along for free, and only for free if nothing here invents a field.
        var snapshot = Remembered.From(Kept(Now.AddMinutes(-20), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!;

        Assert.Empty(snapshot.Accounts);
        Assert.Empty(snapshot.Unresolved);
        Assert.Empty(snapshot.Groups);
        Assert.Empty(snapshot.Unavailable);
        Assert.Empty(snapshot.CellMisses);
        Assert.Empty(snapshot.CounterNames);
        Assert.Null(snapshot.Detail);
        Assert.False(snapshot.Recorded);
    }

    [Fact]
    public void AnAccountThatIsNoLongerYoursDoesNotComeBack()
    {
        var line = Kept(Now.AddHours(-1), (Main.RobloxUserId, 4200), (AltOne.RobloxUserId, 900));

        var snapshot = Remembered.From(line, MainClan, Clan, new HashSet<long> { Main.RobloxUserId })!;

        Assert.Equal([Main.RobloxUserId], snapshot.Rows!.Select(r => r.UserId).ToArray());
    }

    [Fact]
    public void AStatTheRecipeNoLongerOffersIsLeftOutAndAnEmptyRowWithIt()
    {
        var line = Read(MainClan, Now.AddHours(-1), "AutumnBattle", null, "gone-from-the-recipe", (Main.RobloxUserId, 7));

        Assert.Null(Remembered.From(line, MainClan, Clan, Yours));
    }

    [Fact]
    public void AReadingOfOtherInputsIsNotThisSourcesNumbers()
    {
        // The same recipe, another clan: its total and its rows are about something else entirely.
        var otherClan = SourceOf(MainClan.Id, Clan, "K0i2", SourceRole.Main);
        var line = Kept(Now.AddHours(-1), (Main.RobloxUserId, 4200));

        Assert.Null(Remembered.From(line, otherClan, Clan, Yours));
        Assert.Null(Remembered.From(line, SourceOf(MainClan.Id, Profile, null, SourceRole.Mine), Profile, Yours));
    }

    [Fact]
    public void AFinalIsNotAReading()
    {
        var final = Final(MainClan, Now.AddDays(-9), "SpringBattle", new Dictionary<string, double> { ["clan-points"] = 1 }, "value", (Main.RobloxUserId, 4200));

        Assert.Null(Remembered.From(final, MainClan, Clan, Yours));
    }

    [Fact]
    public void EverySourceWithSomethingKeptGetsOneAndASwitchedOffSourceGetsNone()
    {
        var off = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine) with { Enabled = false };
        var reader = Reader(
            Kept(Now.AddHours(-5), (Main.RobloxUserId, 4000)),
            Kept(Now.AddHours(-2), (Main.RobloxUserId, 4200)),
            Read(off, Now.AddHours(-1), "AutumnBattle", null, "value", (AltOne.RobloxUserId, 900)));

        var map = Remembered.ForSources(reader, [MainClan, off], [Installed(Clan, "value")], Yours);

        Assert.Equal([MainClan.Id], map.Keys.ToArray());
        Assert.Equal(Now.AddHours(-2), map[MainClan.Id].RememberedAt);
    }

    [Fact]
    public void ThePlaceTheReadingItselfWorkedOutComesBackWithTheRowsAndNothingIsWorkedOutHere()
    {
        // Fifty rows read, 49 of them with points: the place was counted among the 49 (backlog S1-6.9).
        var kept = Remembered.From(Ranked(Now.AddHours(-3), of: 50, ranked: 49, (Main.RobloxUserId, 4200, 7), (AltOne.RobloxUserId, 900, null)), MainClan, Clan, Yours)!;

        Assert.Equal(new RankInGroup(7, 49), kept.RememberedRanks[(Main.RobloxUserId, "value")]);

        // A line kept before the book knew that field still has a true place. Its "of" counted rows with no points too, so it
        // is not the field and isn't passed off as one: the place comes back with no count.
        var older = Remembered.From(Ranked(Now.AddHours(-3), of: 50, ranked: null, (Main.RobloxUserId, 4200, 7)), MainClan, Clan, Yours)!;
        Assert.Equal(new RankInGroup(7, null), older.RememberedRanks[(Main.RobloxUserId, "value")]);

        // An account the reading kept no place for gets none invented for it, and neither does a stat.
        Assert.DoesNotContain((AltOne.RobloxUserId, "value"), kept.RememberedRanks.Keys);
        Assert.Empty(Remembered.From(Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!.RememberedRanks);
    }

    /// <summary>
    /// Review C1. A remembered snapshot's rows are YOUR accounts alone, so a place worked out from them reads
    /// "#1 of 2" of a clan this never counted — a wrong number, freshly computed, beside a mark that only says the
    /// numbers are old. These two surfaces show the place the reading itself recorded, or nothing. This test fails
    /// the moment either of them derives one again.
    /// </summary>
    [Fact]
    public void NeitherMyAccountsNorTheAccountCardEverWorksOutAPlaceFromARememberedSnapshotsOwnRows()
    {
        // Two of your accounts, kept out of the fifty rows the reading saw: Main placed 7th, AltOne's place was never kept.
        var kept = Remembered.From(Ranked(Now.AddHours(-3), of: 50, ranked: 50, (Main.RobloxUserId, 4200, 7), (AltOne.RobloxUserId, 900, null)), MainClan, Clan, Yours)!;
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });
        var settings = new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value");
        var group = $"In {RecipeWords.Group(Clan)}";

        var mine = PanelModels.MyAccounts(live, Reader(), settings).Groups.SelectMany(g => g.Rows).ToList();
        var card = PanelModels.AccountCard(live, Reader(), settings with { UserId = Main.RobloxUserId });
        var other = PanelModels.AccountCard(live, Reader(), settings with { UserId = AltOne.RobloxUserId });

        Assert.Equal("#7 of 50", mine.Single(r => r.UserId == Main.RobloxUserId).InGroup);
        Assert.Equal("#7 of 50", card.Facts.Single(f => f.Label == group).Value);
        Assert.Equal(StatText.Dash, mine.Single(r => r.UserId == AltOne.RobloxUserId).InGroup);
        Assert.Equal(StatText.Dash, other.Facts.Single(f => f.Label == group).Value);

        // Two rows are in hand, so anything worked out from them ends "of 2". Nothing either panel draws may.
        Assert.DoesNotContain(
            mine.Select(r => r.InGroup).Concat(card.Facts.Concat(other.Facts).Select(f => f.Value)),
            text => text.EndsWith(" of 2", StringComparison.Ordinal));
    }

    /// <summary>
    /// Backlog S1-6.9, before the first read. The book used to hand back its row count as the field, so the board opened on
    /// "#7 of 50" and the first read, counting only members with points, said "#7 of 49" of the same clan. The kept field is
    /// shown when the line has it; a line from before it shows the place alone, because the row count isn't the field.
    /// </summary>
    [Fact]
    public void ARememberedPlaceCountsTheSameFieldTheLiveReadDoesOrNoneAtAll()
    {
        var settings = new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value");
        (string Accounts, string Card) Shown(BookLine line)
        {
            var kept = Remembered.From(line, MainClan, Clan, Yours)!;
            var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
                remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });
            return (
                PanelModels.MyAccounts(live, Reader(), settings).Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId).InGroup,
                PanelModels.AccountCard(live, Reader(), settings with { UserId = Main.RobloxUserId }).Facts.Single(f => f.Label == "In clan").Value);
        }

        Assert.Equal(("#7 of 49", "#7 of 49"), Shown(Ranked(Now.AddHours(-3), of: 50, ranked: 49, (Main.RobloxUserId, 4200, 7))));
        Assert.Equal(("#7", "#7"), Shown(Ranked(Now.AddHours(-3), of: 50, ranked: null, (Main.RobloxUserId, 4200, 7))));

        // And the live read of those same fifty rows, one with no points, says the same as the kept field.
        IReadOnlyList<RecipeRow> rows = [Row(Main.RobloxUserId, 4200), .. Enumerable.Range(1, 48).Select(i => Row(9_000 + i, i < 7 ? 10_000 + i : 1)), Row(9_999, null)];
        var read = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, rows) });
        Assert.Equal("#7 of 49", PanelModels.MyAccounts(read, Reader(), settings).Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId).InGroup);
    }

    /// <summary>
    /// Review round 2. The owner opens Ur Score before a battle on last night's numbers, and the first read of his
    /// main clan comes back with nothing — which is routine, not hypothetical. A read that failed has REPLACED
    /// nothing, so it must not take the numbers with it: going blank there leaves him worse off than before the
    /// window remembered anything. The fault is still the state line's to name, and it still can.
    /// </summary>
    [Fact]
    public void AReadThatFailedNeverClearsTheNumbersItDidNotReplace()
    {
        var kept = Remembered.From(Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!;
        var remembered = new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept };
        var failed = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id };
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = failed }, running: true, remembered: remembered);
        var settings = new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value");

        // The numbers stay, and they stay marked: a failed read is not a quieter way of saying "nothing here".
        Assert.Equal(Now.AddHours(-3), live.SnapshotOf(MainClan.Id)?.RememberedAt);
        Assert.True(live.IsRemembered(MainClan.Id));
        Assert.Equal(Now.AddHours(-3), live.OldestRemembered);

        var table = PanelModels.AccountsTable(live, Reader(), settings);
        Assert.True(table.Head.Remembered);
        Assert.Equal("4,200", table.Rows.Single(r => r.UserId == Main.RobloxUserId).Cells[1]);
        Assert.True(PanelModels.Standing(live, Reader(), settings).Head.Remembered);

        // And the failure is untouched for whoever reports what Ur Score is doing.
        Assert.Equal(WatchState.SourceUnreachable, live.LiveOf(MainClan.Id)?.State);

        // Only a reading that came back clears them, and then the mark goes with the numbers it marked.
        var read = live with { Snapshots = new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, [Row(Main.RobloxUserId, 9_001)]) } };
        Assert.False(read.IsRemembered(MainClan.Id));
        Assert.Null(read.OldestRemembered);
        Assert.Equal("9,001", PanelModels.AccountsTable(read, Reader(), settings).Rows.Single(r => r.UserId == Main.RobloxUserId).Cells[1]);
    }

    /// <summary>
    /// Backlog S1-F.6, checked rather than assumed: "a read that stops (no battle, an error) blanks the panels until the next good
    /// read". An idle read is built with a state and a reason and nothing else, exactly like a failed one, so since 7ee4afd it
    /// replaces nothing either: the panels go on drawing what the book kept from this session's last good read three minutes ago,
    /// marked, and the running line says how old they are. Only the live-only panels stay empty (PanelModelsTests
    /// ALivePanelWhoseLastReadBroughtNothingBackSaysSo).
    /// </summary>
    [Fact]
    public void AReadThatStopsForNoBattleKeepsTheNumbersTheLastGoodReadKept()
    {
        var kept = Remembered.From(Kept(Now.AddMinutes(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!;
        var idle = new RecipeSnapshot(WatchState.SourceIdle, "No clan battle running", [], [], 0) { SourceId = MainClan.Id, RecipeSlug = Clan.Slug };
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = idle }, running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now },
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });
        var settings = new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value");

        var standing = PanelModels.Standing(live, Reader(), settings);
        Assert.True(standing.Head.Remembered);
        Assert.NotEqual(StatText.Dash, standing.Total);
        Assert.Equal("4,200", PanelModels.AccountsTable(live, Reader(), settings).Rows.Single(r => r.UserId == Main.RobloxUserId).Cells[1]);
        Assert.Equal("Reading 1 source. The numbers on screen are the last ones Ur Score read, from 3m ago.",
            Labs626.UrScore.UI.BoardText.StateLine(live, everStarted: true));
    }

    /// <summary>
    /// Review round 3. <c>LastRead</c> stamps every ATTEMPT, so once a failed read stopped clearing the remembered
    /// numbers, the race chart began plotting an hours-old total at the current minute — a flat line drawn out to
    /// "now" for a reading that never happened. On a chart the line to "now" is the claim itself, which makes this the
    /// fabricated rank again in another costume. A point is plotted only for a reading that came back.
    /// </summary>
    [Fact]
    public void TheRaceChartPlotsNoPointForAReadThatBroughtNothingBack()
    {
        var line = Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200));
        var kept = Remembered.From(line, MainClan, Clan, Yours)!;
        var failed = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id };
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = failed },
            running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now },
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        var race = PanelModels.Race(live, Reader(line), new PanelSettings(Clan.Slug, SourceIds: [MainClan.Id]));

        // The one point the book actually holds, at the time it was actually read. Nothing at "now".
        var point = Assert.Single(Assert.Single(race.Series).Points);
        Assert.Equal((Now.AddHours(-3), 14_020_550d), (point.T, point.Value));
        Assert.True(race.Head.Remembered);
    }

    /// <summary>
    /// The same inheritance, found on the other surface that keys off <c>LastRead</c>: a remembered card falling back
    /// to the attempt stamp would answer "Last read 0m ago" beside numbers read hours before.
    /// </summary>
    [Fact]
    public void ARememberedCardNeverCallsAnAttemptStampItsLastRead()
    {
        var kept = Remembered.From(Kept(Now.AddHours(-3), (Main.RobloxUserId, 4200)), MainClan, Clan, Yours)!;
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = MainClan.Id } },
            running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now },
            remembered: new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = kept });

        // An empty book, so the card has no series of its own to date itself by and must fall back.
        var card = PanelModels.AccountCard(live, Reader(), new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value", UserId: Main.RobloxUserId));

        Assert.Equal("3h ago", card.Facts.Single(f => f.Label == "Last read").Value);
    }

    [Fact]
    public void AReadingOfThisSessionStillCountsItsOwnRows()
    {
        // The other half of the door: a live snapshot carries every row it read, so it goes on counting for itself.
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, [Row(5, 9_000), Row(Main.RobloxUserId, 4_200), Row(6, 100)]) });
        var settings = new PanelSettings(Clan.Slug, MainClan.Id, Stat: "value");

        var mine = PanelModels.MyAccounts(live, Reader(), settings).Groups.SelectMany(g => g.Rows).ToList();

        Assert.Equal("#2 of 3", mine.Single(r => r.UserId == Main.RobloxUserId).InGroup);
    }
}
