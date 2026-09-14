using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class StatRulesTests
{
    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static InstalledRecipe Installed(string fixture, RecipeState state)
    {
        var text = RecipeParserTests.Fixture(fixture);
        return new InstalledRecipe(RecipeParser.Parse(text).Recipe!, text, state);
    }

    private static Dictionary<string, StatChoice> Choices(params (string Key, StatChoice Choice)[] choices) =>
        choices.ToDictionary(c => c.Key, c => c.Choice, StringComparer.Ordinal);

    [Fact]
    public void ChoicesWithATickAndANameHaveNoProblems() =>
        Assert.Empty(StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(Show: true, MetricId: "ps99.diamonds"))), []));

    [Fact]
    public void NothingTickedIsRefused()
    {
        // An entry with a name but no tick still ticks nothing.
        var problems = StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(MetricId: "ps99.diamonds"))), []);
        Assert.Equal(new[] { StatRules.TickOne }, problems);
    }

    [Fact]
    public void ATickedStatNeedsAName() =>
        Assert.Equal(new[] { "Give Diamonds a name RoRoRo uses." },
            StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(Send: true, MetricId: "  "))), []));

    [Fact]
    public void TwoStatsWithOneNameAreRefused()
    {
        var problems = StatRules.Problems(Profile, Choices(
            ("diamonds", new StatChoice(Send: true, MetricId: "ps99.same")),
            ("rank", new StatChoice(Show: true, MetricId: "ps99.same"))), []);

        Assert.Equal(new[] { "Diamonds and Player rank use the same name, ps99.same. Give each stat its own." }, problems);
    }

    [Fact]
    public void AMetricIdAnotherInstalledRecipeSendsIsRefusedNamingThatRecipe()
    {
        var clan = Installed("petsim99-clan-battle.recipe.json",
            new RecipeState(Stats: Choices(("value", new StatChoice(Send: true, MetricId: "ps99.diamonds")))));

        var problems = StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(Send: true, MetricId: "ps99.diamonds"))), [clan]);

        Assert.Equal(new[] { "ps99.diamonds is already sent by Pet Sim 99 clan battle points. Give this stat a different name." }, problems);
    }

    [Fact]
    public void AnotherRecipeThatOnlyShowsTheNameIsNoCollision()
    {
        var clan = Installed("petsim99-clan-battle.recipe.json",
            new RecipeState(Stats: Choices(("value", new StatChoice(Show: true, MetricId: "ps99.diamonds")))));

        Assert.Empty(StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(Send: true, MetricId: "ps99.diamonds"))), [clan]));
    }

    [Fact]
    public void TheRecipesOwnSavedStateIsNotACollision()
    {
        // Saving settings again for the same recipe must not collide with what it already sends.
        var self = Installed("petsim99-profile.recipe.json",
            new RecipeState(Stats: Choices(("diamonds", new StatChoice(Send: true, MetricId: "ps99.diamonds")))));

        Assert.Empty(StatRules.Problems(Profile, Choices(("diamonds", new StatChoice(Send: true, MetricId: "ps99.diamonds"))), [self]));
    }
}
