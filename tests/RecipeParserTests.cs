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

    private static IReadOnlyList<string> Problems(string json) => RecipeParser.Parse(json).Problems;

    [Fact]
    public void ThePetSimRecipeParsesWithEveryFieldRead()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        var recipe = result.Recipe!;
        Assert.Equal("Pet Sim 99 clan battle points", recipe.Name);
        Assert.Equal("Points", recipe.ValueLabel);
        Assert.Equal(180, recipe.EffectiveEverySeconds);
        Assert.Equal("clan", Assert.Single(recipe.Inputs).Id);
        Assert.Equal("data", recipe.Inputs[0].Search!.List);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("data.configName", recipe.Steps[0].Take["battle"]);
        Assert.Equal("battle", recipe.Steps[0].IdleWithout);
        Assert.Equal("UserID", recipe.LastStep.UserId);
        Assert.Equal(2, recipe.Headline.Count);
    }

    [Fact]
    public void TheFollowersRecipeParsesAsPerAccount()
    {
        var result = RecipeParser.Parse(Fixture("roblox-followers.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.True(result.Recipe!.LastStep.PerAccount);
        Assert.Equal("count", result.Recipe.LastStep.Value);
    }

    [Fact]
    public void ValueLabelDefaultsToValue() =>
        Assert.Equal("Value", RecipeParser.Parse(With(OneListStep)).Recipe!.ValueLabel);

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
        var steps = """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count" }]""";
        var extra = """, "headline": [{ "label": "Total", "path": "total" }]""";
        Assert.Contains("A headline can only be read from a list-form last step.", Problems(With(steps, extra)));
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
}
