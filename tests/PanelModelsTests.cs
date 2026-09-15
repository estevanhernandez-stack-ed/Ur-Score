using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelModelsTests
{
    private const string Period = "AutumnBattle";

    private static readonly ReadingPeriod LivePeriod = new(Period, Now.AddDays(-2), Now.AddHours(76));

    private static Dictionary<string, double> Headline(double points) => new() { ["clan-points"] = points };

    private static Dictionary<string, RecipeSnapshot> Snaps(params RecipeSnapshot[] snapshots) =>
        snapshots.ToDictionary(s => s.SourceId, s => s, StringComparer.Ordinal);

    [Fact]
    public void YourUserIdsAreYourAccountsIdsLessAnyWithout()
    {
        var noId = new HostAccount(Guid.Parse("55555555-5555-5555-5555-555555555555"), 0, "NotSignedIn");

        var ids = LiveBoard.UserIdsOf([Main, noId, AltOne, Main]);

        Assert.Equal(new long[] { 101, 201 }, ids.OrderBy(id => id).ToArray());
        Assert.Equal(ids, Live([], [], Snaps(), accounts: [Main, noId, AltOne]).MyUserIds);
    }

    // ---- Standing ----

    [Fact]
    public void StandingShowsPlaceTotalChangeAndTheRecipesWords()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [Row(101, 14_020_550), Row(5, 1)], [Place(14), Points(30_214_400)], LivePeriod)));
        var reader = Reader(
            Read(main, Now.AddHours(-1), Period, Headline(28_800_000), "value"),
            Read(main, Now.AddMinutes(-3), Period, Headline(30_214_400), "value"));

        var model = PanelModels.Standing(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(new PanelHead("Clan standing", "CCGP", "★ main"), model.Head);
        Assert.Equal("14th", model.Place);
        Assert.Equal("in the battle", model.PlaceSuffix);
        Assert.Equal("Clan points", model.TotalLabel);
        Assert.Equal("30,214,400", model.Total);
        Assert.Equal(Records.Change(reader.HeadlineSeries(main.Id, "clan-points", Period), Now), model.Change);
        Assert.Equal("1 of 2", model.Accounts);
        Assert.Equal(PanelText.PeriodLine(LivePeriod, Now, null), model.PeriodLine);
        Assert.False(model.HasGap);
    }

    [Fact]
    public void TheGapShowsOnlyWhenTheGroupListHasThisAndTheOneJustAbove()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        GroupRow Group(string name, double points, int rank) => new(name, new Dictionary<string, double> { ["value"] = points }, rank);

        LiveBoard With(params GroupRow[] groups) => Live([main, top], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(main.Id, [], [Place(14), Points(30_200_000)]), Snapshot(top.Id, null, groups: groups)));

        var withAbove = PanelModels.Standing(With(Group("Aurelian", 40_000_000, 13), Group("ccgp", 30_200_000, 14)), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        var withGap = PanelModels.Standing(With(Group("Other", 40_000_000, 12), Group("CCGP", 30_200_000, 14)), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.True(withAbove.HasGap);
        Assert.Equal("To 13th", withAbove.GapLabel);
        Assert.Equal($"{StatText.Abbrev(9_800_000)} behind", withAbove.Gap);
        Assert.Equal(30.2 / 40, withAbove.GapFill, 3);
        Assert.False(withGap.HasGap);
    }

    [Fact]
    public void APanelWhoseSourceWasRemovedSaysSoInTheRecipesWord()
    {
        var model = PanelModels.Standing(Live([], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, SourceId: "s-gone0000"));

        Assert.Equal("This panel's clan was removed.", model.Head.Stale);
        Assert.False(model.Head.HasBody);
        Assert.True(model.Head.HasStale);
    }

    [Fact]
    public void OverdueFollowsRecordsOnlyWhileRunning()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var lastRead = new Dictionary<string, DateTimeOffset> { [main.Id] = Now.AddMinutes(-10) };
        var snaps = Snaps(Snapshot(main.Id, [], [Place(14), Points(1)]));

        var running = PanelModels.Standing(Live([main], [Installed(Clan, "value")], snaps, running: true, lastRead), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        var stopped = PanelModels.Standing(Live([main], [Installed(Clan, "value")], snaps, running: false, lastRead), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(Records.Overdue(Now.AddMinutes(-10), Clan.EffectiveEverySeconds, Now), running.Head.Overdue);
        Assert.False(stopped.Head.Overdue);
    }

    [Fact]
    public void StandingShowsNoChangeBeforeThePeriodIsKnown()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        // The snapshot has no period yet; the only book lines on hand belong to an earlier, unrelated battle.
        var snaps = Snaps(Snapshot(main.Id, [], [Place(14), Points(1)]));
        var reader = Reader(
            Read(main, Now.AddDays(-10), "SummerBattle", Headline(1_000_000), "value"),
            Read(main, Now.AddDays(-9), "SummerBattle", Headline(2_000_000), "value"));

        var model = PanelModels.Standing(Live([main], [Installed(Clan, "value")], snaps), reader, new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(StatText.Dash, model.Change);
    }

    // ---- Race ----

    [Fact]
    public void TheRaceDrawsEachSourcesHeadlineFromTheBookPlusTheLiveRead()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var live = Live([main, rival], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [], [Points(30_214_400)], LivePeriod), Snapshot(rival.Id, [], [Points(31_300_000)], LivePeriod)),
            lastRead: new Dictionary<string, DateTimeOffset> { [main.Id] = Now, [rival.Id] = Now });
        var reader = Reader(
            Read(main, Now.AddHours(-5), Period, Headline(20_000_000), "value"),
            Read(rival, Now.AddHours(-5), Period, Headline(21_000_000), "value"));

        var model = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [main.Id, rival.Id]));

        Assert.Equal("Battle race", model.Head.Title);
        Assert.Equal("clan points since the battle started", model.Head.Subtitle);
        Assert.Equal(new[] { 0, 1 }, model.Series.Select(s => s.Colour).ToArray());
        Assert.Equal(30_214_400, model.Series[0].Points[^1].Value);
        Assert.Equal(new[] { $"★ CCGP {StatText.Abbrev(30_214_400)}", $"NovaForge · watching {StatText.Abbrev(31_300_000)}" },
            model.Legend.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void TheRaceWaitsForTheFirstReadWhenNoSourceHasAPeriodYet()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        // Neither snapshot has a period yet; the only book lines on hand belong to an earlier, unrelated battle.
        var live = Live([main, rival], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [], [Points(30_214_400)]), Snapshot(rival.Id, [], [Points(31_300_000)])));
        var reader = Reader(
            Read(main, Now.AddDays(-10), "SummerBattle", Headline(20_000_000), "value"),
            Read(rival, Now.AddDays(-10), "SummerBattle", Headline(21_000_000), "value"));

        var model = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [main.Id, rival.Id]));

        Assert.Equal("Waiting for the first read.", model.Head.Note);
        Assert.Empty(model.Series);
        Assert.Empty(model.Legend);
    }

    // ---- My accounts ----

    [Fact]
    public void MyAccountsGroupsMainFirstThenMineThenAccountsInNoWatchedClan()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        IReadOnlyList<RecipeRow> ccgpRows = [Row(101, 14_020_550), Row(5, 20_000_000), Row(6, 1_000)];
        IReadOnlyList<RecipeRow> altRows = [Row(202, null), Row(201, 12_418_220), Row(101, 9_000_000), Row(7, 50)];
        var sentLine = new AccountLine("estehernandez", Main.AccountId, new Dictionary<string, double> { ["value"] = 14_020_550 }, Now);
        var live = Live([alts, main], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, ccgpRows, period: LivePeriod, sent: [sentLine]), Snapshot(alts.Id, altRows, period: LivePeriod)));
        var reader = Reader(
            Read(alts, Now.AddHours(-1), Period, null, "value", (201, 12_000_000)),
            Read(alts, Now.AddMinutes(-3), Period, null, "value", (201, 12_418_220)));

        var model = PanelModels.MyAccounts(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));

        Assert.Equal("by points", model.Head.Subtitle);
        Assert.Equal("In clan", model.GroupColumn);
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Not in a watched clan" }, model.Groups.Select(g => g.Heading).ToArray());

        var ccgp = Assert.Single(model.Groups[0].Rows);
        Assert.Equal("estehernandez", ccgp.Name);
        Assert.Equal($"#{Ranking.Competition(ccgpRows, "value")[101]} of 3", ccgp.InGroup);
        Assert.True(ccgp.Sent);

        Assert.Equal(new[] { "CElCPapa", "ItsJustEstePapa" }, model.Groups[1].Rows.Select(r => r.Name).ToArray());
        var missing = model.Groups[1].Rows[1];
        Assert.True(missing.Missing);
        Assert.Equal(StatText.Dash, missing.Value);
        Assert.Equal(StatText.Dash, missing.InGroup);
        Assert.Equal(Records.Change(reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue), Now), model.Groups[1].Rows[0].Change);

        Assert.Equal("ItsJustEste", Assert.Single(model.Groups[2].Rows).Name);
    }

    [Fact]
    public void MyAccountsForAStatNoLongerOfferedIsStale()
    {
        var model = PanelModels.MyAccounts(Live([], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, Stat: "counter:Gone"));

        Assert.Equal(PanelText.StaleStat, model.Head.Stale);
    }

    // ---- Promotion check ----

    [Fact]
    public void PromotionCheckRanksEachAltAmongTheOtherSourcesLiveRows()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([main, alts], [Installed(Clan, "value")], Snaps(
            Snapshot(main.Id, [Row(101, 14_000_000), Row(7, 20_000_000), Row(8, 8_240_900)]),
            Snapshot(alts.Id, [Row(202, 3_050_118), Row(201, 12_418_220), Row(301, null)])));

        var model = PanelModels.PromotionCheck(live, new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value"));

        Assert.Equal("K0i2 → CCGP", model.Head.Subtitle);
        Assert.Equal("CCGP's lowest now", model.LowestLabel);
        Assert.Equal("8,240,900", model.Lowest);
        Assert.Equal(new[] { "CElCPapa", "ItsJustEstePapa", "ItsJustEste" }, model.Rows.Select(r => r.Name).ToArray());

        var place = Records.WouldPlace(12_418_220, [14_000_000, 20_000_000, 8_240_900])!.Value;
        Assert.Equal($"{PanelText.Ordinal(place.Place)} of {place.Of}", model.Rows[0].WouldPlace);
        Assert.True(model.Rows[0].Fits);
        Assert.Equal("below the lowest", model.Rows[1].WouldPlace);
        Assert.False(model.Rows[1].Fits);
        Assert.True(model.Rows[2].Missing);
        Assert.Equal(StatText.Dash, model.Rows[2].Value);
        Assert.Contains("if it were in CCGP now", model.Head.Note);
    }

    [Theory]
    [InlineData(nameof(PanelModels.PromotionCheck))]
    [InlineData(nameof(PanelModels.Top))]
    [InlineData(nameof(PanelModels.LiveLeaderboard))]
    public void LiveOnlyPanelsCannotReachTheBook(string builder)
    {
        var parameters = typeof(PanelModels).GetMethods().Where(m => m.Name == builder).SelectMany(m => m.GetParameters()).ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(ScoreBookReader) || p.ParameterType == typeof(IScoreBook));
    }

    // ---- Account card ----

    [Fact]
    public void TheAccountCardPicksTheTopAccountAndShowsItsRecords()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, [Row(202, 9_104_330), Row(201, 12_418_220)], period: LivePeriod)));
        var reader = Reader(
            Read(alts, Now.AddHours(-2), Period, null, "value", (201, 11_000_000)),
            Read(alts, Now.AddMinutes(-3), Period, null, "value", (201, 12_418_220)),
            Final(alts, Now.AddDays(-9), "ArcadeBattle2026", Headline(18_000_000), "value", (201, 12_900_000)));

        var model = PanelModels.AccountCard(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));
        var records = Records.For(reader, Clan.Slug, alts.InputsKey, [alts.Id], 201, "value", live.Time);
        var series = reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue);

        Assert.Equal("CElCPapa · K0i2", model.Head.Subtitle);
        Assert.Equal("12,418,220", model.Big);
        Assert.Equal(new FactModel("In clan", "#1 of 2"), model.Facts[0]);
        Assert.Contains(new FactModel("Best battle",
            records.BestPeriodValue is { } best ? $"{StatText.Abbrev(best)} · {records.BestPeriod}" : StatText.Dash), model.Facts);
        Assert.Contains(new FactModel("Battles played", records.PeriodsPlayed.ToString()), model.Facts);
        Assert.Contains(new FactModel("Last read", PanelText.Ago(series[^1].T, Now)), model.Facts);
        Assert.Equal(series.Count, Assert.Single(model.Line).Points.Count);
    }

    // ---- Past periods ----

    [Fact]
    public void PastPeriodsListFinalsNewestFirstOneRowPerPeriodWithYourBest()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var arcade = new Dictionary<string, double> { ["clan-place"] = 16, ["clan-points"] = 30_000_000 };
        var cannon = new Dictionary<string, double> { ["clan-place"] = 435, ["clan-points"] = 67_104 };
        var reader = Reader(
            Final(main, Now.AddDays(-20), "Cannon", cannon, "value", (201, 40_210)),
            Final(main, Now.AddDays(-8), "Arcade2026", arcade, "value", (101, 14_100_000)),
            Final(main, Now.AddDays(-2), "Arcade2026", arcade, "value", (202, 2_000_000)));
        var live = Live([main], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.PastPeriods(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id, Stat: "value"));

        Assert.Equal("Past battles", model.Head.Title);
        Assert.Equal("Filled in from the clan's own record.", model.Head.Note);
        Assert.Equal(new[]
        {
            new PastRow("Arcade2026", "16th", StatText.Abbrev(30_000_000), $"{StatText.Abbrev(14_100_000)} · estehernandez"),
            new PastRow("Cannon", "435th", StatText.Abbrev(67_104), $"{StatText.Abbrev(40_210)} · CElCPapa"),
        }, model.Rows.ToArray());
    }

    // ---- Records ----

    [Fact]
    public void RecordsNameWhichAccountHoldsEachAndSkipAccountsWithNoData()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var reader = Reader(
            Read(profile, Now.AddDays(-3), null, null, "diamonds", (201, 100), (202, 50)),
            Read(profile, Now.AddDays(-1), null, null, "diamonds", (201, 300), (202, 1_000)));
        var live = Live([profile], [Installed(Profile, "diamonds")], Snaps());

        var model = PanelModels.RecordsPanel(live, reader, new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.Equal(new[] { "Highest", "Biggest day", "Fastest 7 days" }, model.Facts.Select(f => f.Label).ToArray());
        var holder = new[] { Main, AltOne, AltTwo, Loose }
            .Select(a => (Account: a, Found: Records.For(reader, Profile.Slug, profile.InputsKey, [profile.Id], a.RobloxUserId, "diamonds", live.Time)))
            .Where(x => x.Found.Highest is not null)
            .MaxBy(x => x.Found.Highest)!;
        Assert.Equal($"{holder.Account.DisplayName} · {StatText.Abbrev(holder.Found.Highest!.Value)}", model.Facts[0].Value);
        Assert.Equal("Diamonds", model.Head.Subtitle);
    }

    [Fact]
    public void RecordsThatOnlyFellShowOneMinusSign()
    {
        // Same UTC day, value falling: the biggest day and the fastest 7 days are both -1,500.
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var reader = Reader(
            Read(profile, Now.AddHours(-3), null, null, "diamonds", (201, 2_000)),
            Read(profile, Now.AddHours(-2), null, null, "diamonds", (201, 500)));
        var live = Live([profile], [Installed(Profile, "diamonds")], Snaps());

        var model = PanelModels.RecordsPanel(live, reader, new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.Equal(new FactModel("Biggest day", $"{AltOne.DisplayName} · -1.5K"), model.Facts[1]);
        Assert.Equal(new FactModel("Fastest 7 days", $"{AltOne.DisplayName} · -1.5K"), model.Facts[2]);
    }

    // ---- Top ----

    [Fact]
    public void TopListsTheLeadersAndPlacesYourSourcesWhereTheydRank()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var main = SourceOf("s-00000001", Clan, "G11", SourceRole.Main);
        var outsider = SourceOf("s-00000003", Clan, "Outsider", SourceRole.Watch);
        var groups = Enumerable.Range(1, 12)
            .Select(i => new GroupRow($"G{i}", new Dictionary<string, double> { ["value"] = 1_000 - i * 10 }, i))
            .ToList();
        var live = Live([top, main, outsider], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(top.Id, null, groups: groups), Snapshot(main.Id, [], [Points(890)]), Snapshot(outsider.Id, [], [Points(5)])));

        var model = PanelModels.Top(live, new PanelSettings(TopList.Slug, SourceId: top.Id));

        Assert.Equal("Top of the battle", model.Head.Title);
        Assert.Equal("Clan", model.NameColumn);
        Assert.Equal("Points", model.ValueColumn);
        Assert.Equal(12, model.Rows.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"G{i}").ToArray(), model.Rows.Take(10).Select(r => r.Name).ToArray());
        Assert.Equal(new TopRow("11", "G11 ★", StatText.Abbrev(890), true, false), model.Rows[10]);

        // Below every value the list itself shows (lowest is 880): an estimated "~13" would claim a rank
        // the list never proved, so this reads "below the list" instead (fix round 1, finding 2).
        Assert.Equal(new TopRow("below the list", "Outsider", StatText.Abbrev(5), true, true), model.Rows[11]);
    }

    [Fact]
    public void TopSortsAnEstimateJustBeforeTheGroupItWouldOutrank()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var main = SourceOf("s-00000001", Clan, "Estimator", SourceRole.Main);
        GroupRow Group(string name, double points, int rank) => new(name, new Dictionary<string, double> { ["value"] = points }, rank);
        var groups = new[] { Group("First", 990, 1), Group("Second", 980, 2) };
        var live = Live([top, main], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(top.Id, null, groups: groups), Snapshot(main.Id, [], [Points(985)])));

        var model = PanelModels.Top(live, new PanelSettings(TopList.Slug, SourceId: top.Id));

        // 985 sits between 990 and 980, so the estimate lands between them, not after both.
        Assert.Equal(new[] { "First", "Estimator ★", "Second" }, model.Rows.Select(r => r.Name).ToArray());
        Assert.Equal("~2", model.Rows[1].Rank);
    }

    // ---- Profile stat ----

    [Fact]
    public void ProfileStatShowsTodayAndSevenDayGainsAndKeepsMissingValuesLast()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id, [Row(101, 215_850_364, "diamonds"), Row(201, 3_957_873_882, "diamonds")]) with
        {
            Unavailable = new Dictionary<long, string> { [202] = "Profile is private." },
        };
        var reader = Reader(
            Read(profile, Now.AddDays(-6), null, null, "diamonds", (101, 200_000_000)),
            Read(profile, new DateTimeOffset(Now.Date.AddHours(1), TimeSpan.Zero), null, null, "diamonds", (101, 213_000_000)),
            Read(profile, Now.AddMinutes(-5), null, null, "diamonds", (101, 215_850_364)));
        var live = Live([profile], [Installed(Profile, "diamonds")], Snaps(snapshot));

        var model = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "diamonds"));

        // Highest first; the two with no value keep your account list's order, after every value.
        Assert.Equal(new[] { "CElCPapa", "estehernandez", "ItsJustEstePapa", "ItsJustEste" }, model.Rows.Select(r => r.Name).ToArray());
        var mine = model.Rows[1];
        // Today measures from the reading before midnight — 6 days back, since none is closer — so no span is
        // stated. The 7-day window finds no reading that old, falls back to that same reading, and states the
        // real span it actually covers instead of silently claiming a full 7 days (fix round 1, finding 3).
        Assert.Equal(PanelText.Signed(15_850_364), mine.Today);
        Assert.Equal($"{PanelText.Signed(15_850_364)} in {StatText.Span(Now.AddMinutes(-5) - Now.AddDays(-6))}", mine.Week);
        Assert.Equal(StatText.Dash, model.Rows[2].Value);
        Assert.Equal("Profile is private.", model.Rows[2].Note);
        Assert.True(model.Rows[3].Missing);
        Assert.Equal(StatText.Dash, model.Rows[3].Value);
    }

    [Fact]
    public void AGainNeedsTwoReadings()
    {
        Assert.Null(PanelModels.Gain([]));
        Assert.Null(PanelModels.Gain([new SeriesPoint(Now, 5, null, false, 0)]));
        Assert.Equal(7, PanelModels.Gain([new SeriesPoint(Now.AddHours(-1), 5, null, false, 0), new SeriesPoint(Now, 12, null, false, 0)]));
    }

    // ---- Live leaderboard ----

    [Fact]
    public void TheLiveLeaderboardMarksYourAccountsAndUsesNamesHeldInMemory()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id, [Row(7, 3), Row(5, 20), Row(101, 14)])));

        var model = PanelModels.LiveLeaderboard(live, new PanelSettings(Clan.Slug, SourceId: main.Id), new Dictionary<long, string> { [5] = "Rival" });

        Assert.Equal(new[] { "Points" }, model.Columns.ToArray());
        Assert.Equal(new[] { "Rival", "estehernandez", "Member 7" }, model.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { false, true, false }, model.Rows.Select(r => r.Yours).ToArray());
        Assert.Equal("Live only. Never saved.", model.Head.Note);
    }
}
