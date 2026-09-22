# Setup Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The whole setup — recipes with their ticks, clans, boards, two settings — travels in the stats file and is merged on the receiving PC behind a preview with ticks; keys never travel; sends arrive off.

**Architecture:** Plan, then apply. Pure records describe what travels (`SetupPack`) and what a merge would do (`SetupMerge.Plan` → `SetupMergePlan` of `SetupItem`s with identity, outcome and dependency). The preview window draws the plan with ticks. `SetupMerge.Apply` writes through the stores that exist, in dependency order, behind a dated aside copy; the stats merge then runs exactly as 0.5.5's does. Every rule is testable without a window; the composition test runs the whole path across two temp-folder "PCs".

**Tech Stack:** C# 13 / .NET 10, WPF, xUnit 2.9, `System.IO.Compression`. No new dependencies.

**Spec:** `docs/2026-09-22-setup-transfer-design.md` — read it first; §2 (identity, sends off, ids) and §4 (apply order, failure) are the rules every task argues from.

## Global Constraints

- Work on branch `feat/setup-transfer`, cut from master `80c0f6b` (v0.5.5). Never tag; releasing is the owner's call and the RoRoRo session is pinged first.
- Build: `dotnet build tests/Ur-Score.Tests.csproj`. Test: `dotnet test tests/Ur-Score.Tests.csproj`. Always `grep -E "Failed:|Passed:|Aborted|crashed"` AND check the exit code; compare the Total against the previous run plus what you added (it was **1,545** at the start of this plan). A run that prints "Passed!" with fewer tests than expected has crashed the host — see `docs/testing.md` §5.
- `CS5001 … no static 'Main'` on the FIRST build after a configuration switch is a known flake (V3-S.39): run the build again, once. If `BG1002 … .baml cannot be found` follows, delete `obj/Release` (or `obj/Debug`) and rebuild.
- Every test is watched failing before its fix, and a fixture's decoy must be distinguishable (`docs/testing.md` rules 1–3). Where a test cannot be red first (a new file that does not compile without the code), say so in its doc comment.
- Every test class that touches WPF (a control, a window, a XAML parse) carries `[Collection(WpfCollection.Name)]` and builds its UI through `UiThread.RunInApp`. A fence fails the build otherwise.
- **Never compose `AppServices` in a test with a null host or transport** — that builds the app for real against RoRoRo's pipe (V3-S.43). Use `StubHost` and the `FakeTransport` in `AppCompositionTests`.
- **Privacy:** no player id, name or value in any fixture beyond the ones `BoardFixtures` already carries (101/201/202/301, `BirchMain` etc.). Keys: a key VALUE never reaches a pack, a test fixture or a log line. `BoardsFileFenceTests` pins that the type `BoardsFile` is named only in `Board/BoardsFile.cs` and `Composition/AppServices.cs`; do not reference `BoardsFile` from Core.
- **Sends arrive off** (spec §2): every `StatChoice.Send` in an imported state is false, whatever the file says.
- Every popup themed (no stock `MessageBox`); windows follow `ConfirmWindow.xaml`'s shape: `Background="{DynamicResource BgBrush}"`, `ThemeService.Attach(this)`, named buttons with `AutomationProperties.Name`.
- Button text stays **Export stats…** / **Import stats…** (the owner's names).
- Commit after every task with the `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` trailer. Do not push.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Board/BoardJson.cs` (new, Task 1) | `Serialize`/`Parse` of `BoardDef` lists, moved out of `BoardsFile` so the pack can write boards without naming the boards-file writer. `BoardsFile` calls it. |
| `src/Core/Sources.cs` (Task 2) | `SourceStore.Serialize` / `SourceStore.Parse`, public static, the same options the store uses. |
| `src/Recipes/RecipeStore.cs` (Task 2) | `RecipeStore.SerializeState` / `RecipeStore.ParseState`, public static. |
| `src/Core/SetupPack.cs` (new, Task 3) | What travels: `SetupRecipe`, `SetupKey`, `SetupPack` with `FromHere`, `ToFolder`, `FromFolder`. Pure. |
| `src/Book/BookPack.cs` (Task 4) | Manifest `v: 2` with `Setup`; `Write` takes an optional `SetupPack`; `Open` returns it. |
| `src/Core/SetupMerge.cs` (new, Tasks 5–6) | `SetupItem`, `SetupKind`, `SetupOutcome`, `SetupMergePlan`, `SetupApplied`; `SetupMerge.Plan` (pure) and `SetupMerge.Apply` (through `ISetupServices`). |
| `src/Composition/ISetupServices.cs`, `AppServices.cs` (Task 7) | `Paths`, `SavedBoards`, `SaveImportedBoards`, `ExportStats` writing the setup. |
| `src/UI/Setup/ImportPreviewModel.cs` (new, Task 8) | Rows for the preview: text, tick default, greying. Pure. |
| `src/UI/Setup/ImportPreviewWindow.xaml(.cs)` (new, Task 8) | The preview, themed, `RowList`-based. |
| `src/UI/Setup/ScoreBookPage.xaml.cs`, `ScoreBookModel.cs` (Task 8) | Import opens the preview; the export line counts the setup. |
| `tools/smoke/walk-score-book.ps1` (Task 9) | Export with setup, import into a fresh folder through the preview, untick one clan. |
| `docs/backlog.md`, `CHANGELOG.md`, `tools/smoke/README.md` (Task 9) | V3-S.46 closed with evidence; an Unreleased entry; the walk's row. |

---

### Task 1: `BoardJson` — board serialization the pack can call without naming the boards file

**Files:**
- Create: `src/Board/BoardJson.cs`
- Modify: `src/Board/BoardsFile.cs` (lines 18–33 options and the `Serialize`/`Parse`/DTO members, ~lines 63–140)
- Test: `tests/BoardJsonTests.cs`

**Interfaces:**
- Produces: `public static class BoardJson { public static string Serialize(IReadOnlyList<BoardDef> boards); public static IReadOnlyList<BoardDef> Parse(string json); }` — byte-for-byte the output `BoardsFile.Serialize` gave before.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/BoardJsonTests.cs
using Labs626.UrScore.Board;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The boards' JSON on its own, so a pack can write boards without naming <c>BoardsFile</c>, which the
/// boards-file fence reserves for the one writer of boards.json. The shape is unchanged: what
/// <c>BoardsFile</c> wrote before, it writes now through this.
/// </summary>
public class BoardJsonTests
{
    [Fact]
    public void ABoardRoundTripsThroughItsJson()
    {
        var board = new BoardDef("b-1", "Battle", [
            new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-00000001")),
            new PanelDef("p-2", PanelType.AccountCard, new PanelSize(6, Tall: true), new PanelSettings(Clan.Slug, UserId: 101), new PopOutRect(10, 20, 300, 200)),
        ]);
        var following = new BoardDef("b-starter-alts", "Alts", [], Follows: "alts");

        var json = BoardJson.Serialize([board, following]);
        var back = BoardJson.Parse(json);

        Assert.Equal(2, back.Count);
        Assert.Equal(board, back[0]);
        Assert.Equal(following, back[1]);
        Assert.Contains("\"follows\"", json, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it to see it fail to compile**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BoardJsonTests" 2>&1 | grep -E "error|Failed:|Passed:"`
Expected: `error CS0103: The name 'BoardJson' does not exist`

- [ ] **Step 3: Move `Serialize`, `Parse`, the DTO records and the `JsonSerializerOptions` from `BoardsFile` into a new static class**

Read `src/Board/BoardsFile.cs` first. Every member that only serializes or parses (`Options`, `Serialize`, `Parse`, the `BoardDto`/`PanelDto`/`PopOutDto` records and any `ToDto`/`FromDto` helpers) moves verbatim into:

```csharp
// src/Board/BoardJson.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Board;

/// <summary>
/// The boards' JSON shape, on its own. <see cref="BoardsFile"/> writes and reads boards.json through this; a
/// stats file carries a setup's boards through this too, without naming the boards-file writer — the fence
/// around boards.json (<c>BoardsFileFenceTests</c>) reserves that name for the one place that writes the file.
/// The shape is unchanged from what BoardsFile wrote before 2026-09-22.
/// </summary>
public static class BoardJson
{
    // (the moved Options, records, Serialize and Parse, unchanged)
}
```

`BoardsFile.Save` then calls `BoardJson.Serialize(boards)` and `Load` calls `BoardJson.Parse(...)`. Keep `BoardsFile.Parse` and `BoardsFile.Serialize` only if something outside references them (grep `BoardsFile.Parse|BoardsFile.Serialize` across `src/` and `tests/`); if they are referenced, leave one-line forwarders.

- [ ] **Step 4: Run the new test, the boards tests and the fence**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BoardJsonTests|FullyQualifiedName~BoardsFileTests|FullyQualifiedName~BoardsFileFenceTests" 2>&1 | grep -E "Failed |error|Failed:|Passed:"`
Expected: all pass. If the fence complains that `BoardJson.cs` names `BoardsFile`, the doc comment above uses `<see cref="BoardsFile"/>` — the fence drops whole-line `///` comments, so that is fine; but no CODE line in `BoardJson.cs` may name it.

- [ ] **Step 5: Full suite and commit**

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,546 (1,545 + 1), exit 0.

```bash
git add src/Board/BoardJson.cs src/Board/BoardsFile.cs tests/BoardJsonTests.cs
git commit -m "refactor(board): BoardJson — the boards' JSON on its own, so a pack can write boards without naming the file's writer

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Public serializers for sources and recipe state

**Files:**
- Modify: `src/Core/Sources.cs` (`SourceStore`, ~lines 39–103)
- Modify: `src/Recipes/RecipeStore.cs` (~lines 90–100, 134–140)
- Test: `tests/SourcesTests.cs`, `tests/RecipeStoreTests.cs` (append)

**Interfaces:**
- Produces: `SourceStore.Serialize(IReadOnlyList<Source>) : string`, `SourceStore.Parse(string) : IReadOnlyList<Source>`, `RecipeStore.SerializeState(RecipeState) : string`, `RecipeStore.ParseState(string) : RecipeState` — all `public static`, using each store's existing `Options`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/SourcesTests.cs`:

```csharp
    /// <summary>The sources' JSON, callable without a file: what a stats file carries a setup's clans as.</summary>
    [Fact]
    public void SourcesRoundTripThroughTheirJson()
    {
        var sources = new List<Source>
        {
            new("s-0000000a", "pet-sim-99-clan-battle-points", new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main),
            new("s-0000000b", "pet-sim-99-top-clans", new Dictionary<string, string>(), SourceRole.Watch, Enabled: false),
        };

        var back = SourceStore.Parse(SourceStore.Serialize(sources));

        Assert.Equal(sources, back);
        Assert.Contains("\"role\": \"watch\"", SourceStore.Serialize(sources), StringComparison.Ordinal);
    }
```

Append to `tests/RecipeStoreTests.cs` (check its usings include `Labs626.UrScore.Recipes`):

```csharp
    /// <summary>A recipe's state as JSON, callable without a file: what a stats file carries a setup's ticks as.</summary>
    [Fact]
    public void AStateRoundTripsThroughItsJson()
    {
        var state = new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points") },
            CounterNames: ["Diamonds"],
            SentFieldMetrics: ["threat-gap"],
            ExcludedAccountIds: ["9ad5e605-6b41-478c-add3-b916a31a5ab2"]);

        var back = RecipeStore.ParseState(RecipeStore.SerializeState(state));

        Assert.Equal(state.StatChoices["value"], back.StatChoices["value"]);
        Assert.Equal(state.SavedCounterNames, back.SavedCounterNames);
        Assert.Equal(state.FieldMetricKeys, back.FieldMetricKeys);
        Assert.Equal(state.Excluded, back.Excluded);
    }
```

- [ ] **Step 2: Run them to see them fail to compile**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SourcesRoundTripThroughTheirJson|FullyQualifiedName~AStateRoundTripsThroughItsJson" 2>&1 | grep -E "error|Failed:|Passed:"`
Expected: `error CS0117: 'SourceStore' does not contain a definition for 'Serialize'` (and the same for `RecipeStore.SerializeState`).

- [ ] **Step 3: Add the four statics**

In `SourceStore` (after `DefaultPath`):

```csharp
    /// <summary>The file's JSON for a list of sources, and back. Public so a stats file can carry a setup's clans in the same shape.</summary>
    public static string Serialize(IReadOnlyList<Source> sources) => JsonSerializer.Serialize(sources, Options);

    public static IReadOnlyList<Source> Parse(string json) => JsonSerializer.Deserialize<List<Source>>(json, Options) ?? [];
```

Make `Save` use `Serialize(sources)` where it wrote `JsonSerializer.Serialize(sources, Options)`. In `RecipeStore` (after `Options`):

```csharp
    /// <summary>A state's JSON, and back, in the shape the state file uses. Public so a stats file can carry a setup's ticks.</summary>
    public static string SerializeState(RecipeState state) => JsonSerializer.Serialize(state, Options);

    public static RecipeState ParseState(string json) => JsonSerializer.Deserialize<RecipeState>(json, Options) ?? new RecipeState();
```

- [ ] **Step 4: Run the two tests, then the full suite**

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,548, exit 0.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Sources.cs src/Recipes/RecipeStore.cs tests/SourcesTests.cs tests/RecipeStoreTests.cs
git commit -m "feat(core): sources and recipe state serialize without a file, for the stats file to carry a setup

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: `SetupPack` — what travels, written to and read from a folder

**Files:**
- Create: `src/Core/SetupPack.cs`
- Test: `tests/SetupPackTests.cs`

**Interfaces:**
- Consumes: `BoardJson.Serialize/Parse` (Task 1), `SourceStore.Serialize/Parse`, `RecipeStore.SerializeState/ParseState` (Task 2), `Settings.Save(settings, path)` / `Settings.Load(path)`, `BoardDefs.Sanitize(boards, myUserIds)`, `LiveBoard.UserIdsOf(accounts)`, `Recipe.Keys : IReadOnlyList<RecipeKey>` (`RecipeKey(string Id, string Label, string GetOneAt, KeyPlacement In, string Name)`).
- Produces:

```csharp
public sealed record SetupRecipe(string Slug, string Name, string Text, RecipeState State, IReadOnlyList<long> ExcludedUserIds);
public sealed record SetupKey(string Id, string Label, string RecipeSlug, string RecipeName);
public sealed record SetupPack(
    IReadOnlyList<SetupRecipe> Recipes, IReadOnlyList<Source> Sources, IReadOnlyList<BoardDef> Boards,
    Settings Settings, IReadOnlyList<SetupKey> Keys)
{
    public const string Folder = "setup";
    public static SetupPack FromHere(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyList<BoardDef> savedBoards, Settings settings, IReadOnlyList<HostAccount> accounts);
    public void ToFolder(string root);            // writes <root>/setup/...
    public static SetupPack? FromFolder(string root); // null when <root>/setup does not exist
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SetupPackTests.cs
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
        Assert.Equal(pack.Sources, back.Sources);
        Assert.Equal(pack.Boards, back.Boards);
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
        var pack = SetupPack.FromHere([ClanInstalled(), ProfileInstalled()], [], [], Settings.Defaults, []);
        pack.ToFolder(dir.Path);

        // The profile recipe declares a key; the clan-battle one does not.
        var key = Assert.Single(pack.Keys);
        Assert.Equal((Profile.Keys[0].Id, Profile.Slug), (key.Id, key.RecipeSlug));
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
```

Check `Profile.Keys` is non-empty in the profile fixture (`grep -n '"keys"' tests/Fixtures/petsim99-profile.recipe.json`); if the fixture has no key, use whichever shipped fixture declares one (`grep -l '"keys"' tests/Fixtures/*.recipe.json`) and adjust `ProfileInstalled` to it.

- [ ] **Step 2: Run to see the compile failure**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupPackTests" 2>&1 | grep -E "error|Failed:|Passed:" | head -3`
Expected: `error CS0246: The type or namespace name 'SetupPack' could not be found`

- [ ] **Step 3: Write `SetupPack`**

```csharp
// src/Core/SetupPack.cs
using System.IO;
using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>One recipe as it travels: its text and state, with exclusions as Roblox user ids rather than RoRoRo's GUIDs.</summary>
public sealed record SetupRecipe(string Slug, string Name, string Text, RecipeState State, IReadOnlyList<long> ExcludedUserIds);

/// <summary>The NAME of a key a recipe wants — never its value — so the other PC's reminder is exact.</summary>
public sealed record SetupKey(string Id, string Label, string RecipeSlug, string RecipeName);

/// <summary>
/// What travels with the stats (design 2026-09-22, §1): recipes with their ticks, clans, boards, two settings and
/// the names of keys. Written into and read from a <c>setup</c> folder beside the <c>scorebook</c> one in the
/// stats file. Never <c>keys.dat</c>, never <c>accounts.json</c>, never <c>StartOnOpen</c> (a per-machine choice),
/// and never a RoRoRo account GUID: exclusions cross as Roblox user ids, which are the same everywhere.
/// </summary>
public sealed record SetupPack(
    IReadOnlyList<SetupRecipe> Recipes,
    IReadOnlyList<Source> Sources,
    IReadOnlyList<BoardDef> Boards,
    Settings Settings,
    IReadOnlyList<SetupKey> Keys)
{
    public const string Folder = "setup";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>This machine's setup, made ready to travel: boards sanitized to your own ids, exclusions as user ids, keys as names.</summary>
    public static SetupPack FromHere(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyList<BoardDef> savedBoards,
        Settings settings, IReadOnlyList<HostAccount> accounts)
    {
        var userIdOf = accounts.Where(a => a.RobloxUserId != 0).ToDictionary(a => a.AccountId, a => a.RobloxUserId);
        var recipes = installed.Select(i => new SetupRecipe(
            i.Recipe.Slug, i.Recipe.Name, i.Text,
            i.State with { ExcludedAccountIds = null },
            [.. i.State.Excluded.Where(userIdOf.ContainsKey).Select(id => userIdOf[id]).Order()])).ToList();
        var keys = installed
            .SelectMany(i => i.Recipe.Keys.Select(k => new SetupKey(k.Id, k.Label, i.Recipe.Slug, i.Recipe.Name)))
            .ToList();

        return new SetupPack(
            recipes,
            sources,
            BoardDefs.Sanitize(savedBoards, LiveBoard.UserIdsOf(accounts)),
            new Settings(ResolveNames: settings.ResolveNames, ActiveRecipe: settings.ActiveRecipe),
            keys);
    }

    public void ToFolder(string root)
    {
        var folder = Path.Combine(root, Folder);
        var recipes = Path.Combine(folder, "recipes");
        Directory.CreateDirectory(recipes);
        foreach (var recipe in Recipes)
        {
            File.WriteAllText(Path.Combine(recipes, recipe.Slug + ".recipe.json"), recipe.Text);
            File.WriteAllText(Path.Combine(recipes, recipe.Slug + ".state.json"), RecipeStore.SerializeState(recipe.State));
        }

        File.WriteAllText(Path.Combine(folder, "exclusions.json"),
            JsonSerializer.Serialize(Recipes.ToDictionary(r => r.Slug, r => r.ExcludedUserIds), Json));
        File.WriteAllText(Path.Combine(folder, "sources.json"), SourceStore.Serialize(Sources));
        File.WriteAllText(Path.Combine(folder, "boards.json"), BoardJson.Serialize(Boards));
        Settings.Save(Settings, Path.Combine(folder, "settings.json"));
        File.WriteAllText(Path.Combine(folder, "keys.json"), JsonSerializer.Serialize(Keys, Json));
    }

    /// <summary>The setup a folder holds, or null when it holds none (a 0.5.5 stats-only file).</summary>
    public static SetupPack? FromFolder(string root)
    {
        var folder = Path.Combine(root, Folder);
        if (!Directory.Exists(folder)) return null;

        var exclusions = ReadJson<Dictionary<string, List<long>>>(Path.Combine(folder, "exclusions.json")) ?? [];
        var recipes = new List<SetupRecipe>();
        var recipesFolder = Path.Combine(folder, "recipes");
        foreach (var file in Directory.Exists(recipesFolder) ? Directory.EnumerateFiles(recipesFolder, "*.recipe.json").Order(StringComparer.Ordinal) : [])
        {
            var slug = Path.GetFileName(file)[..^".recipe.json".Length];
            var text = File.ReadAllText(file);
            var parsed = RecipeParser.Parse(text);
            var stateFile = Path.Combine(recipesFolder, slug + ".state.json");
            var state = File.Exists(stateFile) ? RecipeStore.ParseState(File.ReadAllText(stateFile)) : new RecipeState();
            recipes.Add(new SetupRecipe(slug, parsed.Recipe?.Name ?? slug, text, state, exclusions.GetValueOrDefault(slug) ?? []));
        }

        return new SetupPack(
            recipes,
            File.Exists(Path.Combine(folder, "sources.json")) ? SourceStore.Parse(File.ReadAllText(Path.Combine(folder, "sources.json"))) : [],
            File.Exists(Path.Combine(folder, "boards.json")) ? BoardJson.Parse(File.ReadAllText(Path.Combine(folder, "boards.json"))) : [],
            Settings.Load(Path.Combine(folder, "settings.json")),
            ReadJson<List<SetupKey>>(Path.Combine(folder, "keys.json")) ?? []);
    }

    private static T? ReadJson<T>(string file) where T : class =>
        File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : null;
}
```

Check `Settings.Load(path)` returns `Settings.Defaults` for a missing file (read `src/Core/Settings.cs:33-50`); it does.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupPackTests" 2>&1 | grep -E "Failed |error|Failed:|Passed:|Expected|Actual"`
Expected: 5 passed.

- [ ] **Step 5: Mutation check, then full suite and commit**

Temporarily change `i.State with { ExcludedAccountIds = null }` to `i.State` and run `SetupPackTests`: `ExclusionsTravelAsRobloxUserIds…` must fail. Restore. Then:

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,553, exit 0.

```bash
git add src/Core/SetupPack.cs tests/SetupPackTests.cs
git commit -m "feat(core): SetupPack — what travels: recipes with ticks, clans, boards, two settings, key names

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: The stats file carries the setup (manifest v2)

**Files:**
- Modify: `src/Book/BookPack.cs` (whole file; ~130 lines)
- Test: `tests/BookPackTests.cs` (append; adjust the two literal manifests to `"v":2` where they must open)

**Interfaces:**
- Consumes: `SetupPack.ToFolder(root)`, `SetupPack.FromFolder(root)` (Task 3).
- Produces: `BookPackManifest(int V, DateTimeOffset TakenAt, string App, int Readings, int Finals, bool Setup)`; `BookPack.Version == 2`; `BookPack.Write(string bookRoot, string path, string appVersion, DateTimeOffset now, SetupPack? setup = null)`; `BookPackOpened(string? Folder, BookPackManifest? Manifest, SetupPack? Setup, string Problem = "")`.

- [ ] **Step 1: Write the failing tests** (append to `BookPackTests`)

```csharp
    /// <summary>A file with a setup in it carries it back out; the manifest says so; a v:1 file (0.5.5) opens as stats only.</summary>
    [Fact]
    public void ASetupTravelsWithTheStatsAndAStatsOnlyFileStillOpens()
    {
        using var dir = TempDir.Create("urscore-pack");
        var book = Path.Combine(dir.Path, "scorebook");
        BookGenerator.Write(book, clanSources: 1, days: 1);
        var setup = new SetupPack([], [BoardFixtures.MainClan], [], Labs626.UrScore.Core.Settings.Defaults, []);
        var withSetup = Path.Combine(dir.Path, "with.zip");
        var without = Path.Combine(dir.Path, "without.zip");

        var manifest = BookPack.Write(book, withSetup, "0.5.6", Now, setup);
        var plain = BookPack.Write(book, without, "0.5.6", Now);
        var opened = BookPack.Open(withSetup);
        var openedPlain = BookPack.Open(without);

        Assert.Equal((2, true), (manifest.V, manifest.Setup));
        Assert.False(plain.Setup);
        Assert.NotNull(opened.Setup);
        Assert.Equal(BoardFixtures.MainClan, Assert.Single(opened.Setup!.Sources));
        Assert.Null(openedPlain.Setup);
        BookPack.Discard(opened);
        BookPack.Discard(openedPlain);
    }

    [Fact]
    public void AFileFromTheVersionBeforeOpensAsStatsOnly()
    {
        using var dir = TempDir.Create("urscore-pack");
        var file = Path.Combine(dir.Path, "old.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            WriteEntry(zip, BookPack.ManifestName, """{"v":1,"takenAt":"2026-09-22T12:00:00+00:00","app":"0.5.5","readings":0,"finals":0}""");
        }

        var opened = BookPack.Open(file);

        Assert.Equal("", opened.Problem);
        Assert.NotNull(opened.Folder);
        Assert.Null(opened.Setup);
        Assert.False(opened.Manifest!.Setup);
        BookPack.Discard(opened);
    }
```

In the existing `AFileWithNoManifestOrFromANewerUrScoreIsRefusedInPlainWords`, change the "newer" manifest to `"v":3` (2 is now this version). In `AnEntryThatEscapesTheFolderIsRefused` the `"v":1` manifest may stay.

- [ ] **Step 2: Run to see the failures**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~BookPackTests" 2>&1 | grep -E "error|Failed |Failed:|Passed:" | head -5`
Expected: compile errors on `manifest.Setup`, `opened.Setup`, and the fifth `Write` argument.

- [ ] **Step 3: Change `BookPack`**

- `BookPackManifest` gains `bool Setup` as its last positional parameter (`= false` default is not allowed on a positional record parameter that others follow — put it last with a default: `bool Setup = false`).
- `Version` becomes `2`.
- `BookPackOpened` gains `SetupPack? Setup` after `Manifest`.
- `Write(string bookRoot, string path, string appVersion, DateTimeOffset now, SetupPack? setup = null)`: the manifest is `new BookPackManifest(Version, now, appVersion, readings, finals, setup is not null)`; after the book entries, when `setup` is not null: write it with `setup.ToFolder(stagingFolder)` into a temp folder `Path.Combine(Path.GetTempPath(), "626labs.ur-score", "export-" + Guid.NewGuid().ToString("N"))`, then add every file under `<staging>/setup` to the zip as `setup/<relative>` with `zip.CreateEntryFromFile`, then delete the staging folder in a `finally`.
- `Open`: after the manifest is accepted, `var setup = manifest.V >= 2 && manifest.Setup ? SetupPack.FromFolder(folder) : null;` and return `new BookPackOpened(folder, manifest, setup)`. A `v: 1` manifest deserializes with `Setup == false` (missing property → default).
- Update the class doc: "since 2026-09-22 it also carries the setup (`SetupPack`) under `setup/`; `v: 2`. A `v: 1` file is 0.5.5's stats-only file and opens as such."
- `Refuse` returns `new BookPackOpened(null, null, null, problem)`.

Grep for other callers of `BookPackOpened`/`Write` (`grep -rn "BookPack\." src/ tests/`) — `AppServices.ExportStats` and `BookImport.RunFile` compile unchanged because the new parameters default.

- [ ] **Step 4: Run the pack tests, the composition tests, then the full suite**

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,555, exit 0.

- [ ] **Step 5: Commit**

```bash
git add src/Book/BookPack.cs tests/BookPackTests.cs
git commit -m "feat(book): the stats file carries the setup (manifest v2); a 0.5.5 file still opens as stats only

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: `SetupMerge.Plan` — the rules, pure

**Files:**
- Create: `src/Core/SetupMerge.cs` (records and `Plan`; `Apply` comes in Task 6)
- Test: `tests/SetupMergeTests.cs`

**Interfaces:**
- Consumes: `SetupPack` (Task 3), `Source.KeyOf(inputs)`, `InstalledRecipe`, `RecipeParser.Parse`.
- Produces:

```csharp
public enum SetupKind { Recipe, Clan, Board, Key, Stats }
public enum SetupOutcome { Add, Update, Same, Replace, Kept, EnterAgain }
public sealed record SetupItem(
    SetupKind Kind, string Key, string Name, SetupOutcome Outcome, string Note,
    bool Ticked, string? DependsOnRecipe = null, string? FileId = null, string? LocalId = null);
public sealed record SetupHere(IReadOnlyList<InstalledRecipe> Installed, IReadOnlyList<Source> Sources, IReadOnlyList<BoardDef> SavedBoards, IReadOnlyList<HostAccount> Accounts);
public sealed record SetupMergePlan(IReadOnlyList<SetupItem> Items, SetupPack File)
{
    public IEnumerable<SetupItem> Ticked(IReadOnlySet<string> tickedKeys);   // items whose Key is ticked AND whose dependency holds
    public bool CanTick(SetupItem item, IReadOnlySet<string> tickedKeys);    // false for a clan whose recipe is neither ticked nor installed
}
public static class SetupMerge
{
    public static SetupMergePlan Plan(SetupPack file, SetupHere here, int readings, int finals);
    public static RecipeState Arriving(RecipeState fileState, IReadOnlyList<long> excludedUserIds, IReadOnlyList<HostAccount> accounts, out int droppedExclusions); // sends off, exclusions mapped
}
```

`Key` is `"recipe:<slug>"`, `"clan:<slug>|<KeyOf(inputs)>"`, `"board:<name lower>"`, `"key:<slug>|<keyId>"`, `"stats"`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/SetupMergeTests.cs
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
        var file = File(
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
}
```

- [ ] **Step 2: Run to see the compile failure**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupMergeTests" 2>&1 | grep -E "error" | head -3`
Expected: `SetupMerge`, `SetupItem` … not found.

- [ ] **Step 3: Write the records and `Plan`**

```csharp
// src/Core/SetupMerge.cs
using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

public enum SetupKind { Recipe, Clan, Board, Key, Stats }

public enum SetupOutcome { Add, Update, Same, Replace, Kept, EnterAgain }

/// <summary>
/// One line of the preview: what kind of thing, its name, what importing would do to it and why, whether it starts
/// ticked, and — for a clan — the recipe it needs. <see cref="FileId"/> and <see cref="LocalId"/> are a clan's ids on
/// each side; an Add has no local id yet, and Apply mints one.
/// </summary>
public sealed record SetupItem(
    SetupKind Kind, string Key, string Name, SetupOutcome Outcome, string Note, bool Ticked,
    string? DependsOnRecipe = null, string? FileId = null, string? LocalId = null);

/// <summary>This machine's setup, as the plan needs it.</summary>
public sealed record SetupHere(
    IReadOnlyList<InstalledRecipe> Installed, IReadOnlyList<Source> Sources, IReadOnlyList<BoardDef> SavedBoards, IReadOnlyList<HostAccount> Accounts);

public sealed record SetupMergePlan(IReadOnlyList<SetupItem> Items, SetupPack File)
{
    /// <summary>A clan can be ticked only when its recipe is ticked or already installed (spec §2, the one dependency).</summary>
    public bool CanTick(SetupItem item, IReadOnlySet<string> tickedKeys) =>
        item.DependsOnRecipe is not { } slug || tickedKeys.Contains("recipe:" + slug) || RecipeInstalled.Contains(slug);

    /// <summary>The items that will be applied: ticked, tickable, and with something to do.</summary>
    public IEnumerable<SetupItem> Ticked(IReadOnlySet<string> tickedKeys) =>
        Items.Where(i => i.Outcome is SetupOutcome.Add or SetupOutcome.Update or SetupOutcome.Replace && tickedKeys.Contains(i.Key) && CanTick(i, tickedKeys));

    /// <summary>Set by <see cref="SetupMerge.Plan"/>: the slugs installed here, for <see cref="CanTick"/>.</summary>
    internal IReadOnlySet<string> RecipeInstalled { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Importing a setup: the plan, pure (spec §2), and the apply (spec §4, Task 6). Identity: a recipe by slug, a clan by
/// recipe and clan name (case and spaces aside, as the book import matches), a board by name. The file wins where
/// both have a thing and it differs; a clan keeps THIS machine's id; sends arrive off.
/// </summary>
public static class SetupMerge
{
    public static SetupMergePlan Plan(SetupPack file, SetupHere here, int readings, int finals)
    {
        var items = new List<SetupItem>();
        var installedHere = here.Installed.ToDictionary(i => i.Recipe.Slug, StringComparer.Ordinal);

        foreach (var recipe in file.Recipes)
        {
            var key = "recipe:" + recipe.Slug;
            if (installedHere.TryGetValue(recipe.Slug, out var local))
            {
                var differs = !string.Equals(local.Text, recipe.Text, StringComparison.Ordinal);
                items.Add(differs
                    ? new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Update, "the file's copy differs; its ticks come with it, sends off", Ticked: true)
                    : new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Same, "same as here", Ticked: false));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Recipe, key, recipe.Name, SetupOutcome.Add, "sends off until you tick them", Ticked: true));
            }
        }

        var fileNames = file.Sources.Select(s => s.Recipe + "|" + s.InputsKey).ToHashSet(StringComparer.Ordinal);
        foreach (var source in file.Sources)
        {
            var identity = source.Recipe + "|" + source.InputsKey;
            var name = ClanName(source);
            var local = here.Sources.FirstOrDefault(s => string.Equals(s.Recipe + "|" + s.InputsKey, identity, StringComparison.Ordinal));
            if (local is null)
            {
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Add, RoleWord(source), Ticked: true, DependsOnRecipe: source.Recipe, FileId: source.Id));
            }
            else if (local.Role != source.Role || local.Enabled != source.Enabled)
            {
                var note = local.Role != source.Role
                    ? $"here it is {RoleWord(local)}; the file says {RoleWord(source)}"
                    : source.Enabled ? "here it is switched off; the file has it on" : "here it is on; the file has it switched off";
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Replace, note, Ticked: true, DependsOnRecipe: source.Recipe, FileId: source.Id, LocalId: local.Id));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Clan, "clan:" + identity, name, SetupOutcome.Same, "same as here", Ticked: false, DependsOnRecipe: source.Recipe, FileId: source.Id, LocalId: local.Id));
            }
        }

        foreach (var local in here.Sources.Where(s => !fileNames.Contains(s.Recipe + "|" + s.InputsKey)))
        {
            items.Add(new SetupItem(SetupKind.Clan, "clan:" + local.Recipe + "|" + local.InputsKey, ClanName(local), SetupOutcome.Kept, "only this PC has it", Ticked: false, LocalId: local.Id));
        }

        static string BoardKey(BoardDef b) => "board:" + b.Name.Trim().ToLowerInvariant();
        var hereBoards = here.SavedBoards.Where(b => b.Follows is null).ToDictionary(BoardKey, StringComparer.Ordinal);
        foreach (var board in file.Boards.Where(b => b.Follows is null))
        {
            if (hereBoards.TryGetValue(BoardKey(board), out var local))
            {
                items.Add(new SetupItem(SetupKind.Board, BoardKey(board), board.Name, SetupOutcome.Replace,
                    $"{Panels(local.Panels.Count)} here, {board.Panels.Count} in the file", Ticked: true));
            }
            else
            {
                items.Add(new SetupItem(SetupKind.Board, BoardKey(board), board.Name, SetupOutcome.Add, $"{Panels(board.Panels.Count)}", Ticked: true));
            }
        }

        foreach (var key in file.Keys)
        {
            items.Add(new SetupItem(SetupKind.Key, $"key:{key.RecipeSlug}|{key.Id}", key.Label, SetupOutcome.EnterAgain,
                $"{key.RecipeName}. Keys never leave the PC that saved them; Setup › Recipes asks for it.", Ticked: false));
        }

        items.Add(new SetupItem(SetupKind.Stats, "stats", StatsName(readings, finals), SetupOutcome.Add, "what is already here is skipped", Ticked: true));

        return new SetupMergePlan(items, file) { RecipeInstalled = installedHere.Keys.ToHashSet(StringComparer.Ordinal) };
    }

    /// <summary>The state as it arrives (spec §2): every send off, exclusions mapped to this PC's accounts by Roblox id, the unmatched counted.</summary>
    public static RecipeState Arriving(RecipeState fileState, IReadOnlyList<long> excludedUserIds, IReadOnlyList<HostAccount> accounts, out int droppedExclusions)
    {
        var byUserId = accounts.Where(a => a.RobloxUserId != 0).ToDictionary(a => a.RobloxUserId, a => a.AccountId);
        var excluded = excludedUserIds.Where(byUserId.ContainsKey).Select(id => byUserId[id].ToString()).ToList();
        droppedExclusions = excludedUserIds.Count - excluded.Count;
        return fileState with
        {
            Stats = fileState.Stats?.ToDictionary(kv => kv.Key, kv => kv.Value with { Send = false }, StringComparer.Ordinal),
            ExcludedAccountIds = excluded.Count == 0 ? null : excluded,
        };
    }

    public static string StatsName(int readings, int finals) =>
        $"{(readings == 1 ? "1 reading" : readings.ToString("N0", CultureInfo.InvariantCulture) + " readings")} and "
        + (finals == 1 ? "1 finished battle" : finals.ToString("N0", CultureInfo.InvariantCulture) + " finished battles");

    private static string ClanName(Source source) => source.Inputs.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? source.Recipe;

    private static string RoleWord(Source source) => source.Role switch { SourceRole.Main => "main", SourceRole.Mine => "yours", _ => "watched" };

    private static string Panels(int count) => count == 1 ? "1 panel" : $"{count} panels";
}
```

Adjust the tests' expected note strings to what the code says if they differ in a word, but keep the CONTENT: a Replace names both sides.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupMergeTests" 2>&1 | grep -E "Failed |error|Failed:|Passed:|Expected|Actual"`
Expected: 7 passed.

- [ ] **Step 5: Mutations, full suite, commit**

Three mutations, each run against `SetupMergeTests` and restored: (a) `kv.Value with { Send = false }` → `kv.Value` — `AStateArrivesWithEverySendOff…` fails; (b) `!string.Equals(local.Text, recipe.Text` → `false` — `ARecipeIsAddedUpdatedOrSame…` fails; (c) in `CanTick`, drop `|| RecipeInstalled.Contains(slug)` — `AClanCannotBeTickedWithoutItsRecipe` fails.

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,562, exit 0.

```bash
git add src/Core/SetupMerge.cs tests/SetupMergeTests.cs
git commit -m "feat(core): SetupMerge.Plan — what importing a setup would do, item by item; sends arrive off

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: `SetupMerge.Apply` — through the stores, behind an aside copy, a failure named

**Files:**
- Modify: `src/Core/SetupMerge.cs` (add `SetupApplied`, `ISetupWriter`, `Apply`)
- Test: `tests/SetupMergeTests.cs` (append) — with a `FakeSetupWriter`

**Interfaces:**
- Produces:

```csharp
/// <summary>The stores Apply writes through. AppServices implements it (Task 7); a test fakes it.</summary>
public interface ISetupWriter
{
    string DataRoot { get; }
    SetupHere Here { get; }
    void SaveRecipe(Recipe recipe, string text, RecipeState state);
    void SaveSources(IReadOnlyList<Source> sources);
    void SaveImportedBoards(IReadOnlyList<BoardDef> saved);
    void SaveSettings(Settings settings);
    void ReloadRecipes();
}
public sealed record SetupApplied(int Recipes, int Clans, int Boards, int KeptClans, int Keys, int DroppedExclusions, string? AsideFolder, string? FailedStep, string? FailureType);
public static SetupApplied Apply(SetupMergePlan plan, IReadOnlySet<string> tickedKeys, ISetupWriter writer, DateTimeOffset now);
```

`Apply` runs on the UI thread (kilobytes; the writer's members touch `AppServices` state). Order: aside → recipes → clans → `ReloadRecipes` → boards → settings. A throw in a step is caught, its step name and exception TYPE recorded, and the steps before stand.

- [ ] **Step 1: Write the failing tests** (append to `SetupMergeTests`)

```csharp
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
```

- [ ] **Step 2: Run to see the compile failure**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupMergeTests" 2>&1 | grep -E "error" | head -3`
Expected: `ISetupWriter`, `SetupApplied`, `Apply` not found.

- [ ] **Step 3: Write `ISetupWriter`, `SetupApplied` and `Apply`** (append to `SetupMerge.cs`, inside the namespace; `Apply` inside `SetupMerge`)

```csharp
/// <summary>The stores <see cref="SetupMerge.Apply"/> writes through, so the apply is testable against a fake and the app's own writers stay the only writers.</summary>
public interface ISetupWriter
{
    /// <summary>The data folder, for the aside copy beside it.</summary>
    string DataRoot { get; }

    SetupHere Here { get; }

    void SaveRecipe(Recipe recipe, string text, RecipeState state);

    void SaveSources(IReadOnlyList<Source> sources);

    /// <summary>The whole saved boards list, replaced: sanitized and written once, then redrawn.</summary>
    void SaveImportedBoards(IReadOnlyList<BoardDef> saved);

    void SaveSettings(Settings settings);

    void ReloadRecipes();
}

/// <summary>What Apply did, for the line the page says. <see cref="FailedStep"/> names the step that threw, or null.</summary>
public sealed record SetupApplied(
    int Recipes, int Clans, int Boards, int KeptClans, int Keys, int DroppedExclusions, string? AsideFolder, string? FailedStep, string? FailureType);
```

```csharp
    /// <summary>
    /// Applies the ticked items in dependency order (spec §4): the aside copy first, then recipes, clans, a reload,
    /// boards, settings. Each step is atomic on its own file; a step that throws ends the apply with its name and
    /// the exception's TYPE, and the steps before it stand. No roll-back: the aside folder is the recovery.
    /// </summary>
    public static SetupApplied Apply(SetupMergePlan plan, IReadOnlySet<string> tickedKeys, ISetupWriter writer, DateTimeOffset now)
    {
        var here = writer.Here;
        var ticked = plan.Ticked(tickedKeys).ToList();
        var recipes = 0;
        var clans = 0;
        var boards = 0;
        var dropped = 0;
        string? aside = null;
        var step = "aside";
        try
        {
            aside = Aside(writer.DataRoot, now);

            step = "recipes";
            var fileRecipes = plan.File.Recipes.ToDictionary(r => r.Slug, StringComparer.Ordinal);
            foreach (var item in ticked.Where(i => i.Kind == SetupKind.Recipe))
            {
                var recipe = fileRecipes[item.Key["recipe:".Length..]];
                var parsed = RecipeParser.Parse(recipe.Text);
                if (parsed.Recipe is null) continue;   // parsed there, not here: a version gap, counted by not counting it
                var state = Arriving(recipe.State, recipe.ExcludedUserIds, here.Accounts, out var droppedHere);
                dropped += droppedHere;
                writer.SaveRecipe(parsed.Recipe, recipe.Text, state);
                recipes++;
            }

            step = "clans";
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var sources = here.Sources.ToList();
            foreach (var item in plan.Items.Where(i => i.Kind == SetupKind.Clan && i.FileId is not null && i.LocalId is not null))
            {
                idMap[item.FileId!] = item.LocalId!;   // Same and Replace: the file's id means the local clan
            }

            foreach (var item in ticked.Where(i => i.Kind == SetupKind.Clan))
            {
                var fromFile = plan.File.Sources.First(s => s.Id == item.FileId);
                if (item.Outcome == SetupOutcome.Replace)
                {
                    var at = sources.FindIndex(s => s.Id == item.LocalId);
                    sources[at] = fromFile with { Id = item.LocalId! };
                }
                else
                {
                    var minted = SourceRules.NewId();
                    idMap[fromFile.Id] = minted;
                    sources.Add(fromFile with { Id = minted });
                }

                clans++;
            }

            if (clans > 0) writer.SaveSources(sources);
            writer.ReloadRecipes();

            step = "boards";
            var tickedBoards = ticked.Where(i => i.Kind == SetupKind.Board).Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
            if (tickedBoards.Count > 0)
            {
                static string BoardKey(BoardDef b) => "board:" + b.Name.Trim().ToLowerInvariant();
                var saved = here.SavedBoards.ToList();
                foreach (var board in plan.File.Boards.Where(b => b.Follows is null && tickedBoards.Contains(BoardKey(b))))
                {
                    var rewritten = Rewrite(board, idMap);
                    var at = saved.FindIndex(b => b.Follows is null && BoardKey(b) == BoardKey(board));
                    if (at >= 0) saved[at] = rewritten with { Id = saved[at].Id };
                    else saved.Add(rewritten);
                    boards++;
                }

                writer.SaveImportedBoards(saved);
            }

            step = "settings";
            writer.SaveSettings(plan.File.Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new SetupApplied(recipes, clans, boards, KeptClans(plan), plan.File.Keys.Count, dropped, aside, step, ex.GetType().Name);
        }

        return new SetupApplied(recipes, clans, boards, KeptClans(plan), plan.File.Keys.Count, dropped, aside, null, null);
    }

    private static int KeptClans(SetupMergePlan plan) => plan.Items.Count(i => i.Kind == SetupKind.Clan && i.Outcome == SetupOutcome.Kept);

    /// <summary>A board from the file with every clan id it points at mapped to this machine's; an unmapped id is left as it is, which points at nothing here.</summary>
    private static BoardDef Rewrite(BoardDef board, IReadOnlyDictionary<string, string> idMap) =>
        board with
        {
            Panels = [.. board.Panels.Select(p => p with
            {
                Settings = p.Settings with
                {
                    SourceId = p.Settings.SourceId is { } one ? idMap.GetValueOrDefault(one, one) : null,
                    SourceIds = p.Settings.SourceIds?.Select(id => idMap.GetValueOrDefault(id, id)).ToList(),
                    ToSourceId = p.Settings.ToSourceId is { } to ? idMap.GetValueOrDefault(to, to) : null,
                },
            })],
        };

    /// <summary>The setup's files copied beside the data folder, dated, so a person can put them back by hand.</summary>
    private static string Aside(string dataRoot, DateTimeOffset now)
    {
        var folder = $"{dataRoot.TrimEnd(Path.DirectorySeparatorChar)}.before-import-{now:yyyyMMdd-HHmm}";
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "sources.json", "boards.json", "settings.json" })
        {
            var file = Path.Combine(dataRoot, name);
            if (File.Exists(file)) File.Copy(file, Path.Combine(folder, name), overwrite: true);
        }

        var recipes = Path.Combine(dataRoot, "recipes");
        if (Directory.Exists(recipes))
        {
            Directory.CreateDirectory(Path.Combine(folder, "recipes"));
            foreach (var file in Directory.EnumerateFiles(recipes)) File.Copy(file, Path.Combine(folder, "recipes", Path.GetFileName(file)), overwrite: true);
        }

        return folder;
    }
```

Check `PanelSettings` has `ToSourceId` (it does: `src/Board/PanelModels.cs:11-17`). Add `using System.IO;` at the top of the file.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~SetupMergeTests" 2>&1 | grep -E "Failed |error|Failed:|Passed:|Expected|Actual"`
Expected: 10 passed.

- [ ] **Step 5: Mutation, full suite, commit**

Mutation: in the Replace branch write `fromFile` instead of `fromFile with { Id = item.LocalId! }` — `ApplyWritesInOrder…` fails on the K0i2 id. Restore.

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,565, exit 0.

```bash
git add src/Core/SetupMerge.cs tests/SetupMergeTests.cs
git commit -m "feat(core): SetupMerge.Apply — through the stores, in order, behind an aside copy; a failure named, the steps before it standing

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: The composition root writes the setup out and takes it in

**Files:**
- Modify: `src/Composition/ISetupServices.cs`, `src/Composition/AppServices.cs`
- Modify: `src/UI/Setup/ScoreBookModel.cs` (`ExportedLine`), `tests/ScoreBookModelTests.cs`, `tests/ImportFlowTests.cs` (the stub gains members)
- Test: `tests/AppCompositionTests.cs` (append)

**Interfaces:**
- Consumes: `SetupPack.FromHere`, `BookPack.Write(..., setup)`, `ISetupWriter`, `SetupMerge`.
- Produces on `ISetupServices`: `AppPaths Paths { get; }`, `IReadOnlyList<BoardDef> SavedBoards { get; }`, `ISetupWriter SetupWriter { get; }`; `ExportStats` now writes the setup; `ScoreBookModel.ExportedLine(manifest, fileName, SetupPack? setup)`.

- [ ] **Step 1: Write the failing tests**

Append to `AppCompositionTests`:

```csharp
    /// <summary>
    /// The whole setup, PC to PC, through the real composition on both sides (spec §6). A has two recipes, three clans
    /// and an edited board; B is pristine. After the import with everything ticked, B's clans have B's own ids, B's
    /// board points at them, every send is off, and the stats landed. Then into a B that already WATCHES one of
    /// the clans: the plan says Replace, and afterward B has one such clan, not two.
    /// </summary>
    [Fact]
    public async Task TheWholeSetupTravelsAndArrivesUnderTheOtherPcsOwnIds()
    {
        using var a = TempDir.Create("urscore-app-a");
        using var b = TempDir.Create("urscore-app-b");
        var clanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var clan = RecipeParser.Parse(clanText).Recipe!;
        var topText = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var top = RecipeParser.Parse(topText).Recipe!;
        var pathsA = new AppPaths(a.Path);
        new RecipeStore(pathsA.Recipes).Save(clan, clanText, new RecipeState(Stats: new Dictionary<string, StatChoice> { ["value"] = new(Show: true, Send: true, MetricId: "clan.battle.points") }));
        new RecipeStore(pathsA.Recipes).Save(top, topText, new RecipeState());
        var sourcesA = new List<Source>
        {
            new("s-000000a1", clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Main),
            new("s-000000a2", clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Mine),
            new("s-000000a3", top.Slug, new Dictionary<string, string>(), SourceRole.Watch),
        };
        new SourceStore(pathsA.Sources).Save(sourcesA);
        var file = Path.Combine(a.Path, "everything.zip");
        using (var exporter = Compose(a, new StubHost(reachable: false), new FakeTransport()))
        {
            exporter.SaveImportedBoards([new BoardDef("b-a1", "Rivals", [new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(clan.Slug, SourceId: "s-000000a1"))])]);
            var manifest = exporter.ExportStats(file);
            Assert.True(manifest.Setup);
        }

        using var importer = Compose(b, new StubHost(reachable: false), new FakeTransport());
        await importer.LoadBookAsync();
        var opened = BookPack.Open(file);
        Assert.NotNull(opened.Setup);
        var plan = SetupMerge.Plan(opened.Setup!, importer.SetupWriter.Here, opened.Manifest!.Readings, opened.Manifest.Finals);
        var applied = SetupMerge.Apply(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), importer.SetupWriter, Start);
        BookPack.Discard(opened);

        Assert.Null(applied.FailedStep);
        Assert.Equal((2, 3, 1), (applied.Recipes, applied.Clans, applied.Boards));
        Assert.Equal(2, importer.Installed.Count);
        Assert.All(importer.Installed.SelectMany(i => i.State.StatChoices.Values), choice => Assert.False(choice.Send));
        Assert.Equal(3, importer.Sources.Count);
        Assert.All(importer.Sources, s => Assert.DoesNotContain(s.Id, sourcesA.Select(x => x.Id)));
        var k0i2 = Assert.Single(importer.Sources, s => s.InputsKey == "clan=k0i2");
        Assert.NotNull(importer.Runner.WatchFor(k0i2.Id));
        var rivals = Assert.Single(importer.SavedBoards, bd => bd.Name == "Rivals");
        Assert.Equal(k0i2.Id, rivals.Panels[0].Settings.SourceId);
        Assert.True(Directory.GetDirectories(b.Path, "626labs.ur-score.before-import-*").Length == 1 || Directory.GetDirectories(Path.GetDirectoryName(b.Path)!, Path.GetFileName(b.Path) + ".before-import-*").Length == 1);

        // A B that already watches K0i2: Replace, not a second clan.
        using var c = TempDir.Create("urscore-app-c");
        var pathsC = new AppPaths(c.Path);
        new RecipeStore(pathsC.Recipes).Save(clan, clanText, new RecipeState());
        new SourceStore(pathsC.Sources).Save([new Source("s-000000c1", clan.Slug, new Dictionary<string, string> { ["clan"] = "k0i2" }, SourceRole.Watch)]);
        using var watcher = Compose(c, new StubHost(reachable: false), new FakeTransport());
        await watcher.LoadBookAsync();
        var openedAgain = BookPack.Open(file);
        var planC = SetupMerge.Plan(openedAgain.Setup!, watcher.SetupWriter.Here, 0, 0);
        Assert.Equal(SetupOutcome.Replace, Assert.Single(planC.Items, i => i.Kind == SetupKind.Clan && i.Name == "K0i2").Outcome);
        SetupMerge.Apply(planC, planC.Items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal), watcher.SetupWriter, Start);
        BookPack.Discard(openedAgain);

        var k0i2C = Assert.Single(watcher.Sources, s => s.InputsKey == "clan=k0i2");
        Assert.Equal(("s-000000c1", SourceRole.Main), (k0i2C.Id, k0i2C.Role));
    }
```

Note the aside folder is created BESIDE the data root (`<root>.before-import-…`); with `AppPaths(dir.Path)` the root is the temp dir itself, so the aside lands beside it under `%TEMP%` — the test's second `Directory.GetDirectories` branch covers that, and the test must delete it: add at the end `foreach (var aside in Directory.GetDirectories(Path.GetTempPath(), "urscore-app-*.before-import-*")) Directory.Delete(aside, true);`.

Change `ScoreBookModelTests.TheExportedLineCountsWhatWentIntoTheFile` expectations to the new signature and add one row:

```csharp
    [Theory]
    [InlineData(1, 1, null, "Exported 1 reading and 1 finished battle to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book.")]
    [InlineData(16_800, 0, "3|5|2", "Exported 16,800 readings and 0 finished battles, with 3 recipes, 5 clans and 2 boards, to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book.")]
    public void TheExportedLineCountsWhatWentIntoTheFile(int readings, int finals, string? setup, string expected)
    {
        SetupPack? pack = null;
        if (setup is not null)
        {
            var n = setup.Split('|').Select(int.Parse).ToArray();
            pack = new SetupPack(
                [.. Enumerable.Range(0, n[0]).Select(i => new SetupRecipe($"r{i}", $"R{i}", "", new RecipeState(), []))],
                [.. Enumerable.Range(0, n[1]).Select(i => new Source($"s-{i:x8}", "r0", new Dictionary<string, string>(), SourceRole.Mine))],
                [.. Enumerable.Range(0, n[2]).Select(i => new BoardDef($"b-{i}", $"B{i}", []))],
                Settings.Defaults, []);
        }

        Assert.Equal(expected, ScoreBookModel.ExportedLine(
            new BookPackManifest(BookPack.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "0.5.6", readings, finals, pack is not null),
            "ur-score-stats-2026-09-22.zip", pack));
    }
```

- [ ] **Step 2: Run to see the failures**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~TheWholeSetupTravels|FullyQualifiedName~TheExportedLineCounts" 2>&1 | grep -E "error" | head -4`
Expected: `SetupWriter`, `SavedBoards`, `SaveImportedBoards` not found; `ExportedLine` has no 3-argument overload.

- [ ] **Step 3: Wire the composition root**

`ISetupServices` gains (with doc comments in the file's voice):

```csharp
    /// <summary>Where every file lives on this machine.</summary>
    AppPaths Paths { get; }

    /// <summary>What boards.json holds — following entries and your own boards — as last loaded; empty while there is no file.</summary>
    IReadOnlyList<BoardDef> SavedBoards { get; }

    /// <summary>Replaces the saved boards wholesale: sanitized to your own ids, written once, redrawn. The setup import's step 4.</summary>
    void SaveImportedBoards(IReadOnlyList<BoardDef> saved);

    /// <summary>The stores a setup import writes through (<see cref="Core.SetupMerge.Apply"/>).</summary>
    Core.ISetupWriter SetupWriter { get; }
```

`AppServices`:
- store `_paths = paths` in the seams constructor; `public AppPaths Paths => _paths;`
- `public IReadOnlyList<BoardDef> SavedBoards => _savedBoards ?? [];`
- `SaveImportedBoards(IReadOnlyList<BoardDef> saved)`: `var clean = BoardDefs.Sanitize(saved, LiveBoard.UserIdsOf(KnownAccounts)); _boardsFile.Save(clean, keepExisting: _boardsUnread); _savedBoards = clean.Count > 0 ? clean : null; _boardsUnread = false; RaiseChanged();` — read the existing `SaveBoards` (line ~311) and mirror how it sets `_boardsUnread`/`BoardsProblem`. The fence requires the exact text `var clean = BoardDefs.Sanitize(` and `_boardsFile.Save(clean,` — both appear here too, which the fence's `Assert.Contains` still accepts.
- `ExportStats(path)`: `_book.Flush(); var setup = SetupPack.FromHere(Installed, Sources, SavedBoards, Settings, KnownAccounts); return BookPack.Write(_book.Root, path, version, _time.GetUtcNow(), setup);`
- `SetupWriter`: a private nested class `SetupWriter(AppServices owner) : ISetupWriter` returning `owner._paths.Root` for `DataRoot`, `new SetupHere(owner.Installed, owner.Sources, owner.SavedBoards, owner.KnownAccounts)` for `Here`, and forwarding `SaveRecipe` → `owner.Store.Save(recipe, text, state)`, `SaveSources` → `owner.SaveSources`, `SaveImportedBoards` → `owner.SaveImportedBoards`, `SaveSettings` → `owner.SaveSettings`, `ReloadRecipes` → `owner.ReloadRecipes`. Expose `public ISetupWriter SetupWriter { get; }` built in the constructor.

`ImportFlowTests.ImportServices` gains the four members throwing `NotSupportedException`.

`ScoreBookModel.ExportedLine(BookPackManifest manifest, string fileName, SetupPack? setup)`: when `setup` is not null insert `, with {recipes} recipes, {clans} clans and {boards} boards,` before ` to {fileName}` (singulars: "1 recipe", "1 clan", "1 board"). `ScoreBookPage` passes `null` for now (Task 8 passes the real pack).

- [ ] **Step 4: Run the two tests, then the full suite**

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,567 (the theory grew by one row; one new composition test), exit 0.

- [ ] **Step 5: Commit**

```bash
git add src/Composition/ISetupServices.cs src/Composition/AppServices.cs src/UI/Setup/ScoreBookModel.cs src/UI/Setup/ScoreBookPage.xaml.cs tests/AppCompositionTests.cs tests/ScoreBookModelTests.cs tests/ImportFlowTests.cs
git commit -m "feat(composition): the setup goes out with the stats and comes in through the stores; two PCs prove it

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: The preview window, and the page that opens it

**Files:**
- Create: `src/UI/Setup/ImportPreviewModel.cs`, `src/UI/Setup/ImportPreviewWindow.xaml`, `src/UI/Setup/ImportPreviewWindow.xaml.cs`
- Modify: `src/UI/Setup/ScoreBookPage.xaml` (the section sentence), `src/UI/Setup/ScoreBookPage.xaml.cs` (the import handler)
- Test: `tests/ImportPreviewModelTests.cs`

**Interfaces:**
- Consumes: `SetupMergePlan`, `SetupItem`, `SetupMerge.Apply`, `BookImport.Run(folder, services)` (the stats), `BookPack.Open/Discard`, `ISetupServices.SetupWriter`, `RowList`, `ConfirmWindow.xaml` as the shape to copy.
- Produces:

```csharp
public sealed class ImportPreviewRow : INotifyPropertyChanged
{
    public SetupItem Item { get; }
    public bool HasTick { get; }          // Add, Update, Replace
    public bool Ticked { get; set; }      // raises PropertyChanged
    public bool CanTick { get; set; }     // false = greyed "needs its recipe"
    public string Text { get; }           // "Update — the file's copy differs…"
    public string TickName { get; }       // "Import <name>"
}
public sealed record ImportPreviewGroup(string Heading, IReadOnlyList<ImportPreviewRow> Rows);
public static class ImportPreviewModel
{
    public static IReadOnlyList<ImportPreviewGroup> Groups(SetupMergePlan plan);      // RECIPES, CLANS, BOARDS, KEYS TO ENTER AGAIN, STATS — only non-empty groups
    public static string Intro(BookPackManifest manifest, string fileName);            // "Exported 22 Sep by Ur Score 0.5.6 on the other PC."
    public static IReadOnlySet<string> TickedKeys(IEnumerable<ImportPreviewRow> rows);
    public static void Regrey(IReadOnlyList<ImportPreviewRow> rows, SetupMergePlan plan);   // recompute CanTick from the ticks
    public static string AfterLine(SetupApplied applied, BookImportOutcome stats);      // spec §4's after-line
    public const string NothingSent = "Nothing will be sent from this PC until you tick it in Setup › Stats.";
    public const string StatsOnly = "This file holds stats and no setup.";
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ImportPreviewModelTests.cs
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>The preview's rows, from a plan: which have ticks, what they say, how unticking a recipe greys its clans, and the after-line.</summary>
public class ImportPreviewModelTests
{
    private static SetupMergePlan PlanWithARecipeAndItsClan()
    {
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var file = new SetupPack(
            [new SetupRecipe(Clan.Slug, Clan.Name, text, new RecipeState(), [])],
            [new Source("s-file0001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main)],
            [], Settings.Defaults, [new SetupKey("ps99", "PS99 key", Profile.Slug, Profile.Name)]);
        return SetupMerge.Plan(file, new SetupHere([], [], [], []), 40, 2);
    }

    [Fact]
    public void GroupsFollowThePlanAndOnlyChangesHaveTicks()
    {
        var groups = ImportPreviewModel.Groups(PlanWithARecipeAndItsClan());

        Assert.Equal(["RECIPES", "CLANS", "KEYS TO ENTER AGAIN", "STATS"], groups.Select(g => g.Heading));
        var recipe = Assert.Single(groups[0].Rows);
        Assert.True(recipe.HasTick && recipe.Ticked);
        Assert.Equal("Import " + Clan.Name, recipe.TickName);
        Assert.StartsWith("Add", recipe.Text, StringComparison.Ordinal);
        Assert.False(Assert.Single(groups[2].Rows).HasTick);
        Assert.Equal("40 readings and 2 finished battles", Assert.Single(groups[3].Rows).Item.Name);
    }

    [Fact]
    public void UntickingARecipeGreysItsClanAndTickingItBackRestoresIt()
    {
        var plan = PlanWithARecipeAndItsClan();
        var rows = ImportPreviewModel.Groups(plan).SelectMany(g => g.Rows).ToList();
        var recipe = rows.Single(r => r.Item.Kind == SetupKind.Recipe);
        var clan = rows.Single(r => r.Item.Kind == SetupKind.Clan);

        recipe.Ticked = false;
        ImportPreviewModel.Regrey(rows, plan);
        Assert.False(clan.CanTick);
        Assert.DoesNotContain(clan.Item.Key, ImportPreviewModel.TickedKeys(rows));

        recipe.Ticked = true;
        ImportPreviewModel.Regrey(rows, plan);
        Assert.True(clan.CanTick);
        Assert.Contains(clan.Item.Key, ImportPreviewModel.TickedKeys(rows));
    }

    [Fact]
    public void TheAfterLineSaysWhatWasImportedKeptAndWhereTheOldSetupIs()
    {
        var applied = new SetupApplied(2, 3, 2, 1, 1, 0, @"C:\x\626labs.ur-score.before-import-20260922-1431", null, null);
        var stats = new BookImportOutcome(1204, "Imported 1,204 readings. 12 were already here.");

        Assert.Equal(
            "Imported 2 recipes, 3 clans and 2 boards; 1 clan kept as it was; 1 key to enter in Setup › Recipes. Then 1,204 readings, 12 already here. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(applied, stats));

        var failed = applied with { Boards = 0, FailedStep = "boards", FailureType = "UnauthorizedAccessException" };
        Assert.Equal(
            "Imported 2 recipes and 3 clans, then the boards could not be written (UnauthorizedAccessException); what was imported before that stands. Your previous setup is in 626labs.ur-score.before-import-20260922-1431.",
            ImportPreviewModel.AfterLine(failed, new BookImportOutcome(0, "")));
    }
}
```

The `BookImportOutcome` "already here" count is not carried by the record today; `AfterLine` parses nothing — it takes the outcome's `Added` and, for the "already here" part, the stats outcome's `Message` after "Imported N readings." is reused verbatim in a simpler form: implement `AfterLine` to append `" Then " + stats.Message` when `stats.Added > 0 || stats.Message.Length > 0`, and adjust the expected string in the first assert to `"... Setup › Recipes. Then Imported 1,204 readings. 12 were already here. Your previous setup is in …"` if you keep the message verbatim. Either wording is acceptable; the test and the code must agree and the sentence must read as English.

- [ ] **Step 2: Run to see the compile failure**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~ImportPreviewModelTests" 2>&1 | grep -E "error" | head -3`
Expected: `ImportPreviewModel` not found.

- [ ] **Step 3: Write the model**

```csharp
// src/UI/Setup/ImportPreviewModel.cs
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>One row of the preview: a plan item with a tick where the item is a change, greyed while its recipe is not coming.</summary>
public sealed class ImportPreviewRow(SetupItem item) : INotifyPropertyChanged
{
    private bool _ticked = item.Ticked;
    private bool _canTick = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SetupItem Item { get; } = item;

    public bool HasTick { get; } = item.Outcome is SetupOutcome.Add or SetupOutcome.Update or SetupOutcome.Replace;

    public bool Ticked { get => _ticked; set { if (_ticked != value) { _ticked = value; Raise(); } } }

    /// <summary>False for a clan whose recipe is neither coming nor here: shown greyed, with the reason in <see cref="Text"/>.</summary>
    public bool CanTick { get => _canTick; set { if (_canTick != value) { _canTick = value; Raise(); Raise(nameof(Text)); } } }

    public string Name => Item.Name;

    public string Text => !CanTick ? "needs its recipe" : Item.Outcome switch
    {
        SetupOutcome.Add => "Add — " + Item.Note,
        SetupOutcome.Update => "Update — " + Item.Note,
        SetupOutcome.Replace => "Replace — " + Item.Note,
        SetupOutcome.Same => "Same as here",
        SetupOutcome.Kept => "Kept — " + Item.Note,
        _ => Item.Note,
    };

    public string TickName => "Import " + Item.Name;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ImportPreviewGroup(string Heading, IReadOnlyList<ImportPreviewRow> Rows);

/// <summary>What the preview shows for a plan, and the one line said after (spec §3, §4). No WPF here.</summary>
public static class ImportPreviewModel
{
    public const string NothingSent = "Nothing will be sent from this PC until you tick it in Setup › Stats.";

    public const string StatsOnly = "This file holds stats and no setup.";

    public const string AsideNote = "Your clans, recipes and boards here are copied aside first, dated, in case.";

    public static IReadOnlyList<ImportPreviewGroup> Groups(SetupMergePlan plan)
    {
        var rows = plan.Items.Select(i => new ImportPreviewRow(i)).ToList();
        Regrey(rows, plan);
        (SetupKind Kind, string Heading)[] order = [(SetupKind.Recipe, "RECIPES"), (SetupKind.Clan, "CLANS"), (SetupKind.Board, "BOARDS"), (SetupKind.Key, "KEYS TO ENTER AGAIN"), (SetupKind.Stats, "STATS")];
        return [.. order.Select(o => new ImportPreviewGroup(o.Heading, rows.Where(r => r.Item.Kind == o.Kind).ToList())).Where(g => g.Rows.Count > 0)];
    }

    public static string Intro(BookPackManifest manifest, string fileName) =>
        $"Exported {manifest.TakenAt.ToLocalTime():d MMM} by Ur Score {manifest.App} on the other PC.";

    public static IReadOnlySet<string> TickedKeys(IEnumerable<ImportPreviewRow> rows) =>
        rows.Where(r => r.HasTick && r.Ticked && r.CanTick).Select(r => r.Item.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>After any tick changes: a clan can be ticked only while its recipe is ticked or installed.</summary>
    public static void Regrey(IReadOnlyList<ImportPreviewRow> rows, SetupMergePlan plan)
    {
        var ticked = rows.Where(r => r.HasTick && r.Ticked).Select(r => r.Item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows) row.CanTick = plan.CanTick(row.Item, ticked);
    }

    public static string AfterLine(SetupApplied applied, BookImportOutcome stats)
    {
        var aside = applied.AsideFolder is { } folder ? $" Your previous setup is in {System.IO.Path.GetFileName(folder)}." : "";
        if (applied.FailedStep is { } step)
        {
            var done = Parts(applied.Recipes, applied.Clans, applied.Boards);
            var before = done.Count == 0 ? "Nothing was imported before" : "Imported " + Join(done) + ", then";
            return $"{before} the {step} could not be written ({applied.FailureType}); what was imported before that stands.{aside}";
        }

        var parts = new List<string> { "Imported " + Join(Parts(applied.Recipes, applied.Clans, applied.Boards)) };
        if (applied.KeptClans > 0) parts.Add(applied.KeptClans == 1 ? "1 clan kept as it was" : $"{applied.KeptClans} clans kept as they were");
        if (applied.Keys > 0) parts.Add(applied.Keys == 1 ? "1 key to enter in Setup › Recipes" : $"{applied.Keys} keys to enter in Setup › Recipes");
        var line = string.Join("; ", parts) + ".";
        if (stats.Message.Length > 0) line += " Then " + stats.Message;
        return line + aside;
    }

    private static List<string> Parts(int recipes, int clans, int boards)
    {
        var parts = new List<string>();
        if (recipes > 0) parts.Add(recipes == 1 ? "1 recipe" : $"{recipes} recipes");
        if (clans > 0) parts.Add(clans == 1 ? "1 clan" : $"{clans} clans");
        if (boards > 0) parts.Add(boards == 1 ? "1 board" : $"{boards} boards");
        return parts;
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "nothing",
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
```

Make the test's expected strings match this wording exactly (the "Then …" sentence reuses `stats.Message` verbatim).

- [ ] **Step 4: Run the model tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "FullyQualifiedName~ImportPreviewModelTests" 2>&1 | grep -E "Failed |error|Failed:|Passed:|Expected|Actual"`
Expected: 3 passed.

- [ ] **Step 5: Write the window**

```xml
<!-- src/UI/Setup/ImportPreviewWindow.xaml -->
<Window x:Class="Labs626.UrScore.UI.ImportPreviewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:Labs626.UrScore.UI"
        Title="Import from another PC" Width="720" Height="640" MinWidth="560" MinHeight="420"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <DockPanel Margin="20,18,20,18">
        <StackPanel DockPanel.Dock="Top">
            <TextBlock x:Name="ImportFromLine" Style="{StaticResource Heading}" />
            <TextBlock x:Name="ImportIntroLine" Style="{StaticResource Muted}" Margin="0,4,0,12" />
        </StackPanel>
        <StackPanel DockPanel.Dock="Bottom" Margin="0,12,0,0">
            <TextBlock x:Name="ImportNothingSentLine" Style="{StaticResource Muted}" TextWrapping="Wrap" />
            <TextBlock x:Name="ImportAsideLine" Style="{StaticResource Muted}" TextWrapping="Wrap" Margin="0,4,0,0" />
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
                <Button x:Name="ImportTickedButton" Content="Import ticked" Style="{StaticResource PrimaryButton}" Margin="0,0,8,0"
                        Click="OnImportClick" AutomationProperties.Name="Import ticked" />
                <Button x:Name="ImportCancelButton" Content="Cancel" IsCancel="True" AutomationProperties.Name="Cancel" />
            </StackPanel>
        </StackPanel>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ui:RowList x:Name="ImportPreviewList" AutomationProperties.Name="What importing will do, with a tick for each thing that comes in">
                <ui:RowList.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,10">
                            <TextBlock Text="{Binding Heading}" Style="{StaticResource SectionLabel}" Margin="0,8,0,6" />
                            <ui:RowList ItemsSource="{Binding Rows}">
                                <ui:RowList.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,2">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="28" />
                                                <ColumnDefinition Width="220" />
                                                <ColumnDefinition Width="*" />
                                            </Grid.ColumnDefinitions>
                                            <CheckBox IsChecked="{Binding Ticked, Mode=TwoWay}" IsEnabled="{Binding CanTick}"
                                                      Visibility="{Binding HasTick, Converter={StaticResource BoolToVisibility}}"
                                                      AutomationProperties.Name="{Binding TickName}" VerticalAlignment="Center" />
                                            <TextBlock Grid.Column="1" Text="{Binding Name}" FontWeight="SemiBold" TextWrapping="Wrap" VerticalAlignment="Center" />
                                            <TextBlock Grid.Column="2" Text="{Binding Text}" Style="{StaticResource Muted}" TextWrapping="Wrap" VerticalAlignment="Center" />
                                        </Grid>
                                    </DataTemplate>
                                </ui:RowList.ItemTemplate>
                            </ui:RowList>
                        </StackPanel>
                    </DataTemplate>
                </ui:RowList.ItemTemplate>
            </ui:RowList>
        </ScrollViewer>
    </DockPanel>
</Window>
```

Check `App.xaml` for an existing `BooleanToVisibilityConverter` resource (`grep -n "BooleanToVisibilityConverter\|BoolToVisibility" src/App.xaml src/UI/*.xaml`); if one exists under another key, use that key; if none, add `<BooleanToVisibilityConverter x:Key="BoolToVisibility" />` to `App.xaml`'s resources. Check the `PrimaryButton` style key exists (`grep -n 'x:Key="PrimaryButton"' src/App.xaml`); if not, use the plain button style and note it.

```csharp
// src/UI/Setup/ImportPreviewWindow.xaml.cs
using System.ComponentModel;
using System.Windows;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The plan, drawn (design 2026-09-22, §3): every recipe, clan, board and key the file holds, what importing does to
/// each, a tick on each change, and one button that applies the ticked ones. Ur Score's own window, in the theme,
/// never a stock dialog (owner rule, V3-S.10). <see cref="TickedKeys"/> is the answer; the caller applies.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    private readonly SetupMergePlan _plan;
    private readonly List<ImportPreviewRow> _rows;

    public ImportPreviewWindow(SetupMergePlan plan, BookPackManifest manifest, string fileName)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _plan = plan;

        ImportFromLine.Text = $"Import from {fileName}";
        ImportIntroLine.Text = ImportPreviewModel.Intro(manifest, fileName);
        var groups = ImportPreviewModel.Groups(plan);
        _rows = groups.SelectMany(g => g.Rows).ToList();
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;
        ImportPreviewList.ItemsSource = groups;
        ImportNothingSentLine.Text = plan.File.Recipes.Count > 0 ? ImportPreviewModel.NothingSent : ImportPreviewModel.StatsOnly;
        ImportAsideLine.Text = ImportPreviewModel.AsideNote;

        Loaded += (_, _) => ImportCancelButton.Focus();
    }

    /// <summary>The keys of every ticked, tickable change; set when Import ticked was pressed, else null.</summary>
    public IReadOnlySet<string>? TickedKeys { get; private set; }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportPreviewRow.Ticked)) ImportPreviewModel.Regrey(_rows, _plan);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        TickedKeys = ImportPreviewModel.TickedKeys(_rows);
        DialogResult = true;
    }
}
```

- [ ] **Step 6: The page opens the preview**

In `ScoreBookPage.xaml.cs`, replace the body of `OnImportStatsClick`'s transfer lambda. The flow: open the file on a worker; if it holds a setup, show the preview on the UI thread; on yes, `SetupMerge.Apply` on the UI thread, then the stats merge on a worker, then `ReloadBookAsync`; discard the temp folder in a `finally`.

```csharp
        await TransferAsync("Reading that file…", "That file could not be imported", async () =>
        {
            var opened = await Task.Run(() => BookPack.Open(dialog.FileName));
            try
            {
                if (opened.Folder is null) return ("", opened.Problem, false);

                var fileName = Path.GetFileName(dialog.FileName);
                if (opened.Setup is null)
                {
                    // A 0.5.5 file, or a stats-only export: the stats merge as before, no preview needed.
                    var statsOnly = await Task.Run(() => BookImport.Run(opened.Folder, _services));
                    return (statsOnly.Message, statsOnly.Problem, statsOnly.Added > 0);
                }

                var plan = SetupMerge.Plan(opened.Setup, _services.SetupWriter.Here, opened.Manifest!.Readings, opened.Manifest.Finals);
                var preview = new ImportPreviewWindow(plan, opened.Manifest, fileName) { Owner = Window.GetWindow(this) };
                if (preview.ShowDialog() != true || preview.TickedKeys is not { } ticked) return ("Nothing imported.", "", false);

                var applied = SetupMerge.Apply(plan, ticked, _services.SetupWriter, DateTimeOffset.Now);
                if (applied.FailedStep is not null) _services.AddTrail($"SETUP NOT IMPORTED AT {applied.FailedStep.ToUpperInvariant()}: {applied.FailureType}");
                var stats = ticked.Contains("stats") && applied.FailedStep is null
                    ? await Task.Run(() => BookImport.Run(opened.Folder, _services))
                    : new BookImportOutcome(0, "");
                return (ImportPreviewModel.AfterLine(applied, stats), stats.Problem, stats.Added > 0);
            }
            finally
            {
                BookPack.Discard(opened);
            }
        });
```

`BookImport.RunFile` stays for the month-file case: keep the branch `if (!path.EndsWith(BookPack.Extension)) { var outcome = await Task.Run(() => BookImport.RunFile(dialog.FileName, _services)); return (outcome.Message, outcome.Problem, outcome.Added > 0); }` before the code above. `OnExportStatsClick` passes the pack for the line: change `_services.ExportStats` to return what it wrote — simplest: add `SetupPack? LastExportedSetup` is NOT wanted; instead have `ExportStats` return `(BookPackManifest Manifest, SetupPack Setup)`? Keep the interface as it is and compute the line's counts from the manifest alone by adding `int Recipes, int Clans, int Boards` to `BookPackManifest`? No — keep it simple: `ScoreBookModel.ExportedLine(manifest, fileName, setup)` receives `SetupPack.FromHere(_services.Installed, _services.Sources, _services.SavedBoards, _services.Settings, _services.KnownAccounts)` built on the page after the export, which is the same pack the export wrote.

Change the section sentence in `ScoreBookPage.xaml` to: "Ran Ur Score on another PC? Export its stats there to one file, then import that file here. The file carries the score book and your setup — recipes with their ticks, clans, boards — and never a key; you see what will change before anything does."

- [ ] **Step 7: Build, run the whole suite, and render the window for eyes**

Run: `dotnet test tests/Ur-Score.Tests.csproj 2>&1 | grep -E "Failed:|Passed:|Aborted"; echo "exit: ${PIPESTATUS[0]}"`
Expected: Passed 1,570, exit 0.

Then a render test, in the style of `TabMenuRenderTests` (`[Collection(WpfCollection.Name)]`, `UiThread.RunInApp`): build an `ImportPreviewWindow` from `PlanWithARecipeAndItsClan`-shaped data plus one Replace clan and one Kept clan, `window.Show()` off screen (`Left = -4000`), `UpdateLayout`, `RenderTargetBitmap` of `window.Content` to `artifacts/smoke/import-preview.png`, `window.Close()`. Assert the tick for the clan exists in the automation tree named "Import CCGP". Look at the PNG (`Read` it) before calling the task done: headings, ticks, greyed row, both buttons, nothing clipped at 720×640.

- [ ] **Step 8: Commit**

```bash
git add src/UI/Setup/ImportPreviewModel.cs src/UI/Setup/ImportPreviewWindow.xaml src/UI/Setup/ImportPreviewWindow.xaml.cs src/UI/Setup/ScoreBookPage.xaml src/UI/Setup/ScoreBookPage.xaml.cs src/App.xaml tests/ImportPreviewModelTests.cs tests/ImportPreviewRenderTests.cs
git commit -m "feat(ui): Import stats shows the plan with ticks before anything is written; sends arrive off and it says so

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: The smoke walk, the record, and the release note

**Files:**
- Modify: `tools/smoke/walk-score-book.ps1`, `tools/smoke/README.md`
- Modify: `docs/backlog.md` (V3-S.46), `CHANGELOG.md` (an `## Unreleased` section above 0.5.5), `docs/2026-09-22-setup-transfer-design.md` (nothing unless the build taught something)

- [ ] **Step 1: Extend the score-book walk** — after step 4c (import of the same file back), add:

```powershell
    # 4d/4e/4f. The setup travels (V3-S.46): export from this folder (it has a recipe and one clan), start over on a
    # fresh folder, import the file, read the preview's rows by name, untick the clan, Import ticked. The recipe
    # arrives and the clan does not; the after-line says so; the aside folder exists; nothing is set to send.
    $setup = Get-SetupWindow
    Invoke-Element (Get-Button $setup 'Export stats to a file for another PC')
    $setupFile = Join-Path $exportDir 'ur-score-everything-smoke.zip'
    Complete-FileDialog '^Export stats to a file$' $setupFile
    $exportedAll = Wait-Line (Get-SetupWindow) 'StatsTransferLine' 'with 1 recipe' 30
    Check '4d Export stats counts the setup in its line' ($exportedAll -match 'with 1 recipe, 1 clan and \d+ boards?') "'$exportedAll'"

    Stop-UrScoreFromBoard
    $second = Move-UrDataAside          # a second, fresh folder; the first backup is restored in the finally as before
    try {
        Start-UrScore | Out-Null
        $setup = Open-SetupPage 'Score book'
        Invoke-Element (Get-Button $setup 'Import stats from another PC''s file')
        Complete-FileDialog '^Import stats from another PC$' $setupFile
        $preview = Wait-UrWindow '^Import from another PC$' 20
        Check '4e The preview opens and names the recipe, the clan and the stats' ([bool]$preview -and [bool](Get-Check $preview 'Import Pet Sim 99 clan battle points') -and [bool](Get-Check $preview "Import $Main")) "preview=$([bool]$preview)"
        Set-Tick (Get-Check $preview "Import $Main") $false
        Invoke-Element (Get-Button $preview 'Import ticked')
        $after = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^Imported 1 recipe' 60
        Check '4f The recipe arrives, the unticked clan does not, and the line says where the old setup is' ($after -match '^Imported 1 recipe;.*before-import-' -and -not (Test-Path (Join-Path $UrData 'sources.json')) -or ((Get-Content (Join-Path $UrData 'sources.json') -Raw) -notmatch $Main)) "'$after'"
        $recipeState = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-clan-battle-points.state.json') -Raw
        Check '4g Nothing arrived set to send' ($recipeState -notmatch '"send":\s*true') 'state file read'
        & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'score-book-setup-import.png') | Out-Null
    }
    finally {
        Stop-UrScore
        if ($second) { Remove-Item $UrData -Recurse -Force -ErrorAction SilentlyContinue; Rename-Item $second (Split-Path $UrData -Leaf) }
        Get-ChildItem (Split-Path $UrData -Parent) -Directory -Filter '626labs.ur-score.before-import-*' | Remove-Item -Recurse -Force
    }
```

Read `uia.ps1`'s `Get-Check` and `Set-Tick` first; they exist (lines ~130 and ~154). The `Move-UrDataAside` guard from S1-16.1 will refuse a second aside while the first backup exists — so the second folder must be made by hand: replace `$second = Move-UrDataAside` with `Stop-UrScore; $second = "$UrData.smoke-second-$(Get-Date -Format 'HHmmss')"; Rename-Item $UrData (Split-Path $second -Leaf); New-Item -ItemType Directory -Force $UrData | Out-Null` and in the finally rename `$second` back. Parse every script (`[System.Management.Automation.Language.Parser]::ParseFile`) before running.

- [ ] **Step 2: The launch** — RoRoRo quit (`Get-Process ROROROblox.App` empty), Release build (`dotnet build Ur-Score.csproj -c Release`), then `powershell -File tools/smoke/walk-score-book.ps1 -Main <a clan currently in a battle>`; every step PASS, RoRoRo not running before and after. Look at `artifacts/smoke/score-book-setup-import.png` and `import-preview.png`. This step is owner-watched: say so before running it.

- [ ] **Step 3: The record**

- `docs/backlog.md`: V3-S.46 `**FIXED** 2026-09-2x` with the evidence: the tests by name, the two-PC composition test, the walk's steps, sends-off, the aside folder. Counts by rows (OPEN 10 → 9, FIXED 203 → 204). Add a recount line.
- `CHANGELOG.md`: a new `## Unreleased` section above 0.5.5, `### Added`: "**Your setup travels with your stats.** Export stats now carries your recipes with their ticks, your clans, your boards and two settings, and Import stats shows you what will change — added, updated, replaced, kept — with a tick on each, before anything is written. Keys never travel (the file names the ones to enter again). **Nothing arrives set to send:** every send tick is off until you tick it on the new PC, so a file from elsewhere can never start reports on its own. Your previous setup is copied aside, dated, before the import. A 0.5.5 stats file still imports."
- `tools/smoke/README.md`: the walk's row mentions the setup import through the preview.

- [ ] **Step 4: Full suite one last time, then commit**

```bash
git add tools/smoke/walk-score-book.ps1 tools/smoke/README.md docs/backlog.md CHANGELOG.md
git commit -m "smoke+docs: the setup import walked through its preview; V3-S.46 closed; an Unreleased note

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Owner-supplied step (not a task for an agent): the old-versus-new fixture

Before this ships: on the second machine, install 0.5.5 through RoRoRo, open it on the old data folder, and note what it shows. Then copy that folder's `recipes/`, `sources.json`, `boards.json` and `settings.json` — **never `keys.dat` or `accounts.json`** — into `tests/Fixtures/old-install-2026-09/`, scrub any account id the way S1-F.11 did, and a follow-up task adds an `AppCompositionTests` case that composes the app on that folder and asserts it loads with every recipe and clan present and nothing sent. That is the migration test Ur Score has never had; the setup import above must be walked against it too.
