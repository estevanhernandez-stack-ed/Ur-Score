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

    private static InstalledRecipe ProfileInstalled() => new(Profile, RecipeParserTests.Fixture("petsim99-profile.recipe.json"), new RecipeState(
        Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Show: true, MetricId: "profile.rank") }));

    /// <summary>A reader as a fresh install runs it: the seed and nothing the person did.</summary>
    private static InstalledRecipe Pristine(Recipe recipe, string text) =>
        new(recipe, text, RecipeStates.Effective(recipe, null, Labs626.UrScore.Games.Readers.ShowsFor(Labs626.UrScore.Games.GameCatalog.BuiltIn.ModeOf(recipe.Slug)!, recipe.Slug)));

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
            new Settings(ResolveNames: false, ActiveRecipe: Clan.Slug, StartOnOpen: true, Modes: new Dictionary<string, bool> { ["pet-sim-99/battle"] = false }), [Main, AltOne]);

        pack.ToFolder(dir.Path);
        var back = SetupPack.FromFolder(dir.Path);

        Assert.NotNull(back);
        // Names and text are not carried (A7): a reader travels as its slug and its choices.
        Assert.Equal(pack.Recipes.Select(r => r.Slug).Order(StringComparer.Ordinal), back.Recipes.Select(r => r.Slug));
        Assert.All(pack.Recipes.Concat(back.Recipes), r => Assert.Null(r.Text));
        var packClan = pack.Recipes.Single(r => r.Slug == Clan.Slug);
        var backClan = back.Recipes.Single(r => r.Slug == Clan.Slug);
        Assert.Equal(packClan.State.StatChoices["value"], backClan.State.StatChoices["value"]);
        Assert.Equal(packClan.ExcludedUserIds, backClan.ExcludedUserIds);

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

        Assert.Equal((false, null), (back.Settings.ResolveNames, back.Settings.ActiveRecipe));   // activeRecipe is never written (A7)
        Assert.Equal(new Dictionary<string, bool> { ["pet-sim-99/battle"] = false }, back.Settings.Modes);
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

        // Nor does ClanInstalled's excluded RoRoRo account GUIDs: an account id, and one that isn't anybody's.
        Assert.DoesNotContain("22222222-2222", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000-0000-0000-0000-00000000dead", everything, StringComparison.Ordinal);
    }

    [Fact]
    public void StartOnOpenDoesNotTravelAndBoardsAreSanitized()
    {
        using var dir = TempDir.Create("urscore-setup");
        var stranger = new BoardDef("b-1", "Battle", [new PanelDef("p-1", PanelType.AccountCard, new PanelSize(6), new PanelSettings(Clan.Slug, UserId: 987654321))]);
        var pack = SetupPack.FromHere([ClanInstalled()], [], [stranger], new Settings(StartOnOpen: true), [Main]);

        Assert.False(pack.Settings.StartOnOpen);
        Assert.Null(Assert.Single(pack.Boards).Panels[0].Settings.UserId);

        // Not merely false: the KEY isn't on disk, so a later default flip can't turn it on by finding it there.
        pack.ToFolder(dir.Path);
        var settingsText = File.ReadAllText(Path.Combine(dir.Path, SetupPack.Folder, "settings.json"));
        Assert.DoesNotContain("startOnOpen", settingsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFolderWithNoSetupIsNoPack()
    {
        using var dir = TempDir.Create("urscore-setup");
        Assert.Null(SetupPack.FromFolder(dir.Path));
    }

    /// <summary>A new export holds state and modes and no reader text, and settings.json drops activeRecipe.</summary>
    [Fact]
    public void ANewExportHasNoReaderTextAndNoActiveRecipe()
    {
        using var dir = TempDir.Create("urscore-setup");
        var modes = new Dictionary<string, bool> { ["pet-sim-99/profile"] = false };
        var pack = SetupPack.FromHere([ClanInstalled()], [MainClan], [], new Settings(ActiveRecipe: Clan.Slug, Modes: modes), [Main]);

        pack.ToFolder(dir.Path);

        var folder = Path.Combine(dir.Path, SetupPack.Folder);
        Assert.Empty(Directory.EnumerateFiles(folder, "*.recipe.json", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(folder, "recipes", Clan.Slug + ".state.json")));
        Assert.DoesNotContain("activeRecipe", File.ReadAllText(Path.Combine(folder, "settings.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pet-sim-99/profile", File.ReadAllText(Path.Combine(folder, "modes.json")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A 0.6.3 export holds a .recipe.json and a .state.json per reader and an activeRecipe: the text is ignored, the state
    /// read, and a folder with only states (or only old recipe files) enumerates the same readers.
    /// </summary>
    [Fact]
    public void AnOldExportWithRecipeTextStillReadsItsStatesAndIgnoresTheText()
    {
        using var dir = TempDir.Create("urscore-setup");
        var recipes = Directory.CreateDirectory(Path.Combine(dir.Path, SetupPack.Folder, "recipes")).FullName;
        File.WriteAllText(Path.Combine(recipes, Clan.Slug + ".recipe.json"), RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json"));
        File.WriteAllText(Path.Combine(recipes, Clan.Slug + ".state.json"), RecipeStore.SerializeState(ClanInstalled().State));
        File.WriteAllText(Path.Combine(recipes, "old-only.recipe.json"), "not even json");   // no state file: still a reader the file names
        File.WriteAllText(Path.Combine(dir.Path, SetupPack.Folder, "settings.json"), """{"resolveNames":false,"activeRecipe":"pet-sim-99-clan-battle-points"}""");

        var back = SetupPack.FromFolder(dir.Path)!;

        Assert.Equal(["old-only", Clan.Slug], back.Recipes.Select(r => r.Slug).Order(StringComparer.Ordinal));
        Assert.All(back.Recipes, r => Assert.Null(r.Text));
        Assert.True(back.Recipes.Single(r => r.Slug == Clan.Slug).State.StatChoices["value"].Show);
        Assert.False(back.Settings.ResolveNames);
        Assert.Null(back.Settings.ActiveRecipe);
        Assert.Null(back.Settings.Modes);   // no modes.json: nothing to apply
    }

    /// <summary>
    /// Review round 2: a 0.6.3 export carries the clan battle's state as 0.6.3 wrote it, with only a legacy
    /// <c>metricIdOverride</c>. Read plainly that is no ticks at all, so the import would have offered nothing and the
    /// pinned id was lost. Read the way the app reads its own folder (with the built-in reader), it is the value ticked to
    /// show and send under the pinned id.
    /// </summary>
    [Fact]
    public void AnOldExportsLegacyMetricIdIsReadAsItsTick()
    {
        using var dir = TempDir.Create("urscore-setup");
        var recipes = Directory.CreateDirectory(Path.Combine(dir.Path, SetupPack.Folder, "recipes")).FullName;
        File.WriteAllText(Path.Combine(recipes, Clan.Slug + ".state.json"), """{ "inputs": { "clan": "TestClan" }, "metricIdOverride": "clan.battle.points.mine" }""");

        var state = Assert.Single(SetupPack.FromFolder(dir.Path)!.Recipes).State;

        Assert.Equal(new StatChoice(Show: true, Send: true, MetricId: "clan.battle.points.mine"), Assert.Single(state.StatChoices).Value);
        Assert.Equal("TestClan", state.InputValues["clan"]);
    }

    /// <summary>Review round 2: a state with no inputs and one with an empty inputs object are the same state.</summary>
    [Fact]
    public void NoInputsAndEmptyInputsAreCanonicallyTheSame() =>
        Assert.Equal(SetupPack.Canonical(new RecipeState()), SetupPack.Canonical(new RecipeState(Inputs: new Dictionary<string, string>())));

    [Fact]
    public void AStateOnlyFolderReadsToo()
    {
        using var dir = TempDir.Create("urscore-setup");
        SetupPack.FromHere([ClanInstalled()], [], [], Settings.Defaults, [Main]).ToFolder(dir.Path);

        Assert.Equal(Clan.Slug, Assert.Single(SetupPack.FromFolder(dir.Path)!.Recipes).Slug);
    }

    /// <summary>A pristine PC (the shipped readers as seeded, their automatic sources, default switches) has nothing to carry.</summary>
    [Fact]
    public void APristinePcExportsNothing()
    {
        var recipes = new[] { Pristine(Clan, RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")), Pristine(Profile, RecipeParserTests.Fixture("petsim99-profile.recipe.json")) };
        var automatic = new Source("s-auto0001", Profile.Slug, new Dictionary<string, string>(), SourceRole.Main);
        var defaults = new Dictionary<string, bool> { ["pet-sim-99"] = true, ["pet-sim-99/battle"] = true, ["pet-sim-99/profile"] = true };

        var pack = SetupPack.FromHere(recipes, [automatic], [], new Settings(StartOnOpen: true, Modes: defaults), [Main]);

        Assert.True(pack.IsEmpty);
        Assert.Empty(pack.Recipes);
        Assert.Null(pack.Settings.Modes);   // switches equal to their defaults are not a choice
    }

    /// <summary>Each thing a person can make un-empties it: a clan, a tick, a board of their own, a switch away from its default.</summary>
    [Fact]
    public void AnythingThePersonMadeMakesTheExportNonEmpty()
    {
        var clan = Pristine(Clan, RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json"));
        var typed = new Source("s-clan0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);
        var board = new BoardDef("b-1", "Mine", []);

        Assert.False(SetupPack.FromHere([clan], [typed], [], Settings.Defaults, [Main]).IsEmpty);                                       // one clan
        Assert.False(SetupPack.FromHere([clan with { State = clan.State with { Stats = new Dictionary<string, StatChoice>(clan.State.Stats!) { ["rank"] = new(true, false, "clan.battle.rank") } } }], [], [], Settings.Defaults, [Main]).IsEmpty);   // a tick
        Assert.False(SetupPack.FromHere([clan], [], [board], Settings.Defaults, [Main]).IsEmpty);                                      // a board
        Assert.False(SetupPack.FromHere([clan], [], [], new Settings(Modes: new Dictionary<string, bool> { ["pet-sim-99/battle"] = false }), [Main]).IsEmpty);   // a switch
        Assert.True(SetupPack.FromHere([clan with { State = clan.State with { CounterNames = ["Points"] } }], [], [new BoardDef("b-s", "Alts", [], Follows: "alts")], Settings.Defaults, [Main]).IsEmpty);   // a read's counter names and a following tab are not
    }
}
