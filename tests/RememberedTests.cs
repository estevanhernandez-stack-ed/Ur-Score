using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The numbers the window shows before its first read. These pin what a remembered snapshot may carry (your own
/// accounts and the source's own headline, nothing else), what makes one refuse to be built, and that every one is
/// stamped — the stamp is the only thing downstream has to tell it from a read.
/// </summary>
public class RememberedTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);

    private static readonly IReadOnlySet<long> Yours = LiveBoard.UserIdsOf(Accounts);

    private static BookLine Kept(DateTimeOffset at, params (long UserId, double Value)[] rows) =>
        Read(MainClan, at, "AutumnBattle", new Dictionary<string, double> { ["clan-points"] = 14_020_550 }, "value", rows);

    /// <summary>
    /// A kept reading carrying what the book keeps beside each value: the account's place among EVERY row the source
    /// read, and how many rows that was. A null place is an account the reading kept no place for.
    /// </summary>
    private static BookLine Ranked(DateTimeOffset at, int of, params (long UserId, double Value, int? Rank)[] rows) =>
        Kept(at, [.. rows.Select(r => (r.UserId, r.Value))]) with
        {
            Accounts = rows.ToDictionary(
                r => r.UserId.ToString(CultureInfo.InvariantCulture),
                r => new BookAccount(
                    new Dictionary<string, double> { ["value"] = r.Value },
                    r.Rank is { } rank ? new Dictionary<string, int> { ["value"] = rank } : null,
                    of),
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
        var kept = Remembered.From(Ranked(Now.AddHours(-3), of: 50, (Main.RobloxUserId, 4200, 7), (AltOne.RobloxUserId, 900, null)), MainClan, Clan, Yours)!;

        Assert.Equal(new RankInGroup(7, 50), kept.RememberedRanks[(Main.RobloxUserId, "value")]);

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
        var kept = Remembered.From(Ranked(Now.AddHours(-3), of: 50, (Main.RobloxUserId, 4200, 7), (AltOne.RobloxUserId, 900, null)), MainClan, Clan, Yours)!;
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
