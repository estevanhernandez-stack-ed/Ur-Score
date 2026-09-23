using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ClansModelTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "BirchMain");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "AshAlt");
    private static readonly HostAccount Waiting = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, "New Alt");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Source ClanSource(string id, string clan, SourceRole role, bool enabled = true) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, role, enabled);

    private static RecipeSnapshot Read(params long[] userIds) =>
        new(WatchState.NoMatches, null, [], [], userIds.Length, "battle=A", [.. userIds.Select(id => RecipeEngineTests.Row(id, 10))], []);

    [Fact]
    public void ListsPutTheMainFirstAndKeepWatchedClansApart()
    {
        Source[] sources =
        [
            ClanSource("s-00000002", "K0i2", SourceRole.Mine),
            ClanSource("s-00000003", "NovaForge", SourceRole.Watch),
            ClanSource("s-00000001", "CCGP", SourceRole.Main),
        ];
        var latest = new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Read(101, 999), ["s-00000002"] = Read(888) };

        var lists = ClansModel.Lists(Clan, sources, latest, [Main, Alt]);

        Assert.Equal("CCGP", lists.Main!.Name);
        Assert.Equal(new[] { "CCGP", "K0i2" }, lists.Mine.Select(r => r.Name).ToArray());
        Assert.Equal("BirchMain", lists.Mine[0].Who);
        Assert.Equal("None of your accounts found in the last read", lists.Mine[1].Who);
        Assert.Equal("★ main", lists.Mine[0].Chip);
        Assert.False(lists.Mine[0].CanMakeMain);
        Assert.True(lists.Mine[1].CanMakeMain);
        Assert.Equal("Make K0i2 main", lists.Mine[1].MakeMainName);

        var watched = Assert.Single(lists.Watching);
        Assert.Equal("clan-level numbers only", watched.Who);
        Assert.Equal("watching", watched.Chip);
        Assert.Equal("Remove NovaForge", watched.RemoveName);
    }

    [Fact]
    public void AClanNotReadYetSaysSo() =>
        Assert.Equal("Not read yet", ClansModel.Who(Clan, ClanSource("s-00000002", "K0i2", SourceRole.Mine), null, [Main]));

    [Fact]
    public void TheProbeNamesEveryAccountItFound()
    {
        var probe = ClansModel.Probe("CCGP", Read(101, 201, 5), [Main, Alt, Waiting]);

        Assert.Equal(new ClanProbe("Found BirchMain and AshAlt in CCGP.", false), probe);
    }

    [Fact]
    public void AClanWithNoneOfYourAccountsOffersWatchingInstead() =>
        Assert.Equal(
            new ClanProbe("None of your accounts are in NovaForge yet. You can still watch it.", true),
            ClansModel.Probe("NovaForge", Read(5, 6), [Main, Alt]));

    [Fact]
    public void BeforeRoRoRoListsAccountsTheProbeCannotSay()
    {
        var expected = new ClanProbe("Read CCGP. RoRoRo hasn't listed your accounts yet, so Ur Score can't say which of them are in it.", false);

        Assert.Equal(expected, ClansModel.Probe("CCGP", Read(101), []));
        Assert.Equal(expected, ClansModel.Probe("CCGP", Read(101), [Waiting]));
    }

    [Fact]
    public void AStoppedReadOrNoReadSaysWhatHappened()
    {
        var idle = new RecipeSnapshot(WatchState.SourceIdle, "Your clan hasn't joined this battle.", [], [], 0);

        Assert.Equal(new ClanProbe("Added CCGP, but it couldn't be read just now. Your clan hasn't joined this battle.", false),
            ClansModel.Probe("CCGP", idle, [Main]));
        Assert.Equal(new ClanProbe("Added CCGP. It's read once reading starts, when Ur Score opens or from ▶ Start reading on the board.", false), ClansModel.Probe("CCGP", null, [Main]));
    }

    [Fact]
    public void PickingAMainAddsItAsTheOnlyMain()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main)];

        var change = ClansModel.Pick(sources, Clan, " K0i2 ", SourceRole.Main);

        Assert.Null(change.Note);
        var main = Assert.Single(change.Sources, s => s.Role == SourceRole.Main);
        Assert.Equal(change.SourceId, main.Id);
        Assert.Equal("K0i2", main.Inputs["clan"]);
        Assert.Equal(SourceRole.Mine, change.Sources.Single(s => s.Id == "s-00000001").Role);
    }

    [Fact]
    public void PickingTheSameNameInAnotherCaseReusesTheSource()
    {
        Source[] sources = [ClanSource("s-00000002", "K0i2", SourceRole.Mine)];

        var change = ClansModel.Pick(sources, Clan, "k0i2", SourceRole.Mine);

        Assert.Equal("s-00000002", change.SourceId);
        Assert.Equal("K0i2 is already on this list.", change.Note);
        Assert.Single(change.Sources);
    }

    [Fact]
    public void WatchingAClanYourAccountsAreInExplainsInstead()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main)];

        var change = ClansModel.Pick(sources, Clan, "ccgp", SourceRole.Watch);

        Assert.Equal("CCGP is already one of the clans your accounts are in. Remove it there first if you only want to watch it.", change.Note);
        Assert.Same(sources, change.Sources);
    }

    [Fact]
    public void PickingAWatchedClanAsYoursMovesItAcross()
    {
        Source[] sources = [ClanSource("s-00000003", "NovaForge", SourceRole.Watch)];

        var change = ClansModel.Pick(sources, Clan, "NovaForge", SourceRole.Mine);

        Assert.Null(change.Note);
        Assert.Equal(SourceRole.Mine, Assert.Single(change.Sources).Role);
    }

    [Fact]
    public void PickingAWatchedClanAsMainReusesItAndDemotesOnlyItsRecipesMain()
    {
        var watched = ClanSource("s-00000003", "NovaForge", SourceRole.Watch);
        var main = ClanSource("s-00000001", "CCGP", SourceRole.Main);
        var other = ClanSource("s-00000004", "Elsewhere", SourceRole.Main) with { Recipe = "other-recipe" };
        Source[] sources = [main, watched, other];

        var change = ClansModel.Pick(sources, Clan, " novaforge ", SourceRole.Main);

        Assert.Null(change.Note);
        Assert.Equal(watched.Id, change.SourceId);
        Assert.Equal(3, change.Sources.Count);
        Assert.Equal(watched with { Role = SourceRole.Main }, change.Sources.Single(source => source.Id == watched.Id));
        Assert.Equal(main with { Role = SourceRole.Mine }, change.Sources.Single(source => source.Id == main.Id));
        Assert.Equal(other, change.Sources.Single(source => source.Id == other.Id));
        Assert.Equal(SourceRole.Watch, watched.Role);
        Assert.Equal(SourceRole.Main, main.Role);

        var lists = ClansModel.Lists(Clan, change.Sources, new Dictionary<string, RecipeSnapshot>(), [Main, Alt]);
        Assert.Equal("NovaForge", lists.Main!.Name);
        Assert.Equal(new[] { "NovaForge", "CCGP" }, lists.Mine.Select(row => row.Name));
        Assert.Empty(lists.Watching);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickingAWatchedClanAsMainEnablesIt(bool enabled)
    {
        var watched = ClanSource("s-00000003", "NovaForge", SourceRole.Watch, enabled);

        var change = ClansModel.Pick([watched], Clan, "NovaForge", SourceRole.Main);

        Assert.Null(change.Note);
        Assert.Equal(watched.Id, change.SourceId);
        Assert.Equal(watched with { Role = SourceRole.Main, Enabled = true }, Assert.Single(change.Sources));
        Assert.Equal(watched with { Role = SourceRole.Main }, Assert.Single(SourceRules.MakeMain([watched], watched.Id)));
    }

    [Fact]
    public void WatchInsteadAndTheSwitchChangeOnlyTheirSource()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main), ClanSource("s-00000002", "K0i2", SourceRole.Mine)];

        var watched = ClansModel.WatchInstead(sources, "s-00000002");
        var off = ClansModel.SetEnabled(sources, "s-00000001", false);

        Assert.Equal(new[] { SourceRole.Main, SourceRole.Watch }, watched.Select(s => s.Role).ToArray());
        Assert.Equal(new[] { false, true }, off.Select(s => s.Enabled).ToArray());
    }

    [Fact]
    public void RequestsPerHourCountEveryStepOfEveryEnabledSource()
    {
        var installed = new InstalledRecipe(Clan, "", new RecipeState());
        Source[] sources =
        [
            ClanSource("s-00000001", "CCGP", SourceRole.Main),
            ClanSource("s-00000002", "K0i2", SourceRole.Mine),
            ClanSource("s-00000003", "Resting", SourceRole.Watch, enabled: false),
        ];

        var requests = ClansModel.RequestsPerHour(sources, [installed], accountCount: 10);

        var expected = (int)Math.Round(2 * Clan.Steps.Count * 3600.0 / Clan.EffectiveEverySeconds);
        Assert.Equal(new HostRequests("ps99.biggamesapi.io", expected), Assert.Single(requests));
        Assert.Equal($"Your PC asks ps99.biggamesapi.io about {expected} times an hour.", ClansModel.RequestsLine(requests));
    }

    [Fact]
    public void TheSixthEnabledSourceOfARecipeAsksFirst()
    {
        var five = Enumerable.Range(1, 5).Select(i => ClanSource($"s-0000000{i}", $"Clan{i}", SourceRole.Watch)).ToList();
        var fourAndOneOff = five.Select((s, i) => i == 0 ? s with { Enabled = false } : s).ToList();

        Assert.True(ClansModel.NeedsConfirmation(five, Clan.Slug));
        Assert.False(ClansModel.NeedsConfirmation(fourAndOneOff, Clan.Slug));
        Assert.StartsWith("That makes more than 5 clans for Pet Sim 99 clan battle points.", ClansModel.ConfirmText(Clan, []));
    }

    /// <summary>
    /// The Top switch on a recipe's page turns THAT recipe's list source on and off. It used to take the first
    /// installed group-list recipe's source whichever page it was on, which is invisible with one list installed
    /// and wrong the moment there are two: the switch on B's page toggled A's list (S1-11.2). The two lists here
    /// are distinct recipes with distinct sources, and the second is asked for, so "first" and "the page's own"
    /// give different answers.
    /// </summary>
    [Fact]
    public void TheSwitchFindsThePagesOwnListNotTheFirstInstalled()
    {
        var first = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var second = RecipeParser.Parse(GroupList).Recipe!;
        IReadOnlyList<InstalledRecipe> installed = [new(first, "", new RecipeState()), new(second, "", new RecipeState())];
        var firstSource = new Source("s-0000000a", first.Slug, new Dictionary<string, string>(), SourceRole.Watch);
        var secondSource = new Source("s-0000000b", second.Slug, new Dictionary<string, string>(), SourceRole.Watch);

        Assert.Same(secondSource, ClansModel.GroupListSource([firstSource, secondSource], installed, second.Slug));
        Assert.Same(firstSource, ClansModel.GroupListSource([firstSource, secondSource], installed, first.Slug));
        Assert.Null(ClansModel.GroupListSource([firstSource, secondSource], installed, "not-a-list"));
    }

    [Fact]
    public void TheSwitchFindsTheGroupListSource()
    {
        var top = RecipeParser.Parse(GroupList).Recipe!;
        var installed = new[] { new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(top, "", new RecipeState()) };
        var topSource = new Source("s-0000000a", top.Slug, new Dictionary<string, string>(), SourceRole.Watch);

        Assert.Same(topSource, ClansModel.GroupListSource([ClanSource("s-00000001", "CCGP", SourceRole.Main), topSource], installed, top.Slug));
        Assert.Null(ClansModel.GroupListSource([ClanSource("s-00000001", "CCGP", SourceRole.Main)], installed, top.Slug));
        // And asked from a page that is not a list, there is no switch to show.
        Assert.Null(ClansModel.GroupListSource([ClanSource("s-00000001", "CCGP", SourceRole.Main), topSource], installed, Clan.Slug));
    }
}
