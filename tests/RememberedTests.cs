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
}
