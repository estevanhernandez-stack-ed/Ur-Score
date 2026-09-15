using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelGalleryTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static readonly Dictionary<string, RecipeSnapshot> NoReads = new();

    private static GalleryCard Card(IReadOnlyList<GalleryCard> cards, PanelType type) => cards.Single(c => c.Type == type);

    [Fact]
    public void TenCardsInTheSpecsOrderTitledInTheRecipesWords()
    {
        var cards = PanelGallery.Cards(Live(
            [MainClan, AltClan, TopSource, ProfileSource],
            [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds")], NoReads));

        Assert.Equal(
            new[] { "Clan standing", "Battle race", "My accounts", "Promotion check", "Account card", "Past battles", "Records", "Top of the battle", "Profile stat", "Live leaderboard" },
            cards.Select(c => c.Title).ToArray());
        Assert.Equal(PanelGallery.Order, cards.Select(c => c.Type));
        Assert.All(cards, c => Assert.True(c.CanAdd, c.Title));
        Assert.All(cards, c => Assert.Equal("", c.WhyNot));
        Assert.Equal("Needs a clan.", Card(cards, PanelType.Standing).Needs);
        Assert.Equal("Needs 2 to 5 clans of one recipe.", Card(cards, PanelType.Race).Needs);
    }

    [Fact]
    public void WithNothingInstalledEveryCardIsOffAndSaysWhy()
    {
        var cards = PanelGallery.Cards(Live([], [], NoReads));

        Assert.Equal(
            new[] { "Standing", "Race", "My accounts", "Promotion check", "Account card", "Past periods", "Records", "Top of the list", "Profile stat", "Live leaderboard" },
            cards.Select(c => c.Title).ToArray());
        Assert.All(cards, c => Assert.False(c.CanAdd, c.Title));
        Assert.All(cards, c => Assert.NotEqual("", c.WhyNot));
        Assert.Equal("Add a source in Setup first.", Card(cards, PanelType.Standing).WhyNot);
    }

    [Fact]
    public void OneClanCanStandButNotRaceOrBePromotedFrom()
    {
        var cards = PanelGallery.Cards(Live([MainClan], [Installed(Clan, "value")], NoReads));

        Assert.True(Card(cards, PanelType.Standing).CanAdd);
        Assert.True(Card(cards, PanelType.MyAccounts).CanAdd);
        Assert.False(Card(cards, PanelType.Race).CanAdd);
        Assert.False(Card(cards, PanelType.PromotionCheck).CanAdd);
        Assert.False(Card(cards, PanelType.Top).CanAdd);
        Assert.False(Card(cards, PanelType.ProfileStat).CanAdd);
    }

    [Fact]
    public void WithNoTickedStatTheStatPanelsAskForOne()
    {
        var cards = PanelGallery.Cards(Live([MainClan, AltClan], [Installed(Clan)], NoReads));

        Assert.True(Card(cards, PanelType.Standing).CanAdd);
        Assert.True(Card(cards, PanelType.Race).CanAdd);
        Assert.False(Card(cards, PanelType.MyAccounts).CanAdd);
        Assert.False(Card(cards, PanelType.Records).CanAdd);
        Assert.False(Card(cards, PanelType.AccountCard).CanAdd);
        Assert.False(Card(cards, PanelType.PromotionCheck).CanAdd);
        Assert.StartsWith("Tick Show or Send on a stat", Card(cards, PanelType.MyAccounts).WhyNot);
    }
}
