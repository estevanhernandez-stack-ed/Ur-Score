using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// Setup › Stats' clan-and-field section: what it offers, what a Save writes, and what it says when the one
/// thing these numbers need — knowing which row is yours — has not been set up yet.
/// </summary>
public class FieldMetricsModelTests
{
    private static readonly string ClansText = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");

    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static InstalledRecipe Clans(RecipeState? state = null) =>
        new(RecipeParser.Parse(ClansText).Recipe!, ClansText, state ?? new RecipeState());

    private static InstalledRecipe Clan() =>
        new(RecipeParser.Parse(ClanText).Recipe!, ClanText, new RecipeState());

    private static Source SourceFor(InstalledRecipe installed, string? clan, string id) =>
        new(id, installed.Recipe.Slug,
            clan is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["clan"] = clan },
            clan is null ? SourceRole.Watch : SourceRole.Main);

    [Fact]
    public void TheSectionIsAboutTheClansListAndShowsEveryNumber()
    {
        var installed = new[] { Clan(), Clans() };

        Assert.Equal("pet-sim-99-top-clans", FieldMetricsModel.ListFor(installed)!.Recipe.Slug);
        Assert.Equal(FieldMetrics.All.Count, FieldMetricsModel.Items(new RecipeState()).Count);
        Assert.All(FieldMetricsModel.Items(new RecipeState()), item => Assert.False(item.Send));
    }

    [Fact]
    public void WithNoClansListInstalledThereIsNoSection()
    {
        Assert.Null(FieldMetricsModel.ListFor([Clan()]));
    }

    [Fact]
    public void SavedTicksComeBackTickedAndSaveInCatalogueOrder()
    {
        var saved = new RecipeState(SentFieldMetrics: [FieldMetrics.FreeSlots, FieldMetrics.Points]);
        var items = FieldMetricsModel.Items(saved);

        Assert.True(items.Single(i => i.Key == FieldMetrics.Points).Send);
        Assert.True(items.Single(i => i.Key == FieldMetrics.FreeSlots).Send);
        Assert.False(items.Single(i => i.Key == FieldMetrics.Place).Send);

        items.Single(i => i.Key == FieldMetrics.Place).Send = true;

        // Catalogue order, not the order they were ticked in, so the file reads the way the list does.
        Assert.Equal([FieldMetrics.Points, FieldMetrics.Place, FieldMetrics.FreeSlots], FieldMetricsModel.Ticked(items));
    }

    /// <summary>A key this version no longer offers is dropped rather than carried into a send.</summary>
    [Fact]
    public void AKeyFromAnotherVersionIsNotOffered()
    {
        Assert.Equal([FieldMetrics.Place], FieldMetrics.Offered(["place", "who-knows"]).Select(m => m.Key));
    }

    [Fact]
    public void WithoutAClanOfYoursNamedTheSectionSaysWhatToDo()
    {
        var clans = Clans();
        var line = FieldMetricsModel.Line(clans, [SourceFor(clans, null, "s-00000009")], [clans]);

        Assert.Contains(FieldMetricsModel.NoClanSet, line, StringComparison.Ordinal);
    }

    [Fact]
    public void WithYourClanNamedItSaysWhichClanAndThatNoAccountRidesAlong()
    {
        var clans = Clans();
        var clan = Clan();
        var sources = new[] { SourceFor(clans, null, "s-00000009"), SourceFor(clan, "K0i2", "s-00000001") };

        var line = FieldMetricsModel.Line(clans, sources, [clan, clans]);

        Assert.Contains("about K0i2", line, StringComparison.Ordinal);
        Assert.DoesNotContain("K0i2,", line, StringComparison.Ordinal);
        Assert.Contains("no account attached", line, StringComparison.Ordinal);
        Assert.DoesNotContain(FieldMetricsModel.NoClanSet, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A WATCHED clan is somebody else's, and the role's whole contract is that nothing about it is sent or kept.
    /// Until 0.5.2 the set filtered only on the recipe's shape, so watching a rival made its standing yours: if it
    /// placed above you, FieldSummary took ITS points as field-mine and its place, gap and roster counts went to
    /// RoRoRo under your clan's ids. It was live on the owner's own board, which watches CCGP.
    /// </summary>
    [Fact]
    public void AWatchedClanIsNotOneOfYours()
    {
        var clans = Clans();
        var clan = Clan();
        var sources = new[]
        {
            SourceFor(clans, null, "s-00000009"),
            SourceFor(clan, "K0i2", "s-00000001"),
            SourceFor(clan, "DarkLegion", "s-00000002") with { Role = SourceRole.Watch },
        };

        Assert.Equal(["K0i2"], SourceRules.MyClanNames(sources, [clan, clans]));
        Assert.DoesNotContain("DarkLegion", FieldMetricsModel.Line(clans, sources, [clan, clans]), StringComparison.Ordinal);
    }

    /// <summary>A switched-off source is not read, so its clan is not one the numbers can be about either.</summary>
    [Fact]
    public void ASwitchedOffClanIsNotOneOfYours()
    {
        var clans = Clans();
        var clan = Clan();
        var sources = new[]
        {
            SourceFor(clan, "K0i2", "s-00000001"),
            SourceFor(clan, "Dormant", "s-00000002") with { Enabled = false },
        };

        Assert.Equal(["K0i2"], SourceRules.MyClanNames(sources, [clan, clans]));
    }

    /// <summary>A clans list's own source carries no clan of yours, so it is never mistaken for one.</summary>
    [Fact]
    public void TheClansListsOwnSourceNamesNoClanOfYours()
    {
        var clans = Clans();
        var clan = Clan();
        var sources = new[] { SourceFor(clans, "not-mine", "s-00000009"), SourceFor(clan, "K0i2", "s-00000001") };

        Assert.Equal(["K0i2"], SourceRules.MyClanNames(sources, [clan, clans]));
    }

    /// <summary>Two clans read "A and B": a comma-run reads as one long name ("about the best placed of K0i2, CCGP").</summary>
    [Fact]
    public void TwoClansOfYoursAreNamedWithAnAnd()
    {
        var clans = Clans();
        var clan = Clan();
        var sources = new[]
        {
            SourceFor(clans, null, "s-00000009"),
            SourceFor(clan, "K0i2", "s-00000001"),
            SourceFor(clan, "CCGP", "s-00000002"),
        };

        Assert.Contains("the best placed of K0i2 and CCGP.", FieldMetricsModel.Line(clans, sources, [clan, clans]), StringComparison.Ordinal);
    }
}
