using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertsModelTests
{
    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    [Fact]
    public void ThePolicyLineIsReportPolicysOwnSentence()
    {
        var state = new RecipeState(
            ExcludedAccountIds: [Alt.AccountId.ToString()],
            Stats: new Dictionary<string, StatChoice> { ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds") });

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(Profile, "", state)], [Main, Alt], [], resolveNames: false, _ => (3, 1)));

        var expected = new ReportPolicy([new SentStat("diamonds", "Diamonds", "ps99.diamonds")], new HashSet<Guid> { Main.AccountId }).Describe(2, false);
        Assert.Equal(new PolicyItem("Pet Sim 99 profile", expected, "Sent 3, dropped 1 this session."), item);
    }

    [Fact]
    public void ARecipeYouOnlyWatchSaysItSendsNothing()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Send: true, MetricId: "clan.battle.points") });
        var watched = new Source("s-00000001", clan.Slug, new Dictionary<string, string> { ["clan"] = "NovaForge" }, SourceRole.Watch);

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(clan, "", state)], [Main, Alt], [watched], resolveNames: false, _ => (0, 0)));

        Assert.Equal("Nothing is sent to RoRoRo: you only watch its clans.", item.Line);
    }

    /// <summary>
    /// Ticking a clan number has to produce somewhere to set the alert on it. 0.5.0 shipped six metrics a member
    /// could tick and then had nowhere to add a rule for — which was the entire point of them — because Build
    /// filtered group lists out. Found by review before the clan saw it.
    /// </summary>
    [Fact]
    public void ATickedClanNumberGetsAnAlertCard()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var state = new RecipeState(SentFieldMetrics: [FieldMetrics.GapAbove, FieldMetrics.Points]);

        var view = AlertCards.Build([new InstalledRecipe(clans, "", state)], new RulesRead(RulesProblem.None, true, [], []));

        Assert.Equal(
            ["clan.standing.points", "clan.standing.gap-above"],
            view.Cards.Select(c => c.Stat.MetricId));
        Assert.All(view.Cards, c => Assert.True(c.Stat.Sent));
        Assert.All(view.Cards, c => Assert.NotEmpty(c.CanAdd));
        Assert.Equal("Points behind the place above", view.Cards[1].Stat.Label);
        Assert.Equal("", AlertCards.EmptyLine([new InstalledRecipe(clans, "", state)], view));
    }

    /// <summary>
    /// The clan's name rides on the label, and the label is what RoRoRo puts at the head of the alert's
    /// title. A clan number carries no account, so without this the buzz says "Clan points went above
    /// ..." and never says whose. The ids stay fixed and shared; only the label is local — which is the
    /// split that lets forty people set the same alert and each see their own clan on their own phone.
    /// </summary>
    [Fact]
    public void AClanNumbersLabelCarriesYourClansName()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var state = new RecipeState(SentFieldMetrics: [FieldMetrics.Points, FieldMetrics.IdleMembers]);

        var view = AlertCards.Build([new InstalledRecipe(clans, "", state)], new RulesRead(RulesProblem.None, true, [], []), "K0i2");

        Assert.Equal(["K0i2 clan points", "K0i2 members on zero"], view.Cards.Select(c => c.Stat.Label));

        // The id is the same for everyone, whatever their clan is called.
        Assert.Equal(["clan.standing.points", "clan.standing.idle-members"], view.Cards.Select(c => c.Stat.MetricId));
    }

    /// <summary>No clan set up yet is no prefix, rather than a stray space or the word "null".</summary>
    [Fact]
    public void WithNoClanTheLabelIsJustTheNumbersName()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var state = new RecipeState(SentFieldMetrics: [FieldMetrics.Points]);
        var installed = new[] { new InstalledRecipe(clans, "", state) };

        foreach (var clan in new[] { null, "", "   " })
        {
            var view = AlertCards.Build(installed, new RulesRead(RulesProblem.None, true, [], []), clan);
            Assert.Equal("Clan points", Assert.Single(view.Cards).Stat.Label);
        }
    }

    /// <summary>With nothing ticked anywhere, the empty line points at both places a tick lives.</summary>
    [Fact]
    public void TheEmptyLineNamesTheClanAndFieldSectionToo()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var installed = new[] { new InstalledRecipe(clans, "", new RecipeState()) };

        var view = AlertCards.Build(installed, new RulesRead(RulesProblem.None, true, [], []));

        Assert.Empty(view.Cards);
        Assert.Contains("Clan and field", AlertCards.EmptyLine(installed, view), StringComparison.Ordinal);
    }

    /// <summary>
    /// A clans list sends for no account, but from 0.5.0 it may send the clan-and-field numbers you ticked — so it
    /// gets a card, and the card names them. It used to be filtered out of this window altogether.
    /// </summary>
    [Fact]
    public void AClansListsCardNamesTheClanNumbersItSends()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var state = new RecipeState(SentFieldMetrics: [FieldMetrics.Place, FieldMetrics.PaceNeeded]);

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(clans, "", state)], [Main, Alt], [], resolveNames: false, _ => (7, 0)));

        Assert.Equal("Pet Sim 99 top clans", item.RecipeName);
        Assert.Contains("clan.standing.place", item.Line, StringComparison.Ordinal);
        Assert.Contains("clan.standing.pace-needed", item.Line, StringComparison.Ordinal);
        Assert.Contains("with no account attached", item.Line, StringComparison.Ordinal);
        Assert.Contains("no player's id or number ever leaves this plugin", item.Line, StringComparison.Ordinal);
        Assert.Equal("Sent 7, dropped 0 this session.", item.Counts);
    }

    [Fact]
    public void AClansListWithNothingTickedSaysWhereToTickIt()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(clans, "", new RecipeState())], [Main], [], resolveNames: false, _ => (0, 0)));

        Assert.Equal(
            "Nothing is sent to RoRoRo from this list: no clan number is ticked. Tick one in Setup › Stats, under Clan and field.",
            item.Line);
    }
}
