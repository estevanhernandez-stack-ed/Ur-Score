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

    /// <summary>
    /// Your own user ids are one set per <see cref="LiveBoard"/>, not a set built on every read. It was rebuilt on
    /// every access, and two of the accesses sit inside per-row lambdas — "is this row one of mine?" for every row
    /// of every leaderboard on every redraw — so a fifty-row panel built fifty hash sets to draw itself (S1-13.10).
    /// The identity check is what pins the cache. The <c>with</c> half pins the hazard a cache on a record carries:
    /// a copy with other accounts must answer for THOSE accounts, not the ones the original was asked about.
    /// </summary>
    [Fact]
    public void YourIdsAreOneSetPerBoardAndACopyWithOtherAccountsAnswersForThose()
    {
        var live = Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Same(live.MyUserIds, live.MyUserIds);
        Assert.Equal(new long[] { 101, 201, 202, 301 }, live.MyUserIds.Order());

        var copy = live with { Accounts = [Main] };
        Assert.Equal(new long[] { 101 }, copy.MyUserIds);
        Assert.Equal(new long[] { 101, 201, 202, 301 }, live.MyUserIds.Order());
    }

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
    public void AChipsWordsComeFromItsRoleSoTheTwoCanNeverDisagree()
    {
        Assert.Equal((PanelText.Chip(SourceRole.Watch), true), (new PanelHead("Clan standing", ChipRole: SourceRole.Watch).Chip, new PanelHead("Clan standing", ChipRole: SourceRole.Watch).HasChip));
        Assert.Equal(("", false), (new PanelHead("Account card").Chip, new PanelHead("Account card").HasChip));
    }

    [Theory]
    [InlineData(SourceRole.Main)]
    [InlineData(SourceRole.Mine)]
    [InlineData(SourceRole.Watch)]
    public void AChipCarriesItsSourcesRoleSoItsColourNeverHangsOnItsWords(SourceRole role)
    {
        var source = SourceOf("s-00000001", Clan, "CCGP", role);
        var live = Live([source], [Installed(Clan, "value")], Snaps());

        var standing = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: source.Id));
        var past = PanelModels.PastPeriods(live, Reader(), new PanelSettings(Clan.Slug, SourceId: source.Id, Stat: "value"));
        var leaderboard = PanelModels.LiveLeaderboard(live, new PanelSettings(Clan.Slug, SourceId: source.Id), new Dictionary<long, string>());

        Assert.All(new[] { standing.Head, past.Head, leaderboard.Head }, head => Assert.Equal((PanelText.Chip(role), role), (head.Chip, head.ChipRole)));

        // A head with no source has no chip and no role.
        var gone = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: "s-gone0000"));
        Assert.Equal(("", (SourceRole?)null), (gone.Head.Chip, gone.Head.ChipRole));
    }

    /// <summary>
    /// A clan added under "your accounts are in" wore "yours" whatever its read said, so a board showed "yours" directly
    /// above "Your accounts 0 of 57" (owner's screenshot, 2026-09-16). The chip may call a clan yours only while the read
    /// in hand doesn't contradict it: a live read with members, none of them yours, makes it a clan you are watching.
    /// With nothing read, or a clan between battles, there is no evidence either way, so the chip keeps what you chose.
    /// Deciding membership from the clan's roster instead is the larger change, and it is in the backlog.
    /// </summary>
    [Fact]
    public void AClanReadWithNoneOfYourAccountsIsNotCalledYours()
    {
        var mine = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        LiveBoard Board(RecipeSnapshot? snap) => Live([mine], [Installed(Clan, "value")], snap is null ? Snaps() : Snaps(snap));
        PanelHead HeadOf(LiveBoard live) => PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: mine.Id)).Head;

        // Read, members came back, and none of them is one of your accounts: a clan you are watching.
        var strangers = HeadOf(Board(Snapshot(mine.Id, [Row(900001, 50), Row(900002, 40)])));
        Assert.Equal(("watching", SourceRole.Watch), (strangers.Chip, strangers.ChipRole));

        // Read, and one of your accounts is among them: yours.
        var withYou = HeadOf(Board(Snapshot(mine.Id, [Row(900001, 50), Row(Main.RobloxUserId, 40)])));
        Assert.Equal(("yours", SourceRole.Mine), (withYou.Chip, withYou.ChipRole));

        // Nothing read yet, or read between battles: no evidence, so it keeps what you chose.
        Assert.Equal("yours", HeadOf(Board(null)).Chip);
        Assert.Equal("yours", HeadOf(Board(Idle(mine.Id))).Chip);
    }

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

        Assert.Equal(new PanelHead("Clan standing", "CCGP", SourceRole.Main), model.Head);
        Assert.Equal("14th", model.Place);
        Assert.Equal("in the battle", model.PlaceSuffix);
        Assert.Equal("Clan points", model.TotalLabel);
        Assert.Equal("30,214,400", model.Total);
        Assert.Equal("+1.41M in 57m", model.Change);
        Assert.Equal(ChangeDirection.Up, model.ChangeDirection);
        Assert.Equal("1 of 2", model.Accounts);
        // The end is kept as the instant, not as words: the panel's clock ticks it down between reads.
        Assert.Equal("AutumnBattle", model.PeriodLine);
        Assert.Equal(LivePeriod.Ends, model.Ends);
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
        var withMissingRank = PanelModels.Standing(With(Group("Other", 40_000_000, 12), Group("CCGP", 30_200_000, 14)), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.True(withAbove.HasGap);
        Assert.Equal("To 13th", withAbove.GapLabel);
        Assert.Equal("9.8M behind", withAbove.Gap);
        Assert.Equal(0.755, withAbove.GapFill, 3);
        Assert.False(withMissingRank.HasGap);
    }

    /// <summary>
    /// Backlog S1-13.5. A group list ranks ties as competitions do (11, 12, 12, 14), and the gap looked only for rank minus one,
    /// so the group behind a tie, and each group in it, showed no gap to anyone. The group above is the nearest one ranked
    /// higher; the gap shows when the list holds every group ranked between, which is when that group's rank plus how many
    /// share it is this one's.
    /// </summary>
    [Fact]
    public void WithTiedRanksTheGapIsToTheNearestGroupRankedHigher()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        GroupRow Group(string name, double points, int rank) => new(name, new Dictionary<string, double> { ["value"] = points }, rank);

        StandingModel For(string clan, params GroupRow[] groups)
        {
            var main = SourceOf("s-00000001", Clan, clan, SourceRole.Main);
            var live = Live([main, top], [Installed(Clan, "value"), Installed(TopList)],
                Snaps(Snapshot(main.Id, [], [Place(14), Points(30_200_000)]), Snapshot(top.Id, null, groups: groups)));
            return PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        }

        GroupRow[] tied = [Group("Eleventh", 50_000_000, 11), Group("TiedA", 40_000_000, 12), Group("TiedB", 40_000_000, 12), Group("CCGP", 30_200_000, 14)];

        var behindTheTie = For("CCGP", tied);
        var inTheTie = For("TiedB", tied);
        var firstOfTheTie = For("TiedA", tied);

        Assert.Equal((true, "To 12th", "9.8M behind"), (behindTheTie.HasGap, behindTheTie.GapLabel, behindTheTie.Gap));
        Assert.Equal((true, "To 11th", "10M behind"), (inTheTie.HasGap, inTheTie.GapLabel, inTheTie.Gap));
        Assert.Equal((true, "To 11th"), (firstOfTheTie.HasGap, firstOfTheTie.GapLabel));

        // A tied group is never "above" the group it ties with.
        Assert.NotEqual("To 12th", inTheTie.GapLabel);

        // 12, 12 and then 15: the 14th isn't in the list, so nothing proves the 12th is the group just above.
        Assert.False(For("CCGP", Group("TiedA", 40_000_000, 12), Group("TiedB", 40_000_000, 12), Group("CCGP", 30_200_000, 15)).HasGap);

        // Tied for first, alone in the list, or in a list that has no groups: there is no group above.
        Assert.False(For("CCGP", Group("Other", 40_000_000, 1), Group("CCGP", 40_000_000, 1)).HasGap);
        Assert.False(For("Other", Group("Other", 40_000_000, 1), Group("CCGP", 40_000_000, 1)).HasGap);
        Assert.False(For("CCGP", Group("CCGP", 30_200_000, 14)).HasGap);
        Assert.False(For("CCGP").HasGap);
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

        Assert.True(running.Head.Overdue);
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
        Assert.Equal(ChangeDirection.None, model.ChangeDirection);
    }

    /// <summary>
    /// Backlog S1-13.14. The change was drawn in cyan, this app's good news, whichever way it went, so a clan that fell
    /// during a battle read as one that rose. Its colour now comes from the same movement as its words, and the words
    /// carry the sign, so the colour is never the only way to tell.
    /// </summary>
    [Fact]
    public void AStandingChangeSaysWhichWayItWentInTheSameMovementAsItsWords()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id, [], [Place(14), Points(29_000_000)], LivePeriod)));
        StandingModel With(params double[] totals) => PanelModels.Standing(live,
            Reader([.. totals.Select((total, i) => Read(main, Now.AddHours(-1).AddMinutes(i * 30), Period, Headline(total), "value"))]),
            new PanelSettings(Clan.Slug, SourceId: main.Id));

        var fell = With(30_000_000, 29_000_000);
        var rose = With(28_000_000, 29_000_000);
        var level = With(29_000_000, 29_000_000);
        var once = With(29_000_000);

        Assert.Equal(("-1M in 30m", ChangeDirection.Down), (fell.Change, fell.ChangeDirection));
        Assert.Equal(("+1M in 30m", ChangeDirection.Up), (rose.Change, rose.ChangeDirection));
        // Not a rise: no change is neither colour.
        Assert.Equal(ChangeDirection.Flat, level.ChangeDirection);
        Assert.Equal((Records.NoEarlierRead, ChangeDirection.None), (once.Change, once.ChangeDirection));
        Assert.Equal(ChangeDirection.None, Records.Direction([]));
    }

    /// <summary>
    /// The clan's own logo in Clan standing. Each panel shows the picture of the source IT is about, not the window's: a panel
    /// pinned to a watched clan shows that clan's (backlog V3-S.7 is the window wearing the wrong clan; this is the same rule
    /// one level down). Its name says whose picture it is, since a picture with no name is nothing to a screen reader.
    /// </summary>
    [Fact]
    public void AStandingPanelShowsThePictureOfTheClanItIsAboutAndNamesIt()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var live = Live([main, rival], [Installed(Clan, "value")], Snaps()) with
        {
            Icons = new Dictionary<string, string> { [main.Id] = "ccgp.png", [rival.Id] = "nova.png" },
        };

        var pinnedToRival = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: rival.Id));
        var onMain = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(("nova.png", "NovaForge clan icon", true), (pinnedToRival.Icon, pinnedToRival.IconName, pinnedToRival.HasIconSlot));
        Assert.Equal(("ccgp.png", "CCGP clan icon", true), (onMain.Icon, onMain.IconName, onMain.HasIconSlot));
    }

    [Fact]
    public void AStandingPanelWhoseClanHasNoPictureYetKeepsItsSlotAndSaysNothingAboutIt()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var live = Live([main, rival], [Installed(Clan, "value")], Snaps()) with
        {
            Icons = new Dictionary<string, string> { [main.Id] = "ccgp.png" },
        };

        var model = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: rival.Id));

        // Not the main's picture standing in: none, in a slot that keeps its space.
        Assert.Equal((null, true, "NovaForge clan icon"), (model.Icon, model.HasIconSlot, model.IconName));
        // A missing decoration is not news: no note, no stale message.
        Assert.Equal(("", null), (model.Head.Note, model.Head.Stale));

        var nothingYet = PanelModels.Standing(live with { Icons = null }, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        Assert.Equal((null, true), (nothingYet.Icon, nothingYet.HasIconSlot));
    }

    [Fact]
    public void AStandingPanelWhoseRecipeNamesNoIconOrWhoseClanIsGoneHasNoSlot()
    {
        var noIcon = Clan with { Icon = null };
        var main = SourceOf("s-00000001", noIcon, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(noIcon, "value")], Snaps()) with
        {
            // Left over from before the recipe dropped its icon: not drawn, whatever the map still holds.
            Icons = new Dictionary<string, string> { [main.Id] = "ccgp.png" },
        };

        var dropped = PanelModels.Standing(live, Reader(), new PanelSettings(noIcon.Slug, SourceId: main.Id));
        var gone = PanelModels.Standing(live, Reader(), new PanelSettings(noIcon.Slug, SourceId: "s-gone0000"));

        Assert.Equal((null, false), (dropped.Icon, dropped.HasIconSlot));
        Assert.Equal((null, false), (gone.Icon, gone.HasIconSlot));
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

    /// <summary>
    /// Backlog S1-13.6. Every problem a race could have read "This panel's clan was removed.", and a clan that really was
    /// removed dropped off the chart without a word. Each problem now says what it is, and a race draws what it still can.
    /// </summary>
    [Fact]
    public void ARaceSaysWhichProblemItHasInsteadOfClaimingAClanWasRemoved()
    {
        var clans = Enumerable.Range(1, 7).Select(i => SourceOf($"s-0000000{i}", Clan, $"Clan{i}", SourceRole.Watch)).ToList();
        var live = Live(clans, [Installed(Clan, "value"), Installed(Profile, "diamonds")],
            Snaps([.. clans.Select(c => Snapshot(c.Id, [], [Points(1_000)], LivePeriod))]));
        RaceModel Race(string recipe, params string[] ids) => PanelModels.Race(live, Reader(), new PanelSettings(recipe, SourceIds: ids));

        // A recipe with no summed total has nothing to race, whichever lines it names.
        Assert.Equal("This panel's recipe has no total to race.", Race(Profile.Slug, clans[0].Id, clans[1].Id).Head.Stale);

        var oneGone = Race(Clan.Slug, clans[0].Id, "s-gone0000", clans[1].Id);
        Assert.Null(oneGone.Head.Stale);
        Assert.Equal(2, oneGone.Series.Count);
        Assert.Equal("One of this race's clans was removed.", oneGone.Head.Note);
        Assert.Equal("2 of this race's clans were removed.", Race(Clan.Slug, clans[0].Id, "s-gone0000", "s-gone0001").Head.Note);

        var seven = Race(Clan.Slug, [.. clans.Select(c => c.Id)]);
        Assert.Equal(PanelModels.MaxRace, seven.Series.Count);
        Assert.Equal("Only the first 5 clans are drawn.", seven.Head.Note);

        // One clan is a whole race: a switched-on clans list brings its rivals in (2026-09-24). With no lines at all there is nothing to draw.
        var one = Race(Clan.Slug, clans[0].Id);
        Assert.Null(one.Head.Stale);
        Assert.Equal("", one.Head.Note);
        Assert.Single(one.Series);
        Assert.Equal("Choose a clan to race.", Race(Clan.Slug).Head.Stale);

        // Every line gone is the one case that really is "removed".
        Assert.Equal("This panel's clan was removed.", Race(Clan.Slug, "s-gone0000", "s-gone0001").Head.Stale);
    }

    // ---- My accounts ----

    /// <summary>
    /// Backlog S1-13.4. Before the first read every account sat under "Not in a watched clan", which was true of none of
    /// them yet; an account a watched clan held sat there too, the opposite of true; and so did one RoRoRo hadn't matched.
    /// Each leftover account is headed by what is actually known about it.
    /// </summary>
    [Fact]
    public void MyAccountsHeadsEachLeftoverAccountByWhatIsActuallyKnown()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var unmatched = new HostAccount(Guid.Parse("55555555-5555-5555-5555-555555555555"), 0, "NotYetMatched");
        IReadOnlyList<HostAccount> accounts = [Main, AltOne, AltTwo, Loose, unmatched];
        MyAccountsModel Model(
            IReadOnlyDictionary<string, RecipeSnapshot> reads, IReadOnlyDictionary<string, RecipeSnapshot>? remembered = null, IReadOnlyList<Source>? sources = null) =>
            PanelModels.MyAccounts(Live(sources ?? [main, alts, rival], [Installed(Clan, "value")], reads, accounts: accounts, remembered: remembered),
                Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        string[] Headings(MyAccountsModel model) => [.. model.Groups.Select(g => g.Heading)];

        var mainRead = Snapshot(main.Id, [Row(Main.RobloxUserId, 10)]);
        var altsRead = Snapshot(alts.Id, [Row(AltOne.RobloxUserId, 5)]);
        var rivalRead = Snapshot(rival.Id, [Row(AltTwo.RobloxUserId, 7), Row(9, 1)]);

        // Nothing read: no account is "not in" anything yet.
        var nothing = Model(Snaps());
        Assert.Equal(new[] { "No clans read yet", "Not matched by RoRoRo yet" }, Headings(nothing));
        Assert.Equal(4, nothing.Groups[0].Rows.Count);

        // Some read: the leftovers weren't found in what was read, which is all that is known of them.
        Assert.Equal(new[] { "★ CCGP", "Not found in the clans read so far", "Not matched by RoRoRo yet" }, Headings(Model(Snaps(mainRead))));

        // Every clan read: the account a watched clan holds says so, and only the one in none of them is "not in a watched clan".
        var all = Model(Snaps(mainRead, altsRead, rivalRead));
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Only in clans you're watching", "Not in a watched clan", "Not matched by RoRoRo yet" }, Headings(all));
        var watched = Assert.Single(all.Groups[2].Rows);
        Assert.Equal((AltTwo.DisplayName, StatText.Dash, true), (watched.Name, watched.Value, watched.Missing));
        Assert.Equal(Loose.DisplayName, Assert.Single(all.Groups[3].Rows).Name);
        Assert.Equal(unmatched.DisplayName, Assert.Single(all.Groups[4].Rows).Name);

        // Every clan remembered and none read this session: the book keeps only the accounts it kept, so an account missing from
        // it proves nothing about the clan. (A watched clan is never remembered, A40, so this board has your own clans alone.)
        var kept = new Dictionary<string, RecipeSnapshot>
        {
            [main.Id] = mainRead with { RememberedAt = Now.AddHours(-2) },
            [alts.Id] = altsRead with { RememberedAt = Now.AddHours(-2) },
        };
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Not found in the clans read so far", "Not matched by RoRoRo yet" },
            Headings(Model(Snaps(), kept, [main, alts])));
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Not in a watched clan", "Not matched by RoRoRo yet" },
            Headings(Model(Snaps(mainRead, altsRead), sources: [main, alts])));
    }

    /// <summary>
    /// A clan between battles WAS read: its reading is idle, so it has no member list to find an account in. Heading every
    /// account "No clans read yet" then was untrue, and it is what the board shows most of the time, because the main clan
    /// sits between battles far longer than it is in one. The heading says what the reads found, in the recipe's words, and
    /// never more than they found: "right now" only once every clan has been read.
    /// </summary>
    [Fact]
    public void MyAccountsSaysNoClanIsInABattleWhenTheClansWereReadBetweenBattles()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var unmatched = new HostAccount(Guid.Parse("55555555-5555-5555-5555-555555555555"), 0, "NotYetMatched");
        IReadOnlyList<HostAccount> accounts = [Main, AltOne, AltTwo, Loose, unmatched];
        MyAccountsModel Model(IReadOnlyDictionary<string, RecipeSnapshot> reads, IReadOnlyDictionary<string, RecipeSnapshot>? remembered = null) =>
            PanelModels.MyAccounts(Live([main, alts, rival], [Installed(Clan, "value")], reads, accounts: accounts, remembered: remembered),
                Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        string[] Headings(MyAccountsModel model) => [.. model.Groups.Select(g => g.Heading)];

        // Every clan read and none in a battle: nothing to place an account in, and that is what is said.
        var between = Model(Snaps(Idle(main.Id), Idle(alts.Id), Idle(rival.Id)));
        Assert.Equal(new[] { "No clan is in a battle right now", "Not matched by RoRoRo yet" }, Headings(between));
        Assert.Equal(4, between.Groups[0].Rows.Count);

        // The main clan read between battles and the rest not yet, or not reachable: nothing is claimed of the clans not read.
        Assert.Equal(new[] { "No clan read so far is in a battle", "Not matched by RoRoRo yet" }, Headings(Model(Snaps(Idle(main.Id)))));
        Assert.Equal(new[] { "No clan read so far is in a battle", "Not matched by RoRoRo yet" },
            Headings(Model(Snaps(Idle(main.Id), Unreachable(alts.Id), Idle(rival.Id)))));

        // A read that failed read nothing, so with nothing else read, no clan has been.
        Assert.Equal(new[] { "No clans read yet", "Not matched by RoRoRo yet" }, Headings(Model(Snaps(Unreachable(main.Id)))));

        // One clan in a battle among idle ones: its rows were read, and the leftovers weren't found in them.
        var mainRead = Snapshot(main.Id, [Row(Main.RobloxUserId, 10)]);
        Assert.Equal(new[] { "★ CCGP", "Not found in the clans read so far", "Not matched by RoRoRo yet" },
            Headings(Model(Snaps(mainRead, Idle(alts.Id), Idle(rival.Id)))));

        // Numbers the book kept for a clan now between battles still place the accounts they kept, and the rest weren't found
        // in them; an idle read never stands in for a read of the members, and a remembered one never counts as read now.
        var kept = new Dictionary<string, RecipeSnapshot> { [main.Id] = mainRead with { RememberedAt = Now.AddHours(-2) } };
        Assert.Equal(new[] { "★ CCGP", "Not found in the clans read so far", "Not matched by RoRoRo yet" },
            Headings(Model(Snaps(Idle(main.Id), Idle(alts.Id), Idle(rival.Id)), kept)));
    }

    [Fact]
    public void MyAccountsGroupsMainFirstThenMineThenAccountsInNoWatchedClan()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        IReadOnlyList<RecipeRow> ccgpRows = [Row(101, 14_020_550), Row(5, 20_000_000), Row(6, 1_000)];
        IReadOnlyList<RecipeRow> altRows = [Row(202, null), Row(201, 12_418_220), Row(101, 9_000_000), Row(7, 50)];
        var sentLine = new AccountLine("BirchMain", Main.AccountId, new Dictionary<string, double> { ["value"] = 14_020_550 }, Now);
        // Sent by this very read (S1-F.5): the remembered line alone no longer makes a dot.
        var mainRead = Snapshot(main.Id, ccgpRows, period: LivePeriod, sent: [sentLine])
            with { SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Main.AccountId, "value") } };
        var live = Live([alts, main], [Installed(Clan, "value")],
            Snaps(mainRead, Snapshot(alts.Id, altRows, period: LivePeriod)));
        var reader = Reader(
            Read(alts, Now.AddHours(-1), Period, null, "value", (201, 12_000_000)),
            Read(alts, Now.AddMinutes(-3), Period, null, "value", (201, 12_418_220)));

        var model = PanelModels.MyAccounts(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));

        Assert.Equal("by points", model.Head.Subtitle);
        Assert.Equal("In clan", model.GroupColumn);
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Not in a watched clan" }, model.Groups.Select(g => g.Heading).ToArray());

        var ccgp = Assert.Single(model.Groups[0].Rows);
        Assert.Equal("BirchMain", ccgp.Name);
        Assert.Equal($"#{Ranking.Competition(ccgpRows, "value")[101]} of 3", ccgp.InGroup);
        Assert.True(ccgp.Sent);

        Assert.Equal(new[] { "AshAlt", "DuneAlt" }, model.Groups[1].Rows.Select(r => r.Name).ToArray());
        var missing = model.Groups[1].Rows[1];
        Assert.True(missing.Missing);
        Assert.Equal(StatText.Dash, missing.Value);
        Assert.Equal(StatText.Dash, missing.InGroup);
        Assert.Equal(Records.Change(reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue), Now), model.Groups[1].Rows[0].Change);

        Assert.Equal("CedarLoose", Assert.Single(model.Groups[2].Rows).Name);
    }

    /// <summary>
    /// Backlog S1-F.5. The dot is read as "this account's number just went to RoRoRo", so it means exactly that: sent in its
    /// source's last read, of this panel's stat. A send the session remembers from earlier is not a dot, and the legend says
    /// which read it means.
    /// </summary>
    [Fact]
    public void TheSentDotMeansSentInTheLastReadNotEarlierThisSession()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        // RoRoRo got this account's points on an earlier read (the session remembers them), but not on the last one.
        var earlier = new AccountLine(Main.DisplayName, Main.AccountId, new Dictionary<string, double> { ["value"] = 14_000_000 }, Now.AddMinutes(-3));
        var notThisRead = Snapshot(main.Id, [Row(Main.RobloxUserId, 14_020_550)], period: LivePeriod, sent: [earlier]);
        var thisRead = notThisRead with { SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Main.AccountId, "value") } };
        var anotherStat = notThisRead with { SentThisRead = new HashSet<(Guid AccountId, string Stat)> { (Main.AccountId, "eggs") } };

        MyAccountsModel For(RecipeSnapshot snapshot) =>
            PanelModels.MyAccounts(Live([main], [Installed(Clan, "value")], Snaps(snapshot)), Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        bool Dot(RecipeSnapshot snapshot) => For(snapshot).Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId).Sent;

        Assert.False(Dot(notThisRead));
        Assert.True(Dot(thisRead));
        Assert.False(Dot(anotherStat));
        Assert.Equal("● sent to RoRoRo in the last read", For(thisRead).Head.Note);

        // A remembered panel sent nothing this session, whatever it holds.
        var kept = thisRead with { RememberedAt = Now.AddHours(-2) };
        var remembered = PanelModels.MyAccounts(
            Live([main], [Installed(Clan, "value")], Snaps(), remembered: new Dictionary<string, RecipeSnapshot> { [main.Id] = kept with { SentThisRead = new HashSet<(Guid AccountId, string Stat)>() } }),
            Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        Assert.All(remembered.Groups.SelectMany(g => g.Rows), r => Assert.False(r.Sent));
    }

    [Fact]
    public void MyAccountsReadsPlaytimeAsADurationAndADateWithNoChange()
    {
        // Final review Important 2, under the Task 3 ruling: My accounts fits the profile recipe too, so a duration's value
        // and its change read as time ("+2h 0m in 2h 55m", not "+7.2K in 2h 55m"), and a date has no change.
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id,
        [
            new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["playtime"] = 50_651_629, ["first-join"] = 1_600_000_000 }),
            new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["playtime"] = 3_600, ["first-join"] = 1_700_000_000 }),
        ]);
        var live = Live([profile], [Installed(Profile, "playtime", "first-join")], Snaps(snapshot));
        var playtimeBook = Reader(
            Read(profile, Now.AddHours(-3), null, null, "playtime", (Main.RobloxUserId, 50_644_429)),
            Read(profile, Now.AddMinutes(-5), null, null, "playtime", (Main.RobloxUserId, 50_651_629)));
        var dateBook = Reader(
            Read(profile, Now.AddHours(-3), null, null, "first-join", (Main.RobloxUserId, 1_600_000_000)),
            Read(profile, Now.AddMinutes(-5), null, null, "first-join", (Main.RobloxUserId, 1_600_000_000)));

        var playtime = PanelModels.MyAccounts(live, playtimeBook, new PanelSettings(Profile.Slug, Stat: "playtime"));
        var main = playtime.Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId);
        var alt = playtime.Groups.SelectMany(g => g.Rows).Single(r => r.UserId == AltOne.RobloxUserId);

        Assert.Equal("586d 5h", main.Value);
        Assert.Equal($"+2h 0m in {StatText.Span(TimeSpan.FromMinutes(175))}", main.Change);
        Assert.Equal("1h 0m", alt.Value);
        Assert.Equal("no earlier read", alt.Change);

        var firstJoined = PanelModels.MyAccounts(live, dateBook, new PanelSettings(Profile.Slug, Stat: "first-join"));
        var joined = firstJoined.Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId);

        Assert.Equal("13 Sep 2020", joined.Value);
        Assert.Equal(StatText.Dash, joined.Change);
    }

    [Fact]
    public void MyAccountsForAStatNoLongerOfferedIsStale()
    {
        var model = PanelModels.MyAccounts(Live([], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, Stat: "counter:Gone"));

        Assert.Equal(PanelText.StaleStat, model.Head.Stale);
    }

    /// <summary>Plan A44: the panel with the dashes is the panel that says why.</summary>
    [Fact]
    public void MyAccountsSaysWhyARowHasNoNumbers()
    {
        var snapshot = Snapshot(ProfileSource.Id, [Row(Main.RobloxUserId, 4200, "diamonds")])
            with { Unavailable = new Dictionary<long, string> { [AltOne.RobloxUserId] = "Profile is private. Link this account on db.biggames.io and turn on its Profile view." } };
        var live = Live([ProfileSource], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot> { [ProfileSource.Id] = snapshot });

        var model = PanelModels.MyAccounts(live, Reader(), new PanelSettings(Profile.Slug, ProfileSource.Id, Stat: "diamonds"));
        var rows = model.Groups.SelectMany(g => g.Rows).ToList();

        var unread = rows.Single(r => r.UserId == AltOne.RobloxUserId);
        Assert.True(unread.Missing);
        Assert.Equal("Profile is private. Link this account on db.biggames.io and turn on its Profile view.", unread.Note);
        Assert.True(unread.HasNote);

        // An account that was read says nothing, and no row repeats the recipe's name on a panel drawn per recipe.
        var read = rows.Single(r => r.UserId == Main.RobloxUserId);
        Assert.Equal("", read.Note);
        Assert.False(read.HasNote);
    }

    /// <summary>
    /// Plans A39 and A44 meet without arguing. The remembered mark is the panel's, about the numbers it is showing;
    /// the reason is one row's, about numbers it hasn't got. A remembered snapshot carries no Unavailable entry, and
    /// the note is read from the live map alone, so a row drawn from the book is never also told it can't be read.
    /// </summary>
    [Fact]
    public void ARememberedPanelStillSaysWhyARowWithNoNumbersIsEmpty()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var mine = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        // Nothing builds a remembered snapshot with an Unavailable entry (A39); the second one pins that the panel
        // would not print it if something ever did, rather than assuming the map stays empty.
        var kept = Snapshot(main.Id, [Row(Main.RobloxUserId, 14_020_550)])
            with
            {
                RememberedAt = Now.AddHours(-2),
                Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Not in this clan right now." },
            };
        var read = Snapshot(mine.Id, [])
            with { Unavailable = new Dictionary<long, string> { [AltOne.RobloxUserId] = "Profile is private." } };
        var live = Live([main, mine], [Installed(Clan, "value")], Snaps(read),
            remembered: new Dictionary<string, RecipeSnapshot> { [main.Id] = kept });

        var model = PanelModels.MyAccounts(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value"));
        var rows = model.Groups.SelectMany(g => g.Rows).ToList();

        Assert.True(model.Head.Remembered);

        // The row the book remembers keeps its number, and says nothing about being unreadable.
        var fromTheBook = rows.Single(r => r.UserId == Main.RobloxUserId);
        Assert.False(fromTheBook.Missing);
        Assert.Equal("", fromTheBook.Note);

        // The row with no numbers carries its reason, inside that same remembered panel.
        var empty = rows.Single(r => r.UserId == AltOne.RobloxUserId);
        Assert.True(empty.Missing);
        Assert.Equal("Profile is private.", empty.Note);

        // Read from what was actually read: a reason only the remembered snapshot carries is never shown.
        Assert.Equal("", rows.Single(r => r.UserId == AltTwo.RobloxUserId).Note);
    }

    /// <summary>
    /// Backlog S1-6.9. "#rank of N" ranked only the rows that had the stat and then counted every row as N, so a member with
    /// no value made you "#1 of 4" where Promotion check, counting the same clan, ranked among 3. N is now how many rows the
    /// rank was counted among: the ones with a value. Ties, one player and an empty read are the edges.
    /// </summary>
    [Fact]
    public void RankInAGroupCountsOnlyTheRowsWithThatStatAsPromotionCheckDoes()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var settings = new PanelSettings(Clan.Slug, Stat: "value");
        (string Accounts, string Card) RankOf(long userId, params RecipeRow[] rows)
        {
            var live = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, rows, period: LivePeriod)));
            var row = PanelModels.MyAccounts(live, Reader(), settings).Groups.SelectMany(g => g.Rows).Single(r => r.UserId == userId);
            var card = PanelModels.AccountCard(live, Reader(), settings with { UserId = userId });
            return (row.InGroup, card.Facts.Single(f => f.Label == "In clan").Value);
        }

        // Four rows, one with no points: your account is 1st of the 3 that have a value.
        Assert.Equal(("#1 of 3", "#1 of 3"), RankOf(AltOne.RobloxUserId, Row(AltOne.RobloxUserId, 12_418_220), Row(7, 50), Row(8, null), Row(9, 3_000)));
        // Competition ranks (1, 2, 2, 4): a tie shares the place, and every tied row is still counted.
        Assert.Equal(("#2 of 4", "#2 of 4"), RankOf(AltOne.RobloxUserId, Row(7, 200), Row(AltOne.RobloxUserId, 100), Row(8, 100), Row(9, 50)));
        // Alone in the read.
        Assert.Equal(("#1 of 1", "#1 of 1"), RankOf(AltOne.RobloxUserId, Row(AltOne.RobloxUserId, 5)));
        // Your account with no value of its own has no place, whoever else was read.
        Assert.Equal(StatText.Dash, RankOf(AltOne.RobloxUserId, Row(AltOne.RobloxUserId, null), Row(7, 50)).Accounts);

        // A read of no rows places nobody: every account sits under a heading with a dash.
        var empty = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, [], period: LivePeriod)));
        Assert.All(PanelModels.MyAccounts(empty, Reader(), settings).Groups.SelectMany(g => g.Rows), r => Assert.Equal(StatText.Dash, r.InGroup));
    }

    [Fact]
    public void MyAccountsAndPromotionCheckCountTheSameClanTheSameWay()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        // CCGP read four members, and one of them has no points.
        var live = Live([main, alts], [Installed(Clan, "value")], Snaps(
            Snapshot(main.Id, [Row(Main.RobloxUserId, 14_000_000), Row(7, 20_000_000), Row(8, null), Row(6, 1_000)], period: LivePeriod),
            Snapshot(alts.Id, [Row(AltOne.RobloxUserId, 12_418_220)], period: LivePeriod)));

        var inCcgp = PanelModels.MyAccounts(live, Reader(), new PanelSettings(Clan.Slug, Stat: "value"))
            .Groups.SelectMany(g => g.Rows).Single(r => r.UserId == Main.RobloxUserId);
        var wouldPlace = PanelModels.PromotionCheck(live, new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value"))
            .Rows.Single(r => r.Name == AltOne.DisplayName);

        // Three members of CCGP have points. My accounts ranks among them; Promotion check ranks among them plus the account it moves.
        Assert.Equal("#2 of 3", inCcgp.InGroup);
        Assert.Equal("3rd of 4", wouldPlace.WouldPlace);
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
        Assert.Equal(new[] { "AshAlt", "DuneAlt", "CedarLoose" }, model.Rows.Select(r => r.Name).ToArray());

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
        var records = Records.For(reader, Clan.Slug, alts.InputsKey, alts.Id, 201, "value", live.Time);
        var series = reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue);

        Assert.Equal("AshAlt · K0i2", model.Head.Subtitle);
        Assert.Equal("12,418,220", model.Big);
        Assert.Equal(new FactModel("In clan", "#1 of 2"), model.Facts[0]);
        Assert.Contains(new FactModel("Best battle",
            records.BestPeriodValue is { } best ? $"{StatText.Abbrev(best)} · {records.BestPeriod}" : StatText.Dash), model.Facts);
        Assert.Contains(new FactModel("Battles played", records.PeriodsPlayed.ToString()), model.Facts);
        Assert.Contains(new FactModel("Last read", PanelText.Ago(series[^1].T, Now)), model.Facts);
        Assert.Equal(series.Count, Assert.Single(model.Line).Points.Count);
        Assert.True(model.HasLine);
        Assert.False(model.HasSections);
    }

    [Fact]
    public void TheAccountCardShowsThePickedAccountInTheRecipesSections()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id,
        [
            new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 215_850_364, ["rebirths"] = 9, ["playtime"] = 50_651_629, ["first-join"] = 1_600_000_000 }),
            new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 3_957_873_882, ["rebirths"] = 11, ["playtime"] = 3_600 }),
        ]) with
        {
            Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Profile is private." },
        };
        var live = Live([profile], [Installed(Profile, "diamonds", "rebirths", "playtime", "first-join")], Snaps(snapshot));
        var settings = new PanelSettings(Profile.Slug, Stat: "diamonds");

        var top = PanelModels.AccountCard(live, Reader(), settings);
        var picked = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: Main.RobloxUserId);
        var pinned = PanelModels.AccountCard(live, Reader(), settings with { UserId = AltOne.RobloxUserId }, pickedUserId: Main.RobloxUserId);
        var unread = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: AltTwo.RobloxUserId);
        var gone = PanelModels.AccountCard(live, Reader(), settings, pickedUserId: 999);

        // With nothing picked, the top account; a pick shows that account; a card pinned to an account stays on it.
        Assert.StartsWith(AltOne.DisplayName, top.Head.Subtitle);
        Assert.StartsWith(Main.DisplayName, picked.Head.Subtitle);
        Assert.StartsWith(AltOne.DisplayName, pinned.Head.Subtitle);
        Assert.StartsWith(AltOne.DisplayName, gone.Head.Subtitle);

        // The big number is the card's stat; the other shown stats sit in the recipe's sections, in its order, read as time.
        Assert.Equal("215,850,364", picked.Big);
        Assert.Equal(new[] { "Account", "Progression" }, picked.Sections.Select(s => s.Heading).ToArray());
        Assert.Equal(new[] { new FactModel("Playtime", "586d 5h"), new FactModel("First joined", "13 Sep 2020") }, picked.Sections[0].Facts);
        Assert.Equal(new[] { new FactModel("Rebirths", "9") }, picked.Sections[1].Facts);
        Assert.True(picked.HasSections);

        // With no book yet there is no line, so the card draws no chart.
        Assert.Empty(picked.Line);
        Assert.False(picked.HasLine);

        // A picked account the read couldn't reach says why, in the recipe's words.
        Assert.Equal(AltTwo.DisplayName, unread.Head.Subtitle);
        Assert.Equal("Profile is private.", unread.Head.Note);
        Assert.Empty(unread.Sections);
        Assert.False(unread.HasSections);
    }

    /// <summary>
    /// Backlog S1-13.8. A card pinned to one account with no reading said "No reading of your accounts yet.", a line about
    /// every account on a card about one. It names that account and gives the read's own reason when there is one, and
    /// says so when RoRoRo isn't listing the account at all.
    /// </summary>
    [Fact]
    public void ACardPinnedToAnAccountWithNoReadingTalksAboutThatAccount()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var read = Snapshot(alts.Id, [Row(AltOne.RobloxUserId, 12_418_220)], period: LivePeriod);
        PanelHead Card(RecipeSnapshot? snapshot, long? pinned) =>
            PanelModels.AccountCard(Live([alts], [Installed(Clan, "value")], snapshot is null ? Snaps() : Snaps(snapshot)), Reader(),
                new PanelSettings(Clan.Slug, Stat: "value", UserId: pinned)).Head;

        var listed = Card(read, AltTwo.RobloxUserId);
        Assert.Equal((AltTwo.DisplayName, $"No reading of {AltTwo.DisplayName} yet."), (listed.Subtitle, listed.Note));

        var withReason = Card(read with { Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Not in this clan right now." } }, AltTwo.RobloxUserId);
        Assert.Equal((AltTwo.DisplayName, "Not in this clan right now."), (withReason.Subtitle, withReason.Note));

        var unlisted = Card(read, 987_654_321);
        Assert.Equal(("", "RoRoRo isn't listing this panel's account right now."), (unlisted.Subtitle, unlisted.Note));

        // With no account chosen and nothing read, the card really is about all of them.
        Assert.Equal("No reading of your accounts yet.", Card(null, null).Note);
    }

    [Fact]
    public void TheAccountCardsHighestAndBiggestDayFactsReadAsTime()
    {
        // Controller ruling: besides the sections, the card's own value facts (Highest, Biggest day) read
        // through the stat's format too, and a duration's gain reads as a duration ("+3h 20m"), not seconds.
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id, [Row(AltOne.RobloxUserId, 22_000, "playtime")]);
        var reader = Reader(
            Read(profile, Now.AddHours(-3), null, null, "playtime", (AltOne.RobloxUserId, 10_000)),
            Read(profile, Now.AddHours(-2), null, null, "playtime", (AltOne.RobloxUserId, 22_000)));
        var live = Live([profile], [Installed(Profile, "playtime")], Snaps(snapshot));

        var model = PanelModels.AccountCard(live, reader, new PanelSettings(Profile.Slug, Stat: "playtime"));

        Assert.Equal(PanelText.Duration(22_000), model.Big);
        Assert.Equal(new FactModel("Highest", PanelText.Duration(22_000)), model.Facts[0]);
        Assert.Equal(new FactModel("Biggest day", "+3h 20m"), model.Facts[1]);
    }

    // ---- Past periods ----

    [Fact]
    public void WithNoFinalsYetThePanelSaysWhatIsTrueAndWhatHappensNext()
    {
        // V3-S.1: the old line ("No finished battles kept yet.") read as "this clan has never been in one".
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.PastPeriods(live, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id, Stat: "value"));

        Assert.Empty(model.Rows);
        Assert.Equal(
            "Ur Score hasn't read this clan's finished battles yet. The next read fills them in from the clan's own record.",
            model.Head.Note);
    }

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
            new PastRow("Arcade2026", "16th", StatText.Abbrev(30_000_000), $"{StatText.Abbrev(14_100_000)} · BirchMain"),
            new PastRow("Cannon", "435th", StatText.Abbrev(67_104), $"{StatText.Abbrev(40_210)} · AshAlt"),
        }, model.Rows.ToArray());
    }

    [Fact]
    public void OneBackfillsFinalsShareATimestampSoTheRecordsOwnOrderPutsTheNewestOnTop()
    {
        // A backfill writes a whole record in one cycle, so every final carries the same T and T orders nothing.
        // What does: the source lists its finished periods oldest first, so the last one it handed over is the
        // most recent, and the panel reads the record back the other way up.
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var t = Now.AddHours(-1);
        var reader = Reader(
            Final(main, t, "Halloween", Headline(1_000), "value", (101, 100)),
            Final(main, t, "Christmas2024", Headline(2_000), "value", (101, 200)),
            Final(main, t, "SpringBattle", Headline(3_000), "value", (101, 300)),
            Final(main, t, "SummerBattle", Headline(4_000), "value", (101, 400)));
        var live = Live([main], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.PastPeriods(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id, Stat: "value"));

        Assert.Equal(
            new[] { "SummerBattle", "SpringBattle", "Christmas2024", "Halloween" },
            model.Rows.Select(r => r.Period).ToArray());
    }

    [Fact]
    public void APeriodTheBookLearnedInALaterCycleSitsAboveAWholeBackfilledRecord()
    {
        // The other signal: a record only grows at its end, so a final written in a later cycle finished later
        // than everything the backfill already held, wherever the backfill's own rows landed.
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var backfilled = Now.AddDays(-4);
        var reader = Reader(
            Final(main, backfilled, "Halloween", Headline(1_000), "value", (101, 100)),
            Final(main, backfilled, "SpringBattle", Headline(2_000), "value", (101, 200)),
            Final(main, Now.AddHours(-2), "AutumnBattle", Headline(3_000), "value", (101, 300)));
        var live = Live([main], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.PastPeriods(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id, Stat: "value"));

        Assert.Equal(
            new[] { "AutumnBattle", "SpringBattle", "Halloween" },
            model.Rows.Select(r => r.Period).ToArray());
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
            .Select(a => (Account: a, Found: Records.For(reader, Profile.Slug, profile.InputsKey, profile.Id, a.RobloxUserId, "diamonds", live.Time)))
            .Where(x => x.Found.Highest is not null)
            .MaxBy(x => x.Found.Highest)!;
        Assert.Equal($"{holder.Account.DisplayName} · {StatText.Abbrev(holder.Found.Highest!.Value)}", model.Facts[0].Value);
        Assert.Equal("Diamonds", model.Head.Subtitle);
    }

    /// <summary>
    /// Backlog S1-9.3 on the panel. An account both of your clans read has two records, one per clan, and the panel shows the
    /// better one. A merge of the two would put a 960 "rise" from one clan's points to the other's on a board that wants to
    /// be believed during a battle.
    /// </summary>
    [Fact]
    public void AnAccountTwoOfYourClansReadHoldsTheBestOfEachClansOwnRecordsNeverAMerge()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var reader = Reader(
            Read(alts, Now.AddHours(-9), Period, null, "value", (201, 50)), Read(alts, Now.AddHours(-1), Period, null, "value", (201, 80)),
            Read(main, Now.AddHours(-6), Period, null, "value", (201, 1_000)), Read(main, Now.AddMinutes(-30), Period, null, "value", (201, 1_010)));
        var live = Live([main, alts], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.RecordsPanel(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));

        // K0i2 saw 50 then 80 (+30), CCGP saw 1,000 then 1,010 (+10). The best is K0i2's 30, not 1,010 less 50.
        Assert.Equal(new FactModel("Biggest day", $"{AltOne.DisplayName} · {PanelText.Change(30, StatFormat.Number)}"), model.Facts.Single(f => f.Label == "Biggest day"));
        Assert.Equal(new FactModel("Fastest 7 days", $"{AltOne.DisplayName} · {PanelText.Change(30, StatFormat.Number)}"), model.Facts.Single(f => f.Label == "Fastest 7 days"));
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

    [Fact]
    public void RecordsReadDurationsAndOmitAMeaninglessGainForADate()
    {
        // Controller ruling: RecordsPanel's values read through the stat's format too. A duration's own
        // reading and its gains both read as a duration; a date's gain is meaningless, so it's a dash.
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var durationReader = Reader(
            Read(profile, Now.AddHours(-3), null, null, "playtime", (201, 10_000)),
            Read(profile, Now.AddHours(-2), null, null, "playtime", (201, 22_000)));
        var durationLive = Live([profile], [Installed(Profile, "playtime")], Snaps());

        var durations = PanelModels.RecordsPanel(durationLive, durationReader, new PanelSettings(Profile.Slug, Stat: "playtime"));

        Assert.Equal(new FactModel("Highest", $"{AltOne.DisplayName} · {PanelText.Duration(22_000)}"), durations.Facts[0]);
        Assert.Equal(new FactModel("Biggest day", $"{AltOne.DisplayName} · +3h 20m"), durations.Facts[1]);
        Assert.Equal(new FactModel("Fastest 7 days", $"{AltOne.DisplayName} · +3h 20m"), durations.Facts[2]);

        var dateReader = Reader(
            Read(profile, Now.AddHours(-3), null, null, "first-join", (201, 1_600_000_000)),
            Read(profile, Now.AddHours(-2), null, null, "first-join", (201, 1_600_003_600)));
        var dateLive = Live([profile], [Installed(Profile, "first-join")], Snaps());

        var dates = PanelModels.RecordsPanel(dateLive, dateReader, new PanelSettings(Profile.Slug, Stat: "first-join"));

        Assert.Equal(new FactModel("Highest", $"{AltOne.DisplayName} · {PanelText.Value(1_600_003_600, StatFormat.Date, TimeZoneInfo.Utc)}"), dates.Facts[0]);
        Assert.Equal(new FactModel("Biggest day", $"{AltOne.DisplayName} · {StatText.Dash}"), dates.Facts[1]);
        Assert.Equal(new FactModel("Fastest 7 days", $"{AltOne.DisplayName} · {StatText.Dash}"), dates.Facts[2]);
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
        Assert.Equal(new TopRow("below the list", "Outsider", StatText.Abbrev(5), false, true), model.Rows[11]);
    }

    /// <summary>
    /// A clan you WATCH keeps its place on this list and keeps its estimate row, and is not tinted as yours. Staying
    /// visible is the whole point of watching a clan, so the cut at <see cref="PanelModels.TopCount"/> still lets it
    /// through; the tint says "this is mine" and belongs to your own. The set behind the tint was role-blind until
    /// V3-S.33, so a watched rival read as one of yours here exactly as it did on the race chart. Ruled by the owner
    /// on 2026-09-20: keep the place, drop the tint.
    /// </summary>
    [Fact]
    public void TopKeepsAWatchedClanOnTheListWithoutCallingItYours()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var main = SourceOf("s-00000001", Clan, "G1", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "G12", SourceRole.Watch);
        var groups = Enumerable.Range(1, 14)
            .Select(i => new GroupRow($"G{i}", new Dictionary<string, double> { ["value"] = 1_000 - i * 10 }, i))
            .ToList();
        var live = Live([top, main, rival], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(top.Id, null, groups: groups), Snapshot(main.Id, [], [Points(990)]), Snapshot(rival.Id, [], [Points(880)])));

        var model = PanelModels.Top(live, new PanelSettings(TopList.Slug, SourceId: top.Id));

        // G12 is 12th, past the cut of 10, and is here only because it is watched. G11 and G13 are neither
        // yours nor watched, so they stay cut.
        var names = model.Rows.Select(r => r.Name).ToList();
        Assert.Contains("G12", names);
        Assert.DoesNotContain("G11", names);
        Assert.DoesNotContain("G13", names);

        Assert.False(model.Rows.Single(r => r.Name == "G12").Yours);
        Assert.True(model.Rows.Single(r => r.Name == "G1 ★").Yours);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileStatSaysNoEarlierReadForBothWindowsUntilThereAreTwoReadings(bool hasReading)
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var live = Live([profile], [Installed(Profile, "diamonds")],
            Snaps(Snapshot(profile.Id, [Row(Main.RobloxUserId, 500, "diamonds")])), accounts: [Main]);
        var reader = Reader();
        if (hasReading) reader.Apply(Read(profile, Now, null, null, "diamonds", (Main.RobloxUserId, 500)));

        var model = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "diamonds"));

        var row = Assert.Single(model.Rows);
        Assert.Equal("no earlier read", row.Today);
        Assert.Equal("no earlier read", row.Week);
        Assert.False(row.Missing);
    }

    [Theory]
    [InlineData(-5, "+300")]
    [InlineData(9, "+150")]
    public void ProfileStatUsesLocalMidnightForTodayAndARollingSevenDaysForWeek(int offsetHours, string today)
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-zone", TimeSpan.FromHours(offsetHours), "Test zone", "Test zone");
        var live = Live([profile], [Installed(Profile, "diamonds")],
            Snaps(Snapshot(profile.Id, [Row(Main.RobloxUserId, 500, "diamonds")])), accounts: [Main]) with
        {
            Time = new FixedTime(Now, zone),
        };
        var reader = Reader(
            Read(profile, Now.AddDays(-8), null, null, "diamonds", (Main.RobloxUserId, 50)),
            Read(profile, Now.AddDays(-7), null, null, "diamonds", (Main.RobloxUserId, 75)),
            Read(profile, Now.AddDays(-7).AddMinutes(1), null, null, "diamonds", (Main.RobloxUserId, 90)),
            Read(profile, Now.AddHours(-13).AddMinutes(-1), null, null, "diamonds", (Main.RobloxUserId, 150)),
            Read(profile, Now.AddHours(-13), null, null, "diamonds", (Main.RobloxUserId, 200)),
            Read(profile, Now.AddHours(-13).AddMinutes(1), null, null, "diamonds", (Main.RobloxUserId, 250)),
            Read(profile, Now.AddHours(-3).AddMinutes(-1), null, null, "diamonds", (Main.RobloxUserId, 300)),
            Read(profile, Now.AddHours(-3), null, null, "diamonds", (Main.RobloxUserId, 350)),
            Read(profile, Now.AddHours(-3).AddMinutes(1), null, null, "diamonds", (Main.RobloxUserId, 400)),
            Read(profile, Now, null, null, "diamonds", (Main.RobloxUserId, 500)));

        var model = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "diamonds"));

        var row = Assert.Single(model.Rows);
        Assert.Equal(today, row.Today);
        Assert.Equal("+425", row.Week);
    }

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
        Assert.Equal(new[] { "AshAlt", "BirchMain", "DuneAlt", "CedarLoose" }, model.Rows.Select(r => r.Name).ToArray());
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

    /// <summary>
    /// Backlog S1-13.7. A Profile stat pinned to a source that was removed quietly drew another source's numbers under the
    /// settings you chose, and a row whose value missed said "can't read", which names no cause. A pin that's gone says it's
    /// gone, a row carries the read's own miss, and a source that's off says it's off rather than leaving dashes unexplained.
    /// </summary>
    [Fact]
    public void AProfileStatNeverReadsAnotherSourceQuietlyAndARowCarriesItsOwnMiss()
    {
        var other = SourceOf("s-0000000b", Profile, null, SourceRole.Mine);
        const string Miss = "No 'Diamonds' in 'data.views.profile.data'. Keys present: Rank.";
        var snapshot = Snapshot(other.Id, [Row(AltOne.RobloxUserId, 5, "diamonds"), Row(Main.RobloxUserId, null, "diamonds")]) with
        {
            CellMisses = new Dictionary<(long UserId, string Stat), string> { [(Main.RobloxUserId, "diamonds")] = Miss },
        };
        var live = Live([other], [Installed(Profile, "diamonds")], Snaps(snapshot));

        var gone = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, SourceId: "s-gone0000", Stat: "diamonds"));
        Assert.Equal("This panel's source was removed.", gone.Head.Stale);
        Assert.Empty(gone.Rows);

        var pinned = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, SourceId: other.Id, Stat: "diamonds"));
        // The row says it plainly; the path and the keys that came back are Diagnostics' to show, not a panel's.
        Assert.Equal(PanelText.NotInLastRead, pinned.Rows.Single(r => r.Name == Main.DisplayName).Note);
        Assert.DoesNotContain("Keys present", pinned.Rows.Single(r => r.Name == Main.DisplayName).Note);
        Assert.Equal("", pinned.Head.Note);

        var off = other with { Enabled = false };
        var switchedOff = PanelModels.ProfileStat(Live([off], [Installed(Profile, "diamonds")], Snaps()), Reader(),
            new PanelSettings(Profile.Slug, SourceId: off.Id, Stat: "diamonds"));
        Assert.Equal($"{live.SourceName(off)} is switched off, so it isn't read.", switchedOff.Head.Note);
    }

    [Fact]
    public void AProfileStatOfPlaytimeReadsAsADuration()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id, [Row(Main.RobloxUserId, 50_651_629, "playtime")]);
        var reader = Reader(
            Read(profile, Now.AddDays(-8), null, null, "playtime", (Main.RobloxUserId, 50_644_429)),
            Read(profile, Now.AddMinutes(-5), null, null, "playtime", (Main.RobloxUserId, 50_651_629)));
        var live = Live([profile], [Installed(Profile, "playtime")], Snaps(snapshot));

        var row = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "playtime")).Rows[0];

        Assert.Equal("586d 5h", row.Value);
        Assert.Equal("+2h 0m", row.Today);
    }

    [Fact]
    public void AGainNeedsTwoReadings()
    {
        Assert.Null(PanelModels.Gain([]));
        Assert.Null(PanelModels.Gain([new SeriesPoint(Now, 5, null, false, 0)]));
        Assert.Equal(7, PanelModels.Gain([new SeriesPoint(Now.AddHours(-1), 5, null, false, 0), new SeriesPoint(Now, 12, null, false, 0)]));
    }

    // ---- Accounts table ----

    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    /// <summary>Your four accounts on the profile recipe: Main and AltOne read, Loose read without diamonds, AltTwo private.</summary>
    private static LiveBoard ProfileLive(params string[] shown) => Live([ProfileSource], [Installed(Profile, shown)], Snaps(Snapshot(ProfileSource.Id,
    [
        new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 215_850_364, ["rank"] = 37, ["playtime"] = 50_651_629 }),
        new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 3_957_873_882, ["rank"] = 12, ["playtime"] = 3_600 }),
        new RecipeRow(Loose.RobloxUserId, new Dictionary<string, double> { ["rank"] = 5 }),
    ]) with
    {
        Unavailable = new Dictionary<long, string> { [AltTwo.RobloxUserId] = "Profile is private." },
    }));

    private static ScoreBookReader DiamondsBook() => Reader(
        Read(ProfileSource, Now.AddDays(-8), null, null, "diamonds", (Main.RobloxUserId, 200_000_000)),
        Read(ProfileSource, Now.AddMinutes(-5), null, null, "diamonds", (Main.RobloxUserId, 215_850_364)));

    private static readonly PanelSettings TableSettings = new(Profile.Slug, SourceId: "s-00000009");

    [Fact]
    public void TheAccountsTableHasAColumnPerShownStatSortedByTheFirstWithItsChangeAndATotal()
    {
        var model = PanelModels.AccountsTable(ProfileLive("diamonds", "rank", "playtime"), DiamondsBook(), TableSettings);

        Assert.Equal(new[] { "Account", "Diamonds ↓", "Today", "7 days", "Player rank", "Playtime" }, model.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { AltOne.DisplayName, Main.DisplayName, Loose.DisplayName, AltTwo.DisplayName, "Total" }, model.Rows.Select(r => r.Name).ToArray());

        // Highest first; a missing value sorts last; the change comes from the book, a dash with fewer than two readings.
        var main = model.Rows[1];
        Assert.Equal(new[] { Main.DisplayName, "215,850,364", PanelText.Signed(15_850_364), PanelText.Signed(15_850_364), "37", "586d 5h" }, main.Cells);
        Assert.Equal(new[] { AltOne.DisplayName, "3,957,873,882", StatText.Dash, StatText.Dash, "12", "1h 0m" }, model.Rows[0].Cells);
        Assert.False(model.Rows[2].Missing);

        // An account the read couldn't reach says why, plainly, with dashes.
        var hidden = model.Rows[3];
        Assert.Equal(("Profile is private.", true), (hidden.Note, hidden.Missing));
        Assert.All(hidden.Cells.Skip(1), cell => Assert.Equal(StatText.Dash, cell));

        // The total adds up what adds up (diamonds, playtime); a rank and the change columns stay blank.
        var total = model.Rows[^1];
        Assert.True(total.IsTotal);
        Assert.Equal(new[] { "Total", "4,173,724,246", "", "", "", "586d 6h" }, total.Cells);
        Assert.Equal("", model.Head.Note);
    }

    [Fact]
    public void AClickedHeadingSortsItsColumnAndTheChangeFollowsIt()
    {
        var live = ProfileLive("diamonds", "rank", "playtime");

        var byRank = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort("rank", Descending: false));
        Assert.Equal(new[] { "Account", "Diamonds", "Player rank ↑", "Today", "7 days", "Playtime" }, byRank.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { Loose.DisplayName, AltOne.DisplayName, Main.DisplayName, AltTwo.DisplayName, "Total" }, byRank.Rows.Select(r => r.Name).ToArray());

        // The sorted heading flips; another stat sorts highest first; Account sorts A to Z; the change columns don't sort.
        Assert.Equal(new AccountSort("rank", Descending: true), AccountSort.Clicked(byRank.Columns[2]));
        Assert.Equal(new AccountSort("diamonds", Descending: true), AccountSort.Clicked(byRank.Columns[1]));
        Assert.Equal(new AccountSort(AccountSort.NameKey, Descending: false), AccountSort.Clicked(byRank.Columns[0]));
        Assert.False(byRank.Columns[3].CanSort);

        var byName = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort(AccountSort.NameKey, Descending: false));
        Assert.Equal(new[] { "Account ↑", "Diamonds", "Player rank", "Playtime" }, byName.Columns.Select(c => c.Heading).ToArray());
        Assert.Equal(new[] { AltOne.DisplayName, Main.DisplayName, Loose.DisplayName, AltTwo.DisplayName, "Total" }, byName.Rows.Select(r => r.Name).ToArray());

        // A sort on a stat you no longer show falls back to the first.
        var stale = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort("eggs", Descending: false));
        Assert.Equal("Diamonds ↓", stale.Columns[1].Heading);
    }

    [Fact]
    public void TheAccountsTableMarksThePickAndSaysWhatItIsWaitingFor()
    {
        var picked = PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), TableSettings, pickedUserId: AltOne.RobloxUserId);
        Assert.Equal(new[] { AltOne.RobloxUserId }, picked.Rows.Where(r => r.Picked).Select(r => r.UserId).ToArray());

        var unread = PanelModels.AccountsTable(Live([ProfileSource], [Installed(Profile, "diamonds")], Snaps()), DiamondsBook(), TableSettings);
        Assert.Equal("Waiting for the first read.", unread.Head.Note);
        Assert.All(unread.Rows.Where(r => !r.IsTotal), row => Assert.True(row.Missing));

        var nothingShown = PanelModels.AccountsTable(ProfileLive(), DiamondsBook(), TableSettings);
        Assert.Equal("Tick Show on a stat to fill this panel.", nothingShown.Head.Note);
        Assert.Equal(new[] { "Account ↑" }, nothingShown.Columns.Select(c => c.Heading).ToArray());
        Assert.DoesNotContain(nothingShown.Rows, r => r.IsTotal);

        // A pinned source that's gone is stale; an unpinned table reads the recipe's first source that is on.
        Assert.True(PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), new PanelSettings(Profile.Slug, SourceId: "s-gone")).Head.HasStale);
        Assert.False(PanelModels.AccountsTable(ProfileLive("diamonds"), DiamondsBook(), new PanelSettings(Profile.Slug)).Head.HasStale);
    }

    [Fact]
    public void AnAccountsTableOnAListRecipeShowsOnlyYourAccountsNeverTheOtherPlayersItRead()
    {
        // Global Constraint 2 (final review Minor 8): a list recipe's read holds other members' rows; the table's rows, cells
        // and total come from your accounts alone.
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id,
            [Row(5, 20_000_000), Row(Main.RobloxUserId, 14_020_550), Row(6, 1_000), Row(AltOne.RobloxUserId, 12_418_220), Row(7, 999_999_999)])));

        var model = PanelModels.AccountsTable(live, Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(new[] { Main.RobloxUserId, AltOne.RobloxUserId, AltTwo.RobloxUserId, Loose.RobloxUserId }, model.Rows.Where(r => !r.IsTotal).Select(r => r.UserId).Order().ToArray());
        Assert.Equal(new[] { Main.DisplayName, AltOne.DisplayName, AltTwo.DisplayName, Loose.DisplayName, "Total" }.Order(), model.Rows.Select(r => r.Name).Order());
        Assert.DoesNotContain(model.Rows.SelectMany(r => r.Cells), cell => cell is "20,000,000" or "1,000" or "999,999,999");
        Assert.Equal("26,438,770", model.Rows.Single(r => r.IsTotal).Cells[1]);
    }

    [Fact]
    public void AnUnpinnedTableAndProfileStatReadTheRecipesFirstSourceThatIsOnElseItsFirst()
    {
        // Final review Minors 3 and 4: one rule for the recipe's source (PanelForms.FirstSourceOfRecipe). The first source
        // is off and the second is on and read, so both panels read the second, and the table isn't waiting for anything.
        var off = SourceOf("s-00000009", Profile, null, SourceRole.Mine) with { Enabled = false };
        var on = SourceOf("s-0000000b", Profile, null, SourceRole.Mine);
        var live = Live([off, on], [Installed(Profile, "diamonds")], Snaps(Snapshot(on.Id, [Row(Main.RobloxUserId, 215_850_364, "diamonds")])));

        var table = PanelModels.AccountsTable(live, Reader(), new PanelSettings(Profile.Slug));
        var stat = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.Equal("", table.Head.Note);
        Assert.Equal("215,850,364", table.Rows.Single(r => r.UserId == Main.RobloxUserId).Cells[1]);
        Assert.Equal("215,850,364", stat.Rows.Single(r => r.Name == Main.DisplayName).Value);

        // With no source on, both read the recipe's first; the table says it is switched off rather than waiting for a read.
        var allOff = Live([off], [Installed(Profile, "diamonds")], Snaps());

        var offTable = PanelModels.AccountsTable(allOff, Reader(), new PanelSettings(Profile.Slug));
        var offStat = PanelModels.ProfileStat(allOff, Reader(), new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.False(offTable.Head.HasStale);
        Assert.Equal($"{allOff.SourceName(off)} is switched off, so it isn't read.", offTable.Head.Note);
        Assert.False(offStat.Head.HasStale);
    }

    // ---- Live leaderboard ----

    [Fact]
    public void TheLiveLeaderboardMarksYourAccountsAndUsesNamesHeldInMemory()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id, [Row(7, 3), Row(5, 20), Row(101, 14)])));

        var model = PanelModels.LiveLeaderboard(live, new PanelSettings(Clan.Slug, SourceId: main.Id), new Dictionary<long, string> { [5] = "Rival" });

        Assert.Equal(new[] { "Points" }, model.Columns.ToArray());
        Assert.Equal(new[] { "Rival", "BirchMain", "Member 7" }, model.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { false, true, false }, model.Rows.Select(r => r.Yours).ToArray());
        Assert.Equal("Live only. Never saved.", model.Head.Note);
    }

    [Fact]
    public void TheLiveLeaderboardWritesEachStatInItsFormat()
    {
        // Final review Minor 1: no list recipe has a format yet, so the profile recipe stands in for one that does.
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var live = Live([profile], [Installed(Profile, "diamonds", "playtime", "first-join")], Snaps(Snapshot(profile.Id,
        [
            new RecipeRow(Main.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 215_850_364, ["playtime"] = 50_651_629, ["first-join"] = 1_600_000_000 }),
            new RecipeRow(AltOne.RobloxUserId, new Dictionary<string, double> { ["diamonds"] = 1_000 }),
        ])));

        var model = PanelModels.LiveLeaderboard(live, new PanelSettings(Profile.Slug, SourceId: profile.Id), new Dictionary<long, string>());

        Assert.Equal(new[] { "215,850,364", "586d 5h", "13 Sep 2020" }, model.Rows.Single(r => r.Name == Main.DisplayName).Cells);
        Assert.Equal(new[] { "1,000", StatText.Dash, StatText.Dash }, model.Rows.Single(r => r.Name == AltOne.DisplayName).Cells);
    }

    // ---- Your own accounts' pictures ----

    [Fact]
    public void YourOwnAccountsRowsCarryTheirPictureAndNobodyElsesDoes()
    {
        var pictures = new Dictionary<long, string>
        {
            [Main.RobloxUserId] = @"C:\cache\avatar-101.png",
            [999] = @"C:\cache\avatar-999.png",
        };
        var live = ProfileLive("diamonds", "rank") with { Avatars = pictures };

        var table = PanelModels.AccountsTable(live, DiamondsBook(), TableSettings, new AccountSort(AccountSort.NameKey, Descending: false));
        var mine = PanelModels.MyAccounts(live, DiamondsBook(), new PanelSettings(Profile.Slug, Stat: "diamonds"));
        var card = PanelModels.AccountCard(live, DiamondsBook(), new PanelSettings(Profile.Slug, Stat: "diamonds", UserId: Main.RobloxUserId));

        Assert.Equal(@"C:\cache\avatar-101.png", table.Rows.First(r => r.Name == Main.DisplayName).Avatar);
        Assert.Null(table.Rows.First(r => r.Name == AltOne.DisplayName).Avatar);
        Assert.True(table.Rows[^1].IsTotal);
        Assert.Null(table.Rows[^1].Avatar);
        Assert.Equal(@"C:\cache\avatar-101.png", mine.Groups.SelectMany(g => g.Rows).First(r => r.UserId == Main.RobloxUserId).Avatar);
        Assert.Equal(@"C:\cache\avatar-101.png", card.Avatar);

        // A picture for anyone but you is never drawn, whatever the map holds.
        Assert.Null(live.AvatarFor(999));
        Assert.Null(live.AvatarFor(0));
    }

    [Fact]
    public void ThePromotionCheckShowsYourPictureBesideEachAccountItWouldPlace()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([main, alts], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [Row(5, 900), Row(6, 700)]), Snapshot(alts.Id, [Row(Main.RobloxUserId, 800)]))) with
        {
            Avatars = new Dictionary<long, string> { [Main.RobloxUserId] = @"C:\cache\avatar-101.png" },
        };

        var model = PanelModels.PromotionCheck(live, new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value"));

        Assert.Equal(@"C:\cache\avatar-101.png", Assert.Single(model.Rows).Avatar);
    }

    // ---- The last numbers the score book kept ----

    /// <summary>Plan A41: a panel drawing numbers from the score book says so, in every panel that can.</summary>
    [Fact]
    public void EveryPanelThatDrawsRememberedNumbersSaysSoInItsHead()
    {
        var mainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var kept = Remembered.From(
            Read(mainClan, Now.AddHours(-3), Period, Headline(99), "value", (Main.RobloxUserId, 4200)),
            mainClan, Clan, LiveBoard.UserIdsOf(Accounts))!;
        var installed = Installed(Clan, "value");
        var live = Live([mainClan], [installed], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = kept });
        var reader = Reader();
        var settings = new PanelSettings(Clan.Slug, mainClan.Id, Stat: "value");

        Assert.True(PanelModels.Standing(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.Race(live, reader, settings with { SourceIds = [mainClan.Id] }).Head.Remembered);
        Assert.True(PanelModels.MyAccounts(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.AccountCard(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.ProfileStat(live, reader, settings).Head.Remembered);
        Assert.True(PanelModels.AccountsTable(live, reader, settings).Head.Remembered);

        // And the same six say nothing when the numbers were read this session.
        var read = Live([mainClan], [installed],
            new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = Snapshot(mainClan.Id, [Row(Main.RobloxUserId, 4200)], [Points(99)]) });
        Assert.False(PanelModels.Standing(read, reader, settings).Head.Remembered);
        Assert.False(PanelModels.AccountsTable(read, reader, settings).Head.Remembered);

        // And the mark goes with the numbers it marked: the kept ones are still in hand when the first read lands, so
        // this is the case the board is actually in a second after Start. The state line's sentence goes with them.
        var replaced = read with { Remembered = new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = kept } };
        Assert.False(PanelModels.Standing(replaced, reader, settings).Head.Remembered);
        Assert.False(PanelModels.AccountsTable(replaced, reader, settings).Head.Remembered);
        Assert.True(PanelModels.Standing(replaced, reader, settings).HasAccounts);
        Assert.Null(replaced.OldestRemembered);
    }

    /// <summary>Plan A40: the book never kept another member, so a panel that shows them waits for a real read.</summary>
    [Fact]
    public void ThePanelsThatShowOtherMembersNeverDrawRememberedNumbers()
    {
        var mainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var kept = Remembered.From(
            Read(mainClan, Now.AddHours(-3), Period, Headline(99), "value", (Main.RobloxUserId, 4200)),
            mainClan, Clan, LiveBoard.UserIdsOf(Accounts))!;
        var live = Live([mainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(),
            remembered: new Dictionary<string, RecipeSnapshot> { [mainClan.Id] = kept });
        var settings = new PanelSettings(Clan.Slug, mainClan.Id, Stat: "value");

        Assert.Empty(PanelModels.LiveLeaderboard(live, settings, new Dictionary<long, string>()).Rows);
        Assert.Empty(PanelModels.PromotionCheck(live, settings with { ToSourceId = mainClan.Id }).Rows);
        Assert.False(PanelModels.LiveLeaderboard(live, settings, new Dictionary<long, string>()).Head.Remembered);

        // And Standing never turns your own four accounts into "4 of 4" of a clan it did not read.
        Assert.False(PanelModels.Standing(live, Reader(), settings).HasAccounts);
    }

    /// <summary>
    /// What is left of backlog S1-F.6 once 7ee4afd kept the book's numbers through a read that stops. Live leaderboard, Top and
    /// Promotion check show other members, whom the score book never keeps (A40), so after a read that brought nothing back they
    /// have nothing true to draw and stay empty. The leaderboard said nothing about why, and Top said "Waiting for the first read."
    /// after a read had happened. Each says the last read brought nothing back; before any read they still say they are waiting.
    /// </summary>
    [Fact]
    public void ALivePanelWhoseLastReadBroughtNothingBackSaysSo()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        InstalledRecipe[] installed = [Installed(Clan, "value"), Installed(TopList)];
        var leaderboard = new PanelSettings(Clan.Slug, SourceId: main.Id);
        var topList = new PanelSettings(TopList.Slug, SourceId: top.Id);
        var promotion = new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value");

        var stopped = Live([top, main, alts], installed, Snaps(Idle(top.Id), Idle(main.Id), Snapshot(alts.Id, [Row(201, 12)])));
        var board = PanelModels.LiveLeaderboard(stopped, leaderboard, new Dictionary<long, string>());
        Assert.Empty(board.Rows);
        Assert.Equal("Live only. Never saved. The last read brought nothing back.", board.Head.Note);
        Assert.Equal("The last read brought nothing back.", PanelModels.Top(stopped, topList).Head.Note);
        Assert.Equal("The last read of CCGP brought nothing back.", PanelModels.PromotionCheck(stopped, promotion).Head.Note);

        var unread = Live([top, main, alts], installed, Snaps(Snapshot(alts.Id, [Row(201, 12)])));
        Assert.Equal("Live only. Never saved.", PanelModels.LiveLeaderboard(unread, leaderboard, new Dictionary<long, string>()).Head.Note);
        Assert.Equal("Waiting for the first read.", PanelModels.Top(unread, topList).Head.Note);
        Assert.Equal("Waiting for a read of CCGP.", PanelModels.PromotionCheck(unread, promotion).Head.Note);

        // Your side came back empty but the other clan's did not: its lowest is still true, and still shown.
        var yoursEmpty = Live([top, main, alts], installed, Snaps(Idle(alts.Id), Snapshot(main.Id, [Row(301, 40), Row(302, 7)])));
        var check = PanelModels.PromotionCheck(yoursEmpty, promotion);
        Assert.Equal("The last read of K0i2 brought nothing back.", check.Head.Note);
        Assert.Equal("CCGP's lowest now", check.LowestLabel);
        Assert.NotEqual(StatText.Dash, check.Lowest);
        Assert.Empty(check.Rows);
    }
}
