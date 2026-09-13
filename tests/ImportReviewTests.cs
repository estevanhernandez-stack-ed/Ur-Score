using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ImportReviewTests
{
    private sealed class FakeKeys(params SavedKey[] saved) : IKeyStore
    {
        public SavedKey? Find(string keyId) => saved.FirstOrDefault(k => k.Id == keyId);
        public void Save(string keyId, string host, string value) => throw new NotSupportedException();
        public bool Remove(string keyId) => throw new NotSupportedException();
        public IReadOnlyCollection<string> Values() => [.. saved.Select(k => k.Value)];
    }

    private static Recipe Load(string fixture) => RecipeParser.Parse(RecipeParserTests.Fixture(fixture)).Recipe!;

    private static Recipe Parse(string json) => RecipeParser.Parse(json).Recipe!;

    private const string Keyed = """
        {
          "recipe": 1, "name": "Keyed", "credit": "Test.", "metricId": "k.v", "everySeconds": 60,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://tracker.example/keys", "in": "header", "name": "x-api-key" }],
          "steps": [{ "url": "https://api.tracker.example/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """;

    [Fact]
    public void PetSimContactsOneHostAndSendsOnlyTheClanYouEnter()
    {
        // A list recipe never sends your user ids anywhere: it finds your rows in what comes back.
        var review = ImportReview.Review(Load("petsim99-clan-battle.recipe.json"), new FakeKeys());

        var host = Assert.Single(review.Hosts);
        Assert.Equal("ps99.biggamesapi.io", host.Host);
        Assert.Equal(new[] { "the value you enter for Your clan" }, host.Sends);
        Assert.True(review.CanImport);
    }

    [Fact]
    public void APerAccountRecipeSaysItSendsYourUserIds()
    {
        var review = ImportReview.Review(Load("roblox-followers.recipe.json"), new FakeKeys());

        var host = Assert.Single(review.Hosts);
        Assert.Equal("friends.roblox.com", host.Host);
        Assert.Equal(new[] { ImportReview.SendsUserIds }, host.Sends);
    }

    [Fact]
    public void AHostThatOnlyServesAListReceivesNothingAboutYou()
    {
        var recipe = Parse("""
            {
              "recipe": 1, "name": "Two hosts", "credit": "Test.", "metricId": "t.v", "everySeconds": 60,
              "inputs": [{ "id": "clan", "label": "Your clan", "search": { "url": "https://lists.example/clans", "list": "data" } }],
              "steps": [{ "url": "https://api.example/clan/{clan}", "rows": "data", "userId": "id", "value": "v" }]
            }
            """);

        var review = ImportReview.Review(recipe, new FakeKeys());

        Assert.Equal(new[] { ImportReview.SendsNothing }, review.Hosts.Single(h => h.Host == "lists.example").Sends);
        Assert.Equal(new[] { "the value you enter for Your clan" }, review.Hosts.Single(h => h.Host == "api.example").Sends);
    }

    [Fact]
    public void AKeyedRecipeNamesTheKeyItSends()
    {
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys());
        Assert.Equal(new[] { "your Tracker key" }, Assert.Single(review.Hosts).Sends);
        Assert.Empty(review.ReusedKeys);
    }

    [Fact]
    public void ASavedKeyForTheSameHostIsReusedAndSaidSo()
    {
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret")));

        Assert.True(review.CanImport);
        Assert.Equal(new[] { "Uses your saved Tracker key for api.tracker.example." }, review.ReusedKeys);
    }

    [Fact]
    public void ASavedKeyBoundElsewhereRefusesTheImportNamingBothHosts()
    {
        // The doctored-copy case: same key id, pointed at a different host.
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys(new SavedKey("tracker", "tracker.real.example", "abc123secret")));

        Assert.False(review.CanImport);
        Assert.Equal(
            "This recipe would send your saved 'tracker' key to api.tracker.example, but that key is saved for tracker.real.example. "
            + "It was not imported. If this really is a different key, remove the saved one first.",
            Assert.Single(review.Refusals));
    }

    [Fact]
    public void AFirstImportIsNotAnUpdateAndAsks()
    {
        var comparison = ImportReview.CompareToInstalled(null, Load("petsim99-clan-battle.recipe.json"), new FakeKeys());
        Assert.False(comparison.IsUpdate);
        Assert.True(comparison.AsksAgain);
    }

    [Fact]
    public void AnUpdateThatContactsTheSameHostsWithTheSameThingsDoesNotAskAgain()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with { EverySeconds = 300 };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.IsUpdate);
        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Polls every 300s instead of 180s." }, comparison.Changes);
    }

    [Fact]
    public void AMetricIdChangeIsListed()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with
        {
            Steps = [installed.Steps[0], installed.LastStep with { Values = [installed.LastStep.Values[0] with { MetricId = "clan.points" }] }],
        };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.IsUpdate);
        Assert.False(comparison.AsksAgain);
        Assert.Contains("Suggests metric id clan.points instead of clan.battle.points.", comparison.Changes);
    }

    [Fact]
    public void AnUpdateThatContactsANewHostAsksAgainAndSaysWhatIsNew()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with
        {
            Steps = [installed.Steps[0], installed.Steps[1] with { Url = "https://mirror.example/api/clan/{clan}" }],
        };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.AsksAgain);
        Assert.Contains("New: mirror.example receives the value you enter for Your clan", comparison.Changes);
        Assert.Contains("No longer: ps99.biggamesapi.io receives the value you enter for Your clan", comparison.Changes);
    }
}
