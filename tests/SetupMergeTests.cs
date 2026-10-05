using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Games;
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

    /// <summary>A reader's state as the file carries it: no text (A7), and named by its slug, as FromFolder names it.</summary>
    private static SetupRecipe FileRecipe(Recipe recipe, RecipeState? state = null, params long[] excluded) =>
        new(recipe.Slug, recipe.Slug, null, state ?? new RecipeState(), excluded);

    /// <summary>The state a reader here runs with when nothing was ticked: the seed, as <c>Readers.Compose</c> resolves it.</summary>
    private static InstalledRecipe Seeded(Recipe recipe, string text) =>
        new(recipe, text, RecipeStates.Effective(recipe, null, Readers.ShowsFor(GameCatalog.BuiltIn.ModeOf(recipe.Slug)!, recipe.Slug)));

    private static SetupHere Here(IReadOnlyList<InstalledRecipe>? installed = null, IReadOnlyList<Source>? sources = null, IReadOnlyList<BoardDef>? boards = null) =>
        new(installed ?? [], sources ?? [], boards ?? [], [Main, AltOne], Settings.Defaults);

    private static SetupPack Pack(IReadOnlyList<SetupRecipe>? recipes = null, IReadOnlyList<Source>? sources = null, IReadOnlyList<BoardDef>? boards = null, IReadOnlyList<SetupKey>? keys = null) =>
        new(recipes ?? [], sources ?? [], boards ?? [], Settings.Defaults, keys ?? []);

    private static SetupItem Item(SetupMergePlan plan, SetupKind kind, string name) => Assert.Single(plan.Items, i => i.Kind == kind && i.Name == name);

    /// <summary>A clan with no inputs (a profile, a clans list) is named by its mode, not its slug; one whose reader is nowhere keeps the slug.</summary>
    [Fact]
    public void AClanWithNoInputsIsNamedByItsRecipe()
    {
        var inputless = new Source("s-file0003", Profile.Slug, new Dictionary<string, string>(), SourceRole.Mine);
        var orphan = new Source("s-local009", "gone-recipe", new Dictionary<string, string>(), SourceRole.Watch);

        var plan = SetupMerge.Plan(Pack([FileRecipe(Profile)], [inputless]), Here(sources: [orphan]), 0, 0);

        Assert.Equal(SetupOutcome.Add, Item(plan, SetupKind.Clan, "Profile").Outcome);   // named by its mode, not its recipe
        Assert.Equal(SetupOutcome.Kept, Item(plan, SetupKind.Clan, "gone-recipe").Outcome);
    }

    /// <summary>
    /// Both PCs carry the same embedded text now, so text can say nothing: a reader's ticks merge by serialized STATE
    /// (A7), compared in a canonical order and with Send set aside (sends arrive off). Labelled by mode name.
    /// </summary>
    [Fact]
    public void AReaderIsUpdatedOrSameByStateAndNamedByItsMode()
    {
        var here = Here([Seeded(Clan, ClanText), Seeded(Profile, ProfileText)]);
        var ticked = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: false, MetricId: "clan.battle.points"), ["rank"] = new(Show: true, Send: false, MetricId: "clan.battle.rank") });
        var file = Pack([FileRecipe(Clan, ticked), FileRecipe(Profile)]);

        var plan = SetupMerge.Plan(file, here, 0, 0);

        Assert.Equal(SetupOutcome.Update, Item(plan, SetupKind.Recipe, "Battle").Outcome);      // the ticks differ: file wins
        Assert.True(Item(plan, SetupKind.Recipe, "Battle").Ticked);
        // The file's Profile has no stats at all, and a state with none seeds like no state: the same as here.
        var same = Item(plan, SetupKind.Recipe, "Profile");
        Assert.Equal((SetupOutcome.Same, false), (same.Outcome, same.Ticked));
    }

    /// <summary>Dictionary order and Send are not differences: the same ticks written in another order, sent or not, are the same.</summary>
    [Fact]
    public void TheSameTicksInAnotherOrderOrWithSendOnAreTheSame()
    {
        var mine = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(true, false, "clan.battle.points"), ["rank"] = new(true, false, "clan.battle.rank") });
        var theirs = new RecipeState(Stats: new Dictionary<string, StatChoice> { ["rank"] = new(true, true, "clan.battle.rank"), ["value"] = new(true, false, "clan.battle.points") });

        var plan = SetupMerge.Plan(Pack([FileRecipe(Clan, theirs)]), Here([new InstalledRecipe(Clan, ClanText, mine)]), 0, 0);

        Assert.Equal(SetupOutcome.Same, Item(plan, SetupKind.Recipe, "Battle").Outcome);
    }

    /// <summary>A reader no mode names is skipped with its clans, and Apply counts them.</summary>
    [Fact]
    public void AReaderNoModeNamesIsSkippedWithItsClans()
    {
        var orphanClan = new Source("s-file0009", "roblox-followers", new Dictionary<string, string> { ["user"] = "Builderman" }, SourceRole.Watch);
        var file = Pack(
            [new SetupRecipe("roblox-followers", "roblox-followers", null, new RecipeState(), [])],
            [orphanClan]);
        var here = Here([Seeded(Clan, ClanText)]);

        var plan = SetupMerge.Plan(file, here, 0, 0);

        var recipe = Item(plan, SetupKind.Recipe, "roblox-followers");
        var clan = Item(plan, SetupKind.Clan, "Builderman");
        Assert.All([recipe, clan], i => Assert.Equal((SetupOutcome.Skipped, false), (i.Outcome, i.Ticked)));

        using var dir = TempDir.Create("urscore-apply-skip");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var writer = new FakeSetupWriter(data, here);
        var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

        Assert.Null(applied.FailedStep);
        Assert.Equal((2, 0, 0), (applied.SkippedItems, applied.Recipes, applied.Clans));
        Assert.Empty(writer.Recipes);
        Assert.Null(writer.Sources);
    }

    /// <summary>The file's mode switches are one item: same when each already holds here, an update that names what it sets when not.</summary>
    [Fact]
    public void ModeSwitchesAreOneItemSameOrUpdate()
    {
        var offBattle = new Settings(Modes: new Dictionary<string, bool> { ["pet-sim-99/battle"] = false, ["pet-sim-99/profile"] = true });

        var update = SetupMerge.Plan(new SetupPack([], [], [], offBattle, []), Here(), 0, 0);
        var same = SetupMerge.Plan(new SetupPack([], [], [], new Settings(Modes: new Dictionary<string, bool> { ["pet-sim-99/profile"] = true }), []), Here(), 0, 0);
        var none = SetupMerge.Plan(new SetupPack([], [], [], Settings.Defaults, []), Here(), 0, 0);

        var item = Item(update, SetupKind.Mode, "Mode switches");
        Assert.Equal((SetupOutcome.Update, true), (item.Outcome, item.Ticked));
        Assert.Contains("Battle off", item.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("Profile", item.Note, StringComparison.Ordinal);                  // an explicit default changes nothing
        Assert.Equal(SetupOutcome.Same, Item(same, SetupKind.Mode, "Mode switches").Outcome);
        Assert.DoesNotContain(none.Items, i => i.Kind == SetupKind.Mode);
    }

    [Fact]
    public void AClanIsMatchedByRecipeAndNameWhateverTheCaseAndSpacing()
    {
        var mine = new Source("s-local001", Clan.Slug, new Dictionary<string, string> { ["clan"] = " k0i2 " }, SourceRole.Mine);
        var here = Here([Seeded(Clan, ClanText)], [mine, Rival]);
        var file = Pack(
            [FileRecipe(Clan)],
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
        var here = Here([Seeded(Clan, ClanText)], [Rival]);

        var plan = SetupMerge.Plan(Pack([FileRecipe(Clan)]), here, 0, 0);

        var kept = Item(plan, SetupKind.Clan, "NovaForge");
        Assert.Equal((SetupOutcome.Kept, false), (kept.Outcome, kept.Ticked));
    }

    [Fact]
    public void ABoardOnlyHereIsKeptAndListedButAReaderAbsentFromTheFileIsNot()
    {
        var onlyHereBoard = new BoardDef("b-1", "OnlyHere", []);
        var here = Here([Seeded(Clan, ClanText)], boards: [onlyHereBoard]);

        var plan = SetupMerge.Plan(Pack([FileRecipe(Profile)]), here, 0, 0);

        // The file lists only readers whose ticks changed, so a reader missing from it is unchanged there: not "only here".
        Assert.DoesNotContain(plan.Items, i => i.Kind == SetupKind.Recipe && i.Outcome == SetupOutcome.Kept);
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
        var file = Pack([FileRecipe(Clan)], [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)]);
        var plan = SetupMerge.Plan(file, Here(), 0, 0);
        var clan = Item(plan, SetupKind.Clan, "CCGP");

        Assert.True(plan.CanTick(clan, new HashSet<string> { "recipe:" + Clan.Slug }));
        Assert.False(plan.CanTick(clan, new HashSet<string>()));
        Assert.Empty(plan.Ticked(new HashSet<string> { clan.Key }));                         // ticked, but its recipe is not
        Assert.Contains(clan, plan.Ticked(new HashSet<string> { clan.Key, "recipe:" + Clan.Slug }));

        var installedHere = SetupMerge.Plan(file, Here([Seeded(Clan, ClanText)]), 0, 0);
        Assert.True(installedHere.CanTick(Item(installedHere, SetupKind.Clan, "CCGP"), new HashSet<string>()));
    }

    [Fact]
    public void AStateArrivesWithEverySendOffAndExclusionsMappedToThisPcsAccounts()
    {
        var state = new RecipeState(
            Stats: new Dictionary<string, StatChoice>
            {
                ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points"),
                ["rank"] = new(Show: false, Send: true, MetricId: "clan.battle.rank"),
            },
            SentFieldMetrics: [FieldMetrics.ThreatGap, FieldMetrics.Points]);

        var arriving = SetupMerge.Arriving(state, [201, 999], [Main, AltOne], out var dropped);

        Assert.All(arriving.StatChoices.Values, choice => Assert.False(choice.Send));
        Assert.Equal((true, "clan.battle.points"), (arriving.StatChoices["value"].Show, arriving.StatChoices["value"].MetricId));
        // The SECOND send list (a clans list's clan-and-field numbers, sent under fixed ids with no account
        // attached). Stats[*].Send is not all of "sends arrive off": this one has its own property, and
        // AppServices.PolicyFor hands it to every ReportPolicy outside the role gate.
        Assert.Empty(arriving.FieldMetricKeys);
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
        public List<string> Calls { get; } = [];
        public string? ThrowAt { get; init; }
        public string? ThrowMessage { get; init; }
        public void SaveState(string slug, RecipeState state) { if (ThrowAt == "recipes") throw new IOException(ThrowMessage ?? "disk"); Calls.Add("SaveState"); Recipes.Add((slug, state)); }
        public void SaveSources(IReadOnlyList<Source> sources) { if (ThrowAt == "clans") throw new IOException(ThrowMessage ?? "disk"); Calls.Add("SaveSources"); Sources = sources; }
        public void SaveImportedBoards(IReadOnlyList<BoardDef> saved) { if (ThrowAt == "boards") throw new UnauthorizedAccessException(ThrowMessage ?? "denied"); Calls.Add("SaveImportedBoards"); Boards = saved; }
        public void SaveSettings(Settings settings) { if (ThrowAt == "settings") throw new IOException(ThrowMessage ?? "disk"); Calls.Add("SaveSettings"); Settings = settings; }
    }

    /// <summary>
    /// One clan replaced under its local id (K0i2, Main), one clan added and minted (CCGP), one clan only here and
    /// untouched (Rival), a board replaced by name with a panel on each clan plus a <c>ToSourceId</c> panel, and a
    /// second board the file alone carries (the Add branch). CCGP's id is never known ahead of Apply, so panels that
    /// point at it are the ones worth checking after Apply mints it.
    /// </summary>
    private static (SetupMergePlan Plan, FakeSetupWriter Writer, TempDir.Scope Dir) Scenario(string? throwAt = null)
    {
        var dir = TempDir.Create("urscore-apply");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        File.WriteAllText(Path.Combine(data, "sources.json"), "[]");
        Directory.CreateDirectory(Path.Combine(data, "recipes"));
        File.WriteAllText(Path.Combine(data, "recipes", "old.recipe.json"), "{}");
        var local = new Source("s-local001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Watch);
        var here = new SetupHere([Seeded(Clan, ClanText)], [local, Rival], [new BoardDef("b-1", "Battle", [])], [Main, AltOne], Settings.Defaults);
        var fileMain = new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main);
        var fileNew = new Source("s-file0002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Mine);
        var fileBoard = new BoardDef("b-9", "Battle", [
            new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0001")),
            new PanelDef("p-2", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0002")),
            new PanelDef("p-3", PanelType.Race, new PanelSize(6), new PanelSettings(Clan.Slug, SourceIds: ["s-file0001", "s-file0002"])),
            new PanelDef("p-4", PanelType.PromotionCheck, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0001", ToSourceId: "s-file0002", Stat: "value")),
        ]);
        var fileRivals = new BoardDef("b-10", "Rivals", [
            new PanelDef("p-5", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-file0002")),
        ]);
        var file = new SetupPack([FileRecipe(Clan, new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(true, true, "clan.battle.points") }), 201, 999)],
            [fileMain, fileNew], [fileBoard, fileRivals], new Settings(ResolveNames: false, ActiveRecipe: Clan.Slug), []);   // the file's ActiveRecipe is an old export's: never applied
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
            Assert.Equal((1, 2, 2, 1, 1), (applied.Recipes, applied.Clans, applied.Boards, applied.KeptClans, applied.DroppedExclusions));
            Assert.False(writer.Recipes.Single().State.StatChoices["value"].Send);
            Assert.Equal(3, writer.Sources!.Count);
            var k0i2 = Assert.Single(writer.Sources, s => s.InputsKey == "clan=k0i2");
            Assert.Equal(("s-local001", SourceRole.Main), (k0i2.Id, k0i2.Role));                 // replaced under the LOCAL id
            var ccgp = Assert.Single(writer.Sources, s => s.InputsKey == "clan=ccgp");
            Assert.StartsWith("s-", ccgp.Id, StringComparison.Ordinal);
            Assert.NotEqual("s-file0002", ccgp.Id);                                               // minted, never the file's
            Assert.Contains(writer.Sources, s => s.Id == Rival.Id);                                  // kept
            var board = Assert.Single(writer.Boards!, b => b.Name == "Battle");
            Assert.Equal("b-1", board.Id);                                                        // the LOCAL board's id, not the file's b-9
            Assert.Equal("s-local001", board.Panels[0].Settings.SourceId);
            Assert.Equal(ccgp.Id, board.Panels[1].Settings.SourceId);
            Assert.Equal(["s-local001", ccgp.Id], board.Panels[2].Settings.SourceIds);
            Assert.Equal(("s-local001", ccgp.Id), (board.Panels[3].Settings.SourceId, board.Panels[3].Settings.ToSourceId));
            var rivals = Assert.Single(writer.Boards!, b => b.Name == "Rivals");                   // the Add branch: only the file had it
            Assert.Equal(ccgp.Id, rivals.Panels[0].Settings.SourceId);
            Assert.Equal((false, null), (writer.Settings!.ResolveNames, writer.Settings.ActiveRecipe));   // ActiveRecipe is not written any more (A7)
            Assert.Equal(["SaveState", "SaveSources", "SaveImportedBoards", "SaveSettings"], writer.Calls);
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
            Assert.Equal(["s-local001", "s-file0002"], board.Panels[2].Settings.SourceIds);   // one mapped, one left stale
            Assert.Equal(("s-local001", "s-file0002"), (board.Panels[3].Settings.SourceId, board.Panels[3].Settings.ToSourceId));
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

    [Fact]
    public void ASettingsFailureNamesItsStepWithRecipesClansAndBoardsAlreadyStanding()
    {
        var (plan, writer, dir) = Scenario(throwAt: "settings");
        using (dir)
        {
            var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

            Assert.Equal(("settings", nameof(IOException)), (applied.FailedStep, applied.FailureType));
            Assert.Equal((1, 2, 2), (applied.Recipes, applied.Clans, applied.Boards));
            Assert.NotNull(writer.Sources);
            Assert.NotNull(writer.Boards);
            Assert.Null(writer.Settings);
        }
    }

    /// <summary>
    /// Spec §1/§7: the file carries one setting plus the mode switches, and <c>StartOnOpen</c> is this machine's own. The settings
    /// step must therefore write THIS PC's record with the file's laid over it — never the file's whole record,
    /// which <see cref="SetupPack.FromFolder"/> rebuilds with <c>StartOnOpen</c> at the record default, so every
    /// import would quietly turn "Start reading when Ur Score opens" back off (final review, 2026-09-22).
    /// </summary>
    [Fact]
    public void TheSettingsStepTakesTheFilesSettingAndKeepsThisPcsStartOnOpenAndActiveRecipe()
    {
        using var dir = TempDir.Create("urscore-apply-settings");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var here = new SetupHere([], [], [], [Main, AltOne], new Settings(ResolveNames: true, ActiveRecipe: "something-else", StartOnOpen: true));
        var file = new SetupPack([], [], [], new Settings(ResolveNames: false, ActiveRecipe: Clan.Slug), []);
        var plan = SetupMerge.Plan(file, here, 0, 0);
        var writer = new FakeSetupWriter(data, here);

        var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

        Assert.Null(applied.FailedStep);
        Assert.Equal((false, "something-else", true), (writer.Settings!.ResolveNames, writer.Settings.ActiveRecipe, writer.Settings.StartOnOpen));
    }

    /// <summary>
    /// Spec §4: the screen gets the failure's redacted MESSAGE, the trail gets its type. Redacted means the data
    /// folder is not in it — a line that says where a person's files live is a line that goes into a screenshot.
    /// </summary>
    [Fact]
    public void AFailuresMessageNamesTheFileAndNeverTheFolderItIsIn()
    {
        using var dir = TempDir.Create("urscore-apply-redact");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var here = new SetupHere([], [], [], [Main, AltOne], Settings.Defaults);
        var plan = SetupMerge.Plan(new SetupPack([], [], [], Settings.Defaults, []), here, 0, 0);
        var writer = new FakeSetupWriter(data, here)
        {
            ThrowAt = "settings",
            ThrowMessage = $"Access to the path '{Path.Combine(data, "settings.json")}' is denied.",
        };

        var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

        Assert.Equal(("settings", nameof(IOException)), (applied.FailedStep, applied.FailureType));
        Assert.Equal("Access to the path 'settings.json' is denied.", applied.FailureMessage);
        Assert.DoesNotContain(data, applied.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMainArrivingDemotesTheLocalMainOnThatRecipeToMine()
    {
        using var dir = TempDir.Create("urscore-apply-main");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var y = new Source("s-local-y", Clan.Slug, new Dictionary<string, string> { ["clan"] = "Y" }, SourceRole.Main);
        var here = new SetupHere([Seeded(Clan, ClanText)], [y], [], [Main, AltOne], Settings.Defaults);
        var fileX = new Source("s-filex001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "X" }, SourceRole.Main);
        var file = new SetupPack([FileRecipe(Clan)], [fileX], [], Settings.Defaults, []);
        var plan = SetupMerge.Plan(file, here, 0, 0);
        var writer = new FakeSetupWriter(data, here);

        var applied = SetupMerge.Apply(plan, new HashSet<string> { "clan:" + Clan.Slug + "|clan=x" }, writer, DateTimeOffset.UtcNow);

        Assert.Null(applied.FailedStep);
        var mains = writer.Sources!.Where(s => s.Recipe == Clan.Slug && s.Role == SourceRole.Main).ToList();
        var main = Assert.Single(mains);
        Assert.NotEqual("s-local-y", main.Id);
        Assert.NotEqual("s-filex001", main.Id);                                       // minted, never the file's
        var demoted = Assert.Single(writer.Sources!, s => s.Id == "s-local-y");
        Assert.Equal(SourceRole.Mine, demoted.Role);
    }

    [Fact]
    public void AClanGoneFromTheStoreSincePlanFailsTheClansStepByNameInsteadOfThrowingRaw()
    {
        var local = new Source("s-local001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Watch);
        var hereForPlan = new SetupHere([], [local], [], [Main, AltOne], Settings.Defaults);
        var fileMain = new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main);
        var file = new SetupPack([FileRecipe(Clan)], [fileMain], [], Settings.Defaults, []);
        var plan = SetupMerge.Plan(file, hereForPlan, 0, 0);

        using var dir = TempDir.Create("urscore-apply-gone");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var hereForWriter = new SetupHere([], [], [], [Main, AltOne], Settings.Defaults);   // the local clan vanished before Apply ran
        var writer = new FakeSetupWriter(data, hereForWriter);

        var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), writer, DateTimeOffset.UtcNow);

        Assert.Equal(("clans", nameof(InvalidOperationException)), (applied.FailedStep, applied.FailureType));
        Assert.Null(writer.Sources);
    }

    /// <summary>The mode switches apply through the settings step, over this PC's own record, only when their item is ticked.</summary>
    [Fact]
    public void TickedModeSwitchesApplyOverThisPcsOwnAndUntickedOnesDoNot()
    {
        using var dir = TempDir.Create("urscore-apply-modes");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var here = new SetupHere([], [], [], [Main, AltOne], new Settings(StartOnOpen: true, Modes: new Dictionary<string, bool> { ["pet-sim-99/profile"] = false }));
        var file = new SetupPack([], [], [], new Settings(Modes: new Dictionary<string, bool> { ["pet-sim-99/battle"] = false, ["pet-sim-99"] = true, ["not/known"] = true }), []);
        var plan = SetupMerge.Plan(file, here, 0, 0);

        var writer = new FakeSetupWriter(data, here);
        var applied = SetupMerge.Apply(plan, new HashSet<string> { "modes" }, writer, DateTimeOffset.UtcNow);
        var unticked = new FakeSetupWriter(data, here);
        var notApplied = SetupMerge.Apply(plan, new HashSet<string>(), unticked, DateTimeOffset.UtcNow);

        Assert.True(applied.ModesApplied);
        Assert.Equal(new Dictionary<string, bool> { ["pet-sim-99/profile"] = false, ["pet-sim-99/battle"] = false, ["pet-sim-99"] = true }, writer.Settings!.Modes);   // local kept, the file's known keys over it
        Assert.True(writer.Settings.StartOnOpen);
        Assert.False(notApplied.ModesApplied);
        Assert.Equal(here.Settings.Modes, unticked.Settings!.Modes);
    }

    [Fact]
    public void TwoAsideCopiesAtTheSameMinuteGetDistinctFoldersAndTheFirstKeepsItsFiles()
    {
        var (plan, writer, dir) = Scenario();
        using (dir)
        {
            var now = new DateTimeOffset(2026, 9, 22, 14, 31, 0, TimeSpan.Zero);
            var keys = plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            var first = SetupMerge.Apply(plan, keys, writer, now);
            var second = SetupMerge.Apply(plan, keys, writer, now);

            Assert.NotEqual(first.AsideFolder, second.AsideFolder);
            Assert.True(File.Exists(Path.Combine(first.AsideFolder!, "sources.json")));
            Assert.True(File.Exists(Path.Combine(first.AsideFolder!, "recipes", "old.recipe.json")));
            Assert.True(File.Exists(Path.Combine(second.AsideFolder!, "sources.json")));
        }
    }
}
