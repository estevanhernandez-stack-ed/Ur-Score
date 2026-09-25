using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelFormsTests
{
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
    [InlineData(PanelType.AccountsTable, true, new[] { PanelField.Source })]
    [InlineData(PanelType.AccountsTable, false, new[] { PanelField.Source })]
    public void AddingAsksOnlyForWhatThePanelNeeds(PanelType type, bool adding, PanelField[] expected) =>
        Assert.Equal(expected, PanelForms.Fields(type, adding).ToArray());

    [Fact]
    public void AddingAProfileStatAsksForItsSourceWhenItIsntTheOnlyOneOn()
    {
        var stat = new FormValues(Stat: PanelForms.StatKey(Profile.Slug, "diamonds"));
        var second = SourceOf("s-00000019", Profile, null, SourceRole.Mine);
        LiveBoard LiveWith(params Source[] sources) => Live(sources, [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        // One source, on: nothing to ask, and it is the one saved.
        Assert.Equal(new[] { PanelField.Stat }, PanelForms.Fields(PanelType.ProfileStat, true, LiveWith(ProfileSource), stat).ToArray());

        // Two sources, or only one that is off: the form shows the source, so what is saved is one you saw.
        var two = LiveWith(ProfileSource with { Enabled = false }, second);
        Assert.Equal(new[] { PanelField.Stat, PanelField.Source }, PanelForms.Fields(PanelType.ProfileStat, true, two, stat).ToArray());
        Assert.Equal(second.Id, PanelForms.Build(PanelType.ProfileStat, stat, two).SourceId);
        Assert.Equal(new[] { PanelField.Stat, PanelField.Source },
            PanelForms.Fields(PanelType.ProfileStat, true, LiveWith(ProfileSource with { Enabled = false }), stat).ToArray());

        // Every other form is as Fields(type, adding) says.
        Assert.Equal(new[] { PanelField.Stat, PanelField.Source }, PanelForms.Fields(PanelType.ProfileStat, false, LiveWith(ProfileSource), stat).ToArray());
        Assert.Equal(new[] { PanelField.Account }, PanelForms.Fields(PanelType.AccountCard, true, two, stat).ToArray());
    }

    [Fact]
    public void ASavedPickNoLongerOfferedIsHeldAndCheckedNotBlanked()
    {
        var choices = new[] { new FormChoice(PanelForms.TopAccountKey, "Your top account"), new FormChoice("101", "BirchMain") };

        Assert.Equal(new FormPick(choices[1], null), PanelForms.Pick(choices, "101", shown: true, saved: "201"));
        Assert.Equal(new FormPick(null, "201"), PanelForms.Pick(choices, "201", shown: true, saved: "201"));
        Assert.Equal(new FormPick(null, null), PanelForms.Pick(choices, "999", shown: true, saved: "201"));
        Assert.Equal(new FormPick(null, null), PanelForms.Pick(choices, null, shown: true, saved: null));
        Assert.Equal(new FormPick(choices[0], null), PanelForms.Pick(choices, "201", shown: false, saved: "201"));

        // An Account card pinned to an account RoRoRo doesn't list now: held, the form says what's wrong, and it can't
        // be saved as "Your top account" until you choose that. A blank pick would have saved it so, silently.
        var live = Live([], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>(), accounts: [Main]);
        var saved = PanelForms.From(new PanelSettings(Clan.Slug, Stat: "value", UserId: AltOne.RobloxUserId));
        var held = PanelForms.Pick(PanelForms.AccountChoices(live), saved.Account, shown: true, saved: saved.Account);

        Assert.Null(held.Selected);
        Assert.Equal("Choose one of your accounts.", PanelForms.Problem(PanelType.AccountCard, PanelForms.Build(PanelType.AccountCard, saved with { Account = held.Held }, live), live));
        Assert.Null(PanelForms.Build(PanelType.AccountCard, saved with { Account = null }, live).UserId);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    public void ProfileSourceWarningExplainsOffWithoutChangingThePickOrBlockingSave(
        bool firstEnabled, bool secondEnabled, bool savedFirst, bool warns)
    {
        var first = ProfileSource with { Enabled = firstEnabled };
        var second = ProfileSource with { Id = "s-00000019", Enabled = secondEnabled };
        var live = Live([first, second], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());
        var values = new FormValues(Source: savedFirst ? first.Id : null, Stat: PanelForms.StatKey(Profile.Slug, "diamonds"));

        var settings = PanelForms.Build(PanelType.ProfileStat, values, live);

        Assert.Equal(savedFirst || firstEnabled || !secondEnabled ? first.Id : second.Id, settings.SourceId);
        Assert.Equal(warns ? $"{live.SourceName(first)} is switched off, so it isn't read." : null,
            PanelForms.Warning(PanelType.ProfileStat, settings, live));
        Assert.Null(PanelForms.Problem(PanelType.ProfileStat, settings, live));
        Assert.Equal(new[] { firstEnabled, secondEnabled }, live.Sources.Select(source => source.Enabled));
        Assert.Contains(PanelField.Source, PanelForms.Fields(PanelType.ProfileStat, true, live, values));
        var choices = PanelForms.SourceChoices(PanelType.ProfileStat, PanelField.Source, live, values);
        var pick = PanelForms.Pick(choices, settings.SourceId, shown: true, saved: values.Source);
        Assert.Equal(settings.SourceId, pick.Selected!.Key);
        if (warns) Assert.EndsWith("off", pick.Selected.Label);
        Assert.Null(PanelForms.Warning(PanelType.ProfileStat, settings with { SourceId = "removed" }, live));
    }

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
        Assert.Equal(new[] { ClanStat, PanelForms.NoStatKey }, Keys(PanelForms.StatChoices(PanelType.PastPeriods, live, new FormValues(Source: MainClan.Id), null)));
        Assert.Empty(PanelForms.StatChoices(PanelType.PromotionCheck, live, new FormValues(), null));
        Assert.Contains(PanelForms.StatKey(Profile.Slug, "rank"),
            Keys(PanelForms.StatChoices(PanelType.ProfileStat, live, new FormValues(), new PanelSettings(Profile.Slug, Stat: "rank"))));
    }

    [Fact]
    public void AccountsAreYoursByNameAfterYourTopAccount() =>
        Assert.Equal(
            new[] { ("", "Your top account"), ("201", "AshAlt"), ("101", "BirchMain"), ("301", "CedarLoose"), ("202", "DuneAlt") },
            PanelForms.AccountChoices(Everything()).Select(c => (c.Key, c.Label)).ToArray());

    [Fact]
    public void GroupWordsComeFromTheRecipeElseTheFirstWithInputsElseSource()
    {
        Assert.Equal(("clan", "clans"), PanelForms.GroupWords(Clan));
        Assert.Equal(("source", "sources"), PanelForms.GroupWords((Labs626.UrScore.Recipes.Recipe?)null));
        Assert.Equal(("clan", "clans"), PanelForms.GroupWords(Everything()));
        Assert.Equal(("clan", "clans"), PanelForms.GroupWords(Everything(), ""));
        Assert.Equal(("clan", "clans"), PanelForms.GroupWords(Everything(), Clan.Slug));
        Assert.Equal(("source", "sources"), PanelForms.GroupWords(Everything(), "uninstalled-recipe"));
        Assert.Equal(("source", "sources"), PanelForms.GroupWords(Live([], [Installed(TopList)], new Dictionary<string, RecipeSnapshot>())));
    }

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
    public void PastPeriodsCanExplicitlyDropARemovedStatWithoutTickingAnother()
    {
        var live = Live([MainClan], [Installed(Clan)], new Dictionary<string, RecipeSnapshot>());
        var current = new PanelSettings(Clan.Slug, SourceId: MainClan.Id, Stat: "removed-stat");
        var saved = PanelForms.From(current, PanelType.PastPeriods);
        var choices = PanelForms.StatChoices(PanelType.PastPeriods, live, saved, current);
        var none = Assert.Single(choices);
        Assert.Equal(new FormChoice(PanelForms.NoStatKey, "Don't show your best account"), none);
        var held = PanelForms.Pick(choices, saved.Stat, shown: true, saved: saved.Stat);
        Assert.Equal(saved.Stat, held.Held);
        Assert.Null(held.Selected);
        Assert.Equal("This panel's stat was removed. Choose another.", PanelForms.Problem(PanelType.PastPeriods, current, live));

        var settings = PanelForms.Build(PanelType.PastPeriods, saved with { Stat = none.Key }, live);

        Assert.Equal(new PanelSettings(Clan.Slug, SourceId: MainClan.Id), settings);
        Assert.Null(PanelForms.Problem(PanelType.PastPeriods, settings, live));
        Assert.Empty(live.Installed[0].State.TrackedStats(Clan));
        Assert.Equal("removed-stat", current.Stat);
        var reopened = PanelForms.From(settings, PanelType.PastPeriods);
        Assert.Equal(none, PanelForms.Pick(choices, reopened.Stat, shown: true, saved: reopened.Stat).Selected);
        Assert.Equal(settings, PanelForms.Build(PanelType.PastPeriods, reopened, live));
    }

    [Fact]
    public void PastPeriodsNoStatChoiceIsLastAndSurvivesNewlyTickedStats()
    {
        var live = Everything();
        var current = new PanelSettings(Clan.Slug, SourceId: MainClan.Id);
        var saved = PanelForms.From(current, PanelType.PastPeriods);
        var choices = PanelForms.StatChoices(PanelType.PastPeriods, live, saved, current);

        Assert.Equal(ClanStat, choices[0].Key);
        Assert.Equal(PanelForms.NoStatKey, choices[^1].Key);
        Assert.Equal(ClanStat, PanelForms.Defaults(PanelType.PastPeriods, live).Stat);
        Assert.Equal(choices[^1], PanelForms.Pick(choices, saved.Stat, shown: true, saved: saved.Stat).Selected);
        Assert.Equal(current, PanelForms.Build(PanelType.PastPeriods, saved, live));
        Assert.DoesNotContain(PanelForms.StatChoices(PanelType.MyAccounts, live, new FormValues(), null), choice => choice.Key == PanelForms.NoStatKey);
    }

    /// <summary>
    /// A Clan pace settings form offered the profile source, which has no clan and no period and so could only ever draw
    /// an empty panel (2026-09-24). Pace offers what the gallery says it needs: a group whose recipe has a total and a period.
    /// </summary>
    [Fact]
    public void PaceOffersOnlySourcesWithATotalAndAPeriod()
    {
        var live = Everything();

        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id }, Keys(PanelForms.SourceChoices(PanelType.Pace, PanelField.Source, live, new FormValues())));
        Assert.True(PanelForms.Fits(PanelType.Pace, Clan));
        Assert.False(PanelForms.Fits(PanelType.Pace, Profile));
        Assert.False(PanelForms.Fits(PanelType.Pace, TopList));
        Assert.Equal("This panel can't show that source.",
            PanelForms.Problem(PanelType.Pace, new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id), live));
    }

    [Theory]
    [InlineData(PanelType.Standing, "Choose a source.")]
    [InlineData(PanelType.Race, "Choose 1 to 5 sources.")]
    [InlineData(PanelType.PromotionCheck, "Choose a source.")]
    public void AMissingSavedRecipeDoesNotBorrowAnotherRecipesWords(PanelType type, string expected)
    {
        var live = Everything();

        Assert.Equal(expected, PanelForms.Problem(type, new PanelSettings("uninstalled-recipe"), live));
        var rebuilt = PanelForms.Build(type, new FormValues(Source: "removed-source", Sources: ["removed-source"]), live);
        Assert.Equal(expected, PanelForms.Problem(type, rebuilt, live, "uninstalled-recipe"));
        Assert.Equal("Choose a clan.", PanelForms.Problem(PanelType.Standing,
            new PanelSettings(Clan.Slug), live, "uninstalled-recipe"));
    }

    [Fact]
    public void ABlankFormStillUsesTheFirstRecipesWords()
    {
        Assert.Equal("Choose a clan.", PanelForms.Problem(PanelType.Standing, new PanelSettings(""), Everything()));
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

        // With no source pinned, the panel reads the recipe's first source that is on, else its first, as the Accounts table
        // does (final review Minor 4: one rule), so it isn't stale; the form still asks you to pin one while none is on.
        Assert.False(PanelModels.ProfileStat(live, Reader(), settings with { SourceId = null }).Head.HasStale);
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
        Assert.Equal("Choose 1 to 5 clans.", PanelForms.Problem(PanelType.Race, Clans(race: []), live));
        Assert.Null(PanelForms.Problem(PanelType.Race, Clans(race: [MainClan.Id]), live));
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

    [Fact]
    public void AnAccountsTableReadsASourceWithoutAPeriodAndKeepsNoStat()
    {
        var live = Everything();

        Assert.Equal(new[] { ProfileSource.Id }, Keys(PanelForms.SourceChoices(PanelType.AccountsTable, PanelField.Source, live, new FormValues())));

        var settings = PanelForms.Build(PanelType.AccountsTable, new FormValues(Source: ProfileSource.Id, Stat: PanelForms.StatKey(Profile.Slug, "diamonds")), live);
        Assert.Equal(new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id), settings);
        Assert.Null(PanelForms.Problem(PanelType.AccountsTable, settings, live));
        Assert.Null(PanelForms.Problem(PanelType.AccountsTable, new PanelSettings(Profile.Slug), live));
        Assert.Equal("This panel can't show that clan.", PanelForms.Problem(PanelType.AccountsTable, new PanelSettings(Clan.Slug, SourceId: MainClan.Id), live));
        Assert.Equal(new PanelSize(PanelSize.Wide), BoardDefs.DefaultSize(PanelType.AccountsTable));
    }
}
