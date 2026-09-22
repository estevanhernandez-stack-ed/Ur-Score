using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// What travels with the stats (spec §1): recipes with their ticks, clans, boards, two settings, and the NAMES of
/// keys. Never a key value, never RoRoRo's account GUIDs (exclusions travel as Roblox user ids), never
/// StartOnOpen. Cannot be red before the class exists; each rule below is a mutation the class must survive.
/// </summary>
public class SetupPackTests
{
    private static readonly HostAccount AltOne = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "AshAlt");

    private static InstalledRecipe ClanInstalled() => new(Clan, RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json"), new RecipeState(
        Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points") },
        ExcludedAccountIds: [AltOne.AccountId.ToString(), "00000000-0000-0000-0000-00000000dead"]));

    private static InstalledRecipe ProfileInstalled() => new(Profile, RecipeParserTests.Fixture("petsim99-profile.recipe.json"), new RecipeState());

    /// <summary>Neither shipped fixture declares a key (grep confirms it), so this one is built here for the key tests.</summary>
    private const string KeyedRecipeText = """
        {
          "recipe": 1, "name": "Keyed Test", "credit": "Test data.", "metricId": "test.value",
          "everySeconds": 120,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://example.com/keys", "in": "header", "name": "x-api-key" }],
          "steps": [{ "url": "https://example.com/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """;

    private static InstalledRecipe KeyedInstalled() => new(RecipeParser.Parse(KeyedRecipeText).Recipe!, KeyedRecipeText, new RecipeState());

    [Fact]
    public void ThePackRoundTripsThroughItsFolder()
    {
        using var dir = TempDir.Create("urscore-setup");
        var boards = new List<BoardDef> { new("b-1", "Battle", [new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: MainClan.Id))]) };
        var pack = SetupPack.FromHere([ClanInstalled(), ProfileInstalled()], [MainClan, Rival], boards,
            new Settings(ResolveNames: false, ActiveRecipe: Clan.Slug, StartOnOpen: true), [Main, AltOne]);

        pack.ToFolder(dir.Path);
        var back = SetupPack.FromFolder(dir.Path);

        Assert.NotNull(back);
        Assert.Equal(pack.Recipes.Select(r => (r.Slug, r.Name, r.Text)), back.Recipes.Select(r => (r.Slug, r.Name, r.Text)));
        Assert.Equal(pack.Recipes[0].State.StatChoices["value"], back.Recipes[0].State.StatChoices["value"]);

        Assert.Equal(
            pack.Sources.Select(s => (s.Id, s.Recipe, s.Role, s.Enabled, s.InputsKey)),
            back.Sources.Select(s => (s.Id, s.Recipe, s.Role, s.Enabled, s.InputsKey)));
        for (var i = 0; i < pack.Sources.Count; i++) Assert.Equal(pack.Sources[i].Inputs, back.Sources[i].Inputs);

        Assert.Equal(
            pack.Boards.Select(b => (b.Id, b.Name, b.Follows)),
            back.Boards.Select(b => (b.Id, b.Name, b.Follows)));
        for (var i = 0; i < pack.Boards.Count; i++)
        {
            Assert.Equal(pack.Boards[i].Panels.Count, back.Boards[i].Panels.Count);
            for (var j = 0; j < pack.Boards[i].Panels.Count; j++)
            {
                var a = pack.Boards[i].Panels[j];
                var b = back.Boards[i].Panels[j];
                Assert.Equal(
                    (a.Id, a.Type, a.Size, a.Settings.Recipe, a.Settings.SourceId, a.Settings.UserId),
                    (b.Id, b.Type, b.Size, b.Settings.Recipe, b.Settings.SourceId, b.Settings.UserId));
            }
        }

        Assert.Equal((false, Clan.Slug), (back.Settings.ResolveNames, back.Settings.ActiveRecipe));
        Assert.Equal(pack.Keys, back.Keys);
        Assert.True(Directory.Exists(Path.Combine(dir.Path, SetupPack.Folder)));
    }

    [Fact]
    public void ExclusionsTravelAsRobloxUserIdsAndAnUnknownGuidIsDropped()
    {
        var pack = SetupPack.FromHere([ClanInstalled()], [MainClan], [], Settings.Defaults, [Main, AltOne]);

        var recipe = Assert.Single(pack.Recipes);
        Assert.Equal([201L], recipe.ExcludedUserIds);
        Assert.Null(recipe.State.ExcludedAccountIds);
    }

    [Fact]
    public void KeysTravelAsNamesAndNeverAValue()
    {
        using var dir = TempDir.Create("urscore-setup");
        var pack = SetupPack.FromHere([ClanInstalled(), KeyedInstalled()], [], [], Settings.Defaults, []);
        pack.ToFolder(dir.Path);

        // The keyed recipe declares a key; the clan-battle one does not.
        var key = Assert.Single(pack.Keys);
        Assert.Equal(("tracker", KeyedInstalled().Recipe.Slug), (key.Id, key.RecipeSlug));
        var everything = string.Concat(Directory.EnumerateFiles(Path.Combine(dir.Path, SetupPack.Folder), "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("keys.dat", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-VALUE", everything, StringComparison.Ordinal);
    }

    [Fact]
    public void StartOnOpenDoesNotTravelAndBoardsAreSanitized()
    {
        var stranger = new BoardDef("b-1", "Battle", [new PanelDef("p-1", PanelType.AccountCard, new PanelSize(6), new PanelSettings(Clan.Slug, UserId: 987654321))]);
        var pack = SetupPack.FromHere([ClanInstalled()], [], [stranger], new Settings(StartOnOpen: true), [Main]);

        Assert.False(pack.Settings.StartOnOpen);
        Assert.Null(Assert.Single(pack.Boards).Panels[0].Settings.UserId);
    }

    [Fact]
    public void AFolderWithNoSetupIsNoPack()
    {
        using var dir = TempDir.Create("urscore-setup");
        Assert.Null(SetupPack.FromFolder(dir.Path));
    }
}
