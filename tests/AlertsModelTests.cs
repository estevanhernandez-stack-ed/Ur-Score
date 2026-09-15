using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertsModelTests
{
    private const string MetricId = "ps99.diamonds";

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static string TempFile(string? text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-alerts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "file.json");
        if (text is not null) File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void NoRulesFileCanBeCreatedByAddingOne() =>
        Assert.Equal(
            ("RoRoRo has no rules file yet, so nothing can alert until a rule is added. Adding one creates the file.", true),
            AlertsModel.RuleSentence(MetricId, TempFile(null), TempFile(null)));

    [Fact]
    public void RulesWithoutThisMetricSayReportsWillNeverAlert() =>
        Assert.Equal(
            ($"RoRoRo has rules, but none for {MetricId} — so reports will land and never alert.", true),
            AlertsModel.RuleSentence(MetricId, TempFile("""[ { "metricId": "other.one", "kind": "Rate", "threshold": 1 } ]"""), TempFile(null)));

    [Fact]
    public void OurRuleIntactIsReady() =>
        Assert.Equal(
            ($"Ready: RoRoRo has a rule for {MetricId} at 100.", false),
            AlertsModel.RuleSentence(MetricId,
                TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 100, "owner": "626labs.ur-score" } ]"""),
                TempFile(null)));

    [Fact]
    public void OurRuleChangedSinceWeAddedItIsLeftAlone() =>
        Assert.Equal(
            ($"Your rule for {MetricId} has been changed since Ur Score added it (now 50, was 100). Left exactly as it is.", false),
            AlertsModel.RuleSentence(MetricId,
                TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 50, "owner": "626labs.ur-score" } ]"""),
                TempFile($$"""{ "{{MetricId}}": 100 }""")));

    [Fact]
    public void AUsersOwnRuleIsNeverTouched() =>
        Assert.Equal(
            ($"You wrote the rule for {MetricId} yourself (threshold 25). Ur Score will not touch it.", false),
            AlertsModel.RuleSentence(MetricId, TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 25 } ]"""), TempFile(null)));

    [Fact]
    public void AnUnreadableFileIsNeverOverwritten() =>
        Assert.Equal(
            ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            AlertsModel.RuleSentence(MetricId, TempFile("not json"), TempFile(null)));

    [Fact]
    public void RuleChoicesAreEverySentStatOnce()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
            ["rank"] = new(Show: true, Send: true, MetricId: "ps99.rank"),
            ["eggs"] = new(Show: true, MetricId: "ps99.eggs-hatched"),
        });

        var choices = AlertsModel.Choices([new InstalledRecipe(Profile, "", state), new InstalledRecipe(Profile, "", state)]);

        Assert.Equal(new[] { "Diamonds (ps99.diamonds)", "Player rank (ps99.rank)" }, choices.Select(c => c.ToString()).ToArray());
    }

    [Fact]
    public void ThePolicyLineIsReportPolicysOwnSentence()
    {
        var state = new RecipeState(
            ExcludedAccountIds: [Alt.AccountId.ToString()],
            Stats: new Dictionary<string, StatChoice> { ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds") });

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(Profile, "", state)], [Main, Alt], resolveNames: false, _ => (3, 1)));

        var expected = new ReportPolicy([new SentStat("diamonds", "Diamonds", "ps99.diamonds")], new HashSet<Guid> { Main.AccountId }).Describe(2, false);
        Assert.Equal(new PolicyItem("Pet Sim 99 profile", expected, "Sent 3, dropped 1 this session."), item);
    }

    [Fact]
    public void ThePreviewShowsOnlyWhenARuleCanBeAdded()
    {
        Assert.Equal("Rate, below 100 per minute over 10 minutes", AlertsModel.Preview(true));
        Assert.Equal("", AlertsModel.Preview(false));
    }
}
