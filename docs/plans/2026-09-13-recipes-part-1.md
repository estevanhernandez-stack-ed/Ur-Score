# Ur Score Recipes, Part 1: Engine, Format and Import — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur Score reads any web stat described by a recipe file instead of knowing one Pet Simulator 99 address, and a hand-written Pet Sim recipe, imported, reports for the user's own accounts.

**Architecture:** A recipe is data: a list of GET requests, dotted paths into their JSON, and a reading form. A pure parser turns a file into a `Recipe` or named problems; a path walker reads values and tells "the source says nothing" apart from "the shape changed"; an engine runs the steps through a transport seam and returns a `RecipeReading`; `RecipeWatch` replaces `ScoreWatch` and keeps every guarantee it had (one cycle at a time, own accounts only through `ReportPolicy`, no backlog). Keys live in a DPAPI file bound to one host and are redacted from everything that leaves the window.

**Tech Stack:** .NET 10 (SDK 10.0.203, pinned by `global.json`), WPF, `Grpc.Net.Client` 2.70.0, `ROROROblox.PluginContract` 0.10.0 (nuget.org), `System.Security.Cryptography.ProtectedData` 10.0.12, xUnit 2.9.2.

**Spec:** [`docs/2026-09-13-recipes-design.md`](../2026-09-13-recipes-design.md). This plan is §13 part 1. Parts 2 (the window) and 3 (the builder) get their own plans.

**Branch:** `feat/recipes`. Baseline before Task 1: `dotnet test tests/Ur-Score.Tests.csproj` passes 127.

## Global Constraints

Every task's requirements implicitly include these. Most of them fail silently when broken.

- **A recipe describes where a number is and how to read it. It never decides what happens with the number.** No thresholds, rules, alerts, send choices or triggers come from a recipe (spec §2).
- **Requests are GET over https only.** The parser refuses anything else, and the transport refuses again before a connection opens.
- **A recipe's hosts are literal.** A placeholder may never stand in the host part of any url, so the import screen can always name every host.
- **Poll interval is `Recipe.EffectiveEverySeconds`**, which is `Math.Max(60, everySeconds)`. Never throttle reports.
- **Report the raw value as read, never a rate, and never special-case a drop.**
- **`observedAt` is `DateTimeOffset.UtcNow`.**
- **Match accounts on Roblox user id only, never display name.** The host masks display names under streamer mode.
- **`ReportPolicy.SendAsync` is the only path to `IHostClient.ReportMetricAsync`.** The existing fence in `ReportPolicyTests` stays green.
- **Other members' ids and values are never reported and never written to the trail or diagnostics.** The one local exception is the redacted raw-response file under `last-response`, never transmitted.
- **A saved key value never appears in** a recipe file, `settings.json`, a recipe state file, a raw response file, error text, the diagnostics trail, or the clipboard.
- **A key is only ever sent to the host it is bound to.**
- **Every miss names what was actually present.**
- **Never replay a backlog** when the host reappears. Resume from the next poll.
- **Capabilities stay exactly `host.metrics.report` and `host.queries.accounts`; `autostartDefault` stays `off`.**
- **Rule writing is unchanged in part 1:** explicit click, preview, merge, backup, owner stamp. The user-typed threshold is part 2.
- **Not in part 1:** key entry UI, searchable inputs, the recipe list, the builder, export. A recipe needing a key reports `KeyMissing` until part 2.
- **Every task leaves `dotnet build -c Release` and `dotnet test tests/Ur-Score.Tests.csproj` green.**

## Rulings made while writing this plan

Recorded so an executor does not re-decide them. Each is `what — why — cost if wrong`.

1. **`idleWithout` means a JSON null on the path or an empty string at the end. A key missing from an object is a shape miss.** — The spec says "absent, null or an empty string", but reading a renamed field as idle would show "No clan battle running" forever, which is the silent failure `JsonNav` exists to prevent. The retired `ClanParser.ActiveBattle` already drew exactly this line and was proven live. Task 2 amends the spec row. — Cost if wrong: a source that signals "nothing" by omitting a key reads as a shape miss until its recipe is adjusted.
2. **Keys are stored by key id, bound to one host.** Refusal happens when a saved key with the same id is bound to a different host than the recipe would send it to. — This is the doctored-copy attack the spec guards against. — Cost if wrong: two unrelated recipes that both call their key `apiKey` for different services collide, and the refusal tells the user to remove the saved one first.
3. **Keys shorter than 6 characters are refused at save.** — The redactor ignores values that short, because masking a three-character string would shred unrelated text. Refusing them keeps "a key never appears in text" true. — Cost if wrong: a service with a 5-character key cannot be used.
4. **Headline values are read from the last step's response and only allowed on a list-form last step.** — The spec does not say which response a headline path reads, and a per-account step has one response per account. — Cost if wrong: a per-account recipe cannot show a headline.
5. **A per-account step's 400 or 404 costs only that account.** Any other stop ends the reading. — One terminated account must not blank everyone else's numbers. — Cost if wrong: none found.
6. **`KeyRejected` holds until a saved key value changes; `SignInRequired` holds until the recipe or its inputs change.** — Spec §4.3 says both stop; retrying a rejected key every poll is what it forbids. — Cost if wrong: a transient 401 stays held until the user acts.
7. **Build order keeps the old types alive only as long as something compiles against them.** `RecipeWatch` lands beside `ScoreWatch` in Task 8. Task 9 slims `Settings`, which `ScoreWatch` reads, so `ScoreWatch` and its tests leave in Task 9 with the window switch. The Pet Sim source types, `WatchSnapshot` and the three Pet Sim states leave with the fence in Task 11. — So every task compiles and passes. — Cost: the Pet Sim source files sit unused for two tasks.

## File Structure

```text
src/
  Recipes/
    Recipe.cs            the model records, slug, effective poll interval
    Placeholders.cs      find and fill {name} tokens
    RecipeHosts.cs       the literal host of an https url
    RecipeParser.cs      JSON text -> Recipe, or every problem named
    RecipePath.cs        walk a dotted path; Found / Nothing / Missing
    Redactor.cs          mask saved key values in any text
    RecipeTransport.cs   IRecipeTransport, FetchResult, HttpRecipeTransport
    KeyStore.cs          IKeyStore, SavedKey, KeyStore (DPAPI, host-bound)
    RecipeEngine.cs      IRecipeEngine, RecipeEngine, RecipeReading and friends
    ImportReview.cs      hosts contacted and what each receives; update comparison
    RecipeStore.cs       installed recipes and per-recipe state on disk
  Source/
    UrScoreIdentity.cs   the User-Agent, moved out of ClanClient
    NameClient.cs        uses UrScoreIdentity (modified)
    ClanClient.cs, ClanParser.cs, ClanStanding.cs   deleted in Task 11
  Core/
    Leaderboard.cs       Rank, moved from ClanStanding
    RecipeWatch.cs       replaces ScoreWatch; RecipeSnapshot
    WatchState.cs        new states (Task 8); Pet Sim states removed (Task 11)
    Settings.cs          slimmed to resolveNames + activeRecipe (Task 9)
    ScoreWatch.cs        deleted in Task 9
  UI/
    MainWindow.xaml(.cs) runs the active recipe (Task 9), import buttons (Task 10)
    ImportWindow.xaml(.cs)  the safety screen and recipe settings (Task 10)
tests/
  Fixtures/petsim99-clan-battle.recipe.json
  Fixtures/roblox-followers.recipe.json
  RecipeParserTests.cs, RecipePathTests.cs, RedactorTests.cs, RecipeTransportTests.cs,
  KeyStoreTests.cs, RecipeEngineTests.cs, ImportReviewTests.cs, RecipeStoreTests.cs,
  LeaderboardTests.cs, RecipeWatchTests.cs, NoHostnameFenceTests.cs   (new)
  SettingsTests.cs (Task 9), NameClientTests.cs (comments only)
  ScoreWatchTests.cs   deleted in Task 9
  ClanClientTests.cs, ClanParserTests.cs, ClanStandingTests.cs   deleted in Task 11
```

---

### Task 1: The recipe model and its parser

**Files:**
- Create: `src/Recipes/Recipe.cs`, `src/Recipes/Placeholders.cs`, `src/Recipes/RecipeHosts.cs`, `src/Recipes/RecipeParser.cs`
- Create: `tests/Fixtures/petsim99-clan-battle.recipe.json`, `tests/Fixtures/roblox-followers.recipe.json`, `tests/RecipeParserTests.cs`
- Modify: `tests/Ur-Score.Tests.csproj` (copy fixtures to output), `nuget.config` (drop the local contract feed)

**Interfaces:**
- Consumes: `JsonNav.TryGet`, `JsonNav.Keys` (existing, `src/Source/JsonNav.cs`, internal).
- Produces:
  - `record Recipe(int Version, string Name, string Credit, string? Author, string MetricId, string ValueLabel, int EverySeconds, IReadOnlyList<RecipeInput> Inputs, IReadOnlyList<RecipeKey> Keys, IReadOnlyList<RecipeStep> Steps, IReadOnlyList<RecipeHeadline> Headline)` with `const int SupportedVersion = 1`, `const int MinimumEverySeconds = 60`, `int EffectiveEverySeconds`, `RecipeStep LastStep`, `string Slug`
  - `record RecipeInput(string Id, string Label, RecipeSearch? Search)`, `record RecipeSearch(string Url, string List)`
  - `enum KeyPlacement { Header, Query }`, `record RecipeKey(string Id, string Label, string GetOneAt, KeyPlacement In, string Name)`
  - `record RecipeStep(string Url, IReadOnlyList<string> UseKeys, IReadOnlyDictionary<string,string> Take, string? IdleWithout, string? IdleMessage, string? Rows, string? UserId, bool PerAccount, string? Value)`
  - `record RecipeHeadline(string Label, string Path)`
  - `static class Placeholders` (internal): `const string UserId = "userId"`, `IReadOnlyList<string> Names(string text)`, `string Fill(string template, IReadOnlyDictionary<string,string> values, bool encode)`
  - `static class RecipeHosts` (internal): `string HostOf(string httpsUrl)`
  - `record RecipeParseResult(Recipe? Recipe, IReadOnlyList<string> Problems)` with `bool Ok`
  - `static class RecipeParser`: `RecipeParseResult Parse(string json)`

- [ ] **Step 1: Drop the local contract feed**

`ROROROblox.PluginContract` 0.10.0 is on nuget.org now (verified 2026-09-13: the flat container lists `0.10.0`). Replace `nuget.config` with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

Run: `dotnet restore Ur-Score.csproj --force && dotnet test tests/Ur-Score.Tests.csproj`
Expected: restore succeeds from nuget.org; 127 passed.

- [ ] **Step 2: Add the two fixtures**

`tests/Fixtures/petsim99-clan-battle.recipe.json`:

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 clan battle points",
  "credit": "Data from Big Games' public Pet Simulator 99 API.",
  "metricId": "clan.battle.points",
  "valueLabel": "Points",
  "everySeconds": 180,
  "inputs": [
    { "id": "clan", "label": "Your clan",
      "search": { "url": "https://ps99.biggamesapi.io/api/clansList", "list": "data" } }
  ],
  "steps": [
    { "url": "https://ps99.biggamesapi.io/api/activeClanBattle",
      "take": { "battle": "data.configName" },
      "idleWithout": "battle", "idleMessage": "No clan battle running" },
    { "url": "https://ps99.biggamesapi.io/api/clan/{clan}",
      "rows": "data.Battles.{battle}.PointContributions",
      "userId": "UserID", "value": "Points" }
  ],
  "headline": [
    { "label": "Clan place", "path": "data.Battles.{battle}.Place" },
    { "label": "Clan points", "path": "data.Battles.{battle}.Points" }
  ]
}
```

`tests/Fixtures/roblox-followers.recipe.json`:

```json
{
  "recipe": 1,
  "name": "Roblox followers",
  "credit": "Data from Roblox's public friends API.",
  "metricId": "roblox.followers",
  "valueLabel": "Followers",
  "everySeconds": 600,
  "steps": [
    { "url": "https://friends.roblox.com/v1/users/{userId}/followers/count",
      "perAccount": true, "value": "count" }
  ]
}
```

In `tests/Ur-Score.Tests.csproj`, add inside the existing `<Project>`:

```xml
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing parser tests**

`tests/RecipeParserTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeParserTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>A minimal valid recipe, with one field swapped in by each test.</summary>
    private static string With(string steps, string extra = "") => $$"""
        {
          "recipe": 1, "name": "Test", "credit": "Test data.", "metricId": "test.value",
          "everySeconds": 120 {{extra}},
          "steps": {{steps}}
        }
        """;

    private const string OneListStep =
        """[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";

    private static IReadOnlyList<string> Problems(string json) => RecipeParser.Parse(json).Problems;

    [Fact]
    public void ThePetSimRecipeParsesWithEveryFieldRead()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        var recipe = result.Recipe!;
        Assert.Equal("Pet Sim 99 clan battle points", recipe.Name);
        Assert.Equal("Points", recipe.ValueLabel);
        Assert.Equal(180, recipe.EffectiveEverySeconds);
        Assert.Equal("clan", Assert.Single(recipe.Inputs).Id);
        Assert.Equal("data", recipe.Inputs[0].Search!.List);
        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("data.configName", recipe.Steps[0].Take["battle"]);
        Assert.Equal("battle", recipe.Steps[0].IdleWithout);
        Assert.Equal("UserID", recipe.LastStep.UserId);
        Assert.Equal(2, recipe.Headline.Count);
    }

    [Fact]
    public void TheFollowersRecipeParsesAsPerAccount()
    {
        var result = RecipeParser.Parse(Fixture("roblox-followers.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.True(result.Recipe!.LastStep.PerAccount);
        Assert.Equal("count", result.Recipe.LastStep.Value);
    }

    [Fact]
    public void ValueLabelDefaultsToValue() =>
        Assert.Equal("Value", RecipeParser.Parse(With(OneListStep)).Recipe!.ValueLabel);

    [Fact]
    public void APollFasterThanTheFloorIsRaisedToIt()
    {
        var recipe = RecipeParser.Parse(With(OneListStep).Replace("\"everySeconds\": 120", "\"everySeconds\": 5")).Recipe!;
        Assert.Equal(5, recipe.EverySeconds);
        Assert.Equal(60, recipe.EffectiveEverySeconds);
    }

    [Fact]
    public void ARecipeFromANewerFormatIsRefusedAlone()
    {
        // A newer file's other fields may mean something this build cannot read, so every other
        // complaint about it would be noise.
        var problems = Problems("""{ "recipe": 2 }""");
        Assert.Equal(new[] { "This recipe was made by a newer Ur Score (format 2). Update Ur Score to import it." }, problems);
    }

    [Fact]
    public void NotJsonSaysSo() =>
        Assert.StartsWith("This file is not valid JSON:", Assert.Single(Problems("not json")));

    [Fact]
    public void MissingTopLevelFieldsAreEachNamed()
    {
        var problems = Problems("""{ "recipe": 1, "everySeconds": 60, "steps": [] }""");
        Assert.Contains("The recipe has no 'name'.", problems);
        Assert.Contains("The recipe has no 'credit'.", problems);
        Assert.Contains("The recipe has no 'metricId'.", problems);
        Assert.Contains("The recipe has no steps. It needs at least one request to make.", problems);
    }

    [Fact]
    public void AStepWithNoUrlIsNamedByNumber()
    {
        var steps = """[{ "url": "https://example.com/a", "take": { "x": "data.x" } }, { "rows": "data", "userId": "id", "value": "v" }]""";
        Assert.Contains("Step 2 has no 'url'.", Problems(With(steps)));
    }

    [Fact]
    public void PlainHttpIsRefused()
    {
        var steps = """[{ "url": "http://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1's url must start with https://.", Problems(With(steps)));
    }

    [Fact]
    public void AHostMadeOfAPlaceholderIsRefused()
    {
        // The import screen has to be able to name every host before anything runs.
        var steps = """[{ "url": "https://{site}/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        var extra = """, "inputs": [{ "id": "site", "label": "Site" }]""";
        Assert.Contains("Step 1's url must name its host directly, not through a placeholder.", Problems(With(steps, extra)));
    }

    [Fact]
    public void AUrlCarryingAUsernameIsRefused()
    {
        var steps = """[{ "url": "https://me:secret@example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1's url must not carry a username or password.", Problems(With(steps)));
    }

    [Fact]
    public void AnUnknownPlaceholderIsNamed()
    {
        var steps = """[{ "url": "https://example.com/clan/{clna}", "rows": "data", "userId": "id", "value": "score" }]""";
        var extra = """, "inputs": [{ "id": "clan", "label": "Your clan" }]""";
        Assert.Contains("Unknown placeholder {clna} in step 1's url.", Problems(With(steps, extra)));
    }

    [Fact]
    public void UserIdOutsideAPerAccountStepIsRefused()
    {
        var steps = """[{ "url": "https://example.com/u/{userId}", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("{userId} can only be used in a perAccount step, and step 1 is not one.", Problems(With(steps)));
    }

    [Fact]
    public void APerAccountStepMustUseUserId()
    {
        var steps = """[{ "url": "https://example.com/count", "perAccount": true, "value": "count" }]""";
        Assert.Contains("A perAccount step's url must contain {userId}, or it asks the same thing once per account.", Problems(With(steps)));
    }

    [Fact]
    public void TheLastStepMustUseExactlyOneForm()
    {
        var steps = """[{ "url": "https://example.com/rows", "rows": "data", "value": "score" }]""";
        Assert.Contains("The last step needs rows, userId and value, or perAccount and value.", Problems(With(steps)));
    }

    [Fact]
    public void OnlyTheLastStepReads()
    {
        var steps = """[{ "url": "https://example.com/a", "value": "x" }, { "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 reads a value, but only the last step can.", Problems(With(steps)));
    }

    [Fact]
    public void IdleWithoutMustNameSomethingTheStepTakes()
    {
        var steps = """[{ "url": "https://example.com/a", "take": { "battle": "data.name" }, "idleWithout": "round" }, { "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 is idle without 'round', which it does not take.", Problems(With(steps)));
    }

    [Fact]
    public void AnUndeclaredKeyIsNamed()
    {
        var steps = """[{ "url": "https://example.com/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Step 1 uses key 'tracker', which the recipe does not declare.", Problems(With(steps)));
    }

    [Fact]
    public void AKeySentToTwoHostsIsRefused()
    {
        var extra = """, "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://example.com/keys", "in": "header", "name": "x-api-key" }]""";
        var steps = """[{ "url": "https://a.example.com/x", "useKeys": ["tracker"], "take": { "v": "data.v" } }, { "url": "https://b.example.com/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]""";
        Assert.Contains("Key 'tracker' is sent to two hosts, a.example.com and b.example.com. A key can only go to one.", Problems(With(steps, extra)));
    }

    [Fact]
    public void AKeyPlacementThatIsNeitherHeaderNorQueryIsNamed()
    {
        var extra = """, "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://example.com/keys", "in": "body", "name": "k" }]""";
        Assert.Contains("Key 1's 'in' is 'body'. It must be header or query.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void AHeadlineNeedsAListFormLastStep()
    {
        var steps = """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count" }]""";
        var extra = """, "headline": [{ "label": "Total", "path": "total" }]""";
        Assert.Contains("A headline can only be read from a list-form last step.", Problems(With(steps, extra)));
    }

    [Fact]
    public void AHeadlineShowsAtMostTwoValues()
    {
        var extra = """, "headline": [{ "label": "A", "path": "a" }, { "label": "B", "path": "b" }, { "label": "C", "path": "c" }]""";
        Assert.Contains("The headline has 3 values. It shows at most two.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void ASearchListAddressMustBeFixed()
    {
        var extra = """, "inputs": [{ "id": "clan", "label": "Your clan", "search": { "url": "https://example.com/{clan}", "list": "data" } }]""";
        Assert.Contains("Input 'clan' searches with a placeholder {clan}. A search list's address must be fixed.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void AnInputCannotBeCalledUserId()
    {
        var extra = """, "inputs": [{ "id": "userId", "label": "Who" }]""";
        Assert.Contains("An input cannot be called 'userId'. That name is reserved for your accounts' Roblox ids.", Problems(With(OneListStep, extra)));
    }

    [Fact]
    public void TheSlugComesFromNameAndAuthor()
    {
        var recipe = RecipeParser.Parse(With(OneListStep, """, "author": "Este Hernandez" """)).Recipe!;
        Assert.Equal("test-este-hernandez", recipe.Slug);
    }

    [Fact]
    public void PlaceholdersFillEncodedForUrlsAndRawForPaths()
    {
        var values = new Dictionary<string, string> { ["clan"] = "a/b c" };
        Assert.Equal("https://example.com/clan/a%2Fb%20c", Placeholders.Fill("https://example.com/clan/{clan}", values, encode: true));
        Assert.Equal("Battles.a/b c.Rows", Placeholders.Fill("Battles.{clan}.Rows", values, encode: false));
    }

    [Theory]
    [InlineData("https://ps99.biggamesapi.io/api/clan/{clan}", "ps99.biggamesapi.io")]
    [InlineData("https://Friends.Roblox.com:443/v1/users", "friends.roblox.com")]
    [InlineData("https://example.com?x=1", "example.com")]
    public void HostOfReadsTheLiteralHostLowercased(string url, string host) =>
        Assert.Equal(host, RecipeHosts.HostOf(url));
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipeParserTests`
Expected: FAIL to compile — `The type or namespace name 'Recipes' does not exist in the namespace 'Labs626.UrScore'`.

- [ ] **Step 5: Write `src/Recipes/Recipe.cs`**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// Where a number is and how to read it. Never what happens with it (spec §2): nothing here can
/// name a threshold, a rule, an alert, an account to send, or anything to trigger.
/// </summary>
public sealed record Recipe(
    int Version,
    string Name,
    string Credit,
    string? Author,
    string MetricId,
    string ValueLabel,
    int EverySeconds,
    IReadOnlyList<RecipeInput> Inputs,
    IReadOnlyList<RecipeKey> Keys,
    IReadOnlyList<RecipeStep> Steps,
    IReadOnlyList<RecipeHeadline> Headline)
{
    public const int SupportedVersion = 1;

    /// <summary>Whatever a recipe asks for, Ur Score never polls faster than this (spec §4.4).</summary>
    public const int MinimumEverySeconds = 60;

    public int EffectiveEverySeconds => Math.Max(MinimumEverySeconds, EverySeconds);

    public RecipeStep LastStep => Steps[^1];

    /// <summary>File name and identity. Name plus author, so two people's same-named recipes do not collide.</summary>
    public string Slug => Slugify(Author is null ? Name : $"{Name} {Author}");

    internal static string Slugify(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        var slug = Regex.Replace(builder.ToString(), "-+", "-").Trim('-');
        return slug.Length == 0 ? "recipe" : slug;
    }
}

public sealed record RecipeInput(string Id, string Label, RecipeSearch? Search);

public sealed record RecipeSearch(string Url, string List);

public enum KeyPlacement { Header, Query }

public sealed record RecipeKey(string Id, string Label, string GetOneAt, KeyPlacement In, string Name);

public sealed record RecipeStep(
    string Url,
    IReadOnlyList<string> UseKeys,
    IReadOnlyDictionary<string, string> Take,
    string? IdleWithout,
    string? IdleMessage,
    string? Rows,
    string? UserId,
    bool PerAccount,
    string? Value);

public sealed record RecipeHeadline(string Label, string Path);
```

- [ ] **Step 6: Write `src/Recipes/Placeholders.cs` and `src/Recipes/RecipeHosts.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Labs626.UrScore.Recipes;

/// <summary><c>{name}</c> tokens: inputs, taken variables, and <c>{userId}</c> in a per-account step.</summary>
internal static partial class Placeholders
{
    public const string UserId = "userId";

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex Pattern();

    public static IReadOnlyList<string> Names(string text) =>
        [.. Pattern().Matches(text).Select(m => m.Groups[1].Value)];

    /// <summary>
    /// Percent-encoded when filling a url, as-is when filling a path (spec §3.5). Throws on a name
    /// with no value: the parser guarantees every placeholder is known, so a miss here is a bug, and
    /// a bug should be loud rather than a request to a half-filled address.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values, bool encode) =>
        Pattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException($"No value for {{{name}}} when filling '{template}'.");
            }

            return encode ? Uri.EscapeDataString(value) : value;
        });
}
```

```csharp
namespace Labs626.UrScore.Recipes;

/// <summary>The literal host of an https url, read from the raw text so it works before placeholders are filled.</summary>
internal static class RecipeHosts
{
    public const string Scheme = "https://";

    public static string HostOf(string url) => Authority(url).Host;

    /// <summary>The authority split into host and whether it carried user info.</summary>
    public static (string Host, bool HasUserInfo) Authority(string url)
    {
        var rest = url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) ? url[Scheme.Length..] : url;
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];

        var at = authority.LastIndexOf('@');
        var hasUserInfo = at >= 0;
        if (hasUserInfo) authority = authority[(at + 1)..];

        var colon = authority.LastIndexOf(':');
        if (colon >= 0) authority = authority[..colon];

        return (authority.ToLowerInvariant(), hasUserInfo);
    }
}
```

- [ ] **Step 7: Write `src/Recipes/RecipeParser.cs`**

```csharp
using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public sealed record RecipeParseResult(Recipe? Recipe, IReadOnlyList<string> Problems)
{
    public bool Ok => Recipe is not null;
}

/// <summary>
/// Recipe text to a <see cref="Recipe"/>, or every problem in it named (spec §6.5). Reads by hand
/// rather than deserializing, because a deserializer's exception names a byte offset and a person
/// sharing a recipe file needs "step 2 has no url".
/// </summary>
public static class RecipeParser
{
    public static RecipeParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            return Fail($"This file is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("A recipe is a JSON object, and this file is not one.");
            }

            // Version first and alone: a newer file's other fields may mean something this build cannot read.
            if (!TryInt(root, "recipe", out var version))
            {
                return Fail("No 'recipe' version number. A recipe file starts with \"recipe\": 1.");
            }

            if (version > Recipe.SupportedVersion)
            {
                return Fail($"This recipe was made by a newer Ur Score (format {version}). Update Ur Score to import it.");
            }

            if (version < 1)
            {
                return Fail($"'recipe' is {version}, and the first format is 1.");
            }

            var problems = new List<string>();
            var name = RequiredString(root, "name", "the recipe", problems);
            var credit = RequiredString(root, "credit", "the recipe", problems);
            var metricId = RequiredString(root, "metricId", "the recipe", problems);
            var author = OptionalString(root, "author");
            var valueLabel = OptionalString(root, "valueLabel") ?? "Value";

            if (!TryInt(root, "everySeconds", out var everySeconds) || everySeconds <= 0)
            {
                problems.Add("'everySeconds' must be a whole number of seconds above zero.");
            }

            var inputs = ParseInputs(root, problems);
            var keys = ParseKeys(root, problems);
            var steps = ParseSteps(root, problems);
            var headline = ParseHeadline(root, problems);

            // Cross-field checks only on a structurally complete recipe, so one missing url does not
            // cascade into three confusing follow-on complaints.
            if (problems.Count == 0)
            {
                Validate(inputs, keys, steps, headline, problems);
            }

            return problems.Count > 0
                ? new RecipeParseResult(null, problems)
                : new RecipeParseResult(
                    new Recipe(version, name!, credit!, author, metricId!, valueLabel, everySeconds,
                        inputs, keys, steps, headline),
                    []);
        }
    }

    private static RecipeParseResult Fail(string problem) => new(null, [problem]);

    private static bool TryInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        return JsonNav.TryGet(obj, name, out var element)
               && element.ValueKind == JsonValueKind.Number
               && element.TryGetInt32(out value);
    }

    private static string? OptionalString(JsonElement obj, string name) =>
        JsonNav.TryGet(obj, name, out var element)
        && element.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;

    private static string? RequiredString(JsonElement obj, string name, string where, List<string> problems)
    {
        var value = OptionalString(obj, name);
        if (value is null) problems.Add($"{Capitalize(where)} has no '{name}'.");
        return value;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static IEnumerable<(int Number, JsonElement Item)> Items(JsonElement root, string name, List<string> problems)
    {
        if (!JsonNav.TryGet(root, name, out var array) || array.ValueKind == JsonValueKind.Null) yield break;

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"'{name}' must be a list.");
            yield break;
        }

        var number = 0;
        foreach (var item in array.EnumerateArray())
        {
            yield return (++number, item);
        }
    }

    private static void RequireHttps(string url, string what, List<string> problems)
    {
        if (!url.StartsWith(RecipeHosts.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{Capitalize(what)} must start with https://.");
            return;
        }

        var (host, hasUserInfo) = RecipeHosts.Authority(url);
        if (hasUserInfo)
        {
            problems.Add($"{Capitalize(what)} must not carry a username or password.");
        }

        if (host.Length == 0 || host.Contains('{') || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            problems.Add($"{Capitalize(what)} must name its host directly, not through a placeholder.");
        }
    }

    private static List<RecipeInput> ParseInputs(JsonElement root, List<string> problems)
    {
        var inputs = new List<RecipeInput>();
        foreach (var (number, item) in Items(root, "inputs", problems))
        {
            var where = $"input {number}";
            var id = RequiredString(item, "id", where, problems);
            var label = RequiredString(item, "label", where, problems);

            RecipeSearch? search = null;
            if (JsonNav.TryGet(item, "search", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                var url = RequiredString(s, "url", $"{where}'s search", problems);
                var list = RequiredString(s, "list", $"{where}'s search", problems);
                if (url is not null) RequireHttps(url, $"{where}'s search url", problems);
                if (url is not null && list is not null) search = new RecipeSearch(url, list);
            }

            if (id is not null && label is not null) inputs.Add(new RecipeInput(id, label, search));
        }

        return inputs;
    }

    private static List<RecipeKey> ParseKeys(JsonElement root, List<string> problems)
    {
        var keys = new List<RecipeKey>();
        foreach (var (number, item) in Items(root, "keys", problems))
        {
            var where = $"key {number}";
            var id = RequiredString(item, "id", where, problems);
            var label = RequiredString(item, "label", where, problems);
            var getOneAt = RequiredString(item, "getOneAt", where, problems);
            var placement = RequiredString(item, "in", where, problems);
            var headerOrParameter = RequiredString(item, "name", where, problems);

            if (getOneAt is not null) RequireHttps(getOneAt, $"{where}'s getOneAt", problems);

            KeyPlacement? parsed = placement?.ToLowerInvariant() switch
            {
                "header" => KeyPlacement.Header,
                "query" => KeyPlacement.Query,
                _ => null,
            };

            if (placement is not null && parsed is null)
            {
                problems.Add($"{Capitalize(where)}'s 'in' is '{placement}'. It must be header or query.");
            }

            if (id is not null && label is not null && getOneAt is not null && parsed is not null && headerOrParameter is not null)
            {
                keys.Add(new RecipeKey(id, label, getOneAt, parsed.Value, headerOrParameter));
            }
        }

        return keys;
    }

    private static List<RecipeStep> ParseSteps(JsonElement root, List<string> problems)
    {
        var steps = new List<RecipeStep>();
        if (!JsonNav.TryGet(root, "steps", out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0)
        {
            problems.Add("The recipe has no steps. It needs at least one request to make.");
            return steps;
        }

        var number = 0;
        foreach (var item in array.EnumerateArray())
        {
            var where = $"step {++number}";
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{Capitalize(where)} is not an object.");
                continue;
            }

            var url = RequiredString(item, "url", where, problems);
            if (url is not null) RequireHttps(url, $"{where}'s url", problems);

            var useKeys = new List<string>();
            if (JsonNav.TryGet(item, "useKeys", out var keyList) && keyList.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keyList.EnumerateArray())
                {
                    if (key.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString()))
                    {
                        useKeys.Add(key.GetString()!.Trim());
                    }
                }
            }

            var take = new Dictionary<string, string>(StringComparer.Ordinal);
            if (JsonNav.TryGet(item, "take", out var takeObject) && takeObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in takeObject.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        take[property.Name] = property.Value.GetString()!.Trim();
                    }
                    else
                    {
                        problems.Add($"{Capitalize(where)} takes '{property.Name}' without a path.");
                    }
                }
            }

            var perAccount = JsonNav.TryGet(item, "perAccount", out var perAccountElement)
                             && perAccountElement.ValueKind == JsonValueKind.True;

            if (url is not null)
            {
                steps.Add(new RecipeStep(url, useKeys, take,
                    OptionalString(item, "idleWithout"), OptionalString(item, "idleMessage"),
                    OptionalString(item, "rows"), OptionalString(item, "userId"),
                    perAccount, OptionalString(item, "value")));
            }
        }

        return steps;
    }

    private static List<RecipeHeadline> ParseHeadline(JsonElement root, List<string> problems)
    {
        var headline = new List<RecipeHeadline>();
        foreach (var (number, item) in Items(root, "headline", problems))
        {
            var label = RequiredString(item, "label", $"headline {number}", problems);
            var path = RequiredString(item, "path", $"headline {number}", problems);
            if (label is not null && path is not null) headline.Add(new RecipeHeadline(label, path));
        }

        if (headline.Count > 2)
        {
            problems.Add($"The headline has {headline.Count} values. It shows at most two.");
        }

        return headline;
    }

    private static void Validate(
        List<RecipeInput> inputs, List<RecipeKey> keys, List<RecipeStep> steps,
        List<RecipeHeadline> headline, List<string> problems)
    {
        Duplicates(inputs.Select(i => i.Id), "input id", problems);
        Duplicates(keys.Select(k => k.Id), "key id", problems);

        var inputIds = inputs.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (inputIds.Contains(Placeholders.UserId))
        {
            problems.Add("An input cannot be called 'userId'. That name is reserved for your accounts' Roblox ids.");
        }

        var declaredKeys = keys.GroupBy(k => k.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var keyHosts = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var where = $"step {index + 1}";
            var isLast = index == steps.Count - 1;

            var known = new HashSet<string>(inputIds, StringComparer.Ordinal);
            known.UnionWith(taken);

            var urlNames = Placeholders.Names(step.Url);
            foreach (var name in urlNames)
            {
                if (name == Placeholders.UserId)
                {
                    if (!step.PerAccount) problems.Add($"{{userId}} can only be used in a perAccount step, and {where} is not one.");
                }
                else if (!known.Contains(name))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in {where}'s url.");
                }
            }

            if (step.PerAccount && !urlNames.Contains(Placeholders.UserId))
            {
                problems.Add("A perAccount step's url must contain {userId}, or it asks the same thing once per account.");
            }

            foreach (var (label, path) in PathsOf(step))
            {
                foreach (var name in Placeholders.Names(path).Where(n => !known.Contains(n)))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in {where}'s {label}.");
                }
            }

            foreach (var keyId in step.UseKeys)
            {
                if (!declaredKeys.ContainsKey(keyId))
                {
                    problems.Add($"{Capitalize(where)} uses key '{keyId}', which the recipe does not declare.");
                    continue;
                }

                var host = RecipeHosts.HostOf(step.Url);
                if (keyHosts.TryGetValue(keyId, out var other) && other != host)
                {
                    problems.Add($"Key '{keyId}' is sent to two hosts, {other} and {host}. A key can only go to one.");
                }
                else
                {
                    keyHosts[keyId] = host;
                }
            }

            if (step.IdleWithout is not null && !step.Take.ContainsKey(step.IdleWithout))
            {
                problems.Add($"{Capitalize(where)} is idle without '{step.IdleWithout}', which it does not take.");
            }

            var reads = step.Rows is not null || step.UserId is not null || step.Value is not null || step.PerAccount;
            if (!isLast && reads)
            {
                problems.Add($"{Capitalize(where)} reads a value, but only the last step can.");
            }

            if (isLast)
            {
                var listForm = step.Rows is not null && step.UserId is not null && step.Value is not null && !step.PerAccount;
                var perAccountForm = step.PerAccount && step.Value is not null && step.Rows is null && step.UserId is null;

                if (!listForm && !perAccountForm)
                {
                    problems.Add("The last step needs rows, userId and value, or perAccount and value.");
                }

                if (step.Take.Count > 0)
                {
                    problems.Add("The last step reads the number, so it cannot also take values for later steps.");
                }

                if (headline.Count > 0 && !listForm)
                {
                    problems.Add("A headline can only be read from a list-form last step.");
                }
            }

            foreach (var name in step.Take.Keys)
            {
                if (name == Placeholders.UserId)
                {
                    problems.Add($"{Capitalize(where)} takes 'userId', which is reserved for your accounts' Roblox ids.");
                }
                else if (inputIds.Contains(name))
                {
                    problems.Add($"{Capitalize(where)} takes '{name}', which is already the name of an input.");
                }

                taken.Add(name);
            }
        }

        var allKnown = new HashSet<string>(inputIds, StringComparer.Ordinal);
        allKnown.UnionWith(taken);
        for (var index = 0; index < headline.Count; index++)
        {
            foreach (var name in Placeholders.Names(headline[index].Path).Where(n => !allKnown.Contains(n)))
            {
                problems.Add($"Unknown placeholder {{{name}}} in headline {index + 1}'s path.");
            }
        }

        foreach (var input in inputs.Where(i => i.Search is not null))
        {
            foreach (var name in Placeholders.Names(input.Search!.Url).Concat(Placeholders.Names(input.Search.List)))
            {
                problems.Add($"Input '{input.Id}' searches with a placeholder {{{name}}}. A search list's address must be fixed.");
            }
        }
    }

    private static IEnumerable<(string Label, string Path)> PathsOf(RecipeStep step)
    {
        foreach (var (name, path) in step.Take) yield return ($"take path '{name}'", path);
        if (step.Rows is not null) yield return ("rows", step.Rows);
        if (step.UserId is not null) yield return ("userId", step.UserId);
        if (step.Value is not null) yield return ("value", step.Value);
    }

    private static void Duplicates(IEnumerable<string> ids, string what, List<string> problems)
    {
        foreach (var id in ids.GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"The {what} '{id}' is used more than once.");
        }
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipeParserTests`
Expected: PASS, 29 tests. Then `dotnet test tests/Ur-Score.Tests.csproj`: 156 passed.

- [ ] **Step 9: Commit**

```bash
git add nuget.config tests/Ur-Score.Tests.csproj tests/Fixtures src/Recipes tests/RecipeParserTests.cs
git commit -m "feat(recipes): the recipe model and a parser that names every problem"
```

---

### Task 2: Reading a path, and telling "nothing" from "changed"

**Files:**
- Create: `src/Recipes/RecipePath.cs`, `tests/RecipePathTests.cs`
- Modify: `docs/2026-09-13-recipes-design.md` (§3.2 `idleWithout` row, per Ruling 1)

**Interfaces:**
- Consumes: `JsonNav.TryGet`, `JsonNav.Keys`.
- Produces:
  - `enum PathOutcome { Found, Nothing, Missing }`
  - `readonly record struct PathResult(PathOutcome Outcome, JsonElement Value, string? Miss)`
  - `static class RecipePath`: `PathResult Resolve(JsonElement root, string path, string rootName = "the response")`, `string? AsText(JsonElement element)`

- [ ] **Step 1: Write the failing tests**

`tests/RecipePathTests.cs`:

```csharp
using System.Text.Json;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipePathTests
{
    private static PathResult Resolve(string json, string path, string rootName = "the response")
    {
        // Cloned so the element outlives the document this helper disposes.
        using var document = JsonDocument.Parse(json);
        var result = RecipePath.Resolve(document.RootElement.Clone(), path, rootName);
        return result;
    }

    [Fact]
    public void FindsANestedValue()
    {
        var result = Resolve("""{ "data": { "configName": "B" } }""", "data.configName");
        Assert.Equal(PathOutcome.Found, result.Outcome);
        Assert.Equal("B", RecipePath.AsText(result.Value));
    }

    [Fact]
    public void KeysMatchWhateverTheirCasing() =>
        Assert.Equal(PathOutcome.Found, Resolve("""{ "Data": { "ConfigName": "B" } }""", "data.configName").Outcome);

    [Fact]
    public void ANullParentIsNothingNotAMiss()
    {
        // Pet Sim 99 says "no battle running" as "data": null. That is the source speaking.
        var result = Resolve("""{ "status": "ok", "data": null }""", "data.configName");
        Assert.Equal(PathOutcome.Nothing, result.Outcome);
        Assert.Null(result.Miss);
    }

    [Fact]
    public void ANullLeafIsNothing() =>
        Assert.Equal(PathOutcome.Nothing, Resolve("""{ "data": { "configName": null } }""", "data.configName").Outcome);

    [Fact]
    public void AnEmptyStringIsNothing() =>
        Assert.Equal(PathOutcome.Nothing, Resolve("""{ "data": { "configName": "  " } }""", "data.configName").Outcome);

    [Fact]
    public void AMissingKeyIsAMissNamingTheKeysPresent()
    {
        // A renamed field. Reading this as "nothing" would say "no battle running" forever.
        var result = Resolve("""{ "data": { "name": "B", "category": "x" } }""", "data.configName");
        Assert.Equal(PathOutcome.Missing, result.Outcome);
        Assert.Equal("No 'configName' in 'data'. Keys present: name, category.", result.Miss);
    }

    [Fact]
    public void AMissAtTheTopNamesTheRootByItsGivenName()
    {
        Assert.Equal("No 'UserID' in the response. Keys present: id.", Resolve("""{ "id": 1 }""", "UserID").Miss);
        Assert.Equal("No 'UserID' in this row. Keys present: id.", Resolve("""{ "id": 1 }""", "UserID", "this row").Miss);
    }

    [Fact]
    public void WalkingIntoAListSaysSo()
    {
        var result = Resolve("""{ "data": [1, 2] }""", "data.first");
        Assert.Equal(PathOutcome.Missing, result.Outcome);
        Assert.Equal("'data' is a list, not an object, so 'first' cannot be read from it.", result.Miss);
    }

    [Fact]
    public void AnEmptyObjectSaysItHasNoKeys() =>
        Assert.Equal("No 'x' in 'data'. Keys present: none.", Resolve("""{ "data": {} }""", "data.x").Miss);

    [Theory]
    [InlineData("\"B\"", "B")]
    [InlineData("12", "12")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    public void AsTextKeepsTheValueAsWritten(string literal, string expected)
    {
        using var document = JsonDocument.Parse(literal);
        Assert.Equal(expected, RecipePath.AsText(document.RootElement));
    }

    [Fact]
    public void AsTextRefusesObjects()
    {
        using var document = JsonDocument.Parse("""{ "a": 1 }""");
        Assert.Null(RecipePath.AsText(document.RootElement));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipePathTests`
Expected: FAIL to compile — `The name 'RecipePath' does not exist in the current context`.

- [ ] **Step 3: Write `src/Recipes/RecipePath.cs`**

```csharp
using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public enum PathOutcome
{
    /// <summary>The value is there.</summary>
    Found,

    /// <summary>
    /// A JSON null on the way, or an empty string at the end: the source saying "nothing here right
    /// now". This is what <c>idleWithout</c> treats as idle.
    /// </summary>
    Nothing,

    /// <summary>
    /// A key that is not there, or a step into something that is not an object. The shape changed,
    /// and this must never read as <see cref="Nothing"/> (plan Ruling 1).
    /// </summary>
    Missing,
}

public readonly record struct PathResult(PathOutcome Outcome, JsonElement Value, string? Miss);

/// <summary>
/// Dot-separated keys, matched case-insensitively (spec §3.5). Placeholders are filled by the caller
/// before this sees the path. A key containing a literal dot is not addressable in format 1.
/// </summary>
public static class RecipePath
{
    public static PathResult Resolve(JsonElement root, string path, string rootName = "the response")
    {
        var current = root;
        var walked = new List<string>();

        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Null)
            {
                return new PathResult(PathOutcome.Nothing, default, null);
            }

            if (current.ValueKind != JsonValueKind.Object)
            {
                return new PathResult(PathOutcome.Missing, default,
                    $"{Where(walked, rootName)} is {Describe(current)}, not an object, so '{segment}' cannot be read from it.");
            }

            if (!JsonNav.TryGet(current, segment, out var next))
            {
                var keys = JsonNav.Keys(current);
                return new PathResult(PathOutcome.Missing, default,
                    $"No '{segment}' in {Where(walked, rootName)}. Keys present: "
                    + $"{(keys.Count == 0 ? "none" : string.Join(", ", keys))}.");
            }

            walked.Add(segment);
            current = next;
        }

        if (current.ValueKind == JsonValueKind.Null
            || (current.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(current.GetString())))
        {
            return new PathResult(PathOutcome.Nothing, default, null);
        }

        return new PathResult(PathOutcome.Found, current, null);
    }

    /// <summary>A value as text for use in a later address or path: strings as-is, numbers as written.</summary>
    public static string? AsText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static string Where(List<string> walked, string rootName) =>
        walked.Count == 0 ? rootName : $"'{string.Join('.', walked)}'";

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        _ => element.ValueKind.ToString().ToLowerInvariant(),
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipePathTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Amend the spec row (Ruling 1)**

In `docs/2026-09-13-recipes-design.md`, replace the `idleWithout` table row with:

```markdown
| `idleWithout` | A variable name. If `take` reads a JSON null on its path or an empty string, the recipe is idle, not failing. A key that is not there at all is a changed shape, reported with the keys present, never read as idle. |
```

- [ ] **Step 6: Commit**

```bash
git add src/Recipes/RecipePath.cs tests/RecipePathTests.cs docs/2026-09-13-recipes-design.md
git commit -m "feat(recipes): read a path, and never mistake a changed shape for nothing"
```

---

### Task 3: The transport, the User-Agent, and the redactor

**Files:**
- Create: `src/Source/UrScoreIdentity.cs`, `src/Recipes/Redactor.cs`, `src/Recipes/RecipeTransport.cs`
- Create: `tests/RedactorTests.cs`, `tests/RecipeTransportTests.cs`
- Modify: `src/Source/NameClient.cs` (one line), `src/Source/ClanClient.cs` (one member)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `static class UrScoreIdentity`: `string UserAgent`
  - `sealed class Redactor(Func<IReadOnlyCollection<string>> secrets)`: `const string Mask = "[key hidden]"`, `const int MinimumLength = 6`, `static Redactor None`, `string Redact(string? text)`
  - `record FetchResult(int? Status, string? Body, string? Error)` with `bool Answered`, `bool Succeeded`
  - `interface IRecipeTransport`: `Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string,string> headers, string label, CancellationToken cancellationToken)`
  - `sealed class HttpRecipeTransport(HttpClient http, string? rawDirectory, Redactor redactor) : IRecipeTransport`

- [ ] **Step 1: Write the failing tests**

`tests/RedactorTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RedactorTests
{
    private static Redactor With(params string[] secrets) => new(() => secrets);

    [Fact]
    public void MasksEverySavedValue() =>
        Assert.Equal("GET https://example.com/x?key=[key hidden] failed",
            With("abc123secret").Redact("GET https://example.com/x?key=abc123secret failed"));

    [Fact]
    public void MasksThePercentEncodedFormToo()
    {
        // A query-parameter key sits in the address encoded, so the plain form alone would miss it.
        Assert.Equal("?key=[key hidden]", With("a+b/c=d9").Redact("?key=a%2Bb%2Fc%3Dd9"));
    }

    [Fact]
    public void MasksTheLongestValueFirst() =>
        Assert.Equal("[key hidden]", With("secret", "secret-long").Redact("secret-long"));

    [Fact]
    public void IgnoresValuesTooShortToBeKeys() =>
        Assert.Equal("the cat sat", With("cat").Redact("the cat sat"));

    [Fact]
    public void NullBecomesEmpty() => Assert.Equal("", Redactor.None.Redact(null));
}
```

`tests/RecipeTransportTests.cs`:

```csharp
using System.Net;
using System.Text;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeTransportTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class NeverRespondsHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingHandler(string message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(message);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly Dictionary<string, string> NoHeaders = [];

    [Fact]
    public async Task IdentifiesItselfOnTheActualRequest()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.Contains("UrScore", handler.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task SendsTheHeadersItIsGiven()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), new Dictionary<string, string> { ["x-api-key"] = "k123456" }, "t", CancellationToken.None);

        Assert.Equal("k123456", handler.Requests[0].Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task RefusesPlainHttpWithoutMakingARequest()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "{}"));
        var result = await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("http://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.False(result.Answered);
        Assert.Equal("Refused to contact example.com: recipes may only use https.", result.Error);
    }

    [Fact]
    public async Task AFailingStatusStillCarriesItsBody()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.BadRequest, """{"error":"no such clan"}"""));
        var result = await new HttpRecipeTransport(new HttpClient(handler), null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.True(result.Answered);
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Status);
        Assert.Contains("no such clan", result.Body);
    }

    [Fact]
    public async Task SavesTheRawBodyBeforeTheStatusCheckAndRedacted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-transport-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new StubHandler(_ => Respond(HttpStatusCode.InternalServerError, """{"echo":"abc123secret"}"""));
            await new HttpRecipeTransport(new HttpClient(handler), dir, new Redactor(() => ["abc123secret"]))
                .GetAsync(new Uri("https://example.com/a"), NoHeaders, "petsim-step2", CancellationToken.None);

            var saved = File.ReadAllText(Path.Combine(dir, "petsim-step2.json"));
            Assert.Contains("[key hidden]", saved);
            Assert.DoesNotContain("abc123secret", saved);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ASlowEndpointIsAnErrorNotACancellation()
    {
        var client = new HttpClient(new NeverRespondsHandler()) { Timeout = TimeSpan.FromMilliseconds(50) };
        var result = await new HttpRecipeTransport(client, null, Redactor.None)
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.False(result.Answered);
        Assert.StartsWith("Could not reach example.com:", result.Error);
    }

    [Fact]
    public async Task AStopTheCallerAskedForPropagates()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HttpRecipeTransport(new HttpClient(new NeverRespondsHandler()), null, Redactor.None)
                .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", stop.Token));
    }

    [Fact]
    public async Task ATransportErrorIsRedacted()
    {
        var result = await new HttpRecipeTransport(new HttpClient(new ThrowingHandler("bad url ?key=abc123secret")), null,
                new Redactor(() => ["abc123secret"]))
            .GetAsync(new Uri("https://example.com/a"), NoHeaders, "t", CancellationToken.None);

        Assert.DoesNotContain("abc123secret", result.Error);
        Assert.Contains("[key hidden]", result.Error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "RedactorTests|RecipeTransportTests"`
Expected: FAIL to compile — `The type or namespace name 'Redactor' could not be found`.

- [ ] **Step 3: Write `src/Source/UrScoreIdentity.cs`**

```csharp
using System.Reflection;

namespace Labs626.UrScore.Source;

/// <summary>How Ur Score introduces itself to every service it calls. Polling anonymously is rude.</summary>
public static class UrScoreIdentity
{
    public static string UserAgent { get; } =
        $"UrScore/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"} (RoRoRo plugin)";
}
```

In `src/Source/ClanClient.cs`, replace the `UserAgent` property (the two lines starting `public static string UserAgent { get; } =`) with:

```csharp
    public static string UserAgent => UrScoreIdentity.UserAgent;
```

In `src/Source/NameClient.cs`, replace `request.Headers.UserAgent.ParseAdd(ClanClient.UserAgent);` with:

```csharp
            request.Headers.UserAgent.ParseAdd(UrScoreIdentity.UserAgent);
```

- [ ] **Step 4: Write `src/Recipes/Redactor.cs`**

```csharp
namespace Labs626.UrScore.Recipes;

/// <summary>
/// Removes every saved key value from text before it is shown, saved or copied (spec §7.4). Reads
/// the current values on every call, so a key saved mid-session is masked from then on.
/// </summary>
public sealed class Redactor(Func<IReadOnlyCollection<string>> secrets)
{
    public const string Mask = "[key hidden]";

    /// <summary>
    /// Values shorter than this are not masked, because masking a three-character string would
    /// shred unrelated text. <c>KeyStore.Save</c> refuses keys this short, which is what keeps
    /// "a key never appears in text" true (plan Ruling 3).
    /// </summary>
    public const int MinimumLength = 6;

    public static Redactor None { get; } = new(() => []);

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        foreach (var secret in secrets().Where(s => s.Length >= MinimumLength).OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);

            var encoded = Uri.EscapeDataString(secret);
            if (!string.Equals(encoded, secret, StringComparison.Ordinal))
            {
                text = text.Replace(encoded, Mask, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }
}
```

- [ ] **Step 5: Write `src/Recipes/RecipeTransport.cs`**

```csharp
using System.IO;
using System.Net.Http;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// One GET. <see cref="Body"/> and <see cref="Status"/> are set whenever the server answered,
/// success or not; <see cref="Error"/> only when it never did.
/// </summary>
public sealed record FetchResult(int? Status, string? Body, string? Error)
{
    public bool Answered => Status is not null;

    public bool Succeeded => Status is >= 200 and < 300;
}

/// <summary>The seam the engine is tested against, so no test needs a network.</summary>
public interface IRecipeTransport
{
    Task<FetchResult> GetAsync(
        Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken);
}

/// <summary>
/// The only code in Ur Score that opens a connection for a recipe. Carries over every rule the
/// retired <c>ClanClient</c> learned: a 30-second bound of its own, a stop the caller asked for
/// propagates while our own timeout becomes an error, and the raw body is saved before the status
/// check because a failing body that explains itself is the thing worth having.
/// </summary>
public sealed class HttpRecipeTransport(HttpClient http, string? rawDirectory, Redactor redactor) : IRecipeTransport
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<FetchResult> GetAsync(
        Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
    {
        // The parser refuses anything but https; this is the second gate, on the code that can connect.
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return new FetchResult(null, null, $"Refused to contact {url.Host}: recipes may only use https.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UrScoreIdentity.UserAgent);
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            SaveRaw(label, body);
            return new FetchResult((int)response.StatusCode, body, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FetchResult(null, null, redactor.Redact($"Could not reach {url.Host}: {ex.Message}"));
        }
    }

    /// <summary>One file per label, overwritten each poll, redacted, never transmitted.</summary>
    private void SaveRaw(string label, string body)
    {
        if (rawDirectory is null) return;

        try
        {
            Directory.CreateDirectory(rawDirectory);
            var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-'));
            File.WriteAllText(Path.Combine(rawDirectory, $"{safe}.json"), redactor.Redact(body));
        }
        catch (Exception)
        {
            // Diagnostics must never break the thing they diagnose.
        }
    }
}
```

- [ ] **Step 5b: Update the comment references in `tests/NameClientTests.cs`**

The test at line 204 says reading `ClanClient.UserAgent` directly would pass with the header never set. Replace `ClanClient.UserAgent` in that comment with `UrScoreIdentity.UserAgent`. No assertion changes.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 183 tests (156 + 14 path + 5 redactor + 8 transport).

- [ ] **Step 7: Commit**

```bash
git add src/Source/UrScoreIdentity.cs src/Source/ClanClient.cs src/Source/NameClient.cs src/Recipes/Redactor.cs src/Recipes/RecipeTransport.cs tests/RedactorTests.cs tests/RecipeTransportTests.cs tests/NameClientTests.cs
git commit -m "feat(recipes): an https-only transport that redacts saved keys from everything it keeps"
```

---

### Task 4: The key store

**Files:**
- Create: `src/Recipes/KeyStore.cs`, `tests/KeyStoreTests.cs`
- Modify: `Ur-Score.csproj` (one package)

**Interfaces:**
- Consumes: `Redactor.MinimumLength` (Task 3).
- Produces:
  - `record SavedKey(string Id, string Host, string Value)`
  - `interface IKeyStore`: `SavedKey? Find(string keyId)`, `void Save(string keyId, string host, string value)`, `bool Remove(string keyId)`, `IReadOnlyCollection<string> Values()`
  - `sealed class KeyStore(string path) : IKeyStore` with `static string DefaultPath`

- [ ] **Step 1: Add the DPAPI package**

In `Ur-Score.csproj`, inside the `ItemGroup` holding the other `PackageReference` items, add:

```xml
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.12" />
```

Run: `dotnet restore Ur-Score.csproj`
Expected: restore succeeds.

- [ ] **Step 2: Write the failing tests**

`tests/KeyStoreTests.cs`:

```csharp
using System.Text;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class KeyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-keys-" + Guid.NewGuid().ToString("N"));

    private string KeyFile => Path.Combine(_dir, "keys.dat");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ASavedKeyComesBackBoundToItsHost()
    {
        new KeyStore(KeyFile).Save("tracker", "API.Tracker.example", "abc123secret");

        var found = new KeyStore(KeyFile).Find("tracker");

        Assert.NotNull(found);
        Assert.Equal("api.tracker.example", found!.Host);
        Assert.Equal("abc123secret", found.Value);
    }

    [Fact]
    public void TheFileOnDiskDoesNotContainTheKey()
    {
        new KeyStore(KeyFile).Save("tracker", "api.tracker.example", "abc123secret");

        var bytes = File.ReadAllBytes(KeyFile);
        Assert.DoesNotContain("abc123secret", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("tracker", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void SavingTheSameKeyForItsOwnHostReplacesTheValue()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "first-value");
        store.Save("tracker", "api.tracker.example", "second-value");

        Assert.Equal("second-value", store.Find("tracker")!.Value);
        Assert.Single(store.Values());
    }

    [Fact]
    public void AKeyIsNeverSilentlyRebound()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "abc123secret");

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save("tracker", "evil.example", "abc123secret"));
        Assert.Equal("Your 'tracker' key is saved for api.tracker.example. Remove it before saving one for evil.example.", ex.Message);
        Assert.Equal("api.tracker.example", store.Find("tracker")!.Host);
    }

    [Fact]
    public void AKeyTooShortToRedactIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => new KeyStore(KeyFile).Save("tracker", "api.tracker.example", " abc "));
        Assert.StartsWith("That key is 3 characters. A real API key is at least 6.", ex.Message);
    }

    [Fact]
    public void RemoveForgetsTheKey()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "abc123secret");

        Assert.True(store.Remove("tracker"));
        Assert.Null(store.Find("tracker"));
        Assert.False(store.Remove("tracker"));
    }

    [Fact]
    public void ValuesListsEveryKeyForTheRedactor()
    {
        var store = new KeyStore(KeyFile);
        store.Save("a", "a.example", "value-one");
        store.Save("b", "b.example", "value-two");

        Assert.Equal(new[] { "value-one", "value-two" }, store.Values().Order().ToArray());
    }

    [Fact]
    public void AnUnreadableFileMeansNoKeysRatherThanACrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(KeyFile, [1, 2, 3, 4, 5]);

        var store = new KeyStore(KeyFile);
        Assert.Null(store.Find("tracker"));
        Assert.Empty(store.Values());
    }

    [Fact]
    public void NoFileMeansNoKeys() => Assert.Empty(new KeyStore(KeyFile).Values());
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter KeyStoreTests`
Expected: FAIL to compile — `The type or namespace name 'KeyStore' could not be found`.

- [ ] **Step 4: Write `src/Recipes/KeyStore.cs`**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Labs626.UrScore.Recipes;

/// <summary>A user's key for one source, bound to the one host it may be sent to (spec §7.2).</summary>
public sealed record SavedKey(string Id, string Host, string Value);

/// <summary>The seam the engine and the import review are tested against.</summary>
public interface IKeyStore
{
    SavedKey? Find(string keyId);

    void Save(string keyId, string host, string value);

    bool Remove(string keyId);

    /// <summary>Every saved value, for <see cref="Redactor"/>. Never shown.</summary>
    IReadOnlyCollection<string> Values();
}

/// <summary>
/// Keys encrypted with Windows DPAPI for the current user, the same protection RoRoRo gives its
/// Discord and phone settings (spec §7.1). Stored by key id, each bound on first save to one host
/// and never rebound silently (plan Ruling 2).
/// </summary>
public sealed class KeyStore(string path) : IKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("626labs.ur-score.keys.v1");

    private readonly Lock _gate = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "keys.dat");

    public SavedKey? Find(string keyId)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
        }
    }

    public void Save(string keyId, string host, string value)
    {
        value = value.Trim();
        if (value.Length < Redactor.MinimumLength)
        {
            throw new ArgumentException(
                $"That key is {value.Length} characters. A real API key is at least {Redactor.MinimumLength}.",
                nameof(value));
        }

        host = host.ToLowerInvariant();

        lock (_gate)
        {
            var keys = Load();
            var existing = keys.FirstOrDefault(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
            if (existing is not null && !string.Equals(existing.Host, host, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Your '{keyId}' key is saved for {existing.Host}. Remove it before saving one for {host}.");
            }

            keys.RemoveAll(k => string.Equals(k.Id, keyId, StringComparison.Ordinal));
            keys.Add(new SavedKey(keyId, host, value));
            Write(keys);
        }
    }

    public bool Remove(string keyId)
    {
        lock (_gate)
        {
            var keys = Load();
            if (keys.RemoveAll(k => string.Equals(k.Id, keyId, StringComparison.Ordinal)) == 0) return false;
            Write(keys);
            return true;
        }
    }

    public IReadOnlyCollection<string> Values()
    {
        lock (_gate)
        {
            return [.. Load().Select(k => k.Value)];
        }
    }

    private List<SavedKey> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<SavedKey>>(plain) ?? [];
        }
        catch (Exception)
        {
            // Another Windows user's file, or corruption. No keys: recipes then report KeyMissing and
            // name the key, which is a remedy. A crash is not.
            return [];
        }
    }

    private void Write(List<SavedKey> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(keys), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 192 tests (183 + 9).

- [ ] **Step 6: Commit**

```bash
git add Ur-Score.csproj src/Recipes/KeyStore.cs tests/KeyStoreTests.cs
git commit -m "feat(recipes): keys encrypted per Windows user and bound to one host"
```

---

### Task 5: The engine

**Files:**
- Create: `src/Recipes/RecipeEngine.cs`, `tests/RecipeEngineTests.cs`

**Interfaces:**
- Consumes: `Recipe`, `RecipeStep`, `RecipeKey`, `KeyPlacement` (Task 1); `Placeholders.Fill`, `Placeholders.Names`, `Placeholders.UserId`, `RecipeHosts.HostOf` (Task 1); `RecipePath.Resolve`, `RecipePath.AsText`, `PathOutcome` (Task 2); `IRecipeTransport`, `FetchResult` (Task 3); `IKeyStore`, `SavedKey` (Task 4); `JsonNav.TryUserId`, `JsonNav.TryNumber`.
- Produces:
  - `enum ReadingOutcome { Read, NeedsInput, Idle, Unreachable, RateLimited, InputNotFound, SignInRequired, KeyMissing, KeyRejected, ShapeNotUnderstood }`
  - `record RecipeRow(long UserId, double Value)`
  - `record HeadlineValue(string Label, string? Text)`
  - `record RecipeReading(ReadingOutcome Outcome, string? Detail, IReadOnlyList<RecipeRow> Rows, IReadOnlyList<HeadlineValue> Headline, string? Context, int RowsSeen)` with `static RecipeReading Stop(ReadingOutcome outcome, string detail)`
  - `interface IRecipeEngine`: `Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string,string> inputs, IReadOnlyCollection<long> accountUserIds, CancellationToken cancellationToken)`
  - `sealed class RecipeEngine(IRecipeTransport transport, IKeyStore keys) : IRecipeEngine`

`Context` is the taken variables as `name=value`, joined with `; ` in step order, or null when nothing was taken. `RecipeWatch` (Task 8) clears remembered values when it changes, the way `ScoreWatch` clears on a new battle.

- [ ] **Step 1: Write the failing tests**

`tests/RecipeEngineTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeEngineTests
{
    private sealed class FakeTransport : IRecipeTransport
    {
        private readonly List<(Func<Uri, bool> Match, FetchResult Result)> _routes = [];

        public List<(Uri Url, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

        public FakeTransport On(string urlStart, int status, string body)
        {
            _routes.Add((u => u.AbsoluteUri.StartsWith(urlStart, StringComparison.Ordinal), new FetchResult(status, body, null)));
            return this;
        }

        public FakeTransport Unreachable(string urlStart, string error)
        {
            _routes.Add((u => u.AbsoluteUri.StartsWith(urlStart, StringComparison.Ordinal), new FetchResult(null, null, error)));
            return this;
        }

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            Requests.Add((url, new Dictionary<string, string>(headers)));
            var route = _routes.FirstOrDefault(r => r.Match(url));
            return Task.FromResult(route.Result ?? new FetchResult(404, "{}", null));
        }
    }

    private sealed class FakeKeys(params SavedKey[] saved) : IKeyStore
    {
        public SavedKey? Find(string keyId) => saved.FirstOrDefault(k => k.Id == keyId);
        public void Save(string keyId, string host, string value) => throw new NotSupportedException();
        public bool Remove(string keyId) => throw new NotSupportedException();
        public IReadOnlyCollection<string> Values() => [.. saved.Select(k => k.Value)];
    }

    private static Recipe PetSim => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Followers => RecipeParser.Parse(RecipeParserTests.Fixture("roblox-followers.recipe.json")).Recipe!;

    private static Recipe Parse(string json)
    {
        var result = RecipeParser.Parse(json);
        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        return result.Recipe!;
    }

    private static readonly Dictionary<string, string> Clan = new() { ["clan"] = "Noodle Clan" };

    private const string Battle = """{ "status": "ok", "data": { "configName": "B" } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": { "B": {
            "Place": 3, "Points": 999,
            "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ]
        } } } }
        """;

    private static Task<RecipeReading> Read(FakeTransport transport, Recipe recipe,
        IReadOnlyDictionary<string, string>? inputs = null, IReadOnlyCollection<long>? ids = null, FakeKeys? keys = null) =>
        new RecipeEngine(transport, keys ?? new FakeKeys())
            .ReadAsync(recipe, inputs ?? Clan, ids ?? [], CancellationToken.None);

    [Fact]
    public async Task ThePetSimRecipeReadsEveryContribution()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { new RecipeRow(111, 4200), new RecipeRow(222, 10) }, reading.Rows);
        Assert.Equal(2, reading.RowsSeen);
        Assert.Equal("battle=B", reading.Context);
        Assert.Equal("Clan place", reading.Headline[0].Label);
        Assert.Equal("3", reading.Headline[0].Text);
        Assert.Equal("999", reading.Headline[1].Text);
    }

    [Fact]
    public async Task NoBattleIsIdleWithTheRecipesOwnWordsAndAsksNothingMore()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "status": "ok", "data": null }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Idle, reading.Outcome);
        Assert.Equal("No clan battle running", reading.Detail);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AnEmptyBattleNameIsIdleToo()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "configName": "" } }""");
        Assert.Equal(ReadingOutcome.Idle, (await Read(transport, PetSim)).Outcome);
    }

    [Fact]
    public async Task ARenamedFieldIsAShapeMissNeverIdle()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "name": "B" } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("Step 1: No 'configName' in 'data'. Keys present: name.", reading.Detail);
    }

    [Fact]
    public async Task AMissingInputAsksForItAndMakesNoRequest()
    {
        var transport = new FakeTransport();

        var reading = await Read(transport, PetSim, inputs: new Dictionary<string, string>());

        Assert.Equal(ReadingOutcome.NeedsInput, reading.Outcome);
        Assert.Equal("Set Your clan to start.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task InputsAreEncodedIntoAddressesButTakenValuesAreUsedAsIsInPaths()
    {
        // A slash changes a request's shape if it is not escaped. A space in a battle name must
        // match the JSON key exactly.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, """{ "data": { "configName": "Big Battle" } }""")
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "Big Battle": { "PointContributions": [ { "UserID": 111, "Points": 5 } ] } } } }""");

        var reading = await Read(transport, PetSim, inputs: new Dictionary<string, string> { ["clan"] = "a/b" });

        Assert.Equal("https://ps99.biggamesapi.io/api/clan/a%2Fb", transport.Requests[1].Url.AbsoluteUri);
        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
    }

    [Fact]
    public async Task OneUnreadableRowCostsOnlyItself()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "UserID": "x", "Points": 1 }, { "UserID": 111, "Points": 5 } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(new[] { new RecipeRow(111, 5) }, reading.Rows);
        Assert.Equal(2, reading.RowsSeen);
    }

    [Fact]
    public async Task WhenNoRowCanBeReadTheFirstReasonIsKept()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "Name": "a" }, { "UserID": 1, "Points": "lots" } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.Equal("None of the 2 rows could be read: No 'UserID' in this row. Keys present: Name.", reading.Detail);
    }

    [Fact]
    public async Task TextWhereANumberBelongsSaysSo()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200,
                """{ "data": { "Battles": { "B": { "PointContributions": [ { "UserID": 1, "Points": "lots" } ] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal("None of the 1 rows could be read: 'Points' is text in this row, not a number.", reading.Detail);
    }

    [Fact]
    public async Task AnEmptyListIsAReadingWithNoRows()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, """{ "data": { "Battles": { "B": { "PointContributions": [] } } } }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Rows);
    }

    [Fact]
    public async Task APerAccountRecipeAsksOncePerAccountInTurn()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 200, """{ "count": 17 }""")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1, 2]);

        Assert.Equal(new[] { "https://friends.roblox.com/v1/users/1/followers/count", "https://friends.roblox.com/v1/users/2/followers/count" },
            transport.Requests.Select(r => r.Url.AbsoluteUri));
        Assert.Equal(new[] { new RecipeRow(1, 17), new RecipeRow(2, 4) }, reading.Rows);
        Assert.Null(reading.Context);
    }

    [Fact]
    public async Task ANotFoundForOneAccountCostsOnlyThatAccount()
    {
        var transport = new FakeTransport()
            .On("https://friends.roblox.com/v1/users/1/", 404, "{}")
            .On("https://friends.roblox.com/v1/users/2/", 200, """{ "count": 4 }""");

        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1, 2]);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new[] { new RecipeRow(2, 4) }, reading.Rows);
        Assert.Equal("1 of your accounts could not be read: friends.roblox.com has nothing for user id 1.", reading.Detail);
    }

    [Fact]
    public async Task NoAccountsMeansAReadingWithNoRowsAndNoRequest()
    {
        var transport = new FakeTransport();
        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: []);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AnUnansweredRequestIsUnreachableWithItsError()
    {
        var transport = new FakeTransport().Unreachable("https://ps99.biggamesapi.io/", "Could not reach ps99.biggamesapi.io: DNS failure");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Unreachable, reading.Outcome);
        Assert.Equal("Could not reach ps99.biggamesapi.io: DNS failure", reading.Detail);
    }

    [Fact]
    public async Task TooManyRequestsIsRateLimited()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 429, "{}");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.RateLimited, reading.Outcome);
        Assert.Equal("ps99.biggamesapi.io asked us to slow down. Trying again next poll.", reading.Detail);
    }

    [Fact]
    public async Task AServerErrorIsUnreachable()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 500, "{}");
        Assert.Equal("ps99.biggamesapi.io returned 500.", (await Read(transport, PetSim)).Detail);
    }

    [Fact]
    public async Task AnUnauthorisedStepWithNoKeyNeedsSigningIn()
    {
        var transport = new FakeTransport().On("https://friends.roblox.com/", 401, "{}");
        var reading = await Read(transport, Followers, inputs: new Dictionary<string, string>(), ids: [1]);

        Assert.Equal(ReadingOutcome.SignInRequired, reading.Outcome);
        Assert.Equal("friends.roblox.com requires signing in, which recipes cannot do.", reading.Detail);
    }

    [Fact]
    public async Task ABadRequestNamingAnInputSaysWhichValueFoundNothing()
    {
        // The live Pet Sim API answers a made-up clan with 400, not 404.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 400, """{ "error": "bad" }""");

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.InputNotFound, reading.Outcome);
        Assert.Equal("ps99.biggamesapi.io found nothing for 'Noodle Clan' (Your clan). Check the spelling.", reading.Detail);
    }

    [Fact]
    public async Task InvalidJsonIsAShapeMiss()
    {
        var transport = new FakeTransport().On("https://ps99.biggamesapi.io/", 200, "<html>");
        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.ShapeNotUnderstood, reading.Outcome);
        Assert.StartsWith("ps99.biggamesapi.io did not return valid JSON:", reading.Detail);
    }

    private static Recipe KeyedRecipe(string placement) => Parse($$"""
        {
          "recipe": 1, "name": "Keyed", "credit": "Test.", "metricId": "k.v", "everySeconds": 60,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://tracker.example/keys", "in": "{{placement}}", "name": "api_key" }],
          "steps": [{ "url": "https://api.tracker.example/rows?season=1", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """);

    [Fact]
    public async Task AMissingKeyNamesItAndWhereToGetOne()
    {
        var transport = new FakeTransport();
        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>());

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("This recipe needs your Tracker key. Get one at tracker.example.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AKeyBoundToAnotherHostIsNeverSent()
    {
        var transport = new FakeTransport();
        var keys = new FakeKeys(new SavedKey("tracker", "somewhere.else.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal(ReadingOutcome.KeyMissing, reading.Outcome);
        Assert.Equal("Your Tracker key is saved for somewhere.else.example, and this recipe would send it to api.tracker.example. It was not sent.", reading.Detail);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AHeaderKeyGoesInTheNamedHeader()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal("abc123secret", transport.Requests[0].Headers["api_key"]);
        Assert.DoesNotContain("abc123secret", transport.Requests[0].Url.AbsoluteUri);
    }

    [Fact]
    public async Task AQueryKeyIsAppendedToTheAddress()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 200, """{ "data": [] }""");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc/123secret"));

        await Read(transport, KeyedRecipe("query"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal("https://api.tracker.example/rows?season=1&api_key=abc%2F123secret", transport.Requests[0].Url.AbsoluteUri);
        Assert.Empty(transport.Requests[0].Headers);
    }

    [Fact]
    public async Task ARejectedKeyNamesTheHostAndTheKey()
    {
        var transport = new FakeTransport().On("https://api.tracker.example/", 403, "{}");
        var keys = new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret"));

        var reading = await Read(transport, KeyedRecipe("header"), inputs: new Dictionary<string, string>(), keys: keys);

        Assert.Equal(ReadingOutcome.KeyRejected, reading.Outcome);
        Assert.Equal("api.tracker.example rejected your Tracker key. Change it to try again.", reading.Detail);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipeEngineTests`
Expected: FAIL to compile — `The type or namespace name 'RecipeEngine' could not be found`.

- [ ] **Step 3: Write `src/Recipes/RecipeEngine.cs`**

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.Recipes;

public enum ReadingOutcome
{
    Read,
    NeedsInput,
    Idle,
    Unreachable,
    RateLimited,
    InputNotFound,
    SignInRequired,
    KeyMissing,
    KeyRejected,
    ShapeNotUnderstood,
}

public sealed record RecipeRow(long UserId, double Value);

public sealed record HeadlineValue(string Label, string? Text);

/// <summary>
/// What one run of a recipe found. <see cref="Rows"/> is every readable row, the user's own and
/// everyone else's; the watch decides which are the user's. <see cref="Context"/> changes when the
/// thing being read changes, such as a new clan battle.
/// </summary>
public sealed record RecipeReading(
    ReadingOutcome Outcome,
    string? Detail,
    IReadOnlyList<RecipeRow> Rows,
    IReadOnlyList<HeadlineValue> Headline,
    string? Context,
    int RowsSeen)
{
    public static RecipeReading Stop(ReadingOutcome outcome, string detail) => new(outcome, detail, [], [], null, 0);
}

/// <summary>The seam <c>RecipeWatch</c> is tested against.</summary>
public interface IRecipeEngine
{
    Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs a recipe's steps in order (spec §4.1). Reads; never decides what happens with what it read.
/// </summary>
public sealed class RecipeEngine(IRecipeTransport transport, IKeyStore keys) : IRecipeEngine
{
    public async Task<RecipeReading> ReadAsync(
        Recipe recipe, IReadOnlyDictionary<string, string> inputs, IReadOnlyCollection<long> accountUserIds,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in recipe.Inputs)
        {
            if (!inputs.TryGetValue(input.Id, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return RecipeReading.Stop(ReadingOutcome.NeedsInput, $"Set {input.Label} to start.");
            }

            values[input.Id] = value.Trim();
        }

        var taken = new List<string>();

        for (var index = 0; index < recipe.Steps.Count; index++)
        {
            var step = recipe.Steps[index];
            var number = index + 1;
            var label = $"{recipe.Slug}-step{number}";
            var isLast = index == recipe.Steps.Count - 1;

            if (isLast && step.PerAccount)
            {
                return await ReadPerAccountAsync(recipe, step, values, accountUserIds, label, Context(values, taken), cancellationToken)
                    .ConfigureAwait(false);
            }

            var (document, stop) = await FetchJsonAsync(recipe, step, values, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null) return stop;

            using (document!)
            {
                if (isLast)
                {
                    return ReadList(recipe, step, document.RootElement, values, number, Context(values, taken));
                }

                foreach (var (name, pathTemplate) in step.Take)
                {
                    var path = Placeholders.Fill(pathTemplate, values, encode: false);
                    var result = RecipePath.Resolve(document.RootElement, path);

                    if (result.Outcome == PathOutcome.Found && RecipePath.AsText(result.Value) is { Length: > 0 } text)
                    {
                        values[name] = text;
                        taken.Add(name);
                        continue;
                    }

                    if (result.Outcome == PathOutcome.Nothing && step.IdleWithout == name)
                    {
                        return RecipeReading.Stop(ReadingOutcome.Idle, step.IdleMessage ?? "Nothing to read right now.");
                    }

                    return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, result.Outcome switch
                    {
                        PathOutcome.Missing => $"Step {number}: {result.Miss}",
                        PathOutcome.Nothing => $"Step {number}: '{path}' was empty.",
                        _ => $"Step {number}: '{path}' is not a value an address can use.",
                    });
                }
            }
        }

        return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, "The recipe has no steps.");
    }

    private async Task<(JsonDocument? Document, RecipeReading? Stop)> FetchJsonAsync(
        Recipe recipe, RecipeStep step, IReadOnlyDictionary<string, string> values, string label,
        CancellationToken cancellationToken)
    {
        var host = RecipeHosts.HostOf(step.Url);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new List<(string Name, string Value)>();

        foreach (var keyId in step.UseKeys)
        {
            var declared = recipe.Keys.First(k => k.Id == keyId);
            var saved = keys.Find(keyId);

            if (saved is null)
            {
                return (null, RecipeReading.Stop(ReadingOutcome.KeyMissing,
                    $"This recipe needs your {declared.Label} key. Get one at {RecipeHosts.HostOf(declared.GetOneAt)}."));
            }

            if (!string.Equals(saved.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                // Spec §7.2: a key is only ever sent to the host it is bound to. Checked here as well
                // as at import, because a recipe file on disk can be edited after it was imported.
                return (null, RecipeReading.Stop(ReadingOutcome.KeyMissing,
                    $"Your {declared.Label} key is saved for {saved.Host}, and this recipe would send it to {host}. It was not sent."));
            }

            if (declared.In == KeyPlacement.Header) headers[declared.Name] = saved.Value;
            else query.Add((declared.Name, saved.Value));
        }

        var address = AppendQuery(Placeholders.Fill(step.Url, values, encode: true), query);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"The address for {host} is not valid once filled in."));
        }

        var fetched = await transport.GetAsync(uri, headers, label, cancellationToken).ConfigureAwait(false);

        var stop = Classify(recipe, step, fetched, host, values);
        if (stop is not null) return (null, stop);

        try
        {
            return (JsonDocument.Parse(fetched.Body ?? ""), null);
        }
        catch (JsonException ex)
        {
            return (null, RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"{host} did not return valid JSON: {ex.Message}"));
        }
    }

    /// <summary>Spec §4.3, one branch per row of its table.</summary>
    private static RecipeReading? Classify(
        Recipe recipe, RecipeStep step, FetchResult fetched, string host, IReadOnlyDictionary<string, string> values)
    {
        if (!fetched.Answered)
        {
            return RecipeReading.Stop(ReadingOutcome.Unreachable, fetched.Error ?? $"Could not reach {host}.");
        }

        if (fetched.Succeeded) return null;

        var status = fetched.Status!.Value;

        if (status == 429)
        {
            return RecipeReading.Stop(ReadingOutcome.RateLimited, $"{host} asked us to slow down. Trying again next poll.");
        }

        if (status is 401 or 403)
        {
            if (step.UseKeys.Count > 0)
            {
                var labels = string.Join(", ", step.UseKeys.Select(id => recipe.Keys.First(k => k.Id == id).Label));
                return RecipeReading.Stop(ReadingOutcome.KeyRejected, $"{host} rejected your {labels} key. Change it to try again.");
            }

            return RecipeReading.Stop(ReadingOutcome.SignInRequired, $"{host} requires signing in, which recipes cannot do.");
        }

        if (status is 400 or 404)
        {
            // Plan Ruling 5: in a per-account step this is one account's problem, and the caller
            // treats InputNotFound from that step as costing only that account.
            if (step.PerAccount && values.TryGetValue(Placeholders.UserId, out var userId))
            {
                return RecipeReading.Stop(ReadingOutcome.InputNotFound, $"{host} has nothing for user id {userId}.");
            }

            var names = Placeholders.Names(step.Url);
            var input = recipe.Inputs.FirstOrDefault(i => names.Contains(i.Id));
            if (input is not null)
            {
                return RecipeReading.Stop(ReadingOutcome.InputNotFound,
                    $"{host} found nothing for '{values[input.Id]}' ({input.Label}). Check the spelling.");
            }
        }

        return RecipeReading.Stop(ReadingOutcome.Unreachable, $"{host} returned {status}.");
    }

    private static RecipeReading ReadList(
        Recipe recipe, RecipeStep step, JsonElement root, IReadOnlyDictionary<string, string> values, int number, string? context)
    {
        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, rowsPath);

        if (rowsResult.Outcome == PathOutcome.Missing)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: {rowsResult.Miss}");
        }

        if (rowsResult.Outcome == PathOutcome.Nothing || rowsResult.Value.ValueKind != JsonValueKind.Array)
        {
            var what = rowsResult.Outcome == PathOutcome.Nothing ? "empty" : "not a list";
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: '{rowsPath}' is {what}, so there are no rows to read.");
        }

        var userIdPath = Placeholders.Fill(step.UserId!, values, encode: false);
        var valuePath = Placeholders.Fill(step.Value!, values, encode: false);
        var rows = new List<RecipeRow>();
        string? firstProblem = null;
        var total = 0;

        foreach (var row in rowsResult.Value.EnumerateArray())
        {
            total++;
            var problem = ReadRow(row, userIdPath, valuePath, out var parsed);
            if (problem is null) rows.Add(parsed);
            else firstProblem ??= problem;
        }

        if (total > 0 && rows.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of the {total} rows could be read: {firstProblem}");
        }

        var headline = recipe.Headline
            .Select(h =>
            {
                var result = RecipePath.Resolve(root, Placeholders.Fill(h.Path, values, encode: false));
                return new HeadlineValue(h.Label, result.Outcome == PathOutcome.Found ? RecipePath.AsText(result.Value) : null);
            })
            .ToList();

        return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total);
    }

    private static string? ReadRow(JsonElement row, string userIdPath, string valuePath, out RecipeRow parsed)
    {
        parsed = new RecipeRow(0, 0);

        var id = RecipePath.Resolve(row, userIdPath, "this row");
        if (id.Outcome != PathOutcome.Found) return id.Miss ?? $"'{userIdPath}' was empty in this row.";
        if (!JsonNav.TryUserId(id.Value, out var userId)) return $"'{userIdPath}' is not a whole number a user id can be.";

        var value = RecipePath.Resolve(row, valuePath, "this row");
        if (value.Outcome != PathOutcome.Found) return value.Miss ?? $"'{valuePath}' was empty in this row.";
        if (!JsonNav.TryNumber(value.Value, out var number))
        {
            return value.Value.ValueKind == JsonValueKind.String
                ? $"'{valuePath}' is text in this row, not a number."
                : $"'{valuePath}' is not a finite number in this row.";
        }

        parsed = new RecipeRow(userId, number);
        return null;
    }

    private async Task<RecipeReading> ReadPerAccountAsync(
        Recipe recipe, RecipeStep step, Dictionary<string, string> values, IReadOnlyCollection<long> accountUserIds,
        string label, string? context, CancellationToken cancellationToken)
    {
        var ids = accountUserIds.Where(id => id > 0).Distinct().ToList();
        var rows = new List<RecipeRow>();
        string? firstProblem = null;

        // In turn, never all at once: N accounts must not become N concurrent requests (spec §12).
        foreach (var userId in ids)
        {
            var perRequest = new Dictionary<string, string>(values, StringComparer.Ordinal)
            {
                [Placeholders.UserId] = userId.ToString(CultureInfo.InvariantCulture),
            };

            var (document, stop) = await FetchJsonAsync(recipe, step, perRequest, label, cancellationToken).ConfigureAwait(false);
            if (stop is not null)
            {
                if (stop.Outcome == ReadingOutcome.InputNotFound)
                {
                    firstProblem ??= stop.Detail;
                    continue;
                }

                return stop;
            }

            using (document!)
            {
                var valuePath = Placeholders.Fill(step.Value!, values, encode: false);
                var result = RecipePath.Resolve(document.RootElement, valuePath);

                if (result.Outcome != PathOutcome.Found)
                {
                    firstProblem ??= result.Miss ?? $"'{valuePath}' was empty for user id {userId}.";
                    continue;
                }

                if (!JsonNav.TryNumber(result.Value, out var number))
                {
                    firstProblem ??= $"'{valuePath}' is not a finite number for user id {userId}.";
                    continue;
                }

                rows.Add(new RecipeRow(userId, number));
            }
        }

        if (ids.Count > 0 && rows.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of your {ids.Count} accounts could be read: {firstProblem}");
        }

        var detail = rows.Count < ids.Count
            ? $"{ids.Count - rows.Count} of your accounts could not be read: {firstProblem}"
            : null;

        return new RecipeReading(ReadingOutcome.Read, detail, rows, [], context, ids.Count);
    }

    private static string AppendQuery(string url, List<(string Name, string Value)> query)
    {
        if (query.Count == 0) return url;

        var builder = new StringBuilder(url);
        var separator = url.Contains('?') ? '&' : '?';
        foreach (var (name, value) in query)
        {
            builder.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    private static string? Context(IReadOnlyDictionary<string, string> values, List<string> taken) =>
        taken.Count == 0 ? null : string.Join("; ", taken.Select(name => $"{name}={values[name]}"));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 216 tests (192 + 24).

- [ ] **Step 5: Commit**

```bash
git add src/Recipes/RecipeEngine.cs tests/RecipeEngineTests.cs
git commit -m "feat(recipes): an engine that runs a recipe's steps and names every miss"
```

---

### Task 6: The import review

**Files:**
- Create: `src/Recipes/ImportReview.cs`, `tests/ImportReviewTests.cs`

**Interfaces:**
- Consumes: `Recipe`, `Placeholders`, `RecipeHosts` (Task 1); `IKeyStore`, `SavedKey` (Task 4).
- Produces:
  - `record HostContact(string Host, IReadOnlyList<string> Sends)`
  - `record ImportReviewResult(IReadOnlyList<HostContact> Hosts, IReadOnlyList<string> Refusals, IReadOnlyList<string> ReusedKeys)` with `bool CanImport`
  - `record UpdateComparison(bool IsUpdate, bool AsksAgain, IReadOnlyList<string> Changes)`
  - `static class ImportReview`: `const string SendsUserIds`, `const string SendsNothing`, `ImportReviewResult Review(Recipe recipe, IKeyStore keys)`, `UpdateComparison CompareToInstalled(Recipe? installed, Recipe incoming, IKeyStore keys)`

- [ ] **Step 1: Write the failing tests**

`tests/ImportReviewTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ImportReviewTests
{
    private sealed class FakeKeys(params SavedKey[] saved) : IKeyStore
    {
        public SavedKey? Find(string keyId) => saved.FirstOrDefault(k => k.Id == keyId);
        public void Save(string keyId, string host, string value) => throw new NotSupportedException();
        public bool Remove(string keyId) => throw new NotSupportedException();
        public IReadOnlyCollection<string> Values() => [.. saved.Select(k => k.Value)];
    }

    private static Recipe Load(string fixture) => RecipeParser.Parse(RecipeParserTests.Fixture(fixture)).Recipe!;

    private static Recipe Parse(string json) => RecipeParser.Parse(json).Recipe!;

    private const string Keyed = """
        {
          "recipe": 1, "name": "Keyed", "credit": "Test.", "metricId": "k.v", "everySeconds": 60,
          "keys": [{ "id": "tracker", "label": "Tracker", "getOneAt": "https://tracker.example/keys", "in": "header", "name": "x-api-key" }],
          "steps": [{ "url": "https://api.tracker.example/rows", "useKeys": ["tracker"], "rows": "data", "userId": "id", "value": "score" }]
        }
        """;

    [Fact]
    public void PetSimContactsOneHostAndSendsOnlyTheClanYouEnter()
    {
        // A list recipe never sends your user ids anywhere: it finds your rows in what comes back.
        var review = ImportReview.Review(Load("petsim99-clan-battle.recipe.json"), new FakeKeys());

        var host = Assert.Single(review.Hosts);
        Assert.Equal("ps99.biggamesapi.io", host.Host);
        Assert.Equal(new[] { "the value you enter for Your clan" }, host.Sends);
        Assert.True(review.CanImport);
    }

    [Fact]
    public void APerAccountRecipeSaysItSendsYourUserIds()
    {
        var review = ImportReview.Review(Load("roblox-followers.recipe.json"), new FakeKeys());

        var host = Assert.Single(review.Hosts);
        Assert.Equal("friends.roblox.com", host.Host);
        Assert.Equal(new[] { ImportReview.SendsUserIds }, host.Sends);
    }

    [Fact]
    public void AHostThatOnlyServesAListReceivesNothingAboutYou()
    {
        var recipe = Parse("""
            {
              "recipe": 1, "name": "Two hosts", "credit": "Test.", "metricId": "t.v", "everySeconds": 60,
              "inputs": [{ "id": "clan", "label": "Your clan", "search": { "url": "https://lists.example/clans", "list": "data" } }],
              "steps": [{ "url": "https://api.example/clan/{clan}", "rows": "data", "userId": "id", "value": "v" }]
            }
            """);

        var review = ImportReview.Review(recipe, new FakeKeys());

        Assert.Equal(new[] { ImportReview.SendsNothing }, review.Hosts.Single(h => h.Host == "lists.example").Sends);
        Assert.Equal(new[] { "the value you enter for Your clan" }, review.Hosts.Single(h => h.Host == "api.example").Sends);
    }

    [Fact]
    public void AKeyedRecipeNamesTheKeyItSends()
    {
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys());
        Assert.Equal(new[] { "your Tracker key" }, Assert.Single(review.Hosts).Sends);
        Assert.Empty(review.ReusedKeys);
    }

    [Fact]
    public void ASavedKeyForTheSameHostIsReusedAndSaidSo()
    {
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys(new SavedKey("tracker", "api.tracker.example", "abc123secret")));

        Assert.True(review.CanImport);
        Assert.Equal(new[] { "Uses your saved Tracker key for api.tracker.example." }, review.ReusedKeys);
    }

    [Fact]
    public void ASavedKeyBoundElsewhereRefusesTheImportNamingBothHosts()
    {
        // The doctored-copy case: same key id, pointed at a different host.
        var review = ImportReview.Review(Parse(Keyed), new FakeKeys(new SavedKey("tracker", "tracker.real.example", "abc123secret")));

        Assert.False(review.CanImport);
        Assert.Equal(
            "This recipe would send your saved 'tracker' key to api.tracker.example, but that key is saved for tracker.real.example. "
            + "It was not imported. If this really is a different key, remove the saved one first.",
            Assert.Single(review.Refusals));
    }

    [Fact]
    public void AFirstImportIsNotAnUpdateAndAsks()
    {
        var comparison = ImportReview.CompareToInstalled(null, Load("petsim99-clan-battle.recipe.json"), new FakeKeys());
        Assert.False(comparison.IsUpdate);
        Assert.True(comparison.AsksAgain);
    }

    [Fact]
    public void AnUpdateThatContactsTheSameHostsWithTheSameThingsDoesNotAskAgain()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with { EverySeconds = 300 };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.IsUpdate);
        Assert.False(comparison.AsksAgain);
        Assert.Equal(new[] { "Polls every 300s instead of 180s." }, comparison.Changes);
    }

    [Fact]
    public void AnUpdateThatContactsANewHostAsksAgainAndSaysWhatIsNew()
    {
        var installed = Load("petsim99-clan-battle.recipe.json");
        var incoming = installed with
        {
            Steps = [installed.Steps[0], installed.Steps[1] with { Url = "https://mirror.example/api/clan/{clan}" }],
        };

        var comparison = ImportReview.CompareToInstalled(installed, incoming, new FakeKeys());

        Assert.True(comparison.AsksAgain);
        Assert.Contains("New: mirror.example receives the value you enter for Your clan", comparison.Changes);
        Assert.Contains("No longer: ps99.biggamesapi.io receives the value you enter for Your clan", comparison.Changes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter ImportReviewTests`
Expected: FAIL to compile — `The name 'ImportReview' does not exist in the current context`.

- [ ] **Step 3: Write `src/Recipes/ImportReview.cs`**

```csharp
namespace Labs626.UrScore.Recipes;

/// <summary>One host a recipe contacts, and exactly what goes to it (spec §6.2).</summary>
public sealed record HostContact(string Host, IReadOnlyList<string> Sends);

public sealed record ImportReviewResult(
    IReadOnlyList<HostContact> Hosts, IReadOnlyList<string> Refusals, IReadOnlyList<string> ReusedKeys)
{
    public bool CanImport => Refusals.Count == 0;
}

/// <summary>
/// <see cref="AsksAgain"/> is true for a first import, and for an update that changes a host or
/// anything sent (spec §6.3). Any other difference is listed in <see cref="Changes"/> without asking.
/// </summary>
public sealed record UpdateComparison(bool IsUpdate, bool AsksAgain, IReadOnlyList<string> Changes);

/// <summary>
/// What a recipe would do on this PC, worked out before anything runs. Pure: no network, no disk
/// beyond the key lookup, so the safety screen is testable.
/// </summary>
public static class ImportReview
{
    public const string SendsUserIds = "your accounts' Roblox user ids";

    public const string SendsNothing = "nothing about you";

    public static ImportReviewResult Review(Recipe recipe, IKeyStore keys)
    {
        var sends = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string host, string what)
        {
            if (!sends.TryGetValue(host, out var list)) sends[host] = list = [];
            if (!list.Contains(what)) list.Add(what);
        }

        // A search list is fetched with a fixed address (the parser guarantees it), so it carries
        // nothing of yours.
        foreach (var input in recipe.Inputs.Where(i => i.Search is not null))
        {
            Add(RecipeHosts.HostOf(input.Search!.Url), SendsNothing);
        }

        var refusals = new List<string>();
        var reused = new List<string>();
        var reusedLabels = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in recipe.Steps)
        {
            var host = RecipeHosts.HostOf(step.Url);
            var names = Placeholders.Names(step.Url);

            if (names.Contains(Placeholders.UserId)) Add(host, SendsUserIds);

            foreach (var input in recipe.Inputs.Where(i => names.Contains(i.Id)))
            {
                Add(host, $"the value you enter for {input.Label}");
            }

            foreach (var keyId in step.UseKeys)
            {
                var declared = recipe.Keys.First(k => k.Id == keyId);
                Add(host, $"your {declared.Label} key");

                var saved = keys.Find(keyId);
                if (saved is null) continue;

                if (!string.Equals(saved.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    refusals.Add(
                        $"This recipe would send your saved '{keyId}' key to {host}, but that key is saved for {saved.Host}. "
                        + "It was not imported. If this really is a different key, remove the saved one first.");
                }
                else if (reusedLabels.Add(declared.Label))
                {
                    reused.Add($"Uses your saved {declared.Label} key for {host}.");
                }
            }

            if (!sends.ContainsKey(host)) Add(host, SendsNothing);
        }

        // A host that receives something about you is not also "nothing about you".
        foreach (var list in sends.Values.Where(l => l.Count > 1))
        {
            list.Remove(SendsNothing);
        }

        return new ImportReviewResult([.. sends.Select(kv => new HostContact(kv.Key, kv.Value))], refusals, reused);
    }

    public static UpdateComparison CompareToInstalled(Recipe? installed, Recipe incoming, IKeyStore keys)
    {
        if (installed is null) return new UpdateComparison(false, true, []);

        var before = Flatten(Review(installed, keys));
        var after = Flatten(Review(incoming, keys));

        var added = after.Except(before).ToList();
        var removed = before.Except(after).ToList();

        var changes = new List<string>();
        changes.AddRange(added.Select(x => $"New: {x}"));
        changes.AddRange(removed.Select(x => $"No longer: {x}"));

        if (installed.EffectiveEverySeconds != incoming.EffectiveEverySeconds)
        {
            changes.Add($"Polls every {incoming.EffectiveEverySeconds}s instead of {installed.EffectiveEverySeconds}s.");
        }

        if (!string.Equals(installed.MetricId, incoming.MetricId, StringComparison.Ordinal))
        {
            changes.Add($"Suggests metric id {incoming.MetricId} instead of {installed.MetricId}.");
        }

        return new UpdateComparison(true, added.Count > 0 || removed.Count > 0, changes);
    }

    private static HashSet<string> Flatten(ImportReviewResult review) =>
        [.. review.Hosts.SelectMany(h => h.Sends.Select(s => $"{h.Host} receives {s}"))];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 225 tests (216 + 9).

- [ ] **Step 5: Commit**

```bash
git add src/Recipes/ImportReview.cs tests/ImportReviewTests.cs
git commit -m "feat(recipes): name every host a recipe contacts and what it sends, before it runs"
```

---

### Task 7: The recipe store

**Files:**
- Create: `src/Recipes/RecipeStore.cs`, `tests/RecipeStoreTests.cs`

**Interfaces:**
- Consumes: `Recipe`, `RecipeParser` (Task 1).
- Produces:
  - `record RecipeState(IReadOnlyDictionary<string,string>? Inputs = null, string? MetricIdOverride = null, IReadOnlyList<string>? ExcludedAccountIds = null)` with `IReadOnlyDictionary<string,string> InputValues`, `IReadOnlySet<Guid> Excluded`, `string MetricIdFor(Recipe recipe)`
  - `record InstalledRecipe(Recipe Recipe, string Text, RecipeState State)`
  - `record RecipeStoreLoad(IReadOnlyList<InstalledRecipe> Recipes, IReadOnlyList<string> Problems)`
  - `sealed class RecipeStore(string directory)` with `static string DefaultDirectory`, `RecipeStoreLoad LoadAll()`, `InstalledRecipe? Find(string slug)`, `void Save(Recipe recipe, string text, RecipeState state)`, `void SaveState(Recipe recipe, RecipeState state)`, `bool Remove(string slug)`

The recipe file is stored as the exact text imported, so a later update comparison and part 3's export work from what the author wrote. Choices the user makes live in a separate state file, never in the recipe.

- [ ] **Step 1: Write the failing tests**

`tests/RecipeStoreTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-recipes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string PetSimText => RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe PetSim => RecipeParser.Parse(PetSimText).Recipe!;

    [Fact]
    public void ASavedRecipeComesBackWithItsTextAndState()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState(new Dictionary<string, string> { ["clan"] = "Noodle Clan" }));

        var installed = Assert.Single(new RecipeStore(_dir).LoadAll().Recipes);

        Assert.Equal(PetSim.Slug, installed.Recipe.Slug);
        Assert.Equal(PetSimText, installed.Text);
        Assert.Equal("Noodle Clan", installed.State.InputValues["clan"]);
    }

    [Fact]
    public void TheRecipeFileIsTheExactTextImported()
    {
        new RecipeStore(_dir).Save(PetSim, PetSimText, new RecipeState());
        Assert.Equal(PetSimText, File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json")));
    }

    [Fact]
    public void ChoicesLiveInTheStateFileNeverTheRecipe()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState(new Dictionary<string, string> { ["clan"] = "Noodle Clan" }));

        Assert.DoesNotContain("Noodle Clan", File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json")));
        Assert.Contains("Noodle Clan", File.ReadAllText(Path.Combine(_dir, $"{PetSim.Slug}.state.json")));
    }

    [Fact]
    public void SaveStateLeavesTheRecipeTextAlone()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState());
        store.SaveState(PetSim, new RecipeState(ExcludedAccountIds: ["9ad5e605-6b41-478c-add3-b916a31a5ab2"]));

        var installed = store.Find(PetSim.Slug)!;
        Assert.Equal(PetSimText, installed.Text);
        Assert.Contains(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), installed.State.Excluded);
    }

    [Fact]
    public void AnInvalidRecipeFileIsNamedAndSkipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.recipe.json"), "{ \"recipe\": 1 }");
        new RecipeStore(_dir).Save(PetSim, PetSimText, new RecipeState());

        var load = new RecipeStore(_dir).LoadAll();

        Assert.Single(load.Recipes);
        Assert.StartsWith("broken.recipe.json: ", Assert.Single(load.Problems));
    }

    [Fact]
    public void AMissingOrUnreadableStateFileMeansDefaultState()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.recipe.json"), PetSimText);
        File.WriteAllText(Path.Combine(_dir, $"{PetSim.Slug}.state.json"), "not json");

        var installed = Assert.Single(new RecipeStore(_dir).LoadAll().Recipes);
        Assert.Empty(installed.State.InputValues);
        Assert.Empty(installed.State.Excluded);
    }

    [Fact]
    public void RubbishExclusionsAreIgnored() =>
        Assert.Equal(new[] { Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2") },
            new RecipeState(ExcludedAccountIds: ["nope", "9ad5e605-6b41-478c-add3-b916a31a5ab2"]).Excluded.ToArray());

    [Fact]
    public void TheMetricIdIsTheRecipesUnlessTheUserChangedIt()
    {
        Assert.Equal("clan.battle.points", new RecipeState().MetricIdFor(PetSim));
        Assert.Equal("my.points", new RecipeState(MetricIdOverride: " my.points ").MetricIdFor(PetSim));
    }

    [Fact]
    public void RemoveDeletesBothFiles()
    {
        var store = new RecipeStore(_dir);
        store.Save(PetSim, PetSimText, new RecipeState());

        Assert.True(store.Remove(PetSim.Slug));
        Assert.Empty(store.LoadAll().Recipes);
        Assert.False(File.Exists(Path.Combine(_dir, $"{PetSim.Slug}.state.json")));
    }

    [Fact]
    public void NoDirectoryMeansNoRecipes() => Assert.Empty(new RecipeStore(_dir).LoadAll().Recipes);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter RecipeStoreTests`
Expected: FAIL to compile — `The type or namespace name 'RecipeStore' could not be found`.

- [ ] **Step 3: Write `src/Recipes/RecipeStore.cs`**

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// What the user chose for one recipe: input values, a metric id if they changed it, and accounts
/// switched off. Never written into the recipe file (spec §3.3).
/// </summary>
public sealed record RecipeState(
    IReadOnlyDictionary<string, string>? Inputs = null,
    string? MetricIdOverride = null,
    IReadOnlyList<string>? ExcludedAccountIds = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> InputValues => Inputs ?? new Dictionary<string, string>();

    /// <summary>An exclude list, so a newly added account is watched by default (carried from Settings).</summary>
    [JsonIgnore]
    public IReadOnlySet<Guid> Excluded => (ExcludedAccountIds ?? [])
        .Select(id => (Parsed: Guid.TryParse(id, out var guid), Id: guid))
        .Where(x => x.Parsed)
        .Select(x => x.Id)
        .ToHashSet();

    public string MetricIdFor(Recipe recipe) =>
        string.IsNullOrWhiteSpace(MetricIdOverride) ? recipe.MetricId : MetricIdOverride.Trim();
}

public sealed record InstalledRecipe(Recipe Recipe, string Text, RecipeState State);

public sealed record RecipeStoreLoad(IReadOnlyList<InstalledRecipe> Recipes, IReadOnlyList<string> Problems);

/// <summary>
/// Installed recipes on disk: <c>{slug}.recipe.json</c> holds the exact text imported, and
/// <c>{slug}.state.json</c> holds the user's choices. Nothing here ever holds a key value.
/// </summary>
public sealed class RecipeStore(string directory)
{
    private const string RecipeSuffix = ".recipe.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "recipes");

    public RecipeStoreLoad LoadAll()
    {
        if (!Directory.Exists(directory)) return new RecipeStoreLoad([], []);

        var recipes = new List<InstalledRecipe>();
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*" + RecipeSuffix).Order(StringComparer.Ordinal))
        {
            var loaded = Load(file, out var problem);
            if (loaded is not null) recipes.Add(loaded);
            else problems.Add($"{Path.GetFileName(file)}: {problem}");
        }

        return new RecipeStoreLoad(recipes, problems);
    }

    public InstalledRecipe? Find(string slug)
    {
        var file = RecipePath(slug);
        return File.Exists(file) ? Load(file, out _) : null;
    }

    public void Save(Recipe recipe, string text, RecipeState state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(RecipePath(recipe.Slug), text);
        SaveState(recipe, state);
    }

    public void SaveState(Recipe recipe, RecipeState state)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StatePath(recipe.Slug), JsonSerializer.Serialize(state, Options));
    }

    public bool Remove(string slug)
    {
        var existed = File.Exists(RecipePath(slug));
        File.Delete(RecipePath(slug));
        File.Delete(StatePath(slug));
        return existed;
    }

    private InstalledRecipe? Load(string file, out string? problem)
    {
        problem = null;
        try
        {
            var text = File.ReadAllText(file);
            var parsed = RecipeParser.Parse(text);
            if (!parsed.Ok)
            {
                problem = string.Join(" ", parsed.Problems);
                return null;
            }

            return new InstalledRecipe(parsed.Recipe!, text, LoadState(parsed.Recipe!.Slug));
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    private RecipeState LoadState(string slug)
    {
        try
        {
            var file = StatePath(slug);
            return File.Exists(file)
                ? JsonSerializer.Deserialize<RecipeState>(File.ReadAllText(file), Options) ?? new RecipeState()
                : new RecipeState();
        }
        catch (Exception)
        {
            // A hand-edited state file that no longer parses costs the choices, not the recipe.
            return new RecipeState();
        }
    }

    private string RecipePath(string slug) => Path.Combine(directory, slug + RecipeSuffix);

    private string StatePath(string slug) => Path.Combine(directory, slug + ".state.json");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 235 tests (225 + 10).

- [ ] **Step 5: Commit**

```bash
git add src/Recipes/RecipeStore.cs tests/RecipeStoreTests.cs
git commit -m "feat(recipes): installed recipes on disk, with the user's choices kept apart from the file"
```

---

### Task 8: `RecipeWatch`, the loop that replaces `ScoreWatch`

**Files:**
- Create: `src/Core/Leaderboard.cs`, `src/Core/RecipeWatch.cs`, `tests/LeaderboardTests.cs`, `tests/RecipeWatchTests.cs`
- Modify: `src/Core/WatchState.cs` (add seven states), `src/Core/ScoreWatch.cs` (one member delegates)

**Interfaces:**
- Consumes: `IRecipeEngine`, `RecipeReading`, `ReadingOutcome`, `RecipeRow`, `HeadlineValue` (Task 5); `IKeyStore` (Task 4); `Recipe` (Task 1); existing `IHostClient`, `ReportPolicy`, `AccountMap`, `HostAccount`, `AccountLine`.
- Produces:
  - `WatchState` gains `NeedsInput`, `InputNotFound`, `SourceIdle`, `RateLimited`, `SignInRequired`, `KeyMissing`, `KeyRejected`. The old `Idle`, `ClanNotFound` and `NoBattle` stay until Task 11.
  - `record RankedRow(int Position, long UserId, double Value, bool IsMine)`; `static class Leaderboard`: `IReadOnlyList<RankedRow> Rank(IReadOnlyList<RecipeRow> rows, IReadOnlySet<long> mine)`
  - `record RecipeSnapshot(WatchState State, string? Detail, IReadOnlyList<AccountLine> Accounts, IReadOnlyList<HostAccount> Unresolved, int RowsSeen, string? Context = null, IReadOnlyList<RecipeRow>? Rows = null, IReadOnlyList<HeadlineValue>? Headline = null)`
  - `sealed class RecipeWatch(IRecipeEngine engine, IHostClient host, IKeyStore keys, ReportPolicy policy, Recipe recipe, IReadOnlyDictionary<string,string> inputs)` with `ReportPolicy Policy`, `Recipe Recipe`, `void UpdatePolicy(string metricId, IReadOnlySet<Guid> allowedSubjects)`, `void UpdateRecipe(Recipe recipe, IReadOnlyDictionary<string,string> inputs)`, `Task<RecipeSnapshot> RunOnceAsync(CancellationToken)`, `internal static string RejectedMessage(string capability)`

`RecipeWatch` asks the host for accounts before it reads, the reverse of `ScoreWatch`, because a per-account recipe needs the ids to build its requests. A list recipe still reads while the host is down, so the dashboard stays useful.

- [ ] **Step 1: Write the failing tests**

`tests/LeaderboardTests.cs`:

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class LeaderboardTests
{
    [Fact]
    public void RanksByValueHighestFirst()
    {
        var ranked = Leaderboard.Rank([new(111, 10), new(222, 500), new(333, 40)], new HashSet<long>());
        Assert.Equal(new long[] { 222, 333, 111 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void MarksTheUsersOwnAccounts()
    {
        var ranked = Leaderboard.Rank([new(111, 10), new(222, 500)], new HashSet<long> { 111 });
        Assert.True(ranked.Single(r => r.UserId == 111).IsMine);
        Assert.False(ranked.Single(r => r.UserId == 222).IsMine);
    }

    [Fact]
    public void TiedValuesGetDistinctPositionsInAStableOrder()
    {
        // Sharing a position reads as a missing row, and an unstable sort reshuffles every poll.
        var ranked = Leaderboard.Rank([new(333, 500), new(111, 500), new(222, 500)], new HashSet<long>());
        Assert.Equal(new long[] { 111, 222, 333 }, ranked.Select(r => r.UserId).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, ranked.Select(r => r.Position).ToArray());
    }

    [Fact]
    public void NothingRanksToNothing() => Assert.Empty(Leaderboard.Rank([], new HashSet<long>()));
}
```

`tests/RecipeWatchTests.cs`:

```csharp
using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWatchTests
{
    private static readonly Guid Mine = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private static readonly HostAccount MyAccount = new(Mine, 111, "Alt One");

    private static Recipe PetSim => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static readonly Dictionary<string, string> Clan = new() { ["clan"] = "Noodle Clan" };

    private sealed class FakeEngine(Func<RecipeReading> read) : IRecipeEngine
    {
        public Func<RecipeReading> Read { get; set; } = read;

        public int Calls { get; private set; }

        public IReadOnlyCollection<long> LastIds { get; private set; } = [];

        public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, CancellationToken ct)
        {
            Calls++;
            LastIds = accountUserIds;
            return Task.FromResult(Read());
        }
    }

    private sealed class GatedEngine(TaskCompletionSource gate) : IRecipeEngine
    {
        private int _inFlight;

        public int Calls;

        public int MaxInFlight;

        public async Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
            IReadOnlyCollection<long> accountUserIds, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref Calls);
            MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
            if (call == 1) await gate.Task;
            Interlocked.Decrement(ref _inFlight);
            return Reading(("battle=A"), new RecipeRow(111, 1));
        }
    }

    private sealed class FakeHost(bool reachable, IReadOnlyList<HostAccount> accounts) : IHostClient
    {
        public List<(Guid Subject, string MetricId, double Value, DateTimeOffset ObservedAt)> Reported { get; } = [];

        public bool Reachable { get; set; } = reachable;

        public bool DenyAccounts { get; set; }

        public bool DenyReports { get; set; }

        public Task<bool> IsReachableAsync(CancellationToken ct) => Task.FromResult(Reachable);

        public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct) =>
            DenyAccounts ? throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked")) : Task.FromResult(accounts);

        public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken ct)
        {
            if (DenyReports) throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked"));
            Reported.Add((subject, metricId, value, observedAt));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeys : IKeyStore
    {
        public List<string> Saved { get; } = [];
        public SavedKey? Find(string keyId) => null;
        public void Save(string keyId, string host, string value) => Saved.Add(value);
        public bool Remove(string keyId) => false;
        public IReadOnlyCollection<string> Values() => [.. Saved];
    }

    private static RecipeReading Reading(string? context, params RecipeRow[] rows) =>
        new(ReadingOutcome.Read, null, rows, [], context, rows.Length);

    private static RecipeWatch Watch(IRecipeEngine engine, FakeHost host, IEnumerable<Guid>? allowed = null, FakeKeys? keys = null) =>
        new(engine, host, keys ?? new FakeKeys(), new ReportPolicy("clan.battle.points", new HashSet<Guid>(allowed ?? [Mine])), PetSim, Clan);

    [Fact]
    public async Task NeedsInputIsItsOwnStateAndSendsNothing()
    {
        var host = new FakeHost(true, [MyAccount]);
        var snapshot = await Watch(new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.NeedsInput, "Set Your clan to start.")), host)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NeedsInput, snapshot.State);
        Assert.Equal("Set Your clan to start.", snapshot.Detail);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task ReportsOnlyYourOwnAccountsRawAndInUtc()
    {
        var host = new FakeHost(true, [MyAccount]);
        var before = DateTimeOffset.UtcNow;

        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200), new RecipeRow(222, 10))), host)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Reporting, snapshot.State);
        var sent = Assert.Single(host.Reported);
        Assert.Equal((Mine, "clan.battle.points", 4200d), (sent.Subject, sent.MetricId, sent.Value));
        Assert.Equal(TimeSpan.Zero, sent.ObservedAt.Offset);
        Assert.True(sent.ObservedAt >= before);
        Assert.Equal(2, snapshot.Rows!.Count);
    }

    [Fact]
    public async Task SourceIdleIsNotAnErrorAndKeepsTheLastValues()
    {
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running");
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.SourceIdle, snapshot.State);
        Assert.Equal(4200, Assert.Single(snapshot.Accounts).LastValue);
    }

    [Fact]
    public async Task ANewContextClearsRememberedValues()
    {
        // Last battle's points beside this battle's, with nothing saying which is which, is a lie.
        var host = new FakeHost(true, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200)));
        var watch = Watch(engine, host);
        await watch.RunOnceAsync(CancellationToken.None);

        engine.Read = () => Reading("battle=B", new RecipeRow(222, 5));
        var snapshot = await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Empty(snapshot.Accounts);
    }

    [Fact]
    public async Task WhileRoRoRoIsDownNothingIsSentAndNothingIsReplayedAfter()
    {
        var host = new FakeHost(false, [MyAccount]);
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 100)));
        var watch = Watch(engine, host);

        var down = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(WatchState.HostDown, down.State);

        host.Reachable = true;
        engine.Read = () => Reading("battle=A", new RecipeRow(111, 200));
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { 200d }, host.Reported.Select(r => r.Value).ToArray());
    }

    [Fact]
    public async Task DecliningTheAccountsCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyAccounts = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.queries.accounts", snapshot.Detail);
    }

    [Fact]
    public async Task DecliningTheReportCapabilityIsRejectedByName()
    {
        var host = new FakeHost(true, [MyAccount]) { DenyReports = true };
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Rejected, snapshot.State);
        Assert.Contains("host.metrics.report", snapshot.Detail);
    }

    [Fact]
    public async Task NoRowOfYoursIsNoMatches()
    {
        var host = new FakeHost(true, [MyAccount]);
        var snapshot = await Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(222, 1))), host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.NoMatches, snapshot.State);
        Assert.Equal("Read 1 row(s); none of them are your accounts.", snapshot.Detail);
    }

    [Fact]
    public async Task AnAccountOffTheSendListIsDroppedAndCounted()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host, allowed: []);

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Empty(host.Reported);
        Assert.Equal(1, watch.Policy.Dropped);
    }

    [Fact]
    public async Task TheMetricIdSentIsTheOneThePolicyHolds()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1))), host);
        watch.UpdatePolicy("my.points", new HashSet<Guid> { Mine });

        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal("my.points", Assert.Single(host.Reported).MetricId);
    }

    [Fact]
    public async Task OverlappingRunsTakeTurns()
    {
        var gate = new TaskCompletionSource();
        var engine = new GatedEngine(gate);
        var watch = Watch(engine, new FakeHost(true, [MyAccount]));

        var first = watch.RunOnceAsync(CancellationToken.None);
        var second = watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, engine.Calls);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, engine.Calls);
        Assert.Equal(1, engine.MaxInFlight);
    }

    [Fact]
    public async Task ARejectedKeyIsHeldUntilAKeyChanges()
    {
        var keys = new FakeKeys();
        var engine = new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.KeyRejected, "api.tracker.example rejected your Tracker key."));
        var watch = Watch(engine, new FakeHost(true, [MyAccount]), keys: keys);

        await watch.RunOnceAsync(CancellationToken.None);
        var held = await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(WatchState.KeyRejected, held.State);
        Assert.Equal(1, engine.Calls);

        keys.Saved.Add("a-new-key-value");
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task SignInRequiredIsHeldUntilTheRecipeOrInputsChange()
    {
        var engine = new FakeEngine(() => RecipeReading.Stop(ReadingOutcome.SignInRequired, "requires signing in"));
        var watch = Watch(engine, new FakeHost(true, [MyAccount]));

        await watch.RunOnceAsync(CancellationToken.None);
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, engine.Calls);

        watch.UpdateRecipe(PetSim, new Dictionary<string, string> { ["clan"] = "Other Clan" });
        await watch.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, engine.Calls);
    }

    [Fact]
    public async Task TheSameRecipeAndInputsKeepTheRememberedValues()
    {
        var host = new FakeHost(true, [MyAccount]);
        var watch = Watch(new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 4200))), host);
        await watch.RunOnceAsync(CancellationToken.None);

        watch.UpdateRecipe(PetSim, new Dictionary<string, string>(Clan));

        Assert.Single((await watch.RunOnceAsync(CancellationToken.None)).Accounts);
    }

    [Fact]
    public async Task OnlyResolvedUserIdsReachTheEngineAndUnresolvedAccountsAreNamed()
    {
        var waiting = new HostAccount(Guid.NewGuid(), 0, "New Alt");
        var engine = new FakeEngine(() => Reading("battle=A", new RecipeRow(111, 1)));

        var snapshot = await Watch(engine, new FakeHost(true, [MyAccount, waiting])).RunOnceAsync(CancellationToken.None);

        Assert.Equal(new long[] { 111 }, engine.LastIds.ToArray());
        Assert.Equal(WatchState.Reporting, snapshot.State);
        Assert.Equal("New Alt", Assert.Single(snapshot.Unresolved).DisplayName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter "LeaderboardTests|RecipeWatchTests"`
Expected: FAIL to compile — `The name 'Leaderboard' does not exist in the current context`.

- [ ] **Step 3: Add the new states to `src/Core/WatchState.cs`**

Inside `public enum WatchState`, after the `Rejected` member, add:

```csharp
    /// <summary>The recipe has an input the user has not filled in. Nothing is polled.</summary>
    NeedsInput,

    /// <summary>The source says a value the user entered, such as a clan name, matches nothing. Waiting will not fix it.</summary>
    InputNotFound,

    /// <summary>The source says there is nothing to read right now, such as no clan battle running. Normal, not an error.</summary>
    SourceIdle,

    /// <summary>The source asked us to slow down. The next poll tries again.</summary>
    RateLimited,

    /// <summary>The source wants a signed-in session, which recipes never have. Held until the recipe or its inputs change.</summary>
    SignInRequired,

    /// <summary>The recipe needs a key that is not saved, or that is saved for a different host.</summary>
    KeyMissing,

    /// <summary>The source refused the saved key. Held until a key changes, so a bad key is not retried every poll.</summary>
    KeyRejected,
```

- [ ] **Step 4: Write `src/Core/Leaderboard.cs`**

```csharp
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>One row of the leaderboard, as the window renders it.</summary>
public sealed record RankedRow(int Position, long UserId, double Value, bool IsMine);

/// <summary>Moved from <c>ClanStanding.Rank</c>, unchanged in behaviour, generalized from points to any value.</summary>
public static class Leaderboard
{
    /// <summary>
    /// Highest first, with the user's own accounts marked. Positions are distinct even on ties, and
    /// ties break on user id so the order is stable across polls.
    /// </summary>
    public static IReadOnlyList<RankedRow> Rank(IReadOnlyList<RecipeRow> rows, IReadOnlySet<long> mine) =>
        [.. rows
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.UserId)
            .Select((r, index) => new RankedRow(index + 1, r.UserId, r.Value, mine.Contains(r.UserId)))];
}
```

- [ ] **Step 5: Write `src/Core/RecipeWatch.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Everything the window renders from one cycle. <see cref="Rows"/> carries every row the recipe
/// read, the user's own and everyone else's, for the leaderboard; only the user's own are ever
/// reported, through <see cref="ReportPolicy"/>.
/// </summary>
public sealed record RecipeSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int RowsSeen,
    string? Context = null,
    IReadOnlyList<RecipeRow>? Rows = null,
    IReadOnlyList<HeadlineValue>? Headline = null);

/// <summary>
/// One cycle: ask RoRoRo for the user's accounts, read the recipe, keep the rows that are the
/// user's, and hand each value to RoRoRo through the report policy. Carries every guarantee
/// <c>ScoreWatch</c> earned: one cycle at a time, raw values in UTC, no backlog when RoRoRo returns,
/// and a named capability when consent is declined.
/// </summary>
public sealed class RecipeWatch(
    IRecipeEngine engine,
    IHostClient host,
    IKeyStore keys,
    ReportPolicy policy,
    Recipe recipe,
    IReadOnlyDictionary<string, string> inputs)
{
    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>A timer tick and a Test now click must never both report the same observation.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _context;

    /// <summary>A stop that retrying cannot fix, and what would release it (plan Ruling 6).</summary>
    private (RecipeSnapshot Snapshot, string KeyFingerprint)? _held;

    public ReportPolicy Policy => policy;

    public Recipe Recipe => recipe;

    public void UpdatePolicy(string metricId, IReadOnlySet<Guid> allowedSubjects)
    {
        policy = policy.With(metricId, allowedSubjects);
    }

    /// <summary>
    /// A different recipe or different inputs mean every remembered value belongs to something else,
    /// so they are cleared. The same recipe and inputs, reloaded, keep them.
    /// </summary>
    public void UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string, string> newInputs)
    {
        var same = string.Equals(newRecipe.Slug, recipe.Slug, StringComparison.Ordinal)
                   && newInputs.Count == inputs.Count
                   && newInputs.All(kv => inputs.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

        recipe = newRecipe;
        inputs = new Dictionary<string, string>(newInputs, StringComparer.Ordinal);
        _held = null;

        if (!same)
        {
            _lines.Clear();
            _context = null;
        }
    }

    public async Task<RecipeSnapshot> RunOnceAsync(CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunOnceCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<RecipeSnapshot> RunOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (_held is { } held && held.KeyFingerprint == KeyFingerprint())
        {
            return held.Snapshot;
        }

        _held = null;

        // Accounts first: a per-account recipe builds its requests from these ids. Read every cycle,
        // so an account added mid-session is watched without restarting anything.
        IReadOnlyList<HostAccount> accounts = [];
        var hostUp = await host.IsReachableAsync(cancellationToken).ConfigureAwait(false);
        if (hostUp)
        {
            try
            {
                accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);
            }
        }

        var unresolved = AccountMap.Unresolved(accounts);
        var map = AccountMap.Build(accounts);

        var reading = await engine.ReadAsync(recipe, inputs, [.. map.Keys], cancellationToken).ConfigureAwait(false);

        WatchState? stopped = reading.Outcome switch
        {
            ReadingOutcome.NeedsInput => WatchState.NeedsInput,
            ReadingOutcome.Idle => WatchState.SourceIdle,
            ReadingOutcome.Unreachable => WatchState.SourceUnreachable,
            ReadingOutcome.RateLimited => WatchState.RateLimited,
            ReadingOutcome.InputNotFound => WatchState.InputNotFound,
            ReadingOutcome.SignInRequired => WatchState.SignInRequired,
            ReadingOutcome.KeyMissing => WatchState.KeyMissing,
            ReadingOutcome.KeyRejected => WatchState.KeyRejected,
            ReadingOutcome.ShapeNotUnderstood => WatchState.ShapeNotUnderstood,
            _ => null,
        };

        if (stopped is { } state)
        {
            // Idle means nothing is live, so no context is current. The remembered values are the
            // finished thing's final numbers and stay readable until something new replaces them.
            if (reading.Outcome == ReadingOutcome.Idle) _context = null;

            var snapshot = Snapshot(state, reading.Detail, 0, unresolved);
            if (state is WatchState.KeyRejected or WatchState.SignInRequired)
            {
                _held = (snapshot, KeyFingerprint());
            }

            return snapshot;
        }

        if (_context is not null && reading.Context != _context) _lines.Clear();
        _context = reading.Context;

        var seen = reading.RowsSeen;
        var mine = reading.Rows
            .Where(r => map.ContainsKey(r.UserId))
            .Select(r => (Subject: map[r.UserId], r.Value))
            .ToList();

        if (!hostUp)
        {
            // Nothing is fetched from the host and nothing is queued, so there is nothing to replay
            // when it comes back.
            return Snapshot(WatchState.HostDown, "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved, reading);
        }

        if (mine.Count == 0)
        {
            return Snapshot(WatchState.NoMatches, $"Read {seen} row(s); none of them are your accounts.", seen, unresolved, reading);
        }

        var observedAt = DateTimeOffset.UtcNow;

        foreach (var (subject, value) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out.
                var sent = await policy.SendAsync(host, subject, policy.MetricId, value, observedAt, cancellationToken)
                    .ConfigureAwait(false);

                if (sent) Remember(subject, accounts, value, observedAt);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Snapshot(WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading);
            }
        }

        var detail = $"Reporting {mine.Count} of {seen} row(s).";
        if (reading.Detail is not null) detail += " " + reading.Detail;
        return Snapshot(WatchState.Reporting, detail, seen, unresolved, reading);
    }

    /// <summary>
    /// Verified against the running host: the Plugins page's only consent control is Remove. There is
    /// no per-capability re-grant, and an existing consent record is never re-prompted.
    /// </summary>
    internal static string RejectedMessage(string capability) =>
        $"RoRoRo refused this: {capability} is not granted. There is no per-capability re-grant — "
        + "remove Ur Score from RoRoRo's Plugins page and reinstall it to be asked again.";

    private string KeyFingerprint() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\u0001', keys.Values().Order(StringComparer.Ordinal)))));

    private void Remember(Guid subject, IReadOnlyList<HostAccount> accounts, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();
        _lines[subject] = new AccountLine(name, subject, value, at);
    }

    private RecipeSnapshot Snapshot(
        WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved, RecipeReading? reading = null) =>
        new(state, detail, [.. _lines.Values], unresolved, seen, reading?.Context ?? _context, reading?.Rows, reading?.Headline);
}
```

- [ ] **Step 6: Point `ScoreWatch.RejectedMessage` at the new one**

In `src/Core/ScoreWatch.cs`, replace the body of `RejectedMessage` (the expression after `=>`, two lines) so the member reads:

```csharp
    internal static string RejectedMessage(string capability) => RecipeWatch.RejectedMessage(capability);
```

One sentence, one place, until Task 11 deletes `ScoreWatch`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Ur-Score.Tests.csproj`
Expected: PASS, 254 tests (235 + 4 leaderboard + 15 watch).

- [ ] **Step 8: Commit**

```bash
git add src/Core/WatchState.cs src/Core/Leaderboard.cs src/Core/RecipeWatch.cs src/Core/ScoreWatch.cs tests/LeaderboardTests.cs tests/RecipeWatchTests.cs
git commit -m "feat(recipes): RecipeWatch, carrying every guarantee ScoreWatch earned to any recipe"
```

---

### Task 9: The window runs a recipe

**Files:**
- Modify: `src/Core/Settings.cs` (replace), `tests/SettingsTests.cs` (replace)
- Modify: `src/UI/MainWindow.xaml` (five edits), `src/UI/MainWindow.xaml.cs` (replace), `src/Core/ReportPolicy.cs` (doc comment only)
- Delete: `src/Core/ScoreWatch.cs`, `tests/ScoreWatchTests.cs` (Ruling 7: they read the `Settings` members this task removes, and `RecipeWatchTests` already covers every behaviour they guarded)

**Interfaces:**
- Consumes: `RecipeStore`, `InstalledRecipe`, `RecipeState` (Task 7); `KeyStore` (Task 4); `Redactor` (Task 3); `RecipeEngine` (Task 5); `HttpRecipeTransport` (Task 3); `RecipeWatch`, `RecipeSnapshot`, `Leaderboard`, `RankedRow`, `WatchState` (Task 8); `UrScoreIdentity` (Task 3).
- Produces:
  - `record Settings(bool ResolveNames = true, string? ActiveRecipe = null)` with `static Settings Defaults`, `static string DefaultPath`, `static Settings Load(string? path = null)`, `static void Save(Settings settings, string? path = null)`
  - In `MainWindow`: `InstalledRecipe? _active`, `void LoadActiveRecipe()`, `void ReloadActive()`, `void RenderRecipe()`, `string MetricId` — Task 10 calls these.

After this task a recipe placed by hand in `%LOCALAPPDATA%\626labs.ur-score\recipes\` runs. Task 10 adds importing.

- [ ] **Step 1: Replace the settings tests**

Replace all of `tests/SettingsTests.cs` with:

```csharp
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-settings-" + Guid.NewGuid().ToString("N"));

    private string File(string name = "settings.json") => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File(), json);
    }

    [Fact]
    public void DefaultsAreUsableWithoutAFile()
    {
        Assert.True(Settings.Defaults.ResolveNames);
        Assert.Null(Settings.Defaults.ActiveRecipe);
    }

    [Fact]
    public void RoundTripsThroughDisk()
    {
        Settings.Save(new Settings(ResolveNames: false, ActiveRecipe: "pet-sim-99-clan-battle-points"), File());
        Assert.Equal(new Settings(false, "pet-sim-99-clan-battle-points"), Settings.Load(File()));
    }

    [Fact]
    public void SavedJsonUsesCamelCaseKeys()
    {
        Settings.Save(new Settings(false, "x"), File());
        var json = System.IO.File.ReadAllText(File());

        Assert.Contains("\"resolveNames\"", json);
        Assert.Contains("\"activeRecipe\"", json);
        Assert.DoesNotContain("\"ResolveNames\"", json);
    }

    [Fact]
    public void APascalCaseFileStillLoads()
    {
        Write("""{ "ResolveNames": false }""");
        Assert.False(Settings.Load(File()).ResolveNames);
    }

    [Fact]
    public void AFileFromBeforeRecipesStillLoads()
    {
        // Este's own test install wrote this shape on 2026-09-13.
        Write("""{ "clanName": "", "metricId": "clan.battle.points", "pollSeconds": 180, "excludedAccountIds": [], "resolveNames": false }""");

        var loaded = Settings.Load(File());
        Assert.False(loaded.ResolveNames);
        Assert.Null(loaded.ActiveRecipe);
    }

    [Fact]
    public void LoadingWithNoFileWritesTheDefaultsSoThereIsSomethingToEdit()
    {
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
        Assert.True(System.IO.File.Exists(File()));
    }

    [Fact]
    public void AnUnreadableFileYieldsDefaultsRatherThanThrowing()
    {
        Write("{ not json");
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
    }

    [Fact]
    public void AnEmptyObjectYieldsDefaults()
    {
        Write("{}");
        Assert.Equal(Settings.Defaults, Settings.Load(File()));
    }
}
```

- [ ] **Step 2: Replace `src/Core/Settings.cs`**

```csharp
using System.IO;
using System.Text.Json;

namespace Labs626.UrScore.Core;

/// <summary>
/// What the user configures for Ur Score as a whole. Everything about a particular source — its
/// inputs, metric id and which accounts send — lives with that recipe (<c>RecipeState</c>), and
/// thresholds live in RoRoRo, because RoRoRo does the judging.
/// </summary>
public sealed record Settings(bool ResolveNames = true, string? ActiveRecipe = null)
{
    public static Settings Defaults { get; } = new();

    /// <summary>A sibling of RoRoRo's own folder, never inside it.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "settings.json");

    /// <summary>Case-insensitive so a file written by an older PascalCase build still loads.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Settings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;

            if (!File.Exists(file))
            {
                // "Start it once and it creates the file" has to be true.
                Save(Defaults, file);
                return Defaults;
            }

            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Options) ?? Defaults;
        }
        catch (Exception)
        {
            // An unreadable file is not a reason not to start.
            return Defaults;
        }
    }

    public static void Save(Settings settings, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(settings, Options));
    }
}
```

- [ ] **Step 3: Edit `src/UI/MainWindow.xaml`**

Five replacements, each an exact match:

1. Replace `<TextBlock Text="Your clan" FontSize="18" FontWeight="Bold" />` with
   `<TextBlock x:Name="HeadingLine" Text="Ur Score" FontSize="18" FontWeight="Bold" />`
2. Replace `AutomationProperties.Name="Which battle is live"` with `AutomationProperties.Name="What is being read"`, and `AutomationProperties.Name="Your clan's place and total points"` with `AutomationProperties.Name="Headline values"`.
3. Replace `<DataGridTextColumn Header="Points" Binding="{Binding Points}" Width="110" />` with
   `<DataGridTextColumn x:Name="LeaderboardValueColumn" Header="Value" Binding="{Binding Value}" Width="110" />`
4. Replace `<DataGridTextColumn Header="Points" Binding="{Binding Points}" Width="90" IsReadOnly="True" />` with
   `<DataGridTextColumn x:Name="AccountsValueColumn" Header="Value" Binding="{Binding Value}" Width="90" IsReadOnly="True" />`
5. In `RateDisclaimerLine`'s `Text`, replace `from its last two polls of the clan's data.` with `from its last two polls.`

- [ ] **Step 4: Replace `src/UI/MainWindow.xaml.cs`**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Source;

namespace Labs626.UrScore.UI;

/// <summary>
/// Runs the active recipe: the dashboard (headline, leaderboard, your accounts) above, the alert
/// pipeline's diagnostics below. Part 1 runs one recipe; part 2 adds the recipe list.
/// </summary>
public partial class MainWindow : Window
{
    private const string PluginId = "626labs.ur-score";
    private const double DefaultThreshold = 100;
    private const int DefaultWindowMinutes = 10;

    /// <summary>
    /// One row of the accounts grid. Raises change notifications so rows update in place: calling
    /// <c>Items.Refresh()</c> throws while the Send checkbox is mid-edit (F10).
    /// </summary>
    public sealed class Row : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _send = true;
        private string _position = "—";
        private string _value = "—";
        private string _ratePerMinute = "—";
        private string _lastValue = "—";
        private string _lastSent = "—";

        public bool Send { get => _send; set => SetField(ref _send, value); }

        public string DisplayName { get; set; } = "";

        public Guid AccountId { get; set; }

        public long RobloxUserId { get; set; }

        public string Position { get => _position; set => SetField(ref _position, value); }

        public string Value { get => _value; set => SetField(ref _value, value); }

        public string RatePerMinute { get => _ratePerMinute; set => SetField(ref _ratePerMinute, value); }

        public string LastValue { get => _lastValue; set => SetField(ref _lastValue, value); }

        public string LastSent { get => _lastSent; set => SetField(ref _lastSent, value); }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class LeaderboardRow
    {
        public string Position { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Yours { get; set; } = "";
    }

    private readonly ObservableCollection<Row> _rows = [];
    private readonly ObservableCollection<LeaderboardRow> _leaderboardRows = [];
    private readonly HttpClient _http = new();
    private readonly DispatcherTimer _timer = new();
    private readonly HostClient _host = new(PluginId);
    private readonly NameClient _nameClient;
    private readonly RecipeStore _store = new(RecipeStore.DefaultDirectory);
    private readonly KeyStore _keys = new(KeyStore.DefaultPath);
    private readonly Redactor _redactor;
    private readonly List<string> _trail = [];

    /// <summary>This window's own rate samples, cleared when the context changes, never taken from a report.</summary>
    private readonly Dictionary<Guid, PointsSample> _previousSamples = [];

    private string? _lastDashboardContext;
    private IReadOnlyList<string> _storeProblems = [];
    private Settings _settings = Settings.Load();
    private InstalledRecipe? _active;
    private RecipeWatch? _watch;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();
        AccountsGrid.ItemsSource = _rows;
        AccountsGrid.CellEditEnding += OnSendToggled;
        LeaderboardGrid.ItemsSource = _leaderboardRows;

        _nameClient = new NameClient(_http);
        _redactor = new Redactor(() => _keys.Values());
        _timer.Tick += async (_, _) => await CycleAsync();

        LoadActiveRecipe();
        RenderRecipe();
        RenderRule();
        RenderPolicy();
        StateLine.Text = "Not started.";
    }

    private string RawDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "626labs.ur-score", "last-response");

    private string MetricId => _active is null ? "" : _active.State.MetricIdFor(_active.Recipe);

    private HashSet<Guid> CurrentAllowedSubjects() => _rows.Where(r => r.Send).Select(r => r.AccountId).ToHashSet();

    private string Stamp(string text) => $"{DateTimeOffset.UtcNow:O} {_redactor.Redact(text)}";

    /// <summary>The recipe named in settings, else the first installed.</summary>
    private void LoadActiveRecipe()
    {
        var load = _store.LoadAll();
        _storeProblems = load.Problems;
        _active = load.Recipes.FirstOrDefault(r => r.Recipe.Slug == _settings.ActiveRecipe) ?? load.Recipes.FirstOrDefault();

        foreach (var problem in load.Problems)
        {
            _trail.Add(Stamp($"RECIPE FILE SKIPPED: {problem}"));
        }
    }

    /// <summary>Re-reads the active recipe and its state from disk; both files are hand-editable.</summary>
    private void ReloadActive()
    {
        if (_active is null) return;

        var fresh = _store.Find(_active.Recipe.Slug);
        if (fresh is null) return;

        _active = fresh;

        var excluded = _active.State.Excluded;
        foreach (var row in _rows)
        {
            row.Send = !excluded.Contains(row.AccountId);
        }

        _watch?.UpdateRecipe(_active.Recipe, _active.State.InputValues);
        _watch?.UpdatePolicy(MetricId, CurrentAllowedSubjects());
        RenderRecipe();
    }

    private void RenderRecipe()
    {
        if (_active is null)
        {
            HeadingLine.Text = "No recipe yet";
            ClanLine.Text = "Import a recipe to start.";
            ClanDetailLine.Text = "A recipe says where a number is. Ur Score reads it and hands your accounts' values to RoRoRo.";
            AttributionLine.Text = "";
            DetailLine.Text = _storeProblems.Count > 0
                ? _redactor.Redact("Some recipe files could not be read: " + string.Join(" | ", _storeProblems))
                : $"Recipes live in {RecipeStore.DefaultDirectory}.";
            return;
        }

        var recipe = _active.Recipe;
        HeadingLine.Text = recipe.Name;
        ClanLine.Text = "Not started.";
        ClanDetailLine.Text = "";
        AttributionLine.Text = recipe.Credit;
        LeaderboardValueColumn.Header = recipe.ValueLabel;
        AccountsValueColumn.Header = recipe.ValueLabel;
        DetailLine.Text = $"Reads {recipe.Name} when started.";
    }

    /// <summary>
    /// The ONE watch this window uses for a recipe (F2): a fresh watch per cycle would get a fresh
    /// serialization guard, and a timer tick and a Test now click could both report one observation.
    /// </summary>
    private RecipeWatch EnsureWatch(InstalledRecipe active)
    {
        if (_watch is not null) return _watch;

        var engine = new RecipeEngine(new HttpRecipeTransport(_http, RawDirectory, _redactor), _keys);
        var policy = new ReportPolicy(MetricId, CurrentAllowedSubjects());
        return _watch = new RecipeWatch(engine, _host, _keys, policy, active.Recipe, active.State.InputValues);
    }

    /// <summary>
    /// Adds a row for every account RoRoRo has saved. Runs before EVERY cycle (F8), because the
    /// report policy's allow list comes from these rows. Catches its own host calls: a declined
    /// capability returns its message, and any other failure costs the seed, not the cycle.
    /// </summary>
    private async Task<string?> SeedRowsAsync()
    {
        if (!await _host.IsReachableAsync(CancellationToken.None)) return null;

        IReadOnlyList<HostAccount> accounts;
        try
        {
            accounts = await _host.GetAccountsAsync(CancellationToken.None);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        {
            return RecipeWatch.RejectedMessage("host.queries.accounts");
        }
        catch (Exception)
        {
            return null;
        }

        var excluded = _active?.State.Excluded ?? new HashSet<Guid>();

        foreach (var account in accounts)
        {
            var existing = _rows.FirstOrDefault(r => r.AccountId == account.AccountId);
            if (existing is not null)
            {
                existing.RobloxUserId = account.RobloxUserId;
                continue;
            }

            _rows.Add(new Row
            {
                AccountId = account.AccountId,
                DisplayName = account.DisplayName,
                RobloxUserId = account.RobloxUserId,
                Send = !excluded.Contains(account.AccountId),
            });
        }

        _watch?.UpdatePolicy(MetricId, CurrentAllowedSubjects());
        RenderPolicy();
        return null;
    }

    private void OnSendToggled(object? sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_active is null) return;

            try
            {
                var excluded = _rows.Where(r => !r.Send).Select(r => r.AccountId.ToString()).ToList();
                var state = _active.State with { ExcludedAccountIds = excluded };
                _store.SaveState(_active.Recipe, state);
                _active = _active with { State = state };

                _watch?.UpdatePolicy(MetricId, CurrentAllowedSubjects());
                RenderPolicy();
            }
            catch (Exception ex)
            {
                DetailLine.Text = _redactor.Redact($"Could not save that change: {ex.Message}");
            }
        }, DispatcherPriority.Background);
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (!_running && _active is null)
        {
            StateLine.Text = "No recipe to run.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _running = !_running;
        StartStopButton.Content = _running ? "Stop Score Watch" : "Start Score Watch";

        if (!_running)
        {
            _timer.Stop();
            StateLine.Text = "Stopped.";
            return;
        }

        _settings = Settings.Load();
        ReloadActive();

        _timer.Interval = TimeSpan.FromSeconds(_active!.Recipe.EffectiveEverySeconds);
        _timer.Start();

        _ = CycleAsync();
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        if (_active is null)
        {
            StateLine.Text = "No recipe to test.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        TestNowButton.IsEnabled = false;
        try
        {
            await CycleAsync();
        }
        finally
        {
            TestNowButton.IsEnabled = true;
        }
    }

    private async Task CycleAsync()
    {
        var active = _active;
        if (active is null) return;

        try
        {
            var seedProblem = await SeedRowsAsync();

            var snapshot = await EnsureWatch(active).RunOnceAsync(CancellationToken.None);
            Render(snapshot);

            if (seedProblem is not null)
            {
                StateLine.Text = "RoRoRo refused this.";
                DetailLine.Text = seedProblem;
                _trail.Add(Stamp($"SEED REJECTED: {seedProblem}"));
            }

            _trail.Add(Stamp($"{snapshot.State}: {snapshot.Detail}"));
            await RenderDashboardAsync(snapshot);
        }
        catch (Exception ex)
        {
            // The window must never die on a cycle.
            StateLine.Text = "Something unexpected went wrong.";
            DetailLine.Text = _redactor.Redact(ex.Message);
            _trail.Add(Stamp($"EXCEPTION: {ex}"));
        }
    }

    private void Render(RecipeSnapshot snapshot)
    {
        StateLine.Text = snapshot.State switch
        {
            WatchState.NeedsInput => "Waiting for a value to be set.",
            WatchState.SourceUnreachable => "Could not reach the data.",
            WatchState.InputNotFound => "Nothing matched what was entered.",
            WatchState.SourceIdle => "Nothing to read right now.",
            WatchState.ShapeNotUnderstood => "The response was not a shape Ur Score understands.",
            WatchState.NoMatches => "None of your accounts are in what came back.",
            WatchState.Reporting => "Reporting to RoRoRo.",
            WatchState.HostDown => "RoRoRo is not running.",
            WatchState.Rejected => "RoRoRo refused the report.",
            WatchState.RateLimited => "The source asked Ur Score to slow down.",
            WatchState.SignInRequired => "The source wants signing in, which recipes cannot do.",
            WatchState.KeyMissing => "A key is needed.",
            WatchState.KeyRejected => "The source rejected the key.",
            _ => snapshot.State.ToString(),
        };

        DetailLine.Text = _redactor.Redact(snapshot.Detail);

        if (snapshot.Unresolved.Count > 0)
        {
            DetailLine.Text += "  Not watched yet, no Roblox user id resolved: "
                + string.Join(", ", snapshot.Unresolved.Select(a => a.DisplayName));
        }

        foreach (var line in snapshot.Accounts)
        {
            var row = _rows.FirstOrDefault(r => r.AccountId == line.AccountId);
            if (row is null) continue;

            row.LastValue = line.LastValue?.ToString("0.##") ?? "—";
            row.LastSent = line.LastReportedUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        }

        RenderPolicy();
        RenderRule();
    }

    private async Task RenderDashboardAsync(RecipeSnapshot snapshot)
    {
        ClanLine.Text = snapshot.State is WatchState.Reporting or WatchState.NoMatches or WatchState.HostDown
            ? snapshot.Context is not null ? $"Reading {snapshot.Context}" : "Reading live."
            : _redactor.Redact(snapshot.Detail);

        // No fresh rows this cycle: leave the last drawing, beside a state line that says what happened.
        if (snapshot.Rows is null) return;

        if (_lastDashboardContext != snapshot.Context)
        {
            _previousSamples.Clear();
            _lastDashboardContext = snapshot.Context;
        }

        ClanDetailLine.Text = snapshot.Headline is { Count: > 0 } headline
            ? string.Join(" · ", headline.Select(h => $"{h.Label} {FormatNumber(h.Text)}"))
            : "";

        var mine = _rows.Where(r => r.RobloxUserId != 0).Select(r => r.RobloxUserId).ToHashSet();
        var ranked = Leaderboard.Rank(snapshot.Rows, mine);

        await RenderLeaderboardAsync(ranked);
        RenderAccountDashboardRows(ranked, DateTimeOffset.UtcNow);
    }

    private static string FormatNumber(string? text) =>
        text is null ? "unknown"
        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("N0")
        : text;

    /// <summary>Names from Roblox in one batched call, only when resolveNames allows. Your own names are already known.</summary>
    private async Task RenderLeaderboardAsync(IReadOnlyList<RankedRow> ranked)
    {
        var mineNames = _rows.Where(r => r.RobloxUserId != 0)
            .GroupBy(r => r.RobloxUserId)
            .ToDictionary(g => g.Key, g => g.First().DisplayName);

        IReadOnlyDictionary<long, string> resolved = new Dictionary<long, string>();
        if (_settings.ResolveNames)
        {
            var others = ranked.Where(r => !r.IsMine).Select(r => r.UserId).Distinct().ToList();
            if (others.Count > 0)
            {
                resolved = await _nameClient.ResolveAsync(others, CancellationToken.None);
            }
        }

        _leaderboardRows.Clear();
        foreach (var r in ranked)
        {
            _leaderboardRows.Add(new LeaderboardRow
            {
                Position = r.Position.ToString(CultureInfo.CurrentCulture),
                Name = r.IsMine
                    ? mineNames.GetValueOrDefault(r.UserId, $"You ({r.UserId})")
                    : resolved.GetValueOrDefault(r.UserId, $"Member {r.UserId}"),
                Value = r.Value.ToString("N0"),
                Yours = r.IsMine ? "You" : "",
            });
        }
    }

    /// <summary>Position, value and rate per account, from what was read, not what was sent.</summary>
    private void RenderAccountDashboardRows(IReadOnlyList<RankedRow> ranked, DateTimeOffset observedAt)
    {
        var byUserId = ranked.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First());

        foreach (var row in _rows)
        {
            if (row.RobloxUserId == 0 || !byUserId.TryGetValue(row.RobloxUserId, out var r))
            {
                row.Position = "—";
                row.Value = "—";
                continue;
            }

            row.Position = $"#{r.Position}";
            row.Value = r.Value.ToString("N0");

            var current = new PointsSample(r.Value, observedAt);
            var rate = PointsRate.PerMinute(_previousSamples.GetValueOrDefault(row.AccountId), current);
            row.RatePerMinute = rate is double perMinute ? $"{perMinute:+0.#;-0.#;0}/min" : "—";
            _previousSamples[row.AccountId] = current;
        }
    }

    private void RenderPolicy()
    {
        if (_active is null)
        {
            PolicyLine.Text = "No recipe, so nothing is sent to RoRoRo.";
            PolicyCounts.Text = "";
            return;
        }

        var policy = _watch?.Policy ?? new ReportPolicy(MetricId, CurrentAllowedSubjects());
        PolicyLine.Text = policy.Describe(_rows.Count, _settings.ResolveNames);
        PolicyCounts.Text = _watch is null ? "" : $"Sent {_watch.Policy.Sent}, dropped {_watch.Policy.Dropped}.";
    }

    private void RenderRule()
    {
        if (_active is null)
        {
            RuleLine.Text = "Import a recipe first.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        var metricId = MetricId;
        var status = RulesFile.Inspect(null, metricId);
        var recorded = RuleInventory.Recorded(metricId);

        (RuleLine.Text, AddRuleButton.IsEnabled) = status.State switch
        {
            RuleState.NoFile =>
                ("RoRoRo has no rules file yet, so nothing can alert. Adding the rule below creates one.", true),
            RuleState.NoRuleForMetric =>
                ($"RoRoRo has rules, but none for {metricId} — so reports will land and never alert.", true),
            RuleState.OursIntact when recorded is not null && status.Threshold != recorded =>
                ($"Your rule for {metricId} has been changed since Ur Score added it "
                 + $"(now {status.Threshold}, was {recorded}). Left exactly as it is.", false),
            RuleState.OursIntact =>
                ($"Ready: RoRoRo has a rule for {metricId} at {status.Threshold}.", false),
            RuleState.UserOwned =>
                ($"You wrote the rule for {metricId} yourself (threshold {status.Threshold}). Ur Score will not touch it.", false),
            RuleState.OwnedByAnotherPlugin =>
                ($"A rule for {metricId} belongs to {status.Owner}. Left alone.", false),
            RuleState.Unreadable =>
                ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            _ => (status.State.ToString(), false),
        };

        RulePreview.Text = AddRuleButton.IsEnabled
            ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes"
            : "";
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (_active is null) return;

        var metricId = MetricId;
        var preview = RulesFile.Preview(metricId, DefaultThreshold, DefaultWindowMinutes);
        var answer = MessageBox.Show(this,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        try
        {
            if (RulesFile.AddRule(null, metricId, DefaultThreshold, DefaultWindowMinutes))
            {
                RuleInventory.Record(metricId, DefaultThreshold);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not add the rule: {ex.Message}", "Ur Score",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderRule();
    }

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var inputs = _active is null
            ? "(none)"
            : string.Join(", ", _active.State.InputValues.Select(kv => $"{kv.Key}={kv.Value}"));

        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {DateTimeOffset.UtcNow:O}")
            .AppendLine($"recipe={_active?.Recipe.Slug ?? "(none)"} metric={MetricId} "
                + $"poll={_active?.Recipe.EffectiveEverySeconds}s resolveNames={_settings.ResolveNames}")
            .AppendLine($"inputs={inputs}")
            .AppendLine($"host={_host.HostVersion ?? "(not connected)"} reject={_host.RejectReason ?? "(none)"}")
            .AppendLine($"user-agent={UrScoreIdentity.UserAgent}")
            .AppendLine($"raw responses kept in {RawDirectory}")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, _trail.TakeLast(40)))
            .ToString();

        try
        {
            // Redacted as a whole, last, so nothing added above can carry a key out.
            Clipboard.SetText(_redactor.Redact(text));
            DetailLine.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            DetailLine.Text = $"Could not copy diagnostics: {ex.Message}";
        }
    }
}
```

- [ ] **Step 5: Retire `ScoreWatch`**

```bash
git rm src/Core/ScoreWatch.cs tests/ScoreWatchTests.cs
```

In `src/Core/ReportPolicy.cs`, in the doc comment on `With`, replace `<see cref="ScoreWatch"/>` with `<see cref="RecipeWatch"/>`, replace `<c>ScoreWatch</c>` with `<c>RecipeWatch</c>` in the same sentence, and replace `<see cref="ScoreWatch.UpdatePolicy"/> is the only caller.` with `<see cref="RecipeWatch.UpdatePolicy"/> is the only caller.` The code is unchanged.

- [ ] **Step 6: Build and run the tests**

Run: `dotnet build -c Release && dotnet test tests/Ur-Score.Tests.csproj`
Expected: build succeeds with no errors; PASS, 226 tests (254 − 15 old settings tests + 8 new − 21 `ScoreWatchTests`).

- [ ] **Step 7: Smoke the window against a hand-placed recipe**

This step is manual. Close any running Ur Score first (it is single-instance).

```powershell
$dir = Join-Path $env:LOCALAPPDATA '626labs.ur-score\recipes'
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item tests\Fixtures\petsim99-clan-battle.recipe.json $dir
Set-Content (Join-Path $dir 'pet-sim-99-clan-battle-points.state.json') '{ "inputs": { "clan": "<a real clan name>" } }'
dotnet run --project Ur-Score.csproj -c Release
```

Expected: the heading reads "Pet Sim 99 clan battle points", both value columns read "Points", the credit line is the recipe's. Press **Test now**: with RoRoRo running and a battle live, the state reads "Reporting to RoRoRo." or "None of your accounts are in what came back."; with no battle, "Nothing to read right now." and "No clan battle running". Remove the two files afterwards.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Settings.cs tests/SettingsTests.cs src/UI/MainWindow.xaml src/UI/MainWindow.xaml.cs src/Core/ReportPolicy.cs
git commit -m "feat(recipes): the window runs the active recipe instead of a hardcoded clan"
```

---

### Task 10: Importing a recipe, through the safety screen

**Files:**
- Create: `src/UI/ImportWindow.xaml`, `src/UI/ImportWindow.xaml.cs`
- Modify: `src/UI/MainWindow.xaml` (two buttons), `src/UI/MainWindow.xaml.cs` (rule sentence extracted; three handlers added)

**Interfaces:**
- Consumes: `RecipeParser` (Task 1); `ImportReview`, `ImportReviewResult`, `UpdateComparison` (Task 6); `RecipeStore`, `RecipeState`, `InstalledRecipe` (Task 7); from Task 9's `MainWindow`: `_active`, `_store`, `_keys`, `_watch`, `_rows`, `_leaderboardRows`, `_previousSamples`, `_settings`, `_running`, `_timer`, `MetricId`, `CurrentAllowedSubjects()`, `RenderRecipe()`, `RenderRule()`, `RenderPolicy()`, `CycleAsync()`.
- Produces: `ImportWindow(Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing, string ruleSentence, bool settingsOnly = false)` with `IReadOnlyDictionary<string,string> Inputs` and `string? MetricIdOverride`; `MainWindow.Activate(InstalledRecipe installed)`; `MainWindow.RuleSentence(string metricId)`.

The safety screen is UI over logic Task 6 already tests. This task adds no unit tests; Step 6 is a manual check of each path.

- [ ] **Step 1: Add the two buttons to `src/UI/MainWindow.xaml`**

Replace `<StackPanel Grid.Row="11" Orientation="Horizontal">` with:

```xml
        <StackPanel Grid.Row="11" Orientation="Horizontal">
            <Button x:Name="ImportRecipeButton" Content="Import recipe…" Padding="12,6" Margin="0,0,8,0"
                    Click="OnImportRecipeClick" AutomationProperties.Name="Import a recipe file" />
            <Button x:Name="RecipeSettingsButton" Content="Recipe settings…" Padding="12,6" Margin="0,0,16,0"
                    Click="OnRecipeSettingsClick" AutomationProperties.Name="Change this recipe's inputs and metric id" />
```

- [ ] **Step 2: Write `src/UI/ImportWindow.xaml`**

```xml
<Window x:Class="Labs626.UrScore.UI.ImportWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Import recipe" Width="580" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="#0f1f31" Foreground="#f2f6fa">
    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="760">
        <StackPanel Margin="18">
            <TextBlock x:Name="NameLine" FontSize="18" FontWeight="Bold" TextWrapping="Wrap" />
            <TextBlock x:Name="CreditLine" Margin="0,4,0,0" TextWrapping="Wrap" Foreground="#9fb3c8" />
            <TextBlock x:Name="AuthorLine" Margin="0,2,0,0" TextWrapping="Wrap" FontStyle="Italic" Foreground="#9fb3c8" />

            <TextBlock Text="Your PC will contact" FontSize="15" FontWeight="SemiBold" Margin="0,16,0,6" />
            <ItemsControl x:Name="HostsList" AutomationProperties.Name="Every host this recipe contacts and what each receives">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Background="#16293d" CornerRadius="4" Padding="10,6" Margin="0,0,0,6">
                            <StackPanel>
                                <TextBlock Text="{Binding Host}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding SendsText}" TextWrapping="Wrap" Foreground="#9fb3c8" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock x:Name="PollLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ReusedLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ChangesLine" Margin="0,4,0,0" TextWrapping="Wrap" />

            <ItemsControl x:Name="InputsList" Margin="0,14,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,10">
                            <TextBlock Text="{Binding Label}" FontWeight="SemiBold" Margin="0,0,0,4" />
                            <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}" Padding="4"
                                     AutomationProperties.Name="{Binding Label}" />
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <TextBlock Text="Metric id RoRoRo sees" FontWeight="SemiBold" Margin="0,4,0,4" />
            <TextBox x:Name="MetricIdBox" Padding="4" AutomationProperties.Name="Metric id RoRoRo sees" />
            <TextBlock x:Name="RuleLine" Margin="0,4,0,0" TextWrapping="Wrap" Foreground="#9fb3c8" />

            <TextBlock x:Name="RefusalLine" Margin="0,12,0,0" TextWrapping="Wrap" Foreground="#ff8a8a" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                <Button x:Name="ImportButton" Content="Import" Padding="16,6" IsDefault="True" Click="OnImportClick" />
                <Button Content="Cancel" Padding="16,6" Margin="8,0,0,0" IsCancel="True" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Window>
```

- [ ] **Step 3: Write `src/UI/ImportWindow.xaml.cs`**

```csharp
using System.Windows;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2), shown before anything a recipe describes runs: every host it
/// contacts and exactly what each receives. Also part 1's home for a recipe's inputs and metric id,
/// until part 2 moves them into the main window.
/// </summary>
public partial class ImportWindow : Window
{
    public sealed record HostItem(string Host, string SendsText);

    public sealed class InputItem
    {
        public required string Id { get; init; }

        public required string Label { get; init; }

        public string Value { get; set; } = "";
    }

    private readonly Recipe _recipe;
    private readonly List<InputItem> _inputs;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        string ruleSentence, bool settingsOnly = false)
    {
        InitializeComponent();
        _recipe = recipe;

        Title = settingsOnly ? "Recipe settings" : comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, $"Receives {string.Join(", ", h.Sends)}."))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        ReusedLine.Text = string.Join(" ", review.ReusedKeys);
        ChangesLine.Text = comparison.Changes.Count == 0 ? "" : "What changed: " + string.Join(" ", comparison.Changes);

        MetricIdBox.Text = existing?.MetricIdFor(recipe) ?? recipe.MetricId;
        RuleLine.Text = ruleSentence;

        _inputs =
        [
            .. recipe.Inputs.Select(i => new InputItem
            {
                Id = i.Id,
                Label = i.Label,
                Value = existing?.InputValues.GetValueOrDefault(i.Id) ?? "",
            }),
        ];
        InputsList.ItemsSource = _inputs;

        RefusalLine.Text = string.Join(Environment.NewLine, review.Refusals);
        ImportButton.IsEnabled = review.CanImport;
        ImportButton.Content = settingsOnly ? "Save" : comparison.IsUpdate ? "Update" : "Import";
    }

    public IReadOnlyDictionary<string, string> Inputs =>
        _inputs.ToDictionary(i => i.Id, i => i.Value.Trim(), StringComparer.Ordinal);

    /// <summary>Null when the user kept the recipe's suggested metric id.</summary>
    public string? MetricIdOverride { get; private set; }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        // Every declared input must be filled before the recipe runs (spec §3.3).
        var missing = _inputs.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Value));
        if (missing is not null)
        {
            RefusalLine.Text = $"Set {missing.Label} first.";
            return;
        }

        var metricId = MetricIdBox.Text.Trim();
        if (metricId.Length == 0)
        {
            RefusalLine.Text = "The metric id cannot be empty. RoRoRo's rules find the number by it.";
            return;
        }

        MetricIdOverride = string.Equals(metricId, _recipe.MetricId, StringComparison.Ordinal) ? null : metricId;
        DialogResult = true;
    }
}
```

- [ ] **Step 4: Extract the rule sentence in `src/UI/MainWindow.xaml.cs`**

Replace the whole `RenderRule` method from Task 9 with these two methods:

```csharp
    private void RenderRule()
    {
        if (_active is null)
        {
            RuleLine.Text = "Import a recipe first.";
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        (RuleLine.Text, AddRuleButton.IsEnabled) = RuleSentence(MetricId);
        RulePreview.Text = AddRuleButton.IsEnabled
            ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes"
            : "";
    }

    /// <summary>What RoRoRo's rules file holds for a metric id, and whether the helper can add one.</summary>
    private static (string Text, bool CanAdd) RuleSentence(string metricId)
    {
        var status = RulesFile.Inspect(null, metricId);
        var recorded = RuleInventory.Recorded(metricId);

        return status.State switch
        {
            RuleState.NoFile =>
                ("RoRoRo has no rules file yet, so nothing can alert. Adding the rule below creates one.", true),
            RuleState.NoRuleForMetric =>
                ($"RoRoRo has rules, but none for {metricId} — so reports will land and never alert.", true),
            RuleState.OursIntact when recorded is not null && status.Threshold != recorded =>
                ($"Your rule for {metricId} has been changed since Ur Score added it "
                 + $"(now {status.Threshold}, was {recorded}). Left exactly as it is.", false),
            RuleState.OursIntact =>
                ($"Ready: RoRoRo has a rule for {metricId} at {status.Threshold}.", false),
            RuleState.UserOwned =>
                ($"You wrote the rule for {metricId} yourself (threshold {status.Threshold}). Ur Score will not touch it.", false),
            RuleState.OwnedByAnotherPlugin =>
                ($"A rule for {metricId} belongs to {status.Owner}. Left alone.", false),
            RuleState.Unreadable =>
                ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            _ => (status.State.ToString(), false),
        };
    }
```

- [ ] **Step 5: Add the import handlers to `src/UI/MainWindow.xaml.cs`**

Add these three methods to `MainWindow`, after `OnCopyDiagnosticsClick`:

```csharp
    private void OnImportRecipeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a recipe",
            Filter = "Ur Score recipe (*.recipe.json)|*.recipe.json|JSON file (*.json)|*.json",
        };

        if (dialog.ShowDialog(this) != true) return;

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read that file: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parsed = RecipeParser.Parse(text);
        if (!parsed.Ok)
        {
            MessageBox.Show(this,
                "That recipe could not be imported:\n\n" + string.Join("\n", parsed.Problems.Select(p => "• " + p)),
                "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var recipe = parsed.Recipe!;
        var installed = _store.Find(recipe.Slug);

        if (installed is not null && string.Equals(installed.Text, text, StringComparison.Ordinal))
        {
            // Spec §6.3: an identical file imports without asking.
            Activate(installed);
            DetailLine.Text = $"{recipe.Name} is already installed, and is the recipe this window runs.";
            return;
        }

        var review = ImportReview.Review(recipe, _keys);
        var comparison = ImportReview.CompareToInstalled(installed?.Recipe, recipe, _keys);

        try
        {
            if (installed is not null && review.CanImport && !comparison.AsksAgain)
            {
                // An update that contacts the same hosts with the same things: listed, not asked.
                _store.Save(recipe, text, installed.State);
                Activate(_store.Find(recipe.Slug)!);
                DetailLine.Text = $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim();
                return;
            }

            var window = new ImportWindow(recipe, review, comparison, installed?.State, RuleSentence(recipe.MetricId).Text)
            {
                Owner = this,
            };

            if (window.ShowDialog() != true) return;

            var state = (installed?.State ?? new RecipeState()) with
            {
                Inputs = window.Inputs,
                MetricIdOverride = window.MetricIdOverride,
            };

            _store.Save(recipe, text, state);
            Activate(_store.Find(recipe.Slug)!);
            DetailLine.Text = $"Imported {recipe.Name}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save that recipe: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRecipeSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_active is null)
        {
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        var active = _active;
        var window = new ImportWindow(active.Recipe, ImportReview.Review(active.Recipe, _keys),
            new UpdateComparison(false, false, []), active.State, RuleSentence(MetricId).Text, settingsOnly: true)
        {
            Owner = this,
        };

        if (window.ShowDialog() != true) return;

        try
        {
            var state = active.State with { Inputs = window.Inputs, MetricIdOverride = window.MetricIdOverride };
            _store.SaveState(active.Recipe, state);
            Activate(active with { State = state });
            DetailLine.Text = $"Saved settings for {active.Recipe.Name}.";
        }
        catch (Exception ex)
        {
            DetailLine.Text = $"Could not save those settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Makes a recipe the one this window runs. A different recipe clears what the dashboard drew
    /// for the old one; <see cref="RecipeWatch.UpdateRecipe"/> clears remembered values when the
    /// recipe or its inputs changed.
    /// </summary>
    private void Activate(InstalledRecipe installed)
    {
        var switching = !string.Equals(_active?.Recipe.Slug, installed.Recipe.Slug, StringComparison.Ordinal);
        _active = installed;

        _settings = _settings with { ActiveRecipe = installed.Recipe.Slug };
        try
        {
            Settings.Save(_settings);
        }
        catch (Exception)
        {
            // Remembering which recipe was active is a convenience; failing to save it costs only that.
        }

        if (switching)
        {
            _previousSamples.Clear();
            _leaderboardRows.Clear();
            foreach (var row in _rows)
            {
                row.Position = "—";
                row.Value = "—";
                row.RatePerMinute = "—";
            }
        }

        var excluded = installed.State.Excluded;
        foreach (var row in _rows)
        {
            row.Send = !excluded.Contains(row.AccountId);
        }

        _watch?.UpdateRecipe(installed.Recipe, installed.State.InputValues);
        _watch?.UpdatePolicy(MetricId, CurrentAllowedSubjects());

        RenderRecipe();
        RenderRule();
        RenderPolicy();

        if (_running)
        {
            _timer.Interval = TimeSpan.FromSeconds(installed.Recipe.EffectiveEverySeconds);
            _ = CycleAsync();
        }
    }
```

- [ ] **Step 6: Build, test, and walk each import path by hand**

Run: `dotnet build -c Release && dotnet test tests/Ur-Score.Tests.csproj`
Expected: build succeeds; PASS, 226 tests.

Then `dotnet run --project Ur-Score.csproj -c Release`, with no recipes installed (`Remove-Item "$env:LOCALAPPDATA\626labs.ur-score\recipes" -Recurse -ErrorAction SilentlyContinue` first), and check each path:

1. **Invalid file.** Import a copy of the Pet Sim fixture with `"url"` deleted from step 2. Expected: a message listing "Step 2 has no 'url'." and nothing installed.
2. **First import.** Import `tests/Fixtures/petsim99-clan-battle.recipe.json`. Expected: the screen lists one host, `ps99.biggamesapi.io`, receiving "the value you enter for Your clan"; asks every 180 seconds; Import with Your clan empty says "Set Your clan first."; filling it and pressing Import installs it, and the heading reads the recipe name.
3. **Identical file.** Import the same file again. Expected: no screen; the detail line says it is already installed.
4. **Update without new contacts.** Edit a copy to `"everySeconds": 300` and import it. Expected: no screen; the detail line reads "Updated Pet Sim 99 clan battle points. Polls every 300s instead of 180s."
5. **Update with a new host.** Edit a copy so step 2's url uses `https://mirror.example/api/clan/{clan}`. Expected: the screen appears titled "Update recipe", listing "New: mirror.example receives the value you enter for Your clan". Press Cancel.
6. **Recipe settings.** Press Recipe settings…, change the metric id. Expected: the screen titled "Recipe settings" with a Save button; after saving, the report policy line names the new metric id.

- [ ] **Step 7: Commit**

```bash
git add src/UI/ImportWindow.xaml src/UI/ImportWindow.xaml.cs src/UI/MainWindow.xaml src/UI/MainWindow.xaml.cs
git commit -m "feat(recipes): import a recipe through a screen that names every host and what it sends"
```

---

### Task 11: Retire the Pet Sim code, and fence hostnames out of `src/`

**Files:**
- Create: `tests/NoHostnameFenceTests.cs`
- Delete: `src/Source/ClanClient.cs`, `src/Source/ClanParser.cs`, `src/Source/ClanStanding.cs`, `tests/ClanClientTests.cs`, `tests/ClanParserTests.cs`, `tests/ClanStandingTests.cs`
- Modify: `src/Core/WatchState.cs`, `src/Source/JsonNav.cs`, `manifest.json`, `Ur-Score.csproj`, `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: `WatchState` without `Idle`, `ClanNotFound`, `NoBattle`; no `WatchSnapshot`; `JsonNav` without `Unwrap`; the fence.

- [ ] **Step 1: Write the fence test**

`tests/NoHostnameFenceTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace UrScore.Tests;

/// <summary>
/// Spec §2 and §10: hosts live in recipe files, not in Ur Score. One named exemption, the Roblox
/// username lookup, which serves every recipe's leaderboard and is switchable through resolveNames.
/// </summary>
public partial class NoHostnameFenceTests
{
    [GeneratedRegex(@"\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|io|net|org|gg|dev|app)\b")]
    private static partial Regex Hostname();

    private static readonly string NameClient = Path.Combine("Source", "NameClient.cs");

    [Fact]
    public void NoFileInSrcNamesAHostExceptTheUsernameLookup()
    {
        var src = Path.Combine(RepoRoot(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Text: File.ReadAllText(f)))
            .Where(f => !string.Equals(f.Relative, NameClient, StringComparison.Ordinal))
            .SelectMany(f => Hostname().Matches(f.Text).Select(m => $"{f.Relative}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"These files name a host: {string.Join(", ", offenders)}. A source's address belongs in a recipe "
            + "file, so Ur Score itself knows no game and no vendor.");
    }

    [Fact]
    public void TheUsernameLookupNamesOnlyRoblox()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", NameClient));
        var hosts = Hostname().Matches(text).Select(m => m.Value).Distinct().ToList();

        Assert.Equal(new[] { "users.roblox.com" }, hosts);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
```

- [ ] **Step 2: Run the fence to verify it fails**

Run: `dotnet test tests/Ur-Score.Tests.csproj --filter NoHostnameFenceTests`
Expected: FAIL — `These files name a host: Source\ClanClient.cs: ps99.biggamesapi.io, Source\ClanClient.cs: ps99.biggamesapi.io.`

- [ ] **Step 3: Delete the Pet Sim source and its tests**

```bash
git rm src/Source/ClanClient.cs src/Source/ClanParser.cs src/Source/ClanStanding.cs tests/ClanClientTests.cs tests/ClanParserTests.cs tests/ClanStandingTests.cs
```

Their behaviour lives on: request handling in `HttpRecipeTransport` (Task 3), the null-battle line in `RecipePath` (Task 2), the forgiving row reading and first-problem rule in `RecipeEngine` (Task 5), ranking in `Leaderboard` (Task 8), and the Pet Sim flow itself in the recipe fixture.

- [ ] **Step 4: Remove the Pet Sim states and `WatchSnapshot` from `src/Core/WatchState.cs`**

Replace the whole file with:

```csharp
namespace Labs626.UrScore.Core;

/// <summary>
/// What Ur Score is doing, in terms a user can act on. Separate states instead of a shared "error",
/// because causes that look identical from outside need different things done: waiting fixes an
/// unreachable source and a rate limit, never a typo, a missing key or a changed shape.
/// </summary>
public enum WatchState
{
    /// <summary>The source could not be reached, or answered with a server error. Waiting is the remedy.</summary>
    SourceUnreachable,

    /// <summary>The response was not a shape the recipe describes. Someone must look.</summary>
    ShapeNotUnderstood,

    /// <summary>Rows came back, and none of them are the user's accounts.</summary>
    NoMatches,

    /// <summary>Working. At least one account matched and its value went to RoRoRo.</summary>
    Reporting,

    /// <summary>RoRoRo is not running. Reading continues; reporting is held.</summary>
    HostDown,

    /// <summary>RoRoRo refused us: a declined capability, or a rejected handshake.</summary>
    Rejected,

    /// <summary>The recipe has an input the user has not filled in. Nothing is polled.</summary>
    NeedsInput,

    /// <summary>The source says a value the user entered matches nothing. Waiting will not fix it.</summary>
    InputNotFound,

    /// <summary>The source says there is nothing to read right now, such as no clan battle running. Normal.</summary>
    SourceIdle,

    /// <summary>The source asked us to slow down. The next poll tries again.</summary>
    RateLimited,

    /// <summary>The source wants a signed-in session, which recipes never have. Held until the recipe or inputs change.</summary>
    SignInRequired,

    /// <summary>The recipe needs a key that is not saved, or that is saved for a different host.</summary>
    KeyMissing,

    /// <summary>The source refused the saved key. Held until a key changes.</summary>
    KeyRejected,
}

/// <summary>One of the user's accounts, as the window lists it.</summary>
public sealed record AccountLine(
    string DisplayName, Guid AccountId, double? LastValue, DateTimeOffset? LastReportedUtc);
```

- [ ] **Step 5: Remove the unused wrapper logic from `src/Source/JsonNav.cs`**

`Unwrap` guessed at wrapper keys because the Pet Sim shape was unknown. Recipes name their paths exactly, so nothing calls it and its doc no longer describes the file. Delete the `Wrappers` field and the `Unwrap` method (with its doc comment), and replace the class doc comment with:

```csharp
/// <summary>
/// Case-insensitive property lookup and forgiving number reading, shared by the recipe reader and
/// the username lookup. A casing change in a response must not read as a missing field.
/// </summary>
```

Run: `grep -rn "Unwrap" src tests`
Expected: no output.

- [ ] **Step 6: Update the descriptions and banner the README**

In `manifest.json` and in `Ur-Score.csproj`'s `<Description>`, replace `Watches clan battle scores for your own accounts and hands each number to RoRoRo.` with `Reads a number for your own accounts from wherever a recipe says, and hands it to RoRoRo.`

At the very top of `README.md`, above its first heading, add:

```markdown
> **Being rebuilt around recipes (2026-09-13).** Ur Score no longer knows any one game. It reads
> whatever a recipe file describes, and Pet Simulator 99 is one recipe among any number. The setup
> below describes the build before recipes and is rewritten when the new window lands. Design:
> [`docs/2026-09-13-recipes-design.md`](docs/2026-09-13-recipes-design.md).
```

- [ ] **Step 7: Run everything**

Run: `dotnet build -c Release && dotnet test tests/Ur-Score.Tests.csproj`
Expected: build succeeds; PASS, 190 tests (226 − 38 Pet Sim tests + 2 fence tests). `ReportPolicyTests`' own fence still passes.

- [ ] **Step 8: Commit**

```bash
git add -A src tests manifest.json Ur-Score.csproj README.md
git commit -m "refactor(recipes): retire the Pet Sim client; Ur Score's own code names no host"
```

---

### Task 12: Live acceptance — the Pet Sim recipe, imported, reports for your accounts

This task is manual and needs Este: RoRoRo 1.28 running with saved accounts, and a real clan name. It is the exit criterion for part 1 (spec §13).

- [ ] **Step 1: Build the plugin package**

Run: `pwsh -NoProfile -File build/build-plugin.ps1`
Expected: `artifacts/manifest.json`, `artifacts/manifest.sha256`, `artifacts/plugin.zip`.

- [ ] **Step 2: Add the local install server**

RoRoRo's installer takes only https and validates the certificate, so a local install needs a real TLS server. Create `build/serve-local.py`:

```python
"""Serve artifacts/ over https on loopback only, for installing a local build into RoRoRo.

Uses the machine's dotnet development certificate for CN=localhost. Trust it once with
`dotnet dev-certs https --trust` (Windows shows a dialog). Binds ::1 and 127.0.0.1 separately,
never a wildcard, and answers only for the three files RoRoRo's installer asks for. The exported
private key is deleted as soon as the TLS context has loaded it.

    python build/serve-local.py          # then install https://localhost:8443/ in RoRoRo
"""
import http.server
import socket
import ssl
import subprocess
import tempfile
import threading
from pathlib import Path

ARTIFACTS = Path(__file__).resolve().parent.parent / "artifacts"
PORT = 8443
ALLOWED = {"/manifest.json", "/manifest.sha256", "/plugin.zip"}

scratch = Path(tempfile.mkdtemp(prefix="urscore-serve-"))
cert, key = scratch / "devcert.pem", scratch / "devcert.key"
subprocess.run(["dotnet", "dev-certs", "https", "--export-path", str(cert), "--format", "PEM", "--no-password"],
               check=True, capture_output=True)

context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(cert, key)
key.unlink(missing_ok=True)


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(ARTIFACTS), **kwargs)

    def do_GET(self):
        if self.path.split("?", 1)[0] not in ALLOWED:
            self.send_error(404, "Only the three plugin artifacts are served here")
            return
        super().do_GET()


class V6Server(http.server.ThreadingHTTPServer):
    address_family = socket.AF_INET6


def serve(server_class, host):
    server = server_class((host, PORT), Handler)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    print(f"serving https://{host}:{PORT}/ from {ARTIFACTS}", flush=True)
    server.serve_forever()


threads = [threading.Thread(target=serve, args=(http.server.ThreadingHTTPServer, "127.0.0.1"), daemon=True),
           threading.Thread(target=serve, args=(V6Server, "::1"), daemon=True)]
for thread in threads:
    thread.start()
for thread in threads:
    thread.join()
```

Run: `dotnet dev-certs https --check --trust`
Expected: "A trusted certificate was found". If not, run `dotnet dev-certs https --trust` and accept the Windows dialog.

**If RoRoRo was already running when the certificate became trusted, restart RoRoRo from the tray.** A process started before the trust keeps its old trust list and reports "The SSL connection could not be established" (found on 2026-09-13; a fresh process trusts it).

- [ ] **Step 3: Serve it and reinstall in RoRoRo**

Run: `python build/serve-local.py`, and leave it running.

In RoRoRo: **Plugins → Remove** Ur Score, then **Install** `https://localhost:8443/`, and grant both capabilities.

Expected: the install succeeds and Ur Score opens on "No recipe yet".

- [ ] **Step 4: Import and run**

Import `tests/Fixtures/petsim99-clan-battle.recipe.json`, fill in Your clan, press **Import**, then **Start Score Watch**.

Expected, with a battle live: the heading reads the recipe name; the headline shows Clan place and Clan points; the leaderboard lists contributors with your accounts marked; the state reads "Reporting to RoRoRo."; the report policy counts rise. With no battle live: "Nothing to read right now." and "No clan battle running".

- [ ] **Step 5: Confirm nothing leaked**

Open `%LOCALAPPDATA%\626labs.ur-score\recipes\`. Expected: the `.recipe.json` is byte-identical to the fixture, and the `.state.json` holds the clan name and nothing else. Press **Copy diagnostics** and paste it somewhere. Expected: recipe, metric, inputs and trail, and no key.

- [ ] **Step 6: Record the outcome**

Add a dated line to `CHANGELOG.md` naming what was seen, and commit it:

```bash
git add CHANGELOG.md build/serve-local.py
git commit -m "docs: recipes part 1 live acceptance, and the local install server it used"
```
