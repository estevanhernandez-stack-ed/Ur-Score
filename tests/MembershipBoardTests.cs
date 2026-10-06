using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// Backlog V3-S.20 on the board: a clan's members list says who is in it, and battle contributions are the numbers. The owner
/// saw "CCGP · ELeonDog, CECPapa" over a clan holding five of his accounts, because only the two who had scored were placed.
/// A clan whose list hasn't been read falls back to contributions, as before.
/// </summary>
public class MembershipBoardTests
{
    private static readonly Source Main_ = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source Alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Watched = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);

    private static Dictionary<string, IReadOnlySet<long>> Members(params (Source Source, long[] Ids)[] clans) =>
        clans.ToDictionary(c => c.Source.Id, c => (IReadOnlySet<long>)c.Ids.ToHashSet());

    private static Dictionary<string, RecipeSnapshot> Reads(params RecipeSnapshot[] snapshots) =>
        snapshots.ToDictionary(s => s.SourceId!, StringComparer.Ordinal);

    private static MyAccountsModel MyAccounts(
        IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> reads, IReadOnlyDictionary<string, IReadOnlySet<long>>? members) =>
        PanelModels.MyAccounts(Live(sources, [Installed(Clan, "value")], reads, members: members), Reader(), new PanelSettings(Clan.Slug, Stat: "value"));

    /// <summary>A member who hasn't scored this battle sits in its clan's group with the no-value dash, below the ones who have.</summary>
    [Fact]
    public void AMemberWhoHasNotScoredIsInItsClansGroupNotNotFound()
    {
        var read = Snapshot(Main_.Id, [Row(Main.RobloxUserId, 40), Row(987654321001, 90)]);
        var model = MyAccounts([Main_], Reads(read), Members((Main_, [Main.RobloxUserId, AltOne.RobloxUserId, AltTwo.RobloxUserId])));

        var ccgp = model.Groups[0];
        Assert.Equal("★ CCGP", ccgp.Heading);
        Assert.Equal(new[] { Main.DisplayName, AltOne.DisplayName, AltTwo.DisplayName }, ccgp.Rows.Select(r => r.Name));
        var quiet = ccgp.Rows.Single(r => r.UserId == AltOne.RobloxUserId);
        Assert.Equal((StatText.Dash, StatText.Dash, true), (quiet.Value, quiet.InGroup, quiet.Missing));

        // Only Loose, on no clan's list, is left over; with every clan's list read that is a fact, not "read so far".
        Assert.Equal(new[] { "★ CCGP", "Not in a watched clan" }, model.Groups.Select(g => g.Heading));
        Assert.Equal(Loose.DisplayName, Assert.Single(model.Groups[1].Rows).Name);
    }

    /// <summary>Between battles there are no contributions at all, and the members lists still place every account.</summary>
    [Fact]
    public void BetweenBattlesTheMembersListsStillGroupYourAccounts()
    {
        var model = MyAccounts([Main_, Alts, Watched], Reads(Idle(Main_.Id), Idle(Alts.Id), Idle(Watched.Id)),
            Members((Main_, [Main.RobloxUserId, AltOne.RobloxUserId]), (Alts, [AltTwo.RobloxUserId]), (Watched, [Loose.RobloxUserId])));

        Assert.Equal(new[] { "★ CCGP", "K0i2", "Only in clans you're watching" }, model.Groups.Select(g => g.Heading));
        Assert.Equal(2, model.Groups[0].Rows.Count);
        Assert.All(model.Groups.SelectMany(g => g.Rows), r => Assert.Equal(StatText.Dash, r.Value));
    }

    /// <summary>Before any read this session, the members kept from last time still group the board.</summary>
    [Fact]
    public void MembersKeptFromLastTimeGroupTheBoardBeforeTheFirstRead()
    {
        var model = MyAccounts([Main_], Reads(), Members((Main_, [Main.RobloxUserId, AltOne.RobloxUserId, AltTwo.RobloxUserId, Loose.RobloxUserId])));

        Assert.Equal("★ CCGP", Assert.Single(model.Groups).Heading);
        Assert.Equal(4, model.Groups[0].Rows.Count);
    }

    /// <summary>A clan whose members list hasn't been read is grouped by contributions, exactly as before.</summary>
    [Fact]
    public void AClanWithNoMembersReadFallsBackToContributions()
    {
        var read = Snapshot(Main_.Id, [Row(Main.RobloxUserId, 40)]);
        var model = MyAccounts([Main_, Alts], Reads(read, Snapshot(Alts.Id, [Row(AltOne.RobloxUserId, 5)])), Members((Alts, [AltOne.RobloxUserId, AltTwo.RobloxUserId])));

        Assert.Equal(Main.DisplayName, Assert.Single(model.Groups[0].Rows).Name);
        Assert.Equal(new[] { AltOne.DisplayName, AltTwo.DisplayName }, model.Groups[1].Rows.Select(r => r.Name));
    }

    /// <summary>
    /// The chip follows the members list (V3-S.19's rule, now with evidence between battles too): a clan added as yours whose list
    /// holds none of your accounts is one you are watching, and one whose list holds one is yours whatever the battle read says.
    /// </summary>
    [Fact]
    public void TheChipFollowsTheMembersList()
    {
        SourceRole Chip(IReadOnlyDictionary<string, RecipeSnapshot> reads, IReadOnlyDictionary<string, IReadOnlySet<long>>? members) =>
            Live([Alts], [Installed(Clan, "value")], reads, members: members).ChipRole(Alts);

        // Between battles: the list decides, where the battle read had no evidence either way.
        Assert.Equal(SourceRole.Watch, Chip(Reads(Idle(Alts.Id)), Members((Alts, []))));
        Assert.Equal(SourceRole.Mine, Chip(Reads(Idle(Alts.Id)), Members((Alts, [AltOne.RobloxUserId]))));
        Assert.Equal(SourceRole.Watch, Chip(Reads(), Members((Alts, []))));

        // In a battle where none of the list's accounts has scored yet: still yours.
        Assert.Equal(SourceRole.Mine, Chip(Reads(Snapshot(Alts.Id, [Row(987654321001, 50)])), Members((Alts, [AltOne.RobloxUserId]))));

        // No list read: the battle read decides, as before.
        Assert.Equal(SourceRole.Watch, Chip(Reads(Snapshot(Alts.Id, [Row(987654321001, 50)])), null));
        Assert.Equal(SourceRole.Mine, Chip(Reads(Idle(Alts.Id)), null));

        // A main clan is main whatever its list says.
        Assert.Equal(SourceRole.Main, Live([Main_], [Installed(Clan, "value")], Reads(), members: Members((Main_, []))).ChipRole(Main_));
    }
}
