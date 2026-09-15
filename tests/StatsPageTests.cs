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
}
