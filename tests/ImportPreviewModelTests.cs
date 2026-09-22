using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>The preview's rows, from a plan: which have ticks, what they say, how unticking a recipe greys its clans, and the after-line.</summary>
public class ImportPreviewModelTests
{
    private static SetupMergePlan PlanWithARecipeAndItsClan()
    {
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var file = new SetupPack(
            [new SetupRecipe(Clan.Slug, Clan.Name, text, new RecipeState(), [])],
            [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)],
            [], Settings.Defaults, [new SetupKey("ps99", "PS99 key", Profile.Slug, Profile.Name)]);
        return SetupMerge.Plan(file, new SetupHere([], [], [], []), 40, 2);
    }

    [Fact]
    public void GroupsFollowThePlanAndOnlyChangesHaveTicks()
    {
        var groups = ImportPreviewModel.Groups(PlanWithARecipeAndItsClan());

        Assert.Equal(["RECIPES", "CLANS", "KEYS TO ENTER AGAIN", "STATS"], groups.Select(g => g.Heading));
        var recipe = Assert.Single(groups[0].Rows);
        Assert.True(recipe.HasTick && recipe.Ticked);
        Assert.Equal("Import " + Clan.Name, recipe.TickName);
        Assert.StartsWith("Add", recipe.Text, StringComparison.Ordinal);
        Assert.False(Assert.Single(groups[2].Rows).HasTick);
        Assert.Equal("40 readings and 2 finished battles", Assert.Single(groups[3].Rows).Item.Name);
    }

    [Fact]
    public void UntickingARecipeGreysItsClanAndTickingItBackRestoresIt()
    {
        var plan = PlanWithARecipeAndItsClan();
        var rows = ImportPreviewModel.Groups(plan).SelectMany(g => g.Rows).ToList();
        var recipe = rows.Single(r => r.Item.Kind == SetupKind.Recipe);
        var clan = rows.Single(r => r.Item.Kind == SetupKind.Clan);

        recipe.Ticked = false;
        ImportPreviewModel.Regrey(rows, plan);
        Assert.False(clan.CanTick);
        Assert.DoesNotContain(clan.Item.Key, ImportPreviewModel.TickedKeys(rows));

        recipe.Ticked = true;
        ImportPreviewModel.Regrey(rows, plan);
        Assert.True(clan.CanTick);
        Assert.Contains(clan.Item.Key, ImportPreviewModel.TickedKeys(rows));
    }

    [Fact]
    public void TheAfterLineSaysWhatWasImportedKeptAndWhereTheOldSetupIs()
    {
        var applied = new SetupApplied(2, 3, 2, 1, 1, 0, @"C:\x\626labs.ur-score.before-import-20260922-1431", null, null);
        var stats = new BookImportOutcome(1204, "Imported 1,204 readings. 12 were already here.");

        Assert.Equal(
            "Imported 2 recipes, 3 clans and 2 boards; 1 clan kept as it was; 1 key to enter in Setup › Recipes. "
            + "Then Imported 1,204 readings. 12 were already here. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(applied, stats));

        // A recipe that parsed on the sending PC but not here (spec §4.2): counted by not counting it, and named
        // in the same sentence as the kept clans, so the line accounts for every ticked thing one way or another.
        var skipped = applied with { SkippedRecipes = ["clan-battle-v9"] };
        Assert.Equal(
            "Imported 2 recipes, 3 clans and 2 boards; 1 clan kept as it was; 1 recipe skipped as it did not parse here (clan-battle-v9); "
            + "1 key to enter in Setup › Recipes. Then Imported 1,204 readings. 12 were already here. "
            + "Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(skipped, stats));

        var failed = applied with { Boards = 0, FailedStep = "boards", FailureType = "UnauthorizedAccessException" };
        Assert.Equal(
            "Imported 2 recipes and 3 clans, then the boards could not be written (UnauthorizedAccessException); what was imported before that stands. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(failed, new BookImportOutcome(0, "")));
    }
}
