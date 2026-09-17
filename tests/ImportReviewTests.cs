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

    /// <summary>The profile fixture's first three values (diamonds, eggs, rank): the update tests below describe that recipe.</summary>
    private static Recipe Profile => FirstThree(Load("petsim99-profile.recipe.json"));

    private static Recipe FirstThree(Recipe recipe) => WithValues(recipe, [.. recipe.LastStep.Values.Take(3)]);

    /// <summary>The same recipe with its last step's values replaced.</summary>
    private static Recipe WithValues(Recipe recipe, params RecipeValue[] values) =>
        recipe with { Steps = [.. recipe.Steps.Take(recipe.Steps.Count - 1), recipe.LastStep with { Values = values }] };

    private const string Keyed = """
        {
          "recipe": 1, "name": "Keyed", "credit": "Test.", "metricId": "k.v", "everySeconds": 60,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://tracker.example/keys", "in": "header", "name": "x-api-key" }],
          "steps": [{ "url": "https://api.tracker.example/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """;

    [Fact]
    public void PetSimSendsOnlyTheClanYouEnterToItsSource()
    {
        // A list recipe never sends your user ids anywhere: it finds your rows in what comes back.
        var review = ImportReview.Review(Load("petsim99-clan-battle.recipe.json"), new FakeKeys());

        var host = review.Hosts.Single(h => h.Host == "ps99.biggamesapi.io");
        Assert.Equal(new[] { "the value you enter for Your clan" }, host.Sends);
        Assert.True(review.CanImport);
    }

    [Fact]
    public void AnIconNamesRobloxsTwoPictureHostsAndWhatEach()
    {
        var review = ImportReview.Review(Load("petsim99-clan-battle.recipe.json"), new FakeKeys());

        Assert.Equal(new[] { "ps99.biggamesapi.io", "thumbnails.roblox.com", "tr.rbxcdn.com" }, review.Hosts.Select(h => h.Host).ToArray());
        Assert.Equal("Receives the picture's id, to find the icon.", ImportReview.SendsText(review.Hosts[1]));
        Assert.Equal("Sends the picture.", ImportReview.SendsText(review.Hosts[2]));
    }

    [Fact]
    public void APerAccountRecipeSaysItSendsTheUserIdOfEveryAccount()
    {
        var review = ImportReview.Review(Load("roblox-followers.recipe.json"), new FakeKeys());

        var host = Assert.Single(review.Hosts);
        Assert.Equal("friends.roblox.com", host.Host);
        Assert.Equal("Receives the Roblox user id of every account in your RoRoRo list.", ImportReview.SendsText(host));
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
    public void ASuggestedMetricIdChangeIsListed()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = WithValues(installed, installed.LastStep.Values[0] with { MetricId = "clan.points" });

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.IsUpdate);
        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Suggests clan.points for Points instead of clan.battle.points." }, comparison.Changes);
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

    // Stats design §7.2, one test per row of its table.

    [Fact]
    public void ANewStatIsListedWithoutAsking()
    {
        var installed = WithValues(Profile, Profile.LastStep.Values[0]);

        var comparison = ImportReview.CompareToInstalled(installed, Profile, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "New stat: Eggs hatched.", "New stat: Player rank." }, comparison.Changes);
    }

    [Fact]
    public void RemovingAStatYouDoNotSendIsListedWithoutAsking()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Show: true, MetricId: "ps99.rank") });
        var incoming = WithValues(Profile, Profile.LastStep.Values[0], Profile.LastStep.Values[1]);

        var comparison = ImportReview.CompareToInstalled(Profile, incoming, new FakeKeys(), state);

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Removed stat: Player rank." }, comparison.Changes);
    }

    [Fact]
    public void ATrackedStatReadFromADifferentPlaceIsListedWithoutAsking()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Show: true, Send: true, MetricId: "ps99.rank") });
        var values = Profile.LastStep.Values;
        var incoming = WithValues(Profile, values[0], values[1], values[2] with { Path = "data.views.profile.data.PlayerRank" });

        var comparison = ImportReview.CompareToInstalled(Profile, incoming, new FakeKeys(), state);

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Player rank is read from a different place." }, comparison.Changes);
        Assert.Equal("ps99.rank", Assert.Single(state.SentStats(incoming)).MetricId);
    }

    [Fact]
    public void RemovingAStatYouSendAsksAgainAndNamesWhatRoRoRoStopsGetting()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Send: true, MetricId: "ps99.rank") });
        var incoming = WithValues(Profile, Profile.LastStep.Values[0], Profile.LastStep.Values[1]);

        var comparison = ImportReview.CompareToInstalled(Profile, incoming, new FakeKeys(), state);

        Assert.True(comparison.AsksAgain);
        Assert.Equal(new[] { "Player rank will no longer be read, so RoRoRo stops getting ps99.rank." }, comparison.Changes);
    }

    [Fact]
    public void AStatOfferedAgainComesBackUnticked()
    {
        // v2 drops rank, the user accepts, and the save normalizes it. v3 offers rank again: listed, unticked, no ask.
        var dir = Path.Combine(Path.GetTempPath(), "urscore-recipes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var v2 = WithValues(Profile, Profile.LastStep.Values[0], Profile.LastStep.Values[1]);
            var store = new RecipeStore(dir);
            store.Save(v2, RecipeParserTests.Fixture("petsim99-profile.recipe.json"), new RecipeState(Stats: new Dictionary<string, StatChoice>
            {
                ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
                ["rank"] = new(Send: true, MetricId: "ps99.rank"),
            }));
            var saved = store.Find(v2.Slug)!.State;
            var v3 = Profile;

            var comparison = ImportReview.CompareToInstalled(v2, v3, new FakeKeys(), saved);

            Assert.False(comparison.AsksAgain);
            Assert.Equal(new[] { "New stat: Player rank." }, comparison.Changes);
            Assert.DoesNotContain("rank", saved.SentStats(v3).Select(s => s.Key));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AnIconAddedAsksAgainBecauseItContactsRobloxsPictureHosts()
    {
        var incoming = Load("petsim99-clan-battle.recipe.json");
        var installed = incoming with { Icon = null };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.AsksAgain);
        Assert.Equal(new[] { "Adds an icon, which asks Roblox for the picture." }, comparison.Changes);
    }

    [Fact]
    public void AStatThatNowCountsOrReadsAsTimeIsListedWithoutAsking()
    {
        var values = Profile.LastStep.Values;
        var incoming = WithValues(Profile, values[0], values[1] with { Count = true }, values[2] with { Format = StatFormat.Duration });

        var comparison = ImportReview.CompareToInstalled(Profile, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Eggs hatched now counts entries.", "Player rank is shown as a duration instead of a number." }, comparison.Changes);
    }

    [Fact]
    public void AChangeInWhatAnAnswerMeansIsListedOnceWithoutAsking()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with
        {
            PlaceLabel = "Rank in clan",
            Steps = [installed.Steps[0], installed.LastStep with { AbsentMessage = "Not in this one." }],
        };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Changes what an empty answer means." }, comparison.Changes);
    }

    [Fact]
    public void AddingPeriodTrackingAndPastHistoryListsBothWithoutAsking()
    {
        var incoming = Load("petsim99-clan-battle.recipe.json");
        var installed = incoming with { Period = null };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Now tracks the current battle.", "Now reads past battles." }, comparison.Changes);
    }

    [Fact]
    public void AddingPeriodTrackingWithoutHistoryListsOnlyTheCurrentPeriod()
    {
        var recipe = Load("petsim99-clan-battle.recipe.json");
        var incoming = recipe with { Period = recipe.Period! with { Past = null } };

        var comparison = ImportReview.CompareToInstalled(incoming with { Period = null }, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Now tracks the current battle." }, comparison.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddingOrRemovingOnlyHistoryDoesNotClaimTheCurrentPeriodChanged(bool adding)
    {
        var withHistory = Load("petsim99-clan-battle.recipe.json");
        var withoutHistory = withHistory with { Period = withHistory.Period! with { Past = null } };

        var comparison = ImportReview.CompareToInstalled(adding ? withoutHistory : withHistory,
            adding ? withHistory : withoutHistory, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { adding ? "Now reads past battles." : "No longer reads past battles. Your score book is kept." }, comparison.Changes);
    }

    [Fact]
    public void RemovingPeriodTrackingUsesTheOldRecipesWordsAndKeepsTheBook()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");

        var comparison = ImportReview.CompareToInstalled(installed, installed with { Period = null }, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "No longer tracks the current battle.", "No longer reads past battles. Your score book is kept." }, comparison.Changes);
    }

    [Fact]
    public void AHistoryPathChangeIsListedWithoutClaimingHistoryIsNew()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with { Period = installed.Period! with { Past = "data.Archive" } };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Past battles are read from a different place." }, comparison.Changes);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("starts")]
    [InlineData("ends")]
    public void ACurrentPeriodDefinitionChangeIsListedOnce(string field)
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var period = installed.Period!;
        var incoming = installed with
        {
            Period = field switch
            {
                "value" => period with { Value = "season" },
                "starts" => period with { Starts = null },
                _ => period with { Ends = null },
            },
        };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { field == "value" ? "Changes how the current season is read." : "Changes how the current battle is read." }, comparison.Changes);
    }

    [Fact]
    public void PeriodMessagesUseTheRecipesOwnWords()
    {
        var recipe = Load("petsim99-clan-battle.recipe.json");
        var incoming = recipe with { Period = recipe.Period! with { Value = "season" } };

        var comparison = ImportReview.CompareToInstalled(incoming with { Period = null }, incoming, new FakeKeys());

        Assert.Equal(new[] { "Now tracks the current season.", "Now reads past seasons." }, comparison.Changes);
    }

    [Fact]
    public void EqualPeriodDefinitionsProduceNoChangeEvenAsSeparateInstances()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with { Period = installed.Period! with { } };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.False(comparison.AsksAgain);
        Assert.Empty(comparison.Changes);
    }
}
