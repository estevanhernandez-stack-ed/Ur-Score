using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// Backlog V3-S.7. An icon belongs to a source, and the window shows the main clan's. It used to be kept per recipe, so every
/// clan read on the clan battle recipe replaced it and the window wore whichever clan was read last: that is how K0i2's icon
/// reached the owner's taskbar. These pin whose picture the window, a recipe's row and the forgetting rule are about, decided
/// by the sources and the recipes alone, never by which picture happened to arrive first.
/// </summary>
public class IconChoiceTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
    private static readonly Source ProfileSource = SourceOf("s-00000004", Profile, null, SourceRole.Mine);

    private static IReadOnlyList<InstalledRecipe> Recipes => [Installed(Clan, "value"), Installed(Profile, "diamonds")];

    /// <summary>Every clan has a picture: the one the window shows is chosen, not whichever is there.</summary>
    private static string? EveryClanHasOne(string sourceId) => sourceId switch
    {
        "s-00000001" => "ccgp.png",
        "s-00000002" => "k0i2.png",
        "s-00000003" => "nova.png",
        _ => null,
    };

    [Fact]
    public void TheWindowShowsTheMainClansIconWhereverTheMainSitsInTheList()
    {
        Assert.Equal(new WindowIcon("ccgp.png", "CCGP clan icon"), IconChoice.ForWindow([AltClan, Rival, MainClan], Recipes, EveryClanHasOne));
    }

    [Fact]
    public void UntilTheMainClansPictureIsThereTheWindowWaitsForItAndNeverWearsAnotherClans()
    {
        // K0i2's and the watched clan's pictures arrived first. They are theirs, not the window's.
        string? onlyTheOthers(string id) => id == MainClan.Id ? null : EveryClanHasOne(id);

        var icon = IconChoice.ForWindow([MainClan, AltClan, Rival], Recipes, onlyTheOthers);

        Assert.Equal(new WindowIcon(null, "CCGP clan icon"), icon);
        Assert.True(icon.HasSlot);
    }

    [Fact]
    public void WithNoMainClanTheWindowKeepsUrScoresOwnAndNoOtherClanStandsIn()
    {
        var icon = IconChoice.ForWindow([AltClan, Rival, ProfileSource], Recipes, EveryClanHasOne);

        Assert.Equal(WindowIcon.None, icon);
        Assert.False(icon.HasSlot);
    }

    [Fact]
    public void AMainThatIsSwitchedOffOrWhoseRecipeNamesNoIconGivesTheWindowNone()
    {
        Assert.Equal(WindowIcon.None, IconChoice.ForWindow([MainClan with { Enabled = false }, AltClan], Recipes, EveryClanHasOne));

        IReadOnlyList<InstalledRecipe> noIcon = [Installed(Clan with { Icon = null }, "value")];
        Assert.Equal(WindowIcon.None, IconChoice.ForWindow([MainClan, AltClan], noIcon, EveryClanHasOne));

        // A main whose recipe isn't installed any more has no icon either.
        Assert.Equal(WindowIcon.None, IconChoice.ForWindow([MainClan], [Installed(Profile, "diamonds")], EveryClanHasOne));
    }

    [Fact]
    public void MakeMainMovesTheWindowsIconToTheNewMain()
    {
        var sources = SourceRules.MakeMain([MainClan, AltClan, Rival], AltClan.Id);

        Assert.Equal(new WindowIcon("k0i2.png", "K0i2 clan icon"), IconChoice.ForWindow(sources, Recipes, EveryClanHasOne));
    }

    [Fact]
    public void ARecipesRowShowsItsOwnMainClansIconAndNoOtherClans()
    {
        Assert.Equal("ccgp.png", IconChoice.ForRecipe(Clan.Slug, [Rival, AltClan, MainClan], Recipes, EveryClanHasOne));
        Assert.Null(IconChoice.ForRecipe(Clan.Slug, [Rival, AltClan], Recipes, EveryClanHasOne));
        Assert.Null(IconChoice.ForRecipe(Profile.Slug, [MainClan, ProfileSource], Recipes, EveryClanHasOne));
    }

    [Fact]
    public void OnlyASourceWhoseRecipeStillNamesAnIconMayKeepOne()
    {
        // Backlog S1-14.1: a recipe updated to one without an icon, or removed, takes its sources' icons with it.
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, Rival.Id },
            IconChoice.SourcesWithIcons([MainClan, AltClan, Rival with { Enabled = false }, ProfileSource], Recipes).Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(IconChoice.SourcesWithIcons([MainClan, AltClan], [Installed(Clan with { Icon = null }, "value")]));
        Assert.Empty(IconChoice.SourcesWithIcons([MainClan, AltClan], [Installed(Profile, "diamonds")]));
    }
}
