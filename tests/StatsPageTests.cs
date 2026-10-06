using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class StatsPageTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void ShowsBodyOnlyWhenThereIsARecipeToChooseFrom(int recipeChoiceCount, bool expected) =>
        Assert.Equal(expected, StatsPage.ShowsBody(recipeChoiceCount));

    [Fact]
    public void TheClansListLineSaysPlainlyWhyItHasNothingToTick()
    {
        Assert.Equal("", StatsPage.GroupListLine([]));
        Assert.Equal(
            "Battle · clans list reads other clans, not your accounts, so it has no stats to tick here. "
            + "What it can send is under Clan and field, below.",
            StatsPage.GroupListLine(["Battle · clans list"]));
        Assert.DoesNotContain("—", StatsPage.GroupListLine(["A", "B"]));
        Assert.StartsWith("A and B read other clans", StatsPage.GroupListLine(["A", "B"]));
    }
}
