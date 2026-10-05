using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>The preview's rows, from a plan: which have ticks, what they say, how unticking a recipe greys its clans, and the after-line.</summary>
public class ImportPreviewModelTests
{
    /// <param name="installed">What this PC holds: the shipped reader (the app always does) or nothing, for a clan whose reader is not here.</param>
    private static SetupMergePlan PlanWithARecipeAndItsClan(bool readerHere = true)
    {
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        IReadOnlyList<InstalledRecipe> installed = readerHere ? [new InstalledRecipe(Clan, text, new RecipeState())] : [];
        var file = new SetupPack(
            [new SetupRecipe(Clan.Slug, Clan.Slug, null, new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points") }), [])],
            [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)],
            [], Settings.Defaults, [new SetupKey("ps99", "PS99 key", Profile.Slug, Profile.Name)]);
        return SetupMerge.Plan(file, new SetupHere(installed, [], [], [], Settings.Defaults), 40, 2);
    }

    [Fact]
    public void GroupsFollowThePlanAndOnlyChangesHaveTicks()
    {
        var groups = ImportPreviewModel.Groups(PlanWithARecipeAndItsClan());

        Assert.Equal(["MODES", "CLANS", "KEYS TO ENTER AGAIN", "STATS"], groups.Select(g => g.Heading));
        var recipe = Assert.Single(groups[0].Rows);
        Assert.True(recipe.HasTick && recipe.Ticked);
        Assert.Equal("Import Battle", recipe.TickName);   // labelled by its mode, never its recipe name
        Assert.StartsWith("Update", recipe.Text, StringComparison.Ordinal);
        Assert.False(Assert.Single(groups[2].Rows).HasTick);
        Assert.Equal("40 readings and 2 finished battles", Assert.Single(groups[3].Rows).Item.Name);
    }

    /// <summary>
    /// A clan needs its reader here or coming. The shipped readers are always here now, so unticking one greys nothing;
    /// a clan whose reader this PC does not hold is greyed whatever is ticked.
    /// </summary>
    [Fact]
    public void AClanIsGreyedOnlyWhenItsReaderIsNeitherHereNorComing()
    {
        var plan = PlanWithARecipeAndItsClan();
        var rows = ImportPreviewModel.Groups(plan).SelectMany(g => g.Rows).ToList();
        var recipe = rows.Single(r => r.Item.Kind == SetupKind.Recipe);
        var clan = rows.Single(r => r.Item.Kind == SetupKind.Clan);

        recipe.Ticked = false;
        ImportPreviewModel.Regrey(rows, plan);
        Assert.True(clan.CanTick);

        var bare = PlanWithARecipeAndItsClan(readerHere: false);
        var bareRows = ImportPreviewModel.Groups(bare).SelectMany(g => g.Rows).ToList();
        var bareClan = bareRows.Single(r => r.Item.Kind == SetupKind.Clan);
        Assert.False(bareClan.CanTick);
        Assert.DoesNotContain(bareClan.Item.Key, ImportPreviewModel.TickedKeys(bareRows));
        Assert.Equal("needs its mode", bareClan.Text);
    }

    [Fact]
    public void TheAfterLineSaysWhatWasImportedKeptAndWhereTheOldSetupIs()
    {
        // Two modes' readers (three readers): the line counts modes (review round 2).
        var applied = new SetupApplied(3, 3, 2, 1, 1, 0, @"C:\x\626labs.ur-score.before-import-20260922-1431", null, null, Modes: 2);
        var stats = new BookImportOutcome(1204, "Imported 1,204 readings. 12 were already here.");

        // The "Then " sentence reuses stats.Message verbatim except its own first letter, which is lowered so it
        // reads as a continuation of "Then …" rather than a second, oddly-capitalised sentence.
        Assert.Equal(
            "Imported 2 modes, 3 clans and 2 boards; 1 clan kept as it was; 1 key to enter. "
            + "Then imported 1,204 readings. 12 were already here. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(applied, stats));

        // A reader no mode names (A7): counted by not counting it, singular and plural, with the mode switches that came along.
        var skipped = applied with { SkippedItems = 1, ModesApplied = true };
        Assert.Equal(
            "Imported 2 modes, 3 clans and 2 boards; 1 clan kept as it was; mode switches applied; 1 item skipped: not part of any mode; "
            + "1 key to enter. Then imported 1,204 readings. 12 were already here. "
            + "Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(skipped, stats));
        Assert.Contains("3 items skipped: not part of any mode", ImportPreviewModel.AfterLine(applied with { SkippedItems = 3 }, stats), StringComparison.Ordinal);
        Assert.Contains("Imported 1 mode,", ImportPreviewModel.AfterLine(applied with { Modes = 1 }, stats), StringComparison.Ordinal);

        // No message of its own: the type in brackets, as the line read before there was one.
        var failed = applied with { Boards = 0, FailedStep = "boards", FailureType = "UnauthorizedAccessException" };
        Assert.Equal(
            "Imported 2 modes and 3 clans, then the boards could not be written (UnauthorizedAccessException); what was imported before that stands. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(failed, new BookImportOutcome(0, "")));

        // Spec §4: the redacted MESSAGE on screen (the type goes to the trail). Its own full stop is dropped, so
        // the sentence that carries on after it reads as one sentence and not two run together.
        var said = failed with { FailureMessage = "Access to the path 'boards.json' is denied." };
        Assert.Equal(
            "Imported 2 modes and 3 clans, then the boards could not be written: Access to the path 'boards.json' is denied; what was imported before that stands. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(said, new BookImportOutcome(0, "")));

        // Nothing at all got written (the very first step, the aside copy, failed): its own arm, since "Imported
        // nothing, then ..." reads as nonsense and "what was imported before that stands" is vacuous when nothing was.
        var nothingDone = new SetupApplied(0, 0, 0, 0, 0, 0, @"C:\x\626labs.ur-score.before-import-20260922-1431", "aside", "UnauthorizedAccessException");
        Assert.Equal(
            "Nothing was imported: the aside could not be written (UnauthorizedAccessException). Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(nothingDone, new BookImportOutcome(0, "")));
    }

    [Fact]
    public void TheAfterLineSaysNothingNewInTheSetupWhenEveryRowWasSame()
    {
        // Every row Same (a clan already in the file too, ticked but nothing to apply): "Imported nothing" would
        // read like a failure, so an empty Parts() gets its own plain sentence instead.
        var applied = new SetupApplied(0, 0, 0, KeptClans: 1, Keys: 0, DroppedExclusions: 0,
            AsideFolder: @"C:\x\626labs.ur-score.before-import-20260922-1431", FailedStep: null, FailureType: null);
        var stats = new BookImportOutcome(0, "Nothing new to import. 40 were already here.");

        Assert.Equal(
            "Nothing new in the setup; 1 clan kept as it was. Then nothing new to import. 40 were already here. "
            + "Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(applied, stats));
    }
}
