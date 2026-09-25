using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class StarterBoardsTests
{
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
    public void TheBattleTabHasTheMocksThreeRowsEachFillingTwelveColumns()
    {
        var slug = Clan.Slug;

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [AltClan, Rival, MainClan, SecondAltClan, TopSource], StarterBoards.Battle);

        Assert.Equal((StarterBoards.Battle, BoardEmpty.None), (board.Name, board.Empty));
        Assert.Equal(
            new[]
            {
                (PanelType.Standing, 3), (PanelType.Standing, 3), (PanelType.Race, 6),
                (PanelType.MyAccounts, 5), (PanelType.PromotionCheck, 4), (PanelType.Top, 3),
                (PanelType.PastPeriods, 6), (PanelType.Records, 6),
            },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id), board.Panels[1].Settings);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, SecondAltClan.Id, Rival.Id }, board.Panels[2].Settings.SourceIds!.ToArray());
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[3].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"), board.Panels[4].Settings);
        Assert.Equal(new PanelSettings(TopList.Slug, SourceId: TopSource.Id), board.Panels[5].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id, Stat: "value"), board.Panels[6].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[7].Settings);

        var rows = BoardLayout.Flow(board.Panels.Select(p => p.Span).ToList(), 1280).GroupBy(p => p.Row);
        Assert.Equal(3, rows.Count());
        Assert.All(rows, row => Assert.Equal(BoardLayout.Columns, row.Sum(p => p.Span)));
    }

    [Fact]
    public void ARowWithAPanelMissingClosesUpIntoEqualShares()
    {
        // One clan and a switched-off top list: its standing takes row 1 alone (no race), My accounts row 2 alone.
        var one = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource with { Enabled = false }], StarterBoards.Battle);
        Assert.Equal(new[] { (PanelType.Standing, 12), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 6), (PanelType.Records, 6) },
            one.Panels.Select(p => (p.Type, p.Span)).ToArray());

        // Two clans your accounts are in and no main: both standings and the race keep row 1; with no promotion check
        // and no top list, My accounts has row 2.
        var two = StarterBoards.Build([Installed(Clan, "value")], [AltClan, SecondAltClan], StarterBoards.Battle);
        Assert.Equal(
            new[] { (PanelType.Standing, 3), (PanelType.Standing, 3), (PanelType.Race, 6), (PanelType.MyAccounts, 12), (PanelType.PastPeriods, 6), (PanelType.Records, 6) },
            two.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(AltClan.Id, two.Panels[0].Settings.SourceId);

        // Your main and a clan your accounts are in, with no rival and no top list: two clans still race in row 1, and
        // My accounts and the promotion check split row 2.
        var noTop = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan], StarterBoards.Battle);
        Assert.Equal(
            new[] { (PanelType.Standing, 3), (PanelType.Standing, 3), (PanelType.Race, 6), (PanelType.MyAccounts, 6), (PanelType.PromotionCheck, 6), (PanelType.PastPeriods, 6), (PanelType.Records, 6) },
            noTop.Panels.Select(p => (p.Type, p.Span)).ToArray());
    }

    /// <summary>
    /// One clan and a switched-on clans list is a whole race: the list brings the rivals in, as it does for a race added by
    /// hand (2026-09-24). One clan and no list is a single line with nothing to race, so the starter leaves it off.
    /// </summary>
    [Fact]
    public void OneClanRacesOnTheStarterOnlyWithAClansListOn()
    {
        var withList = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource], StarterBoards.Battle);
        var race = Assert.Single(withList.Panels, p => p.Type == PanelType.Race);
        Assert.Equal(new[] { MainClan.Id }, race.Settings.SourceIds!.ToArray());
        var rows = BoardLayout.Flow(withList.Panels.Select(p => p.Span).ToList(), 1280).GroupBy(p => p.Row);
        Assert.All(rows, row => Assert.Equal(BoardLayout.Columns, row.Sum(p => p.Span)));

        var noList = StarterBoards.Build([Installed(Clan, "value")], [MainClan], StarterBoards.Battle);
        Assert.DoesNotContain(noList.Panels, p => p.Type == PanelType.Race);
    }

    [Fact]
    public void WithNoAltsClanAndNoPastPeriodsAWatchedClanStillRacesAndRecordsTakesItsRow()
    {
        // A recipe that reads no past periods, your main and a clan you watch, and a top list on: no alts' clan means
        // no second standing and no promotion check, so the main shares row 1 with the race, My accounts shares row 2
        // with the top, and Records has row 3.
        var noPast = Clan with { Period = Clan.Period! with { Past = null } };
        var main = SourceOf("s-00000001", noPast, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", noPast, "NovaForge", SourceRole.Watch);

        var board = StarterBoards.Build([Installed(noPast, "value"), Installed(TopList)], [main, rival, TopSource], StarterBoards.Battle);

        Assert.Equal(
            new[] { (PanelType.Standing, 6), (PanelType.Race, 6), (PanelType.MyAccounts, 6), (PanelType.Top, 6), (PanelType.Records, 12) },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new[] { main.Id, rival.Id }, board.Panels[1].Settings.SourceIds!.ToArray());

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
    public void AStarterIsBuiltByItsNameOrItsKeyInAnyLetterCaseAndNoOtherName()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        IReadOnlyList<InstalledRecipe> installed = [Installed(Clan, "value"), Installed(Profile, "diamonds")];
        IReadOnlyList<Source> sources = [MainClan, profile];

        // A tab's follows value is the key ("alts"): it builds Alts, not Battle.
        foreach (var name in new[] { StarterBoards.Alts, "alts", "ALTS", " Alts " })
        {
            Assert.Equal(PanelType.AccountsTable, StarterBoards.Build(installed, sources, name).Panels[0].Type);
        }

        Assert.Equal(StarterBoards.Battle, StarterBoards.Build(installed, sources, "battle").Name);
        Assert.Throws<ArgumentException>(() => StarterBoards.Build(installed, sources, "grind"));
    }

    [Fact]
    public void AltsWithNoSourceReadsYourAccountsUnlessItsRecipeNeedsOne()
    {
        // The profile recipe reads each of your accounts and needs no source: with none, the table still reads it.
        var noSource = StarterBoards.Build([Installed(Profile, "diamonds")], [], StarterBoards.Alts);
        Assert.Equal(BoardEmpty.None, noSource.Empty);
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: null), noSource.Panels[0].Settings);

        // A recipe without a period that asks for an input can't be read without a source: Alts asks for one.
        var members = Clan with { Name = "Clan members", Period = null };
        var asks = StarterBoards.Build([Installed(members, "value")], [], StarterBoards.Alts);
        Assert.Equal((StarterBoards.Alts, BoardEmpty.NoSources, 0, members.Slug), (asks.Name, asks.Empty, asks.Panels.Count, asks.RecipeSlug));
    }

    [Fact]
    public void AltsTakesARecipeWithASourceOnFirstThenOneThatReadsYourAccountsOneByOne()
    {
        var members = Clan with { Name = "Clan members", Period = null };
        var membersSource = SourceOf("s-00000005", members, "CCGP", SourceRole.Main);
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        IReadOnlyList<InstalledRecipe> installed = [Installed(members, "value"), Installed(Profile, "diamonds")];

        string TableRecipe(params Source[] sources) => StarterBoards.Build(installed, sources, StarterBoards.Alts).Panels[0].Settings.Recipe;

        // Both have a source on: the one that reads your accounts one by one, though it is installed second.
        Assert.Equal(Profile.Slug, TableRecipe(membersSource, profile));
        // Only the other has a source on: that one.
        Assert.Equal(members.Slug, TableRecipe(membersSource, profile with { Enabled = false }));
        // Neither has: your accounts one by one again.
        Assert.Equal(Profile.Slug, TableRecipe());
    }

    [Fact]
    public void TheFirstStatFallsBackToASentOneWhenNoneIsShown()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Send: true, MetricId: "ps99.rank") }));

        Assert.Equal("rank", StarterBoards.FirstStat(installed));
    }
}
