using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeEngineTests
{
    private sealed class FakeTransport : IRecipeTransport
    {
        private readonly List<(Func<Uri, bool> Match, FetchResult Result)> _routes = [];

        public List<(Uri Url, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

        public FakeTransport On(string urlStart, int status, string body)
        {
            _routes.Add((u => u.AbsoluteUri.StartsWith(urlStart, StringComparison.Ordinal), new FetchResult(status, body, null)));
            return this;
        }

        public FakeTransport Unreachable(string urlStart, string error)
        {
            _routes.Add((u => u.AbsoluteUri.StartsWith(urlStart, StringComparison.Ordinal), new FetchResult(null, null, error)));
            return this;
        }

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            Requests.Add((url, new Dictionary<string, string>(headers)));
            var route = _routes.FirstOrDefault(r => r.Match(url));
            return Task.FromResult(route.Result ?? new FetchResult(404, "{}", null));
        }
    }

    private sealed class FakeKeys(params SavedKey[] saved) : IKeyStore
    {
        public SavedKey? Find(string keyId) => saved.FirstOrDefault(k => k.Id == keyId);
        public void Save(string keyId, string host, string value) => throw new NotSupportedException();
        public bool Remove(string keyId) => throw new NotSupportedException();
        public IReadOnlyCollection<string> Values() => [.. saved.Select(k => k.Value)];
    }

    private static Recipe PetSim => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Followers => RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;

    private static Recipe Parse(string json)
    {
        var result = RecipeParser.Parse(json);
        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        return result.Recipe!;
    }

    private static readonly Dictionary<string, string> Clan = new() { ["clan"] = "Noodle Clan" };

    private const string Battle = """{ "status": "ok", "data": { "configName": "B" } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": { "B": {
            "Place": 3, "Points": 999,
            "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ]
        } } } }
        """;

    private static Task<RecipeReading> Read(FakeTransport transport, Recipe recipe,
        IReadOnlyDictionary<string, string>? inputs = null, IReadOnlyCollection<long>? ids = null, FakeKeys? keys = null) =>
        new RecipeEngine(transport, keys ?? new FakeKeys())
            .ReadAsync(recipe, inputs ?? Clan, ids ?? [], CancellationToken.None);

    [Fact]
    public async Task ThePetSimRecipeReadsEveryContribution()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { new RecipeRow(111, 4200), new RecipeRow(222, 10) }, reading.Rows);
        Assert.Equal(2, reading.RowsSeen);
        Assert.Equal("battle=B", reading.Context);
        Assert.Equal("Clan place", reading.Headline[0].Label);
        Assert.Equal("3", reading.Headline[0].Text);
        Assert.Equal("999", reading.Headline[1].Text);
    }

    [Fact]
    public async Task NoBattleIsIdleWithTheRecipesOwnWordsAndAsksNothingMore()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "status": "ok", "data": null }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Idle, reading.Outcome);
        Assert.Equal("No clan battle running", reading.Detail);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AnEmptyBattleNameIsIdleToo()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "configName": "" } }""");
        Assert.Equal(ReadingOutcome.Idle, (await Read(transport, PetSim)).Outcome);
    }

    [Fact]
    public async Task ARenamedFieldIsAShapeMissNeverIdle()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "name": "B" } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("Step 1: No 'configName' in 'data'. Keys present: name.", reading.Detail);
    }

    [Fact]
    public async Task AMissingInputAsksForItAndMakesNoRequest()
    {
        var transport = new FakeTransport();

        var reading = await Read(transport, PetSim, inputs: new Dictionary<string, string>());

        Assert.Equal(ReadingOutcome.NeedsInput, reading.Outcome);
        Assert.Equal("Set Your clan to start.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task InputsAreEncodedIntoAddressesButTakenValuesAreUsedAsIsInPaths()
    {
        // A slash changes a request's shape if it is not escaped. A space in a battle name must
        // match the JSON key exactly.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "configName": "Big Battle" } }""")
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "Big Battle": { "PointContributions": [ { "UserID": 111, "Points": 5 } ] } } } }""");

        var reading = await Read(transport, PetSim, inputs: new Dictionary<string, string> { ["clan"] = "a/b" });

        Assert.Equal("https://ps99.biggamesapi.io/api/clan/a%2Fb", transport.Requests[1].Url.AbsoluteUri);
        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
    }

    [Fact]
    public async Task OneUnreadableRowCostsOnlyItself()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "UserID": "x", "Points": 1 }, { "UserID": 111, "Points": 5 } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(new[] { new RecipeRow(111, 5) }, reading.Rows);
        Assert.Equal(2, reading.RowsSeen);
    }

    [Fact]
    public async Task WhenNoRowCanBeReadTheFirstReasonIsKept()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "Name": "a" }, { "UserID": 1, "Points": "lots" } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("None of the 2 rows could be read: No 'UserID' in this row. Keys present: Name.", reading.Detail);
    }

    [Fact]
    public async Task TextWhereANumberBelongsSaysSo()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "UserID": 1, "Points": "lots" } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal("None of the 1 rows could be read: 'Points' is text in this row, not a number.", reading.Detail);
    }

    [Fact]
    public async Task AnEmptyListIsAReadingWithNoRows()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, """{ "data": { "Battles": { "B": { "PointContributions": [] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Rows);
    }

    [Fact]
    public async Task APerAccountRecipeAsksOncePerAccountInTurn()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 200, """{ "count": 17 }""")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1, 2]);

        Assert.Equal(new[] { "https://friends.roblox.com/v1/users/1/followers/count", "https://friends.roblox.com/v1/users/2/followers/count" },
            transport.Requests.Select(r => r.Url.AbsoluteUri));
        Assert.Equal(new[] { new RecipeRow(1, 17), new RecipeRow(2, 4) }, reading.Rows);
        Assert.Null(reading.Context);
    }

    [Fact]
    public async Task ANotFoundForOneAccountCostsOnlyThatAccount()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 404, "{}")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1, 2]);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { new RecipeRow(2, 4) }, reading.Rows);
        Assert.Equal("1 of your accounts could not be read: friends.roblox.com has nothing for user id 1.", reading.Detail);
    }

    [Fact]
    public async Task NoAccountsMeansAReadingWithNoRowsAndNoRequest()
    {
        var transport = new FakeTransport();
        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: []);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AnUnansweredRequestIsUnreachableWithItsError()
    {
        var transport = new FakeTransport().Unreachable("https://ps99.biggamesapi.io/", "Could not reach ps99.biggamesapi.io: DNS failure");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Unreachable, reading.Outcome);
        Assert.Equal("Could not reach ps99.biggamesapi.io: DNS failure", reading.Detail);
    }

    [Fact]
    public async Task TooManyRequestsIsRateLimited()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 429, "{}");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.RateLimited, reading.Outcome);
        Assert.Equal("ps99.biggamesapi.io asked us to slow down. Trying again next poll.", reading.Detail);
    }

    [Fact]
    public async Task AServerErrorIsUnreachable()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 500, "{}");
        Assert.Equal("ps99.biggamesapi.io returned 500.", (await Read(transport, PetSim)).Detail);
    }

    [Fact]
    public async Task AnUnauthorisedStepWithNoKeyNeedsSigningIn()
    {
        var transport = new FakeTransport().On("https://friends.roblox.com/", 401, "{}");
        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1]);

        Assert.Equal(ReadingOutcome.SignInRequired, reading.Outcome);
        Assert.Equal("friends.roblox.com requires signing in, which recipes cannot do.", reading.Detail);
    }

    [Fact]
    public async Task ABadRequestNamingAnInputSaysWhichValueFoundNothing()
    {
        // The live Pet Sim API answers a made-up clan with 400, not 404.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 400, """{ "error": "bad" }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.InputNotFound, reading.Outcome);
        Assert.Equal("ps99.biggamesapi.io found nothing for 'Noodle Clan' (Your clan). Check the spelling.", reading.Detail);
    }

    [Fact]
    public async Task InvalidJsonIsAShapeMiss()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 200, "<html>");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.StartsWith("ps99.biggamesapi.io did not return valid JSON:", reading.Detail);
    }

    private static Recipe KeyedRecipe(string placement) => Parse($$"""
        {
          "recipe": 1, "name": "Keyed", "credit": "Test.", "metricId": "k.v", "everySeconds": 60,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://tracker.example/keys", "in": "{{placement}}", "name": "api_key" }],
          "steps": [{ "url": "https://api.tracker.example/rows?season=1", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """);

    [Fact]
    public async Task AMissingKeyNamesItAndWhereToGetOne()
    {
        var transport = new FakeTransport();
        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>());

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("This recipe needs your Tracker key. Get one at tracker.example.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AKeyBoundToAnotherHostIsNeverSent()
    {
        var transport = new FakeTransport();
        var keys = new FakeKeys(new SavedKey("tracker", "somewhere.else.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("Your Tracker key is saved for somewhere.else.example, and this recipe would send it to api.tracker.example. It was not sent.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AHeaderKeyGoesInTheNamedHeader()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal("abc123secret", transport.Requests[0].Headers["api_key"]);
        Assert.DoesNotContain("abc123secret", transport.Requests[0].Url.AbsoluteUri);
    }

    [Fact]
    public async Task AQueryKeyIsAppendedToTheAddress()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc/123secret"));

        await Read(transport, KeyedRecipe("query"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal("https://api.tracker.example/rows?season=1&api_key=abc%2F123secret", transport.Requests[0].Url.AbsoluteUri);
        Assert.Empty(transport.Requests[0].Headers);
    }

    [Fact]
    public async Task ARejectedKeyNamesTheHostAndTheKey()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 403, "{}");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal(ReadingOutcome.KeyRejected, reading.Outcome);
        Assert.Equal("api.tracker.example rejected your Tracker key. Change it to try again.", reading.Detail);
    }
}
