using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// Backlog V3-S.20 in Setup: a clan row says who is in the clan from its members list, with how many of them are scoring this
/// battle as a detail ("CCGP · 5 accounts: ... · 2 scoring this battle"); Setup › Your accounts places an account the same way;
/// and the requests line counts the members reads, one per clan per half hour.
/// </summary>
public class MembershipSetupTests
{
    private const string ClanSlug = "pet-sim-99-clan-battle-points";

    private static Recipe Clan => BuiltInRecipes.BySlug[ClanSlug];

    private static HostAccount Account(int n, string name) => new(Guid.Parse($"{n:D8}-0000-0000-0000-000000000000"), 100 + n, name);

    private static readonly HostAccount[] Owners =
        [Account(1, "ELeonDog"), Account(2, "CECPapa"), Account(3, "ItsjustesteAgain"), Account(4, "PapasbbBri"), Account(5, "ItsJustEste")];

    private static Source ClanSource(string id, string clan, SourceRole role, bool enabled = true) =>
        new(id, ClanSlug, new Dictionary<string, string> { ["clan"] = clan }, role, enabled);

    private static readonly Source Ccgp = ClanSource("s-00000001", "CCGP", SourceRole.Main);

    private static RecipeSnapshot Read(params long[] userIds) =>
        new(WatchState.Reporting, null, [], [], userIds.Length, "battle=A", [.. userIds.Select(id => RecipeEngineTests.Row(id, 10))], []);

    private static RecipeSnapshot Idle => new(WatchState.SourceIdle, "No clan battle running", [], [], 0);

    private static IReadOnlySet<long> Ids(params HostAccount[] accounts) => accounts.Select(a => a.RobloxUserId).ToHashSet();

    /// <summary>The owner's screen: five of his accounts on CCGP's list, two of them scoring.</summary>
    [Fact]
    public void AClanRowNamesEveryMemberAndCountsWhoIsScoring()
    {
        var read = Read(Owners[0].RobloxUserId, Owners[1].RobloxUserId, 987654321001);

        Assert.Equal("5 accounts: ELeonDog, CECPapa, ItsjustesteAgain, PapasbbBri, ItsJustEste · 2 scoring this battle",
            ClansModel.Who(Clan, Ccgp, read, Owners, Ids(Owners)));

        // Through Lists too, which is what the page draws.
        var lists = ClansModel.Lists(Clan, [Ccgp], new Dictionary<string, RecipeSnapshot> { [Ccgp.Id] = read }, Owners,
            new Dictionary<string, IReadOnlySet<long>> { [Ccgp.Id] = Ids(Owners) });
        Assert.StartsWith("5 accounts: ", lists.Main!.Who);
    }

    [Fact]
    public void TheScoringDetailIsSaidOnlyWhenThereIsABattleRead()
    {
        Assert.Equal("1 account: ELeonDog · 1 scoring this battle", ClansModel.Who(Clan, Ccgp, Read(Owners[0].RobloxUserId), Owners, Ids(Owners[0])));
        Assert.Equal("2 accounts: ELeonDog, CECPapa · none scoring this battle", ClansModel.Who(Clan, Ccgp, Read(987654321001), Owners, Ids(Owners[0], Owners[1])));

        // Between battles, or not read this session: no rows, so nothing to say about scoring.
        Assert.Equal("2 accounts: ELeonDog, CECPapa", ClansModel.Who(Clan, Ccgp, Idle, Owners, Ids(Owners[0], Owners[1])));
        Assert.Equal("2 accounts: ELeonDog, CECPapa", ClansModel.Who(Clan, Ccgp, null, Owners, Ids(Owners[0], Owners[1])));
    }

    [Fact]
    public void ALongListEndsWithHowManyMore()
    {
        HostAccount[] eight = [.. Owners, Account(6, "Six"), Account(7, "Seven"), Account(8, "Eight")];

        Assert.Equal("8 accounts: ELeonDog, CECPapa, ItsjustesteAgain, PapasbbBri and 4 more", ClansModel.Who(Clan, Ccgp, null, eight, Ids(eight)));
    }

    [Fact]
    public void AListWithNoneOfYoursSaysSoAndNoListFallsBackToTheBattleRead()
    {
        var k0i2 = ClanSource("s-00000002", "K0i2", SourceRole.Mine);
        Assert.Equal("None of your accounts are members", ClansModel.Who(Clan, k0i2, Idle, Owners, new HashSet<long>()));

        // No list read: what the battle read found, exactly as before.
        Assert.Equal("ELeonDog", ClansModel.Who(Clan, k0i2, Read(Owners[0].RobloxUserId), Owners, null));
        Assert.Equal("Not read yet", ClansModel.Who(Clan, k0i2, null, Owners, null));

        // A watched clan is never matched to your accounts, list or not.
        Assert.Equal("clan-level numbers only", ClansModel.Who(Clan, ClanSource("s-00000003", "NovaForge", SourceRole.Watch), null, Owners, Ids(Owners)));
    }

    /// <summary>Setup › Your accounts places an account by the members lists too, scored or not, and in the same words as the board.</summary>
    [Fact]
    public void YourAccountsPlacesAMemberWhoHasNotScored()
    {
        var installed = new InstalledRecipe(Clan, "", new RecipeState());
        var k0i2 = ClanSource("s-00000002", "K0i2", SourceRole.Mine);
        var latest = new Dictionary<string, RecipeSnapshot> { [Ccgp.Id] = Read(Owners[0].RobloxUserId), [k0i2.Id] = Idle };
        var members = new Dictionary<string, IReadOnlySet<long>> { [Ccgp.Id] = Ids(Owners[0], Owners[1]), [k0i2.Id] = Ids(Owners[2]) };

        Assert.Equal("★ CCGP", AccountsModel.FoundIn(Owners[1], [installed], [Ccgp, k0i2], latest, members));
        Assert.Equal("K0i2", AccountsModel.FoundIn(Owners[2], [installed], [Ccgp, k0i2], latest, members));

        // On neither list, with both lists read: that is a fact now, not "read so far".
        Assert.Equal("Not in a watched clan", AccountsModel.FoundIn(Owners[3], [installed], [Ccgp, k0i2], latest, members));

        // And the rows the page draws carry it.
        var rows = AccountsModel.Rows(Owners, [installed], [Ccgp, k0i2], latest, members: members);
        Assert.Equal("★ CCGP", rows.Single(r => r.DisplayName == "CECPapa").FoundIn);
    }

    /// <summary>One members read per enabled clan per half hour (Membership.Every) on the members list's host, and none with no accounts to look for.</summary>
    [Fact]
    public void TheRequestsLineCountsTheMembersReads()
    {
        var installed = new InstalledRecipe(Clan, "", new RecipeState());
        Source[] sources = [Ccgp, ClanSource("s-00000002", "K0i2", SourceRole.Mine), ClanSource("s-00000003", "Resting", SourceRole.Watch, enabled: false)];

        double Battle(int accounts) => 2 * Clan.Steps.Sum(s => s.PerAccount ? accounts : 1) * 3600.0 / Clan.EffectiveEverySeconds;
        var members = 2 * 3600.0 / Membership.Every.TotalSeconds;
        Assert.Equal(new HostRequests("ps99.biggamesapi.io", (int)Math.Round(Battle(8) + members)), Assert.Single(ClansModel.RequestsPerHour(sources, [installed], accountCount: 8)));
        Assert.Equal(new HostRequests("ps99.biggamesapi.io", (int)Math.Round(Battle(0))), Assert.Single(ClansModel.RequestsPerHour(sources, [installed], accountCount: 0)));
    }
}
