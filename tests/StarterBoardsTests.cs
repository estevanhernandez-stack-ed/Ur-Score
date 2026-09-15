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
    public void TheBattleTabHasTheDesignsFourRowsEachFillingTwelveColumns()
    {
        var slug = Clan.Slug;

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [AltClan, Rival, MainClan, SecondAltClan, TopSource], StarterBoards.Battle);

        Assert.Equal((StarterBoards.Battle, BoardEmpty.None), (board.Name, board.Empty));
        Assert.Equal(
            new[]
            {
                (PanelType.Standing, 4), (PanelType.Standing, 4), (PanelType.Top, 4),
                (PanelType.Race, 8), (PanelType.PromotionCheck, 4),
                (PanelType.MyAccounts, 12),
                (PanelType.PastPeriods, 7), (PanelType.Records, 5),
            },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id), board.Panels[1].Settings);
        Assert.Equal(new PanelSettings(TopList.Slug, SourceId: TopSource.Id), board.Panels[2].Settings);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, SecondAltClan.Id, Rival.Id }, board.Panels[3].Settings.SourceIds!.ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"), board.Panels[4].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[5].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id, Stat: "value"), board.Panels[6].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[7].Settings);

        var rows = BoardLayout.Flow(board.Panels.Select(p => p.Span).ToList(), 1280).GroupBy(p => p.Row);
        Assert.All(rows, row => Assert.Equal(BoardLayout.Columns, row.Sum(p => p.Span)));
    }

    [Fact]
    public void ARowWithAPanelMissingClosesUpIntoEqualShares()
    {
        // One clan and a switched-off top list: its standing takes row 1 alone; there's no race or promotion check.
        var one = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource with { Enabled = false }], StarterBoards.Battle);
        Assert.Equal(new[] { (PanelType.Standing, 12), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 7), (PanelType.Records, 5) },
            one.Panels.Select(p => (p.Type, p.Span)).ToArray());

        // Two clans your accounts are in and no main: the first leads, both share row 1, the race takes row 2 alone.
        var two = StarterBoards.Build([Installed(Clan, "value")], [AltClan, SecondAltClan], StarterBoards.Battle);
        Assert.Equal(
            new[] { (PanelType.Standing, 6), (PanelType.Standing, 6), (PanelType.Race, 12), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 7), (PanelType.Records, 5) },
            two.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(AltClan.Id, two.Panels[0].Settings.SourceId);
    }

    [Fact]
    public void WithNoAltsClanAndNoPastPeriodsAWatchedClanStillRacesAndRecordsTakesItsRow()
    {
        // A recipe that reads no past periods, your main and a clan you watch, and a top list on: no alts' clan means
        // no second standing and no promotion check, so the main shares row 1 with the top and the race has row 2.
        var noPast = Clan with { Period = Clan.Period! with { Past = null } };
        var main = SourceOf("s-00000001", noPast, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", noPast, "NovaForge", SourceRole.Watch);

        var board = StarterBoards.Build([Installed(noPast, "value"), Installed(TopList)], [main, rival, TopSource], StarterBoards.Battle);

        Assert.Equal(
            new[] { (PanelType.Standing, 6), (PanelType.Top, 6), (PanelType.Race, 12), (PanelType.MyAccounts, 12), (PanelType.Records, 12) },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new[] { main.Id, rival.Id }, board.Panels[2].Settings.SourceIds!.ToArray());

        var rows = BoardLayout.Flow(board.Panels.Select(p => p.Span).ToList(), 1280).GroupBy(p => p.Row);
        Assert.All(rows, row => Assert.Equal(BoardLayout.Columns, row.Sum(p => p.Span)));
    }

    [Fact]
    public void TheAltsTabIsTheAccountsTableThenRecordsAndTheAccountCard()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(Profile, "diamonds", "eggs", "rank")], [MainClan, profile], StarterBoards.Alts);

        Assert.Equal((StarterBoards.Alts, BoardEmpty.None), (board.Name, board.Empty));
        Assert.Equal(new[] { (PanelType.AccountsTable, 12), (PanelType.Records, 5), (PanelType.AccountCard, 7) }, board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: profile.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(Profile.Slug, Stat: "diamonds"), board.Panels[1].Settings);
        Assert.Equal(new PanelSettings(Profile.Slug, Stat: "diamonds"), board.Panels[2].Settings);
    }

    [Fact]
    public void EachStarterNeedsItsKindOfRecipeAndAllListsBothInTabOrder()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var clanOnly = StarterBoards.All([Installed(Clan, "value")], [MainClan]);
        Assert.Equal(new[] { StarterBoards.Battle, StarterBoards.Alts }, clanOnly.Select(s => s.Name).ToArray());
        Assert.NotEmpty(clanOnly[0].Panels);
        Assert.Equal((BoardEmpty.NoStats, 0), (clanOnly[1].Empty, clanOnly[1].Panels.Count));

        var profileOnly = StarterBoards.All([Installed(Profile, "diamonds")], [profile]);
        Assert.Empty(profileOnly[0].Panels);
        Assert.NotEmpty(profileOnly[1].Panels);

        Assert.Same(profileOnly[1], StarterBoards.Named(profileOnly, "ALTS"));
        Assert.Null(StarterBoards.Named(profileOnly, "grind"));
        Assert.Equal("battle", StarterBoards.KeyOf(StarterBoards.Battle));

        // With nothing to show anywhere, the empty state asks for what's missing first.
        Assert.Equal(BoardEmpty.NoRecipes, StarterBoards.EmptyState(StarterBoards.All([], [])).Empty);
        var noMain = StarterBoards.All([Installed(Clan, "value"), Installed(Profile)], []);
        Assert.Equal((StarterBoards.Battle, BoardEmpty.NoSources), (StarterBoards.EmptyState(noMain).Name, StarterBoards.EmptyState(noMain).Empty));
    }

    [Fact]
    public void TheFirstStatFallsBackToASentOneWhenNoneIsShown()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Send: true, MetricId: "ps99.rank") }));

        Assert.Equal("rank", StarterBoards.FirstStat(installed));
    }
}
