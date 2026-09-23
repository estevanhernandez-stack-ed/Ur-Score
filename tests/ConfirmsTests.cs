using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The one themed confirmation Ur Score asks with: when it asks at all, what the question says, what the button
/// that acts is called, and that the safe answer is the one keyboard lands on. The words live away from the window
/// that draws them, the way Setup › Alerts keeps its sentences in <see cref="AlertCards"/> (owner rule, V3-S.10).
/// </summary>
public class ConfirmsTests
{
    /// <summary>A recipe whose input is not a clan, so a question built from a recipe's own words can't be a hardcoded "clan".</summary>
    private const string Squads = """
        {
          "recipe": 1, "name": "Squad points", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "inputs": [ { "id": "squad", "label": "Your squad", "plural": "Squads" } ],
          "steps": [ { "url": "https://example.test/squad/{squad}", "rows": "data.rows", "userId": "UserID", "value": "Points" } ]
        }
        """;

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Recipe Squad => RecipeParser.Parse(Squads).Recipe!;

    private static Source ClanSource(string id, string clan, bool enabled = true) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Watch, enabled);

    private static IReadOnlyList<Source> Clans(int count, bool enabled = true) =>
        [.. Enumerable.Range(1, count).Select(i => ClanSource($"s-0000000{i}", $"Clan{i}", enabled))];

    private static IReadOnlyList<InstalledRecipe> Installed(Recipe recipe) => [new InstalledRecipe(recipe, "", new RecipeState())];

    [Fact]
    public void RemovingARecipeAsksInThatRecipesName()
    {
        var question = RecipesModel.RemoveQuestion(Profile);

        Assert.Equal("Remove recipe", question.Title);
        Assert.Equal(RecipesModel.ConfirmRemove(Profile), question.Question);
        Assert.Equal("Remove", question.DoText);
        Assert.Equal("Remove Pet Sim 99 profile", question.DoName);
    }

    [Fact]
    public void TheAnswerThatDoesNothingIsTheOneKeyboardLandsOn() => Assert.Equal("Cancel", Confirm.CancelText);

    /// <summary>
    /// Deleting a board asks in that board's name and says what goes with it. The smoke walk answers this window
    /// by the acting button's accessible name, so that name is part of the contract, not decoration.
    /// </summary>
    [Fact]
    public void DeletingABoardAsksInThatBoardsNameAndSaysWhatGoesWithIt()
    {
        var question = BoardText.DeleteBoardQuestion(new BoardDef("b-00000002", "Rivals copy", []));

        Assert.Equal("Delete board", question.Title);
        Assert.Equal("Delete the Rivals copy board? Its panels go with it. Your score book isn't touched.", question.Question);
        Assert.Equal("Delete", question.DoText);
        Assert.Equal("Delete the Rivals copy board", question.DoName);
    }

    [Fact]
    public void OneClanPastTheLimitAsksAndSaysWhatAddingItCosts()
    {
        var five = Clans(5);
        var change = new SourceChange([.. five, ClanSource("s-00000006", "Clan6")], "s-00000006", null);

        var question = ClansModel.AddQuestion(five, change, Clan, Installed(Clan), accountCount: 2, "Clan6");

        Assert.NotNull(question);
        Assert.Equal("Add another clan", question.Title);
        Assert.StartsWith("That makes more than 5 clans for Pet Sim 99 clan battle points.", question.Question);
        Assert.Contains("Your PC asks ps99.biggamesapi.io about", question.Question);
        Assert.EndsWith("Add it anyway?", question.Question);
        Assert.Equal("Add anyway", question.DoText);
        Assert.Equal("Add Clan6 anyway", question.DoName);
    }

    [Fact]
    public void UnderTheLimitNothingIsAskedAndThePickIsSavedStraightAway()
    {
        var four = Clans(4);
        var change = new SourceChange([.. four, ClanSource("s-00000005", "Clan5")], "s-00000005", null);

        Assert.Null(ClansModel.AddQuestion(four, change, Clan, Installed(Clan), accountCount: 2, "Clan5"));
    }

    [Fact]
    public void ASourceAlreadyThereIsNotAnotherOneSoNothingIsAsked()
    {
        var five = Clans(5);
        var again = new SourceChange(five, "s-00000001", null);

        Assert.Null(ClansModel.AddQuestion(five, again, Clan, Installed(Clan), accountCount: 2, "Clan1"));
    }

    [Fact]
    public void FiveOfThemOffAreNotFiveSoNothingIsAsked()
    {
        var off = Clans(5, enabled: false);
        var change = new SourceChange([.. off, ClanSource("s-00000006", "Clan6")], "s-00000006", null);

        Assert.Null(ClansModel.AddQuestion(off, change, Clan, Installed(Clan), accountCount: 2, "Clan6"));
    }

    /// <summary>A question is built from the recipe's own words, so a recipe with squads asks about squads.</summary>
    [Fact]
    public void AQuestionBorrowsTheRecipesWordForItsGroups()
    {
        var squad = Squad;
        var sources = Enumerable.Range(1, 5)
            .Select(i => new Source($"s-0000000{i}", squad.Slug, new Dictionary<string, string> { ["squad"] = $"Squad{i}" }, SourceRole.Watch))
            .ToList();
        var change = new SourceChange(
            [.. sources, new Source("s-00000006", squad.Slug, new Dictionary<string, string> { ["squad"] = "Squad6" }, SourceRole.Watch)],
            "s-00000006", null);

        var question = ClansModel.AddQuestion(sources, change, squad, Installed(squad), accountCount: 1, "Squad6");

        Assert.NotNull(question);
        Assert.Equal("Add another squad", question.Title);
        Assert.StartsWith("That makes more than 5 squads for Squad points.", question.Question);
    }

    /// <summary>
    /// Every question Ur Score asks is asked out loud: sentence case, a question mark somewhere in it (the words
    /// around it differ — one asks and then explains, the other explains and then asks), and no emoji anywhere.
    /// </summary>
    [Fact]
    public void EveryQuestionIsAQuestionInTheOwnersVoice()
    {
        var five = Clans(5);
        var change = new SourceChange([.. five, ClanSource("s-00000006", "Clan6")], "s-00000006", null);
        Confirm[] asked =
        [
            RecipesModel.RemoveQuestion(Clan),
            RecipesModel.RemoveQuestion(Profile),
            BoardText.DeleteBoardQuestion(new BoardDef("b-00000002", "Rivals copy", [])),
            ClansModel.AddQuestion(five, change, Clan, Installed(Clan), accountCount: 2, "Clan6")!,
        ];

        Assert.NotEmpty(asked);
        foreach (var question in asked)
        {
            Assert.Contains("?", question.Question, StringComparison.Ordinal);
            Assert.True(char.IsUpper(question.Question[0]), question.Question);
            Assert.True(char.IsUpper(question.Title[0]), question.Title);
            Assert.True(char.IsUpper(question.DoText[0]), question.DoText);
            Assert.False(question.Title[1..].Any(char.IsUpper), question.Title);
            Assert.All(
                new[] { question.Title, question.Question, question.DoText, question.DoName },
                text => Assert.False(text.Any(char.IsSurrogate), text));
        }
    }

    private static InstalledRecipe WithState(Recipe recipe, RecipeState state) => new(recipe, "", state);

    /// <summary>Closing asks only while something reaches RoRoRo: a send tick or a field metric, on a recipe with a clan switched on.</summary>
    [Fact]
    public void ClosingAsksOnlyWhileSomethingIsSending()
    {
        var sendOn = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points") });
        var showOnly = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: false) });
        var fieldOn = new RecipeState(SentFieldMetrics: ["points"]);
        var clan = ClanSource("s-00000001", "CCGP");

        Assert.False(BoardText.Sending([WithState(Clan, showOnly)], [clan]));
        Assert.True(BoardText.Sending([WithState(Clan, sendOn)], [clan]));
        Assert.True(BoardText.Sending([WithState(Clan, fieldOn)], [clan]));
        Assert.False(BoardText.Sending([WithState(Clan, sendOn)], [ClanSource("s-00000001", "CCGP", enabled: false)]));
        Assert.False(BoardText.Sending([WithState(Clan, sendOn)], []));
    }

    [Fact]
    public void TheCloseQuestionSaysAlertsStopAndKeepRunningIsTheSafeAnswer()
    {
        var q = BoardText.CloseWhileSending;

        Assert.Equal("Close Ur Score", q.Title);
        Assert.Equal("Close Ur Score? While it is closed nothing is read or recorded, and nothing reaches RoRoRo, so your phone alerts stop.", q.Question);
        Assert.Equal(("Close", "Close Ur Score", "Keep running"), (q.DoText, q.DoName, q.CancelButton));
        Assert.Equal(Confirm.CancelText, BoardText.DeleteBoardQuestion(new BoardDef("b-1", "Rivals", [])).CancelButton);
    }
}
