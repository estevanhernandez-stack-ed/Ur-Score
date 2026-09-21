using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertCardsTests
{
    private const string Diamonds = "ps99.diamonds";
    private const string Rank = "ps99.rank";

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    /// <summary>The profile recipe with these value ids shown and sent under "ps99.{id}".</summary>
    private static InstalledRecipe Sending(params string[] keys) => new(Profile, "", new RecipeState(Stats: keys.ToDictionary(
        key => key, key => new StatChoice(Show: true, Send: true, MetricId: $"ps99.{key}"), StringComparer.Ordinal)));

    private static AlertRule Rule(int index, string metricId, AlertKind kind, double threshold, RuleOwner owner = RuleOwner.UrScore,
        double window = 0, bool below = true, string? label = null) => new(index, metricId, kind, threshold, window, below, owner, label);

    private static RulesRead RulesOf(params AlertRule[] rules) => new(RulesProblem.None, Exists: true, rules, []);

    /// <summary>The card for one metric id, built the way the page builds the whole view.</summary>
    private static AlertCard CardFor(IReadOnlyList<InstalledRecipe> installed, RulesRead rules, string metricId, string? clan = null) =>
        AlertCards.Build(installed, rules, clan).Cards.Single(c => string.Equals(c.Stat.MetricId, metricId, StringComparison.Ordinal));

    [Fact]
    public void AlertsReadAsSentencesInYourWords()
    {
        Assert.Equal("Alert me when an account's Points gains fewer than 100 a minute for 10 minutes.", AlertCards.Sentence(AlertKind.Rate, "Points", 100, 10, true));
        Assert.Equal("Alert me when an account's Diamonds gains fewer than 1,500.5 a minute for 1 minute.", AlertCards.Sentence(AlertKind.Rate, "Diamonds", 1500.5, 1, true));
        Assert.Equal("Alert me when an account's Rank goes above 40.", AlertCards.Sentence(AlertKind.Level, "Rank", 40, 0, false));
        Assert.Equal("Alert me when an account's Rank goes below 0.25.", AlertCards.Sentence(AlertKind.Level, "Rank", 0.25, 0, true));
        Assert.Equal("Alert me when an account's Rank changes.", AlertCards.Sentence(AlertKind.Event, "Rank", 0, 0, true));
    }

    [Fact]
    public void EverySentStatHasACardListingItsAlertsByOwner()
    {
        var rules = RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, 100, window: 10),
            Rule(1, Diamonds, AlertKind.Rate, 7, RuleOwner.You, window: 30),
            Rule(2, Diamonds, AlertKind.Level, 9, RuleOwner.AnotherPlugin, below: false),
            Rule(3, Diamonds, AlertKind.Rate, 200, window: 10, label: "Gems"),
            Rule(4, "memory.warning", AlertKind.Event, 0, RuleOwner.You));

        var view = AlertCards.Build([Sending("diamonds", "rank")], rules);

        Assert.Equal(new[] { new AlertStat(Diamonds, "Diamonds", true), new AlertStat(Rank, "Player rank", true) }, view.Cards.Select(c => c.Stat).ToArray());
        var diamonds = view.Cards[0];
        Assert.Equal(
            new[]
            {
                "Alert me when an account's Diamonds gains fewer than 100 a minute for 10 minutes.",
                "Alert me when an account's Diamonds gains fewer than 7 a minute for 30 minutes.",
                "Alert me when an account's Diamonds goes above 9.",
                "Alert me when an account's Diamonds gains fewer than 200 a minute for 10 minutes.",
            },
            diamonds.Alerts.Select(a => a.Sentence).ToArray());
        Assert.Equal(new[] { "", "yours", "another plugin's", "a copy" }, diamonds.Alerts.Select(a => a.Mark).ToArray());
        Assert.Equal(new[] { true, false, false, false }, diamonds.Alerts.Select(a => a.Managed).ToArray());
        Assert.Equal(new[] { AlertKind.Level }, diamonds.CanAdd);
        Assert.Empty(view.Cards[1].Alerts);
        Assert.Equal(new[] { AlertKind.Rate, AlertKind.Level }, view.Cards[1].CanAdd);
        Assert.True(view.ShowNext);
    }

    [Fact]
    public void TheNextStepInRoRoRoShowsOnlyWhileASentStatHasAnAlert()
    {
        Assert.False(AlertCards.Build([Sending("diamonds")], RulesOf()).ShowNext);
        Assert.True(AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Level, 5, RuleOwner.You))).ShowNext);
        Assert.False(AlertCards.Build([Sending("rank")], RulesOf(Rule(0, Diamonds, AlertKind.Level, 5))).ShowNext);
        Assert.Equal("Next, in RoRoRo: Settings › Alerts › turn on Metric alerts and choose where they go (desktop, Discord, phone).", AlertCards.NextInRoRoRo);
    }

    [Fact]
    public void AStatYouNoLongerSendKeepsItsCardWhileUrScoreHasAnAlertForIt()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Show: true, Send: true, MetricId: Diamonds),
            ["rank"] = new(Show: true, MetricId: Rank),
        }));
        var rules = RulesOf(
            Rule(0, Rank, AlertKind.Level, 40, below: false),
            Rule(1, Rank, AlertKind.Rate, 3, RuleOwner.You, window: 10),
            Rule(2, "old.coins", AlertKind.Rate, 5, window: 10, label: "Coins"),
            Rule(3, "gone.metric", AlertKind.Level, 1),
            Rule(4, "yours.only", AlertKind.Level, 1, RuleOwner.You));

        var view = AlertCards.Build([installed], rules);

        Assert.Equal(
            new[]
            {
                new AlertStat(Diamonds, "Diamonds", true), new AlertStat("gone.metric", "gone.metric", false),
                new AlertStat("old.coins", "Coins", false), new AlertStat(Rank, "Player rank", false),
            },
            view.Cards.Select(c => c.Stat).ToArray());
        var rank = view.Cards[3];
        Assert.Equal("You don't send Player rank any more, so its alerts can't fire. Tick Send in Setup › Stats, or remove them.", rank.Note);
        Assert.Empty(rank.CanAdd);
        Assert.Equal(2, rank.Alerts.Count);

        var row = AlertCards.Rows(view, AlertsUi.Closed)[3];
        Assert.False(row.ShowAdd);
        Assert.Equal(new[] { (false, true), (false, false) }, row.Lines.Select(l => (l.ShowChange, l.ShowRemove)).ToArray());
    }

    [Fact]
    public void AStatChoiceWithNoMetricIdNeverStopsTheCardsFromBuilding()
    {
        // A hand-edited state file can say "metricId": null. Every other reader of a stat choice guards it, and the label a
        // stale card borrows from the recipe must too, or Setup › Alerts would fail to open.
        var installed = new InstalledRecipe(Profile, "", new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Show: true, Send: true, MetricId: Diamonds),
            ["rank"] = new(Show: true, Send: true, MetricId: null!),
        }));
        var rules = RulesOf(Rule(0, "old.coins", AlertKind.Rate, 5, window: 10, label: "Coins"));

        var view = AlertCards.Build([installed], rules);

        Assert.Equal(new[] { new AlertStat(Diamonds, "Diamonds", true), new AlertStat("old.coins", "Coins", false) }, view.Cards.Select(c => c.Stat).ToArray());
    }

    [Theory]
    [InlineData(RulesProblem.CantOpen, "RoRoRo's rules file is locked or can't be opened, so Ur Score can't show or change alerts right now. Try again in a moment.")]
    [InlineData(RulesProblem.NotJson, "RoRoRo's rules file has a mistake in it, so Ur Score can't show or change alerts. Fix it by hand, then come back.")]
    [InlineData(RulesProblem.NotAList, "RoRoRo's rules file isn't a list of rules, so Ur Score can't show or change alerts. Fix it by hand, then come back.")]
    public void ARulesFileUrScoreCantUseIsSaidOnTheCardAndOffersNothing(RulesProblem problem, string note)
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesRead.Unusable(problem));

        var card = Assert.Single(view.Cards);
        Assert.Equal(note, card.Note);
        Assert.Empty(card.CanAdd);
        Assert.False(view.ShowNext);
        Assert.Equal("", AlertCards.Rows(view, AlertsUi.Closed)[0].NoAlerts);
    }

    [Fact]
    public void RulesRoRoRoCantReadAreCountedOnTheirCard()
    {
        var one = new RulesRead(RulesProblem.None, true, [], [Diamonds]);
        var two = new RulesRead(RulesProblem.None, true, [], [Diamonds, Diamonds, Rank]);

        Assert.Equal("1 rule for Diamonds is written in a way RoRoRo can't read, so it never alerts. Ur Score leaves it as it is.",
            AlertCards.Build([Sending("diamonds")], one).Cards[0].Note);
        Assert.Equal("2 rules for Diamonds are written in a way RoRoRo can't read, so they never alert. Ur Score leaves them as they are.",
            AlertCards.Build([Sending("diamonds")], two).Cards[0].Note);
    }

    /// <summary>
    /// <see cref="AlertCards.Sentence"/> always draws the METRIC's own label (A4), so a threat number's card says
    /// "Threat hours" while RoRoRo's own alert says "H8ER catching K0i2" (<see cref="FieldMetrics.ThreatLabel"/>) —
    /// two different names, on purpose. Without this note, a member's first sign of that split is their phone
    /// naming a clan this screen never showed them (controller ruling, Task 3, 2026-09-20).
    /// </summary>
    [Fact]
    public void AThreatNumbersCardExplainsThatItsNameIsWrittenByUrScore()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var installed = new[] { new InstalledRecipe(clans, "", new RecipeState(SentFieldMetrics: [FieldMetrics.ThreatHours])) };

        var card = CardFor(installed, RulesOf(), FieldMetrics.Find(FieldMetrics.ThreatHours)!.MetricId);

        Assert.Equal(AlertCards.ManagedLabelNote, card.Note);
    }

    /// <summary>
    /// The negative half of the check above. Asserting only the positive would let a bug that put the sentence on
    /// EVERY card pass unnoticed — that failure mode has already bitten this branch more than once.
    /// </summary>
    [Fact]
    public void AnOrdinaryClanNumbersCardCarriesNoManagedLabelNote()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var installed = new[] { new InstalledRecipe(clans, "", new RecipeState(SentFieldMetrics: [FieldMetrics.Points])) };

        var card = CardFor(installed, RulesOf(), FieldMetrics.Find(FieldMetrics.Points)!.MetricId);

        Assert.Equal("", card.Note);
    }

    [Theory]
    [InlineData("100", AlertKind.Rate, 100d, "")]
    [InlineData(" 2,500.5 ", AlertKind.Rate, 2500.5, "")]
    [InlineData("1,000,000", AlertKind.Level, 1000000d, "")]
    [InlineData("0", AlertKind.Level, 0d, "")]
    [InlineData("0.25", AlertKind.Rate, 0.25, "")]
    [InlineData("", AlertKind.Level, 0d, "Type a number, like 100.")]
    [InlineData("lots", AlertKind.Rate, 0d, "Type a number, like 100.")]
    [InlineData("-5", AlertKind.Level, 0d, "Type a number, like 100.")]
    [InlineData("1e6", AlertKind.Rate, 0d, "Type a number, like 100.")]
    [InlineData("1,5", AlertKind.Rate, 0d, "Use a dot for decimals, like 1.5.")]
    [InlineData("1.255", AlertKind.Rate, 0d, "Use at most two decimal places, like 1.25.")]
    [InlineData("0", AlertKind.Rate, 0d, "Use a number above 0.")]
    [InlineData("1000000000000000", AlertKind.Level, 0d, "Use a number below 1,000,000,000,000,000.")]
    [InlineData("999999999999999.9", AlertKind.Level, 0d, "Use 15 digits or fewer, counting the decimals.")]
    [InlineData("99999999999999.99", AlertKind.Rate, 0d, "Use 15 digits or fewer, counting the decimals.")]
    [InlineData("999,999,999,999,999", AlertKind.Level, 999999999999999d, "")]
    [InlineData("9999999999999.99", AlertKind.Rate, 9999999999999.99, "")]
    [InlineData("100000000000000.00", AlertKind.Level, 1e14, "")]
    public void TheNumberYouTypeIsCheckedBeforeAnythingIsWritten(string typed, AlertKind kind, double expected, string problem)
    {
        Assert.Equal(problem, AlertCards.ParseNumber(typed, kind, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("999,999,999,999,999", "999,999,999,999,999", "999999999999999")]
    [InlineData("9999999999999.99", "9,999,999,999,999.99", "9999999999999.99")]
    [InlineData("99,999,999,999,999.9", "99,999,999,999,999.9", "99999999999999.9")]
    [InlineData("0.01", "0.01", "0.01")]
    public void ANumberYouCanTypeShowsAsYouTypedIt(string typed, string shown, string inTheBox)
    {
        // A double formats at 15 significant digits, so every accepted number must fit in 15, or the card would round it
        // (to 1,000,000,000,000,000 at the cap, the very limit the refusal names).
        var (spec, problem) = AlertCards.Check(AlertKind.Level, new AlertDraft { Number = typed }, "Rank");

        Assert.Equal("", problem);
        Assert.Equal(shown, AlertCards.Number(spec!.Threshold));
        Assert.Equal(inTheBox, AlertCards.Editable(spec.Threshold));
        Assert.Equal(((AlertSpec?)null, "Use 15 digits or fewer, counting the decimals."), AlertCards.Check(AlertKind.Level, new AlertDraft { Number = "999999999999999.9" }, "Rank"));
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("")]
    public void StopsClimbingTakesOnlyTheMinutesTheBoxOffers(string minutes)
    {
        Assert.Equal(((AlertSpec?)null, "Choose how many minutes."),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = minutes }, "Diamonds"));
    }

    [Theory]
    [InlineData(1e15)]
    [InlineData(1e300)]
    [InlineData(double.MaxValue)]
    public void ExtremeFiniteThresholdsStayCompactAndRequireAnExplicitReplacement(double threshold)
    {
        var rule = Rule(0, Diamonds, AlertKind.Rate, threshold, window: 10);
        var view = AlertCards.Build([Sending("diamonds")], RulesOf(rule));
        var line = Assert.Single(view.Cards[0].Alerts);
        var changing = AlertCards.OpenChange(line);
        var row = AlertCards.Rows(view, changing)[0];

        Assert.True(line.Sentence.Length < 150);
        Assert.True(changing.Draft!.Number.Length <= 24);
        Assert.Equal(threshold, double.Parse(changing.Draft.Number, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Use a number below 1,000,000,000,000,000.", row.Problem);
        Assert.Equal(((AlertSpec?)null, row.Problem), AlertCards.Check(AlertKind.Rate, changing.Draft, "Diamonds"));
        Assert.Equal(threshold, line.Rule.Threshold);
        Assert.True(AlertCards.Rows(view, AlertsUi.Closed)[0].Lines[0].ShowChange);
        Assert.True(AlertCards.Rows(view, AlertsUi.Closed)[0].Lines[0].ShowRemove);
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusAfterCancel(view, changing));
        changing.Draft.Number = "250.25";
        Assert.Equal((new AlertSpec(AlertKind.Rate, 250.25, 10, true, "Diamonds"), ""),
            AlertCards.Check(AlertKind.Rate, changing.Draft, "Diamonds"));
        Assert.Equal(threshold, line.Rule.Threshold);
    }

    [Theory]
    [InlineData("1e300", "Use a number below 1,000,000,000,000,000.")]
    [InlineData("1E400", "Use a number below 1,000,000,000,000,000.")]
    [InlineData("1e6", "Type a number, like 100.")]
    [InlineData("1e-300", "Type a number, like 100.")]
    [InlineData("NaN", "Type a number, like 100.")]
    [InlineData("Infinity", "Type a number, like 100.")]
    [InlineData("-1e300", "Type a number, like 100.")]
    public void UnsupportedNumericInputNeverBecomesASavedThreshold(string typed, string problem)
    {
        Assert.Equal(problem, AlertCards.ParseNumber(typed, AlertKind.Level, out var value));
        Assert.Equal(0, value);
        Assert.Equal(((AlertSpec?)null, problem), AlertCards.Check(AlertKind.Level, new AlertDraft { Number = typed }, "Rank"));
    }

    /// <summary>
    /// Backlog AC-2.13. A number of minutes the box doesn't offer can only have come from the rule being changed, and Save said
    /// the generic "Choose how many minutes." over a box that had never shown one. It is still refused (controller ruling 3), and
    /// the refusal names the rule's own minutes and what to do.
    /// </summary>
    [Theory]
    [InlineData("20")]
    [InlineData("10.5")]
    public void MinutesFromTheRuleThatTheBoxDoesNotOfferAreRefusedByName(string minutes)
    {
        Assert.Equal(((AlertSpec?)null, $"Ur Score offers 10, 15 or 30 minutes, and this alert uses {minutes}. Choose one to save a change, or Cancel to leave the alert as it is."),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = minutes }, "Diamonds"));
    }

    [Fact]
    public void ARuleWithOtherMinutesReadsAsItIsAndChangeOffersOnlyTheUsualMinutes()
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 20)));
        var line = view.Cards[0].Alerts[0];
        var changing = AlertCards.OpenChange(line);

        Assert.Equal("Alert me when an account's Diamonds gains fewer than 100 a minute for 20 minutes.", line.Sentence);
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.Rows(view, changing)[0].MinuteChoices);
        Assert.Equal(((AlertSpec?)null, AlertCards.OtherMinutes("20")), AlertCards.Check(AlertKind.Rate, changing.Draft!, "Diamonds"));
        foreach (var offered in AlertCards.MinuteChoices(null))
        {
            Assert.Equal("", AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "100", Minutes = offered }, "Diamonds").Problem);
        }
    }

    /// <summary>
    /// Backlog AC-2.13. Change on a rule whose minutes the box doesn't offer opened with the minutes box empty and nothing on
    /// screen saying why. The editor opens saying so, before Save is ever pressed; a rule with minutes the box offers, and a
    /// crosses-a-number rule, open with nothing to say.
    /// </summary>
    [Fact]
    public void ChangeOnARuleWhoseMinutesTheBoxDoesNotOfferSaysWhyTheBoxIsEmpty()
    {
        var odd = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 20)));
        var row = AlertCards.Rows(odd, AlertCards.OpenChange(odd.Cards[0].Alerts[0]))[0];

        Assert.Equal("Ur Score offers 10, 15 or 30 minutes, and this alert uses 20. Choose one to save a change, or Cancel to leave the alert as it is.", row.Problem);
        Assert.True(row.HasProblem);

        var usual = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 15)));
        Assert.Equal("", AlertCards.Rows(usual, AlertCards.OpenChange(usual.Cards[0].Alerts[0]))[0].Problem);

        var level = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Level, 40)));
        Assert.Equal("", AlertCards.Rows(level, AlertCards.OpenChange(level.Cards[0].Alerts[0]))[0].Problem);
    }

    [Fact]
    public void TurnOnBuildsTheRuleFromWhatYouFilledIn()
    {
        Assert.Equal((new AlertSpec(AlertKind.Rate, 250, 15, true, "Diamonds"), ""),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = "15" }, "Diamonds"));
        Assert.Equal((new AlertSpec(AlertKind.Level, 40, 0, false, "Player rank"), ""),
            AlertCards.Check(AlertKind.Level, new AlertDraft { Number = "40", Direction = AlertCards.Above }, "Player rank"));
        Assert.Equal(((AlertSpec?)null, "Choose how many minutes."),
            AlertCards.Check(AlertKind.Rate, new AlertDraft { Number = "250", Minutes = "" }, "Diamonds"));
        Assert.Equal(((AlertSpec?)null, "Choose above or below."),
            AlertCards.Check(AlertKind.Level, new AlertDraft { Number = "40", Direction = "sideways" }, "Player rank"));
        Assert.Equal(((AlertSpec?)null, "Type a number, like 100."),
            AlertCards.Check(AlertKind.Level, AlertCards.NewDraft(AlertKind.Level), "Player rank"));
    }

    [Fact]
    public void ANewAlertStartsFromTheUsualRuleAndChangeStartsFromTheRulesOwnValues()
    {
        var rate = AlertCards.NewDraft(AlertKind.Rate);
        Assert.Equal(("100", "10"), (rate.Number, rate.Minutes));
        var level = AlertCards.NewDraft(AlertKind.Level);
        Assert.Equal(("", AlertCards.Below), (level.Number, level.Direction));

        var changing = AlertCards.DraftOf(Rule(0, Diamonds, AlertKind.Rate, 1500.5, window: 20));
        Assert.Equal(("1500.5", "20"), (changing.Number, changing.Minutes));
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.MinuteChoices(changing.Minutes));
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.MinuteChoices("15"));
        Assert.Equal(new[] { "10", "15", "30" }, AlertCards.MinuteChoices(null));
        Assert.Equal("10", AlertCards.DraftOf(Rule(0, Diamonds, AlertKind.Rate, 5)).Minutes);
        Assert.Equal(AlertCards.Above, AlertCards.DraftOf(Rule(0, Rank, AlertKind.Level, 40, below: false)).Direction);
    }

    [Fact]
    public void AddingAnAlertAsksWhichKindThenShowsItsSentenceToFillIn()
    {
        var view = AlertCards.Build([Sending("diamonds", "rank")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));

        var closed = AlertCards.Rows(view, AlertsUi.Closed)[0];
        Assert.True(closed.ShowAdd);
        Assert.Equal("Add an alert for Diamonds", closed.AddName);
        Assert.False(closed.ShowKinds || closed.ShowEditor);
        Assert.Equal(("Change the stops climbing alert for Diamonds", "Remove the stops climbing alert for Diamonds"), (closed.Lines[0].ChangeName, closed.Lines[0].RemoveName));

        var choosing = AlertCards.Rows(view, AlertCards.OpenAdd(Diamonds));
        Assert.False(choosing[0].ShowAdd);
        Assert.True(choosing[0].ShowKinds);
        Assert.False(choosing[0].ShowRateKind);
        Assert.True(choosing[0].ShowLevelKind);
        Assert.Equal("Crosses a number, for Diamonds", choosing[0].LevelKindName);
        Assert.True(choosing[1].ShowAdd);
        Assert.False(choosing[1].ShowKinds);

        var adding = AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level));
        var row = AlertCards.Rows(view, adding)[0];
        Assert.True(row.ShowLevelEditor);
        Assert.False(row.ShowRateEditor);
        Assert.Same(adding.Draft, row.Draft);
        Assert.Equal(("Turn on", "Turn on the alert for Diamonds", "Number for Diamonds"), (row.ConfirmText, row.ConfirmName, row.NumberName));
        Assert.Equal(new[] { AlertCards.Below, AlertCards.Above }, row.DirectionChoices);
    }

    [Fact]
    public void ChangeOpensTheSentenceInPlaceOfItsButtons()
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 20)));

        var changing = AlertCards.OpenChange(view.Cards[0].Alerts[0]);
        var row = AlertCards.Rows(view, changing)[0];

        Assert.Equal((AlertEditMode.Changing, AlertKind.Rate), (changing.Mode, changing.Kind));
        Assert.True(row.ShowRateEditor);
        Assert.Equal(("Save", "Save the alert for Diamonds"), (row.ConfirmText, row.ConfirmName));
        Assert.False(row.Lines[0].ShowChange || row.Lines[0].ShowRemove);
        Assert.False(row.ShowAdd);
        Assert.Equal(new[] { "10", "15", "30" }, row.MinuteChoices);
    }

    [Fact]
    public void AfterTurnOnChangeOrRemoveTheCardSaysWhatHappened()
    {
        var spec = new AlertSpec(AlertKind.Rate, 250, 15, true, "Diamonds");
        var adding = AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Rate));
        var changing = adding with { Mode = AlertEditMode.Changing };

        Assert.Equal(
            new AlertsUi(ResultMetricId: Diamonds, Result: "On. RoRoRo will alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes."),
            AlertCards.AfterWrite(adding, RuleWrite.Done, spec));
        Assert.Equal("Changed. RoRoRo will now alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes.",
            AlertCards.AfterWrite(changing, RuleWrite.Done, spec).Result);

        var locked = AlertCards.AfterWrite(adding, RuleWrite.CantWrite, spec);
        Assert.Equal(AlertEditMode.Adding, locked.Mode);
        Assert.Same(adding.Draft, locked.Draft);
        Assert.Equal("Ur Score couldn't save RoRoRo's rules file. It may be locked by another program. Nothing was changed; try again in a moment.", locked.Problem);

        Assert.Equal(
            new AlertsUi(ResultMetricId: Diamonds, Result: "That alert isn't in RoRoRo's rules file any more, so nothing was changed.", ResultIsProblem: true),
            AlertCards.AfterWrite(changing, RuleWrite.NotThere, spec));

        var line = new AlertLine(Rule(2, Rank, AlertKind.Level, 40), "", "", true);
        var target = new AlertTarget(Rank, AlertKind.Level);
        Assert.Equal("Removed. RoRoRo won't alert you when an account's Player rank goes below 40 any more.",
            AlertCards.AfterRemove(target, line, "Player rank", RuleWrite.Done).Result);
        Assert.True(AlertCards.AfterRemove(target, line, "Player rank", RuleWrite.CantOpen).ResultIsProblem);
    }

    [Fact]
    public void ChangeAndRemovePickTheAlertFromTheFileAsItIsNowNotTheDrawnCard()
    {
        using var dir = TempDir.Create("urscore-alertcards");
        var path = Path.Combine(dir.Path, "metric-rules.json");
        var target = new AlertTarget(Diamonds, AlertKind.Rate);
        File.WriteAllText(path,
            $$"""[ { "metricId": "{{Diamonds}}", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "owner": "{{RulesFile.Owner}}" } ]""");

        var drawn = AlertCards.Build([Sending("diamonds")], RulesFile.Read(path));
        Assert.NotNull(AlertCards.Managed(drawn, target));

        // The same rule, given to another plugin by hand while the card sat on screen: the click says so and writes nothing.
        File.WriteAllText(path,
            $$"""[ { "metricId": "{{Diamonds}}", "kind": "Rate", "threshold": 50, "windowMinutes": 15, "owner": "another.plugin" } ]""");
        Assert.Null(AlertCards.Managed(AlertCards.Build([Sending("diamonds")], RulesFile.Read(path)), target));
        Assert.Equal(
            new AlertsUi(ResultMetricId: Diamonds, Result: "That alert isn't in RoRoRo's rules file any more, so nothing was changed.", ResultIsProblem: true),
            AlertCards.Gone(target));

        // Still Ur Score's, with numbers changed by hand: Change opens on what the file says now, never on the drawn card.
        File.WriteAllText(path,
            $$"""[ { "metricId": "{{Diamonds}}", "kind": "Rate", "threshold": 50, "windowMinutes": 15, "owner": "{{RulesFile.Owner}}" } ]""");
        var now = AlertCards.Managed(AlertCards.Build([Sending("diamonds")], RulesFile.Read(path)), target);
        Assert.NotNull(now);
        var editor = AlertCards.OpenChange(now);
        Assert.Equal(("50", "15"), (editor.Draft!.Number, editor.Draft!.Minutes));
        Assert.Equal("100", AlertCards.OpenChange(AlertCards.Managed(drawn, target)!).Draft!.Number);
    }

    [Theory]
    [InlineData(AlertKind.Level, double.PositiveInfinity, false, "Alert me when an account's Rank goes above a number too big to show.")]
    [InlineData(AlertKind.Level, double.NegativeInfinity, true, "Alert me when an account's Rank goes below a negative number too big to show.")]
    [InlineData(AlertKind.Rate, double.PositiveInfinity, true, "Alert me when an account's Rank gains fewer than a number too big to show a minute for 10 minutes.")]
    public void ANumberTooBigToShowIsSaidInWords(AlertKind kind, double threshold, bool below, string sentence)
    {
        // RoRoRo reads a threshold like 1e400 as infinity and keeps the rule, so its card lists it and must say it plainly.
        Assert.Equal(sentence, AlertCards.Sentence(kind, "Rank", threshold, 10, below));
    }

    [Fact]
    public void AnAlertWhoseNumberIsTooBigToShowCanBeRemovedButNotChanged()
    {
        using var dir = TempDir.Create("urscore-alertcards");
        var path = Path.Combine(dir.Path, "metric-rules.json");
        File.WriteAllText(path, $$"""[ { "metricId": "{{Diamonds}}", "kind": "Level", "threshold": 1e400, "alertWhenBelow": false, "owner": "{{RulesFile.Owner}}" } ]""");

        var view = AlertCards.Build([Sending("diamonds")], RulesFile.Read(path));

        var line = Assert.Single(view.Cards[0].Alerts);
        Assert.Equal("Alert me when an account's Diamonds goes above a number too big to show.", line.Sentence);
        Assert.True(line.Managed);
        var row = AlertCards.Rows(view, AlertsUi.Closed)[0];
        Assert.Equal((false, true), (row.Lines[0].ShowChange, row.Lines[0].ShowRemove));
        Assert.Equal("", AlertCards.DraftOf(line.Rule).Number);
        Assert.Equal("Add an alert for Diamonds", AlertCards.FocusName(view, Diamonds, AlertsUi.Closed, AlertKind.Level));
        Assert.Equal("Removed. RoRoRo won't alert you when an account's Diamonds goes above a number too big to show any more.",
            AlertCards.AfterRemove(new AlertTarget(Diamonds, AlertKind.Level), line, "Diamonds", RuleWrite.Done).Result);

        // With nothing left to add and no alert that can change, focus goes to a Remove that is there, never a hidden Change.
        var bothTooBig = AlertCards.Build([Sending("diamonds")], RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, double.PositiveInfinity, window: 10), Rule(1, Diamonds, AlertKind.Level, double.PositiveInfinity)));
        Assert.Equal("Remove the stops climbing alert for Diamonds", AlertCards.FocusName(bothTooBig, Diamonds, AlertsUi.Closed, AlertKind.Rate));
    }

    [Fact]
    public void KeyboardFocusGoesWhereYourNextKeyPressIsUseful()
    {
        var none = AlertCards.Build([Sending("diamonds")], RulesOf());
        var rateOn = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var bothOn = AlertCards.Build([Sending("diamonds")], RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Diamonds, AlertKind.Level, 4)));

        // + Add an alert, then a kind.
        Assert.Equal("Stops climbing, for Diamonds", AlertCards.FocusName(none, Diamonds, AlertCards.OpenAdd(Diamonds), AlertKind.Rate));
        Assert.Equal("Crosses a number, for Diamonds", AlertCards.FocusName(rateOn, Diamonds, AlertCards.OpenAdd(Diamonds), AlertKind.Rate));
        Assert.Equal("Number for Diamonds", AlertCards.FocusName(none, Diamonds, AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level)), AlertKind.Level));

        // After Turn on or Save: that alert's Change. After Remove: + Add an alert, else the first Change.
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusName(rateOn, Diamonds, AlertsUi.Closed, AlertKind.Rate));
        Assert.Equal("Add an alert for Diamonds", AlertCards.FocusName(none, Diamonds, AlertsUi.Closed, AlertKind.Rate));
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusName(bothOn, Diamonds, AlertsUi.Closed, AlertKind.Event));
        Assert.Equal("", AlertCards.FocusName(none, "gone.metric", AlertsUi.Closed, AlertKind.Rate));

        // Cancel goes back to the button that opened the editor.
        Assert.Equal("Add an alert for Diamonds", AlertCards.FocusAfterCancel(rateOn, AlertCards.ChooseKind(new AlertTarget(Diamonds, AlertKind.Level))));
        Assert.Equal("Change the stops climbing alert for Diamonds", AlertCards.FocusAfterCancel(rateOn, AlertCards.OpenChange(rateOn.Cards[0].Alerts[0])));
    }

    [Fact]
    public void ARedrawThatChangesNothingIsSkipped()
    {
        IReadOnlyList<InstalledRecipe> installed = [Sending("diamonds")];
        var a = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var b = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10)));
        var c = AlertCards.Build(installed, RulesOf(Rule(0, Diamonds, AlertKind.Rate, 250, window: 10)));

        Assert.True(AlertCards.Same(a, b));
        Assert.False(AlertCards.Same(a, c));
        Assert.False(AlertCards.Same(a, AlertCards.Empty));
    }

    [Fact]
    public void ARuleAddedOrRemovedHigherInTheFileLeavesTheCardsAsTheyAre()
    {
        // Someone else's rule above ours shifts our row's index and changes nothing a card says, so the page mustn't redraw
        // (and drop keyboard focus) for it.
        IReadOnlyList<InstalledRecipe> installed = [Sending("diamonds", "rank")];
        var before = AlertCards.Build(installed, RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Rank, AlertKind.Level, 40, RuleOwner.You, below: false)));
        var inserted = AlertCards.Build(installed, RulesOf(
            Rule(0, "memory.warning", AlertKind.Event, 0, RuleOwner.AnotherPlugin),
            Rule(1, Diamonds, AlertKind.Rate, 100, window: 10), Rule(2, Rank, AlertKind.Level, 40, RuleOwner.You, below: false)));
        var removed = AlertCards.Build(installed, RulesOf(
            Rule(0, "memory.warning", AlertKind.Event, 0, RuleOwner.AnotherPlugin), Rule(1, "old.metric", AlertKind.Level, 3, RuleOwner.You),
            Rule(2, Diamonds, AlertKind.Rate, 100, window: 10), Rule(3, Rank, AlertKind.Level, 40, RuleOwner.You, below: false)));

        Assert.True(AlertCards.Same(before, inserted));
        Assert.True(AlertCards.Same(removed, inserted));

        // What a rule says still counts: its owner, its direction.
        Assert.False(AlertCards.Same(before, AlertCards.Build(installed, RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Rank, AlertKind.Level, 40, RuleOwner.AnotherPlugin, below: false)))));
        Assert.False(AlertCards.Same(before, AlertCards.Build(installed, RulesOf(
            Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Rank, AlertKind.Level, 40, RuleOwner.You, below: true)))));
    }

    [Fact]
    public void TheStatsTableSaysHowManyAlertsAStatHas()
    {
        var rules = RulesOf(Rule(0, Diamonds, AlertKind.Rate, 100, window: 10), Rule(1, Rank, AlertKind.Rate, 1, RuleOwner.You), Rule(2, Rank, AlertKind.Level, 4));

        Assert.Equal("No alert yet. Add one in Setup › Alerts.", AlertCards.StatLine(rules, "ps99.eggs-hatched"));
        Assert.Equal("1 alert in RoRoRo. See it in Setup › Alerts.", AlertCards.StatLine(rules, Diamonds));
        Assert.Equal("2 alerts in RoRoRo. See them in Setup › Alerts.", AlertCards.StatLine(rules, Rank));
        Assert.Equal("RoRoRo's rules file can't be read right now. Setup › Alerts says why.", AlertCards.StatLine(RulesRead.Unusable(RulesProblem.NotJson), Diamonds));
    }

    /// <summary>
    /// A clan-and-field number belongs to no account and is reported without one, so its sentence must not call it
    /// an account's. Every one of the six said "an account's" until 0.5.3.
    /// </summary>
    [Fact]
    public void AClanNumbersSentenceIsNotAboutAnAccount()
    {
        var clan = AlertCards.Sentence(AlertKind.Level, "Clan place", 10, 0, below: false, "clan.standing.place");
        var stat = AlertCards.Sentence(AlertKind.Level, "Diamonds", 10, 0, below: false, "ps99.diamonds");

        Assert.Equal("Alert me when your clan's Clan place goes above 10.", clan);
        Assert.Equal("Alert me when an account's Diamonds goes above 10.", stat);

        // No id at all keeps the old subject, so every existing caller reads as it did.
        Assert.Equal(stat, AlertCards.Sentence(AlertKind.Level, "Diamonds", 10, 0, below: false));
    }

    /// <summary>
    /// A label that already names the clan needs no subject in front of it. Seen on screen before
    /// this: "Alert me when your clan's K0i2 clan points goes below" — which says the same thing
    /// twice. The catalogue label on its own still gets one, because then nothing else says whose.
    /// </summary>
    [Fact]
    public void ALabelThatAlreadyNamesTheClanGetsNoSubject()
    {
        var named = AlertCards.Sentence(AlertKind.Level, "K0i2 clan points", 500, 0, below: true, "clan.standing.points");
        var plain = AlertCards.Sentence(AlertKind.Level, "Clan points", 500, 0, below: true, "clan.standing.points");

        Assert.Equal("Alert me when K0i2 clan points goes below 500.", named);
        Assert.Equal("Alert me when your clan's Clan points goes below 500.", plain);
    }

    /// <summary>
    /// The tick survives the round trip: draft to spec to the rules file and back into the draft a
    /// Change opens on. Without the last leg, opening an existing recovery rule to edit its number
    /// would quietly turn its recovery off.
    /// </summary>
    [Fact]
    public void TheRecoveryTickSurvivesTheRoundTrip()
    {
        var draft = new AlertDraft { Number = "500", Direction = AlertCards.Below, TellMeWhenItRecovers = true };
        var (spec, problem) = AlertCards.Check(AlertKind.Level, draft, "Clan points");

        Assert.Equal("", problem);
        Assert.NotNull(spec);
        Assert.True(spec.TellMeWhenItRecovers);

        var rule = new AlertRule(0, "clan.standing.points", AlertKind.Level, 500, 0, true, RuleOwner.UrScore, "Clan points", true);
        Assert.True(AlertCards.DraftOf(rule).TellMeWhenItRecovers);

        // And a rule that never asked stays off through the same path.
        var quiet = rule with { TellMeWhenItRecovers = false };
        Assert.False(AlertCards.DraftOf(quiet).TellMeWhenItRecovers);
    }

    /// <summary>A card whose rule asked says so, under the alert's own sentence.</summary>
    [Fact]
    public void ACardSaysWhenItWillAlsoTellYouItIsOver()
    {
        Assert.Contains("comes right again", AlertCards.AndTellsYou, StringComparison.Ordinal);
        Assert.Contains("comes right again", AlertCards.AlsoTellMe, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageSaysWhatToDoWhenThereIsNoCard()
    {
        Assert.Equal("Import a recipe first.", AlertCards.EmptyLine([], AlertCards.Empty));
        Assert.Equal(AlertCards.NoSentStat, AlertCards.EmptyLine([Sending()], AlertCards.Build([Sending()], RulesOf())));

        // Both places a tick lives are named, so a member who ticked a clan number is not told nothing is sent.
        Assert.Contains("Setup › Stats", AlertCards.NoSentStat, StringComparison.Ordinal);
        Assert.Contains("Clan and field", AlertCards.NoSentStat, StringComparison.Ordinal);
        Assert.Equal("", AlertCards.EmptyLine([Sending("diamonds")], AlertCards.Build([Sending("diamonds")], RulesOf())));
    }

    [Fact]
    public void AResultWhoseCardIsGoneShowsUnderTheCards()
    {
        var view = AlertCards.Build([Sending("diamonds")], RulesOf());
        var removed = new AlertsUi(ResultMetricId: Rank, Result: "Removed.");

        Assert.Equal("Removed.", AlertCards.OrphanResult(view, removed));
        Assert.Equal("", AlertCards.OrphanResult(view, removed with { ResultMetricId = Diamonds }));
        Assert.Equal("", AlertCards.OrphanResult(view, AlertsUi.Closed));
    }
}
