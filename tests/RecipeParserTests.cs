using System.Text.Json;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeParserTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>A minimal valid recipe, with one field swapped in by each test.</summary>
    private static string With(string steps, string extra = "") => $$"""
        {
          "recipe": 1, "name": "Test", "credit": "Test data.", "metricId": "test.value",
          "everySeconds": 120 {{extra}},
          "steps": {{steps}}
        }
        """;

    private const string OneListStep =
        """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";

    private const string OnePerAccountStep =
        """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count" }]""";

    /// <summary>The profile fixture's value ids, in recipe order (D13). Other tests use it so a new stat changes one list.</summary>
    internal static readonly string[] ProfileIds =
    [
        "diamonds", "eggs", "rank", "rebirths", "rank-stars", "pets", "goals", "achievements", "zones",
        "playtime", "sessions", "first-join", "booth-diamonds", "booth-slots", "egg-slots", "pet-slots",
    ];

    /// <summary>A valid per-account recipe whose last step lists these values.</summary>
    private static string Values(string values) =>
        With($$"""[{ "url": "https://example.com/u/{userId}", "perAccount": true, "values": [ {{values}} ] }]""");

    private static IReadOnlyList<string> Problems(string json) => RecipeParser.Parse(json).Problems;

    [Fact]
    public void ThePetSimRecipeParsesWithEveryFieldRead()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        var recipe = result.Recipe!;
        Assert.Equal("Pet Sim 99 clan battle points", recipe.Name);
        Assert.Equal(180, recipe.EffectiveEverySeconds);
        Assert.Equal("clan", Assert.Single(recipe.Inputs).Id);
        Assert.Equal("data", recipe.Inputs[0].Search!.List);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("data.configName", recipe.Steps[0].Take["battle"]);
        Assert.Equal("battle", recipe.Steps[0].IdleWithout);
        Assert.Equal("UserID", recipe.LastStep.UserId);
        Assert.Equal("Your clan hasn't joined this battle.", recipe.LastStep.AbsentMessage);
        Assert.Equal("Clan place", recipe.PlaceLabel);
        Assert.Equal("data.Icon", recipe.Icon);
        Assert.Equal(new[] { false, true }, recipe.Headline.Select(h => h.Sum).ToArray());
    }

    [Fact]
    public void ASingleValueBecomesAOneItemValuesList()
    {
        // The shorthand never reaches the engine, the watch or the window: they only see Values.
        var clan = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Equal(new RecipeValue("value", "Points", "Points", "clan.battle.points", Sum: true), Assert.Single(clan.LastStep.Values));

        var followers = RecipeParser.Parse(Fixture("roblox-followers.recipe.json"));
        Assert.True(followers.Ok, string.Join(" | ", followers.Problems));
        Assert.True(followers.Recipe!.LastStep.PerAccount);
        Assert.Equal(new RecipeValue("value", "Followers", "count", "roblox.followers"), Assert.Single(followers.Recipe.LastStep.Values));
    }

    [Fact]
    public void TheProfileRecipeParsesItsValuesCountersAndUnavailable()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-profile.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        var step = result.Recipe!.LastStep;
        Assert.Equal(ProfileIds, step.Values.Select(v => v.Id).ToArray());
        Assert.Equal(
            new[]
            {
                "ps99.diamonds", "ps99.eggs-hatched", "ps99.rank", "ps99.rebirths", "ps99.rank-stars", "ps99.pets-hatched", "ps99.goals-completed",
                "ps99.achievements", "ps99.zones-unlocked", "ps99.playtime", "ps99.sessions", "ps99.first-join", "ps99.booth-diamonds-earned",
                "ps99.booth-slots", "ps99.egg-slots", "ps99.pet-slots",
            },
            step.Values.Select(v => v.MetricId).ToArray());
        Assert.Equal(new[] { "diamonds", "eggs", "rank", "rebirths", "pets", "goals", "playtime" }, step.Values.Where(v => v.Show).Select(v => v.Id).ToArray());
        Assert.Equal(new[] { "diamonds", "eggs", "goals", "playtime", "sessions", "booth-diamonds" }, step.Values.Where(v => v.Sum).Select(v => v.Id).ToArray());
        Assert.Equal(new[] { "pets", "achievements", "zones" }, step.Values.Where(v => v.Count).Select(v => v.Id).ToArray());
        Assert.Equal(StatFormat.Duration, step.Values.Single(v => v.Id == "playtime").Format);
        Assert.Equal(StatFormat.Date, step.Values.Single(v => v.Id == "first-join").Format);
        Assert.Equal(new[] { "Account", "Progression", "Slots" }, step.Values.Select(v => v.Section).Distinct().ToArray());
        Assert.Equal(new RecipeCounters("Game statistics", "data.views.profile.data.Statistics", "ps99.stat."), step.Counters);
        Assert.Equal("data.views.profile.available", step.Unavailable!.Path);
        Assert.Equal(JsonValueKind.False, step.Unavailable.IsKind);
        Assert.Equal("Profile is private. Link this account on db.biggames.io and turn on its Profile view.", step.Unavailable.Message);
        Assert.Null(result.Recipe.Icon);
        Assert.Equal("Place", result.Recipe.PlaceLabel);
    }

    [Fact]
    public void UnavailableMatchesTheSameJsonKindAndValueOnly()
    {
        var isFalse = new RecipeUnavailable("p", JsonValueKind.False, "false", "m");
        var isZero = new RecipeUnavailable("p", JsonValueKind.Number, "0", "m");
        var isText = new RecipeUnavailable("p", JsonValueKind.String, "private", "m");

        using var document = JsonDocument.Parse("""[false, "false", 0, 0.0, 1, "private", "Private"]""");
        var items = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(new[] { true, false, false, false, false, false, false }, items.Select(isFalse.Matches).ToArray());
        Assert.Equal(new[] { false, false, true, true, false, false, false }, items.Select(isZero.Matches).ToArray());
        Assert.Equal(new[] { false, false, false, false, false, true, false }, items.Select(isText.Matches).ToArray());
    }

    [Fact]
    public void ValueLabelDefaultsToValue() =>
        Assert.Equal("Value", RecipeParser.Parse(With(OneListStep)).Recipe!.LastStep.Values[0].Label);

    [Fact]
    public void APollFasterThanTheFloorIsRaisedToIt()
    {
        var recipe = RecipeParser.Parse(With(OneListStep).Replace("\"everySeconds\": 120", "\"everySeconds\": 5")).Recipe!;
        Assert.Equal(5, recipe.EverySeconds);
        Assert.Equal(60, recipe.EffectiveEverySeconds);
    }

    [Fact]
    public void ARecipeFromANewerFormatIsRefusedAlone()
    {
        // A newer file's other fields may mean something this build cannot read, so every other
        // complaint about it would be noise.
        var problems = Problems("""{ "recipe": 2 }""");
        Assert.Equal(new[] { "This recipe was made by a newer Ur Score (format 2). Update Ur Score to import it." }, problems);
    }

    [Fact]
    public void NotJsonSaysSo() =>
        Assert.StartsWith("This file is not valid JSON:", Assert.Single(Problems("not json")));

    [Fact]
    public void MissingTopLevelFieldsAreEachNamed()
    {
        var problems = Problems("""{ "recipe": 1, "everySeconds": 60, "steps": [] }""");
        Assert.Contains("The recipe has no 'name'.", problems);
        Assert.Contains("The recipe has no 'credit'.", problems);
        Assert.Contains("The recipe has no 'metricId'.", problems);
        Assert.Contains("The recipe has no steps. It needs at least one request to make.", problems);
    }

    [Fact]
    public void AStepWithNoUrlIsNamedByNumber()
    {
        var steps = """[{ "url": "https://example.com/a", "take": { "x": "data.x" } }, { "rows": "data", "userId": "id", "value": "v" }]""";
        Assert.Contains("Step 2 has no 'url'.", Problems(With(steps)));
    }

    [Fact]
    public void PlainHttpIsRefused()
    {
        var steps = """[{ "url": "http://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1's url must start with https://.", Problems(With(steps)));
    }

    [Fact]
    public void AHostMadeOfAPlaceholderIsRefused()
    {
        // The import screen has to be able to name every host before anything runs.
        var steps = """[{ "url": "https://{site}/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        var extra = """, "inputs": [{ "id": "site", "label": "Site" }]""";
        Assert.Contains("Step 1's url must name its host directly, not through a placeholder.", Problems(With(steps, extra)));
    }

    // The second letter is Cyrillic U+043E, a lookalike for the Latin 'o', written as the
    // backslash-u escape below in a regular string literal so this source file stays ASCII.
    private const string LookalikeHost = "g\u043Eogle.com";

    [Fact]
    public void ANonAsciiHostIsRefused()
    {
        var steps = $$"""[{ "url": "https://{{LookalikeHost}}/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains(
            "Step 1's url names its host with non-ASCII characters. Write it in plain ASCII (punycode) "
            + "so the import screen shows the host that is actually contacted.",
            Problems(With(steps)));
    }

    [Fact]
    public void AUrlCarryingAUsernameIsRefused()
    {
        var steps = """[{ "url": "https://me:secret@example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1's url must not carry a username or password.", Problems(With(steps)));
    }

    [Fact]
    public void AnUnknownPlaceholderIsNamed()
    {
        var steps = """[{ "url": "https://example.com/clan/{clna}", "rows": "data", "userId": "id", "value": "score" }]""";
        var extra = """, "inputs": [{ "id": "clan", "label": "Your clan" }]""";
        Assert.Contains("Unknown placeholder {clna} in step 1's url.", Problems(With(steps, extra)));
    }

    [Fact]
    public void UserIdOutsideAPerAccountStepIsRefused()
    {
        var steps = """[{ "url": "https://example.com/u/{userId}", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("{userId} can only be used in a perAccount step, and step 1 is not one.", Problems(With(steps)));
    }

    [Fact]
    public void APerAccountStepMustUseUserId()
    {
        var steps = """[{ "url": "https://example.com/count", "perAccount": true, "value": "count" }]""";
        Assert.Contains("A perAccount step's url must contain {userId}, or it asks the same thing once per account.", Problems(With(steps)));
    }

    [Fact]
    public void TheLastStepMustUseExactlyOneForm()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "value": "score" }]""";
        Assert.Contains("The last step needs rows, userId and value, or perAccount and value.", Problems(With(steps)));
    }

    [Fact]
    public void OnlyTheLastStepReads()
    {
        var steps = """[{ "url": "https://example.com/a", "value": "x" }, { "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 reads a value, but only the last step can.", Problems(With(steps)));
    }

    [Fact]
    public void IdleWithoutMustNameSomethingTheStepTakes()
    {
        var steps = """[{ "url": "https://example.com/a", "take": { "battle": "data.name" }, "idleWithout": "round" }, { "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 is idle without 'round', which it does not take.", Problems(With(steps)));
    }

    [Fact]
    public void AnUndeclaredKeyIsNamed()
    {
        var steps = """[{ "url": "https://example.com/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 uses key 'tracker', which the recipe does not declare.", Problems(With(steps)));
    }

    [Fact]
    public void AKeySentToTwoHostsIsRefused()
    {
        var extra = """, "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://example.com/keys", "in": "header", "name": "x-api-key" }]""";
        var steps = """[{ "url": "https://a.example.com/x", "useKeys": ["tracker"], "take": { "v": "data.v" } }, { "url": "https://b.example.com/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Key 'tracker' is sent to two hosts, a.example.com and b.example.com. A key can only go to one.", Problems(With(steps, extra)));
    }

    [Fact]
    public void AKeyPlacementThatIsNeitherHeaderNorQueryIsNamed()
    {
        var extra = """, "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://example.com/keys", "in": "body", "name": "k" }]""";
        Assert.Contains("Key 1's 'in' is 'body'. It must be header or query.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void AHeadlineNeedsAListFormLastStep()
    {
        var extra = """, "headline": [{ "label": "Total", "path": "total" }]""";
        Assert.Contains("A headline can only be read from a list-form last step.", Problems(With(OnePerAccountStep, extra)));
    }

    [Fact]
    public void AHeadlineShowsAtMostTwoValues()
    {
        var extra = """, "headline": [{ "label": "A", "path": "a" }, { "label": "B", "path": "b" }, { "label": "C", "path": "c" }]""";
        Assert.Contains("The headline has 3 values. It shows at most two.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void ASearchListAddressMustBeFixed()
    {
        var extra = """, "inputs": [{ "id": "clan", "label": "Your clan", "search": { "url": "https://example.com/{clan}", "list": "data" } }]""";
        Assert.Contains("Input 'clan' searches with a placeholder {clan}. A search list's address must be fixed.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void AnInputCannotBeCalledUserId()
    {
        var extra = """, "inputs": [{ "id": "userId", "label": "Who" }]""";
        Assert.Contains("An input cannot be called 'userId'. That name is reserved for your accounts' Roblox ids.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void TheSlugComesFromNameAndAuthor()
    {
        var recipe = RecipeParser.Parse(With(OneListStep, """, "author": "Este Hernandez" """)).Recipe!;
        Assert.Equal("test-este-hernandez", recipe.Slug);
    }

    [Fact]
    public void PlaceholdersFillEncodedForUrlsAndRawForPaths()
    {
        var values = new Dictionary<string, string> { ["clan"] = "a/b c" };
        Assert.Equal("https://example.com/clan/a%2Fb%20c", Placeholders.Fill("https://example.com/clan/{clan}", values, encode: true));
        Assert.Equal("Battles.a/b c.Rows", Placeholders.Fill("Battles.{clan}.Rows", values, encode: false));
    }

    [Theory]
    [InlineData("https://ps99.biggamesapi.io/api/clan/{clan}", "ps99.biggamesapi.io")]
    [InlineData("https://Friends.Roblox.com:443/v1/users", "friends.roblox.com")]
    [InlineData("https://example.com?x=1", "example.com")]
    public void HostOfReadsTheLiteralHostLowercased(string url, string host) =>
        Assert.Equal(host, RecipeHosts.HostOf(url));

    // Stats design §7.3: every rule names its field and its step.

    [Fact]
    public void ValueAndValuesInOneStepAreRefused()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score", "values": [{ "id": "a", "label": "A", "path": "a", "metricId": "t.a" }] }]""";
        Assert.Contains("Step 1 has both 'value' and 'values'. Use 'values' for several stats, or 'value' for one.", Problems(With(steps)));
    }

    [Fact]
    public void ADuplicateValueIdIsNamedWithItsStep()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "values": [{ "id": "a", "label": "A", "path": "a", "metricId": "t.a" }, { "id": "a", "label": "B", "path": "b", "metricId": "t.b" }] }]""";
        Assert.Contains("Step 1 uses the value id 'a' more than once.", Problems(With(steps)));
    }

    [Fact]
    public void ADuplicateMetricIdWithinTheRecipeIsNamed()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "values": [{ "id": "a", "label": "A", "path": "a", "metricId": "t.same" }, { "id": "b", "label": "B", "path": "b", "metricId": "t.same" }] }]""";
        Assert.Contains("The metricId 't.same' is suggested for more than one value. Each stat needs its own.", Problems(With(steps)));
    }

    [Fact]
    public void AValueIdCannotUseTheCounterPrefix()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "values": [{ "id": "counter:a", "label": "A", "path": "a", "metricId": "t.a" }] }]""";
        Assert.Contains("Step 1's value id 'counter:a' starts with 'counter:', which is kept for statistics picked from counters.", Problems(With(steps)));
    }

    [Fact]
    public void ASumThatIsNotTrueOrFalseIsNamed()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "values": [{ "id": "a", "label": "A", "path": "a", "metricId": "t.a", "sum": "no" }] }]""";
        Assert.Contains("'sum' in step 1's value 1 must be true or false.", Problems(With(steps)));

        var extra = """, "headline": [{ "label": "Place", "path": "place", "sum": 0 }]""";
        Assert.Contains("'sum' in headline 1 must be true or false.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void CountersNeedAPath()
    {
        var steps = """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count", "counters": { "label": "Stats", "metricIdPrefix": "t." } }]""";
        Assert.Contains("Step 1's counters has no 'path'.", Problems(With(steps)));
    }

    [Fact]
    public void AnIconOnAPerAccountRecipeIsRefused() =>
        Assert.Contains("An icon can only be read from a list-form last step.", Problems(With(OnePerAccountStep, """, "icon": "data.Icon" """)));

    [Fact]
    public void APlaceLabelOnAPerAccountRecipeIsRefused() =>
        Assert.Contains("A placeLabel only applies to a list-form last step.", Problems(With(OnePerAccountStep, """, "placeLabel": "Rank" """)));

    [Fact]
    public void UnavailableOnAListStepIsRefused()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score", "unavailable": { "path": "open", "is": false, "message": "Closed." } }]""";
        Assert.Contains("Step 1 has 'unavailable', but only a perAccount step can.", Problems(With(steps)));
    }

    [Fact]
    public void UnavailableNeedsAPathAnIsAndAMessage()
    {
        var steps = """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count", "unavailable": { "is": { } } }]""";
        var problems = Problems(With(steps));

        Assert.Contains("Step 1's unavailable has no 'path'.", problems);
        Assert.Contains("Step 1's unavailable has no 'message'.", problems);
        Assert.Contains("Step 1's unavailable has no 'is'. It must be true, false, a number or text.", problems);
    }

    [Fact]
    public void AnAbsentMessageThatIsNotTextIsNamed()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score", "absentMessage": true }]""";
        Assert.Contains("Step 1's 'absentMessage' must be text.", Problems(With(steps)));
    }

    private const string TwoSteps = """
        [ { "url": "https://example.com/active", "take": { "season": "data.name", "seasonEnds": "data.ends" } },
          { "url": "https://example.com/rows/{season}", "rows": "data.rows", "userId": "id", "value": "score" } ]
        """;

    [Fact]
    public void APeriodNamesTakesAndAPastPath()
    {
        var result = RecipeParser.Parse(With(TwoSteps, """, "period": { "value": "season", "ends": "seasonEnds", "past": "data.seasons" }"""));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(new RecipePeriod("season", null, "seasonEnds", "data.seasons"), result.Recipe!.Period);
    }

    [Theory]
    [InlineData("""{ "ends": "seasonEnds" }""", "The period has no 'value'.")]
    [InlineData("""{ "value": "nope" }""", "The period's value 'nope' is not something an earlier step takes.")]
    [InlineData("""{ "value": "season", "starts": "nope" }""", "The period's starts 'nope' is not something an earlier step takes.")]
    public void APeriodMustNameRealTakes(string period, string problem) =>
        Assert.Contains(problem, Problems(With(TwoSteps, $$""", "period": {{period}}""")));

    [Fact]
    public void APastPathNeedsAListOfPlayers()
    {
        var perAccount = """
            [ { "url": "https://example.com/active", "take": { "season": "data.name" } },
              { "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count" } ]
            """;

        Assert.Contains("A period's 'past' needs a last step with rows and userId.",
            Problems(With(perAccount, """, "period": { "value": "season", "past": "data.seasons" }""")));
    }

    [Theory]
    [InlineData("1234567.history")]
    [InlineData("data.1234567.history")]
    [InlineData("data.history.1234567")]
    [InlineData("data.0001234567.history")]
    [InlineData("data.999999999999999999999999999999.history")]
    public void APastPathCannotPointAtAParticularPlayer(string path)
    {
        var result = RecipeParser.Parse(With(TwoSteps, $$""", "period": { "value": "season", "past": "{{path}}" }"""));

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, problem => problem.Contains($"'{path}' names a number.", StringComparison.Ordinal)
            && problem.EndsWith("Recipes can't point at a particular player; use a placeholder instead.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("data.seasons")]
    [InlineData("data.season2026.history")]
    [InlineData("data.2026season.history")]
    [InlineData("data.{season}.history")]
    public void APastPathCanUseNamedFieldsAndKnownPlaceholders(string path)
    {
        var result = RecipeParser.Parse(With(TwoSteps, $$""", "period": { "value": "season", "past": "{{path}}" }"""));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(path, result.Recipe!.Period!.Past);
    }

    [Fact]
    public void AsOfIsReadOnlyOnTheLastStep()
    {
        var result = RecipeParser.Parse(With(
            """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count", "asOf": { "time": "data.fetchedAt", "stale": "data.isStale" } }]"""));
        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(new RecipeAsOf("data.fetchedAt", "data.isStale"), result.Recipe!.LastStep.AsOf);

        var early = """
            [ { "url": "https://example.com/active", "take": { "season": "data.name" }, "asOf": { "time": "data.t" } },
              { "url": "https://example.com/rows/{season}", "rows": "data.rows", "userId": "id", "value": "score" } ]
            """;
        Assert.Contains("Step 1 has 'asOf', but only the last step can.", Problems(With(early)));
    }

    [Fact]
    public void HeadlineIdsDefaultToTheLabelsSlugAndMustBeUnique()
    {
        var ok = RecipeParser.Parse(With(OneListStep,
            """, "headline": [ { "label": "Clan place", "path": "data.place" }, { "id": "total", "label": "Clan points", "path": "data.points" } ]"""));
        Assert.True(ok.Ok, string.Join(" | ", ok.Problems));
        Assert.Equal(new[] { "clan-place", "total" }, ok.Recipe!.Headline.Select(h => h.Id).ToArray());

        Assert.Contains("The headline id 'same' is used more than once.", Problems(With(OneListStep,
            """, "headline": [ { "id": "same", "label": "A", "path": "data.a" }, { "id": "same", "label": "B", "path": "data.b" } ]""")));
    }

    [Fact]
    public void AGroupListReadsGroupsAndNeedsNoMetricId()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-top-clans.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.True(result.Recipe!.IsGroupList);
        Assert.Equal("name", result.Recipe.LastStep.GroupName);
        Assert.Equal("rank", result.Recipe.LastStep.Rank);
        Assert.Null(result.Recipe.LastStep.UserId);
    }

    [Fact]
    public void AGroupListCannotAlsoReadPlayers()
    {
        Assert.Contains("Step 1 has both 'userId' and 'groupName'. A row is a player or a group, not both.",
            Problems(With("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "groupName": "name", "value": "score" }]""")));

        Assert.Contains("Step 1 has 'rank', which only a groupName step can use.",
            Problems(With("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "rank": "rank", "value": "score" }]""")));
    }

    [Theory]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data.1234567", "userId": "id", "value": "score" }]""", "", "Step 1: 'data.1234567' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "stats.99" }]""", "", "Step 1: 'stats.99' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""", """, "headline": [ { "label": "Owner", "path": "data.members.42.name" } ]""", "Headline 1: 'data.members.42.name' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""", """, "icon": "data.7.icon" """, "The icon path: 'data.7.icon' names a number.")]
    public void RecipesCannotPointAtAParticularPlayer(string steps, string extra, string start)
    {
        var problems = Problems(With(steps, extra));
        Assert.Contains(problems, p => p.StartsWith(start, StringComparison.Ordinal)
            && p.EndsWith("Recipes can't point at a particular player; use a placeholder instead.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWorkedRecipesCarryTheirNewFields()
    {
        var clan = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Equal("Clans", clan.Inputs[0].PluralLabel);
        Assert.Equal(new[] { "clan-place", "clan-points" }, clan.Headline.Select(h => h.Id).ToArray());

        var profile = RecipeParser.Parse(Fixture("petsim99-profile.recipe.json")).Recipe!;
        Assert.Equal(new RecipeAsOf("data.views.profile.fetchedAt", "data.views.profile.isStale"), profile.LastStep.AsOf);
    }

    [Fact]
    public void AValueCanCountEntriesReadAsTimeSuggestShowAndNameItsSection()
    {
        var result = RecipeParser.Parse(Values("""
            { "id": "pets", "label": "Pets", "path": "p", "metricId": "t.pets", "count": true, "sum": false, "show": true, "section": " Progression " },
            { "id": "time", "label": "Time", "path": "t", "metricId": "t.time", "format": "duration" },
            { "id": "joined", "label": "Joined", "path": "j", "metricId": "t.joined", "format": "date", "sum": false },
            { "id": "plain", "label": "Plain", "path": "n", "metricId": "t.plain", "format": null, "section": null }
            """));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(
            new[]
            {
                new RecipeValue("pets", "Pets", "p", "t.pets", Sum: false, Count: true, Show: true, Section: "Progression"),
                new RecipeValue("time", "Time", "t", "t.time", Format: StatFormat.Duration),
                new RecipeValue("joined", "Joined", "j", "t.joined", Sum: false, Format: StatFormat.Date),
                new RecipeValue("plain", "Plain", "n", "t.plain"),
            },
            result.Recipe!.LastStep.Values);
    }

    [Theory]
    [InlineData("\"format\": \"minutes\"", "'format' in step 1's value 1 must be \"number\", \"duration\" or \"date\".")]
    [InlineData("\"format\": 3", "'format' in step 1's value 1 must be \"number\", \"duration\" or \"date\".")]
    [InlineData("\"count\": \"yes\"", "'count' in step 1's value 1 must be true or false.")]
    [InlineData("\"show\": 1", "'show' in step 1's value 1 must be true or false.")]
    [InlineData("\"section\": 4", "'section' in step 1's value 1 must be text.")]
    [InlineData("\"section\": \"  \"", "'section' in step 1's value 1 must be text.")]
    [InlineData("\"count\": true, \"format\": \"duration\"", "Step 1's value 1 counts entries, so its format can only be \"number\".")]
    [InlineData("\"format\": \"date\"", "Step 1's value 1 is a date, and dates can't be added up. Give it \"sum\": false.")]
    public void AValuesNewFieldsNameTheirProblem(string field, string problem) =>
        Assert.Contains(problem, Problems(Values($$"""{ "id": "a", "label": "A", "path": "a", "metricId": "t.a", {{field}} }""")));
}
