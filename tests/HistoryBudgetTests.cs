using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class HistoryBudgetTests
{
    private static readonly Guid One = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");
    private static readonly Guid Two = Guid.Parse("88dc7685-3a36-4f93-b526-a9bff2d7da6c");

    [Fact]
    public void TheCountIsSendingAccountsTimesSentStatsSummedAcrossRecipes() =>
        Assert.Equal(5 * 3 + 5 * 1 + 0 * 4, HistoryBudget.Count([(5, 3, 0), (5, 1, 0), (0, 4, 0)]));

    /// <summary>
    /// A clan-and-field number is one series with no account behind it, and the budget counted it as nothing:
    /// accounts times stats is zero when there are no accounts. Six ticked clan numbers across two installed
    /// lists are six of RoRoRo's 256 slots, and a setup near the ceiling could cross it from the one screen that
    /// exists to warn about that without a word (V3-S.28). The recipe share carries the field count now, and the
    /// fixture is built so the field term is the ONLY thing that moves the total — the list recipe has no
    /// accounts and no account stats, so under the old count its share is exactly zero.
    /// </summary>
    [Fact]
    public void AClanNumberIsOneSlotEvenThoughNoAccountIsBehindIt()
    {
        var clansText = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var clans = new InstalledRecipe(RecipeParser.Parse(clansText).Recipe!, clansText,
            new RecipeState(SentFieldMetrics: [FieldMetrics.Points, FieldMetrics.Place, FieldMetrics.ThreatGap]));
        var profileText = RecipeParserTests.Fixture("petsim99-profile.recipe.json");
        var profile = new InstalledRecipe(RecipeParser.Parse(profileText).Recipe!, profileText, new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds") }));

        var shares = HistoryBudget.Installed([profile, clans], [One, Two]);

        Assert.Equal(new[] { (2, 1, 0), (0, 0, 3) }, shares.ToArray());
        Assert.Equal(2 * 1 + 3, HistoryBudget.Count(shares));
    }

    /// <summary>
    /// The field save runs the same refusal the stat save does. Two hundred and fifty-four slots in use elsewhere,
    /// two clan numbers ticked: allowed, at the limit exactly. Three: refused, and the line says so. Lowering an
    /// already-too-high count is allowed, as it is for stats, so the way back under is never blocked.
    /// </summary>
    [Fact]
    public void TickingAClanNumberPastTheLimitIsRefusedLikeAStat()
    {
        var others = new[] { (254, 1, 0) };

        Assert.True(HistoryBudget.Check(others, (0, 0, 0), (0, 0, 2), accountsKnown: true).Allowed);
        var refused = HistoryBudget.Check(others, (0, 0, 0), (0, 0, 3), accountsKnown: true);
        Assert.False(refused.Allowed);
        Assert.Equal(257, refused.Count);
        Assert.Contains("Not allowed", refused.Line);
        Assert.True(HistoryBudget.Check(others, (0, 0, 6), (0, 0, 5), accountsKnown: true).Allowed);
    }

    [Fact]
    public void AnInstalledRecipesShareUsesItsOwnSendTicks()
    {
        var profileText = RecipeParserTests.Fixture("petsim99-profile.recipe.json");
        var profile = new InstalledRecipe(RecipeParser.Parse(profileText).Recipe!, profileText, new RecipeState(
            ExcludedAccountIds: [Two.ToString()],
            Stats: new Dictionary<string, StatChoice>
            {
                ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
                ["rank"] = new(Send: true, MetricId: "ps99.rank"),
                ["eggs"] = new(Show: true, MetricId: "ps99.eggs-hatched"),
            }));
        var clanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var clan = new InstalledRecipe(RecipeParser.Parse(clanText).Recipe!, clanText, new RecipeState());

        Assert.Equal(new[] { (1, 2, 0), (2, 0, 0) }, HistoryBudget.Installed([profile, clan], [One, Two]).ToArray());
        Assert.Equal(new[] { (2, 0, 0) }, HistoryBudget.Installed([profile, clan], [One, Two], exceptSlug: profile.Recipe.Slug).ToArray());
    }

    [Fact]
    public void UnderTheWarningTheLineStatesTheCountAndItsBasis()
    {
        var check = HistoryBudget.Check([(5, 3, 0)], before: (5, 4, 0), after: (5, 5, 0), accountsKnown: true);

        Assert.True(check.Allowed);
        Assert.Equal(40, check.Count);
        Assert.Equal("40 of RoRoRo's 256 history slots: accounts with Send on times stats with Send on, plus each clan number sent, across your installed recipes.", check.Line);
    }

    [Fact]
    public void FromTwoHundredTheLineWarnsThatOtherPluginsShareTheSlots()
    {
        var check = HistoryBudget.Check([(10, 15, 0)], before: (10, 4, 0), after: (10, 5, 0), accountsKnown: true);

        Assert.True(check.Allowed);
        Assert.Equal(200, check.Count);
        Assert.EndsWith(" Other plugins share these slots, so leave room for them.", check.Line);
    }

    [Fact]
    public void ExactlyTheLimitIsAllowed() =>
        Assert.True(HistoryBudget.Check([(8, 16, 0)], before: (8, 15, 0), after: (8, 16, 0), accountsKnown: true).Allowed);

    [Fact]
    public void AChangePastTheLimitIsRefusedAndSaysWhy()
    {
        var check = HistoryBudget.Check([(8, 16, 0)], before: (8, 16, 0), after: (8, 17, 0), accountsKnown: true);

        Assert.False(check.Allowed);
        Assert.Equal(264, check.Count);
        Assert.StartsWith("Not allowed: that would use 264 of RoRoRo's 256 history slots.", check.Line);
    }

    [Fact]
    public void LoweringACountThatIsAlreadyTooHighIsAllowed() =>
        Assert.True(HistoryBudget.Check([(10, 20, 0)], before: (10, 8, 0), after: (10, 7, 0), accountsKnown: true).Allowed);

    /// <summary>The profile fixture sending diamonds, eggs and rank (3 stats), and the clan fixture sending its points (1).</summary>
    private static InstalledRecipe[] FourStatsPerAccount()
    {
        var profileText = RecipeParserTests.Fixture("petsim99-profile.recipe.json");
        var profile = new InstalledRecipe(RecipeParser.Parse(profileText).Recipe!, profileText, new RecipeState(
            Stats: new Dictionary<string, StatChoice>
            {
                ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
                ["eggs"] = new(Send: true, MetricId: "ps99.eggs-hatched"),
                ["rank"] = new(Send: true, MetricId: "ps99.rank"),
            }));
        var clanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var clan = new InstalledRecipe(RecipeParser.Parse(clanText).Recipe!, clanText, new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["value"] = new(Send: true, MetricId: "clan.battle.points") }));

        return [profile, clan];
    }

    private static Guid[] Accounts(int count) => [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())];

    [Fact]
    public void AfterSeedIsQuietWithinTheLimit() =>
        Assert.Null(HistoryBudget.AfterSeed(FourStatsPerAccount(), Accounts(64)));

    [Fact]
    public void AfterSeedSaysSoPastTheLimit()
    {
        var check = HistoryBudget.AfterSeed(FourStatsPerAccount(), Accounts(65));

        Assert.NotNull(check);
        Assert.False(check.Allowed);
        Assert.Equal(260, check.Count);
        Assert.Equal("260 of RoRoRo's 256 history slots are in use, so RoRoRo will ignore the newest. Untick Send on some stats or accounts.", check.Line);
    }

    [Fact]
    public void WithNoAccountsKnownYetTheLineSaysSo()
    {
        var check = HistoryBudget.Check([], before: (0, 0, 0), after: (0, 3, 0), accountsKnown: false);

        Assert.True(check.Allowed);
        Assert.Equal("0 of RoRoRo's 256 history slots so far. RoRoRo hasn't been reached yet, so your accounts count as 0 until it is.", check.Line);
    }
}
