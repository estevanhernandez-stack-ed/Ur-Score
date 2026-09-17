using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
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
    public void EveryCardInOrderTitledInTheRecipesWords()
    {
        var cards = PanelGallery.Cards(Live(
            [MainClan, AltClan, TopSource, ProfileSource],
            [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds")], NoReads));

        Assert.Equal(
            new[] { "Clan standing", "Battle race", "My accounts", "Promotion check", "Account card", "Past battles", "Records", "Top of the battle", "Profile stat", "Accounts table", "Live leaderboard" },
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
            new[] { "Standing", "Race", "My accounts", "Promotion check", "Account card", "Past periods", "Records", "Top of the list", "Profile stat", "Accounts table", "Live leaderboard" },
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void RaceNeedsTwoEnabledClans(bool mainEnabled, bool altEnabled, bool canAdd)
    {
        var live = Live(
            [MainClan with { Enabled = mainEnabled }, AltClan with { Enabled = altEnabled }],
            [Installed(Clan, "value")], NoReads);

        var race = Card(PanelGallery.Cards(live), PanelType.Race);

        Assert.Equal(canAdd, race.CanAdd);
        Assert.Equal(canAdd ? "" : "Needs at least 2 clans of one recipe. Add them in Setup.", race.WhyNot);
    }

    private const string GuildSeasonJson = """
        {
          "recipe": 1, "name": "Guild season", "credit": "Test data.", "metricId": "test.guild", "valueLabel": "Points",
          "everySeconds": 180,
          "inputs": [ { "id": "guild", "label": "Your guild", "plural": "Guilds" } ],
          "steps": [
            { "url": "https://example.test/season", "take": { "season": "data.season" } },
            { "url": "https://example.test/guild/{guild}", "rows": "data.members", "userId": "id", "value": "points" }
          ],
          "period": { "value": "season" },
          "headline": [ { "id": "guild-points", "label": "Guild points", "path": "data.points" } ]
        }
        """;

    private const string TopRoundJson = """
        {
          "recipe": 1, "name": "Top of the round", "credit": "Test data.", "valueLabel": "Points", "everySeconds": 300,
          "steps": [
            { "url": "https://example.test/round", "take": { "round": "data.round" } },
            { "url": "https://example.test/top/{round}", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" }
          ],
          "period": { "value": "round" }
        }
        """;

    private static Recipe Guild => RecipeParser.Parse(GuildSeasonJson).Recipe!;

    private static Recipe TopRound => RecipeParser.Parse(TopRoundJson).Recipe!;

    private static string[] Lines(IReadOnlyList<GalleryCard> cards, PanelType type)
    {
        var card = Card(cards, type);
        return [card.Title, card.Needs, card.Shows, card.WhyNot];
    }

    [Fact]
    public void EachCardSpeaksForOneRecipeTheOneAPanelAddedFromItWouldRead()
    {
        // The guild recipe comes first, but your main is a clan: a panel added now reads the clans.
        var guildWatch = new Source("s-00000004", Guild.Slug, new Dictionary<string, string> { ["guild"] = "Wolves" }, SourceRole.Watch);
        var roundTop = SourceOf("s-0000000b", TopRound, null, SourceRole.Watch);
        var cards = PanelGallery.Cards(Live(
            [guildWatch, MainClan, AltClan, roundTop],
            [Installed(Guild, "value"), Installed(Clan, "value"), Installed(TopRound)], NoReads));

        Assert.Equal(new[] { "Clan standing", "Needs a clan.", "Place, total, the last hour's gain and the battle line.", "" }, Lines(cards, PanelType.Standing));
        Assert.Equal(new[] { "Battle race", "Needs 2 to 5 clans of one recipe.", "Each clan's total over the current battle, one line each.", "" }, Lines(cards, PanelType.Race));
        Assert.Equal(new[] { "Past battles", "Needs a clan.", "Finished battles newest first: place, total and your best account.", "" }, Lines(cards, PanelType.PastPeriods));
        // Top reads the list's own period, and names groups the way its panel's name column does.
        Assert.Equal(new[] { "Top of the round", "Needs a recipe that lists groups.", "The top of the round live, with your guilds placed where they'd rank.", "" },
            Lines(cards, PanelType.Top));
    }

    [Fact]
    public void PromotionCardSpeaksForAnAddableRecipeWhenTheMainHasNoPartner()
    {
        var guild = Guild;
        var origin = SourceOf("s-00000004", guild, "First", SourceRole.Mine);
        var target = SourceOf("s-00000005", guild, "Second", SourceRole.Watch);
        var live = Live([MainClan, origin, target],
            [Installed(Clan, "value"), Installed(guild, "value")], NoReads);

        var card = Card(PanelGallery.Cards(live), PanelType.PromotionCheck);

        Assert.True(card.CanAdd);
        Assert.Equal("Needs a guild your accounts are in, and one to compare with (your main unless you pick another).", card.Needs);
        Assert.Equal("Where each of your accounts would place in the other guild now. Live only.", card.Shows);
        Assert.Empty(card.WhyNot);
    }

    [Fact]
    public void PromotionPrefersTheMainRecipeWhenBothRecipesHaveValidPairs()
    {
        var guild = Guild;
        var origin = SourceOf("s-00000004", guild, "First", SourceRole.Mine);
        var target = SourceOf("s-00000005", guild, "Second", SourceRole.Watch);
        var live = Live([origin, target, AltClan, MainClan],
            [Installed(guild, "value"), Installed(Clan, "value")], NoReads);

        var card = Card(PanelGallery.Cards(live), PanelType.PromotionCheck);

        Assert.True(card.CanAdd);
        Assert.Equal("Needs a clan your accounts are in, and one to compare with (your main unless you pick another).", card.Needs);
        Assert.Equal("Where each of your accounts would place in the other clan now. Live only.", card.Shows);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void PromotionRecipeNeedsAnOwnedOriginAndATickedStat(bool ownedOrigin, bool tickedStat, bool canAdd)
    {
        var guild = Guild;
        var origin = SourceOf("s-00000004", guild, "First", ownedOrigin ? SourceRole.Mine : SourceRole.Watch);
        var target = SourceOf("s-00000005", guild, "Second", SourceRole.Watch);
        var live = Live([origin, target, MainClan, AltClan],
            [Installed(Clan), tickedStat ? Installed(guild, "value") : Installed(guild)], NoReads);

        var card = Card(PanelGallery.Cards(live), PanelType.PromotionCheck);

        Assert.Equal(canAdd, card.CanAdd);
        var group = canAdd ? "guild" : "clan";
        Assert.Equal($"Needs a {group} your accounts are in, and one to compare with (your main unless you pick another).", card.Needs);
        Assert.Equal(canAdd ? "" : "Needs a clan your accounts are in, another to compare with, and a ticked stat.", card.WhyNot);
    }

    [Fact]
    public void ASavedPanelIsTitledByItsOwnRecipeNotTheCardsPick()
    {
        // Your main is a clan, so the Standing card speaks for clans; a Standing panel on the guild recipe is still a guild's.
        var guildWatch = new Source("s-00000004", Guild.Slug, new Dictionary<string, string> { ["guild"] = "Wolves" }, SourceRole.Watch);
        var live = Live([MainClan, AltClan, guildWatch], [Installed(Clan, "value"), Installed(Guild, "value")], NoReads);
        var guildStanding = new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Guild.Slug, SourceId: guildWatch.Id));
        var clanStanding = guildStanding with { Settings = new PanelSettings(Clan.Slug, SourceId: MainClan.Id) };
        var guildRace = new PanelDef("p-00000002", PanelType.Race, new PanelSize(6), new PanelSettings(Guild.Slug, SourceIds: [guildWatch.Id]));

        Assert.Equal("Clan standing", PanelGallery.Title(PanelType.Standing, live));
        Assert.Equal("Guild standing", PanelGallery.TitleOf(guildStanding, live));
        Assert.Equal("Clan standing", PanelGallery.TitleOf(clanStanding, live));
        Assert.Equal("Season race", PanelGallery.TitleOf(guildRace, live));

        // What the panel itself shows once its recipe is gone.
        Assert.Equal("Standing", PanelGallery.TitleOf(guildStanding with { Settings = new PanelSettings("removed-recipe", SourceId: guildWatch.Id) }, live));
        Assert.Equal(
            PanelModels.Standing(live, Reader(), guildStanding.Settings).Head.Title,
            PanelGallery.TitleOf(guildStanding, live));
    }

    [Fact]
    public void WithNothingToReadYetEachCardSpeaksForTheFirstRecipeItFits()
    {
        var cards = PanelGallery.Cards(Live([], [Installed(Guild, "value"), Installed(Clan, "value")], NoReads));

        Assert.Equal(
            new[] { "Guild standing", "Needs a guild.", "Place, total, the last hour's gain and the season line.", "Add a guild in Setup first." },
            Lines(cards, PanelType.Standing));
        Assert.Equal(
            new[] { "Season race", "Needs 2 to 5 guilds of one recipe.", "Each guild's total over the current season, one line each.", "Needs at least 2 guilds of one recipe. Add them in Setup." },
            Lines(cards, PanelType.Race));
        // Only the clan recipe keeps past periods.
        Assert.Equal(
            new[] { "Past battles", "Needs a clan.", "Finished battles newest first: place, total and your best account.", "Needs a clan whose recipe keeps past battles." },
            Lines(cards, PanelType.PastPeriods));
        Assert.Equal(
            new[] { "Top of the season", "Needs a recipe that lists groups.", "The top of the season live, with your guilds placed where they'd rank.", "Import a recipe that lists groups, and turn it on in Setup." },
            Lines(cards, PanelType.Top));
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
