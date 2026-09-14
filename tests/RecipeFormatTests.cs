using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeFormatTests
{
    [Fact]
    public void UnixSecondsAndIsoTextBothParse()
    {
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757611800), TimeText.Parse("1757611800"));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 20, 11, 47, 320, TimeSpan.Zero), TimeText.Parse("2026-09-14T20:11:47.320Z"));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero), TimeText.Parse("2026-09-14T10:00:00-05:00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("99999999999999999999")]
    public void AnythingElseIsNoTime(string? text) => Assert.Null(TimeText.Parse(text));

    [Theory]
    [InlineData("data.members.1234567.name", true)]
    [InlineData("42", true)]
    [InlineData("data.Battles.{battle}.Points", false)]
    [InlineData("data.{clan}.7x", false)]
    [InlineData("data.Battles.2024Spring", false)]
    [InlineData("data.{userId}", false)]
    public void ALiteralNumberSegmentIsFound(string path, bool expected) => Assert.Equal(expected, PathRules.HasLiteralNumber(path));

    [Theory]
    [InlineData("Your clan", null, "Clans")]
    [InlineData("your guild", null, "Guilds")]
    [InlineData("Team", null, "Teams")]
    [InlineData("Your stats", null, "Stats")]
    [InlineData("Your clan", "Crews", "Crews")]
    public void AnInputsPluralComesFromTheRecipeOrItsLabel(string label, string? plural, string expected) =>
        Assert.Equal(expected, new RecipeInput("clan", label, null, plural).PluralLabel);
}
