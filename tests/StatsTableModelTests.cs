using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class StatsTableModelTests
{
    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Dictionary<string, StatChoice> Saved(params (string Key, StatChoice Choice)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Choice, StringComparer.Ordinal);

    [Fact]
    public void RowsAreValuesThenCounterNamesInSourceOrderThenSavedCountersThenChoicesNoLongerOffered()
    {
        var saved = Saved(
            ("counter:Zones Unlocked", new StatChoice(true, false, "ps99.stat.zones-unlocked")),
            ("prestige", new StatChoice(false, false, "ps99.prestige")));

        var rows = StatsTableModel.Build(Profile, saved, ["Pets Hatched", "Coins Spent", "Pets Hatched", "123456", "Bad.Name"]);

        string[] expected = [.. RecipeParserTests.ProfileIds, "counter:Pets Hatched", "counter:Coins Spent", "counter:Zones Unlocked", "prestige"];
        Assert.Equal(expected, rows.Select(r => r.Key).ToArray());
        Assert.All(rows.Take(rows.Count - 1), row => Assert.True(row.Offered));
        Assert.False(rows[^1].Offered);
        Assert.Equal("prestige (no longer offered)", rows[^1].DisplayLabel);
        Assert.Equal("Coins Spent", rows.Single(r => r.Key == "counter:Coins Spent").DisplayLabel);
    }

    [Fact]
    public void ARowKeepsItsSavedTicksAndPinnedNameElseSuggestsTheRecipesName()
    {
        var rows = StatsTableModel.Build(Profile, Saved(("eggs", new StatChoice(true, true, "my.eggs"))), []);

        var eggs = rows.Single(r => r.Key == "eggs");
        var rank = rows.Single(r => r.Key == "rank");
        Assert.True(eggs.Show);
        Assert.True(eggs.Send);
        Assert.Equal("my.eggs", eggs.MetricId);
        Assert.False(rank.Show);
        Assert.Equal("ps99.rank", rank.MetricId);
    }

    [Fact]
    public void AChoiceTheRecipeNoLongerOffersIsNeverTicked()
    {
        var rows = StatsTableModel.Build(Profile, Saved(("prestige", new StatChoice(true, true, "ps99.prestige"))), []);

        var gone = rows.Single(r => r.Key == "prestige");
        Assert.False(gone.Show);
        Assert.False(gone.Send);
        Assert.False(StatsTableModel.AnyTicked(rows));
    }

    [Fact]
    public void SearchFiltersByLabelIgnoringCaseAndKeepsTickedRows()
    {
        var rows = StatsTableModel.Build(Profile, Saved(("rank", new StatChoice(true, false, "ps99.rank"))), ["Eggs Opened", "Coins Spent"]);

        var visible = StatsTableModel.Visible(rows, "  EGGS ");

        Assert.Equal(new[] { "eggs", "rank", "counter:Eggs Opened" }, visible.Select(r => r.Key).ToArray());
    }

    [Fact]
    public void ABlankSearchShowsEveryRowAndNoShowingLine()
    {
        var rows = StatsTableModel.Build(Profile, Saved(), ["Eggs Opened"]);

        Assert.Equal(rows.Count, StatsTableModel.Visible(rows, " ").Count);
        Assert.Equal("", StatsTableModel.ShowingLine(4, 4, " "));
        Assert.Equal("Showing 12 of 75 · ticked stats always shown", StatsTableModel.ShowingLine(12, 75, "eg"));
    }

    [Fact]
    public void TheNameColumnShowsOnlyWhileSendIsTicked()
    {
        var row = StatsTableModel.Build(Profile, Saved(), [])[0];
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(row.NameShown);
        row.Send = true;

        Assert.True(row.NameShown);
        Assert.True(row.Ticked);
        Assert.Contains(nameof(StatRow.NameShown), raised);
        Assert.Contains(nameof(StatRow.Ticked), raised);
    }

    [Fact]
    public void ChoicesKeepSavedEntriesAndAddOnlyTickedRows()
    {
        var saved = Saved(("rank", new StatChoice(false, false, "ps99.rank")), ("rebirths", new StatChoice(false, false, "old.rebirths")));
        var rows = StatsTableModel.Build(Profile, saved, []);
        rows.Single(r => r.Key == "diamonds").Show = true;

        var choices = StatsTableModel.Choices(saved, rows);

        Assert.Equal(new[] { "diamonds", "rank", "rebirths" }, choices.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new StatChoice(true, false, "ps99.diamonds"), choices["diamonds"]);
        Assert.Equal(new StatChoice(false, false, "old.rebirths"), choices["rebirths"]);
    }

    [Fact]
    public void ASendTickPastRoRoRosLimitIsRefusedAndNamedAmongTheRefusals()
    {
        var accounts = Enumerable.Range(0, HistoryBudget.Limit + 1).Select(_ => Guid.NewGuid()).ToList();
        var rows = StatsTableModel.Build(Profile, Saved(), []);
        rows[0].Send = true;

        var budget = StatsTableModel.Budget(Profile, new RecipeState(), [], accounts, rows);

        Assert.False(budget.Allowed);
        Assert.Contains(budget.Line, StatsTableModel.Refusals([], rows, budget.Line));
    }

    [Fact]
    public void NothingTickedAsksForOneStatAfterTheScreensOwnRefusals()
    {
        var rows = StatsTableModel.Build(Profile, Saved(), []);

        Assert.Equal(new[] { "Review says no.", StatRules.TickOne }, StatsTableModel.Refusals(["Review says no."], rows, null).ToArray());
    }

    [Fact]
    public void SaveProblemsNameTwoStatsSharingOneName()
    {
        var rows = StatsTableModel.Build(Profile, Saved(), []);
        rows[0].Show = true;
        rows[0].MetricId = "same.name";
        rows[1].Show = true;
        rows[1].MetricId = "same.name";

        var problems = StatsTableModel.SaveProblems(Profile, new RecipeState(), [], [], rows);

        Assert.Contains("Diamonds and Eggs hatched use the same name, same.name. Give each stat its own.", problems);
    }

    [Fact]
    public void RuleLinesCoverOnlySentRowsThatHaveAName()
    {
        var rows = StatsTableModel.Build(Profile, Saved(), []);
        rows[0].Send = true;
        rows[1].Send = true;
        rows[1].MetricId = " ";

        Assert.Equal("Diamonds: rule for ps99.diamonds", StatsTableModel.RuleLines(rows, id => $"rule for {id}"));
    }

    [Fact]
    public void AFirstImportStartsWithShowTickedOnWhatTheRecipeSuggestsAndNothingSent()
    {
        var suggested = StatsTableModel.Suggested(Profile);

        Assert.Equal(new[] { "diamonds", "eggs", "goals", "pets", "playtime", "rank", "rebirths" }, suggested.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(suggested.Values, choice => Assert.True(choice.Show && !choice.Send));
        Assert.Equal("ps99.playtime", suggested["playtime"].MetricId);

        // Rows built from them are ticked, and what Import saves is exactly them: a fresh import has no saved choices.
        var rows = StatsTableModel.Build(Profile, suggested, []);
        Assert.True(StatsTableModel.AnyTicked(rows));
        Assert.Equal(suggested.Keys.Order(StringComparer.Ordinal), StatsTableModel.Choices(new Dictionary<string, StatChoice>(), rows).Keys.Order(StringComparer.Ordinal));

        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Empty(StatsTableModel.Suggested(clan));
    }

    [Fact]
    public void TheNamesLinesSayWhereNamesComeFrom()
    {
        Assert.Equal("Reading stat names once from ps99.biggamesapi.io…", StatsTableModel.ReadingNamesLine(Profile));
        Assert.Equal("No statistic names read yet.", StatsTableModel.NamesLine(Profile, 0));
        Assert.Equal("1,204 statistic names from the last read.", StatsTableModel.NamesLine(Profile, 1204));
        Assert.Equal("Found 75 statistic names. Search them above.", StatsTableModel.FoundNamesLine(75));

        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Equal("", StatsTableModel.NamesLine(clan, 0));
    }
}
