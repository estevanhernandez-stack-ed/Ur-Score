using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The plan (spec §2): what importing a setup would do, item by item, with nothing written. Each identity rule has a
/// decoy that differs only in case or spaces; each outcome is pinned; sends arrive off; a clan needs its recipe.
/// Cannot be red before the class exists; the mutations named on each test are what it must survive.
/// </summary>
public class SetupMergeTests
{
    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
    private static readonly string ProfileText = RecipeParserTests.Fixture("petsim99-profile.recipe.json");
    private static readonly HostAccount AltOne = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "AshAlt");

    private static SetupRecipe FileRecipe(Recipe recipe, string text, RecipeState? state = null, params long[] excluded) =>
        new(recipe.Slug, recipe.Name, text, state ?? new RecipeState(), excluded);

    private static SetupHere Here(IReadOnlyList<InstalledRecipe>? installed = null, IReadOnlyList<Source>? sources = null, IReadOnlyList<BoardDef>? boards = null) =>
        new(installed ?? [], sources ?? [], boards ?? [], [Main, AltOne]);

    private static SetupPack Pack(IReadOnlyList<SetupRecipe>? recipes = null, IReadOnlyList<Source>? sources = null, IReadOnlyList<BoardDef>? boards = null, IReadOnlyList<SetupKey>? keys = null) =>
        new(recipes ?? [], sources ?? [], boards ?? [], Settings.Defaults, keys ?? []);

    private static SetupItem Item(SetupMergePlan plan, SetupKind kind, string name) => Assert.Single(plan.Items, i => i.Kind == kind && i.Name == name);

    [Fact]
    public void ARecipeIsAddedUpdatedOrSameBySlugAndText()
    {
        var here = Here([new InstalledRecipe(Clan, ClanText, new RecipeState())]);
        var file = Pack([FileRecipe(Clan, ClanText + "\n"), FileRecipe(Profile, ProfileText)]);

        var plan = SetupMerge.Plan(file, here, 0, 0);

        Assert.Equal(SetupOutcome.Update, Item(plan, SetupKind.Recipe, Clan.Name).Outcome);       // text differs: file wins
        Assert.Equal(SetupOutcome.Add, Item(plan, SetupKind.Recipe, Profile.Name).Outcome);
        var same = SetupMerge.Plan(Pack([FileRecipe(Clan, ClanText)]), here, 0, 0);
        Assert.Equal(SetupOutcome.Same, Item(same, SetupKind.Recipe, Clan.Name).Outcome);
        Assert.False(Item(same, SetupKind.Recipe, Clan.Name).Ticked);
        Assert.True(Item(plan, SetupKind.Recipe, Clan.Name).Ticked);
    }

    [Fact]
    public void AClanIsMatchedByRecipeAndNameWhateverTheCaseAndSpacing()
    {
        var mine = new Source("s-local001", Clan.Slug, new Dictionary<string, string> { ["clan"] = " k0i2 " }, SourceRole.Mine);
        var here = Here([new InstalledRecipe(Clan, ClanText, new RecipeState())], [mine, Rival]);
        var file = Pack(
            [FileRecipe(Clan, ClanText)],
            [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main),   // same clan, other role
             new Source("s-file0002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "NovaForge" }, SourceRole.Watch), // same as Rival
             new Source("s-file0003", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)]);   // only in the file

        var plan = SetupMerge.Plan(file, here, 0, 0);

        var replaced = Item(plan, SetupKind.Clan, "K0i2");
        Assert.Equal((SetupOutcome.Replace, "s-file0001", "s-local001"), (replaced.Outcome, replaced.FileId, replaced.LocalId));
        Assert.Contains("yours", replaced.Note, StringComparison.Ordinal);   // "here it is yours; the file says main"
        Assert.Equal(SetupOutcome.Same, Item(plan, SetupKind.Clan, "NovaForge").Outcome);
        var added = Item(plan, SetupKind.Clan, "CCGP");
        Assert.Equal((SetupOutcome.Add, "s-file0003"), (added.Outcome, added.FileId));
        Assert.Null(added.LocalId);
        Assert.Equal(Clan.Slug, added.DependsOnRecipe);
    }

    [Fact]
    public void AClanOnlyHereIsKeptAndListed()
    {
        var here = Here([new InstalledRecipe(Clan, ClanText, new RecipeState())], [Rival]);

        var plan = SetupMerge.Plan(Pack([FileRecipe(Clan, ClanText)]), here, 0, 0);

        var kept = Item(plan, SetupKind.Clan, "NovaForge");
        Assert.Equal((SetupOutcome.Kept, false), (kept.Outcome, kept.Ticked));
    }

    [Fact]
    public void ARecipeOrBoardOnlyHereIsKeptAndListed()
    {
        var onlyHereBoard = new BoardDef("b-1", "OnlyHere", []);
        var here = Here([new InstalledRecipe(Clan, ClanText, new RecipeState())], boards: [onlyHereBoard]);

        var plan = SetupMerge.Plan(Pack([FileRecipe(Profile, ProfileText)]), here, 0, 0);

        var keptRecipe = Item(plan, SetupKind.Recipe, Clan.Name);
        Assert.Equal((SetupOutcome.Kept, false), (keptRecipe.Outcome, keptRecipe.Ticked));
        var keptBoard = Item(plan, SetupKind.Board, "OnlyHere");
        Assert.Equal((SetupOutcome.Kept, false), (keptBoard.Outcome, keptBoard.Ticked));
    }

    [Fact]
    public void ABoardIsReplacedByNameOrAddedAndFollowingTabsAreLeftAlone()
    {
        var hereBattle = new BoardDef("b-1", "battle", [new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug))]);
        var following = new BoardDef("b-starter-alts", "Alts", [], Follows: "alts");
        var fileBattle = new BoardDef("b-9", "Battle ", [new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug)), new PanelDef("p-2", PanelType.Race, new PanelSize(6), new PanelSettings(Clan.Slug))]);
        var fileRivals = new BoardDef("b-8", "Rivals", []);

        var plan = SetupMerge.Plan(Pack(boards: [fileBattle, fileRivals, following]), Here(boards: [hereBattle, following]), 0, 0);

        var replaced = Item(plan, SetupKind.Board, "Battle ");
        Assert.Equal(SetupOutcome.Replace, replaced.Outcome);
        Assert.Contains("1 panel here, 2 in the file", replaced.Note, StringComparison.Ordinal);
        Assert.Equal(SetupOutcome.Add, Item(plan, SetupKind.Board, "Rivals").Outcome);
        Assert.DoesNotContain(plan.Items, i => i.Kind == SetupKind.Board && i.Name == "Alts");
    }

    [Fact]
    public void TwoLocalBoardsFoldingToTheSameNameDoNotThrowAndTheFirstIsMatched()
    {
        var battle = new BoardDef("b-1", "Battle", []);
        var battleAgain = new BoardDef("b-2", " battle ", []);
        var fileBattle = new BoardDef("b-9", "BATTLE", []);

        var plan = SetupMerge.Plan(Pack(boards: [fileBattle]), Here(boards: [battle, battleAgain]), 0, 0);

        Assert.Equal(SetupOutcome.Replace, Assert.Single(plan.Items, i => i.Kind == SetupKind.Board).Outcome);
    }

    [Fact]
    public void KeysAreRemindersAndStatsAreOneLine()
    {
        var plan = SetupMerge.Plan(Pack(keys: [new SetupKey("ps99", "PS99 key", Profile.Slug, Profile.Name)]), Here(), 1204, 12);

        var key = Item(plan, SetupKind.Key, "PS99 key");
        Assert.Equal((SetupOutcome.EnterAgain, false), (key.Outcome, key.Ticked));
        var stats = Assert.Single(plan.Items, i => i.Kind == SetupKind.Stats);
        Assert.Equal((SetupOutcome.Add, true, "1,204 readings and 12 finished battles"), (stats.Outcome, stats.Ticked, stats.Name));
    }

    [Fact]
    public void AClanCannotBeTickedWithoutItsRecipe()
    {
        var file = Pack([FileRecipe(Clan, ClanText)], [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)]);
        var plan = SetupMerge.Plan(file, Here(), 0, 0);
        var clan = Item(plan, SetupKind.Clan, "CCGP");

        Assert.True(plan.CanTick(clan, new HashSet<string> { "recipe:" + Clan.Slug }));
        Assert.False(plan.CanTick(clan, new HashSet<string>()));
        Assert.Empty(plan.Ticked(new HashSet<string> { clan.Key }));                         // ticked, but its recipe is not
        Assert.Contains(clan, plan.Ticked(new HashSet<string> { clan.Key, "recipe:" + Clan.Slug }));

        var installedHere = SetupMerge.Plan(file, Here([new InstalledRecipe(Clan, ClanText, new RecipeState())]), 0, 0);
        Assert.True(installedHere.CanTick(Item(installedHere, SetupKind.Clan, "CCGP"), new HashSet<string>()));
    }

    [Fact]
    public void AStateArrivesWithEverySendOffAndExclusionsMappedToThisPcsAccounts()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points"),
            ["rank"] = new(Show: false, Send: true, MetricId: "clan.battle.rank"),
        });

        var arriving = SetupMerge.Arriving(state, [201, 999], [Main, AltOne], out var dropped);

        Assert.All(arriving.StatChoices.Values, choice => Assert.False(choice.Send));
        Assert.Equal((true, "clan.battle.points"), (arriving.StatChoices["value"].Show, arriving.StatChoices["value"].MetricId));
        Assert.Equal([AltOne.AccountId], arriving.Excluded);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void ArrivingDoesNotThrowWhenTwoHostAccountsShareARobloxIdAndMapsToTheFirst()
    {
        var alsoTwoOhOne = new HostAccount(Guid.Parse("55555555-5555-5555-5555-555555555555"), 201, "SecondAsh");

        var arriving = SetupMerge.Arriving(new RecipeState(), [201], [AltOne, alsoTwoOhOne], out var dropped);

        Assert.Equal([AltOne.AccountId], arriving.Excluded);
        Assert.Equal(0, dropped);
    }

    private sealed class FakeSetupWriter(string root, SetupHere here) : ISetupWriter
    {
        public string DataRoot => root;
        public SetupHere Here => here;
        public List<(string Slug, RecipeState State)> Recipes { get; } = [];
        public IReadOnlyList<Source>? Sources { get; private set; }
        public IReadOnlyList<BoardDef>? Boards { get; private set; }
        public Settings? Settings { get; private set; }
        public int Reloads { get; private set; }
        public string? ThrowAt { get; init; }
        public void SaveRecipe(Recipe recipe, string text, RecipeState state) { if (ThrowAt == "recipes") throw new IOException("disk"); Recipes.Add((recipe.Slug, state)); }
        public void SaveSources(IReadOnlyList<Source> sources) { if (ThrowAt == "clans") throw new IOException("disk"); Sources = sources; }
        public void SaveImportedBoards(IReadOnlyList<BoardDef> saved) { if (ThrowAt == "boards") throw new UnauthorizedAccessException("denied"); Boards = saved; }
        public void SaveSettings(Settings settings) => Settings = settings;
        public void ReloadRecipes() => Reloads++;
    }

    private static (SetupMergePlan Plan, FakeSetupWriter Writer, TempDir.Scope Dir) Scenario(string? throwAt = null)
    {
        var dir = TempDir.Create("urscore-apply");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        File.WriteAllText(Path.Combine(data, "sources.json"), "[]");
        Directory.CreateDirectory(Path.Combine(data, "recipes"));
        File.WriteAllText(Path.Combine(data, "recipes", "old.recipe.json"), "{}");
        var local = new Source("s-local001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Watch);
        var here = new SetupHere([], [local, Rival], [new BoardDef("b-1", "Battle", [])], [Main, AltOne]);
        var fileMain = new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main);
        var fileNew = new Source("s-file0002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Mine);
        var fileBoard = new BoardDef("b-9", "Battle", [
            new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0001")),
            new PanelDef("p-2", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0002")),
            new PanelDef("p-3", PanelType.Race, new PanelSize(6), new PanelSettings(Clan.Slug, SourceIds: ["s-file0001", "s-file0002"])),
        ]);
        var file = new SetupPack([FileRecipe(Clan, ClanText, new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(true, true, "clan.battle.points") }), 201, 999)],
            [fileMain, fileNew], [fileBoard], new Settings(ResolveNames: false, ActiveRecipe: Clan.Slug), []);
        var plan = SetupMerge.Plan(file, here, 0, 0);
        return (plan, new FakeSetupWriter(data, here) { ThrowAt = throwAt }, dir);
    }

    [Fact]
    public void ApplyWritesInOrderMintsIdsForNewClansAndRewritesBoardsThroughThem()
    {
        var (plan, writer, dir) = Scenario();
        using (dir)
        {
            var all = plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            var applied = SetupMerge.Apply(plan, all, writer, new DateTimeOffset(2026, 9, 22, 14, 31, 0, TimeSpan.Zero));

            Assert.Null(applied.FailedStep);
            Assert.Equal((1, 2, 1, 1, 1), (applied.Recipes, applied.Clans, applied.Boards, applied.KeptClans, applied.DroppedExclusions));
            Assert.False(writer.Recipes.Single().State.StatChoices["value"].Send);
            Assert.Equal(3, writer.Sources!.Count);
            var k0i2 = Assert.Single(writer.Sources, s => s.InputsKey == "clan=k0i2");
            Assert.Equal(("s-local001", SourceRole.Main), (k0i2.Id, k0i2.Role));                 // replaced under the LOCAL id
            var ccgp = Assert.Single(writer.Sources, s => s.InputsKey == "clan=ccgp");
            Assert.StartsWith("s-", ccgp.Id, StringComparison.Ordinal);
            Assert.NotEqual("s-file0002", ccgp.Id);                                               // minted, never the file's
            Assert.Contains(writer.Sources, s => s.Id == Rival.Id);                                  // kept
            var board = Assert.Single(writer.Boards!, b => b.Name == "Battle");
            Assert.Equal("s-local001", board.Panels[0].Settings.SourceId);
            Assert.Equal(ccgp.Id, board.Panels[1].Settings.SourceId);
            Assert.Equal(["s-local001", ccgp.Id], board.Panels[2].Settings.SourceIds);
            Assert.Equal(1, writer.Reloads);
            Assert.Equal((false, Clan.Slug), (writer.Settings!.ResolveNames, writer.Settings.ActiveRecipe));
            var aside = Directory.GetDirectories(dir.Path, "626labs.ur-score.before-import-*").Single();
            Assert.Equal(aside, applied.AsideFolder);
            Assert.True(File.Exists(Path.Combine(aside, "sources.json")));
            Assert.True(File.Exists(Path.Combine(aside, "recipes", "old.recipe.json")));
        }
    }

    [Fact]
    public void AnUntickedClanIsNotWrittenAndABoardPanelPointingAtItIsLeftPointingAtNothingHere()
    {
        var (plan, writer, dir) = Scenario();
        using (dir)
        {
            var ticks = plan.Items.Select(i => i.Key).Where(k => k != "clan:" + Clan.Slug + "|clan=ccgp").ToHashSet(StringComparer.Ordinal);

            SetupMerge.Apply(plan, ticks, writer, DateTimeOffset.UtcNow);

            Assert.DoesNotContain(writer.Sources!, s => s.InputsKey == "clan=ccgp");
            var board = Assert.Single(writer.Boards!, b => b.Name == "Battle");
            Assert.Equal("s-file0002", board.Panels[1].Settings.SourceId);   // nothing here has that id: the panel shows stale
        }
    }

    [Fact]
    public void AFailureNamesItsStepAndTheStepsBeforeItStand()
    {
        var (plan, writer, dir) = Scenario(throwAt: "boards");
        using (dir)
        {
            var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

            Assert.Equal(("boards", nameof(UnauthorizedAccessException)), (applied.FailedStep, applied.FailureType));
            Assert.Single(writer.Recipes);
            Assert.NotNull(writer.Sources);
            Assert.Null(writer.Boards);
            Assert.Null(writer.Settings);
            Assert.NotNull(applied.AsideFolder);
        }
    }
}
