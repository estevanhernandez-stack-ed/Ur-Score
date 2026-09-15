using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class StarterBoardsTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
    private static readonly Source SecondAltClan = SourceOf("s-00000004", Clan, "Z9", SourceRole.Mine);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);

    [Fact]
    public void WithNoRecipesTheBoardAsksForAnImport()
    {
        var board = StarterBoards.Build([], []);

        Assert.Equal(BoardEmpty.NoRecipes, board.Empty);
        Assert.Empty(board.Panels);
    }

    [Fact]
    public void RecipesWithNothingTickedAskForStats()
    {
        var board = StarterBoards.Build([Installed(Clan), Installed(TopList)], [MainClan, TopSource]);

        Assert.Equal(BoardEmpty.NoStats, board.Empty);
        Assert.Equal(Clan.Slug, board.RecipeSlug);
    }

    [Fact]
    public void AClanRecipeWithNoSourcesAsksForTheMainClan()
    {
        var board = StarterBoards.Build([Installed(Clan, "value")], []);

        Assert.Equal(BoardEmpty.NoSources, board.Empty);
        Assert.Equal(Clan.Slug, board.RecipeSlug);
    }

    [Fact]
    public void TheBattleBoardFollowsTheSpecsGridOrder()
    {
        var slug = Clan.Slug;

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [AltClan, Rival, MainClan, SecondAltClan, TopSource]);

        Assert.Equal(StarterBoards.Battle, board.Name);
        Assert.Equal(BoardEmpty.None, board.Empty);
        Assert.Equal(MainClan.Id, board.AnchorSourceId);
        Assert.Equal(
            new[]
            {
                (PanelType.Standing, 3), (PanelType.Standing, 3), (PanelType.Race, 6), (PanelType.MyAccounts, 5),
                (PanelType.PromotionCheck, 4), (PanelType.AccountCard, 3), (PanelType.Top, 4), (PanelType.PastPeriods, 4),
            },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id), board.Panels[1].Settings);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, SecondAltClan.Id, Rival.Id }, board.Panels[2].Settings.SourceIds!.ToArray());
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[3].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"), board.Panels[4].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[5].Settings);
        Assert.Equal(new PanelSettings(TopList.Slug, SourceId: TopSource.Id), board.Panels[6].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id, Stat: "value"), board.Panels[7].Settings);
    }

    [Fact]
    public void WithoutAMainTheFirstClanLeadsAndThereIsNoPromotionCheck()
    {
        var board = StarterBoards.Build([Installed(Clan, "value")], [AltClan, SecondAltClan]);

        Assert.Equal(AltClan.Id, board.AnchorSourceId);
        Assert.Equal(SecondAltClan.Id, board.Panels[1].Settings.SourceId);
        Assert.Contains(board.Panels, p => p.Type == PanelType.Race);
        Assert.DoesNotContain(board.Panels, p => p.Type == PanelType.PromotionCheck);
    }

    [Fact]
    public void OneSourceHasNoRaceAndASwitchedOffTopListHasNoTopPanel()
    {
        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource with { Enabled = false }]);

        Assert.Equal(new[] { PanelType.Standing, PanelType.MyAccounts, PanelType.AccountCard, PanelType.PastPeriods },
            board.Panels.Select(p => p.Type).ToArray());
    }

    [Fact]
    public void WithNoPeriodRecipeTheBoardIsGrind()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var board = StarterBoards.Build([Installed(Profile, "diamonds", "eggs", "rank")], [profile]);

        Assert.Equal(StarterBoards.Grind, board.Name);
        Assert.Equal(new[] { (PanelType.ProfileStat, 6), (PanelType.ProfileStat, 6), (PanelType.Records, 3), (PanelType.AccountCard, 3) },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new string?[] { "diamonds", "eggs", "diamonds", "diamonds" }, board.Panels.Select(p => p.Settings.Stat).ToArray());
        Assert.Equal(profile.Id, board.Panels[0].Settings.SourceId);
    }

    [Fact]
    public void TheFirstStatFallsBackToASentOneWhenNoneIsShown()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Send: true, MetricId: "ps99.rank") }));

        Assert.Equal("rank", StarterBoards.FirstStat(installed));
    }

    [Fact]
    public void TheKeyChangesOnlyWhenThePanelsDo()
    {
        var one = StarterBoards.Build([Installed(Clan, "value")], [MainClan]);
        var same = StarterBoards.Build([Installed(Clan, "value")], [MainClan]);
        var more = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        Assert.Equal(one.Key, same.Key);
        Assert.NotEqual(one.Key, more.Key);
    }

    [Fact]
    public void AStarterCanBeAskedForByName()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        InstalledRecipe[] installed = [Installed(Clan, "value"), Installed(Profile, "diamonds")];
        Source[] sources = [MainClan, profile];

        Assert.Equal(StarterBoards.Battle, StarterBoards.Build(installed, sources).Name);

        var grind = StarterBoards.Build(installed, sources, StarterBoards.Grind);
        Assert.Equal(StarterBoards.Grind, grind.Name);
        Assert.Equal(PanelType.ProfileStat, grind.Panels[0].Type);
        Assert.Equal(Profile.Slug, grind.Panels[0].Settings.Recipe);

        Assert.Empty(StarterBoards.Build([Installed(Clan, "value")], [MainClan], StarterBoards.Grind).Panels);
        Assert.Empty(StarterBoards.Build([Installed(Profile, "diamonds")], [profile], StarterBoards.Battle).Panels);
    }
}
