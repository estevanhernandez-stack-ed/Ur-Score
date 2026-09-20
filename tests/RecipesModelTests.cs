using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class RecipesModelTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Source ClanSource(string id, string clan) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Mine);

    [Fact]
    public void AnItemNamesHostsIntervalSourcesAndIcon()
    {
        var items = RecipesModel.Items(
            [new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(Profile, "", new RecipeState())],
            [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2")],
            slug => slug == Clan.Slug ? @"C:\cache\icon.png" : null);

        Assert.Equal("Pet Sim 99 clan battle points", items[0].Name);
        Assert.Equal("ps99.biggamesapi.io", items[0].Hosts);
        Assert.Equal($"Asks every {Clan.EffectiveEverySeconds} s", items[0].Every);
        Assert.Equal("2 clans", items[0].Sources);
        Assert.True(items[0].HasIcon);
        Assert.Equal("Remove Pet Sim 99 clan battle points", items[0].RemoveName);
        Assert.Equal("", items[1].Sources);
        Assert.False(items[1].HasIcon);
    }

    /// <summary>
    /// A recipe's row draws its main clan's picture (backlog V3-S.7), so its name says whose picture it is, and its slot is laid
    /// out while that picture isn't there (A26). A recipe that names no icon has no slot, and nothing to name.
    /// </summary>
    [Fact]
    public void ARecipesPictureIsNamedForItsMainClanAndKeepsItsSlotWhileItIsNotThere()
    {
        var main = new Source("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

        var items = RecipesModel.Items(
            [new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(Profile, "", new RecipeState())],
            [ClanSource("s-00000002", "K0i2"), main],
            _ => null);

        Assert.Equal((true, false, "CCGP clan icon"), (items[0].HasIconSlot, items[0].HasIcon, items[0].IconName));
        Assert.Equal((false, ""), (items[1].HasIconSlot, items[1].IconName));

        // With no main clan yet, the slot stays, and nothing claims to be anyone's picture.
        var noMain = RecipesModel.Items([new InstalledRecipe(Clan, "", new RecipeState())], [ClanSource("s-00000002", "K0i2")], _ => null);
        Assert.Equal((true, ""), (noMain[0].HasIconSlot, noMain[0].IconName));
    }

    [Fact]
    public void SourcesTextCountsInTheRecipesWords()
    {
        Assert.Equal("No clans yet", RecipesModel.SourcesText(Clan, []));
        Assert.Equal("1 clan", RecipesModel.SourcesText(Clan, [ClanSource("s-00000001", "CCGP")]));
        // A clans list keeps the field's numbers (0.3.7) and the clans a chart draws (0.3.10), so both older words are now false.
        Assert.Equal($"Every row live; the top {GroupRows.Top} and yours kept", RecipesModel.SourcesText(RecipeParser.Parse(GroupList).Recipe!, []));
    }

    [Fact]
    public void RemovingSaysTheBookStays() =>
        Assert.Equal(
            "Remove Pet Sim 99 profile? It stops being read. Its score book stays on this PC, so importing it again carries on where it left off.",
            RecipesModel.ConfirmRemove(Profile));

    /// <summary>
    /// The page offers what you do not have. An installed recipe is a row of its own above; offering it again would
    /// read as a second copy of the same thing.
    /// </summary>
    [Fact]
    public void OnlyTheBuiltInRecipesYouHaveNotInstalledAreOffered()
    {
        var all = RecipesModel.BuiltIn([]);
        Assert.NotEmpty(all);
        Assert.All(all, item => Assert.False(string.IsNullOrWhiteSpace(item.Hosts)));
        Assert.Contains(all, item => item.AddName.StartsWith("Add ", StringComparison.Ordinal));

        var one = BuiltInRecipes.All[0];
        var installed = RecipeParser.Parse(one.Text).Recipe!;
        var rest = RecipesModel.BuiltIn([new InstalledRecipe(installed, one.Text, new RecipeState())]);

        Assert.Equal(all.Count - 1, rest.Count);
        Assert.DoesNotContain(rest, item => item.Slug == one.Slug);
    }
}
