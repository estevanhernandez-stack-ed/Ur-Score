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

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Recipe Parse(string json)
    {
        var result = RecipeParser.Parse(json);
        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        return result.Recipe!;
    }

    /// <summary>A row holding one stat under the shorthand id, which is what a single-value recipe reads.</summary>
    internal static RecipeRow Row(long userId, double value) => new(userId, new Dictionary<string, double> { ["value"] = value });

    private static readonly HashSet<string> ValueOnly = ["value"];

    private static readonly HashSet<string> ProfileStats = ["diamonds", "eggs", "rank"];

    private static readonly Dictionary<string, string> Clan = new() { ["clan"] = "Noodle Clan" };

    private static readonly Dictionary<string, string> NoInputs = [];

    private const string Battle = """{ "status": "ok", "data": { "configName": "B" } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Icon": "rbxassetid://14976358748", "Battles": { "B": {
            "Place": 3, "Points": 999,
            "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ]
        } } } }
        """;

    private const string ProfileUrl1 = "https://ps99.biggamesapi.io/v1/players/1?";

    private const string ProfileUrl2 = "https://ps99.biggamesapi.io/v1/players/2?";

    private static string ProfileResponse(string profileData) =>
        $$"""{ "status": "ok", "data": { "views": { "profile": { "available": true, "data": { {{profileData}} } } } } }""";

    private const string FullProfile = """
        "Currency": { "Diamonds": { "_am": 9169613101 } }, "EggsHatched": 5000, "Rank": 12,
        "Statistics": { "Huge Pets Opened": 3, "Best Zone": "Tech", "Eggs Opened": "12", "Pets.Huge": 1 }
        """;

    private const string PrivateProfile = """{ "status": "ok", "data": { "views": { "profile": { "available": false, "reason": "not_public" } } } }""";

    private static Task<RecipeReading> Read(FakeTransport transport, Recipe recipe,
        IReadOnlyDictionary<string, string>? inputs = null, IReadOnlyCollection<long>? ids = null, FakeKeys? keys = null,
        IReadOnlySet<string>? tracked = null) =>
        new RecipeEngine(transport, keys ?? new FakeKeys())
            .ReadAsync(recipe, inputs ?? Clan, ids ?? [], tracked ?? ValueOnly, CancellationToken.None);

    [Fact]
    public async Task ThePetSimRecipeReadsEveryContribution()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { Row(111, 4200), Row(222, 10) }, reading.Rows);
        Assert.Equal(2, reading.RowsSeen);
        Assert.Equal("battle=B", reading.Context);
        Assert.Equal("Clan place", reading.Headline[0].Label);
        Assert.Equal("3", reading.Headline[0].Text);
        Assert.Equal("999", reading.Headline[1].Text);
        Assert.Equal("rbxassetid://14976358748", reading.IconText);
        Assert.Empty(reading.StatMisses);
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

        var reading = await Read(transport, PetSim, inputs: NoInputs);

        Assert.Equal(ReadingOutcome.NeedsInput, reading.Outcome);
        Assert.Equal("Set Your clan to start.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task NothingTrackedMakesNoRequest()
    {
        // Nothing is ticked by default, so a recipe nobody has chosen stats for reads nothing.
        var transport = new FakeTransport();

        var reading = await Read(transport, PetSim, tracked: new HashSet<string>());

        Assert.Equal(ReadingOutcome.NeedsInput, reading.Outcome);
        Assert.Equal(RecipeEngine.NothingTracked, reading.Detail);
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

        Assert.Equal(new[] { Row(111, 5) }, reading.Rows);
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
    public async Task AClanNotInTheBattleIsIdleOnTheRecipesAbsentMessageAndKeepsItsIcon()
    {
        // The object that should hold the battle exists; the battle's name, which came from a
        // placeholder, is not in it.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Icon": "rbxassetid://14976358748", "Battles": { "LastWeek": { "PointContributions": [] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Idle, reading.Outcome);
        Assert.Equal("Your clan hasn't joined this battle.", reading.Detail);
        Assert.Equal("rbxassetid://14976358748", reading.IconText);
    }

    [Fact]
    public async Task AMissingLiteralKeyStaysAShapeMissWhenTheStepHasAnAbsentMessage()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, """{ "data": { "Name": "Noodle Clan" } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("Step 2: No 'Battles' in 'data'. Keys present: Name.", reading.Detail);
    }

    [Fact]
    public async Task AnAbsentMessageAlsoAppliesToATakePath()
    {
        var recipe = Parse("""
            {
              "recipe": 1, "name": "Seasons", "credit": "Test.", "metricId": "t.v", "everySeconds": 60,
              "inputs": [{ "id": "season", "label": "Season" }],
              "steps": [
                { "url": "https://example.com/seasons", "take": { "board": "data.{season}.board" }, "absentMessage": "That season has not started." },
                { "url": "https://example.com/boards/{board}", "rows": "data", "userId": "id", "value": "score" }
              ]
            }
            """);
        var transport = new FakeTransport().On("https://example.com/seasons", 200, """{ "data": { "spring": { "board": "b1" } } }""");

        var reading = await Read(transport, recipe, inputs: new Dictionary<string, string> { ["season"] = "summer" });

        Assert.Equal(ReadingOutcome.Idle, reading.Outcome);
        Assert.Equal("That season has not started.", reading.Detail);
    }

    [Fact]
    public async Task SeveralStatsComeFromOneListResponse()
    {
        var recipe = Parse("""
            {
              "recipe": 1, "name": "League", "credit": "Test.", "everySeconds": 60,
              "steps": [{ "url": "https://example.com/league", "rows": "data.rows", "userId": "UserID",
                "values": [
                  { "id": "points", "label": "Points", "path": "Points", "metricId": "l.points" },
                  { "id": "level", "label": "Level", "path": "Level", "metricId": "l.level", "sum": false }
                ] }]
            }
            """);
        var transport = new FakeTransport().On("https://example.com/league", 200,
            """{ "data": { "rows": [ { "UserID": 111, "Points": 50, "Level": 4 }, { "UserID": 222, "Points": 7, "Level": 1 } ] } }""");

        var reading = await Read(transport, recipe, inputs: NoInputs, tracked: new HashSet<string> { "points", "level" });

        Assert.Single(transport.Requests);
        Assert.Equal(new[]
        {
            new RecipeRow(111, new Dictionary<string, double> { ["points"] = 50, ["level"] = 4 }),
            new RecipeRow(222, new Dictionary<string, double> { ["points"] = 7, ["level"] = 1 }),
        }, reading.Rows);
    }

    [Fact]
    public async Task SeveralStatsComeFromOneResponsePerAccount()
    {
        var transport = new FakeTransport()
            .On(ProfileUrl1, 200, ProfileResponse(FullProfile))
            .On(ProfileUrl2, 200, ProfileResponse("""
                "Currency": { "Diamonds": { "_am": 40 } }, "EggsHatched": 7, "Rank": 3
                """));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(new[] { "https://ps99.biggamesapi.io/v1/players/1?include=profile", "https://ps99.biggamesapi.io/v1/players/2?include=profile" },
            transport.Requests.Select(r => r.Url.AbsoluteUri));
        Assert.Equal(new[]
        {
            new RecipeRow(1, new Dictionary<string, double> { ["diamonds"] = 9169613101, ["eggs"] = 5000, ["rank"] = 12 }),
            new RecipeRow(2, new Dictionary<string, double> { ["diamonds"] = 40, ["eggs"] = 7, ["rank"] = 3 }),
        }, reading.Rows);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public async Task UntrackedStatsAreNotRead()
    {
        // No EggsHatched and no Rank in this answer, and neither is tracked, so neither is a miss.
        var transport = new FakeTransport().On(ProfileUrl1, 200, ProfileResponse("""
            "Currency": { "Diamonds": { "_am": 40 } }
            """));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1], tracked: new HashSet<string> { "diamonds" });

        Assert.Equal(new[] { new RecipeRow(1, new Dictionary<string, double> { ["diamonds"] = 40 }) }, reading.Rows);
        Assert.Empty(reading.StatMisses);
        Assert.Empty(reading.CellMisses);
    }

    [Fact]
    public async Task OneStatMissingForOneAccountCostsOnlyThatCell()
    {
        var transport = new FakeTransport()
            .On(ProfileUrl1, 200, ProfileResponse(FullProfile))
            .On(ProfileUrl2, 200, ProfileResponse("""
                "Currency": { "Diamonds": { "_am": 40 } }, "Rank": 3
                """));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new RecipeRow(2, new Dictionary<string, double> { ["diamonds"] = 40, ["rank"] = 3 }), reading.Rows[1]);
        Assert.Empty(reading.StatMisses);
        var cell = Assert.Single(reading.CellMisses);
        Assert.Equal((2L, "eggs"), cell.Key);
        Assert.Equal("No 'EggsHatched' in 'data.views.profile.data'. Keys present: Currency, Rank.", cell.Value);
    }

    [Fact]
    public async Task AStatMissingForEveryAccountIsOneStatWideMiss()
    {
        const string noRank = """
            "Currency": { "Diamonds": { "_am": 40 } }, "EggsHatched": 7
            """;
        var transport = new FakeTransport()
            .On(ProfileUrl1, 200, ProfileResponse(noRank))
            .On(ProfileUrl2, 200, ProfileResponse(noRank));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        var miss = Assert.Single(reading.StatMisses);
        Assert.Equal("rank", miss.Key);
        Assert.Equal("No 'Rank' in 'data.views.profile.data'. Keys present: Currency, EggsHatched.", miss.Value);
        Assert.Empty(reading.CellMisses);
        Assert.Equal(40, reading.Rows[0].Values["diamonds"]);
    }

    [Fact]
    public async Task EveryTrackedStatMissingIsAShapeMiss()
    {
        var transport = new FakeTransport().On(ProfileUrl1, 200, ProfileResponse(""" "Coins": 1 """));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1], tracked: new HashSet<string> { "rank" });

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("None of your 1 accounts could be read: No 'Rank' in 'data.views.profile.data'. Keys present: Coins.", reading.Detail);
    }

    [Fact]
    public async Task AnAnswerMatchingUnavailableCostsOnlyThatAccountAndSaysTheRecipesMessage()
    {
        var transport = new FakeTransport()
            .On(ProfileUrl1, 200, PrivateProfile)
            .On(ProfileUrl2, 200, ProfileResponse(FullProfile));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(2, Assert.Single(reading.Rows).UserId);
        Assert.Equal("Profile is private. Link this account on db.biggames.io and turn on its Profile view.", reading.Unavailable[1]);
        Assert.Equal("1 of your accounts could not be read: Profile is private. Link this account on db.biggames.io and turn on its Profile view.", reading.Detail);
        Assert.Empty(reading.StatMisses);
    }

    [Fact]
    public async Task ANotFoundOnARecipeWithUnavailableSaysTheRecipesMessage()
    {
        var transport = new FakeTransport()
            .On(ProfileUrl1, 404, """{ "error": "player_not_found" }""")
            .On(ProfileUrl2, 200, ProfileResponse(FullProfile));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal("Profile is private. Link this account on db.biggames.io and turn on its Profile view.", Assert.Single(reading.Unavailable).Value);
    }

    [Fact]
    public async Task ABadRequestOnARecipeWithUnavailableKeepsTheHostsTextNotTheRecipesMessage()
    {
        // A 400 is not the source saying "this account isn't there" the way a 404 is, so the
        // recipe's own unavailable message would misdirect: only a 404 gets it (spec §3.2).
        var transport = new FakeTransport()
            .On(ProfileUrl1, 400, """{ "error": "bad" }""")
            .On(ProfileUrl2, 200, ProfileResponse(FullProfile));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal("ps99.biggamesapi.io has nothing for user id 1.", Assert.Single(reading.Unavailable).Value);
        Assert.Equal(2, Assert.Single(reading.Rows).UserId);
    }

    [Fact]
    public async Task EveryAccountUnavailableIsStillAReading()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/v1/players/", 200, PrivateProfile);

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Rows);
        Assert.Equal(2, reading.Unavailable.Count);
    }

    [Fact]
    public async Task CounterNamesComeFromTheFirstAccountThatHasThem()
    {
        // The first account is private, so its answer has no statistics to offer.
        var transport = new FakeTransport()
            .On(ProfileUrl1, 200, PrivateProfile)
            .On(ProfileUrl2, 200, ProfileResponse(FullProfile));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1, 2], tracked: ProfileStats);

        // Text values and names with a dot are not offered.
        Assert.Equal(new[] { "Huge Pets Opened", "Eggs Opened" }, reading.CounterNames.ToArray());
    }

    [Fact]
    public async Task APickedCounterIsReadUnderTheCountersPath()
    {
        var transport = new FakeTransport().On(ProfileUrl1, 200, ProfileResponse(FullProfile));

        var reading = await Read(transport, Profile, inputs: NoInputs, ids: [1], tracked: new HashSet<string> { "counter:Huge Pets Opened" });

        Assert.Equal(3, Assert.Single(reading.Rows).Values["counter:Huge Pets Opened"]);
    }

    [Fact]
    public async Task APerAccountRecipeAsksOncePerAccountInTurn()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 200, """{ "count": 17 }""")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: NoInputs, ids: [1, 2]);

        Assert.Equal(new[] { "https://friends.roblox.com/v1/users/1/followers/count", "https://friends.roblox.com/v1/users/2/followers/count" },
            transport.Requests.Select(r => r.Url.AbsoluteUri));
        Assert.Equal(new[] { Row(1, 17), Row(2, 4) }, reading.Rows);
        Assert.Null(reading.Context);
    }

    [Fact]
    public async Task ANotFoundForOneAccountCostsOnlyThatAccount()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 404, "{}")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: NoInputs, ids: [1, 2]);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { Row(2, 4) }, reading.Rows);
        Assert.Equal("1 of your accounts could not be read: friends.roblox.com has nothing for user id 1.", reading.Detail);
        Assert.Equal("friends.roblox.com has nothing for user id 1.", reading.Unavailable[1]);
    }

    [Fact]
    public async Task NoAccountsMeansAReadingWithNoRowsAndNoRequest()
    {
        var transport = new FakeTransport();
        var reading = await Read(transport, Followers, inputs: NoInputs, ids: []);

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
        var reading = await Read(transport, Followers, inputs: NoInputs, ids: [1]);

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
    public async Task ARedirectIsReportedAndNotFollowed()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 302, "");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Unreachable, reading.Outcome);
        Assert.Equal(
            "ps99.biggamesapi.io redirected to another address. Recipes never follow redirects, so nothing was sent there.",
            reading.Detail);
        Assert.Single(transport.Requests);
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
        var reading = await Read(transport, KeyedRecipe("header"), inputs: NoInputs);

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("This recipe needs your Tracker key. Get one at tracker.example.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AKeyBoundToAnotherHostIsNeverSent()
    {
        var transport = new FakeTransport();
        var keys = new FakeKeys(new SavedKey("tracker", "somewhere.else.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: NoInputs, keys: keys);

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("Your Tracker key is saved for somewhere.else.example, and this recipe would send it to api.tracker.example. It was not sent.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AHeaderKeyGoesInTheNamedHeader()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        await Read(transport, KeyedRecipe("header"), inputs: NoInputs, keys: keys);

        Assert.Equal("abc123secret", transport.Requests[0].Headers["api_key"]);
        Assert.DoesNotContain("abc123secret", transport.Requests[0].Url.AbsoluteUri);
    }

    [Fact]
    public async Task AQueryKeyIsAppendedToTheAddress()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc/123secret"));

        await Read(transport, KeyedRecipe("query"), inputs: NoInputs, keys: keys);

        Assert.Equal("https://api.tracker.example/rows?season=1&api_key=abc%2F123secret", transport.Requests[0].Url.AbsoluteUri);
        Assert.Empty(transport.Requests[0].Headers);
    }

    [Fact]
    public async Task ARejectedKeyNamesTheHostAndTheKey()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 403, "{}");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: NoInputs, keys: keys);

        Assert.Equal(ReadingOutcome.KeyRejected, reading.Outcome);
        Assert.Equal("api.tracker.example rejected your Tracker key. Change it to try again.", reading.Detail);
    }

    private const string BattleWithTimes = """
        { "status": "ok", "data": { "configName": "B", "configData": { "StartTime": 1756490400, "FinishTime": 1757611800 } } }
        """;

    private const string ClanWithHistory = """
        { "status": "ok", "data": { "Icon": "rbxassetid://1", "Battles": {
            "A": { "Place": 40, "Points": 500, "PointContributions": [ { "UserID": 111, "Points": 300 }, { "UserID": 222, "Points": 200 } ] },
            "Empty": { "Place": 900, "Points": 10 },
            "123456": { "Place": 1, "Points": 1 },
            "B": { "Place": 3, "Points": 999, "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ] }
        } } }
        """;

    [Fact]
    public async Task HeadlineValuesCarryTheirIdAndNumber()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(("clan-place", 3d), (reading.Headline[0].Id, reading.Headline[0].Number!.Value));
        Assert.Equal(("clan-points", 999d), (reading.Headline[1].Id, reading.Headline[1].Number!.Value));
        Assert.Equal("3", reading.Headline[0].Text);
    }

    [Fact]
    public async Task ThePeriodComesFromTheTakesWithItsTimes()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, BattleWithTimes)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new ReadingPeriod("B", DateTimeOffset.FromUnixTimeSeconds(1756490400), DateTimeOffset.FromUnixTimeSeconds(1757611800)), reading.Period);
    }

    [Fact]
    public async Task AMissingStartOrEndStillReads()
    {
        // Ruling R2: a take named only by the period's starts or ends is optional.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new ReadingPeriod("B", null, null), reading.Period);
    }

    [Fact]
    public async Task EveryPastPeriodIsReadFromTheSameResponse()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanWithHistory);

        var reading = await Read(transport, PetSim);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(new[] { "A", "Empty", "B" }, reading.Past.Select(p => p.Value).ToArray());

        var a = reading.Past[0];
        Assert.True(a.RowsReadable);
        Assert.Equal(new[] { Row(111, 300), Row(222, 200) }, a.Rows);
        Assert.Equal(new double?[] { 40, 500 }, a.Headline.Select(h => h.Number).ToArray());

        var empty = reading.Past[1];
        Assert.False(empty.RowsReadable);
        Assert.Empty(empty.Rows);
        Assert.Equal(new double?[] { 900, 10 }, empty.Headline.Select(h => h.Number).ToArray());
    }

    [Fact]
    public async Task APastKeyMadeOfDigitsIsNeverAPeriod()
    {
        // Ruling R3: an object keyed by user ids must not become period values in the book.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanWithHistory);

        var reading = await Read(transport, PetSim);

        Assert.DoesNotContain(reading.Past, p => p.Value == "123456");
    }

    [Fact]
    public async Task APerAccountAsOfIsReadForEachAccount()
    {
        const string stamped = """
            { "status": "ok", "data": { "views": { "profile": { "available": true, "isStale": true, "fetchedAt": "2026-09-14T20:11:47.320Z",
              "data": { "Currency": { "Diamonds": { "_am": 5 } }, "EggsHatched": 1, "Rank": 2 } } } } }
            """;
        var transport = new FakeTransport().On(ProfileUrl1, 200, stamped);

        var reading = await Read(transport, Profile, NoInputs, [1], tracked: ProfileStats);

        Assert.Equal(new AsOfStamp(new DateTimeOffset(2026, 9, 14, 20, 11, 47, 320, TimeSpan.Zero), true), reading.AccountAsOf[1]);
        Assert.Null(reading.ListAsOf);
    }

    [Fact]
    public async Task AGroupListReadsEveryGroupWhateverIsTicked()
    {
        const string top = """
            { "status": "ok", "data": { "topClans": [
                { "rank": 1, "name": "Aurelian", "points": 412000000 },
                { "rank": 2, "name": "SkyHarbor", "points": 388500000 },
                { "rank": 3, "points": 5 }
            ] } }
            """;
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/v1/clans/battles/B", 200, top);
        var recipe = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

        var reading = await Read(transport, recipe, NoInputs, tracked: new HashSet<string>());

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Rows);
        Assert.Equal(new[] { ("Aurelian", 412000000d, (int?)1), ("SkyHarbor", 388500000d, (int?)2) },
            reading.Groups.Select(g => (g.Name, g.Values["value"], g.Rank)).ToArray());
        Assert.Equal(3, reading.RowsSeen);
    }

    [Fact]
    public async Task ACountingStatReadsHowManyEntriesItsObjectOrListHolds()
    {
        var recipe = Parse("""
            { "recipe": 1, "name": "Counts", "credit": "Test.", "everySeconds": 60,
              "steps": [ { "url": "https://example.test/u/{userId}", "perAccount": true,
                "values": [
                  { "id": "pets", "label": "Pets", "path": "data.Pets", "metricId": "t.pets", "count": true, "sum": false },
                  { "id": "zones", "label": "Zones", "path": "data.Zones", "metricId": "t.zones", "count": true, "sum": false } ] } ] }
            """);
        var transport = new FakeTransport()
            .On("https://example.test/u/1", 200, """{ "data": { "Pets": { "Cat": 3, "Dog": 1 }, "Zones": ["a", "b", "c"] } }""")
            .On("https://example.test/u/2", 200, """{ "data": { "Pets": {}, "Zones": 7 } }""");

        var reading = await Read(transport, recipe, inputs: NoInputs, ids: [1, 2], tracked: new HashSet<string> { "pets", "zones" });

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new RecipeRow(1, new Dictionary<string, double> { ["pets"] = 2, ["zones"] = 3 }), reading.Rows[0]);
        Assert.Equal(new RecipeRow(2, new Dictionary<string, double> { ["pets"] = 0 }), reading.Rows[1]);
        Assert.Equal("'data.Zones' is not a list or an object for user id 2, so its entries can't be counted.", reading.CellMisses[(2, "zones")]);
    }

    [Fact]
    public async Task AGroupListsAsOfIsRead()
    {
        var recipe = Parse("""
            {
              "recipe": 1, "name": "Top clans as of", "credit": "Test data.", "everySeconds": 300,
              "steps": [
                { "url": "https://ps99.biggamesapi.io/v1/clans/battles/top",
                  "rows": "data.topClans", "groupName": "name", "value": "points", "rank": "rank",
                  "asOf": { "time": "data.meta.fetchedAt" } }
              ]
            }
            """);
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/v1/clans/battles/top", 200, """
            { "status": "ok", "data": { "meta": { "fetchedAt": "2026-09-14T20:11:47.320Z" }, "topClans": [
                { "rank": 1, "name": "Aurelian", "points": 412000000 },
                { "rank": 2, "name": "SkyHarbor", "points": 388500000 }
            ] } }
            """);

        var reading = await Read(transport, recipe, NoInputs, tracked: new HashSet<string>());

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new AsOfStamp(new DateTimeOffset(2026, 9, 14, 20, 11, 47, 320, TimeSpan.Zero), null), reading.ListAsOf);
    }
}
