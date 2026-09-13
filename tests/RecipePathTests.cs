using System.Text.Json;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipePathTests
{
    private static PathResult Resolve(string json, string path, string rootName = "the response")
    {
        // Cloned so the element outlives the document this helper disposes.
        using var document = JsonDocument.Parse(json);
        var result = RecipePath.Resolve(document.RootElement.Clone(), path, rootName);
        return result;
    }

    [Fact]
    public void FindsANestedValue()
    {
        var result = Resolve("""{ "data": { "configName": "B" } }""", "data.configName");
        Assert.Equal(PathOutcome.Found, result.Outcome);
        Assert.Equal("B", RecipePath.AsText(result.Value));
    }

    [Fact]
    public void KeysMatchWhateverTheirCasing() =>
        Assert.Equal(PathOutcome.Found, Resolve("""{ "Data": { "ConfigName": "B" } }""", "data.configName").Outcome);

    [Fact]
    public void ANullParentIsNothingNotAMiss()
    {
        // Pet Sim 99 says "no battle running" as "data": null. That is the source speaking.
        var result = Resolve("""{ "status": "ok", "data": null }""", "data.configName");
        Assert.Equal(PathOutcome.Nothing, result.Outcome);
        Assert.Null(result.Miss);
    }

    [Fact]
    public void ANullLeafIsNothing() =>
        Assert.Equal(PathOutcome.Nothing, Resolve("""{ "data": { "configName": null } }""", "data.configName").Outcome);

    [Fact]
    public void AnEmptyStringIsNothing() =>
        Assert.Equal(PathOutcome.Nothing, Resolve("""{ "data": { "configName": "  " } }""", "data.configName").Outcome);

    [Fact]
    public void AMissingKeyIsAMissNamingTheKeysPresent()
    {
        // A renamed field. Reading this as "nothing" would say "no battle running" forever.
        var result = Resolve("""{ "data": { "name": "B", "category": "x" } }""", "data.configName");
        Assert.Equal(PathOutcome.Missing, result.Outcome);
        Assert.Equal("No 'configName' in 'data'. Keys present: name, category.", result.Miss);
    }

    [Fact]
    public void AMissAtTheTopNamesTheRootByItsGivenName()
    {
        Assert.Equal("No 'UserID' in the response. Keys present: id.", Resolve("""{ "id": 1 }""", "UserID").Miss);
        Assert.Equal("No 'UserID' in this row. Keys present: id.", Resolve("""{ "id": 1 }""", "UserID", "this row").Miss);
    }

    [Fact]
    public void WalkingIntoAListSaysSo()
    {
        var result = Resolve("""{ "data": [1, 2] }""", "data.first");
        Assert.Equal(PathOutcome.Missing, result.Outcome);
        Assert.Equal("'data' is a list, not an object, so 'first' cannot be read from it.", result.Miss);
    }

    [Fact]
    public void AnEmptyObjectSaysItHasNoKeys() =>
        Assert.Equal("No 'x' in 'data'. Keys present: none.", Resolve("""{ "data": {} }""", "data.x").Miss);

    [Fact]
    public void NumericKeysAreCountedNotListed()
    {
        var result = Resolve("""{ "data": { "name": "x", "111": 1, "222": 2 } }""", "data.missing");
        Assert.Equal("No 'missing' in 'data'. Keys present: name, and 2 numeric keys.", result.Miss);
    }

    [Fact]
    public void OnlyNumericKeysAreCounted()
    {
        var result = Resolve("""{ "data": { "111": 1 } }""", "data.missing");
        Assert.Equal("No 'missing' in 'data'. Keys present: 1 numeric key.", result.Miss);
    }

    [Theory]
    [InlineData("\"B\"", "B")]
    [InlineData("12", "12")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    public void AsTextKeepsTheValueAsWritten(string literal, string expected)
    {
        using var document = JsonDocument.Parse(literal);
        Assert.Equal(expected, RecipePath.AsText(document.RootElement));
    }

    [Fact]
    public void AsTextRefusesObjects()
    {
        using var document = JsonDocument.Parse("""{ "a": 1 }""");
        Assert.Null(RecipePath.AsText(document.RootElement));
    }
}
