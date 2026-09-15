using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelFormsTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static LiveBoard Everything() => Live(
        [Rival, AltClan, MainClan, TopSource, ProfileSource],
        [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds", "eggs")],
        new Dictionary<string, RecipeSnapshot>());

    private static string ClanStat => PanelForms.StatKey(Clan.Slug, "value");

    private static string[] Keys(IEnumerable<FormChoice> choices) => [.. choices.Select(c => c.Key)];

    [Theory]
    [InlineData(PanelType.Standing, true, new[] { PanelField.Source })]
    [InlineData(PanelType.Standing, false, new[] { PanelField.Source })]
    [InlineData(PanelType.Race, true, new[] { PanelField.Sources })]
    [InlineData(PanelType.MyAccounts, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.PromotionCheck, true, new[] { PanelField.Source, PanelField.ToSource })]
    [InlineData(PanelType.PromotionCheck, false, new[] { PanelField.Source, PanelField.ToSource, PanelField.Stat })]
    [InlineData(PanelType.AccountCard, true, new[] { PanelField.Account })]
    [InlineData(PanelType.AccountCard, false, new[] { PanelField.Account, PanelField.Stat })]
    [InlineData(PanelType.PastPeriods, true, new[] { PanelField.Source })]
    [InlineData(PanelType.PastPeriods, false, new[] { PanelField.Source, PanelField.Stat })]
    [InlineData(PanelType.Records, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.Top, true, new[] { PanelField.Source })]
    [InlineData(PanelType.ProfileStat, true, new[] { PanelField.Stat })]
    [InlineData(PanelType.ProfileStat, false, new[] { PanelField.Stat, PanelField.Source })]
    [InlineData(PanelType.LiveLeaderboard, true, new[] { PanelField.Source })]
    public void AddingAsksOnlyForWhatThePanelNeeds(PanelType type, bool adding, PanelField[] expected) =>
        Assert.Equal(expected, PanelForms.Fields(type, adding).ToArray());

    [Fact]
    public void SourcesAreOfferedMainFirstAndOnlyWhereThePanelFits()
    {
        var live = Everything();
        var none = new FormValues();

        var standing = PanelForms.SourceChoices(PanelType.Standing, PanelField.Source, live, none);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(standing));
        Assert.Equal(new[] { "★ CCGP", "K0i2", "NovaForge · watching" }, standing.Select(c => c.Label).ToArray());

        Assert.Equal(new[] { TopSource.Id }, Keys(PanelForms.SourceChoices(PanelType.Top, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.Race, PanelField.Sources, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.PastPeriods, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.LiveLeaderboard, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id }, Keys(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.Source, live, none)));
        Assert.Equal(new[] { MainClan.Id, Rival.Id },
            Keys(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, new FormValues(Source: AltClan.Id))));
        Assert.Empty(PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, none));
        Assert.Equal(new[] { ProfileSource.Id },
            Keys(PanelForms.SourceChoices(PanelType.ProfileStat, PanelField.Source, live, new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")))));
    }

    [Fact]
    public void ASourceThatIsOffSaysSo()
    {
        var live = Live([MainClan with { Enabled = false }], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("★ CCGP · off", PanelForms.SourceChoices(PanelType.Standing, PanelField.Source, live, new FormValues()).Single().Label);
    }

    [Fact]
    public void StatsAreTheTickedOnesNamedByRecipeWhenThereIsMoreThanOne()
    {
        var live = Everything();

        Assert.Equal(
            new[]
            {
                (ClanStat, "Points · Pet Sim 99 clan battle points"),
                (PanelForms.StatKey(Profile.Slug, "diamonds"), "Diamonds · Pet Sim 99 profile"),
                (PanelForms.StatKey(Profile.Slug, "eggs"), "Eggs hatched · Pet Sim 99 profile"),
            },
            PanelForms.StatChoices(PanelType.MyAccounts, live, new FormValues(), null).Select(c => (c.Key, c.Label)).ToArray());
        Assert.Equal(new[] { "Diamonds", "Eggs hatched" },
            PanelForms.StatChoices(PanelType.ProfileStat, live, new FormValues(), null).Select(c => c.Label).ToArray());
        Assert.Equal(new[] { ClanStat }, Keys(PanelForms.StatChoices(PanelType.PastPeriods, live, new FormValues(Source: MainClan.Id), null)));
        Assert.Empty(PanelForms.StatChoices(PanelType.PromotionCheck, live, new FormValues(), null));
        Assert.Contains(PanelForms.StatKey(Profile.Slug, "rank"),
            Keys(PanelForms.StatChoices(PanelType.ProfileStat, live, new FormValues(), new PanelSettings(Profile.Slug, Stat: "rank"))));
    }

    [Fact]
    public void AccountsAreYoursByNameAfterYourTopAccount() =>
        Assert.Equal(
            new[] { ("", "Your top account"), ("201", "CElCPapa"), ("101", "estehernandez"), ("301", "ItsJustEste"), ("202", "ItsJustEstePapa") },
            PanelForms.AccountChoices(Everything()).Select(c => (c.Key, c.Label)).ToArray());

    [Fact]
    public void DefaultsFollowTheMain()
    {
        var live = Everything();

        Assert.Equal(new FormValues(Source: MainClan.Id), PanelForms.Defaults(PanelType.Standing, live));
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, PanelForms.Defaults(PanelType.Race, live).Sources!.ToArray());
        Assert.Equal(new FormValues(Source: AltClan.Id, ToSource: MainClan.Id, Stat: ClanStat), PanelForms.Defaults(PanelType.PromotionCheck, live));
        Assert.Equal(new FormValues(Account: PanelForms.TopAccountKey, Stat: ClanStat), PanelForms.Defaults(PanelType.AccountCard, live));
        Assert.Equal(new FormValues(Source: MainClan.Id, Stat: ClanStat), PanelForms.Defaults(PanelType.PastPeriods, live));
        Assert.Equal(new FormValues(Source: TopSource.Id), PanelForms.Defaults(PanelType.Top, live));
        Assert.Equal(new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), PanelForms.Defaults(PanelType.ProfileStat, live));
    }

    [Fact]
    public void EveryTypesDefaultsBuildSettingsWithNoProblem()
    {
        var live = Everything();

        Assert.All(PanelGallery.Order, type =>
            Assert.Null(PanelForms.Problem(type, PanelForms.Build(type, PanelForms.Defaults(type, live), live), live)));
    }

    [Fact]
    public void BuildingGivesTheSettingsThePanelsRead()
    {
        var live = Everything();

        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: AltClan.Id), PanelForms.Build(PanelType.Standing, new FormValues(Source: AltClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"),
            PanelForms.Build(PanelType.PromotionCheck, new FormValues(Source: AltClan.Id, ToSource: MainClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value", UserId: 201),
            PanelForms.Build(PanelType.AccountCard, new FormValues(Account: "201", Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value"),
            PanelForms.Build(PanelType.AccountCard, new FormValues(Account: PanelForms.TopAccountKey, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id, Stat: "diamonds"),
            PanelForms.Build(PanelType.ProfileStat, new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live));
        Assert.Equal(new PanelSettings(Clan.Slug, Stat: "value"),
            PanelForms.Build(PanelType.MyAccounts, new FormValues(Source: MainClan.Id, Stat: ClanStat), live));
        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: MainClan.Id),
            PanelForms.Build(PanelType.PastPeriods, new FormValues(Source: MainClan.Id, Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live));

        var race = PanelForms.Build(PanelType.Race, new FormValues(Sources: [MainClan.Id, Rival.Id]), live);
        Assert.Equal(Clan.Slug, race.Recipe);
        Assert.Equal(new[] { MainClan.Id, Rival.Id }, race.SourceIds!.ToArray());
        Assert.Null(race.SourceId);

        var promotion = new PanelSettings(Clan.Slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value");
        Assert.Equal(promotion, PanelForms.Build(PanelType.PromotionCheck, PanelForms.From(promotion), live));
    }

    [Fact]
    public void AProfileStatWhoseOnlySourceIsOffIsNotStale()
    {
        var off = ProfileSource with { Enabled = false };
        var live = Live([off], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        var settings = PanelForms.Build(PanelType.ProfileStat, PanelForms.Defaults(PanelType.ProfileStat, live), live);

        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: off.Id, Stat: "diamonds"), settings);
        Assert.Null(PanelForms.Problem(PanelType.ProfileStat, settings, live));
        Assert.False(PanelModels.ProfileStat(live, Reader(), settings).Head.HasStale);

        // With no source pinned, the panel only reads a source that is on, so the form asks for one.
        Assert.True(PanelModels.ProfileStat(live, Reader(), settings with { SourceId = null }).Head.HasStale);
        Assert.Equal("Choose a source.", PanelForms.Problem(PanelType.ProfileStat, settings with { SourceId = null }, live));
    }

    [Fact]
    public void ProblemsUseTheRecipesWordsAndNameWhatToFix()
    {
        var live = Everything();
        PanelSettings Clans(string? source = null, string? to = null, string? stat = null, long? user = null, IReadOnlyList<string>? race = null) =>
            new(Clan.Slug, source, race, to, stat, user);

        Assert.Equal("Choose a clan.", PanelForms.Problem(PanelType.Standing, Clans(), live));
        Assert.Equal("This panel's clan was removed. Choose another.", PanelForms.Problem(PanelType.Standing, Clans("s-gone0000"), live));
        Assert.Equal("Choose 2 to 5 clans.", PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id]), live));
        Assert.Equal("Every line in a race comes from the same recipe.", PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id, TopSource.Id]), live));
        Assert.Null(PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id, Rival.Id]), live));
        Assert.Equal("Choose a clan your accounts are in.", PanelForms.Problem(PanelType.PromotionCheck, Clans(Rival.Id, MainClan.Id, "value"), live));
        Assert.Equal("Choose a different clan of the same recipe to compare with.", PanelForms.Problem(PanelType.PromotionCheck, Clans(AltClan.Id, AltClan.Id, "value"), live));
        Assert.Equal("Choose a stat.", PanelForms.Problem(PanelType.PromotionCheck, Clans(AltClan.Id, MainClan.Id), live));
        Assert.Equal("This panel's stat was removed. Choose another.", PanelForms.Problem(PanelType.MyAccounts, Clans(stat: "gone-stat"), live));
        Assert.Equal("Choose one of your accounts.", PanelForms.Problem(PanelType.AccountCard, Clans(stat: "value", user: 987654321), live));
        Assert.Null(PanelForms.Problem(PanelType.PastPeriods, Clans(MainClan.Id), live));
        Assert.Null(PanelForms.Problem(PanelType.ProfileStat, new PanelSettings(Profile.Slug, Stat: "diamonds"), live));
        Assert.Equal("This panel can't show that clan.", PanelForms.Problem(PanelType.Standing, new PanelSettings(TopList.Slug, SourceId: TopSource.Id), live));
    }
}
