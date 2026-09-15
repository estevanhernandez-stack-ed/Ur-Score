# Score book, stage 1: data, Setup and the starter board — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ur Score v0.2.0 stage 1. Several sources (clans you're in, rivals, the top of the battle, profile stats) run at once. Every successful read is kept in a local score book, and past battles are filled in from the source. Setup lives in its own window, with an easy main-clan search. The main window becomes a fixed starter scoreboard built from ten panels.

**Architecture:**
- **Recipe format:** gains `period`, `asOf`, headline ids, input `plural` and group lists. The engine reads them and exposes past periods from the same response.
- **Sources:** a source is a recipe plus input values plus a role. `SourceHost` runs one `RecipeWatch` per source, over a shared account list.
- **Recording:** each watch records through `LineBuilder` into one `ScoreBook` writer, and `FinalsPlanner` writes final lines.
- **The window:** `ScoreBookReader` turns the book into series, finals and records. Panels render from the reader plus live snapshots. `BoardWindow` replaces `MainWindow`, and `SetupWindow` holds everything that is setup.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), xUnit 2.9, System.Text.Json, gRPC client to RoRoRo (`ROROROblox.PluginContract` 0.10.0). No new packages.

**Spec:** `docs/2026-09-14-score-book-design.md` (commit 493c3d8). Section numbers below (§n) refer to it.

## Global Constraints

- **No hostname literal in `src/`** except `NameClient.cs` (users.roblox.com) and `IconClient.cs` (thumbnails.roblox.com). `NoHostnameFenceTests` enforces it. Recipes and fixtures carry hosts; code never does.
- **Other players never reach disk.** Another player's Roblox id, name or value is never written to state, `sources.json`, `accounts.json`, the score book, the trail, diagnostics or the clipboard. Live leaderboards may show them in memory.
- **Keys never reach disk.** A saved key value never appears in files, errors, the trail or the clipboard. `Redactor` masks keys in any text that could carry one.
- **One path to RoRoRo.** `ReportPolicy.SendAsync` is the only caller of `IHostClient.ReportMetricAsync` (`ReportPolicyTests` enforces it).
- **Theme.** Themed brushes are referenced with `DynamicResource` only. No hex colours or literal brushes in `src/UI` XAML, and no colour code in UI `.cs` (`ThemeFenceTests`). Every brush used is one `ThemeService` paints.
- **Ur Score's own text never names a game.** Words like "clan" come from the recipe (input `plural`, labels).
- **Copy style:** sentence case, second person, specific, no emoji.
- **Book format:**
  - `v: 1`; UTF-8 JSON lines, `\n`-terminated;
  - files at `%LOCALAPPDATA%\626labs.ur-score\scorebook\<slug>\YYYY-MM.jsonl` (UTC month of `t`);
  - recipe texts at `scorebook\<slug>\recipes\<hash>.json`;
  - `hash` is the first 16 lowercase hex digits of the SHA-256 of the UTF-8 recipe text.
- **Other data files:** `sources.json`, `accounts.json`, `boards.json` (stage 2), all under `%LOCALAPPDATA%\626labs.ur-score\`.
- **Headline values in the book** are finite numbers only.
- **Group-list recipes** are never matched to accounts, never sent, never recorded.
- **`watch` sources** send nothing and record no accounts.
- **Requests:** at most one request at a time per host, at least 2 s apart.
- **Build gate:** `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` passes, and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build` passes. Run both at the end of every task.
- **Commits:** one or more per task, message style `area: what changed`, ending with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File structure

New files:

| File | Responsibility |
|---|---|
| `src/Recipes/RecipeFormat.cs` | `RecipePeriod`, `RecipeAsOf`, `TimeText` (unix-or-ISO parsing), `PathRules.HasLiteralNumber` |
| `src/Core/Sources.cs` | `SourceRole`, `Source`, `SourceStore` (`sources.json`), `SourceRules` (migrate, add, make main, remove) |
| `src/Core/SharedAccounts.cs` | `AccountsCache` (`accounts.json`), `AccountList`, `SharedAccounts` (30 s shared fetch with cache fallback) |
| `src/Core/AccountClaims.cs` | Which source owns an account for a recipe within a window |
| `src/Recipes/SpacedTransport.cs` | Serializes and spaces requests per host |
| `src/Core/SourceHost.cs` | Runs one `RecipeWatch` per enabled source |
| `src/Book/BookLine.cs` | Line records and `BookJson` |
| `src/Book/BookFiles.cs` | Paths, hashing, reading lines back |
| `src/Book/ScoreBook.cs` | `IScoreBook` and the queued, crash-tolerant writer |
| `src/Book/Ranking.cs` | Competition ranking |
| `src/Book/LineBuilder.cs` | `ReadContext`; a reading's line and a past period's final line, with the privacy rules |
| `src/Book/Finals.cs` | `FinalsIndex`, `FinalsPlanner` |
| `src/Book/ScoreBookReader.cs` | Series, finals, headline series, counts |
| `src/Book/Records.cs` | Records, would-place, stalled, overdue |
| `src/Cli/TryCommand.cs` | `--try` |
| `src/UI/Controls/StatsTable.xaml(.cs)` | The single searchable stats table (Task 10) |
| `src/UI/Controls/ClanSearchBox.xaml(.cs)` | Type-to-search over an input's search list (Task 11) |
| `src/UI/SetupWindow.xaml(.cs)` and `src/UI/Setup/*Page.xaml(.cs)` | Setup (Tasks 11–12) |
| `src/UI/Panels/*.xaml(.cs)` and `src/Board/PanelModels.cs` | The ten panels and their pure view-model builders (Task 13) |
| `src/UI/BoardWindow.xaml(.cs)`, `src/Board/StarterBoards.cs`, `src/Composition/AppServices.cs` | The starter board window and the composition root (Task 14) |
| `tools/smoke/*` | UI Automation smoke scripts (Task 16) |

Modified files:
- `src/Recipes/Recipe.cs`, `RecipeParser.cs`, `RecipeEngine.cs`;
- `src/Core/RecipeWatch.cs`;
- `src/UI/ImportWindow.xaml(.cs)`;
- `src/App.xaml.cs`;
- `tests/Fixtures/*.recipe.json`.

Retired in Task 14: `src/UI/MainWindow.xaml(.cs)`. Its alert rule and report policy logic move to `src/UI/Setup/AlertsPage`, and its orchestration moves to `AppServices` and `SourceHost`.

## Interface contract

Group-list recipes (`Recipe.IsGroupList`) have no stats to tick: the import screen and Setup › Stats show no Stats table for them, and "tick at least one stat" doesn't apply.

Every task implements, or relies on, exactly these names. Namespaces:
- `Labs626.UrScore.Recipes` for format and engine;
- `Labs626.UrScore.Core` for sources and watches;
- `Labs626.UrScore.Book` for the score book;
- `Labs626.UrScore.Board` for panel models;
- `Labs626.UrScore.UI` for windows and controls;
- `Labs626.UrScore.Cli` for `--try`.

```csharp
// ---- Task 1: format (Labs626.UrScore.Recipes) ----
public sealed record RecipePeriod(string Value, string? Starts, string? Ends, string? Past);
public sealed record RecipeAsOf(string Time, string? Stale);
public static class TimeText { public static DateTimeOffset? Parse(string? text); }            // all digits = unix seconds; else ISO-8601; else null
public static class PathRules { public static bool HasLiteralNumber(string path); }            // a dot segment with no placeholder that is all ASCII digits
// Recipe gains:        RecipePeriod? Period = null   (last positional parameter, after PlaceLabel)
//                      bool IsGroupList => LastStep.GroupName is not null;
// RecipeInput gains:   string? Plural = null   and   string PluralLabel { get; }   ("Your clan" -> "Clans")
// RecipeStep gains:    string? GroupName = null, string? Rank = null, RecipeAsOf? AsOf = null   (after AbsentMessage)
// RecipeHeadline gains: string Id = ""   (parser always fills it: given id, else RecipeStats.Slug(label))

// ---- Task 2: engine (Labs626.UrScore.Recipes) ----
public sealed record ReadingPeriod(string Value, DateTimeOffset? Starts, DateTimeOffset? Ends);
public sealed record AsOfStamp(DateTimeOffset Time, bool? Stale);
public sealed record GroupRow(string Name, IReadOnlyDictionary<string, double> Values, int? Rank);
public sealed record PastPeriodReading(string Value, IReadOnlyList<RecipeRow> Rows, IReadOnlyList<HeadlineValue> Headline, bool RowsReadable);
// HeadlineValue(string Label, string? Text) gains init properties:  string Id  and  double? Number
// RecipeReading gains init properties:
//   ReadingPeriod? Period; AsOfStamp? ListAsOf; IReadOnlyDictionary<long, AsOfStamp> AccountAsOf;
//   IReadOnlyList<GroupRow> Groups; IReadOnlyList<PastPeriodReading> Past;

// ---- Task 3: sources (Labs626.UrScore.Core) ----
public enum SourceRole { Main, Mine, Watch }
public sealed record Source(string Id, string Recipe, IReadOnlyDictionary<string, string> Inputs, SourceRole Role, bool Enabled = true)
{ public string InputsKey { get; }  public static string KeyOf(IReadOnlyDictionary<string, string> inputs); }   // ordinal-sorted "id=value", values trimmed and lower-cased, joined by U+001F
public sealed class SourceStore(string path)
{ public static string DefaultPath { get; } public IReadOnlyList<Source> Load(); public void Save(IReadOnlyList<Source> sources); }
public static class SourceRules
{
    public static IReadOnlyList<Source> Migrate(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> existing);
    public static IReadOnlyList<Source> Add(IReadOnlyList<Source> sources, string recipe, IReadOnlyDictionary<string, string> inputs, SourceRole role);
    public static IReadOnlyList<Source> MakeMain(IReadOnlyList<Source> sources, string sourceId);
    public static IReadOnlyList<Source> Remove(IReadOnlyList<Source> sources, string sourceId);
    public static IReadOnlyList<Source> ForgetRecipe(IReadOnlyList<Source> sources, string recipe);
    public static string NewId();                                                                 // "s-" + 8 lowercase hex
}

// ---- Task 4: accounts, claims, spacing ----
public sealed class AccountsCache(string path)                                                    // Labs626.UrScore.Core
{ public static string DefaultPath { get; } public IReadOnlyList<HostAccount> Load(); public void Save(IReadOnlyList<HostAccount> accounts); public DateTimeOffset? SavedAt(); }
public sealed record AccountList(IReadOnlyList<HostAccount> Accounts, bool HostUp, bool FromCache, bool Denied, DateTimeOffset? ListedAt);
public sealed class SharedAccounts(IHostClient host, AccountsCache cache, TimeProvider time)
{ public static readonly TimeSpan Window; /* 30 s */ public Task<AccountList> GetAsync(CancellationToken cancellationToken); public AccountList? Last { get; } }
public sealed class AccountClaims(TimeProvider time)
{ public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window); }          // true when unclaimed, claimed by this source, or the claim is older than window
public sealed class SpacedTransport(IRecipeTransport inner, TimeProvider time, TimeSpan spacing) : IRecipeTransport   // Labs626.UrScore.Recipes
{ public static readonly TimeSpan DefaultSpacing; /* 2 s */ }

// ---- Task 5: the book (Labs626.UrScore.Book) ----
public sealed record BookRecipeRef(string Slug, string Hash);
public sealed record BookPeriod(string Value, DateTimeOffset? Starts = null, DateTimeOffset? Ends = null);
public sealed record BookAccount(IReadOnlyDictionary<string, double> V, IReadOnlyDictionary<string, int>? Rank = null, int? Of = null, DateTimeOffset? AsOf = null, bool? Stale = null);
public sealed record BookLine(
    int V, string Kind, DateTimeOffset T, int Off, string Trigger, BookRecipeRef Recipe, string Source, string Role,
    IReadOnlyDictionary<string, string> Inputs, BookPeriod? Period, IReadOnlyDictionary<string, double> Headline,
    IReadOnlyList<string> Stats, IReadOnlyDictionary<string, BookAccount> Accounts,
    IReadOnlyList<string>? Unavail = null, DateTimeOffset? AsOf = null, bool? Stale = null)
{
    public const int Version = 1;
    public const string KindRead = "read", KindFinal = "final";
    public const string TriggerStart = "start", TriggerTimer = "timer", TriggerManual = "manual", TriggerBackfill = "backfill", TriggerEnded = "ended";
}
public static class BookJson { public static string Serialize(BookLine line); public static BookLine? TryParse(string text); }
public static class BookFiles
{
    public static string DefaultRoot { get; }
    public static string Hash(string recipeText);
    public static string MonthFile(string root, string slug, DateTimeOffset t);
    public static string RecipeFile(string root, string slug, string hash);
    public static IEnumerable<BookLine> ReadAll(string root, string slug);
    public static IReadOnlyList<string> Slugs(string root);
}
public interface IScoreBook
{
    void Append(BookLine line, string recipeText);
    event Action<BookLine>? Written;
    int Pending { get; }
    int Dropped { get; }
    void Flush();
    string Root { get; }
}
public sealed class ScoreBook : IScoreBook, IDisposable
{ public ScoreBook(string root, bool background = true); public const int MaxPending = 5000; public static readonly TimeSpan RetryDelay; /* 5 s */ }

// ---- Task 6: lines and recording ----
public static class Ranking { public static IReadOnlyDictionary<long, int> Competition(IEnumerable<RecipeRow> rows, string statKey); }   // Labs626.UrScore.Book
public sealed record ReadContext(Source Source, Recipe Recipe, string RecipeHash, string Trigger, DateTimeOffset At, int OffsetMinutes);
public static class LineBuilder
{
    public static BookLine? Reading(ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked);
    public static BookLine Final(ReadContext context, PastPeriodReading past, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked, IReadOnlyCollection<long>? onlyUsers, string trigger);
}
// RecipeWatch gains optional ctor parameters (after trackedStats, in this order):
//   IScoreBook? book = null, Source? source = null, SharedAccounts? sharedAccounts = null, string recipeText = "",
//   AccountClaims? claims = null, FinalsIndex? finals = null, TimeProvider? time = null
// RecipeWatch.RunOnceAsync(CancellationToken cancellationToken, string trigger = BookLine.TriggerTimer)
// RecipeWatch.UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string,string> newInputs, IReadOnlySet<string> newTrackedStats, string? newRecipeText = null)
// RecipeWatch.UpdateSource(Source source)   and   Source? RecipeWatch.Source { get; }   (role changes without a new watch)
// RecipeSnapshot gains init properties:  bool Recorded;  string? NotRecordingReason;  ReadingPeriod? Period;  IReadOnlyList<GroupRow> Groups;  string SourceId

// ---- Task 7: finals (Labs626.UrScore.Book) ----
public sealed class FinalsIndex
{
    public void Add(BookLine line);
    public bool HasClan(string slug, string inputsKey, string period);
    public bool HasAccount(string slug, string inputsKey, string period, long userId);
    public static FinalsIndex Load(string root);
}
public static class FinalsPlanner
{
    public static IReadOnlyList<BookLine> Plan(ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked, FinalsIndex index, string? previousPeriod);
    public static bool CurrentPeriodEnded(ReadContext context, RecipeReading reading, FinalsIndex index);
}

// ---- Task 8: host (Labs626.UrScore.Core) ----
public sealed class SourceHost : IDisposable
{
    public SourceHost(Func<Source, RecipeWatch?> createWatch, Func<Source, int> intervalSeconds);
    public event Action<string, RecipeSnapshot>? SnapshotReady;                                   // sourceId, snapshot; raised on the thread pool
    public void Apply(IReadOnlyList<Source> sources);
    public void Start();
    public void Stop();
    public bool Running { get; }
    public Task RunAllNowAsync(string trigger, CancellationToken cancellationToken);
    public IReadOnlyDictionary<string, RecipeSnapshot> Latest { get; }
    public RecipeWatch? WatchFor(string sourceId);
}

// ---- Task 9: reading the book (Labs626.UrScore.Book) ----
public sealed record SeriesPoint(DateTimeOffset T, double Value, DateTimeOffset? AsOf, bool Stale, int Off);      // Off = the line's UTC offset in minutes
public sealed record FinalEntry(string Period, DateTimeOffset T, IReadOnlyDictionary<string, double> Headline, IReadOnlyDictionary<long, BookAccount> Accounts);
public sealed class ScoreBookReader
{
    public ScoreBookReader(string root, TimeProvider time);                                   // keeps reading lines from the last 35 days and every final
    public void Load(IEnumerable<string> slugs);
    public void Apply(BookLine line);
    public IReadOnlyList<SeriesPoint> Series(string sourceId, long userId, string stat, string? period, DateTimeOffset since);
    public IReadOnlyList<SeriesPoint> HeadlineSeries(string sourceId, string headlineId, string? period);
    public IReadOnlyList<FinalEntry> Finals(string slug, string inputsKey);
    public int Readings(string slug);
    public DateTimeOffset? FirstReading(string slug);
    public long Bytes(string slug);
}
public sealed record AccountRecords(double? BestPeriodValue, string? BestPeriod, int? BestRank, string? BestRankPeriod, int PeriodsPlayed, double? Highest, double? BiggestDay, double? FastestWeek);
public static class Records
{
    public static AccountRecords For(ScoreBookReader reader, string slug, string inputsKey, IEnumerable<string> sourceIds, long userId, string stat, TimeProvider time);
    public static (int Place, int Of)? WouldPlace(double value, IEnumerable<double> otherRows);   // competition place among otherRows plus this value
    public static bool Stalled(IReadOnlyList<SeriesPoint> mine, IEnumerable<IReadOnlyList<SeriesPoint>> others);
    public static bool Overdue(DateTimeOffset? lastRead, int intervalSeconds, DateTimeOffset now);
    public static string Change(IReadOnlyList<SeriesPoint> series, DateTimeOffset now);           // "+220K in 1h", or "no earlier read"
}
// StatText (Labs626.UrScore.Core, existing) gains:
//   public static string Abbrev(double value);      // 950 -> "950", 220000 -> "220K", 12418220 -> "12.4M", 9169613101 -> "9.17B"
//   public static string Span(TimeSpan span);       // under 1h -> "31m", under 48h -> "5h", else "3d"

// ---- Task 15: try (Labs626.UrScore.Cli) ----
public static class TryCommand
{
    public const int Ok = 0, Refused = 2, InputMissing = 3, Stopped = 4, BadArguments = 5;
    public static bool Wants(string[] args);                                                       // args contains "--try"
    public static Task<int> RunAsync(string[] args, TextWriter output, IRecipeTransport transport, IKeyStore keys, CancellationToken cancellationToken);
}
```

---

## Rulings made while planning

These are recorded here so a reviewer doesn't read them as drift from the spec.

- **R1. Migration makes the existing source `main`, not `mine`.** Part 2a's single clan input becomes that
  recipe's ★ main source. Spec §4.1 says `mine`, but §8's starter board is built around a main, and a user
  upgrading with one clan means that clan. If wrong, the cost is one click on **Make main**.
- **R2. `period.starts` and `period.ends` takes are optional in the engine.** A `take` named only by
  `starts`/`ends` that is missing doesn't stop the read. Today every take is required, and the battle feed's
  `configData` could be absent. If wrong, the cost is none: a missing time is only a missing time.
- **R3. Past-period keys made only of digits are skipped,** the same rule as `RecipeStats.CanPick`. A `past`
  path pointed at an object keyed by user ids would otherwise write player ids into `period.value`. If wrong,
  a game whose period names are pure numbers gets no backfill.
- **R4. The reader collapses duplicates without knowing the recipe's interval.** Consecutive identical values
  collapse when both carry the same `asOf`, or when neither has one and they are under 60 s apart
  (`Recipe.MinimumEverySeconds`), such as a Test now next to a timer read. If wrong, a few duplicate points
  remain on a line.
- **R5. The reader keeps reading lines from the last 35 days, plus every final.** A year of five sources is
  close to a million lines, too many to hold. Records that need long history (best period, best rank,
  periods played) come from finals. "Biggest day" and "fastest 7 days" look at the last 35 days. If wrong,
  those two records only cover five weeks.
- **R6. Claims:** an account seen by two `mine`/`main` sources of the same recipe belongs to the source that
  claimed it within twice the recipe's interval.

---

### Task 1: Recipe format additions

Spec §3.1–§3.7. Parsing and validation only; the engine reads nothing new until Task 2.

**Files:**
- Create: `src/Recipes/RecipeFormat.cs`
- Modify: `src/Recipes/Recipe.cs` (records `Recipe`, `RecipeInput`, `RecipeStep`, `RecipeHeadline`)
- Modify: `src/Recipes/RecipeParser.cs`
- Modify: `tests/Fixtures/petsim99-clan-battle.recipe.json` (input `plural`, headline ids only; the takes and `period` come in Task 2)
- Modify: `tests/Fixtures/petsim99-profile.recipe.json` (add `asOf`)
- Create: `tests/Fixtures/petsim99-top-clans.recipe.json`
- Create: `tests/RecipeFormatTests.cs`
- Modify: `tests/RecipeParserTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `RecipePeriod`, `RecipeAsOf`, `TimeText.Parse`, `PathRules.HasLiteralNumber`, `Recipe.Period`, `Recipe.IsGroupList`, `RecipeInput.Plural`/`PluralLabel`, `RecipeStep.GroupName`/`Rank`/`AsOf`, `RecipeHeadline.Id`, exactly as in the contract.

- [ ] **Step 1: Write the failing tests for the new pure helpers**

Create `tests/RecipeFormatTests.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeFormatTests
{
    [Fact]
    public void UnixSecondsAndIsoTextBothParse()
    {
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757611800), TimeText.Parse("1757611800"));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 20, 11, 47, 320, TimeSpan.Zero), TimeText.Parse("2026-09-14T20:11:47.320Z"));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero), TimeText.Parse("2026-09-14T10:00:00-05:00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("99999999999999999999")]
    public void AnythingElseIsNoTime(string? text) => Assert.Null(TimeText.Parse(text));

    [Theory]
    [InlineData("data.members.1234567.name", true)]
    [InlineData("42", true)]
    [InlineData("data.Battles.{battle}.Points", false)]
    [InlineData("data.{clan}.7x", false)]
    [InlineData("data.Battles.2024Spring", false)]
    [InlineData("data.{userId}", false)]
    public void ALiteralNumberSegmentIsFound(string path, bool expected) => Assert.Equal(expected, PathRules.HasLiteralNumber(path));

    [Theory]
    [InlineData("Your clan", null, "Clans")]
    [InlineData("your guild", null, "Guilds")]
    [InlineData("Team", null, "Teams")]
    [InlineData("Your stats", null, "Stats")]
    [InlineData("Your clan", "Crews", "Crews")]
    public void AnInputsPluralComesFromTheRecipeOrItsLabel(string label, string? plural, string expected) =>
        Assert.Equal(expected, new RecipeInput("clan", label, null, plural).PluralLabel);
}
```

- [ ] **Step 2: Write the failing parser tests**

Append these tests inside `RecipeParserTests` in `tests/RecipeParserTests.cs`, before the final closing brace:

```csharp
    private const string TwoSteps = """
        [ { "url": "https://example.com/active", "take": { "season": "data.name", "seasonEnds": "data.ends" } },
          { "url": "https://example.com/rows/{season}", "rows": "data.rows", "userId": "id", "value": "score" } ]
        """;

    [Fact]
    public void APeriodNamesTakesAndAPastPath()
    {
        var result = RecipeParser.Parse(With(TwoSteps, """, "period": { "value": "season", "ends": "seasonEnds", "past": "data.seasons" }"""));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(new RecipePeriod("season", null, "seasonEnds", "data.seasons"), result.Recipe!.Period);
    }

    [Theory]
    [InlineData("""{ "ends": "seasonEnds" }""", "The period has no 'value'.")]
    [InlineData("""{ "value": "nope" }""", "The period's value 'nope' is not something an earlier step takes.")]
    [InlineData("""{ "value": "season", "starts": "nope" }""", "The period's starts 'nope' is not something an earlier step takes.")]
    public void APeriodMustNameRealTakes(string period, string problem) =>
        Assert.Contains(problem, Problems(With(TwoSteps, $$""", "period": {{period}}""")));

    [Fact]
    public void APastPathNeedsAListOfPlayers()
    {
        var perAccount = """
            [ { "url": "https://example.com/active", "take": { "season": "data.name" } },
              { "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count" } ]
            """;

        Assert.Contains("A period's 'past' needs a last step with rows and userId.",
            Problems(With(perAccount, """, "period": { "value": "season", "past": "data.seasons" }""")));
    }

    [Fact]
    public void AsOfIsReadOnlyOnTheLastStep()
    {
        var result = RecipeParser.Parse(With(
            """[{ "url": "https://example.com/u/{userId}", "perAccount": true, "value": "count", "asOf": { "time": "data.fetchedAt", "stale": "data.isStale" } }]"""));
        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.Equal(new RecipeAsOf("data.fetchedAt", "data.isStale"), result.Recipe!.LastStep.AsOf);

        var early = """
            [ { "url": "https://example.com/active", "take": { "season": "data.name" }, "asOf": { "time": "data.t" } },
              { "url": "https://example.com/rows/{season}", "rows": "data.rows", "userId": "id", "value": "score" } ]
            """;
        Assert.Contains("Step 1 has 'asOf', but only the last step can.", Problems(With(early)));
    }

    [Fact]
    public void HeadlineIdsDefaultToTheLabelsSlugAndMustBeUnique()
    {
        var ok = RecipeParser.Parse(With(OneListStep,
            """, "headline": [ { "label": "Clan place", "path": "data.place" }, { "id": "total", "label": "Clan points", "path": "data.points" } ]"""));
        Assert.True(ok.Ok, string.Join(" | ", ok.Problems));
        Assert.Equal(new[] { "clan-place", "total" }, ok.Recipe!.Headline.Select(h => h.Id).ToArray());

        Assert.Contains("The headline id 'same' is used more than once.", Problems(With(OneListStep,
            """, "headline": [ { "id": "same", "label": "A", "path": "data.a" }, { "id": "same", "label": "B", "path": "data.b" } ]""")));
    }

    [Fact]
    public void AGroupListReadsGroupsAndNeedsNoMetricId()
    {
        var result = RecipeParser.Parse(Fixture("petsim99-top-clans.recipe.json"));

        Assert.True(result.Ok, string.Join(" | ", result.Problems));
        Assert.True(result.Recipe!.IsGroupList);
        Assert.Equal("name", result.Recipe.LastStep.GroupName);
        Assert.Equal("rank", result.Recipe.LastStep.Rank);
        Assert.Null(result.Recipe.LastStep.UserId);
    }

    [Fact]
    public void AGroupListCannotAlsoReadPlayers()
    {
        Assert.Contains("Step 1 has both 'userId' and 'groupName'. A row is a player or a group, not both.",
            Problems(With("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "groupName": "name", "value": "score" }]""")));

        Assert.Contains("Step 1 has 'rank', which only a groupName step can use.",
            Problems(With("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "rank": "rank", "value": "score" }]""")));
    }

    [Theory]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data.1234567", "userId": "id", "value": "score" }]""", "", "Step 1: 'data.1234567' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "stats.99" }]""", "", "Step 1: 'stats.99' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""", """, "headline": [ { "label": "Owner", "path": "data.members.42.name" } ]""", "Headline 1: 'data.members.42.name' names a number.")]
    [InlineData("""[{ "url": "https://example.com/rows", "rows": "data", "userId": "id", "value": "score" }]""", """, "icon": "data.7.icon" """, "The icon path: 'data.7.icon' names a number.")]
    public void RecipesCannotPointAtAParticularPlayer(string steps, string extra, string start)
    {
        var problems = Problems(With(steps, extra));
        Assert.Contains(problems, p => p.StartsWith(start, StringComparison.Ordinal)
            && p.EndsWith("Recipes can't point at a particular player; use a placeholder instead.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWorkedRecipesCarryTheirNewFields()
    {
        var clan = RecipeParser.Parse(Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        Assert.Equal("Clans", clan.Inputs[0].PluralLabel);
        Assert.Equal(new[] { "clan-place", "clan-points" }, clan.Headline.Select(h => h.Id).ToArray());

        var profile = RecipeParser.Parse(Fixture("petsim99-profile.recipe.json")).Recipe!;
        Assert.Equal(new RecipeAsOf("data.views.profile.fetchedAt", "data.views.profile.isStale"), profile.LastStep.AsOf);
    }
```

- [ ] **Step 3: Add the fixtures**

Create `tests/Fixtures/petsim99-top-clans.recipe.json`:

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 top clans",
  "credit": "Data from Big Games' public Pet Simulator 99 API. Built from the top 100 clans by all-time points.",
  "valueLabel": "Points",
  "everySeconds": 300,
  "steps": [
    { "url": "https://ps99.biggamesapi.io/api/activeClanBattle",
      "take": { "battle": "data.configName" },
      "idleWithout": "battle", "idleMessage": "No clan battle running" },
    { "url": "https://ps99.biggamesapi.io/v1/clans/battles/{battle}",
      "rows": "data.topClans", "groupName": "name", "value": "points", "rank": "rank" }
  ]
}
```

In `tests/Fixtures/petsim99-clan-battle.recipe.json`:
- Replace the `inputs` line with:
  `"inputs": [ { "id": "clan", "label": "Your clan", "plural": "Clans", "search": { "url": "https://ps99.biggamesapi.io/api/clansList", "list": "data" } } ],`
- Replace the `headline` array with:

```json
  "headline": [
    { "id": "clan-place", "label": "Clan place", "path": "data.Battles.{battle}.Place", "sum": false },
    { "id": "clan-points", "label": "Clan points", "path": "data.Battles.{battle}.Points" }
  ],
```

In `tests/Fixtures/petsim99-profile.recipe.json`, add this line to the step object right after `"perAccount": true,`:

```json
      "asOf": { "time": "data.views.profile.fetchedAt", "stale": "data.views.profile.isStale" },
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL. The compile errors name `TimeText`, `PathRules`, `RecipePeriod`, `RecipeAsOf`, and the missing `RecipeInput` constructor argument and `PluralLabel`.

- [ ] **Step 5: Create `src/Recipes/RecipeFormat.cs`**

```csharp
using System.Globalization;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// What a reading belongs to (score book spec §3.1). <see cref="Value"/>, <see cref="Starts"/> and
/// <see cref="Ends"/> name takes from earlier steps; <see cref="Past"/> is a path in the last step's
/// response to an object whose keys are period values.
/// </summary>
public sealed record RecipePeriod(string Value, string? Starts, string? Ends, string? Past);

/// <summary>The source's own snapshot time, and whether it says the data is stale (score book spec §3.2).</summary>
public sealed record RecipeAsOf(string Time, string? Stale);

public static class TimeText
{
    /// <summary>Latest instant a time can name: the last second of year 9999.</summary>
    private const long MaxUnixSeconds = 253402300799;

    /// <summary>All digits is unix seconds; anything else must be ISO-8601. Returns UTC, or null.</summary>
    public static DateTimeOffset? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        if (text.All(char.IsAsciiDigit))
        {
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                   && seconds is > 0 and <= MaxUnixSeconds
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}

public static class PathRules
{
    /// <summary>
    /// A dot segment with no placeholder that is only digits. <c>RecipePath</c> walks objects by key and
    /// never indexes lists, so such a segment can only name an object key, and in these APIs that is a
    /// player's user id. A recipe must never point at a particular player (score book spec §3.6).
    /// </summary>
    public static bool HasLiteralNumber(string path) =>
        path.Split('.').Any(segment => segment.Length > 0 && !segment.Contains('{') && segment.All(char.IsAsciiDigit));
}
```

- [ ] **Step 6: Extend the records in `src/Recipes/Recipe.cs`**

Change the `Recipe` record's parameter list so it ends with `string PlaceLabel = Recipe.DefaultPlaceLabel, RecipePeriod? Period = null)`, and add this property below `LastStep`:

```csharp
    /// <summary>The last step reads groups (clans), not players: never matched to accounts, sent or recorded.</summary>
    public bool IsGroupList => LastStep.GroupName is not null;
```

Replace `public sealed record RecipeInput(string Id, string Label, RecipeSearch? Search);` with:

```csharp
public sealed record RecipeInput(string Id, string Label, RecipeSearch? Search, string? Plural = null)
{
    /// <summary>The word for several of these, for Setup and panel titles: the recipe's own, else from the label.</summary>
    public string PluralLabel => Plural ?? DefaultPlural(Label);

    internal static string DefaultPlural(string label)
    {
        var text = label.Trim();
        if (text.StartsWith("Your ", StringComparison.OrdinalIgnoreCase)) text = text[5..].Trim();
        if (text.Length == 0) return "Items";

        text = char.ToUpperInvariant(text[0]) + text[1..];
        return text.EndsWith('s') ? text : text + "s";
    }
}
```

Change the `RecipeStep` record so its parameter list ends with:

```csharp
    RecipeCounters? Counters = null,
    RecipeUnavailable? Unavailable = null,
    string? AbsentMessage = null,
    string? GroupName = null,
    string? Rank = null,
    RecipeAsOf? AsOf = null);
```

Replace `public sealed record RecipeHeadline(string Label, string Path, bool Sum = true);` with:

```csharp
/// <summary><see cref="Id"/> is how the score book keeps this value; the parser always fills it.</summary>
public sealed record RecipeHeadline(string Label, string Path, bool Sum = true, string Id = "");
```

- [ ] **Step 7: Parse the new fields in `src/Recipes/RecipeParser.cs`**

1. In `Parse`, after `var headline = ParseHeadline(root, problems);`, add:

```csharp
            var period = ParsePeriod(root, problems);
            var groupList = steps.Count > 0 && steps[^1].GroupName is not null;
```

   Change the metricId check to `if (metricId is null && !lastUsesValues && !groupList)`.
   Change the `Validate(...)` call to `Validate(inputs, keys, steps, headline, icon, placeLabel, period, problems);`.
   Change the `new Recipe(...)` call to pass `icon, placeLabel ?? Recipe.DefaultPlaceLabel, period)`.

2. In `ParseInputs`, change the add line to:

```csharp
            if (id is not null && label is not null) inputs.Add(new RecipeInput(id, label, search, OptionalString(item, "plural")));
```

3. In `ParseSteps`, change the `new RecipeStep(...)` call so its last arguments are:

```csharp
                    ParseAbsentMessage(item, where, problems),
                    OptionalString(item, "groupName"),
                    OptionalString(item, "rank"),
                    ParseAsOf(item, where, problems)));
```

4. In `ParseHeadline`, replace the add line with:

```csharp
            var id = OptionalString(item, "id") ?? (label is null ? null : RecipeStats.Slug(label));
            if (label is not null && path is not null) headline.Add(new RecipeHeadline(label, path, sum, id!));
```

5. Add these two methods next to `ParseAbsentMessage`:

```csharp
    private static RecipePeriod? ParsePeriod(JsonElement root, List<string> problems)
    {
        if (!Present(root, "period", out var period)) return null;

        if (period.ValueKind != JsonValueKind.Object)
        {
            problems.Add("'period' must be an object with a value, and optionally starts, ends and past.");
            return null;
        }

        var value = OptionalString(period, "value");
        if (value is null)
        {
            problems.Add("The period has no 'value'.");
            return null;
        }

        return new RecipePeriod(value, OptionalString(period, "starts"), OptionalString(period, "ends"), OptionalString(period, "past"));
    }

    private static RecipeAsOf? ParseAsOf(JsonElement step, string where, List<string> problems)
    {
        if (!Present(step, "asOf", out var asOf)) return null;

        var time = asOf.ValueKind == JsonValueKind.Object ? OptionalString(asOf, "time") : null;
        if (time is null)
        {
            problems.Add($"{Capitalize(where)}'s 'asOf' must be an object with a 'time' path.");
            return null;
        }

        return new RecipeAsOf(time, OptionalString(asOf, "stale"));
    }
```

6. Change `Validate`'s signature to take `RecipePeriod? period` before `List<string> problems`. Make these changes inside it:
   - After the two `Duplicates(...)` calls at the top, add `Duplicates(headline.Select(h => h.Id), "headline id", problems);`.
   - Add `var groupForm = false;` next to `var listForm = false;`.
   - In the per-step loop, change `var reads = ...` to also include `|| step.GroupName is not null || step.Rank is not null`.
   - After the `if (step.Unavailable is not null && !step.PerAccount)` block, add:

```csharp
            if (step.AsOf is not null && !isLast)
            {
                problems.Add($"{Capitalize(where)} has 'asOf', but only the last step can.");
            }

            if (step.GroupName is not null && step.UserId is not null)
            {
                problems.Add($"{Capitalize(where)} has both 'userId' and 'groupName'. A row is a player or a group, not both.");
            }

            if (step.Rank is not null && step.GroupName is null)
            {
                problems.Add($"{Capitalize(where)} has 'rank', which only a groupName step can use.");
            }
```

   - Replace the whole `if (isLast) { ... }` block with:

```csharp
            if (isLast)
            {
                listForm = step.Rows is not null && step.UserId is not null && step.GroupName is null && step.Values.Count > 0 && !step.PerAccount;
                groupForm = step.Rows is not null && step.GroupName is not null && step.UserId is null && step.Values.Count > 0 && !step.PerAccount;
                var perAccountForm = step.PerAccount && step.Values.Count > 0 && step.Rows is null && step.UserId is null && step.GroupName is null;

                if (!listForm && !perAccountForm && !groupForm && step.GroupName is null)
                {
                    problems.Add("The last step needs rows, userId and value, or perAccount and value.");
                }
                else if (!groupForm && step.GroupName is not null && step.UserId is null)
                {
                    problems.Add("A groupName step needs rows and a value, and can't be perAccount.");
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
```

   - After the `foreach (var input in inputs.Where(i => i.Search is not null))` block at the end of `Validate`, add:

```csharp
        if (period is not null)
        {
            if (!taken.Contains(period.Value))
            {
                problems.Add($"The period's value '{period.Value}' is not something an earlier step takes.");
            }

            foreach (var (name, what) in new[] { (period.Starts, "starts"), (period.Ends, "ends") })
            {
                if (name is not null && !taken.Contains(name))
                {
                    problems.Add($"The period's {what} '{name}' is not something an earlier step takes.");
                }
            }

            if (period.Past is not null)
            {
                if (!listForm) problems.Add("A period's 'past' needs a last step with rows and userId.");

                foreach (var name in Placeholders.Names(period.Past).Where(n => !allKnown.Contains(n)))
                {
                    problems.Add($"Unknown placeholder {{{name}}} in the period's past path.");
                }
            }
        }

        // Score book spec §3.6: no path may name a particular player.
        var everyPath = steps.SelectMany((step, index) => PathsOf(step).Select(p => ($"step {index + 1}", p.Path)))
            .Concat(headline.Select((h, index) => ($"headline {index + 1}", h.Path)))
            .Concat(icon is null ? [] : new[] { ("the icon path", icon) })
            .Concat(period?.Past is null ? [] : new[] { ("the period's past path", period.Past) });

        foreach (var (where, path) in everyPath.Where(p => PathRules.HasLiteralNumber(p.Path)))
        {
            problems.Add($"{Capitalize(where)}: '{path}' names a number. Recipes can't point at a particular player; use a placeholder instead.");
        }
```

7. Extend `PathsOf` with these lines before its closing brace:

```csharp
        if (step.GroupName is not null) yield return ("groupName", step.GroupName);
        if (step.Rank is not null) yield return ("rank", step.Rank);
        if (step.AsOf is not null) yield return ("asOf time", step.AsOf.Time);
        if (step.AsOf?.Stale is not null) yield return ("asOf stale", step.AsOf.Stale);
```

   **Mind the ordering.** `taken` only holds takes from steps already walked. The period checks sit after the loop, so every take is known by then. `allKnown` is the set `Validate` already builds before the headline placeholder checks, so the period block must come after that point, which it does at the end of the method.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS. The count is 329 plus the new tests. No existing test changes result; if an existing
test fails, the parser change broke a message it asserts. Fix the parser, not the test.

- [ ] **Step 9: Commit**

```bash
git add src/Recipes/RecipeFormat.cs src/Recipes/Recipe.cs src/Recipes/RecipeParser.cs tests/RecipeFormatTests.cs tests/RecipeParserTests.cs tests/Fixtures/
git commit -m "recipes: period, asOf, headline ids, group lists, and no paths that name a player

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: The engine reads periods, source times, headline numbers, groups and past periods

Spec §3.1–§3.5 and §6.2. One response still serves everything, and adding past periods adds no request.

**Files:**
- Modify: `src/Recipes/RecipeEngine.cs`
- Modify: `tests/Fixtures/petsim99-clan-battle.recipe.json` (the takes for start and end, plus `period`)
- Modify: `tests/RecipeEngineTests.cs`

**Interfaces:**
- Consumes: Task 1's `Recipe.Period`, `Recipe.IsGroupList`, `RecipeStep.GroupName`/`Rank`/`AsOf`, `RecipeHeadline.Id`, `TimeText.Parse`.
- Produces: `ReadingPeriod`, `AsOfStamp`, `GroupRow`, `PastPeriodReading`. `HeadlineValue.Id` and `.Number`. `RecipeReading.Period`, `.ListAsOf`, `.AccountAsOf`, `.Groups` and `.Past`, exactly as in the contract.

- [ ] **Step 1: Update the clan fixture**

In `tests/Fixtures/petsim99-clan-battle.recipe.json`, replace step 1's `take` with:

```json
      "take": { "battle": "data.configName", "battleStarts": "data.configData.StartTime", "battleEnds": "data.configData.FinishTime" },
```

and add this after the `steps` array, before `"headline"`:

```json
  "period": { "value": "battle", "starts": "battleStarts", "ends": "battleEnds", "past": "data.Battles" },
```

- [ ] **Step 2: Write the failing tests**

Append inside `RecipeEngineTests`:

```csharp
    private const string BattleWithTimes = """
        { "status": "ok", "data": { "configName": "B", "configData": { "StartTime": 1756490400, "FinishTime": 1757611800 } } }
        """;

    private const string ClanWithHistory = """
        { "status": "ok", "data": { "Icon": "rbxassetid://1", "Battles": {
            "A": { "Place": 40, "Points": 500, "PointContributions": [ { "UserID": 111, "Points": 300 }, { "UserID": 222, "Points": 200 } ] },
            "Empty": { "Place": 900, "Points": 10 },
            "123456": { "Place": 1, "Points": 1 },
            "B": { "Place": 3, "Points": 999, "PointContributions": [ { "UserID": 111, "Points": 4200 }, { "UserID": 222, "Points": 10 } ] }
        } } }
        """;

    [Fact]
    public async Task HeadlineValuesCarryTheirIdAndNumber()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(("clan-place", 3d), (reading.Headline[0].Id, reading.Headline[0].Number!.Value));
        Assert.Equal(("clan-points", 999d), (reading.Headline[1].Id, reading.Headline[1].Number!.Value));
        Assert.Equal("3", reading.Headline[0].Text);
    }

    [Fact]
    public async Task ThePeriodComesFromTheTakesWithItsTimes()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, BattleWithTimes)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new ReadingPeriod("B", DateTimeOffset.FromUnixTimeSeconds(1756490400), DateTimeOffset.FromUnixTimeSeconds(1757611800)), reading.Period);
    }

    [Fact]
    public async Task AMissingStartOrEndStillReads()
    {
        // Ruling R2: a take named only by the period's starts or ends is optional.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanResponse);

        var reading = await Read(transport, PetSim);

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Equal(new ReadingPeriod("B", null, null), reading.Period);
    }

    [Fact]
    public async Task EveryPastPeriodIsReadFromTheSameResponse()
    {
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanWithHistory);

        var reading = await Read(transport, PetSim);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(new[] { "A", "Empty", "B" }, reading.Past.Select(p => p.Value).ToArray());

        var a = reading.Past[0];
        Assert.True(a.RowsReadable);
        Assert.Equal(new[] { Row(111, 300), Row(222, 200) }, a.Rows);
        Assert.Equal(new double?[] { 40, 500 }, a.Headline.Select(h => h.Number).ToArray());

        var empty = reading.Past[1];
        Assert.False(empty.RowsReadable);
        Assert.Empty(empty.Rows);
        Assert.Equal(new double?[] { 900, 10 }, empty.Headline.Select(h => h.Number).ToArray());
    }

    [Fact]
    public async Task APastKeyMadeOfDigitsIsNeverAPeriod()
    {
        // Ruling R3: an object keyed by user ids must not become period values in the book.
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/api/clan/", 200, ClanWithHistory);

        var reading = await Read(transport, PetSim);

        Assert.DoesNotContain(reading.Past, p => p.Value == "123456");
    }

    [Fact]
    public async Task APerAccountAsOfIsReadForEachAccount()
    {
        const string stamped = """
            { "status": "ok", "data": { "views": { "profile": { "available": true, "isStale": true, "fetchedAt": "2026-09-14T20:11:47.320Z",
              "data": { "Currency": { "Diamonds": { "_am": 5 } }, "EggsHatched": 1, "Rank": 2 } } } } }
            """;
        var transport = new FakeTransport().On(ProfileUrl1, 200, stamped);

        var reading = await Read(transport, Profile, NoInputs, [1], tracked: ProfileStats);

        Assert.Equal(new AsOfStamp(new DateTimeOffset(2026, 9, 14, 20, 11, 47, 320, TimeSpan.Zero), true), reading.AccountAsOf[1]);
        Assert.Null(reading.ListAsOf);
    }

    [Fact]
    public async Task AGroupListReadsEveryGroupWhateverIsTicked()
    {
        const string top = """
            { "status": "ok", "data": { "topClans": [
                { "rank": 1, "name": "Aurelian", "points": 412000000 },
                { "rank": 2, "name": "SkyHarbor", "points": 388500000 },
                { "rank": 3, "points": 5 }
            ] } }
            """;
        var transport = new FakeTransport()
            .On("https://ps99.biggamesapi.io/api/activeClanBattle", 200, Battle)
            .On("https://ps99.biggamesapi.io/v1/clans/battles/B", 200, top);
        var recipe = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

        var reading = await Read(transport, recipe, NoInputs, tracked: new HashSet<string>());

        Assert.Equal(ReadingOutcome.Read, reading.Outcome);
        Assert.Empty(reading.Rows);
        Assert.Equal(new[] { ("Aurelian", 412000000d, (int?)1), ("SkyHarbor", 388500000d, (int?)2) },
            reading.Groups.Select(g => (g.Name, g.Values["value"], g.Rank)).ToArray());
        Assert.Equal(3, reading.RowsSeen);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL. The compile errors name `ReadingPeriod`, `AsOfStamp`, `HeadlineValue.Id`/`Number`, `RecipeReading.Past`, `.AccountAsOf`, `.ListAsOf` and `.Groups`.

- [ ] **Step 4: Add the reading records to `src/Recipes/RecipeEngine.cs`**

Replace `public sealed record HeadlineValue(string Label, string? Text);` with:

```csharp
/// <summary>A headline as read: its text for display, and its id and number for the score book.</summary>
public sealed record HeadlineValue(string Label, string? Text)
{
    public string Id { get; init; } = "";

    /// <summary>The value when it is a finite number, else null. The book keeps only this.</summary>
    public double? Number { get; init; }
}

/// <summary>What this reading belongs to, and when it starts and ends when the source says.</summary>
public sealed record ReadingPeriod(string Value, DateTimeOffset? Starts, DateTimeOffset? Ends);

/// <summary>The source's own snapshot time for what was read.</summary>
public sealed record AsOfStamp(DateTimeOffset Time, bool? Stale);

/// <summary>One group (a clan) from a group-list step. Never matched to an account.</summary>
public sealed record GroupRow(string Name, IReadOnlyDictionary<string, double> Values, int? Rank);

/// <summary>
/// One key under the recipe's <c>period.past</c>, read with the live paths. <see cref="Rows"/> is every
/// row, the user's and everyone else's; the score book keeps only the user's.
/// </summary>
public sealed record PastPeriodReading(string Value, IReadOnlyList<RecipeRow> Rows, IReadOnlyList<HeadlineValue> Headline, bool RowsReadable);
```

Add these init properties to `RecipeReading`, after `IconText`:

```csharp
    public ReadingPeriod? Period { get; init; }

    /// <summary>A list step's <c>asOf</c>.</summary>
    public AsOfStamp? ListAsOf { get; init; }

    /// <summary>A per-account step's <c>asOf</c>, by user id.</summary>
    public IReadOnlyDictionary<long, AsOfStamp> AccountAsOf { get; init; } = new Dictionary<long, AsOfStamp>();

    /// <summary>A group-list step's rows.</summary>
    public IReadOnlyList<GroupRow> Groups { get; init; } = [];

    /// <summary>Every readable key under <c>period.past</c>, in the source's order.</summary>
    public IReadOnlyList<PastPeriodReading> Past { get; init; } = [];
```

- [ ] **Step 5: Read them in `RecipeEngine.ReadAsync`**

1. Replace the stats selection and the `NothingTracked` check with:

```csharp
        // A group list has nothing to tick: it reads every value it declares.
        var stats = recipe.IsGroupList
            ? RecipeStats.Offered(recipe, []).ToList()
            : RecipeStats.Offered(recipe, trackedStats).Where(stat => trackedStats.Contains(stat.Key)).ToList();
        if (stats.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.NeedsInput, NothingTracked);
        }
```

2. In the `foreach (var (name, pathTemplate) in step.Take)` loop, add this right after the `if (result.Outcome == PathOutcome.Found && ...) { ... continue; }` block:

```csharp
                    // Ruling R2: a take named only by the period's start or end is optional.
                    if (recipe.Period is { } optional && (name == optional.Starts || name == optional.Ends))
                    {
                        continue;
                    }
```

3. Change the list call to `return ReadList(recipe, step, stats, document!.RootElement, values, number, Context(values, taken)) with { Period = PeriodOf(recipe, values) };`.
   Change the per-account call to append ` with { Period = PeriodOf(recipe, values) }` after its `ConfigureAwait(false)`, wrapping the awaited call in parentheses.
   A stopped reading keeps `Period` null, because `with` only applies to `Read` outcomes: both helpers return early `Stop(...)` readings, and a null period on those is harmless.

- [ ] **Step 6: Read headline numbers, `asOf`, groups and past periods in `ReadList`**

1. At the top of `ReadList`, after the `icon` line, add:

```csharp
        if (step.GroupName is not null) return ReadGroups(step, stats, root, values, number, context);
```

2. Replace the `var headline = recipe.Headline.Select(...).ToList();` block with `var headline = HeadlineAt(recipe, root, values);`.

3. Replace the final `return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total) { ... };` with:

```csharp
        return new RecipeReading(ReadingOutcome.Read, null, rows, headline, context, total)
        {
            StatMisses = tally.StatMisses(rows.Count),
            CellMisses = tally.CellMisses(rows.Count),
            CounterNames = counterNames ?? [],
            IconText = icon,
            ListAsOf = step.AsOf is null ? null : AsOfAt(root, step.AsOf, values),
            Past = PastAt(recipe, step, stats, root, values),
        };
```

4. In `ReadPerAccountAsync`:
   - Add `var asOf = new Dictionary<long, AsOfStamp>();` next to `var unavailable = ...`.
   - Right after `rows.Add(new RecipeRow(userId, found));`, add `if (step.AsOf is not null && AsOfAt(root, step.AsOf, perRequest) is { } stamp) asOf[userId] = stamp;`.
   - Add `AccountAsOf = asOf,` to the final object initializer.

5. Add these helpers above `NumberAt`:

```csharp
    private static List<HeadlineValue> HeadlineAt(Recipe recipe, JsonElement root, IReadOnlyDictionary<string, string> values) =>
        [.. recipe.Headline.Select(h =>
        {
            var result = RecipePath.Resolve(root, h.Path, values);
            var found = result.Outcome == PathOutcome.Found;
            double? number = found && JsonNav.TryNumber(result.Value, out var n) ? n : null;
            return new HeadlineValue(h.Label, found ? RecipePath.AsText(result.Value) : null) { Id = h.Id, Number = number };
        })];

    private static AsOfStamp? AsOfAt(JsonElement root, RecipeAsOf asOf, IReadOnlyDictionary<string, string> values)
    {
        if (TimeText.Parse(TextAt(root, asOf.Time, values)) is not { } time) return null;

        bool? stale = null;
        if (asOf.Stale is not null && RecipePath.Resolve(root, asOf.Stale, values) is { Outcome: PathOutcome.Found } said
            && said.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            stale = said.Value.GetBoolean();
        }

        return new AsOfStamp(time, stale);
    }

    private static ReadingPeriod? PeriodOf(Recipe recipe, IReadOnlyDictionary<string, string> values)
    {
        if (recipe.Period is not { } period || !values.TryGetValue(period.Value, out var value)) return null;

        DateTimeOffset? At(string? take) => take is not null && values.TryGetValue(take, out var text) ? TimeText.Parse(text) : null;
        return new ReadingPeriod(value, At(period.Starts), At(period.Ends));
    }

    /// <summary>
    /// Score book spec §6.2: each key under <c>period.past</c>, read with the live rows, value and headline
    /// paths and the period placeholder set to that key. No miss here stops anything; a key whose rows
    /// can't be read still has its headline.
    /// </summary>
    private static IReadOnlyList<PastPeriodReading> PastAt(
        Recipe recipe, RecipeStep step, IReadOnlyList<RecipeStat> stats, JsonElement root, IReadOnlyDictionary<string, string> values)
    {
        if (recipe.Period is not { Past: { } pastPath } period) return [];

        var past = RecipePath.Resolve(root, pastPath, values);
        if (past.Outcome != PathOutcome.Found || past.Value.ValueKind != JsonValueKind.Object) return [];

        var readings = new List<PastPeriodReading>();
        foreach (var property in past.Value.EnumerateObject())
        {
            // Ruling R3: an all-digit key is far likelier a player's id than a period's name.
            if (property.Name.Length == 0 || property.Name.All(char.IsAsciiDigit)) continue;

            var keyed = new Dictionary<string, string>(values, StringComparer.Ordinal) { [period.Value] = property.Name };
            var rowsResult = RecipePath.Resolve(root, step.Rows!, keyed);
            var readable = rowsResult.Outcome == PathOutcome.Found && rowsResult.Value.ValueKind == JsonValueKind.Array;

            var rows = new List<RecipeRow>();
            if (readable)
            {
                foreach (var row in rowsResult.Value.EnumerateArray())
                {
                    var id = RecipePath.Resolve(row, Placeholders.Fill(step.UserId!, keyed, encode: false), "this row");
                    if (id.Outcome != PathOutcome.Found || !JsonNav.TryUserId(id.Value, out var userId)) continue;

                    var found = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (var stat in stats)
                    {
                        if (NumberAt(RecipePath.Resolve(row, stat.Path, keyed, "this row"), stat.Path, "in this row", out var value) is null)
                        {
                            found[stat.Key] = value;
                        }
                    }

                    rows.Add(new RecipeRow(userId, found));
                }
            }

            readings.Add(new PastPeriodReading(property.Name, rows, HeadlineAt(recipe, root, keyed), readable));
        }

        return readings;
    }

    private static RecipeReading ReadGroups(
        RecipeStep step, IReadOnlyList<RecipeStat> stats, JsonElement root, IReadOnlyDictionary<string, string> values, int number, string? context)
    {
        var rowsPath = Placeholders.Fill(step.Rows!, values, encode: false);
        var rowsResult = RecipePath.Resolve(root, step.Rows!, values);
        if (rowsResult.Outcome != PathOutcome.Found || rowsResult.Value.ValueKind != JsonValueKind.Array)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"Step {number}: '{rowsPath}' is not a list, so there are no groups to read.");
        }

        var groups = new List<GroupRow>();
        var total = 0;
        foreach (var row in rowsResult.Value.EnumerateArray())
        {
            total++;
            if (TextAt(row, step.GroupName!, values) is not { Length: > 0 } name) continue;

            var found = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in stats)
            {
                if (NumberAt(RecipePath.Resolve(row, stat.Path, values, "this row"), stat.Path, "in this row", out var value) is null)
                {
                    found[stat.Key] = value;
                }
            }

            int? rank = step.Rank is not null
                        && NumberAt(RecipePath.Resolve(row, step.Rank, values, "this row"), step.Rank, "in this row", out var r) is null
                ? (int)r
                : null;

            groups.Add(new GroupRow(name, found, rank));
        }

        if (total > 0 && groups.Count == 0)
        {
            return RecipeReading.Stop(ReadingOutcome.ShapeNotUnderstood, $"None of the {total} groups had a name at '{step.GroupName}'.");
        }

        return new RecipeReading(ReadingOutcome.Read, null, [], [], context, total) { Groups = groups };
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS, every existing engine and watch test included. `ThePetSimRecipeReadsEveryContribution`
still passes, because its `Battle` has no `configData` and Ruling R2 makes those takes optional.

- [ ] **Step 8: Commit**

```bash
git add src/Recipes/RecipeEngine.cs tests/RecipeEngineTests.cs tests/Fixtures/petsim99-clan-battle.recipe.json
git commit -m "engine: periods, source times, headline numbers, group lists and past periods from one response

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Sources

Spec §4.1. A source is one recipe, one set of input values and a role, kept in `sources.json`.

**Files:**
- Create: `src/Core/Sources.cs`
- Create: `tests/SourcesTests.cs`

**Interfaces:**
- Consumes: `InstalledRecipe`, `RecipeState.InputValues`, `Recipe.Slug`, `Recipe.Inputs`, `Recipe.IsGroupList`.
- Produces: `SourceRole`, `Source` (with `InputsKey` and `KeyOf`), `SourceStore`, `SourceRules`, exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/SourcesTests.cs`:

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SourcesTests
{
    private static InstalledRecipe Installed(string fixture, RecipeState? state = null)
    {
        var text = RecipeParserTests.Fixture(fixture);
        return new InstalledRecipe(RecipeParser.Parse(text).Recipe!, text, state ?? new RecipeState());
    }

    private static Dictionary<string, string> Clan(string name) => new() { ["clan"] = name };

    [Fact]
    public void AnInputKeyIgnoresOrderCaseAndSpaces()
    {
        var a = new Dictionary<string, string> { ["clan"] = " K0i2 ", ["region"] = "EU" };
        var b = new Dictionary<string, string> { ["region"] = "eu", ["clan"] = "k0i2" };

        Assert.Equal(Source.KeyOf(a), Source.KeyOf(b));
        Assert.NotEqual(Source.KeyOf(a), Source.KeyOf(Clan("CCGP")));
    }

    [Fact]
    public void MigrationTurnsTheOldClanIntoTheMainSourceAndGivesInputlessRecipesOne()
    {
        // Ruling R1: the one clan a 2a user had is their main.
        var installed = new[]
        {
            Installed("petsim99-clan-battle.recipe.json", new RecipeState(Inputs: Clan("CCGP"))),
            Installed("petsim99-profile.recipe.json"),
            Installed("petsim99-top-clans.recipe.json"),
        };

        var sources = SourceRules.Migrate(installed, []);

        Assert.Equal(3, sources.Count);
        var clan = sources.Single(s => s.Recipe == installed[0].Recipe.Slug);
        Assert.Equal((SourceRole.Main, "CCGP"), (clan.Role, clan.Inputs["clan"]));
        Assert.Equal(SourceRole.Mine, sources.Single(s => s.Recipe == installed[1].Recipe.Slug).Role);
        Assert.Equal(SourceRole.Watch, sources.Single(s => s.Recipe == installed[2].Recipe.Slug).Role);
        Assert.All(sources, s => Assert.Matches("^s-[0-9a-f]{8}$", s.Id));
    }

    [Fact]
    public void MigrationLeavesExistingSourcesAndSkipsARecipeWithNoInputsSet()
    {
        var clan = Installed("petsim99-clan-battle.recipe.json");
        var existing = new[] { new Source("s-00000001", "somewhere-else", Clan("X"), SourceRole.Watch) };

        var sources = SourceRules.Migrate([clan], existing);

        Assert.Equal(existing, sources);
    }

    [Fact]
    public void AddingTheSameClanTwiceKeepsOneSourceAndUpdatesItsRole()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("NovaForge"), SourceRole.Watch);
        sources = SourceRules.Add(sources, "clan-recipe", Clan(" novaforge "), SourceRole.Mine);

        var source = Assert.Single(sources);
        Assert.Equal(SourceRole.Mine, source.Role);
    }

    [Fact]
    public void ThereIsOnlyOneMainPerRecipe()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
        sources = SourceRules.Add(sources, "clan-recipe", Clan("K0i2"), SourceRole.Mine);
        sources = SourceRules.Add(sources, "other-recipe", Clan("Elsewhere"), SourceRole.Main);
        var k0i2 = sources.Single(s => s.Inputs["clan"] == "K0i2");

        sources = SourceRules.MakeMain(sources, k0i2.Id);

        Assert.Equal(SourceRole.Mine, sources.Single(s => s.Inputs["clan"] == "CCGP").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Inputs["clan"] == "K0i2").Role);
        Assert.Equal(SourceRole.Main, sources.Single(s => s.Recipe == "other-recipe").Role);
    }

    [Fact]
    public void RemovingASourceOrARecipeTakesOnlyThose()
    {
        var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
        sources = SourceRules.Add(sources, "clan-recipe", Clan("K0i2"), SourceRole.Mine);
        sources = SourceRules.Add(sources, "profile", [], SourceRole.Mine);

        Assert.Equal(2, SourceRules.Remove(sources, sources[0].Id).Count);
        Assert.Equal("profile", Assert.Single(SourceRules.ForgetRecipe(sources, "clan-recipe")).Recipe);
    }

    [Fact]
    public void TheStoreRoundTripsAndABrokenFileLoadsAsNoSources()
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-sources-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sources.json");
        try
        {
            var store = new SourceStore(path);
            Assert.Empty(store.Load());

            var sources = SourceRules.Add([], "clan-recipe", Clan("CCGP"), SourceRole.Main);
            store.Save(sources);

            var loaded = Assert.Single(store.Load());
            Assert.Equal((sources[0].Id, "clan-recipe", SourceRole.Main, "CCGP", true),
                (loaded.Id, loaded.Recipe, loaded.Role, loaded.Inputs["clan"], loaded.Enabled));
            Assert.Contains("\"role\": \"main\"", File.ReadAllText(path));

            File.WriteAllText(path, "{ not json");
            Assert.Empty(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'Source' could not be found`.

- [ ] **Step 3: Create `src/Core/Sources.cs`**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>Score book spec §4.1.</summary>
public enum SourceRole
{
    /// <summary>The ★ clan. At most one per recipe; promotion checks measure other sources against it.</summary>
    Main,

    /// <summary>Your accounts are expected here: matched, sent and recorded.</summary>
    Mine,

    /// <summary>Clan-level only: never matched to your accounts, never sent, and no account recorded.</summary>
    Watch,
}

/// <summary>One installed recipe with one set of input values and a role.</summary>
public sealed record Source(string Id, string Recipe, IReadOnlyDictionary<string, string> Inputs, SourceRole Role, bool Enabled = true)
{
    /// <summary>The same clan typed twice with different case or spaces is one source.</summary>
    [JsonIgnore]
    public string InputsKey => KeyOf(Inputs);

    public static string KeyOf(IReadOnlyDictionary<string, string> inputs) =>
        string.Join('\u001f', inputs
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.Trim().ToLowerInvariant()}"));
}

/// <summary><c>sources.json</c>. Holds recipe slugs, input values and roles: never a key, never an account.</summary>
public sealed class SourceStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "sources.json");

    /// <summary>A missing or hand-broken file is no sources; migration rebuilds what it can from recipe state.</summary>
    public IReadOnlyList<Source> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];

            var loaded = JsonSerializer.Deserialize<List<Source>>(File.ReadAllText(path), Options) ?? [];
            return [.. loaded.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Recipe) && s.Inputs is not null)];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<Source> sources)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sources, Options));
        File.Move(temp, path, overwrite: true);
    }
}

public static class SourceRules
{
    /// <summary>
    /// Every installed recipe that has no source yet gets one when it can: a recipe without inputs gets
    /// one (watch for a group list, mine otherwise), and a recipe whose saved inputs are all filled gets
    /// its main (Ruling R1). Existing sources are kept as they are, including ones whose recipe didn't
    /// load this time, so a recipe file that briefly fails to parse doesn't cost the user's clans.
    /// </summary>
    public static IReadOnlyList<Source> Migrate(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> existing)
    {
        var result = existing.ToList();
        foreach (var item in installed)
        {
            var recipe = item.Recipe;
            if (result.Any(s => s.Recipe == recipe.Slug)) continue;

            if (recipe.Inputs.Count == 0)
            {
                result.Add(new Source(NewId(), recipe.Slug, new Dictionary<string, string>(),
                    recipe.IsGroupList ? SourceRole.Watch : SourceRole.Mine));
                continue;
            }

            var values = item.State.InputValues;
            if (recipe.Inputs.All(i => values.TryGetValue(i.Id, out var v) && !string.IsNullOrWhiteSpace(v)))
            {
                result.Add(new Source(NewId(), recipe.Slug,
                    recipe.Inputs.ToDictionary(i => i.Id, i => values[i.Id].Trim(), StringComparer.Ordinal), SourceRole.Main));
            }
        }

        return result;
    }

    /// <summary>The same recipe and inputs update the existing source's role; a new pair becomes a new source.</summary>
    public static IReadOnlyList<Source> Add(IReadOnlyList<Source> sources, string recipe, IReadOnlyDictionary<string, string> inputs, SourceRole role)
    {
        var key = Source.KeyOf(inputs);
        var list = sources.ToList();
        var index = list.FindIndex(s => s.Recipe == recipe && s.InputsKey == key);

        string id;
        if (index >= 0)
        {
            list[index] = list[index] with { Role = role, Enabled = true };
            id = list[index].Id;
        }
        else
        {
            id = NewId();
            list.Add(new Source(id, recipe,
                inputs.ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal), role));
        }

        return role == SourceRole.Main ? MakeMain(list, id) : list;
    }

    public static IReadOnlyList<Source> MakeMain(IReadOnlyList<Source> sources, string sourceId)
    {
        var target = sources.FirstOrDefault(s => s.Id == sourceId);
        if (target is null) return sources;

        return [.. sources.Select(s =>
            s.Id == sourceId ? s with { Role = SourceRole.Main }
            : s.Recipe == target.Recipe && s.Role == SourceRole.Main ? s with { Role = SourceRole.Mine }
            : s)];
    }

    public static IReadOnlyList<Source> Remove(IReadOnlyList<Source> sources, string sourceId) =>
        [.. sources.Where(s => s.Id != sourceId)];

    public static IReadOnlyList<Source> ForgetRecipe(IReadOnlyList<Source> sources, string recipe) =>
        [.. sources.Where(s => s.Recipe != recipe)];

    public static string NewId() => "s-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Sources.cs tests/SourcesTests.cs
git commit -m "core: sources, one recipe plus inputs plus a role, kept in sources.json

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Shared accounts, account claims and spaced requests

Spec §4.2 and §5.5. Every watch shares one account list, fetched at most every 30 s. It falls back to the last list saved on disk when RoRoRo is closed. Requests to one host go one at a time, at least 2 s apart.

**Files:**
- Create: `src/Core/SharedAccounts.cs`
- Create: `src/Core/AccountClaims.cs`
- Create: `src/Recipes/SpacedTransport.cs`
- Create: `tests/TestDoubles.cs`
- Create: `tests/SharedAccountsTests.cs`

**Interfaces:**
- Consumes: `IHostClient`, `HostAccount`, `IRecipeTransport`, `FetchResult`.
- Produces, exactly as in the contract:
  - `AccountsCache`, including `SavedAt()`;
  - `AccountList`;
  - `SharedAccounts`, with `Window` and `Last`;
  - `AccountClaims.TryClaim`;
  - `SpacedTransport`, with `DefaultSpacing`.
- Produces for later tests (in `tests/TestDoubles.cs`): `ManualTime`, `StubHost`, `NoKeys` and `StubEngine`.

- [ ] **Step 1: Create the shared test doubles**

Create `tests/TestDoubles.cs`:

```csharp
using Grpc.Core;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class StubHost(bool reachable, params HostAccount[] accounts) : IHostClient
{
    public bool Reachable { get; set; } = reachable;

    public bool DenyAccounts { get; set; }

    public int AccountCalls { get; private set; }

    public List<(Guid Subject, string MetricId, double Value, DateTimeOffset ObservedAt)> Reported { get; } = [];

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(Reachable);

    public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        AccountCalls++;
        return DenyAccounts
            ? throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked"))
            : Task.FromResult<IReadOnlyList<HostAccount>>(accounts);
    }

    public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        Reported.Add((subject, metricId, value, observedAt));
        return Task.CompletedTask;
    }
}

internal sealed class NoKeys : IKeyStore
{
    public SavedKey? Find(string keyId) => null;

    public void Save(string keyId, string host, string value) => throw new NotSupportedException();

    public bool Remove(string keyId) => false;

    public IReadOnlyCollection<string> Values() => [];
}

internal sealed class StubEngine(Func<RecipeReading> read) : IRecipeEngine
{
    public Func<RecipeReading> Read { get; set; } = read;

    public int Calls { get; private set; }

    public IReadOnlyCollection<long> LastIds { get; private set; } = [];

    public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
        IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken cancellationToken)
    {
        Calls++;
        LastIds = accountUserIds;
        return Task.FromResult(Read());
    }
}

internal static class TempDir
{
    /// <summary>A fresh folder under the system temp path, deleted when the returned scope is disposed.</summary>
    public static Scope Create(string prefix) => new(Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}"));

    internal sealed class Scope : IDisposable
    {
        public Scope(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/SharedAccountsTests.cs`:

```csharp
using System.Diagnostics;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SharedAccountsTests
{
    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private static readonly DateTimeOffset Start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneFetchServesEveryWatchForThirtySecondsAndIsSaved()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        var host = new StubHost(true, Alt);
        var time = new ManualTime(Start);
        var shared = new SharedAccounts(host, cache, time);

        var first = await shared.GetAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(29));
        await shared.GetAsync(CancellationToken.None);

        Assert.Equal(1, host.AccountCalls);
        Assert.Equal((true, false, false, (DateTimeOffset?)Start), (first.HostUp, first.FromCache, first.Denied, first.ListedAt));
        Assert.Equal(Alt, Assert.Single(cache.Load()));

        time.Advance(TimeSpan.FromSeconds(2));
        await shared.GetAsync(CancellationToken.None);
        Assert.Equal(2, host.AccountCalls);
    }

    [Fact]
    public async Task WhenRoRoRoIsClosedTheSavedAccountsAreUsed()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([Alt]);

        var list = await new SharedAccounts(new StubHost(false), cache, new ManualTime(Start)).GetAsync(CancellationToken.None);

        Assert.Equal((false, true), (list.HostUp, list.FromCache));
        Assert.Equal(Alt, Assert.Single(list.Accounts));
        Assert.NotNull(list.ListedAt);
    }

    [Fact]
    public async Task ARefusedAccountListIsDeniedAndEmpty()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var host = new StubHost(true, Alt) { DenyAccounts = true };

        var list = await new SharedAccounts(host, new AccountsCache(Path.Combine(dir.Path, "a.json")), new ManualTime(Start)).GetAsync(CancellationToken.None);

        Assert.True(list.Denied);
        Assert.Empty(list.Accounts);
    }

    [Fact]
    public void ACacheThatIsMissingOrBrokenLoadsEmpty()
    {
        using var dir = TempDir.Create("urscore-accounts");
        var path = Path.Combine(dir.Path, "accounts.json");
        Assert.Empty(new AccountsCache(path).Load());
        Assert.Null(new AccountsCache(path).SavedAt());

        File.WriteAllText(path, "[ nope");
        Assert.Empty(new AccountsCache(path).Load());
    }

    [Fact]
    public void AnAccountBelongsToTheFirstSourceThatClaimsItUntilTheClaimGoesStale()
    {
        var time = new ManualTime(Start);
        var claims = new AccountClaims(time);
        var window = TimeSpan.FromMinutes(6);

        Assert.True(claims.TryClaim("clan", 111, "s-1", window));
        Assert.False(claims.TryClaim("clan", 111, "s-2", window));
        Assert.True(claims.TryClaim("clan", 111, "s-1", window));
        Assert.True(claims.TryClaim("other-recipe", 111, "s-2", window));

        time.Advance(TimeSpan.FromMinutes(7));
        Assert.True(claims.TryClaim("clan", 111, "s-2", window));
        Assert.False(claims.TryClaim("clan", 111, "s-1", window));
    }

    private sealed class CountingTransport : IRecipeTransport
    {
        private int _inFlight;

        public int MaxInFlight;

        public List<(string Host, long At)> Starts { get; } = [];

        public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (Starts)
            {
                MaxInFlight = Math.Max(MaxInFlight, now);
                Starts.Add((url.Host, Stopwatch.GetTimestamp()));
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            return new FetchResult(200, "{}", null);
        }
    }

    [Fact]
    public async Task RequestsToOneHostGoOneAtATimeAndSpacedButOtherHostsDontWait()
    {
        var inner = new CountingTransport();
        var spacing = TimeSpan.FromMilliseconds(150);
        var transport = new SpacedTransport(inner, TimeProvider.System, spacing);
        var headers = new Dictionary<string, string>();

        await Task.WhenAll(
            transport.GetAsync(new Uri("https://one.example/a"), headers, "a", CancellationToken.None),
            transport.GetAsync(new Uri("https://one.example/b"), headers, "b", CancellationToken.None),
            transport.GetAsync(new Uri("https://two.example/c"), headers, "c", CancellationToken.None));

        var one = inner.Starts.Where(s => s.Host == "one.example").Select(s => s.At).Order().ToList();
        Assert.Equal(2, one.Count);
        Assert.True(Stopwatch.GetElapsedTime(one[0], one[1]) >= spacing, "the second request to one host started too soon");
        Assert.Contains(inner.Starts, s => s.Host == "two.example");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'AccountsCache' could not be found`.

- [ ] **Step 4: Create `src/Core/SharedAccounts.cs`**

```csharp
using System.IO;
using System.Text.Json;
using Grpc.Core;
using Labs626.UrScore.Host;

namespace Labs626.UrScore.Core;

/// <summary>
/// <c>accounts.json</c>: the user's own RoRoRo accounts as last listed, so reading and recording go on while
/// RoRoRo is closed (score book spec §5.5). Only the user's accounts; never a key or cookie.
/// </summary>
public sealed class AccountsCache(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "accounts.json");

    public IReadOnlyList<HostAccount> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<HostAccount>>(File.ReadAllText(path), Options) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<HostAccount> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(accounts, Options));
        File.Move(temp, path, overwrite: true);
    }

    public DateTimeOffset? SavedAt() => File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
}

/// <summary>What one account fetch found. <see cref="ListedAt"/> is when RoRoRo last listed these accounts.</summary>
public sealed record AccountList(IReadOnlyList<HostAccount> Accounts, bool HostUp, bool FromCache, bool Denied, DateTimeOffset? ListedAt);

/// <summary>
/// One account fetch shared by every watch within <see cref="Window"/>, so five sources are one question
/// to RoRoRo, not five (score book spec §4.2).
/// </summary>
public sealed class SharedAccounts(IHostClient host, AccountsCache cache, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _one = new(1, 1);

    private DateTimeOffset _fetchedAt;

    public AccountList? Last { get; private set; }

    public async Task<AccountList> GetAsync(CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = time.GetUtcNow();
            if (Last is not null && now - _fetchedAt < Window) return Last;

            AccountList list;
            if (await host.IsReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
                    TrySave(accounts);
                    list = new AccountList(accounts, HostUp: true, FromCache: false, Denied: false, ListedAt: now);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
                {
                    list = new AccountList([], HostUp: true, FromCache: false, Denied: true, ListedAt: Last?.ListedAt);
                }
            }
            else
            {
                var cached = cache.Load();
                list = new AccountList(cached, HostUp: false, FromCache: true, Denied: false, ListedAt: Last?.ListedAt ?? cache.SavedAt());
            }

            Last = list;
            _fetchedAt = now;
            return list;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>A cache that can't be written costs only the fallback, never the read.</summary>
    private void TrySave(IReadOnlyList<HostAccount> accounts)
    {
        try
        {
            cache.Save(accounts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
```

- [ ] **Step 5: Create `src/Core/AccountClaims.cs`**

```csharp
namespace Labs626.UrScore.Core;

/// <summary>
/// Ruling R6 (score book spec §4.2): when two sources of the same recipe both see one of your accounts,
/// such as an alt mid-way through a clan switch, the account belongs to whichever source claimed it first,
/// until that claim is older than the window. One account is then sent and kept once per recipe.
/// </summary>
public sealed class AccountClaims(TimeProvider time)
{
    private readonly object _gate = new();

    private readonly Dictionary<(string Recipe, long UserId), (string SourceId, DateTimeOffset At)> _claims = [];

    public bool TryClaim(string recipe, long userId, string sourceId, TimeSpan window)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_claims.TryGetValue((recipe, userId), out var claim) && claim.SourceId != sourceId && now - claim.At < window)
            {
                return false;
            }

            _claims[(recipe, userId)] = (sourceId, now);
            return true;
        }
    }
}
```

- [ ] **Step 6: Create `src/Recipes/SpacedTransport.cs`**

```csharp
using System.Collections.Concurrent;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// Several sources share one host (score book spec §4.2). Requests to a host go one at a time and at least
/// <c>spacing</c> apart, so five clan watches are five calls in turn, never a burst against someone else's API.
/// </summary>
public sealed class SpacedTransport(IRecipeTransport inner, TimeProvider time, TimeSpan spacing) : IRecipeTransport
{
    public static readonly TimeSpan DefaultSpacing = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, Lane> _lanes = new(StringComparer.OrdinalIgnoreCase);

    public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
    {
        var lane = _lanes.GetOrAdd(url.Host, _ => new Lane());
        await lane.One.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = lane.LastFinished + spacing - time.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);

            return await inner.GetAsync(url, headers, label, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lane.LastFinished = time.GetUtcNow();
            lane.One.Release();
        }
    }

    private sealed class Lane
    {
        public readonly SemaphoreSlim One = new(1, 1);

        public DateTimeOffset LastFinished = DateTimeOffset.MinValue;
    }
}
```

**Two things to know:**
- **`DateTimeOffset.MinValue + spacing` doesn't overflow**, because spacing is positive and small.
- **`url.Host` is a value read at run time, not a hostname literal,** so `NoHostnameFenceTests` stays green.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Core/SharedAccounts.cs src/Core/AccountClaims.cs src/Recipes/SpacedTransport.cs tests/TestDoubles.cs tests/SharedAccountsTests.cs
git commit -m "core: one shared account list with a saved fallback, account claims, and spaced requests per host

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: The score book writer

Spec §5.1–§5.3, §5.7. Lines are JSON, one per line, written through one queue. The writer tolerates a crash, a file locked by another program, and a missing final newline.

**Files:**
- Create: `src/Book/BookLine.cs`
- Create: `src/Book/BookFiles.cs`
- Create: `src/Book/ScoreBook.cs`
- Modify: `tests/TestDoubles.cs` (add `MemoryBook`)
- Create: `tests/ScoreBookTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces, exactly as in the contract: `BookRecipeRef`, `BookPeriod`, `BookAccount`, `BookLine` (with its constants), `BookJson`, `BookFiles`, `IScoreBook` and `ScoreBook`.

- [ ] **Step 1: Add `MemoryBook` to `tests/TestDoubles.cs`**

Add `using Labs626.UrScore.Book;` at the top, and this class at the end of the file:

```csharp
internal sealed class MemoryBook : IScoreBook
{
    public List<BookLine> Lines { get; } = [];

    public List<string> RecipeTexts { get; } = [];

    public event Action<BookLine>? Written;

    public int Pending => 0;

    public int Dropped => 0;

    public string Root => "memory";

    public void Append(BookLine line, string recipeText)
    {
        Lines.Add(line);
        RecipeTexts.Add(recipeText);
        Written?.Invoke(line);
    }

    public void Flush()
    {
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ScoreBookTests.cs`:

```csharp
using System.Text;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ScoreBookTests
{
    private const string Slug = "pet-sim-99-clan-battle-points";

    private static BookLine Line(DateTimeOffset t, string kind = BookLine.KindRead, double points = 12418220) => new(
        BookLine.Version, kind, t, -300, BookLine.TriggerTimer,
        new BookRecipeRef(Slug, BookFiles.Hash("recipe text")), "s-7f3a0001", "mine",
        new Dictionary<string, string> { ["clan"] = "K0i2" },
        new BookPeriod("ArcadeBattle2026", new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), null),
        new Dictionary<string, double> { ["clan-place"] = 212, ["clan-points"] = 18902110 },
        ["value"],
        new Dictionary<string, BookAccount>
        {
            ["1647274201"] = new(new Dictionary<string, double> { ["value"] = points }, new Dictionary<string, int> { ["value"] = 1 }, 48),
        });

    private static readonly DateTimeOffset T = new(2026, 9, 19, 18, 3, 0, 412, TimeSpan.Zero);

    [Fact]
    public void ALineSerializesToTheSpecShapeAndParsesBack()
    {
        var json = BookJson.Serialize(Line(T));

        Assert.StartsWith("""{"v":1,"kind":"read","t":"2026-09-19T18:03:00.412Z","off":-300,"trigger":"timer",""", json);
        Assert.Contains(""""recipe":{"slug":"pet-sim-99-clan-battle-points","hash":""", json);
        Assert.Contains(""""period":{"value":"ArcadeBattle2026","starts":"2026-08-29T18:00:00.000Z"}""", json);
        Assert.Contains(""""accounts":{"1647274201":{"v":{"value":12418220},"rank":{"value":1},"of":48}}""", json);
        Assert.DoesNotContain("unavail", json);
        Assert.DoesNotContain("\n", json);

        var back = BookJson.TryParse(json)!;
        Assert.Equal((T, "K0i2", 12418220d, 48), (back.T, back.Inputs["clan"], back.Accounts["1647274201"].V["value"], back.Accounts["1647274201"].Of!.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("""{"v":2,"kind":"read"}""")]
    [InlineData("{\"v\":1,\"kind\":\"read\"\0}")]
    public void ALineThatIsBrokenOrFromANewerFormatIsSkipped(string text) => Assert.Null(BookJson.TryParse(text));

    [Fact]
    public void TheHashIsSixteenLowercaseHexDigitsOfTheRecipeText()
    {
        Assert.Matches("^[0-9a-f]{16}$", BookFiles.Hash("recipe text"));
        Assert.NotEqual(BookFiles.Hash("recipe text"), BookFiles.Hash("recipe text 2"));
    }

    [Fact]
    public void EachAppendIsOneLineAndTheRecipeTextIsKeptOncePerHash()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);

        book.Append(Line(T), "recipe text");
        book.Append(Line(T.AddMinutes(3)), "recipe text");

        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Assert.Equal(2, File.ReadAllLines(file).Length);
        Assert.Single(Directory.GetFiles(Path.Combine(dir.Path, Slug, "recipes")));
        Assert.Equal("recipe text", File.ReadAllText(BookFiles.RecipeFile(dir.Path, Slug, BookFiles.Hash("recipe text"))));
        Assert.Equal(2, BookFiles.ReadAll(dir.Path, Slug).Count());
        Assert.Equal(new[] { Slug }, BookFiles.Slugs(dir.Path));
    }

    [Fact]
    public void AHalfWrittenLastLineIsClosedOffAndSkipped()
    {
        using var dir = TempDir.Create("urscore-book");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, BookJson.Serialize(Line(T.AddMinutes(-3))) + "\n{\"v\":1,\"kind\":\"re", new UTF8Encoding(false));

        using var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T), "recipe text");

        Assert.Equal(new[] { T.AddMinutes(-3), T }, BookFiles.ReadAll(dir.Path, Slug).Select(l => l.T).ToArray());
    }

    [Fact]
    public void NulRunsFromAPowerLossAreSkipped()
    {
        using var dir = TempDir.Create("urscore-book");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new string('\0', 40) + "\n" + BookJson.Serialize(Line(T)) + "\n", new UTF8Encoding(false));

        Assert.Single(BookFiles.ReadAll(dir.Path, Slug));
    }

    [Fact]
    public void ALockedFileKeepsTheLineUntilItCanBeWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T.AddMinutes(-3)), "recipe text");

        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            book.Append(Line(T), "recipe text");
            Assert.Equal(1, book.Pending);
        }

        book.Flush();
        Assert.Equal(0, book.Pending);
        Assert.Equal(2, BookFiles.ReadAll(dir.Path, Slug).Count());
    }

    [Fact]
    public void PastTheLimitTheOldestReadingsAreDroppedButNeverAFinal()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T), "recipe text");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            book.Append(Line(T, BookLine.KindFinal), "recipe text");
            for (var i = 0; i < ScoreBook.MaxPending + 10; i++) book.Append(Line(T.AddSeconds(i + 1)), "recipe text");

            Assert.Equal(ScoreBook.MaxPending, book.Pending);
            Assert.Equal(11, book.Dropped);
        }

        book.Flush();
        Assert.Contains(BookFiles.ReadAll(dir.Path, Slug), l => l.Kind == BookLine.KindFinal);
    }

    [Fact]
    public void TheMonthFileFollowsUtc()
    {
        var lateLocal = new DateTimeOffset(2026, 9, 30, 21, 30, 0, TimeSpan.FromHours(-5));
        Assert.EndsWith(Path.Combine(Slug, "2026-10.jsonl"), BookFiles.MonthFile("root", Slug, lateLocal));
    }

    [Fact]
    public void RemovingARecipeLeavesItsBook()
    {
        using var dir = TempDir.Create("urscore-book");
        var store = new RecipeStore(Path.Combine(dir.Path, "recipes"));
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        store.Save(recipe, text, new RecipeState());

        var root = Path.Combine(dir.Path, "scorebook");
        using var book = new ScoreBook(root, background: false);
        book.Append(Line(T), text);

        store.Remove(recipe.Slug);

        Assert.Single(BookFiles.ReadAll(root, Slug));
    }

    [Fact]
    public void TheBackgroundWriterWritesAndRaisesWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        var written = new TaskCompletionSource<BookLine>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var book = new ScoreBook(dir.Path))
        {
            book.Written += line => written.TrySetResult(line);
            book.Append(Line(T), "recipe text");
            Assert.True(written.Task.Wait(TimeSpan.FromSeconds(5)), "the background writer never wrote the line");
        }

        Assert.Single(BookFiles.ReadAll(dir.Path, Slug));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'Book' does not exist in the namespace 'Labs626.UrScore'`.

- [ ] **Step 4: Create `src/Book/BookLine.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs626.UrScore.Book;

public sealed record BookRecipeRef(string Slug, string Hash);

public sealed record BookPeriod(string Value, DateTimeOffset? Starts = null, DateTimeOffset? Ends = null);

/// <summary>One of the user's accounts on a line: its stats by key, and on list recipes its rank among every row.</summary>
public sealed record BookAccount(
    IReadOnlyDictionary<string, double> V,
    IReadOnlyDictionary<string, int>? Rank = null,
    int? Of = null,
    DateTimeOffset? AsOf = null,
    bool? Stale = null);

/// <summary>One line of the score book (score book spec §5.2, §5.3).</summary>
public sealed record BookLine(
    int V,
    string Kind,
    DateTimeOffset T,
    int Off,
    string Trigger,
    BookRecipeRef Recipe,
    string Source,
    string Role,
    IReadOnlyDictionary<string, string> Inputs,
    BookPeriod? Period,
    IReadOnlyDictionary<string, double> Headline,
    IReadOnlyList<string> Stats,
    IReadOnlyDictionary<string, BookAccount> Accounts,
    IReadOnlyList<string>? Unavail = null,
    DateTimeOffset? AsOf = null,
    bool? Stale = null)
{
    public const int Version = 1;

    public const string KindRead = "read";
    public const string KindFinal = "final";

    public const string TriggerStart = "start";
    public const string TriggerTimer = "timer";
    public const string TriggerManual = "manual";
    public const string TriggerBackfill = "backfill";
    public const string TriggerEnded = "ended";
}

public static class BookJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new UtcTimeConverter() },
    };

    public static string Serialize(BookLine line) => JsonSerializer.Serialize(line, Options);

    /// <summary>A line that isn't valid JSON, carries NUL, or has an unknown <c>v</c> is null, and a reader skips it.</summary>
    public static BookLine? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return null;

        try
        {
            var line = JsonSerializer.Deserialize<BookLine>(text, Options);
            return line is { V: BookLine.Version, Kind: not null, Recipe: not null, Inputs: not null, Headline: not null, Stats: not null, Accounts: not null }
                ? line
                : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Always UTC with a Z and milliseconds, so lines sort and compare as text.</summary>
    private sealed class UtcTimeConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
```

`JsonConverter<DateTimeOffset>` also handles `DateTimeOffset?`, because System.Text.Json wraps the converter for nullable values.

- [ ] **Step 5: Create `src/Book/BookFiles.cs`**

```csharp
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Labs626.UrScore.Book;

public static class BookFiles
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "scorebook");

    public static string Hash(string recipeText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipeText)))[..16].ToLowerInvariant();

    public static string MonthFile(string root, string slug, DateTimeOffset t) =>
        Path.Combine(root, slug, t.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");

    public static string RecipeFile(string root, string slug, string hash) => Path.Combine(root, slug, "recipes", hash + ".json");

    /// <summary>Recipe slugs that have a book. A slug is letters, digits and hyphens, so it is always a safe folder name.</summary>
    public static IReadOnlyList<string> Slugs(string root) => Directory.Exists(root)
        ? [.. Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    /// <summary>Every readable line for a slug, oldest file first. A file another program holds open is still read.</summary>
    public static IEnumerable<BookLine> ReadAll(string root, string slug)
    {
        var folder = Path.Combine(root, slug);
        if (!Directory.Exists(folder)) yield break;

        foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl").Order(StringComparer.Ordinal))
        {
            List<string> texts;
            try
            {
                texts = ReadShared(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var text in texts)
            {
                if (BookJson.TryParse(text) is { } line) yield return line;
            }
        }
    }

    public static long Bytes(string root, string slug)
    {
        var folder = Path.Combine(root, slug);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.jsonl").Sum(f => new FileInfo(f).Length) : 0;
    }

    private static List<string> ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var texts = new List<string>();
        while (reader.ReadLine() is { } text)
        {
            if (text.Length > 0) texts.Add(text);
        }

        return texts;
    }
}
```

- [ ] **Step 6: Create `src/Book/ScoreBook.cs`**

```csharp
using System.IO;
using System.Text;

namespace Labs626.UrScore.Book;

public interface IScoreBook
{
    void Append(BookLine line, string recipeText);

    /// <summary>Raised after a line reaches disk, on the writer's thread.</summary>
    event Action<BookLine>? Written;

    int Pending { get; }

    int Dropped { get; }

    void Flush();

    string Root { get; }
}

/// <summary>
/// The only code that writes the score book (score book spec §5.7). One queue with one consumer: two watches
/// never interleave bytes, a file another program holds keeps its lines in memory until it's free, and
/// past <see cref="MaxPending"/> the oldest reading lines are dropped, never a final.
/// </summary>
public sealed class ScoreBook : IScoreBook, IDisposable
{
    public const int MaxPending = 5000;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly object _drainGate = new();
    private readonly LinkedList<(BookLine Line, string RecipeText)> _pending = new();
    private readonly bool _background;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread? _thread;
    private volatile bool _stopping;
    private int _dropped;

    public ScoreBook(string root, bool background = true)
    {
        Root = root;
        _background = background;
        if (background)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Ur Score book writer" };
            _thread.Start();
        }
    }

    public string Root { get; }

    public event Action<BookLine>? Written;

    public int Pending
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    public int Dropped => Volatile.Read(ref _dropped);

    public void Append(BookLine line, string recipeText)
    {
        lock (_gate)
        {
            _pending.AddLast((line, recipeText));
            while (_pending.Count > MaxPending)
            {
                var node = _pending.First;
                while (node is not null && node.Value.Line.Kind != BookLine.KindRead) node = node.Next;
                if (node is null) break;

                _pending.Remove(node);
                Interlocked.Increment(ref _dropped);
            }
        }

        if (_background) _signal.Set();
        else Drain();
    }

    public void Flush() => Drain();

    public void Dispose()
    {
        _stopping = true;
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        Drain();
        _signal.Dispose();
    }

    private void Run()
    {
        while (!_stopping)
        {
            _signal.WaitOne(RetryDelay);
            Drain();
        }
    }

    private void Drain()
    {
        lock (_drainGate)
        {
            while (true)
            {
                LinkedListNode<(BookLine Line, string RecipeText)>? node;
                lock (_gate) node = _pending.First;
                if (node is null) return;

                try
                {
                    Write(node.Value.Line, node.Value.RecipeText);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return; // stays pending; the next append, flush or retry tries again
                }

                lock (_gate)
                {
                    if (node.List is not null) _pending.Remove(node);
                }

                Written?.Invoke(node.Value.Line);
            }
        }
    }

    private void Write(BookLine line, string recipeText)
    {
        var slug = line.Recipe.Slug;

        var recipeFile = BookFiles.RecipeFile(Root, slug, line.Recipe.Hash);
        if (!File.Exists(recipeFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recipeFile)!);
            File.WriteAllText(recipeFile, recipeText, Utf8);
        }

        var file = BookFiles.MonthFile(Root, slug, line.T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        var bytes = new List<byte>(512);
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n') bytes.Add((byte)'\n');
        }

        bytes.AddRange(Utf8.GetBytes(BookJson.Serialize(line)));
        bytes.Add((byte)'\n');

        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes.ToArray());
        stream.Flush(flushToDisk: true);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS. `PastTheLimitTheOldestReadingsAreDroppedButNeverAFinal` appends 5,011 lines to memory; it runs in well under a second.

- [ ] **Step 8: Commit**

```bash
git add src/Book/ tests/ScoreBookTests.cs tests/TestDoubles.cs
git commit -m "book: the score book writer, JSON lines through one queue that survives locks and crashes

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Lines, and every read kept by its watch

Spec §5.2, §5.4–§5.6. `LineBuilder` holds the privacy rules. `RecipeWatch` records every successful read, including while RoRoRo is closed and when no stat is sent.

**Files:**
- Create: `src/Book/Ranking.cs`
- Create: `src/Book/LineBuilder.cs`
- Create: `src/Book/Finals.cs`, with only the `FinalsIndex`/`FinalsPlanner` shells described in Step 5. Task 7 fills them in.
- Replace: `src/Core/RecipeWatch.cs`
- Create: `tests/LineBuilderTests.cs`
- Create: `tests/RecipeWatchBookTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–5.
- Produces, exactly as in the contract:
  - `Ranking.Competition`, `ReadContext`, `LineBuilder.Reading`, `LineBuilder.Final`;
  - the new optional `RecipeWatch` constructor parameters;
  - `RunOnceAsync(ct, trigger)`, `UpdateRecipe(..., newRecipeText)`, `UpdateSource`, `Source`;
  - the `RecipeSnapshot` init properties `Recorded`, `NotRecordingReason`, `Period`, `Groups` and `SourceId`.

- [ ] **Step 1: Write the failing `LineBuilder` tests**

Create `tests/LineBuilderTests.cs`:

```csharp
using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class LineBuilderTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Recipe TopClans => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;

    private static Source SourceOf(Recipe recipe, SourceRole role) =>
        new("s-00000001", recipe.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, role);

    private static ReadContext Context(Recipe recipe, SourceRole role = SourceRole.Mine) =>
        new(SourceOf(recipe, role), recipe, "3f9a1c0b7e2d4a55", BookLine.TriggerTimer, new DateTimeOffset(2026, 9, 19, 18, 3, 0, TimeSpan.Zero), -300);

    private static RecipeRow Row(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    private static HeadlineValue Head(string id, double? number) => new(id, number?.ToString(CultureInfo.InvariantCulture)) { Id = id, Number = number };

    private static readonly HashSet<string> Points = ["value"];

    private static RecipeReading ClanReading(IReadOnlyList<RecipeRow> rows, params HeadlineValue[] headline) =>
        new(ReadingOutcome.Read, null, rows, headline, "battle=B", rows.Count) { Period = new ReadingPeriod("B", null, null) };

    [Fact]
    public void OnlyYourAccountsOfFiftyRowsAreWrittenWithTheirRank()
    {
        var rows = Enumerable.Range(1, 50).Select(i => Row(7_000_000 + i, i * 100)).ToList();
        var map = new Dictionary<long, Guid> { [7_000_050] = A, [7_000_001] = B };

        var line = LineBuilder.Reading(Context(Clan), ClanReading(rows, Head("clan-place", 14), Head("clan-points", 30214400)), map, Points)!;

        Assert.Equal(new[] { "7000001", "7000050" }, line.Accounts.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((1, 50), (line.Accounts["7000050"].Rank!["value"], line.Accounts["7000050"].Of!.Value));
        Assert.Equal(50, line.Accounts["7000001"].Rank!["value"]);
        Assert.Equal(new BookPeriod("B"), line.Period);

        var json = BookJson.Serialize(line);
        foreach (var other in rows.Select(r => r.UserId).Where(id => !map.ContainsKey(id)))
        {
            Assert.DoesNotContain(other.ToString(CultureInfo.InvariantCulture), json);
        }
    }

    [Fact]
    public void RanksAreCompetitionStyle()
    {
        var ranks = Ranking.Competition([Row(1, 10), Row(2, 20), Row(3, 20), Row(4, 5), new RecipeRow(5, new Dictionary<string, double>())], "value");

        Assert.Equal(new Dictionary<long, int> { [1] = 3, [2] = 1, [3] = 1, [4] = 4 }, ranks);
    }

    [Fact]
    public void TextHeadlinesAndHeadlinesEqualToAnotherPlayersIdAreDropped()
    {
        var rows = new[] { Row(7_000_001, 5), Row(7_000_002, 9) };
        var owner = new HeadlineValue("Owner", "7000002") { Id = "owner", Number = 7_000_002 };
        var motto = new HeadlineValue("Motto", "Go") { Id = "motto", Number = null };

        var line = LineBuilder.Reading(Context(Clan), ClanReading(rows, Head("clan-place", 14), owner, motto),
            new Dictionary<long, Guid> { [7_000_001] = A }, Points)!;

        Assert.Equal(new Dictionary<string, double> { ["clan-place"] = 14 }, line.Headline);
    }

    [Fact]
    public void ShowOnlyStatsAreWrittenAndUntickedOnesAreNot()
    {
        var row = new RecipeRow(7_000_001, new Dictionary<string, double> { ["value"] = 5, ["eggs"] = 9, ["secret"] = 1 });

        var line = LineBuilder.Reading(Context(Clan), ClanReading([row]), new Dictionary<long, Guid> { [7_000_001] = A }, new HashSet<string> { "value", "eggs" })!;

        Assert.Equal(new Dictionary<string, double> { ["value"] = 5, ["eggs"] = 9 }, line.Accounts["7000001"].V);
        Assert.Equal(new[] { "eggs", "value" }, line.Stats);
    }

    [Fact]
    public void UnavailableHoldsOnlyYourIdsAndAPerAccountLineCarriesEachAccountsSourceTime()
    {
        var stamp = new AsOfStamp(new DateTimeOffset(2026, 9, 19, 17, 40, 0, TimeSpan.Zero), true);
        var reading = new RecipeReading(ReadingOutcome.Read, null,
            [new RecipeRow(1, new Dictionary<string, double> { ["diamonds"] = 5 })], [], null, 3)
        {
            Unavailable = new Dictionary<long, string> { [2] = "Profile is private.", [99] = "Profile is private." },
            AccountAsOf = new Dictionary<long, AsOfStamp> { [1] = stamp },
        };
        var map = new Dictionary<long, Guid> { [1] = A, [2] = B };

        var line = LineBuilder.Reading(Context(Profile), reading, map, new HashSet<string> { "diamonds" })!;

        Assert.Equal(new[] { "2" }, line.Unavail);
        Assert.Equal((stamp.Time, (bool?)true), (line.Accounts["1"].AsOf!.Value, line.Accounts["1"].Stale));
        Assert.Null(line.Accounts["1"].Rank);
        Assert.Null(line.Accounts["1"].Of);
    }

    [Fact]
    public void AWatchSourceKeepsTheHeadlineAndNoAccounts()
    {
        var line = LineBuilder.Reading(Context(Clan, SourceRole.Watch), ClanReading([Row(7_000_001, 5)], Head("clan-points", 900)),
            new Dictionary<long, Guid> { [7_000_001] = A }, Points)!;

        Assert.Empty(line.Accounts);
        Assert.Equal(900, line.Headline["clan-points"]);
        Assert.Equal("watch", line.Role);
    }

    [Fact]
    public void NothingOfYoursAndNoHeadlineIsNoLine() =>
        Assert.Null(LineBuilder.Reading(Context(Clan), ClanReading([Row(7_000_001, 5)]), new Dictionary<long, Guid>(), Points));

    [Fact]
    public void GroupListsAndStoppedReadsAreNeverALine()
    {
        var groups = new RecipeReading(ReadingOutcome.Read, null, [], [], null, 1)
        {
            Groups = [new GroupRow("Aurelian", new Dictionary<string, double> { ["value"] = 1 }, 1)],
        };

        Assert.Null(LineBuilder.Reading(Context(TopClans, SourceRole.Watch), groups, new Dictionary<long, Guid>(), Points));
        Assert.Null(LineBuilder.Reading(Context(Clan), RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running"), new Dictionary<long, Guid> { [1] = A }, Points));
    }

    [Fact]
    public void AFinalLineKeepsOnlyTheAccountsAskedForAndSkipsOnesWithNoTrackedValue()
    {
        var past = new PastPeriodReading("Cannon",
            [Row(7_000_001, 40210), Row(7_000_002, 30), new RecipeRow(7_000_003, new Dictionary<string, double>())],
            [Head("clan-place", 435), Head("clan-points", 67104)], RowsReadable: true);
        var map = new Dictionary<long, Guid> { [7_000_001] = A, [7_000_002] = B, [7_000_003] = Guid.NewGuid() };

        var all = LineBuilder.Final(Context(Clan), past, map, Points, null, BookLine.TriggerBackfill);
        var one = LineBuilder.Final(Context(Clan), past, map, Points, [7_000_002], BookLine.TriggerEnded);

        Assert.Equal((BookLine.KindFinal, BookLine.TriggerBackfill, "Cannon"), (all.Kind, all.Trigger, all.Period!.Value));
        Assert.Equal(new[] { "7000001", "7000002" }, all.Accounts.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((1, 3), (all.Accounts["7000001"].Rank!["value"], all.Accounts["7000001"].Of!.Value));
        Assert.Equal(new[] { "7000002" }, one.Accounts.Keys.ToArray());
        Assert.Equal(435, one.Headline["clan-place"]);
    }
}
```

- [ ] **Step 2: Write the failing watch tests**

Create `tests/RecipeWatchBookTests.cs`:

```csharp
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWatchBookTests
{
    private static readonly Guid Alt = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");

    private static readonly HostAccount AltAccount = new(Alt, 111, "Alt One");

    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe Clan => RecipeParser.Parse(ClanText).Recipe!;

    private static readonly Dictionary<string, string> Inputs = new() { ["clan"] = "K0i2" };

    private static readonly SentStat Points = new("value", "Points", "clan.battle.points");

    private static Source SourceOf(SourceRole role, string id = "s-00000001") => new(id, Clan.Slug, Inputs, role);

    private static RecipeReading Reading(params RecipeRow[] rows) =>
        new(ReadingOutcome.Read, null, rows, [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], "battle=B", rows.Length)
        {
            Period = new ReadingPeriod("B", null, null),
        };

    private static RecipeWatch Watch(
        IRecipeEngine engine, StubHost host, IScoreBook book, Source source,
        SharedAccounts? shared = null, AccountClaims? claims = null, IReadOnlySet<string>? tracked = null,
        FinalsIndex? finals = null, TimeProvider? time = null, Recipe? recipe = null, string? text = null) =>
        new(engine, host, new NoKeys(), new ReportPolicy([Points], new HashSet<Guid> { Alt }), recipe ?? Clan, Inputs,
            tracked ?? new HashSet<string> { "value" },
            book, source, shared, text ?? ClanText, claims, finals, time);

    [Fact]
    public async Task AReadIsKeptWithOnlyYourAccountsAndStillSent()
    {
        var book = new MemoryBook();
        var host = new StubHost(true, AltAccount);
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200), EngineRow(222, 10)));

        var snapshot = await Watch(engine, host, book, SourceOf(SourceRole.Mine)).RunOnceAsync(CancellationToken.None, BookLine.TriggerManual);

        var line = Assert.Single(book.Lines);
        Assert.Equal((BookLine.KindRead, BookLine.TriggerManual, "s-00000001"), (line.Kind, line.Trigger, line.Source));
        Assert.Equal(new[] { "111" }, line.Accounts.Keys.ToArray());
        Assert.Equal(BookFiles.Hash(ClanText), line.Recipe.Hash);
        Assert.Equal(ClanText, Assert.Single(book.RecipeTexts));
        Assert.True(snapshot.Recorded);
        Assert.Null(snapshot.NotRecordingReason);
        Assert.Equal("s-00000001", snapshot.SourceId);
        Assert.Single(host.Reported);
    }

    private static RecipeRow EngineRow(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    [Fact]
    public async Task WithRoRoRoClosedTheSavedAccountsKeepRecordingAndNothingIsSent()
    {
        using var dir = TempDir.Create("urscore-watch");
        var cache = new AccountsCache(Path.Combine(dir.Path, "accounts.json"));
        cache.Save([AltAccount]);
        var host = new StubHost(false);
        var book = new MemoryBook();
        var shared = new SharedAccounts(host, cache, TimeProvider.System);

        var snapshot = await Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), host, book, SourceOf(SourceRole.Mine), shared)
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.HostDown, snapshot.State);
        Assert.Equal(new[] { "111" }, Assert.Single(book.Lines).Accounts.Keys.ToArray());
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task AStoppedReadKeepsNothingAndSaysWhy()
    {
        var book = new MemoryBook();

        var snapshot = await Watch(new StubEngine(() => RecipeReading.Stop(ReadingOutcome.Idle, "No clan battle running")),
            new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine)).RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.False(snapshot.Recorded);
        Assert.Equal("No clan battle running", snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task AWatchSourceSendsNothingAndKeepsNoAccount()
    {
        var book = new MemoryBook();
        var host = new StubHost(true, AltAccount);

        var snapshot = await Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), host, book, SourceOf(SourceRole.Watch))
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(WatchState.Showing, snapshot.State);
        Assert.Equal(RecipeWatch.WatchOnlyDetail, snapshot.Detail);
        Assert.Empty(Assert.Single(book.Lines).Accounts);
        Assert.Empty(host.Reported);
    }

    [Fact]
    public async Task TwoSourcesThatSeeOneAccountKeepAndSendItOnce()
    {
        var claims = new AccountClaims(TimeProvider.System);
        var host = new StubHost(true, AltAccount);
        var book = new MemoryBook();
        var engine = new StubEngine(() => Reading(EngineRow(111, 4200)));

        await Watch(engine, host, book, SourceOf(SourceRole.Main, "s-00000001"), claims: claims).RunOnceAsync(CancellationToken.None);
        await Watch(engine, host, book, SourceOf(SourceRole.Mine, "s-00000002"), claims: claims).RunOnceAsync(CancellationToken.None);

        Assert.Single(host.Reported);
        Assert.Equal(new[] { "111" }, book.Lines[0].Accounts.Keys.ToArray());
        Assert.Empty(book.Lines[1].Accounts);
    }

    [Fact]
    public async Task AGroupListIsShownAndNeverKept()
    {
        var text = RecipeParserTests.Fixture("petsim99-top-clans.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        var book = new MemoryBook();
        var reading = new RecipeReading(ReadingOutcome.Read, null, [], [], "battle=B", 2)
        {
            Groups = [new GroupRow("Aurelian", new Dictionary<string, double> { ["value"] = 1 }, 1), new GroupRow("SkyHarbor", new Dictionary<string, double> { ["value"] = 2 }, 2)],
        };

        var snapshot = await Watch(new StubEngine(() => reading), new StubHost(true, AltAccount), book,
            new Source("s-00000009", recipe.Slug, new Dictionary<string, string>(), SourceRole.Watch), recipe: recipe, text: text)
            .RunOnceAsync(CancellationToken.None);

        Assert.Empty(book.Lines);
        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(RecipeWatch.NotRecordingGroups, snapshot.NotRecordingReason);
    }

    [Fact]
    public async Task ChangingASourcesRoleNeedsNoNewWatch()
    {
        var book = new MemoryBook();
        var watch = Watch(new StubEngine(() => Reading(EngineRow(111, 4200))), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine));

        watch.UpdateSource(SourceOf(SourceRole.Watch));
        await watch.RunOnceAsync(CancellationToken.None);

        Assert.Equal(SourceRole.Watch, watch.Source!.Role);
        Assert.Empty(Assert.Single(book.Lines).Accounts);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The name 'LineBuilder' does not exist in the current context`.

- [ ] **Step 4: Create `src/Book/Ranking.cs` and `src/Book/LineBuilder.cs`**

`src/Book/Ranking.cs`:

```csharp
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

public static class Ranking
{
    /// <summary>Standard competition ranking (1, 2, 2, 4) over the rows that have the stat, highest first.</summary>
    public static IReadOnlyDictionary<long, int> Competition(IEnumerable<RecipeRow> rows, string statKey)
    {
        var values = rows
            .Where(r => r.Values.TryGetValue(statKey, out var v) && double.IsFinite(v))
            .GroupBy(r => r.UserId)
            .Select(g => (UserId: g.Key, Value: g.First().Values[statKey]))
            .ToList();

        var descending = values.Select(v => v.Value).OrderByDescending(v => v).ToArray();
        return values.ToDictionary(v => v.UserId, v => 1 + CountGreater(descending, v.Value));
    }

    private static int CountGreater(double[] descending, double value)
    {
        var low = 0;
        var high = descending.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (descending[mid] > value) low = mid + 1;
            else high = mid;
        }

        return low;
    }
}
```

`src/Book/LineBuilder.cs`:

```csharp
using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Everything a line needs about the read that isn't in the reading.</summary>
public sealed record ReadContext(Source Source, Recipe Recipe, string RecipeHash, string Trigger, DateTimeOffset At, int OffsetMinutes);

/// <summary>
/// Score book spec §5.2, §5.3 and §5.6. The only place a reading becomes a line, and so the only place the
/// privacy rules for the book live:
/// - accounts come only from rows whose id is in the user's map;
/// - a watch source writes no account;
/// - a group list writes nothing;
/// - headline values are finite numbers that aren't another row's user id.
/// </summary>
public static class LineBuilder
{
    public static BookLine? Reading(ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked)
    {
        var recipe = context.Recipe;
        if (reading.Outcome != ReadingOutcome.Read || recipe.IsGroupList) return null;

        var stats = tracked.Order(StringComparer.Ordinal).ToList();
        var headline = Headline(reading.Headline, OtherIds(reading.Rows, map));
        var accounts = new Dictionary<string, BookAccount>(StringComparer.Ordinal);
        var unavail = new List<string>();

        if (context.Source.Role != SourceRole.Watch)
        {
            var list = !recipe.LastStep.PerAccount;
            foreach (var (id, account) in Accounts(reading.Rows, map, tracked, stats, list, onlyUsers: null))
            {
                accounts[id] = reading.AccountAsOf.TryGetValue(long.Parse(id, CultureInfo.InvariantCulture), out var stamp)
                    ? account with { AsOf = stamp.Time, Stale = stamp.Stale }
                    : account;
            }

            unavail.AddRange(reading.Unavailable.Keys.Where(map.ContainsKey).Order().Select(Id));
        }

        if (headline.Count == 0 && accounts.Count == 0 && unavail.Count == 0) return null;

        return new BookLine(
            BookLine.Version, BookLine.KindRead, context.At, context.OffsetMinutes, context.Trigger,
            new BookRecipeRef(recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
            Inputs(context.Source), Period(reading.Period), headline, stats, accounts,
            unavail.Count == 0 ? null : unavail, reading.ListAsOf?.Time, reading.ListAsOf?.Stale);
    }

    public static BookLine Final(
        ReadContext context, PastPeriodReading past, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        IReadOnlyCollection<long>? onlyUsers, string trigger)
    {
        var stats = tracked.Order(StringComparer.Ordinal).ToList();
        var accounts = context.Source.Role == SourceRole.Watch
            ? new Dictionary<string, BookAccount>(StringComparer.Ordinal)
            : Accounts(past.Rows, map, tracked, stats, list: true, onlyUsers).ToDictionary(a => a.Id, a => a.Account, StringComparer.Ordinal);

        return new BookLine(
            BookLine.Version, BookLine.KindFinal, context.At, context.OffsetMinutes, trigger,
            new BookRecipeRef(context.Recipe.Slug, context.RecipeHash), context.Source.Id, RoleText(context.Source.Role),
            Inputs(context.Source), new BookPeriod(past.Value), Headline(past.Headline, OtherIds(past.Rows, map)), stats, accounts);
    }

    /// <summary>The user's rows with at least one tracked finite value, with competition ranks among every row on list recipes.</summary>
    private static IEnumerable<(string Id, BookAccount Account)> Accounts(
        IReadOnlyList<RecipeRow> rows, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        IReadOnlyList<string> stats, bool list, IReadOnlyCollection<long>? onlyUsers)
    {
        var ranks = list ? stats.ToDictionary(s => s, s => Ranking.Competition(rows, s), StringComparer.Ordinal) : null;

        foreach (var row in rows.Where(r => map.ContainsKey(r.UserId) && (onlyUsers is null || onlyUsers.Contains(r.UserId))))
        {
            var values = row.Values
                .Where(kv => tracked.Contains(kv.Key) && double.IsFinite(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (values.Count == 0) continue;

            Dictionary<string, int>? rank = null;
            if (ranks is not null)
            {
                rank = ranks.Where(r => r.Value.ContainsKey(row.UserId)).ToDictionary(r => r.Key, r => r.Value[row.UserId], StringComparer.Ordinal);
                if (rank.Count == 0) rank = null;
            }

            yield return (Id(row.UserId), new BookAccount(values, rank, list ? rows.Count : null));
        }
    }

    private static HashSet<long> OtherIds(IReadOnlyList<RecipeRow> rows, IReadOnlyDictionary<long, Guid> map) =>
        rows.Select(r => r.UserId).Where(id => !map.ContainsKey(id)).ToHashSet();

    /// <summary>Numbers only, and never a number that is another row's user id (score book spec §5.6).</summary>
    private static Dictionary<string, double> Headline(IReadOnlyList<HeadlineValue> headline, HashSet<long> otherIds) =>
        headline
            .Where(h => h.Id.Length > 0 && h.Number is { } n && double.IsFinite(n)
                        && !(n == Math.Floor(n) && n is > 0 and < long.MaxValue && otherIds.Contains((long)n)))
            .GroupBy(h => h.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Number!.Value, StringComparer.Ordinal);

    private static Dictionary<string, string> Inputs(Source source) => new(source.Inputs, StringComparer.Ordinal);

    private static BookPeriod? Period(ReadingPeriod? period) => period is null ? null : new BookPeriod(period.Value, period.Starts, period.Ends);

    private static string RoleText(SourceRole role) => role.ToString().ToLowerInvariant();

    private static string Id(long userId) => userId.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Create the `src/Book/Finals.cs` shells**

These compile and do nothing until Task 7:

```csharp
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Filled in by Task 7.</summary>
public sealed class FinalsIndex
{
    public void Add(BookLine line)
    {
    }

    public bool HasClan(string slug, string inputsKey, string period) => false;

    public bool HasAccount(string slug, string inputsKey, string period, long userId) => false;

    public static FinalsIndex Load(string root) => new();
}

/// <summary>Filled in by Task 7.</summary>
public static class FinalsPlanner
{
    public static IReadOnlyList<BookLine> Plan(
        ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        FinalsIndex index, string? previousPeriod) => [];

    public static bool CurrentPeriodEnded(ReadContext context, RecipeReading reading, FinalsIndex index) => false;
}
```

- [ ] **Step 6: Replace `src/Core/RecipeWatch.cs` with this file**

```csharp
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Labs626.UrScore.Book;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Everything the window renders from one cycle. <see cref="Rows"/> carries every row the recipe
/// read, the user's own and everyone else's, for the leaderboard; only the user's own are ever
/// reported, through <see cref="ReportPolicy"/>, or kept, through <see cref="LineBuilder"/>.
/// <see cref="CellMisses"/> holds only the user's own accounts, so another member's id never travels
/// further than the leaderboard.
/// </summary>
public sealed record RecipeSnapshot(
    WatchState State,
    string? Detail,
    IReadOnlyList<AccountLine> Accounts,
    IReadOnlyList<HostAccount> Unresolved,
    int RowsSeen,
    string? Context = null,
    IReadOnlyList<RecipeRow>? Rows = null,
    IReadOnlyList<HeadlineValue>? Headline = null)
{
    public IReadOnlyDictionary<long, string> Unavailable { get; init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<string, string> StatMisses { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<(long UserId, string Stat), string> CellMisses { get; init; } = new Dictionary<(long UserId, string Stat), string>();

    public IReadOnlyList<string> CounterNames { get; init; } = [];

    public string? IconText { get; init; }

    /// <summary>The slug of the recipe this cycle actually read, which the window checks against the one it now runs.</summary>
    public string RecipeSlug { get; init; } = "";

    /// <summary>The source this cycle read for, or empty for a watch without one.</summary>
    public string SourceId { get; init; } = "";

    /// <summary>Whether this cycle wrote a reading line to the score book.</summary>
    public bool Recorded { get; init; }

    /// <summary>Why this cycle kept nothing, when a book is attached and nothing was kept.</summary>
    public string? NotRecordingReason { get; init; }

    public ReadingPeriod? Period { get; init; }

    /// <summary>A group list's groups, shown live and never kept.</summary>
    public IReadOnlyList<GroupRow> Groups { get; init; } = [];
}

/// <summary>
/// One cycle for one source: ask for the user's accounts, read the recipe, keep the user's rows in the score
/// book, and hand each sent stat to RoRoRo through the report policy. Carries every guarantee
/// <c>ScoreWatch</c> earned: one cycle at a time, raw values in UTC, no backlog when RoRoRo returns, and a
/// named capability when consent is declined.
/// </summary>
public sealed class RecipeWatch(
    IRecipeEngine engine,
    IHostClient host,
    IKeyStore keys,
    ReportPolicy policy,
    Recipe recipe,
    IReadOnlyDictionary<string, string> inputs,
    IReadOnlySet<string> trackedStats,
    IScoreBook? book = null,
    Source? source = null,
    SharedAccounts? sharedAccounts = null,
    string recipeText = "",
    AccountClaims? claims = null,
    FinalsIndex? finals = null,
    TimeProvider? time = null)
{
    internal const string RecipeChangedDetail = "The recipe changed while it was being read, so nothing was sent this time.";

    internal const string RecipeChangedMidSendDetail = "The recipe changed while this reading was being sent, so the rest of it was not sent.";

    internal const string WatchOnlyDetail = "Watching only: nothing is sent, and no account is kept.";

    internal const string NotRecordingNoAccounts = "None of your accounts were in this read.";

    internal const string NotRecordingEnded = "It has ended, and its final result is saved.";

    internal const string NotRecordingGroups = "Group lists are shown live and never kept.";

    internal const string NotRecordingNothingRead = "Nothing was read this time.";

    private readonly Dictionary<Guid, AccountLine> _lines = [];

    /// <summary>
    /// <see cref="UpdateRecipe"/> and <see cref="UpdateSource"/> run on the UI thread while a cycle runs on
    /// the pool. Every access from both sides to these goes through this one lock: the recipe, its text,
    /// inputs, tracked stats, source, <see cref="_lines"/>, <see cref="_context"/>,
    /// <see cref="_previousPeriod"/> and <see cref="_held"/>. So does every read of the policy that must
    /// match a recipe check. Never held across an await.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>A timer tick and a Test now click must never both report the same observation.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private string? _context;

    /// <summary>The period the last successful read belonged to, so a period that just ended is an "ended" final.</summary>
    private string? _previousPeriod;

    /// <summary>
    /// A stop that retrying cannot fix, and what would release it (plan Ruling 6). A null
    /// <c>KeyFingerprint</c> means only <see cref="UpdateRecipe"/> releases the hold — that is
    /// <see cref="ReadingOutcome.SignInRequired"/>, which no key change can fix. A non-null one is
    /// <see cref="ReadingOutcome.KeyRejected"/>, released when the saved keys change (Ruling E).
    /// </summary>
    private (RecipeSnapshot Snapshot, string? KeyFingerprint)? _held;

    public ReportPolicy Policy => policy;

    public Recipe Recipe => recipe;

    public Source? Source
    {
        get
        {
            lock (_gate) return source;
        }
    }

    public void UpdatePolicy(IReadOnlyList<SentStat> sentStats, IReadOnlySet<Guid> allowedSubjects)
    {
        lock (_gate) policy = policy.With(sentStats, allowedSubjects);
    }

    /// <summary>A role change (make main, watch a clan) applies to the next cycle, with no new watch.</summary>
    public void UpdateSource(Source newSource)
    {
        lock (_gate) source = newSource;
    }

    /// <summary>
    /// A different recipe or different inputs mean every remembered value belongs to something else,
    /// so they are cleared. The same recipe and inputs, reloaded, keep them. A change to which stats
    /// are tracked only changes what the next read asks for.
    /// </summary>
    public void UpdateRecipe(Recipe newRecipe, IReadOnlyDictionary<string, string> newInputs, IReadOnlySet<string> newTrackedStats, string? newRecipeText = null)
    {
        lock (_gate)
        {
            var same = string.Equals(newRecipe.Slug, recipe.Slug, StringComparison.Ordinal)
                       && newInputs.Count == inputs.Count
                       && newInputs.All(kv => inputs.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

            recipe = newRecipe;
            inputs = new Dictionary<string, string>(newInputs, StringComparer.Ordinal);
            trackedStats = new HashSet<string>(newTrackedStats, StringComparer.Ordinal);
            recipeText = newRecipeText ?? recipeText;
            _held = null;

            if (!same)
            {
                _lines.Clear();
                _context = null;
                _previousPeriod = null;
            }
        }
    }

    public async Task<RecipeSnapshot> RunOnceAsync(CancellationToken cancellationToken, string trigger = BookLine.TriggerTimer)
    {
        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunOnceCoreAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<RecipeSnapshot> RunOnceCoreAsync(string trigger, CancellationToken cancellationToken)
    {
        // This cycle reads with what is current now, and only that. UpdateRecipe can swap these while the
        // read is in flight; RecipeChanged catches it before anything is kept or sent.
        Recipe readRecipe;
        IReadOnlyDictionary<string, string> readInputs;
        IReadOnlySet<string> readTracked;
        string readText;
        Source? readSource;
        (RecipeSnapshot Snapshot, string? KeyFingerprint)? heldNow;
        lock (_gate)
        {
            readRecipe = recipe;
            readInputs = inputs;
            readTracked = trackedStats;
            readText = recipeText;
            readSource = source;
            heldNow = _held;
        }

        if (heldNow is { } held && (held.KeyFingerprint is null || held.KeyFingerprint == KeyFingerprint()))
        {
            return held.Snapshot;
        }

        lock (_gate) _held = null;

        // Accounts first: a per-account recipe builds its requests from these ids. Read every cycle (or from
        // the shared list), so an account added mid-session is watched without restarting anything.
        IReadOnlyList<HostAccount> accounts = [];
        bool hostUp;
        if (sharedAccounts is not null)
        {
            var list = await sharedAccounts.GetAsync(cancellationToken).ConfigureAwait(false);
            if (list.Denied) return Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);

            hostUp = list.HostUp;
            accounts = list.Accounts;
        }
        else
        {
            hostUp = await host.IsReachableAsync(cancellationToken).ConfigureAwait(false);
            if (hostUp)
            {
                try
                {
                    accounts = await host.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
                {
                    return Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.queries.accounts"), 0, []);
                }
            }
        }

        var watchOnly = readSource?.Role == SourceRole.Watch;
        var unresolved = AccountMap.Unresolved(accounts);
        var map = watchOnly ? new Dictionary<long, Guid>() : AccountMap.Build(accounts);

        var reading = await engine.ReadAsync(readRecipe, readInputs, [.. map.Keys], readTracked, cancellationToken).ConfigureAwait(false);

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
            var fingerprint = state == WatchState.KeyRejected ? KeyFingerprint() : null;

            lock (_gate)
            {
                // A stop read for a recipe that is no longer running must not hold the new one.
                if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, readSource, unresolved);

                // Idle means nothing is live, so no context is current. The remembered values are the
                // finished thing's final numbers and stay readable until something new replaces them.
                if (reading.Outcome == ReadingOutcome.Idle) _context = null;

                // An idle clan still has an icon: the engine reads it before deciding the clan sat out.
                var snapshot = Snapshot(readRecipe, readSource, state, reading.Detail, 0, unresolved) with
                {
                    IconText = reading.IconText,
                    NotRecordingReason = book is null ? null : reading.Detail ?? NotRecordingNothingRead,
                };

                if (state == WatchState.KeyRejected)
                {
                    _held = (snapshot, fingerprint);
                }
                else if (state == WatchState.SignInRequired)
                {
                    _held = (snapshot, null);
                }

                return snapshot;
            }
        }

        lock (_gate)
        {
            // Nor may its context clear, or stand in for, the new recipe's remembered values.
            if (RecipeChanged(readRecipe, readInputs)) return ChangedSnapshot(readRecipe, readSource, unresolved);

            if (_context is not null && reading.Context != _context) _lines.Clear();
            _context = reading.Context;
        }

        var seen = reading.RowsSeen;

        if (readRecipe.IsGroupList)
        {
            return Snapshot(readRecipe, readSource, WatchState.Showing, $"Read {reading.Groups.Count} groups.", seen, unresolved, reading, map) with
            {
                NotRecordingReason = book is null ? null : NotRecordingGroups,
            };
        }

        // Ruling R6: an account two sources of this recipe both saw belongs to the one that claimed it first.
        var owned = OwnedMap(readRecipe, readSource, reading, map);

        var (recorded, notRecording) = Record(readRecipe, readText, readTracked, readSource, trigger, reading, owned);
        RecipeSnapshot Kept(RecipeSnapshot snapshot) => snapshot with { Recorded = recorded, NotRecordingReason = notRecording };

        var mine = reading.Rows
            .Where(r => owned.ContainsKey(r.UserId))
            .Select(r => (Subject: owned[r.UserId], r.Values))
            .ToList();

        if (watchOnly)
        {
            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, WatchOnlyDetail, seen, unresolved, reading, map));
        }

        if (!hostUp)
        {
            // Nothing is fetched from the host and nothing is queued, so there is nothing to replay
            // when it comes back. The book above kept the reading anyway.
            return Kept(Snapshot(readRecipe, readSource, WatchState.HostDown, "RoRoRo is not running. Still watching; nothing is being sent.", seen, unresolved, reading, map));
        }

        if (mine.Count == 0)
        {
            var none = $"Read {seen} row(s); none of them are your accounts.";
            if (reading.Detail is not null) none += " " + reading.Detail;
            return Kept(Snapshot(readRecipe, readSource, WatchState.NoMatches, none, seen, unresolved, reading, map));
        }

        if (policy.SentStats.Count == 0)
        {
            var showing = $"Read {mine.Count} of {seen} row(s). No stat is set to send, so nothing went to RoRoRo.";
            if (reading.Detail is not null) showing += " " + reading.Detail;
            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, showing, seen, unresolved, reading, map));
        }

        // One fixed list for the whole loop. A stat unticked mid-loop is refused by the policy each send
        // captures, which checks its own metric ids.
        IReadOnlyList<SentStat> stats;
        lock (_gate) stats = policy.SentStats;

        var observedAt = DateTimeOffset.UtcNow;

        foreach (var (subject, values) in mine)
        {
            try
            {
                // Raw and unmodified, through the only route out: one observation per sent stat this
                // account has a number for. The policy also checks the account's own Send.
                foreach (var stat in stats)
                {
                    if (!values.TryGetValue(stat.Key, out var value)) continue;

                    // The recipe can be replaced during any send's await. A reading of a recipe that is
                    // no longer running would go out under the new recipe's metric ids wherever the stat
                    // keys coincide, so each send re-checks and takes the policy in the same section.
                    // The first send's check also covers a change during the read.
                    ReportPolicy current;
                    lock (_gate)
                    {
                        if (RecipeChanged(readRecipe, readInputs))
                            return Kept(Snapshot(readRecipe, readSource, WatchState.Showing, RecipeChangedMidSendDetail, seen, unresolved));
                        current = policy;
                    }

                    var sent = await current.SendAsync(host, subject, stat.MetricId, value, observedAt, cancellationToken)
                        .ConfigureAwait(false);

                    if (sent) Remember(readRecipe, readInputs, subject, accounts, stat.Key, value, observedAt);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                return Kept(Snapshot(readRecipe, readSource, WatchState.Rejected, RejectedMessage("host.metrics.report"), seen, unresolved, reading, map));
            }
        }

        var detail = $"Reporting {mine.Count} of {seen} row(s).";
        if (reading.Detail is not null) detail += " " + reading.Detail;
        return Kept(Snapshot(readRecipe, readSource, WatchState.Reporting, detail, seen, unresolved, reading, map));
    }

    /// <summary>Ruling R6. Accounts not in this read's rows (a private profile) need no claim.</summary>
    private IReadOnlyDictionary<long, Guid> OwnedMap(Recipe readRecipe, Source? readSource, RecipeReading reading, IReadOnlyDictionary<long, Guid> map)
    {
        if (claims is null || readSource is null || map.Count == 0) return map;

        var window = TimeSpan.FromSeconds(readRecipe.EffectiveEverySeconds * 2);
        var inRows = reading.Rows.Select(r => r.UserId).ToHashSet();
        return map
            .Where(kv => !inRows.Contains(kv.Key) || claims.TryClaim(readRecipe.Slug, kv.Key, readSource.Id, window))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Score book spec §5.4 and §6: finals first, from the same response, then this read's line, unless its
    /// period has already ended. Returns whether a reading line was kept, and why not.
    /// </summary>
    private (bool Recorded, string? Reason) Record(
        Recipe readRecipe, string readText, IReadOnlySet<string> readTracked, Source? readSource, string trigger,
        RecipeReading reading, IReadOnlyDictionary<long, Guid> owned)
    {
        if (book is null || readSource is null) return (false, null);

        var at = (time ?? TimeProvider.System).GetUtcNow();
        var offset = (int)TimeZoneInfo.Local.GetUtcOffset(at).TotalMinutes;
        var context = new ReadContext(readSource, readRecipe, BookFiles.Hash(readText), trigger, at, offset);

        if (finals is not null)
        {
            string? previous;
            lock (_gate) previous = _previousPeriod;

            foreach (var final in FinalsPlanner.Plan(context, reading, owned, readTracked, finals, previous))
            {
                finals.Add(final);
                book.Append(final, readText);
            }
        }

        lock (_gate)
        {
            if (reading.Period is { } period) _previousPeriod = period.Value;
        }

        if (finals is not null && FinalsPlanner.CurrentPeriodEnded(context, reading, finals)) return (false, NotRecordingEnded);

        var line = LineBuilder.Reading(context, reading, owned, readTracked);
        if (line is null) return (false, NotRecordingNoAccounts);

        book.Append(line, readText);
        return (true, null);
    }

    /// <summary>Whether the recipe or inputs a cycle read with have been replaced since. Call under <see cref="_gate"/>.</summary>
    private bool RecipeChanged(Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs) =>
        !ReferenceEquals(recipe, readRecipe) || !ReferenceEquals(inputs, readInputs);

    /// <summary>Nothing sent, nothing kept, and no rows, so the window has nothing of the old recipe's to draw.</summary>
    private RecipeSnapshot ChangedSnapshot(Recipe readRecipe, Source? readSource, IReadOnlyList<HostAccount> unresolved) =>
        Snapshot(readRecipe, readSource, WatchState.Showing, RecipeChangedDetail, 0, unresolved);

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

    private void Remember(
        Recipe readRecipe, IReadOnlyDictionary<string, string> readInputs,
        Guid subject, IReadOnlyList<HostAccount> accounts, string statKey, double value, DateTimeOffset at)
    {
        var name = accounts.FirstOrDefault(a => a.AccountId == subject)?.DisplayName ?? subject.ToString();

        lock (_gate)
        {
            // A reading sent just before its recipe was replaced must not come back into the lines
            // UpdateRecipe cleared for the new one.
            if (RecipeChanged(readRecipe, readInputs)) return;

            var values = _lines.TryGetValue(subject, out var line)
                ? new Dictionary<string, double>(line.LastValues, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);

            values[statKey] = value;
            _lines[subject] = new AccountLine(name, subject, values, at);
        }
    }

    private RecipeSnapshot Snapshot(
        Recipe readRecipe, Source? readSource, WatchState state, string? detail, int seen, IReadOnlyList<HostAccount> unresolved,
        RecipeReading? reading = null, IReadOnlyDictionary<long, Guid>? map = null)
    {
        IReadOnlyList<AccountLine> lines;
        string? context;
        lock (_gate)
        {
            lines = [.. _lines.Values];
            context = _context;
        }

        return new(state, detail, lines, unresolved, seen, reading?.Context ?? context, reading?.Rows, reading?.Headline)
        {
            Unavailable = reading?.Unavailable ?? new Dictionary<long, string>(),
            StatMisses = reading?.StatMisses ?? new Dictionary<string, string>(),
            CellMisses = reading is null || map is null
                ? new Dictionary<(long UserId, string Stat), string>()
                : reading.CellMisses.Where(cell => map.ContainsKey(cell.Key.UserId)).ToDictionary(cell => cell.Key, cell => cell.Value),
            CounterNames = reading?.CounterNames ?? [],
            IconText = reading?.IconText,
            RecipeSlug = readRecipe.Slug,
            SourceId = readSource?.Id ?? "",
            Period = reading?.Period,
            Groups = reading?.Groups ?? [],
        };
    }
}
```

**Two things to check:**
- **`string.Join('\u0001', ...)` in `KeyFingerprint` must be written with the escape sequence,** exactly as shown, never as a literal control character.
- **`RecipeWatchTests` builds watches with the original seven arguments and calls `RunOnceAsync(ct)`.** Both still compile, because every new parameter is optional.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS, including every existing test in `RecipeWatchTests`.

- [ ] **Step 8: Commit**

```bash
git add src/Book/Ranking.cs src/Book/LineBuilder.cs src/Book/Finals.cs src/Core/RecipeWatch.cs tests/LineBuilderTests.cs tests/RecipeWatchBookTests.cs
git commit -m "book: every successful read is kept, with only your accounts, even while RoRoRo is closed

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Finals and backfill

Spec §6. Every settled period under `period.past` is kept once as a final line, from the same response. An alt added later gets a line of its own. When the current period ends, reading lines stop.

**Files:**
- Replace: `src/Book/Finals.cs` (the Task 6 shell)
- Create: `tests/FinalsTests.cs`
- Modify: `tests/RecipeWatchBookTests.cs`

**Interfaces:**
- Consumes: `LineBuilder.Final`, `ReadContext`, `RecipeReading.Past`/`Period`, `Source.KeyOf`/`InputsKey`, `BookFiles.ReadAll`/`Slugs`.
- Produces: `FinalsIndex` (`Add`, `HasClan`, `HasAccount`, `Load`) and `FinalsPlanner` (`Plan`, `CurrentPeriodEnded`), exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/FinalsTests.cs`:

```csharp
using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class FinalsTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset At = new(2026, 9, 19, 18, 3, 0, TimeSpan.Zero);

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static ReadContext Context(SourceRole role = SourceRole.Mine) => new(
        new Source("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, role),
        Clan, "3f9a1c0b7e2d4a55", BookLine.TriggerTimer, At, -300);

    private static RecipeRow Row(long id, double points) => new(id, new Dictionary<string, double> { ["value"] = points });

    private static PastPeriodReading Past(string value, params RecipeRow[] rows) => new(value, rows,
        [new HeadlineValue("Clan place", "40") { Id = "clan-place", Number = 40 }], RowsReadable: rows.Length > 0);

    private static RecipeReading Reading(string current, DateTimeOffset? ends, params PastPeriodReading[] past) =>
        new(ReadingOutcome.Read, null, [], [], $"battle={current}", 0) { Period = new ReadingPeriod(current, null, ends), Past = past };

    private static readonly HashSet<string> Points = ["value"];

    private static readonly Dictionary<long, Guid> Map = new() { [111] = A };

    [Fact]
    public void EveryFinishedBattleIsBackfilledOnce()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", At.AddDays(3), Past("A", Row(111, 300), Row(222, 200)), Past("B", Row(111, 90)), Past("C", Row(111, 5)));

        var lines = FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: null);

        Assert.Equal(new[] { "A", "B" }, lines.Select(l => l.Period!.Value).ToArray());
        Assert.All(lines, l => Assert.Equal((BookLine.KindFinal, BookLine.TriggerBackfill), (l.Kind, l.Trigger)));
        Assert.Equal(new[] { "111" }, lines[0].Accounts.Keys.ToArray());

        foreach (var line in lines) index.Add(line);
        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, previousPeriod: null));
    }

    [Fact]
    public void AnAltAddedLaterGetsAFinalOfItsOwn()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("A", Row(111, 300), Row(333, 250)));
        foreach (var line in FinalsPlanner.Plan(Context(), reading, Map, Points, index, null)) index.Add(line);

        var withAlt = new Dictionary<long, Guid>(Map) { [333] = Guid.NewGuid() };
        var later = Assert.Single(FinalsPlanner.Plan(Context(), reading, withAlt, Points, index, null));

        Assert.Equal(new[] { "333" }, later.Accounts.Keys.ToArray());
        Assert.Equal(40, later.Headline["clan-place"]);
    }

    [Fact]
    public void TheCurrentBattleEndsWhenItsEndTimePasses()
    {
        var index = new FinalsIndex();
        var reading = Reading("B", At.AddMinutes(-1), Past("B", Row(111, 4200)));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, index, "B"));

        Assert.Equal((BookLine.TriggerEnded, "B"), (line.Trigger, line.Period!.Value));
        Assert.True(FinalsPlanner.CurrentPeriodEnded(Context(), reading, index));
        Assert.False(FinalsPlanner.CurrentPeriodEnded(Context(), Reading("B", At.AddMinutes(5)), index));
    }

    [Fact]
    public void ABattleReplacedByTheNextOneEnds()
    {
        var reading = Reading("C", null, Past("B", Row(111, 4200)), Past("C", Row(111, 1)));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, new FinalsIndex(), previousPeriod: "B"));

        Assert.Equal((BookLine.TriggerEnded, "B"), (line.Trigger, line.Period!.Value));
    }

    [Fact]
    public void ABattleWithNoContributionsStillKeepsTheClansResultOnce()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("Empty"));

        var line = Assert.Single(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
        Assert.Empty(line.Accounts);
        Assert.Equal(40, line.Headline["clan-place"]);

        index.Add(line);
        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
    }

    [Fact]
    public void AnAccountWithNoTrackedValueNeverAsksForAnotherLine()
    {
        var index = new FinalsIndex();
        var reading = Reading("C", null, Past("A", new RecipeRow(111, new Dictionary<string, double>())));
        foreach (var line in FinalsPlanner.Plan(Context(), reading, Map, Points, index, null)) index.Add(line);

        Assert.Empty(FinalsPlanner.Plan(Context(), reading, Map, Points, index, null));
    }

    [Fact]
    public void AWatchedClansFinalsKeepNoAccounts()
    {
        var line = Assert.Single(FinalsPlanner.Plan(Context(SourceRole.Watch), Reading("C", null, Past("A", Row(111, 300))), Map, Points, new FinalsIndex(), null));

        Assert.Empty(line.Accounts);
    }

    [Fact]
    public void TheIndexLoadsFromTheBookAndIgnoresInputCase()
    {
        using var dir = TempDir.Create("urscore-finals");
        using (var book = new ScoreBook(dir.Path, background: false))
        {
            foreach (var line in FinalsPlanner.Plan(Context(), Reading("C", null, Past("A", Row(111, 300))), Map, Points, new FinalsIndex(), null))
            {
                book.Append(line, "recipe text");
            }
        }

        var index = FinalsIndex.Load(dir.Path);
        var key = Source.KeyOf(new Dictionary<string, string> { ["clan"] = " k0i2" });

        Assert.True(index.HasClan(Clan.Slug, key, "A"));
        Assert.True(index.HasAccount(Clan.Slug, key, "A", 111));
        Assert.False(index.HasAccount(Clan.Slug, key, "A", 222));
    }
}
```

Append to `RecipeWatchBookTests`:

```csharp
    [Fact]
    public async Task FinalsComeFirstAndReadingsStopOnceTheBattleHasEnded()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
        var book = new MemoryBook();
        var reading = new RecipeReading(ReadingOutcome.Read, null, [EngineRow(111, 4200)],
            [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], "battle=B", 1)
        {
            Period = new ReadingPeriod("B", null, time.Now.AddMinutes(-10)),
            Past =
            [
                new PastPeriodReading("A", [EngineRow(111, 300)], [new HeadlineValue("Clan place", "40") { Id = "clan-place", Number = 40 }], true),
                new PastPeriodReading("B", [EngineRow(111, 4200)], [new HeadlineValue("Clan place", "14") { Id = "clan-place", Number = 14 }], true),
            ],
        };

        var snapshot = await Watch(new StubEngine(() => reading), new StubHost(true, AltAccount), book, SourceOf(SourceRole.Mine),
            finals: new FinalsIndex(), time: time).RunOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { ("A", BookLine.TriggerBackfill), ("B", BookLine.TriggerEnded) }, book.Lines.Select(l => (l.Period!.Value, l.Trigger)).ToArray());
        Assert.All(book.Lines, l => Assert.Equal(BookLine.KindFinal, l.Kind));
        Assert.False(snapshot.Recorded);
        Assert.Equal(RecipeWatch.NotRecordingEnded, snapshot.NotRecordingReason);
    }
```

**The expected `ended` trigger on B comes from `ends`.** The previous period is null on a first read, so the trigger has to be `ended` because B is the current period and its `ends` has passed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`, then `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Finals"`
Expected: build PASS, tests FAIL, because the shells from Task 6 return nothing.

- [ ] **Step 3: Replace `src/Book/Finals.cs`**

```csharp
using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Which finals the book already holds, by recipe, inputs, period and account. Built from the book, updated as lines are planned.</summary>
public sealed class FinalsIndex
{
    private readonly object _gate = new();
    private readonly HashSet<(string Slug, string Inputs, string Period)> _clans = [];
    private readonly HashSet<(string Slug, string Inputs, string Period, long UserId)> _accounts = [];

    public void Add(BookLine line)
    {
        if (line.Kind != BookLine.KindFinal || line.Period is null) return;

        var inputs = Source.KeyOf(line.Inputs);
        lock (_gate)
        {
            _clans.Add((line.Recipe.Slug, inputs, line.Period.Value));
            foreach (var id in line.Accounts.Keys)
            {
                if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
                {
                    _accounts.Add((line.Recipe.Slug, inputs, line.Period.Value, userId));
                }
            }
        }
    }

    public bool HasClan(string slug, string inputsKey, string period)
    {
        lock (_gate) return _clans.Contains((slug, inputsKey, period));
    }

    public bool HasAccount(string slug, string inputsKey, string period, long userId)
    {
        lock (_gate) return _accounts.Contains((slug, inputsKey, period, userId));
    }

    public static FinalsIndex Load(string root)
    {
        var index = new FinalsIndex();
        foreach (var slug in BookFiles.Slugs(root))
        {
            foreach (var line in BookFiles.ReadAll(root, slug)) index.Add(line);
        }

        return index;
    }
}

/// <summary>Score book spec §6.1.</summary>
public static class FinalsPlanner
{
    public static IReadOnlyList<BookLine> Plan(
        ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        FinalsIndex index, string? previousPeriod)
    {
        if (reading.Outcome != ReadingOutcome.Read || reading.Past.Count == 0 || context.Recipe.Period?.Past is null) return [];

        var slug = context.Recipe.Slug;
        var inputs = context.Source.InputsKey;
        var watch = context.Source.Role == SourceRole.Watch;
        var current = reading.Period?.Value;
        var currentEnded = reading.Period?.Ends is { } ends && ends <= context.At;

        var lines = new List<BookLine>();
        foreach (var past in reading.Past)
        {
            var isCurrent = past.Value == current;
            if (isCurrent && !currentEnded) continue;

            var trigger = isCurrent || past.Value == previousPeriod ? BookLine.TriggerEnded : BookLine.TriggerBackfill;

            if (!index.HasClan(slug, inputs, past.Value))
            {
                lines.Add(LineBuilder.Final(context, past, map, tracked, onlyUsers: null, trigger));
                continue;
            }

            if (watch) continue;

            // Only accounts that would actually be written, so an account with no tracked value never asks again.
            var missing = past.Rows
                .Where(r => map.ContainsKey(r.UserId)
                            && r.Values.Any(kv => tracked.Contains(kv.Key) && double.IsFinite(kv.Value))
                            && !index.HasAccount(slug, inputs, past.Value, r.UserId))
                .Select(r => r.UserId)
                .Distinct()
                .ToList();

            if (missing.Count > 0) lines.Add(LineBuilder.Final(context, past, map, tracked, missing, trigger));
        }

        return lines;
    }

    /// <summary>The current period's end has passed, or its final is already kept: no more reading lines for it.</summary>
    public static bool CurrentPeriodEnded(ReadContext context, RecipeReading reading, FinalsIndex index) =>
        reading.Period is { } period
        && ((period.Ends is { } ends && ends <= context.At)
            || index.HasClan(context.Recipe.Slug, context.Source.InputsKey, period.Value));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Book/Finals.cs tests/FinalsTests.cs tests/RecipeWatchBookTests.cs
git commit -m "book: past battles filled in once from the clan's own record, and a battle's final kept when it ends

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: SourceHost

Spec §4.2. It runs one watch per enabled source, each on its recipe's interval. A source keeps its watch across `Apply` calls, so one observation is never read twice by two watches.

**Files:**
- Create: `src/Core/SourceHost.cs`
- Create: `tests/SourceHostTests.cs`

**Interfaces:**
- Consumes: `RecipeWatch` (`RunOnceAsync(ct, trigger)`, `UpdateSource`, `Source`), `Source`, `BookLine` trigger constants, `Recipe.MinimumEverySeconds`.
- Produces: `SourceHost`, exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/SourceHostTests.cs`:

```csharp
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SourceHostTests
{
    private static readonly HostAccount Alt = new(Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2"), 111, "Alt One");

    private static readonly string ClanText = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");

    private static Recipe Clan => RecipeParser.Parse(ClanText).Recipe!;

    private static Source SourceNamed(string id, string clan, SourceRole role = SourceRole.Mine) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, role);

    private static RecipeReading Reading() =>
        new(ReadingOutcome.Read, null, [new RecipeRow(111, new Dictionary<string, double> { ["value"] = 5 })],
            [new HeadlineValue("Clan place", "3") { Id = "clan-place", Number = 3 }], "battle=B", 1);

    private sealed class Factory(StubEngine engine, MemoryBook book)
    {
        public int Created { get; private set; }

        public RecipeWatch Create(Source source)
        {
            Created++;
            return new RecipeWatch(engine, new StubHost(true, Alt), new NoKeys(), new ReportPolicy([], new HashSet<Guid>()),
                Clan, source.Inputs, new HashSet<string> { "value" }, book, source, recipeText: ClanText);
        }
    }

    [Fact]
    public void ASourceKeepsOneWatchAcrossAppliesAndTakesItsNewRole()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        using var host = new SourceHost(factory.Create, _ => 180);

        host.Apply([SourceNamed("s-1", "CCGP")]);
        var first = host.WatchFor("s-1");
        host.Apply([SourceNamed("s-1", "CCGP", SourceRole.Main)]);

        Assert.Equal(1, factory.Created);
        Assert.Same(first, host.WatchFor("s-1"));
        Assert.Equal(SourceRole.Main, host.WatchFor("s-1")!.Source!.Role);
    }

    [Fact]
    public async Task RemovedAndSwitchedOffSourcesLoseTheirWatchAndSnapshot()
    {
        var factory = new Factory(new StubEngine(Reading), new MemoryBook());
        using var host = new SourceHost(factory.Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);
        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        host.Apply([SourceNamed("s-1", "CCGP") with { Enabled = false }]);

        Assert.Null(host.WatchFor("s-1"));
        Assert.Null(host.WatchFor("s-2"));
        Assert.Empty(host.Latest);
    }

    [Fact]
    public async Task TestNowReadsEverySourceAndRaisesEachSnapshot()
    {
        var engine = new StubEngine(Reading);
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(engine, book).Create, _ => 180);
        var seen = new List<string>();
        host.SnapshotReady += (id, _) => { lock (seen) seen.Add(id); };
        host.Apply([SourceNamed("s-1", "CCGP"), SourceNamed("s-2", "K0i2")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        Assert.Equal(new[] { "s-1", "s-2" }, seen.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, engine.Calls);
        Assert.Equal("s-2", host.Latest["s-2"].SourceId);
        Assert.All(book.Lines, l => Assert.Equal(BookLine.TriggerManual, l.Trigger));
    }

    [Fact]
    public async Task StartReadsEverySourceAtOnceWithTheStartTriggerAndStopEndsIt()
    {
        var book = new MemoryBook();
        using var host = new SourceHost(new Factory(new StubEngine(Reading), book).Create, _ => 180);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (_, _) => ready.TrySetResult();
        host.Apply([SourceNamed("s-1", "CCGP")]);

        host.Start();
        Assert.True(host.Running);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Stop();

        Assert.False(host.Running);
        Assert.Equal(BookLine.TriggerStart, book.Lines[0].Trigger);
    }

    [Fact]
    public async Task ASourceAddedWhileRunningStartsReading()
    {
        using var host = new SourceHost(new Factory(new StubEngine(Reading), new MemoryBook()).Create, _ => 180);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.SnapshotReady += (id, _) => ready.TrySetResult(id);

        host.Start();
        host.Apply([SourceNamed("s-9", "NovaForge", SourceRole.Watch)]);

        Assert.Equal("s-9", await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task AReadThatThrowsBecomesASnapshotThatNamesOnlyTheErrorsType()
    {
        var engine = new StubEngine(() => throw new InvalidOperationException("failed at https://example.com/?key=SECRET-VALUE"));
        using var host = new SourceHost(new Factory(engine, new MemoryBook()).Create, _ => 180);
        host.Apply([SourceNamed("s-1", "CCGP")]);

        await host.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        var snapshot = host.Latest["s-1"];
        Assert.Equal(WatchState.SourceUnreachable, snapshot.State);
        Assert.Contains("InvalidOperationException", snapshot.Detail);
        Assert.DoesNotContain("SECRET-VALUE", snapshot.Detail);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'SourceHost' could not be found`.

- [ ] **Step 3: Create `src/Core/SourceHost.cs`**

```csharp
using System.Collections.Concurrent;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Score book spec §4.2: one <see cref="RecipeWatch"/> per enabled source, each on its own recipe's interval.
/// A source keeps its watch for as long as its id exists (F2: a watch built per cycle has a fresh
/// serialization guard, and one observation could then be reported twice).
/// </summary>
public sealed class SourceHost(Func<Source, RecipeWatch?> createWatch, Func<Source, int> intervalSeconds) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecipeSnapshot> _latest = new(StringComparer.Ordinal);
    private CancellationTokenSource? _run;

    /// <summary>Raised on the thread pool after each read, with the source id.</summary>
    public event Action<string, RecipeSnapshot>? SnapshotReady;

    public bool Running
    {
        get
        {
            lock (_gate) return _run is not null;
        }
    }

    public IReadOnlyDictionary<string, RecipeSnapshot> Latest => _latest;

    public RecipeWatch? WatchFor(string sourceId)
    {
        lock (_gate) return _entries.TryGetValue(sourceId, out var entry) ? entry.Watch : null;
    }

    public void Apply(IReadOnlyList<Source> sources)
    {
        lock (_gate)
        {
            var wanted = sources
                .Where(s => s.Enabled)
                .GroupBy(s => s.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var id in _entries.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                _entries[id].Stop.Cancel();
                _entries.Remove(id);
                _latest.TryRemove(id, out _);
            }

            foreach (var source in wanted.Values)
            {
                if (_entries.TryGetValue(source.Id, out var existing))
                {
                    existing.Source = source;
                    existing.Watch.UpdateSource(source);
                    continue;
                }

                if (createWatch(source) is not { } watch) continue;

                var entry = new Entry(source, watch);
                _entries[source.Id] = entry;
                if (_run is not null) StartLoop(entry, _run.Token);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_run is not null) return;

            _run = new CancellationTokenSource();
            foreach (var entry in _entries.Values) StartLoop(entry, _run.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _run?.Cancel();
            _run = null;
        }
    }

    public Task RunAllNowAsync(string trigger, CancellationToken cancellationToken)
    {
        List<Entry> entries;
        lock (_gate) entries = [.. _entries.Values];

        return Task.WhenAll(entries.Select(entry => RunOneAsync(entry, trigger, cancellationToken)));
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            foreach (var entry in _entries.Values) entry.Stop.Cancel();
        }
    }

    private void StartLoop(Entry entry, CancellationToken runToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, entry.Stop.Token);
        _ = Task.Run(() => LoopAsync(entry, linked.Token));
    }

    private async Task LoopAsync(Entry entry, CancellationToken cancellationToken)
    {
        var trigger = BookLine.TriggerStart;
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOneAsync(entry, trigger, cancellationToken).ConfigureAwait(false);
            trigger = BookLine.TriggerTimer;

            var seconds = Math.Max(Recipe.MinimumEverySeconds, intervalSeconds(entry.Source));
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOneAsync(Entry entry, string trigger, CancellationToken cancellationToken)
    {
        RecipeSnapshot snapshot;
        try
        {
            snapshot = await entry.Watch.RunOnceAsync(cancellationToken, trigger).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // The type only: an exception's message can carry an address with a key in it.
            snapshot = new RecipeSnapshot(WatchState.SourceUnreachable, $"The last read failed ({ex.GetType().Name}).", [], [], 0)
            {
                SourceId = entry.Source.Id,
                NotRecordingReason = "The last read failed.",
            };
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(entry.Source.Id, out var current) || !ReferenceEquals(current, entry)) return;
        }

        _latest[entry.Source.Id] = snapshot;
        SnapshotReady?.Invoke(entry.Source.Id, snapshot);
    }

    private sealed class Entry(Source source, RecipeWatch watch)
    {
        public Source Source { get; set; } = source;

        public RecipeWatch Watch { get; } = watch;

        public CancellationTokenSource Stop { get; } = new();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/SourceHost.cs tests/SourceHostTests.cs
git commit -m "core: SourceHost runs one watch per source, each on its own interval

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Reading the book: series, finals, records and honest numbers

Spec §9.1, §9.5, §9.6. These are pure, so every panel in Task 13 renders from them.

**Files:**
- Create: `src/Book/ScoreBookReader.cs`
- Create: `src/Book/Records.cs`
- Modify: `src/Core/StatHistory.cs` (add `StatText.Abbrev` and `StatText.Span`)
- Create: `tests/ScoreBookReaderTests.cs`

**Interfaces:**
- Consumes: `BookLine`, `BookFiles`, `BookJson`, `Source.KeyOf`.
- Produces, exactly as in the contract:
  - `SeriesPoint` (with `Off`), `FinalEntry`, `ScoreBookReader` (with `KeepReadings`);
  - `AccountRecords`, `Records`;
  - `StatText.Abbrev`, `StatText.Span`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ScoreBookReaderTests.cs`:

```csharp
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class ScoreBookReaderTests
{
    private const string Slug = "pet-sim-99-clan-battle-points";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> K0i2 = new() { ["clan"] = "K0i2" };

    private static BookLine Read(DateTimeOffset t, double points, string period = "B", DateTimeOffset? asOf = null, int off = -300, string source = "s-1") => new(
        BookLine.Version, BookLine.KindRead, t, off, BookLine.TriggerTimer, new BookRecipeRef(Slug, "3f9a1c0b7e2d4a55"), source, "mine", K0i2,
        new BookPeriod(period), new Dictionary<string, double> { ["clan-points"] = points * 10 }, ["value"],
        new Dictionary<string, BookAccount> { ["111"] = new(new Dictionary<string, double> { ["value"] = points }, AsOf: asOf) });

    private static BookLine Final(string period, DateTimeOffset t, double points, int rank, string account = "111") => new(
        BookLine.Version, BookLine.KindFinal, t, -300, BookLine.TriggerBackfill, new BookRecipeRef(Slug, "3f9a1c0b7e2d4a55"), "s-1", "mine", K0i2,
        new BookPeriod(period), new Dictionary<string, double> { ["clan-place"] = rank * 10 }, ["value"],
        new Dictionary<string, BookAccount> { [account] = new(new Dictionary<string, double> { ["value"] = points }, new Dictionary<string, int> { ["value"] = rank }, 48) });

    private static ScoreBookReader Reader(params BookLine[] lines)
    {
        var reader = new ScoreBookReader("unused-root", new ManualTime(Now));
        foreach (var line in lines) reader.Apply(line);
        return reader;
    }

    [Fact]
    public void ASeriesIsOneAccountsStatInOrderForAPeriod()
    {
        var reader = Reader(Read(Now.AddMinutes(-6), 20), Read(Now.AddMinutes(-9), 10), Read(Now.AddMinutes(-3), 30, period: "A"));

        var series = reader.Series("s-1", 111, "value", "B", DateTimeOffset.MinValue);

        Assert.Equal(new[] { 10d, 20d }, series.Select(p => p.Value).ToArray());
        Assert.Equal(-300, series[0].Off);
        Assert.Empty(reader.Series("s-2", 111, "value", "B", DateTimeOffset.MinValue));
    }

    [Fact]
    public void DuplicateReadingsCollapse()
    {
        // Ruling R4: same value and same asOf, or no asOf and under 60 s apart.
        var stamp = Now.AddMinutes(-30);
        var reader = Reader(
            Read(Now.AddMinutes(-10), 5, asOf: stamp), Read(Now.AddMinutes(-7), 5, asOf: stamp),
            Read(Now.AddMinutes(-4), 5), Read(Now.AddMinutes(-4).AddSeconds(20), 5),
            Read(Now.AddMinutes(-1), 5));

        Assert.Equal(3, reader.Series("s-1", 111, "value", "B", DateTimeOffset.MinValue).Count);
    }

    [Fact]
    public void ReadingsOlderThanThirtyFiveDaysAreNotKeptButStillCounted()
    {
        var reader = Reader(Read(Now.AddDays(-40), 1), Read(Now.AddDays(-1), 2));

        Assert.Single(reader.Series("s-1", 111, "value", null, DateTimeOffset.MinValue));
        Assert.Equal(2, reader.Readings(Slug));
        Assert.Equal(Now.AddDays(-40), reader.FirstReading(Slug));
    }

    [Fact]
    public void FinalsMergeSupplementaryLinesAndListNewestFirst()
    {
        var reader = Reader(Final("A", Now.AddDays(-20), 300, 2), Final("B", Now.AddDays(-5), 4200, 1), Final("A", Now.AddDays(-1), 250, 3, account: "333"));

        var finals = reader.Finals(Slug, Source.KeyOf(new Dictionary<string, string> { ["clan"] = "k0i2" }));

        Assert.Equal(new[] { "B", "A" }, finals.Select(f => f.Period).ToArray());
        Assert.Equal(new long[] { 111, 333 }, finals[1].Accounts.Keys.Order().ToArray());
        Assert.Equal(20, finals[1].Headline["clan-place"]);
    }

    [Fact]
    public void RecordsComeFromFinalsAndTheLastFiveWeeks()
    {
        var reader = Reader(
            Final("A", Now.AddDays(-20), 300, 2), Final("B", Now.AddDays(-5), 4200, 1),
            Read(Now.AddDays(-2).AddHours(1), 100), Read(Now.AddDays(-2).AddHours(10), 900),
            Read(Now.AddDays(-1).AddHours(1), 1000), Read(Now.AddDays(-1).AddHours(2), 1100));

        var records = Records.For(reader, Slug, Source.KeyOf(K0i2), ["s-1"], 111, "value", new ManualTime(Now));

        Assert.Equal((4200d, "B"), (records.BestPeriodValue!.Value, records.BestPeriod));
        Assert.Equal((1, "B"), (records.BestRank!.Value, records.BestRankPeriod));
        Assert.Equal(2, records.PeriodsPlayed);
        Assert.Equal(4200, records.Highest);
        Assert.Equal(800, records.BiggestDay);
        Assert.Equal(1000, records.FastestWeek);
    }

    [Fact]
    public void ADayIsTheAccountsLocalDayFromTheLinesOffset()
    {
        // 03:30 UTC is 22:30 the evening before at UTC-5, so both readings share one local day.
        var reader = Reader(Read(new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), 10), Read(new DateTimeOffset(2026, 9, 19, 3, 30, 0, TimeSpan.Zero), 70));

        Assert.Equal(60, Records.For(reader, Slug, Source.KeyOf(K0i2), ["s-1"], 111, "value", new ManualTime(Now)).BiggestDay);
    }

    [Fact]
    public void WouldPlaceRanksAValueAmongAnotherClansRows()
    {
        Assert.Equal((2, 4), Records.WouldPlace(9_100_000, [12_400_000, 8_240_900, 8_700_000]));
        Assert.Equal((1, 1), Records.WouldPlace(5, []));
    }

    [Fact]
    public void StalledNeedsTwoOthersAndMostOfThemMoving()
    {
        SeriesPoint P(double v, int minutes) => new(Now.AddMinutes(minutes), v, null, false, 0);
        var flat = new[] { P(5, -6), P(5, -3) };
        var moving = new[] { P(5, -6), P(9, -3) };

        Assert.True(Records.Stalled(flat, [moving, moving, flat]));
        Assert.False(Records.Stalled(flat, [moving]));
        Assert.False(Records.Stalled(moving, [moving, moving]));
        Assert.False(Records.Stalled(flat, [flat, flat, moving]));
    }

    [Fact]
    public void OverdueIsLaterThanOneAndAHalfIntervals()
    {
        Assert.False(Records.Overdue(Now.AddSeconds(-269), 180, Now));
        Assert.True(Records.Overdue(Now.AddSeconds(-271), 180, Now));
        Assert.False(Records.Overdue(null, 180, Now));
    }

    [Fact]
    public void ChangeSaysOverWhatSpan()
    {
        SeriesPoint P(double v, int minutes) => new(Now.AddMinutes(minutes), v, null, false, 0);

        Assert.Equal("+220K in 1h", Records.Change([P(1_000_000, -90), P(1_100_000, -60), P(1_320_000, 0)], Now));
        Assert.Equal("+50 in 20m", Records.Change([P(10, -20), P(60, 0)], Now));
        Assert.Equal("-1.5M in 3d", Records.Change([P(2_000_000, -72 * 60), P(500_000, 0)], Now));
        Assert.Equal("no earlier read", Records.Change([P(10, 0)], Now));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1234, "1.23K")]
    [InlineData(220000, "220K")]
    [InlineData(999999, "1M")]
    [InlineData(12418220, "12.4M")]
    [InlineData(9169613101, "9.17B")]
    [InlineData(-310000, "-310K")]
    public void NumbersAbbreviate(double value, string expected) => Assert.Equal(expected, StatText.Abbrev(value));

    [Fact]
    public void SpansAreMinutesHoursOrDays()
    {
        Assert.Equal("31m", StatText.Span(TimeSpan.FromMinutes(31)));
        Assert.Equal("5h", StatText.Span(TimeSpan.FromHours(5)));
        Assert.Equal("3d", StatText.Span(TimeSpan.FromDays(3)));
    }
}
```

**How the expected record numbers are worked out:**
- **`BiggestDay` is 800:** the day two days ago goes from 100 to 900.
- **`FastestWeek` is 1000:** the readings run 100 → 1100 within 7 days.
- **`Highest` is 4200:** the B final is higher than any reading.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'ScoreBookReader' could not be found`.

- [ ] **Step 3: Add `Abbrev` and `Span` to `StatText` in `src/Core/StatHistory.cs`**

```csharp
    private static readonly (double Divisor, string Suffix)[] Units = [(1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "K")];

    /// <summary>Short numbers for panels: 950, 1.23K, 220K, 12.4M, 9.17B. A value that would round up to 1000 of a unit moves to the next.</summary>
    public static string Abbrev(double value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);

        foreach (var (divisor, suffix) in Units)
        {
            if (abs < divisor * 0.9995) continue;

            var scaled = abs / divisor;
            var format = scaled >= 99.95 ? "0" : scaled >= 9.995 ? "0.#" : "0.##";
            return sign + scaled.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }

        return sign + abs.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>How long a change took: under an hour in minutes, under two days in hours, else days.</summary>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)Math.Round(span.TotalMinutes))}m";
        if (span < TimeSpan.FromHours(48)) return $"{(int)Math.Round(span.TotalHours)}h";
        return $"{(int)Math.Round(span.TotalDays)}d";
    }
```

The `0.9995` threshold is what keeps 999,999 from rounding up to "1000K" under K: it tips over to "1M".

- [ ] **Step 4: Create `src/Book/ScoreBookReader.cs`**

```csharp
using System.Globalization;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

public sealed record SeriesPoint(DateTimeOffset T, double Value, DateTimeOffset? AsOf, bool Stale, int Off);

public sealed record FinalEntry(string Period, DateTimeOffset T, IReadOnlyDictionary<string, double> Headline, IReadOnlyDictionary<long, BookAccount> Accounts);

/// <summary>
/// The book as panels read it (score book spec §9.1). Keeps reading lines from the last
/// <see cref="KeepReadings"/> and every final (Ruling R5); counts every reading it loaded.
/// </summary>
public sealed class ScoreBookReader(string root, TimeProvider time)
{
    public static readonly TimeSpan KeepReadings = TimeSpan.FromDays(35);

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, SlugData> _slugs = new(StringComparer.Ordinal);

    public void Load(IEnumerable<string> slugs)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        foreach (var slug in slugs.Distinct(StringComparer.Ordinal))
        {
            var data = new SlugData();
            foreach (var line in BookFiles.ReadAll(root, slug)) data.Add(line, cutoff);

            lock (_gate) _slugs[slug] = data;
        }
    }

    public void Apply(BookLine line)
    {
        var cutoff = time.GetUtcNow() - KeepReadings;
        lock (_gate)
        {
            if (!_slugs.TryGetValue(line.Recipe.Slug, out var data)) _slugs[line.Recipe.Slug] = data = new SlugData();
            data.Add(line, cutoff);
        }
    }

    public IReadOnlyList<SeriesPoint> Series(string sourceId, long userId, string stat, string? period, DateTimeOffset since)
    {
        var id = userId.ToString(CultureInfo.InvariantCulture);
        var points = Readings(sourceId, period, since)
            .Where(l => l.Accounts.TryGetValue(id, out var a) && a.V.ContainsKey(stat))
            .Select(l =>
            {
                var account = l.Accounts[id];
                return new SeriesPoint(l.T, account.V[stat], account.AsOf ?? l.AsOf, account.Stale ?? l.Stale ?? false, l.Off);
            });

        return Collapse(points);
    }

    public IReadOnlyList<SeriesPoint> HeadlineSeries(string sourceId, string headlineId, string? period) =>
        Collapse(Readings(sourceId, period, DateTimeOffset.MinValue)
            .Where(l => l.Headline.ContainsKey(headlineId))
            .Select(l => new SeriesPoint(l.T, l.Headline[headlineId], l.AsOf, l.Stale ?? false, l.Off)));

    public IReadOnlyList<FinalEntry> Finals(string slug, string inputsKey)
    {
        List<BookLine> finals;
        lock (_gate) finals = _slugs.TryGetValue(slug, out var data) ? [.. data.Finals] : [];

        return [.. finals
            .Select((line, order) => (line, order))
            .Where(x => x.line.Period is not null && Source.KeyOf(x.line.Inputs) == inputsKey)
            .GroupBy(x => x.line.Period!.Value, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.OrderBy(x => x.order).First();
                var accounts = new Dictionary<long, BookAccount>();
                foreach (var (line, _) in g.OrderBy(x => x.order))
                {
                    foreach (var (key, account) in line.Accounts)
                    {
                        if (long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) accounts.TryAdd(userId, account);
                    }
                }

                return (Entry: new FinalEntry(g.Key, g.Min(x => x.line.T), first.line.Headline, accounts), first.order);
            })
            .OrderByDescending(x => x.Entry.T)
            .ThenByDescending(x => x.order)
            .Select(x => x.Entry)];
    }

    public int Readings(string slug)
    {
        lock (_gate) return _slugs.TryGetValue(slug, out var data) ? data.ReadingCount : 0;
    }

    public DateTimeOffset? FirstReading(string slug)
    {
        lock (_gate) return _slugs.TryGetValue(slug, out var data) ? data.First : null;
    }

    public long Bytes(string slug) => BookFiles.Bytes(root, slug);

    private List<BookLine> Readings(string sourceId, string? period, DateTimeOffset since)
    {
        lock (_gate)
        {
            return [.. _slugs.Values
                .SelectMany(d => d.Readings)
                .Where(l => l.Source == sourceId && (period is null ? l.T >= since : l.Period?.Value == period))
                .OrderBy(l => l.T)];
        }
    }

    /// <summary>Ruling R4.</summary>
    private static List<SeriesPoint> Collapse(IEnumerable<SeriesPoint> points)
    {
        var kept = new List<SeriesPoint>();
        foreach (var point in points)
        {
            if (kept.Count > 0)
            {
                var last = kept[^1];
                var same = last.Value.Equals(point.Value)
                           && (last.AsOf is not null && point.AsOf is not null
                               ? last.AsOf == point.AsOf
                               : last.AsOf is null && point.AsOf is null && point.T - last.T < DuplicateWindow);
                if (same) continue;
            }

            kept.Add(point);
        }

        return kept;
    }

    private sealed class SlugData
    {
        public List<BookLine> Readings { get; } = [];

        public List<BookLine> Finals { get; } = [];

        public int ReadingCount { get; private set; }

        public DateTimeOffset? First { get; private set; }

        public void Add(BookLine line, DateTimeOffset cutoff)
        {
            if (line.Kind == BookLine.KindFinal)
            {
                Finals.Add(line);
                return;
            }

            if (line.Kind != BookLine.KindRead) return;

            ReadingCount++;
            if (First is null || line.T < First) First = line.T;
            if (line.T >= cutoff) Readings.Add(line);
        }
    }
}
```

**Check the duplicate test against this rule.** In `DuplicateReadingsCollapse` there are five lines:
1. The first two share an `asOf`, so they collapse into one point.
2. The third has no `asOf`, and its neighbour has one, so it stays.
3. The fourth is 20 s after the third with no `asOf`, so it collapses.
4. The fifth is 3 minutes later, so it stays.

That leaves three points.

- [ ] **Step 5: Create `src/Book/Records.cs`**

```csharp
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Book;

public sealed record AccountRecords(
    double? BestPeriodValue, string? BestPeriod, int? BestRank, string? BestRankPeriod, int PeriodsPlayed,
    double? Highest, double? BiggestDay, double? FastestWeek);

/// <summary>Score book spec §9.5 and §9.6. Pure: derived from the reader, never written anywhere.</summary>
public static class Records
{
    public static AccountRecords For(
        ScoreBookReader reader, string slug, string inputsKey, IEnumerable<string> sourceIds, long userId, string stat, TimeProvider time)
    {
        var finals = reader.Finals(slug, inputsKey)
            .Where(f => f.Accounts.TryGetValue(userId, out var account) && account.V.ContainsKey(stat))
            .Select(f => (f.Period, Account: f.Accounts[userId]))
            .ToList();

        (double Value, string Period)? best = finals.Count == 0 ? null : finals.Select(f => (f.Account.V[stat], f.Period)).MaxBy(x => x.Item1);

        var ranked = finals
            .Where(f => f.Account.Rank?.ContainsKey(stat) == true)
            .Select(f => (Rank: f.Account.Rank![stat], f.Period))
            .ToList();
        (int Rank, string Period)? bestRank = ranked.Count == 0 ? null : ranked.MinBy(x => x.Rank);

        var since = time.GetUtcNow() - ScoreBookReader.KeepReadings;
        var series = sourceIds.Distinct(StringComparer.Ordinal)
            .SelectMany(id => reader.Series(id, userId, stat, null, since))
            .OrderBy(p => p.T)
            .ToList();

        var values = series.Select(p => p.Value).Concat(finals.Select(f => f.Account.V[stat])).ToList();

        return new AccountRecords(
            best?.Value, best?.Period, bestRank?.Rank, bestRank?.Period, finals.Count,
            values.Count == 0 ? null : values.Max(), BiggestDay(series), FastestWeek(series));
    }

    /// <summary>Where this value would place among another group's values: competition place, and the group's size with this one added.</summary>
    public static (int Place, int Of)? WouldPlace(double value, IEnumerable<double> otherRows)
    {
        var others = otherRows.Where(double.IsFinite).ToList();
        return (1 + others.Count(o => o > value), others.Count + 1);
    }

    /// <summary>Unchanged over the last two readings while more than half of at least two others moved.</summary>
    public static bool Stalled(IReadOnlyList<SeriesPoint> mine, IEnumerable<IReadOnlyList<SeriesPoint>> others)
    {
        if (mine.Count < 2 || !mine[^1].Value.Equals(mine[^2].Value)) return false;

        var comparable = others.Where(o => o.Count >= 2).ToList();
        if (comparable.Count < 2) return false;

        var moved = comparable.Count(o => !o[^1].Value.Equals(o[^2].Value));
        return moved > comparable.Count / 2.0;
    }

    public static bool Overdue(DateTimeOffset? lastRead, int intervalSeconds, DateTimeOffset now) =>
        lastRead is { } last && now - last > TimeSpan.FromSeconds(intervalSeconds * 1.5);

    /// <summary>
    /// "+220K in 1h": the last value against the latest reading at least an hour before it, or the first
    /// reading when all are within the hour. <paramref name="now"/> is kept for callers that show "ago" beside it.
    /// </summary>
    public static string Change(IReadOnlyList<SeriesPoint> series, DateTimeOffset now)
    {
        if (series.Count < 2) return "no earlier read";

        var last = series[^1];
        var target = last.T - TimeSpan.FromHours(1);
        var earlier = series[0];
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= target)
            {
                earlier = series[i];
                break;
            }
        }

        var delta = last.Value - earlier.Value;
        return $"{(delta < 0 ? "-" : "+")}{StatText.Abbrev(Math.Abs(delta))} in {StatText.Span(last.T - earlier.T)}";
    }

    private static double? BiggestDay(IReadOnlyList<SeriesPoint> series)
    {
        var days = series
            .GroupBy(p => (p.T + TimeSpan.FromMinutes(p.Off)).UtcDateTime.Date)
            .Where(g => g.Count() >= 2)
            .Select(g => g.OrderBy(p => p.T).Last().Value - g.OrderBy(p => p.T).First().Value)
            .ToList();

        return days.Count == 0 ? null : days.Max();
    }

    private static double? FastestWeek(IReadOnlyList<SeriesPoint> series)
    {
        double? best = null;
        var start = 0;
        for (var i = 0; i < series.Count; i++)
        {
            while (series[i].T - series[start].T > TimeSpan.FromDays(7)) start++;
            if (start < i) best = Math.Max(best ?? double.MinValue, series[i].Value - series[start].Value);
        }

        return best;
    }
}
```

**Check the `+220K in 1h` case.** The last reading is at 0 min. The latest reading at or before −60 min is the one at −60, which reads 1,100,000. The change is 1,320,000 − 1,100,000 = 220,000 over exactly 1h, so "+220K in 1h".

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Book/ScoreBookReader.cs src/Book/Records.cs src/Core/StatHistory.cs tests/ScoreBookReaderTests.cs
git commit -m "book: series, finals and records read from the book, and honest change and overdue text

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Contract gaps raised while drafting the UI tasks, and where they are met

- **Watch sources keep their live rows.** `RecipeSnapshot.Rows` carries every row read for `watch` sources too. Task 6's `RecipeWatch` passes the reading to its snapshot in the watch-only branch.
- **`SourceRules.MakeMain` promotes any source.** It sets the source to `Main` from any role, `Watch` included, and turns the recipe's other `Main` into `Mine` (Task 3).
- **Groups and headline ids are always present.** `RecipeSnapshot.Groups` defaults to an empty list, and headline values carry `Id` and `Number` (Tasks 2 and 6).

---

## Things every UI task must know

- **The `Source` name clash.** The namespace `Labs626.UrScore.Source` already exists (`IconClient`, `NameClient`, `UrScoreIdentity`). In any file whose namespace is `Labs626.UrScore.UI`, `.Board` or `.Composition`, the simple name `Source` resolves to that namespace before any `using` at the top of the file is consulted. The fix is a `using` alias placed **after** the file-scoped `namespace` line, where it is consulted first:
  ```csharp
  namespace Labs626.UrScore.Board;

  using Source = Labs626.UrScore.Core.Source;
  ```
  Every file below that names the `Source` type does this. The same clash hits `Labs626.UrScore.Book` files (such as `ReadContext`), so Tasks 5–9 need the alias too. `UrScoreIdentity` is then written `global::Labs626.UrScore.Source.UrScoreIdentity`, assigned to a local first, because a `::` inside an interpolation hole reads as a format specifier. Test files (namespace `UrScore.Tests`) are not affected.
- **`System.IO` is not an implicit using in this WPF project.** WPF drops it so `Path` doesn't clash with `System.Windows.Shapes.Path`. Any `src/` file that uses `File`, `Directory` or `Path` needs `using System.IO;`. Tests get it from `tests/Usings.cs`.
- **No search-list fetch exists today.** Neither `MainWindow` nor `ImportWindow` reads an input's `search` list: the parser only checks the address is fixed (`RecipeParser.Validate`), and `ImportReview` names its host. Task 11 therefore adds the one path, `src/Recipes/SearchLists.cs`. It goes through the same shared `IRecipeTransport` as every recipe read (`SpacedTransport` over `HttpRecipeTransport`), so it is spaced per host, keeps the no-redirect/no-cookie handler, and names no host.
- **Theme.** Only these brushes exist, and only these may be used, always as `{DynamicResource ...}` in XAML or `SetResourceReference(..., "KeyBrush")` in code: `BgBrush`, `CyanBrush`, `MagentaBrush`, `WhiteBrush`, `MutedTextBrush`, `DividerBrush`, `RowBgBrush`, `RowHoverBrush`, `EdgeBrush`. The mock's green and amber have no theme slot, so "good" is `CyanBrush` and "warning" is `MagentaBrush`. `Background="Transparent"` is the only literal a XAML attribute may carry, and a `<Setter Property="Background" Value="Transparent" />` is fine too.
- **Hostnames.** No `something.com`/`.io`/`.net`/`.org`/`.gg`/`.dev`/`.app` text in `src/` (including comments and lowercase identifiers such as `page.app`). Hosts come from recipes at run time (`RecipeHosts.HostOf`).
- **Recipe words.** Titles and copy get "clan", "Clans", "battle" from the recipe through `RecipeWords` (Task 11): input label and `PluralLabel`, and the `period.value` take name. With the worked fixtures that gives "Clans", "Clan standing", "Battle race", "Past battles", "Top of the battle".
- **Build gate, end of every task:**
  ```text
  dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
  dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
  ```
- **A running Ur Score locks `bin\Release`.** Close it (tray or window) before building.

## Automation ids (smoke scripts rely on these; change them only with the scripts in the same commit)

| Window | Automation ids |
|---|---|
| Board (`RoRoRo Ur Score`) | `PeriodLine`, `StartStopButton`, `TestNowButton`, `SetupButton`, `StateLine`, `DetailLine`, `BoardPanels`, `EmptyState`, `EmptyStateLine`, `EmptyStateButton`, `AttributionLine`, `BoardIcon`; each panel `StandingPanel1`, `StandingPanel2`, `RacePanel1`, `MyAccountsPanel1`, `PromotionCheckPanel1`, `AccountCardPanel1`, `TopPanel1`, `PastPeriodsPanel1`, `ProfileStatPanel1`, `ProfileStatPanel2`, `RecordsPanel1`, `LiveLeaderboardPanel1`; inside a panel `PanelTitle`, `PanelSubtitle`, `PanelStale`, `PanelNote` |
| Setup (`Setup`) | `SetupNav` (list items named by page title), `PageHost` |
| Setup › Clans | `ClansPageTitle`, `MainClanLabel`, `MainCurrentLine`, `MainClanSearch`, `MainFoundLine`, `MainWatchInsteadButton`, `MineList`, `AddMineButton`, `MineClanSearch`, `MineFoundLine`, `MineWatchInsteadButton`, `WatchList`, `WatchClanButton`, `WatchClanSearch`, `WatchFoundLine`, `TopRow`, `TopSwitch`, `RequestsLine`; row buttons named `Make <name> main`, `Remove <name>` |
| ClanSearchBox (inside each search) | `SearchText`, `SearchMatches` (buttons named `Pick <name>`), `SearchStatus` |
| Setup › Your accounts | `ListedLine`, `AccountsBudgetLine`, `AccountsTable`; checkboxes named `Send <account> for <recipe>` |
| Setup › Stats | `StatsRecipeBox`, `StatsGroupListLine`, `StatsTable`, `SaveStatsButton`, `StatsSavedLine` |
| StatsTable (import screen and Stats page) | `StatsSearchBox`, `ShowingLine`, `StatsRows` (checkboxes `Show <label>`, `Send <label>`, text box `Name RoRoRo uses for <label>`), `ReadNamesButton`, `NamesLine`, `SlotLine`, `RuleLine`, `RefusalLine` |
| Import screen (`Import recipe` / `Update recipe`; `Recipe settings` until Task 14) | `ImportButton`, `ImportRefusalLine`, `KeptNote`, `KeptList`, `StatsSection`, `StatsTable` (and its ids above), `ChangesLine`, `PollLine` |
| Setup › Recipes | `RecipesList` (buttons `Remove <name>`), `ImportRecipeButton`, `RecipesLine` |
| Setup › Alerts | `RuleStatBox`, `RuleLine`, `AddRuleButton`, `RulePreview`, `PolicyList` |
| Setup › Score book | `BookFolderLine`, `OpenBookFolderButton`, `BookRecipesList`, `NotRecordingList`, `BookPendingLine` |
| Setup › Diagnostics | `SourcesDiagnostics`, `CopyDiagnosticsButton`, `DiagnosticsLine` |

---

### Task 10: One searchable Stats table

Spec §7.3. One table replaces the Stats section, **Look up stat names**, **Add a game statistic** and **Add this statistic**:
- The rows are recipe values in recipe order, then every counter name the source offers in source order, then saved counters not in that list, then saved choices the recipe no longer offers (greyed, never tickable).
- The columns are label, Show, Send, and "Name RoRoRo uses", which only shows on a row with Send ticked.
- A search box filters by label, ignoring case, and ticked rows always stay visible.
- The list virtualizes.
- The slot line, the budget refusal that undoes a Send tick, `StatRules` problems and "tick at least one stat" are unchanged.

The filtering and row building live in the pure `StatsTableModel`. The import screen also gets **Show every game statistic (reads once from the hosts above)** and a "Kept in your score book" list. `MainWindow` still exists until Task 14, and this task keeps it compiling: the import screen keeps its input boxes and its settings mode for now.

**Files:**
- Create: `src/UI/Controls/StatsTableModel.cs`, `src/UI/Controls/StatsTable.xaml`, `src/UI/Controls/StatsTable.xaml.cs`, `src/UI/ImportText.cs`
- Create: `tests/StatsTableModelTests.cs`, `tests/ImportTextTests.cs`
- Modify: `src/UI/ImportWindow.xaml`, `src/UI/ImportWindow.xaml.cs`, `src/UI/MainWindow.xaml.cs` (the `CounterLookup` record moves out of `ImportWindow`)

**Interfaces:**
- Consumes:
  - existing: `Recipe`, `RecipeState`, `StatChoice`, `InstalledRecipe`, `RecipeStats`, `StatRules`, `HistoryBudget`, `BudgetCheck`, `RecipeHosts`
  - Task 1: `Recipe.IsGroupList`
- Produces (namespace `Labs626.UrScore.UI`):
  - `sealed record CounterLookup(IReadOnlyList<string> Names, string? Problem)` (was nested in `ImportWindow`)
  - `sealed class StatRow : INotifyPropertyChanged`, with members:
    - `required string Key`, `required string Label`, `bool Offered`
    - `bool Show`, `bool Send`, `string MetricId`
    - `bool Ticked`, `bool NameShown`, `string DisplayLabel`, `string ShowName`, `string SendName`, `string MetricIdName`
  - `static class StatsTableModel`, with members:
    - `IReadOnlyList<StatRow> Build(Recipe recipe, IReadOnlyDictionary<string, StatChoice> choices, IReadOnlyList<string> counterNames)`
    - `Dictionary<string, StatChoice> Choices(IReadOnlyDictionary<string, StatChoice> saved, IEnumerable<StatRow> rows)`
    - `IReadOnlyList<StatRow> Visible(IReadOnlyList<StatRow> rows, string? query)`
    - `string ShowingLine(int visible, int total, string? query)`
    - `bool AnyTicked(IEnumerable<StatRow> rows)`
    - `BudgetCheck Budget(Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)`
    - `IReadOnlyList<string> Refusals(IEnumerable<string> extra, IEnumerable<StatRow> rows, string? budgetRefusal)`
    - `IReadOnlyList<string> SaveProblems(Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)`
    - `string RuleLines(IEnumerable<StatRow> rows, Func<string, string> ruleSentence)`
    - `string NamesLine(Recipe recipe, int savedNames)`, `string FoundNamesLine(int count)`, `string ReadingNamesLine(Recipe recipe)`
  - `partial class StatsTable : UserControl`, with members:
    - `void Load(Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed, Func<IReadOnlyCollection<Guid>> accountIds, Func<string, string> ruleSentence, Func<CancellationToken, Task<CounterLookup>>? readNames, string readNamesLabel, IReadOnlyList<string>? extraRefusals = null)`
    - `Task ReadNamesAsync(CancellationToken cancellationToken)`
    - `IReadOnlyDictionary<string, StatChoice> Choices`, `IReadOnlyList<string> CounterNames`, `bool AnyTicked`, `bool HasSavedNames`
    - `IReadOnlyList<string> SaveProblems()`, `void ShowProblems(IReadOnlyList<string> problems)`
    - `event EventHandler? Changed`
  - `static class ImportText`, with members:
    - `const string ShowEveryStat`
    - `IReadOnlyList<string> Kept(Recipe recipe)`
    - `string KeptNote(Recipe recipe)`

- [ ] **Step 1: Write the failing tests**

Create `tests/StatsTableModelTests.cs`

```csharp
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
            ("rebirths", new StatChoice(false, false, "ps99.rebirths")));

        var rows = StatsTableModel.Build(Profile, saved, ["Pets Hatched", "Coins Spent", "Pets Hatched", "123456", "Bad.Name"]);

        Assert.Equal(
            new[] { "diamonds", "eggs", "rank", "counter:Pets Hatched", "counter:Coins Spent", "counter:Zones Unlocked", "rebirths" },
            rows.Select(r => r.Key).ToArray());
        Assert.All(rows.Take(6), row => Assert.True(row.Offered));
        Assert.False(rows[^1].Offered);
        Assert.Equal("rebirths (no longer offered)", rows[^1].DisplayLabel);
        Assert.Equal("Coins Spent", rows[4].DisplayLabel);
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
        var rows = StatsTableModel.Build(Profile, Saved(("rebirths", new StatChoice(true, true, "ps99.rebirths"))), []);

        var gone = rows.Single(r => r.Key == "rebirths");
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
```

Create `tests/ImportTextTests.cs`

```csharp
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ImportTextTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ],
          "headline": [ { "label": "Total", "path": "data.total" } ]
        }
        """;

    [Fact]
    public void EveryHeadlineItemIsListedAsKept()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

        Assert.Equal(new[] { "Clan place", "Clan points" }, ImportText.Kept(clan).ToArray());
        Assert.Equal("Every read keeps these headline items, and the stats you tick for your own accounts only.", ImportText.KeptNote(clan));
    }

    [Fact]
    public void ARecipeWithNoHeadlineKeepsOnlyTheTickedStats()
    {
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Empty(ImportText.Kept(profile));
        Assert.Equal("Every read keeps the stats you tick, for your own accounts only.", ImportText.KeptNote(profile));
    }

    [Fact]
    public void AGroupListKeepsNothing()
    {
        var parsed = RecipeParser.Parse(GroupList);
        Assert.True(parsed.Ok, string.Join(" ", parsed.Problems));

        Assert.Empty(ImportText.Kept(parsed.Recipe!));
        Assert.Equal("Nothing from this recipe is kept. Its rows are groups, shown live only.", ImportText.KeptNote(parsed.Recipe!));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: the build FAILS with `error CS0246: The type or namespace name 'StatRow' could not be found` and `error CS0103: The name 'StatsTableModel' does not exist in the current context` and `error CS0103: The name 'ImportText' does not exist in the current context`.

- [ ] **Step 3: Create `src/UI/Controls/StatsTableModel.cs`**

Create `src/UI/Controls/StatsTableModel.cs`

```csharp
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>What a one-off read of counter names found, or why it found nothing.</summary>
public sealed record CounterLookup(IReadOnlyList<string> Names, string? Problem);

/// <summary>
/// One row of the Stats table (spec §7.3). Raises change notifications so the slot line, the refusals and the
/// import button follow every tick. A row the recipe no longer offers is shown greyed and never ticked.
/// </summary>
public sealed class StatRow : INotifyPropertyChanged
{
    private bool _show;
    private bool _send;
    private string _metricId = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>False for a saved choice this recipe no longer offers.</summary>
    public bool Offered { get; init; } = true;

    public bool Show
    {
        get => _show;
        set
        {
            if (SetField(ref _show, value)) Raise(nameof(Ticked));
        }
    }

    public bool Send
    {
        get => _send;
        set
        {
            if (!SetField(ref _send, value)) return;
            Raise(nameof(Ticked));
            Raise(nameof(NameShown));
        }
    }

    public string MetricId { get => _metricId; set => SetField(ref _metricId, value); }

    public bool Ticked => Show || Send;

    /// <summary>"Name RoRoRo uses" only matters for a stat that is sent, so it shows only then.</summary>
    public bool NameShown => Send;

    public string DisplayLabel => Offered ? Label : $"{Label} (no longer offered)";

    public string ShowName => $"Show {Label}";

    public string SendName => $"Send {Label}";

    public string MetricIdName => $"Name RoRoRo uses for {Label}";

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// The Stats table's rules, kept out of the control so they are testable (spec §7.3). The budget and
/// save rules are the ones the import screen used before, moved here unchanged.
/// </summary>
public static class StatsTableModel
{
    /// <summary>
    /// Recipe values in recipe order, then counter names in the order the source gave them, then saved
    /// counters that are not in that list, then saved choices this recipe no longer offers.
    /// </summary>
    public static IReadOnlyList<StatRow> Build(
        Recipe recipe, IReadOnlyDictionary<string, StatChoice> choices, IReadOnlyList<string> counterNames)
    {
        var rows = new List<StatRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(RecipeStat stat)
        {
            if (!seen.Add(stat.Key)) return;

            var choice = choices.GetValueOrDefault(stat.Key);
            rows.Add(new StatRow
            {
                Key = stat.Key,
                Label = stat.Label,
                Show = choice?.Show ?? false,
                Send = choice?.Send ?? false,
                MetricId = choice?.MetricId ?? stat.SuggestedMetricId,
            });
        }

        foreach (var value in recipe.LastStep.Values)
        {
            if (RecipeStats.Find(recipe, value.Id) is { } stat) Add(stat);
        }

        if (recipe.LastStep.Counters is not null)
        {
            foreach (var name in counterNames.Where(RecipeStats.CanPick))
            {
                if (RecipeStats.Find(recipe, RecipeStats.CounterKey(name)) is { } stat) Add(stat);
            }

            foreach (var key in choices.Keys.Where(key => RecipeStats.IsCounterKey(key, out _)).Order(StringComparer.Ordinal))
            {
                if (RecipeStats.Find(recipe, key) is { } stat) Add(stat);
            }
        }

        foreach (var key in choices.Keys.Where(key => !seen.Contains(key)).Order(StringComparer.Ordinal))
        {
            seen.Add(key);
            rows.Add(new StatRow
            {
                Key = key,
                Label = RecipeStats.IsCounterKey(key, out var name) ? name : key,
                Offered = false,
                MetricId = choices[key].MetricId,
            });
        }

        return rows;
    }

    /// <summary>
    /// Every entry already saved, plus every offered row ticked now. An entry, once written, keeps its
    /// name through recipe updates (stats design §5.1), so a row never ticked writes nothing.
    /// </summary>
    public static Dictionary<string, StatChoice> Choices(IReadOnlyDictionary<string, StatChoice> saved, IEnumerable<StatRow> rows)
    {
        var choices = new Dictionary<string, StatChoice>(saved, StringComparer.Ordinal);
        foreach (var row in rows.Where(r => r.Offered && (choices.ContainsKey(r.Key) || r.Ticked)))
        {
            choices[row.Key] = new StatChoice(row.Show, row.Send, row.MetricId.Trim());
        }

        return choices;
    }

    /// <summary>Rows whose label contains the query, ignoring case, plus every ticked row. A blank query shows all.</summary>
    public static IReadOnlyList<StatRow> Visible(IReadOnlyList<StatRow> rows, string? query)
    {
        var trimmed = query?.Trim() ?? "";
        return trimmed.Length == 0
            ? rows
            : [.. rows.Where(r => r.Ticked || r.Label.Contains(trimmed, StringComparison.OrdinalIgnoreCase))];
    }

    public static string ShowingLine(int visible, int total, string? query) =>
        string.IsNullOrWhiteSpace(query) ? "" : $"Showing {visible} of {total} · ticked stats always shown";

    public static bool AnyTicked(IEnumerable<StatRow> rows) => rows.Any(r => r.Offered && r.Ticked);

    /// <summary>Stats design §5.3: the history slots these ticks would use, across installed recipes.</summary>
    public static BudgetCheck Budget(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)
    {
        var sending = accountIds.Count(id => !existing.Excluded.Contains(id));
        var others = HistoryBudget.Installed(installed, accountIds, exceptSlug: recipe.Slug);
        var before = (sending, existing.SentStats(recipe).Count);
        var after = (sending, new RecipeState(Stats: Choices(existing.StatChoices, rows)).SentStats(recipe).Count);
        return HistoryBudget.Check(others, before, after, accountsKnown: accountIds.Count > 0);
    }

    /// <summary>The screen's own refusals, then "tick at least one", then the last budget refusal.</summary>
    public static IReadOnlyList<string> Refusals(IEnumerable<string> extra, IEnumerable<StatRow> rows, string? budgetRefusal)
    {
        var refusals = extra.ToList();
        if (!AnyTicked(rows)) refusals.Add(StatRules.TickOne);
        if (budgetRefusal is not null) refusals.Add(budgetRefusal);
        return refusals;
    }

    /// <summary>Every reason these ticks cannot be saved: <see cref="StatRules"/>, then the budget.</summary>
    public static IReadOnlyList<string> SaveProblems(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        IReadOnlyCollection<Guid> accountIds, IEnumerable<StatRow> rows)
    {
        var list = rows.ToList();
        var problems = StatRules.Problems(recipe, Choices(existing.StatChoices, list), installed).ToList();
        if (Budget(recipe, existing, installed, accountIds, list) is { Allowed: false } refused) problems.Add(refused.Line);
        return problems;
    }

    public static string RuleLines(IEnumerable<StatRow> rows, Func<string, string> ruleSentence) =>
        string.Join(Environment.NewLine, rows
            .Where(r => r.Offered && r.Send && !string.IsNullOrWhiteSpace(r.MetricId))
            .Select(r => $"{r.Label}: {ruleSentence(r.MetricId.Trim())}"));

    public static string NamesLine(Recipe recipe, int savedNames) =>
        recipe.LastStep.Counters is null ? ""
        : savedNames == 0 ? "No statistic names read yet."
        : $"{savedNames.ToString("N0", CultureInfo.InvariantCulture)} statistic names from the last read.";

    public static string FoundNamesLine(int count) =>
        $"Found {count.ToString("N0", CultureInfo.InvariantCulture)} statistic names. Search them above.";

    public static string ReadingNamesLine(Recipe recipe) =>
        $"Reading stat names once from {RecipeHosts.HostOf(recipe.LastStep.Url)}…";
}
```

- [ ] **Step 4: Create `src/UI/ImportText.cs`**

Create `src/UI/ImportText.cs`

```csharp
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>The import screen's words about the score book (spec §7.3, §14: headline items are listed as kept).</summary>
public static class ImportText
{
    public const string ShowEveryStat = "Show every game statistic (reads once from the hosts above)";

    /// <summary>The headline items every successful read writes to the book. A group list writes nothing.</summary>
    public static IReadOnlyList<string> Kept(Recipe recipe) =>
        recipe.IsGroupList ? [] : [.. recipe.Headline.Select(h => h.Label)];

    public static string KeptNote(Recipe recipe) =>
        recipe.IsGroupList ? "Nothing from this recipe is kept. Its rows are groups, shown live only."
        : recipe.Headline.Count == 0 ? "Every read keeps the stats you tick, for your own accounts only."
        : "Every read keeps these headline items, and the stats you tick for your own accounts only.";
}
```

- [ ] **Step 5: Run the model tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~StatsTableModelTests|FullyQualifiedName~ImportTextTests"`
Expected: PASS, 15 tests.

- [ ] **Step 6: Create the control**

Create `src/UI/Controls/StatsTable.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.StatsTable"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AutomationProperties.AutomationId="StatsTable">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="Visible" />
        <Style x:Key="StatLabel" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="Margin" Value="0,0,8,0" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Offered}" Value="False">
                    <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </UserControl.Resources>
    <StackPanel>
        <TextBlock Style="{StaticResource Muted}" Margin="0,0,0,8"
                   Text="Show puts a stat on your board and in your score book. Send also reports it to RoRoRo, where alerts still need a rule." />
        <TextBox x:Name="StatsSearchBox" TextChanged="OnSearchChanged" AutomationProperties.Name="Search stats" />
        <TextBlock x:Name="ShowingLine" Style="{StaticResource Muted}" Margin="0,4,0,0" Visibility="Collapsed" />

        <Grid Margin="8,10,8,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="64" />
                <ColumnDefinition Width="64" />
                <ColumnDefinition Width="220" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="Stat" Style="{StaticResource Muted}" />
            <TextBlock Grid.Column="1" Text="Show" Style="{StaticResource Muted}" />
            <TextBlock Grid.Column="2" Text="Send" Style="{StaticResource Muted}" />
            <TextBlock Grid.Column="3" Text="Name RoRoRo uses" Style="{StaticResource Muted}" />
        </Grid>

        <!-- Virtualized: a profile recipe can offer hundreds of statistic names. A fixed height keeps
             virtualization on inside a scrolling window. -->
        <ListBox x:Name="StatsRows" Height="300"
                 Background="{DynamicResource BgBrush}" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1"
                 Foreground="{DynamicResource WhiteBrush}"
                 ScrollViewer.CanContentScroll="True" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                 VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.VirtualizationMode="Recycling"
                 KeyboardNavigation.TabNavigation="Continue"
                 AutomationProperties.Name="Stats this recipe offers">
            <ListBox.ItemContainerStyle>
                <Style TargetType="ListBoxItem">
                    <Setter Property="Focusable" Value="False" />
                    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <Border x:Name="Item" Background="Transparent" Padding="8,4">
                                    <ContentPresenter />
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Item" Property="Background" Value="{DynamicResource RowHoverBrush}" />
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ListBox.ItemContainerStyle>
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="64" />
                            <ColumnDefinition Width="64" />
                            <ColumnDefinition Width="220" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{Binding DisplayLabel}" Style="{StaticResource StatLabel}" />
                        <CheckBox Grid.Column="1" IsChecked="{Binding Show}" IsEnabled="{Binding Offered}"
                                  VerticalAlignment="Center" AutomationProperties.Name="{Binding ShowName}" />
                        <CheckBox Grid.Column="2" IsChecked="{Binding Send}" IsEnabled="{Binding Offered}"
                                  VerticalAlignment="Center" AutomationProperties.Name="{Binding SendName}" />
                        <TextBox Grid.Column="3" Text="{Binding MetricId, UpdateSourceTrigger=PropertyChanged}"
                                 FontFamily="{StaticResource MonoFont}"
                                 Visibility="{Binding NameShown, Converter={StaticResource Visible}}"
                                 AutomationProperties.Name="{Binding MetricIdName}" />
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <StackPanel x:Name="CounterPanel" Margin="0,8,0,0" Visibility="Collapsed">
            <Button x:Name="ReadNamesButton" HorizontalAlignment="Left" Click="OnReadNamesClick" />
            <TextBlock x:Name="NamesLine" Margin="0,4,0,0" Style="{StaticResource Muted}" />
        </StackPanel>

        <TextBlock x:Name="SlotLine" Margin="0,12,0,0" Style="{StaticResource Muted}" />
        <TextBlock x:Name="RuleLine" Margin="0,6,0,0" Style="{StaticResource Muted}" />
        <TextBlock x:Name="RefusalLine" Margin="0,12,0,0" Style="{StaticResource Refusal}" />
    </StackPanel>
</UserControl>
```

Create `src/UI/Controls/StatsTable.xaml.cs`

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// The single searchable Stats table (spec §7.3), shared by the import screen and Setup › Stats. Holds no
/// HTTP: the one read it can ask for goes through the callback its host supplies.
/// </summary>
public partial class StatsTable : UserControl
{
    private Recipe? _recipe;
    private RecipeState _existing = new();
    private IReadOnlyList<InstalledRecipe> _installed = [];
    private Func<IReadOnlyCollection<Guid>> _accountIds = () => [];
    private Func<string, string> _ruleSentence = _ => "";
    private Func<CancellationToken, Task<CounterLookup>>? _readNames;
    private IReadOnlyList<string> _extraRefusals = [];
    private IReadOnlyList<StatRow> _rows = [];
    private List<string> _counterNames = [];

    /// <summary>Why the last Send tick was undone, kept until the next change.</summary>
    private string? _budgetRefusal;

    private bool _reverting;
    private bool _reading;

    public StatsTable() => InitializeComponent();

    /// <summary>Raised after any tick, name or refusal change, so the host can enable its button.</summary>
    public event EventHandler? Changed;

    public IReadOnlyDictionary<string, StatChoice> Choices => StatsTableModel.Choices(_existing.StatChoices, _rows);

    public IReadOnlyList<string> CounterNames => _counterNames;

    public bool AnyTicked => StatsTableModel.AnyTicked(_rows);

    public bool HasSavedNames => _counterNames.Count > 0;

    /// <param name="accountIds">Asked each time the budget is worked out, so accounts listed later are counted.</param>
    /// <param name="readNames">The one read of counter names, or null when this host offers none.</param>
    /// <param name="extraRefusals">The host's own refusals, shown first in the refusal line.</param>
    public void Load(
        Recipe recipe, RecipeState existing, IReadOnlyList<InstalledRecipe> installed,
        Func<IReadOnlyCollection<Guid>> accountIds, Func<string, string> ruleSentence,
        Func<CancellationToken, Task<CounterLookup>>? readNames, string readNamesLabel,
        IReadOnlyList<string>? extraRefusals = null)
    {
        _recipe = recipe;
        _existing = existing;
        _installed = installed;
        _accountIds = accountIds;
        _ruleSentence = ruleSentence;
        _readNames = readNames;
        _extraRefusals = extraRefusals ?? [];
        _budgetRefusal = null;
        _counterNames = [.. existing.SavedCounterNames];

        ReadNamesButton.Content = readNamesLabel;
        AutomationProperties.SetName(ReadNamesButton, readNamesLabel);
        CounterPanel.Visibility = recipe.LastStep.Counters is null || readNames is null ? Visibility.Collapsed : Visibility.Visible;
        Show(NamesLine, StatsTableModel.NamesLine(recipe, _counterNames.Count));

        StatsSearchBox.Text = "";
        Rebuild(existing.StatChoices);
    }

    /// <summary>Reads counter names once and rebuilds the rows, keeping every tick made so far.</summary>
    public async Task ReadNamesAsync(CancellationToken cancellationToken)
    {
        if (_recipe is null || _readNames is null || _reading) return;

        var recipe = _recipe;
        _reading = true;
        ReadNamesButton.IsEnabled = false;
        Show(NamesLine, StatsTableModel.ReadingNamesLine(recipe));

        try
        {
            var found = await _readNames(cancellationToken);
            if (!ReferenceEquals(recipe, _recipe)) return;

            if (found.Names.Count > 0)
            {
                _counterNames = [.. found.Names];
                Rebuild(Choices);
            }

            Show(NamesLine, found.Names.Count > 0
                ? StatsTableModel.FoundNamesLine(found.Names.Count)
                : found.Problem ?? "No statistic names came back.");
            Refresh();
        }
        catch (OperationCanceledException)
        {
            // The page closed; nothing to show.
        }
        catch (Exception ex)
        {
            Show(NamesLine, $"Could not read them ({ex.GetType().Name}).");
        }
        finally
        {
            _reading = false;
            ReadNamesButton.IsEnabled = true;
        }
    }

    public IReadOnlyList<string> SaveProblems() =>
        _recipe is null ? [] : StatsTableModel.SaveProblems(_recipe, _existing, _installed, _accountIds(), _rows);

    /// <summary>Stays on screen until the next change, so a collided name can be fixed right here.</summary>
    public void ShowProblems(IReadOnlyList<string> problems) => Show(RefusalLine, string.Join(Environment.NewLine, problems));

    private async void OnReadNamesClick(object sender, RoutedEventArgs e) => await ReadNamesAsync(CancellationToken.None);

    private void Rebuild(IReadOnlyDictionary<string, StatChoice> choices)
    {
        foreach (var row in _rows) row.PropertyChanged -= OnRowChanged;
        _rows = StatsTableModel.Build(_recipe!, choices, _counterNames);
        foreach (var row in _rows) row.PropertyChanged += OnRowChanged;

        ApplyFilter();
        Refresh();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_recipe is not null) ApplyFilter();
    }

    /// <summary>Filters only when the search changes, never on a tick, so a row never jumps out from under the pointer.</summary>
    private void ApplyFilter()
    {
        var visible = StatsTableModel.Visible(_rows, StatsSearchBox.Text);
        StatsRows.ItemsSource = visible;
        Show(ShowingLine, StatsTableModel.ShowingLine(visible.Count, _rows.Count, StatsSearchBox.Text));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(StatRow.Show) or nameof(StatRow.Send) or nameof(StatRow.MetricId))) return;

        if (!_reverting)
        {
            _budgetRefusal = null;

            // Stats design §5.3: a Send tick that would pass RoRoRo's limit is undone, and says why.
            if (sender is StatRow { Send: true } row && e.PropertyName == nameof(StatRow.Send) && _recipe is not null
                && StatsTableModel.Budget(_recipe, _existing, _installed, _accountIds(), _rows) is { Allowed: false } refused)
            {
                _budgetRefusal = refused.Line;
                Dispatcher.BeginInvoke(() =>
                {
                    _reverting = true;
                    row.Send = false;
                    _reverting = false;
                    Refresh();
                });
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_recipe is null) return;

        Show(SlotLine, StatsTableModel.Budget(_recipe, _existing, _installed, _accountIds(), _rows).Line);
        Show(RuleLine, StatsTableModel.RuleLines(_rows, _ruleSentence));
        Show(RefusalLine, string.Join(Environment.NewLine, StatsTableModel.Refusals(_extraRefusals, _rows, _budgetRefusal)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An empty line takes no space.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 7: Put the table on the import screen**

Replace the whole of `src/UI/ImportWindow.xaml` with

```xml
<Window x:Class="Labs626.UrScore.UI.ImportWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:Labs626.UrScore.UI"
        Title="Import recipe" Width="680" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="860">
        <StackPanel Margin="20,18,20,18">
            <TextBlock x:Name="NameLine" Style="{StaticResource Heading}" />
            <TextBlock x:Name="CreditLine" Margin="0,4,0,0" Style="{StaticResource Muted}" />
            <TextBlock x:Name="AuthorLine" Margin="0,2,0,0" Style="{StaticResource Muted}" FontStyle="Italic" />

            <TextBlock Text="YOUR PC WILL CONTACT" Style="{StaticResource SectionLabel}" Margin="0,18,0,6" />
            <ItemsControl x:Name="HostsList" AutomationProperties.Name="Every host this recipe contacts and what each receives">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource Card}" Padding="12,8" Margin="0,0,0,6">
                            <StackPanel>
                                <TextBlock Text="{Binding Host}" FontWeight="SemiBold" FontFamily="{StaticResource MonoFont}" />
                                <TextBlock Text="{Binding SendsText}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock x:Name="PollLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ReusedLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ChangesLine" Margin="0,4,0,0" TextWrapping="Wrap" />

            <ItemsControl x:Name="InputsList" Margin="0,16,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="0,0,0,12">
                            <TextBlock Text="{Binding Label}" FontWeight="SemiBold" Margin="0,0,0,4" />
                            <TextBox Text="{Binding Value, UpdateSourceTrigger=PropertyChanged}"
                                     AutomationProperties.Name="{Binding Label}" />
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Spec §7.3 and §14: what every successful read writes to the score book. -->
            <TextBlock Text="KEPT IN YOUR SCORE BOOK" Style="{StaticResource SectionLabel}" Margin="0,8,0,4" />
            <TextBlock x:Name="KeptNote" Style="{StaticResource Muted}" />
            <ItemsControl x:Name="KeptList" Margin="0,4,0,0" AutomationProperties.Name="Kept in your score book">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding}" Margin="0,2,0,0" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Nothing is ticked for the user: a recipe that pre-ticked a stat would be deciding what gets
                 sent. A group list has no stats to tick at all. -->
            <StackPanel x:Name="StatsSection">
                <TextBlock Text="STATS" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
                <ui:StatsTable x:Name="StatsTable" />
            </StackPanel>

            <TextBlock x:Name="ImportRefusalLine" Margin="0,12,0,0" Style="{StaticResource Refusal}" Visibility="Collapsed" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,18,0,0">
                <Button Content="Cancel" IsCancel="True" Margin="0,0,8,0" />
                <Button x:Name="ImportButton" Content="Import" IsDefault="True" Style="{StaticResource PrimaryButton}"
                        Click="OnImportClick" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Window>
```

Replace the whole of `src/UI/ImportWindow.xaml.cs` with

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2, stats design §7.1), shown before anything a recipe describes runs:
/// every host it contacts and exactly what each receives, what the score book keeps, and the Stats
/// table. Holds no HTTP of its own; the one read it can ask for goes through the callback it is given.
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
    private readonly ImportReviewResult _review;
    private readonly RecipeState _existing;
    private readonly List<InputItem> _inputs;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        IReadOnlyList<InstalledRecipe> installed, Func<IReadOnlyCollection<Guid>> accountIds,
        Func<string, string> ruleSentence,
        Func<IReadOnlyDictionary<string, string>, Task<CounterLookup>>? lookUpCounters,
        bool settingsOnly = false)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _recipe = recipe;
        _review = review;
        _existing = existing ?? new RecipeState();

        Title = settingsOnly ? "Recipe settings" : comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, ImportReview.SendsText(h)))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        Show(ReusedLine, string.Join(" ", review.ReusedKeys));
        Show(ChangesLine, comparison.Changes.Count == 0
            ? ""
            : "What changed:" + string.Concat(comparison.Changes.Select(c => Environment.NewLine + "• " + c)));

        _inputs =
        [
            .. recipe.Inputs.Select(i => new InputItem
            {
                Id = i.Id,
                Label = i.Label,
                Value = _existing.InputValues.GetValueOrDefault(i.Id) ?? "",
            }),
        ];
        InputsList.ItemsSource = _inputs;

        KeptNote.Text = ImportText.KeptNote(recipe);
        KeptList.ItemsSource = ImportText.Kept(recipe).Select(label => "• " + label).ToList();

        if (recipe.IsGroupList)
        {
            StatsSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatsTable.Load(recipe, _existing, installed, accountIds, ruleSentence,
                lookUpCounters is null ? null : _ => lookUpCounters(Inputs),
                ImportText.ShowEveryStat, review.Refusals);
            StatsTable.Changed += (_, _) => Refresh();
        }

        ImportButton.Content = settingsOnly ? "Save" : comparison.IsUpdate ? "Update" : "Import";
        Refresh();
    }

    public IReadOnlyDictionary<string, string> Inputs =>
        _inputs.ToDictionary(i => i.Id, i => i.Value.Trim(), StringComparer.Ordinal);

    /// <summary>The choices to save. Set when Import or Save is accepted.</summary>
    public IReadOnlyDictionary<string, StatChoice> Stats { get; private set; } = new Dictionary<string, StatChoice>();

    /// <summary>The counter names to keep in the recipe's state, read here or saved before.</summary>
    public IReadOnlyList<string> CounterNames => _recipe.IsGroupList ? _existing.SavedCounterNames : StatsTable.CounterNames;

    private void Refresh()
    {
        // With the table hidden, the review's own refusals need a line of their own.
        Show(ImportRefusalLine, _recipe.IsGroupList ? string.Join(Environment.NewLine, _review.Refusals) : "");
        ImportButton.IsEnabled = _review.CanImport && (_recipe.IsGroupList || StatsTable.AnyTicked);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        // Every declared input must be filled before the recipe runs (spec §3.3).
        var missing = _inputs.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Value));
        if (missing is not null)
        {
            Show(ImportRefusalLine, $"Set {missing.Label} first.");
            return;
        }

        if (_recipe.IsGroupList)
        {
            Stats = _existing.StatChoices;
            DialogResult = true;
            return;
        }

        var problems = StatsTable.SaveProblems();
        if (problems.Count > 0)
        {
            StatsTable.ShowProblems([.. _review.Refusals, .. problems]);
            return;
        }

        Stats = StatsTable.Choices;
        DialogResult = true;
    }

    /// <summary>An empty line takes no space, so the screen has no gaps where nothing applies.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 8: Keep `MainWindow` compiling**

In `src/UI/MainWindow.xaml.cs`, replace every `ImportWindow.CounterLookup` with `CounterLookup` (four occurrences: the return type of `CounterLookupFor`, the return type of `LookUpCounterNamesAsync`, and the two `new ImportWindow.CounterLookup(` calls). Nothing else in `MainWindow` changes; it is deleted in Task 14.

- [ ] **Step 9: Build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: `Build succeeded.` with 0 warnings. `ThemeFenceTests` scans `src/UI/Controls/*.xaml` too, and the only literal in it is `Background="Transparent"`.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: every test passes, including `ThemeFenceTests` and `NoHostnameFenceTests`.

- [ ] **Step 10: Commit**

```text
git add src/UI/Controls/StatsTableModel.cs src/UI/Controls/StatsTable.xaml src/UI/Controls/StatsTable.xaml.cs src/UI/ImportText.cs src/UI/ImportWindow.xaml src/UI/ImportWindow.xaml.cs src/UI/MainWindow.xaml.cs tests/StatsTableModelTests.cs tests/ImportTextTests.cs
git commit -m "ui: one searchable stats table replaces look up and add

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 11: The Setup window, the clan search, and Setup › Clans

Spec §7, §7.1, §3.4, §14. Setup is a separate window with a list of pages on the left:
- Clans, one page per recipe with inputs, titled by `RecipeInput.PluralLabel`;
- Your accounts, Stats, Recipes, Alerts, Score book, Diagnostics.

This task builds the shell, the `ClanSearchBox` control and the Clans page. The other pages come in Task 12.

The Clans page works like this:
- **Your main clan** is a search over the input's `search` list: substring, ignoring case, top 8, and Enter or a click picks. Ur Score then reads that source once and says "Found X in Y", or "None of your accounts are in Y yet. You can still watch it." with **Watch it instead**.
- **Clans your accounts are in** lists `mine` sources (the main first), each with **Make main** and **Remove**, plus **Add a clan your accounts are in**.
- **Clans you're watching** lists `watch` sources, each with **Remove**, plus **Watch a clan**.
- **Top of the battle** is a switch for the group-list source.
- A line gives the request volume per host, and adding a sixth source for one recipe asks first.
- Changes apply at once.

Pages talk to the app through `ISetupServices`, which `AppServices` implements in Task 14. The interface is defined in full here, so every Setup page compiles against one seam before the composition root exists. Nothing opens the Setup window until Task 14.

**Files:**
- Create: `src/Recipes/SearchLists.cs`, `tests/SearchListsTests.cs`
- Create: `src/Board/RecipeWords.cs`, `tests/RecipeWordsTests.cs`
- Create: `src/UI/Setup/ClansModel.cs`, `tests/ClansModelTests.cs`
- Create: `src/UI/Setup/SetupPages.cs`, `tests/SetupPagesTests.cs`
- Create: `src/Composition/ISetupServices.cs`
- Create: `src/UI/Controls/ClanSearchBox.xaml`, `src/UI/Controls/ClanSearchBox.xaml.cs`
- Create: `src/UI/SetupWindow.xaml`, `src/UI/SetupWindow.xaml.cs`
- Create: `src/UI/Setup/ClansPage.xaml`, `src/UI/Setup/ClansPage.xaml.cs`
- Modify: `src/App.xaml` (two shared resources)

**Interfaces:**
- Consumes:
  - Task 1: `RecipeInput.PluralLabel`, `Recipe.Period`, `Recipe.IsGroupList`
  - Task 3: `Source`, `Source.KeyOf`, `SourceRole`, `SourceRules.Add`/`MakeMain`/`Remove`
  - Task 4: `SharedAccounts`, `AccountsCache`, `AccountList`
  - Task 5: `IScoreBook`
  - Task 6: `RecipeSnapshot.Rows` (existing), `RecipeSnapshot.SourceId`
  - Task 9: `ScoreBookReader`
  - Task 10: `CounterLookup`
  - existing: `IRecipeTransport`, `FetchResult`, `RecipePath`, `RecipeHosts`, `RecipeStore`, `IKeyStore`, `Redactor`, `Settings`, `HostAccount`
- Produces:
  - `Labs626.UrScore.Recipes`:
    - `sealed record SearchListResult(IReadOnlyList<string> Names, string? Problem)`
    - `sealed class SearchLists(IRecipeTransport transport)`, with members:
      - `const int MatchLimit = 8`
      - `Task<SearchListResult> GetAsync(RecipeSearch search, CancellationToken cancellationToken)`
      - `static IReadOnlyList<string> Match(IReadOnlyList<string> names, string? query, int limit = MatchLimit)`
  - `Labs626.UrScore.Board`: `static class RecipeWords`, with members:
    - `RecipeInput? MainInput(Recipe)`
    - `string Group(Recipe)`, `string Groups(Recipe)`, `string GroupsLower(Recipe)`
    - `string Period(Recipe)`, `string Periods(Recipe)`
    - `string Capital(string)`, `string Lower(string)`, `string Spaced(string take)`
  - `Labs626.UrScore.UI`:
    - `sealed record ClanRow(string SourceId, string Name, SourceRole Role, string Who)` (with `Chip`, `CanMakeMain`, `MakeMainName`, `RemoveName`)
    - `sealed record ClanLists(ClanRow? Main, IReadOnlyList<ClanRow> Mine, IReadOnlyList<ClanRow> Watching)`
    - `sealed record ClanProbe(string Text, bool OfferWatch)`
    - `sealed record SourceChange(IReadOnlyList<Source> Sources, string SourceId, string? Note)`
    - `sealed record HostRequests(string Host, int PerHour)`
    - `static class ClansModel`, with members:
      - `const int ConfirmAbove = 5`
      - `string NameOf(Recipe, Source)`
      - `IReadOnlyList<string> FoundAccounts(RecipeSnapshot?, IReadOnlyList<HostAccount>)`
      - `string Who(Recipe, Source, RecipeSnapshot?, IReadOnlyList<HostAccount>)`
      - `ClanLists Lists(Recipe, IReadOnlyList<Source>, IReadOnlyDictionary<string, RecipeSnapshot>, IReadOnlyList<HostAccount>)`
      - `ClanProbe Probe(string name, RecipeSnapshot?, IReadOnlyList<HostAccount>)`
      - `SourceChange Pick(IReadOnlyList<Source>, Recipe, string value, SourceRole role)`
      - `IReadOnlyList<Source> WatchInstead(IReadOnlyList<Source>, string sourceId)`
      - `IReadOnlyList<Source> SetEnabled(IReadOnlyList<Source>, string sourceId, bool enabled)`
      - `Source? GroupListSource(IReadOnlyList<Source>, IReadOnlyList<InstalledRecipe>)`
      - `bool NeedsConfirmation(IReadOnlyList<Source>, string recipeSlug)`
      - `IReadOnlyList<HostRequests> RequestsPerHour(IReadOnlyList<Source>, IReadOnlyList<InstalledRecipe>, int accountCount)`
      - `string RequestsLine(IReadOnlyList<HostRequests>)`
      - `string ConfirmText(Recipe, IReadOnlyList<HostRequests>)`
    - `sealed record SetupPage(string Id, string Title, string? RecipeSlug = null)`
    - `interface ISetupPage { void Refresh(); }`
    - `static class SetupPages`, with members:
      - constants `Accounts`, `Stats`, `Recipes`, `Alerts`, `ScoreBook`, `Diagnostics`
      - `string ClansId(string slug)`
      - `IReadOnlyList<SetupPage> For(IReadOnlyList<InstalledRecipe>)`
      - `bool HasClansPage(InstalledRecipe)`
      - `string? FirstRunPage(IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>)`
      - `string StartPage(IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>)`
    - `partial class ClanSearchBox : UserControl`, with members:
      - `event Action<string>? Picked`
      - `bool AllowTyped`
      - `void SetLabel(string)`, `void SetNames(IReadOnlyList<string>)`, `void SetStatus(string)`, `void FocusSearch()`
    - `partial class SetupWindow : Window`, with members:
      - `SetupWindow(ISetupServices services, string? startPage = null)`
      - `void ShowPage(string pageId)`
    - `partial class ClansPage : UserControl, ISetupPage`, with `ClansPage(ISetupServices services, string recipeSlug)`
  - `Labs626.UrScore.Composition`: `interface ISetupServices` (full listing in Step 9)

- [ ] **Step 1: Write the failing tests**

Create `tests/SearchListsTests.cs`

```csharp
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class SearchListsTests
{
    private static readonly RecipeSearch Search = new("https://example.test/api/list", "data");

    private sealed class CountingTransport(Func<FetchResult> answer) : IRecipeTransport
    {
        public int Calls;

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(answer());
        }
    }

    [Fact]
    public async Task TheListIsReadOnceAndKeptForTheSession()
    {
        var transport = new CountingTransport(() => new FetchResult(200, """{"data":["CCGP","K0i2","CCGP",7,"",null]}""", null));
        var lists = new SearchLists(transport);

        var first = await lists.GetAsync(Search, CancellationToken.None);
        var second = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Null(first.Problem);
        Assert.Equal(new[] { "CCGP", "K0i2" }, first.Names.ToArray());
        Assert.Same(first, second);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task AFailedReadIsNotKeptSoTheNextAskTriesAgain()
    {
        var reachable = false;
        var transport = new CountingTransport(() => reachable
            ? new FetchResult(200, """{"data":["CCGP"]}""", null)
            : new FetchResult(null, null, "Could not reach example.test: timed out"));
        var lists = new SearchLists(transport);

        var failed = await lists.GetAsync(Search, CancellationToken.None);
        reachable = true;
        var retried = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Equal("Could not reach example.test: timed out", failed.Problem);
        Assert.Empty(failed.Names);
        Assert.Equal(new[] { "CCGP" }, retried.Names.ToArray());
        Assert.Equal(2, transport.Calls);
    }

    [Theory]
    [InlineData(500, """{"data":["CCGP"]}""", "example.test answered 500.")]
    [InlineData(200, """{"data":{"a":1}}""", "example.test did not return a list at 'data'.")]
    [InlineData(200, "not json", "example.test did not return valid JSON.")]
    [InlineData(200, """{"data":[]}""", "example.test returned an empty list.")]
    public async Task ABadAnswerSaysWhatWasWrong(int status, string body, string problem)
    {
        var lists = new SearchLists(new CountingTransport(() => new FetchResult(status, body, null)));

        var result = await lists.GetAsync(Search, CancellationToken.None);

        Assert.Equal(problem, result.Problem);
        Assert.Empty(result.Names);
    }

    [Fact]
    public void MatchesPutExactThenStartsWithThenAnywhereAndStopAtEight()
    {
        string[] names = ["Accursed", "cc", "CCKings", "CCGP", .. Enumerable.Range(1, 10).Select(i => $"Xcc{i}")];

        var matches = SearchLists.Match(names, " cC ");

        Assert.Equal(new[] { "cc", "CCKings", "CCGP", "Accursed", "Xcc1", "Xcc2", "Xcc3", "Xcc4" }, matches.ToArray());
    }

    [Fact]
    public void ABlankQueryMatchesNothing() => Assert.Empty(SearchLists.Match(["CCGP"], "   "));
}
```

Create `tests/RecipeWordsTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RecipeWordsTests
{
    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    [Fact]
    public void AClanRecipeSpeaksOfClansAndBattles()
    {
        Assert.Equal("clan", RecipeWords.Group(Clan));
        Assert.Equal("Clans", RecipeWords.Groups(Clan));
        Assert.Equal("clans", RecipeWords.GroupsLower(Clan));
        Assert.Equal("battle", RecipeWords.Period(Clan));
        Assert.Equal("battles", RecipeWords.Periods(Clan));
        Assert.Equal("clan", RecipeWords.MainInput(Clan)!.Id);
    }

    [Fact]
    public void ARecipeWithNoInputAndNoPeriodUsesPlainWords()
    {
        Assert.Null(RecipeWords.MainInput(Profile));
        Assert.Equal("source", RecipeWords.Group(Profile));
        Assert.Equal("Sources", RecipeWords.Groups(Profile));
        Assert.Equal("period", RecipeWords.Period(Profile));
    }

    [Theory]
    [InlineData("battle", "battle")]
    [InlineData("seasonName", "season name")]
    [InlineData("battle_id", "battle id")]
    [InlineData("  ", "period")]
    public void ATakeNameReadsAsWords(string take, string words) => Assert.Equal(words, RecipeWords.Spaced(take));

    [Theory]
    [InlineData("Clan", "clan")]
    [InlineData("NFT", "NFT")]
    [InlineData("", "")]
    public void LowerLeavesAcronymsAlone(string text, string lower) => Assert.Equal(lower, RecipeWords.Lower(text));

    [Fact]
    public void CapitalRaisesTheFirstLetterOnly() => Assert.Equal("Battle race", RecipeWords.Capital("battle race"));
}
```

Create `tests/ClansModelTests.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ClansModelTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");
    private static readonly HostAccount Waiting = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, "New Alt");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Source ClanSource(string id, string clan, SourceRole role, bool enabled = true) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, role, enabled);

    private static RecipeSnapshot Read(params long[] userIds) =>
        new(WatchState.NoMatches, null, [], [], userIds.Length, "battle=A", [.. userIds.Select(id => RecipeEngineTests.Row(id, 10))], []);

    [Fact]
    public void ListsPutTheMainFirstAndKeepWatchedClansApart()
    {
        Source[] sources =
        [
            ClanSource("s-00000002", "K0i2", SourceRole.Mine),
            ClanSource("s-00000003", "NovaForge", SourceRole.Watch),
            ClanSource("s-00000001", "CCGP", SourceRole.Main),
        ];
        var latest = new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Read(101, 999), ["s-00000002"] = Read(888) };

        var lists = ClansModel.Lists(Clan, sources, latest, [Main, Alt]);

        Assert.Equal("CCGP", lists.Main!.Name);
        Assert.Equal(new[] { "CCGP", "K0i2" }, lists.Mine.Select(r => r.Name).ToArray());
        Assert.Equal("estehernandez", lists.Mine[0].Who);
        Assert.Equal("None of your accounts found in the last read", lists.Mine[1].Who);
        Assert.Equal("★ main", lists.Mine[0].Chip);
        Assert.False(lists.Mine[0].CanMakeMain);
        Assert.True(lists.Mine[1].CanMakeMain);
        Assert.Equal("Make K0i2 main", lists.Mine[1].MakeMainName);

        var watched = Assert.Single(lists.Watching);
        Assert.Equal("clan-level numbers only", watched.Who);
        Assert.Equal("watching", watched.Chip);
        Assert.Equal("Remove NovaForge", watched.RemoveName);
    }

    [Fact]
    public void AClanNotReadYetSaysSo() =>
        Assert.Equal("Not read yet", ClansModel.Who(Clan, ClanSource("s-00000002", "K0i2", SourceRole.Mine), null, [Main]));

    [Fact]
    public void TheProbeNamesEveryAccountItFound()
    {
        var probe = ClansModel.Probe("CCGP", Read(101, 201, 5), [Main, Alt, Waiting]);

        Assert.Equal(new ClanProbe("Found estehernandez and CElCPapa in CCGP.", false), probe);
    }

    [Fact]
    public void AClanWithNoneOfYourAccountsOffersWatchingInstead() =>
        Assert.Equal(
            new ClanProbe("None of your accounts are in NovaForge yet. You can still watch it.", true),
            ClansModel.Probe("NovaForge", Read(5, 6), [Main, Alt]));

    [Fact]
    public void BeforeRoRoRoListsAccountsTheProbeCannotSay()
    {
        var expected = new ClanProbe("Read CCGP. RoRoRo hasn't listed your accounts yet, so Ur Score can't say which of them are in it.", false);

        Assert.Equal(expected, ClansModel.Probe("CCGP", Read(101), []));
        Assert.Equal(expected, ClansModel.Probe("CCGP", Read(101), [Waiting]));
    }

    [Fact]
    public void AStoppedReadOrNoReadSaysWhatHappened()
    {
        var idle = new RecipeSnapshot(WatchState.SourceIdle, "Your clan hasn't joined this battle.", [], [], 0);

        Assert.Equal(new ClanProbe("Added CCGP, but it couldn't be read just now. Your clan hasn't joined this battle.", false),
            ClansModel.Probe("CCGP", idle, [Main]));
        Assert.Equal(new ClanProbe("Added CCGP. It's read when you press Start.", false), ClansModel.Probe("CCGP", null, [Main]));
    }

    [Fact]
    public void PickingAMainAddsItAsTheOnlyMain()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main)];

        var change = ClansModel.Pick(sources, Clan, " K0i2 ", SourceRole.Main);

        Assert.Null(change.Note);
        var main = Assert.Single(change.Sources, s => s.Role == SourceRole.Main);
        Assert.Equal(change.SourceId, main.Id);
        Assert.Equal("K0i2", main.Inputs["clan"]);
        Assert.Equal(SourceRole.Mine, change.Sources.Single(s => s.Id == "s-00000001").Role);
    }

    [Fact]
    public void PickingTheSameNameInAnotherCaseReusesTheSource()
    {
        Source[] sources = [ClanSource("s-00000002", "K0i2", SourceRole.Mine)];

        var change = ClansModel.Pick(sources, Clan, "k0i2", SourceRole.Mine);

        Assert.Equal("s-00000002", change.SourceId);
        Assert.Equal("K0i2 is already on this list.", change.Note);
        Assert.Single(change.Sources);
    }

    [Fact]
    public void WatchingAClanYourAccountsAreInExplainsInstead()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main)];

        var change = ClansModel.Pick(sources, Clan, "ccgp", SourceRole.Watch);

        Assert.Equal("CCGP is already one of the clans your accounts are in. Remove it there first if you only want to watch it.", change.Note);
        Assert.Same(sources, change.Sources);
    }

    [Fact]
    public void PickingAWatchedClanAsYoursMovesItAcross()
    {
        Source[] sources = [ClanSource("s-00000003", "NovaForge", SourceRole.Watch)];

        var change = ClansModel.Pick(sources, Clan, "NovaForge", SourceRole.Mine);

        Assert.Null(change.Note);
        Assert.Equal(SourceRole.Mine, Assert.Single(change.Sources).Role);
    }

    [Fact]
    public void WatchInsteadAndTheSwitchChangeOnlyTheirSource()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", SourceRole.Main), ClanSource("s-00000002", "K0i2", SourceRole.Mine)];

        var watched = ClansModel.WatchInstead(sources, "s-00000002");
        var off = ClansModel.SetEnabled(sources, "s-00000001", false);

        Assert.Equal(new[] { SourceRole.Main, SourceRole.Watch }, watched.Select(s => s.Role).ToArray());
        Assert.Equal(new[] { false, true }, off.Select(s => s.Enabled).ToArray());
    }

    [Fact]
    public void RequestsPerHourCountEveryStepOfEveryEnabledSource()
    {
        var installed = new InstalledRecipe(Clan, "", new RecipeState());
        Source[] sources =
        [
            ClanSource("s-00000001", "CCGP", SourceRole.Main),
            ClanSource("s-00000002", "K0i2", SourceRole.Mine),
            ClanSource("s-00000003", "Resting", SourceRole.Watch, enabled: false),
        ];

        var requests = ClansModel.RequestsPerHour(sources, [installed], accountCount: 10);

        var expected = (int)Math.Round(2 * Clan.Steps.Count * 3600.0 / Clan.EffectiveEverySeconds);
        Assert.Equal(new HostRequests("ps99.biggamesapi.io", expected), Assert.Single(requests));
        Assert.Equal($"Your PC asks ps99.biggamesapi.io about {expected} times an hour.", ClansModel.RequestsLine(requests));
    }

    [Fact]
    public void TheSixthEnabledSourceOfARecipeAsksFirst()
    {
        var five = Enumerable.Range(1, 5).Select(i => ClanSource($"s-0000000{i}", $"Clan{i}", SourceRole.Watch)).ToList();
        var fourAndOneOff = five.Select((s, i) => i == 0 ? s with { Enabled = false } : s).ToList();

        Assert.True(ClansModel.NeedsConfirmation(five, Clan.Slug));
        Assert.False(ClansModel.NeedsConfirmation(fourAndOneOff, Clan.Slug));
        Assert.StartsWith("That makes more than 5 clans for Pet Sim 99 clan battle points.", ClansModel.ConfirmText(Clan, []));
    }

    [Fact]
    public void TheSwitchFindsTheGroupListSource()
    {
        var top = RecipeParser.Parse(GroupList).Recipe!;
        var installed = new[] { new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(top, "", new RecipeState()) };
        var topSource = new Source("s-0000000a", top.Slug, new Dictionary<string, string>(), SourceRole.Watch);

        Assert.Same(topSource, ClansModel.GroupListSource([ClanSource("s-00000001", "CCGP", SourceRole.Main), topSource], installed));
        Assert.Null(ClansModel.GroupListSource([ClanSource("s-00000001", "CCGP", SourceRole.Main)], installed));
    }
}
```

Create `tests/SetupPagesTests.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class SetupPagesTests
{
    private static InstalledRecipe Clan => new(RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!, "", new RecipeState());

    private static InstalledRecipe Profile => new(RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!, "", new RecipeState());

    [Fact]
    public void ARecipeWithInputsGetsAClansPageBeforeTheFixedPages()
    {
        var clan = Clan;

        var pages = SetupPages.For([clan, Profile]);

        Assert.Equal(new[] { "Clans", "Your accounts", "Stats", "Recipes", "Alerts", "Score book", "Diagnostics" }, pages.Select(p => p.Title).ToArray());
        Assert.Equal(SetupPages.ClansId(clan.Recipe.Slug), pages[0].Id);
        Assert.Equal(clan.Recipe.Slug, pages[0].RecipeSlug);
        Assert.Equal("Clans", pages[0].ToString());
    }

    [Fact]
    public void FirstRunOpensTheClansPageOfARecipeWithInputsAndNoSources()
    {
        var clan = Clan;
        var source = new Source("s-00000001", clan.Recipe.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

        Assert.Equal(SetupPages.ClansId(clan.Recipe.Slug), SetupPages.FirstRunPage([clan, Profile], []));
        Assert.Null(SetupPages.FirstRunPage([clan, Profile], [source]));
        Assert.Null(SetupPages.FirstRunPage([Profile], []));
    }

    [Fact]
    public void WithNoRecipesSetupStartsOnRecipes()
    {
        Assert.Equal(SetupPages.Recipes, SetupPages.StartPage([], []));
        Assert.Equal(SetupPages.Accounts, SetupPages.StartPage([Profile], []));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: the build FAILS with `error CS0246: The type or namespace name 'SearchLists' could not be found`, `error CS0103: The name 'RecipeWords' does not exist in the current context`, `error CS0103: The name 'ClansModel' does not exist in the current context` and `error CS0103: The name 'SetupPages' does not exist in the current context`.

- [ ] **Step 3: Create `src/Recipes/SearchLists.cs`**

Create `src/Recipes/SearchLists.cs`

```csharp
using System.Text.Json;

namespace Labs626.UrScore.Recipes;

/// <summary>An input's search list as read, or why it couldn't be.</summary>
public sealed record SearchListResult(IReadOnlyList<string> Names, string? Problem);

/// <summary>
/// The one path that reads an input's <c>search</c> list (spec §7.1). Goes through the shared recipe
/// transport, so the request is spaced per host and uses the no-redirect, no-cookie handler. A list read
/// successfully is kept in memory for the session and never written anywhere. A failure is not kept, so
/// the next ask tries again. The list's address is fixed (the parser refuses placeholders), so nothing
/// about you is sent.
/// </summary>
public sealed class SearchLists(IRecipeTransport transport)
{
    public const int MatchLimit = 8;

    private readonly Dictionary<string, Task<SearchListResult>> _cache = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Task<SearchListResult> GetAsync(RecipeSearch search, CancellationToken cancellationToken)
    {
        var key = search.Url + "\u001f" + search.List;
        Task<SearchListResult> task;

        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out var cached) || Failed(cached))
            {
                // Not tied to the caller's token: a page closing mid-read must not spoil the list for the next one.
                cached = FetchAsync(search);
                _cache[key] = cached;
            }

            task = cached;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>An exact match first, then names starting with the query, then names containing it, each in list order.</summary>
    public static IReadOnlyList<string> Match(IReadOnlyList<string> names, string? query, int limit = MatchLimit)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0 || limit <= 0) return [];

        var exact = new List<string>();
        var starts = new List<string>();
        var inside = new List<string>();

        foreach (var name in names)
        {
            if (name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(name);
            }
            else if (name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                if (starts.Count < limit) starts.Add(name);
            }
            else if (inside.Count < limit && name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                inside.Add(name);
            }
        }

        return [.. exact.Concat(starts).Concat(inside).Take(limit)];
    }

    private static bool Failed(Task<SearchListResult> task) =>
        task.IsCompleted && (!task.IsCompletedSuccessfully || task.Result.Problem is not null);

    private async Task<SearchListResult> FetchAsync(RecipeSearch search)
    {
        var host = RecipeHosts.HostOf(search.Url);
        if (!Uri.TryCreate(search.Url, UriKind.Absolute, out var address))
        {
            return new SearchListResult([], $"The list address for {host} is not valid.");
        }

        try
        {
            var fetched = await transport.GetAsync(address, new Dictionary<string, string>(), "search-list", CancellationToken.None)
                .ConfigureAwait(false);

            if (!fetched.Answered) return new SearchListResult([], fetched.Error ?? $"Could not reach {host}.");
            if (!fetched.Succeeded) return new SearchListResult([], $"{host} answered {fetched.Status}.");

            using var document = JsonDocument.Parse(fetched.Body ?? "");
            var found = RecipePath.Resolve(document.RootElement, search.List);
            if (found.Outcome != PathOutcome.Found || found.Value.ValueKind != JsonValueKind.Array)
            {
                return new SearchListResult([], $"{host} did not return a list at '{search.List}'.");
            }

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in found.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name && seen.Add(name)) names.Add(name);
            }

            return names.Count == 0
                ? new SearchListResult([], $"{host} returned an empty list.")
                : new SearchListResult(names, null);
        }
        catch (JsonException)
        {
            return new SearchListResult([], $"{host} did not return valid JSON.");
        }
        catch (Exception ex)
        {
            return new SearchListResult([], $"Could not read the list from {host} ({ex.GetType().Name}).");
        }
    }
}
```

- [ ] **Step 4: Create `src/Board/RecipeWords.cs`**

Create `src/Board/RecipeWords.cs`

```csharp
using System.Text;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>
/// The recipe's own words for what Ur Score shows (spec §3.4), so Ur Score's text never names a game:
/// "clan" and "Clans" come from the input's label and plural, "battle" from the take name `period.value` uses.
/// </summary>
public static class RecipeWords
{
    /// <summary>The input a source is named by: the first with a search list, else the first.</summary>
    public static RecipeInput? MainInput(Recipe recipe) =>
        recipe.Inputs.FirstOrDefault(i => i.Search is not null) ?? recipe.Inputs.FirstOrDefault();

    /// <summary>"Your clan" becomes "clan". "source" when the recipe has no input.</summary>
    public static string Group(Recipe recipe)
    {
        var label = MainInput(recipe)?.Label.Trim() ?? "";
        if (label.StartsWith("Your ", StringComparison.OrdinalIgnoreCase)) label = label[5..].Trim();
        return label.Length == 0 ? "source" : Lower(label);
    }

    /// <summary>The input's plural, "Clans". "Sources" when the recipe has no input.</summary>
    public static string Groups(Recipe recipe) =>
        MainInput(recipe)?.PluralLabel is { Length: > 0 } plural ? Capital(plural) : "Sources";

    public static string GroupsLower(Recipe recipe) => Lower(Groups(recipe));

    /// <summary>"battle" from <c>period.value</c>; "period" when the recipe has none.</summary>
    public static string Period(Recipe recipe) => recipe.Period is { } period ? Spaced(period.Value) : "period";

    public static string Periods(Recipe recipe) => Period(recipe) + "s";

    public static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Lowers the first letter, unless the second is a capital too (an acronym such as "NFT").</summary>
    public static string Lower(string text) =>
        text.Length == 0 || (text.Length > 1 && char.IsUpper(text[1])) ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>A take name as words: "seasonName" reads "season name", "battle_id" reads "battle id".</summary>
    public static string Spaced(string take)
    {
        var builder = new StringBuilder();
        foreach (var c in take.Trim())
        {
            if (c is '_' or '-' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
                continue;
            }

            if (char.IsUpper(c) && builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
            builder.Append(char.ToLowerInvariant(c));
        }

        var words = builder.ToString().Trim();
        return words.Length == 0 ? "period" : words;
    }
}
```

- [ ] **Step 5: Create `src/UI/Setup/ClansModel.cs` and `src/UI/Setup/SetupPages.cs`**

Create `src/UI/Setup/ClansModel.cs`

```csharp
using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One row of a Clans list: the source, its name, and who was found in it.</summary>
public sealed record ClanRow(string SourceId, string Name, SourceRole Role, string Who)
{
    public string Chip => Role switch { SourceRole.Main => "★ main", SourceRole.Mine => "yours", _ => "watching" };

    public bool CanMakeMain => Role == SourceRole.Mine;

    public string MakeMainName => $"Make {Name} main";

    public string RemoveName => $"Remove {Name}";
}

public sealed record ClanLists(ClanRow? Main, IReadOnlyList<ClanRow> Mine, IReadOnlyList<ClanRow> Watching);

/// <summary>What the one read after a pick found, and whether to offer <b>Watch it instead</b>.</summary>
public sealed record ClanProbe(string Text, bool OfferWatch);

/// <summary>The sources after a pick, the picked source's id, and a note when nothing needed adding.</summary>
public sealed record SourceChange(IReadOnlyList<Source> Sources, string SourceId, string? Note);

public sealed record HostRequests(string Host, int PerHour);

/// <summary>
/// Setup › Clans' decisions (spec §7.1, §14), kept out of the page so they are testable. Changes are
/// returned as new source lists; the page saves them through <c>ISetupServices.SaveSources</c>.
/// </summary>
public static class ClansModel
{
    /// <summary>Adding a source past this many for one recipe asks first (spec §14).</summary>
    public const int ConfirmAbove = 5;

    public static string NameOf(Recipe recipe, Source source) =>
        RecipeWords.MainInput(recipe) is { } input && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0
            ? value.Trim()
            : source.Inputs.Count > 0 ? string.Join(" · ", source.Inputs.Values) : recipe.Name;

    /// <summary>Your accounts' names among a read's rows. Never another row's id or name.</summary>
    public static IReadOnlyList<string> FoundAccounts(RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot?.Rows is not { } rows) return [];

        var ids = rows.Select(r => r.UserId).ToHashSet();
        return [.. accounts
            .Where(a => a.RobloxUserId != 0 && ids.Contains(a.RobloxUserId))
            .Select(a => a.DisplayName)
            .Distinct(StringComparer.Ordinal)];
    }

    public static string Who(Recipe recipe, Source source, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (source.Role == SourceRole.Watch) return $"{RecipeWords.Group(recipe)}-level numbers only";
        if (snapshot?.Rows is null) return "Not read yet";

        var found = FoundAccounts(snapshot, accounts);
        return found.Count == 0 ? "None of your accounts found in the last read" : string.Join(", ", found);
    }

    /// <summary>The main source, the sources your accounts are in (main first), and the watched ones.</summary>
    public static ClanLists Lists(
        Recipe recipe, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest, IReadOnlyList<HostAccount> accounts)
    {
        var own = sources.Where(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal)).ToList();
        ClanRow Row(Source s) => new(s.Id, NameOf(recipe, s), s.Role, Who(recipe, s, latest.GetValueOrDefault(s.Id), accounts));

        var main = own.FirstOrDefault(s => s.Role == SourceRole.Main);
        return new ClanLists(
            main is null ? null : Row(main),
            [.. own.Where(s => s.Role == SourceRole.Main).Concat(own.Where(s => s.Role == SourceRole.Mine)).Select(Row)],
            [.. own.Where(s => s.Role == SourceRole.Watch).Select(Row)]);
    }

    /// <summary>The sentence after the one read that follows a pick (spec §7.1).</summary>
    public static ClanProbe Probe(string name, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot is null) return new ClanProbe($"Added {name}. It's read when you press Start.", false);

        if (snapshot.Rows is null)
        {
            return new ClanProbe($"Added {name}, but it couldn't be read just now. {snapshot.Detail}".TrimEnd(), false);
        }

        if (accounts.All(a => a.RobloxUserId == 0))
        {
            return new ClanProbe($"Read {name}. RoRoRo hasn't listed your accounts yet, so Ur Score can't say which of them are in it.", false);
        }

        var found = FoundAccounts(snapshot, accounts);
        return found.Count > 0
            ? new ClanProbe($"Found {JoinWithAnd(found)} in {name}.", false)
            : new ClanProbe($"None of your accounts are in {name} yet. You can still watch it.", true);
    }

    /// <summary>
    /// Adds a picked name with a role, or reuses the source that already has it (names match ignoring case,
    /// through <see cref="Source.KeyOf"/>). A new main is added as mine and then made main, so there is only
    /// ever one main per recipe.
    /// </summary>
    public static SourceChange Pick(IReadOnlyList<Source> sources, Recipe recipe, string value, SourceRole role)
    {
        var name = value.Trim();
        var input = RecipeWords.MainInput(recipe) ?? throw new InvalidOperationException($"{recipe.Name} has no input to pick a value for.");
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal) { [input.Id] = name };
        var key = Source.KeyOf(inputs);

        bool Same(Source s) =>
            string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal) && string.Equals(s.InputsKey, key, StringComparison.Ordinal);

        if (sources.FirstOrDefault(Same) is { } existing)
        {
            var shown = NameOf(recipe, existing);
            return role switch
            {
                SourceRole.Main when existing.Role == SourceRole.Main =>
                    new SourceChange(sources, existing.Id, $"{shown} is already your main {RecipeWords.Group(recipe)}."),
                SourceRole.Main =>
                    new SourceChange(SourceRules.MakeMain(Replace(sources, existing with { Enabled = true }), existing.Id), existing.Id, null),
                SourceRole.Mine when existing.Role == SourceRole.Watch =>
                    new SourceChange(Replace(sources, existing with { Role = SourceRole.Mine, Enabled = true }), existing.Id, null),
                SourceRole.Watch when existing.Role != SourceRole.Watch =>
                    new SourceChange(sources, existing.Id,
                        $"{shown} is already one of the {RecipeWords.GroupsLower(recipe)} your accounts are in. Remove it there first if you only want to watch it."),
                _ => new SourceChange(sources, existing.Id, $"{shown} is already on this list."),
            };
        }

        var added = SourceRules.Add(sources, recipe.Slug, inputs, role == SourceRole.Main ? SourceRole.Mine : role);
        var id = added.First(Same).Id;
        return role == SourceRole.Main
            ? new SourceChange(SourceRules.MakeMain(added, id), id, null)
            : new SourceChange(added, id, null);
    }

    public static IReadOnlyList<Source> WatchInstead(IReadOnlyList<Source> sources, string sourceId) =>
        [.. sources.Select(s => s.Id == sourceId ? s with { Role = SourceRole.Watch } : s)];

    public static IReadOnlyList<Source> SetEnabled(IReadOnlyList<Source> sources, string sourceId, bool enabled) =>
        [.. sources.Select(s => s.Id == sourceId ? s with { Enabled = enabled } : s)];

    /// <summary>The source of an installed group-list recipe, which the Top switch turns on and off.</summary>
    public static Source? GroupListSource(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed) =>
        sources.FirstOrDefault(s => installed.Any(i => string.Equals(i.Recipe.Slug, s.Recipe, StringComparison.Ordinal) && i.Recipe.IsGroupList));

    public static bool NeedsConfirmation(IReadOnlyList<Source> sources, string recipeSlug) =>
        sources.Count(s => s.Enabled && string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal)) >= ConfirmAbove;

    /// <summary>Requests an hour per host: every step once a cycle, a per-account step once per account.</summary>
    public static IReadOnlyList<HostRequests> RequestsPerHour(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, int accountCount)
    {
        var perHost = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var source in sources.Where(s => s.Enabled))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;

            var cycles = 3600.0 / recipe.EffectiveEverySeconds;
            foreach (var step in recipe.Steps)
            {
                var host = RecipeHosts.HostOf(step.Url);
                perHost.TryGetValue(host, out var sofar);
                perHost[host] = sofar + cycles * (step.PerAccount ? accountCount : 1);
            }
        }

        return [.. perHost.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new HostRequests(kv.Key, (int)Math.Round(kv.Value)))];
    }

    public static string RequestsLine(IReadOnlyList<HostRequests> requests) =>
        string.Join(" ", requests.Select(r => $"Your PC asks {r.Host} about {r.PerHour.ToString("N0", CultureInfo.InvariantCulture)} times an hour."));

    public static string ConfirmText(Recipe recipe, IReadOnlyList<HostRequests> after) =>
        $"That makes more than {ConfirmAbove} {RecipeWords.GroupsLower(recipe)} for {recipe.Name}. {RequestsLine(after)} Add it anyway?"
            .Replace("  ", " ", StringComparison.Ordinal);

    private static IReadOnlyList<Source> Replace(IReadOnlyList<Source> sources, Source updated) =>
        [.. sources.Select(s => s.Id == updated.Id ? updated : s)];

    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
```

Create `src/UI/Setup/SetupPages.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One entry in Setup's list. Its text is its title, so a screen reader and UI Automation read the title.</summary>
public sealed record SetupPage(string Id, string Title, string? RecipeSlug = null)
{
    public override string ToString() => Title;
}

/// <summary>A Setup page re-renders from the services when anything changes.</summary>
public interface ISetupPage
{
    void Refresh();
}

/// <summary>Which pages Setup lists, and where it opens (spec §7, §7.1 first run).</summary>
public static class SetupPages
{
    public const string Accounts = "accounts";
    public const string Stats = "stats";
    public const string Recipes = "recipes";
    public const string Alerts = "alerts";
    public const string ScoreBook = "score-book";
    public const string Diagnostics = "diagnostics";

    private const string ClansPrefix = "clans:";

    public static string ClansId(string slug) => ClansPrefix + slug;

    public static bool HasClansPage(InstalledRecipe installed) => installed.Recipe.Inputs.Count > 0 && !installed.Recipe.IsGroupList;

    /// <summary>A Clans page per recipe with inputs, titled by its plural, then the fixed pages.</summary>
    public static IReadOnlyList<SetupPage> For(IReadOnlyList<InstalledRecipe> installed)
    {
        var withInputs = installed.Where(HasClansPage).ToList();
        var pages = new List<SetupPage>();

        foreach (var item in withInputs)
        {
            var title = RecipeWords.Groups(item.Recipe);
            if (withInputs.Count(other => RecipeWords.Groups(other.Recipe) == title) > 1) title = $"{title} · {item.Recipe.Name}";
            pages.Add(new SetupPage(ClansId(item.Recipe.Slug), title, item.Recipe.Slug));
        }

        pages.Add(new SetupPage(Accounts, "Your accounts"));
        pages.Add(new SetupPage(Stats, "Stats"));
        pages.Add(new SetupPage(Recipes, "Recipes"));
        pages.Add(new SetupPage(Alerts, "Alerts"));
        pages.Add(new SetupPage(ScoreBook, "Score book"));
        pages.Add(new SetupPage(Diagnostics, "Diagnostics"));
        return pages;
    }

    /// <summary>Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.</summary>
    public static string? FirstRunPage(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        installed.Where(HasClansPage)
            .FirstOrDefault(i => !sources.Any(s => string.Equals(s.Recipe, i.Recipe.Slug, StringComparison.Ordinal))) is { } bare
            ? ClansId(bare.Recipe.Slug)
            : null;

    public static string StartPage(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        installed.Count == 0 ? Recipes : FirstRunPage(installed, sources) ?? For(installed)[0].Id;
}
```

- [ ] **Step 6: Run the model tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~SearchListsTests|FullyQualifiedName~RecipeWordsTests|FullyQualifiedName~ClansModelTests|FullyQualifiedName~SetupPagesTests"`
Expected: PASS, 35 tests.

- [ ] **Step 7: Add the shared resources to `src/App.xaml`**

In `src/App.xaml`, directly after the `PrimaryButton` style, add:

```xml
            <!-- Bool to Visibility for bindings. Panels and Setup rows use it. -->
            <BooleanToVisibilityConverter x:Key="BoolToVisible" />

            <!-- A button that reads as a row: no fill or edge until hovered, content on the left. Search
                 matches and panel tools use it. -->
            <Style x:Key="FlatButton" TargetType="Button">
                <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}" />
                <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="Padding" Value="8,5" />
                <Setter Property="Cursor" Value="Hand" />
                <Setter Property="HorizontalAlignment" Value="Stretch" />
                <Setter Property="FontFamily" Value="{StaticResource BodyFont}" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Button">
                            <Grid>
                                <Border x:Name="Fill" Background="{TemplateBinding Background}" CornerRadius="3" />
                                <Border x:Name="Sheen" Background="{DynamicResource WhiteBrush}" CornerRadius="3"
                                        Opacity="0" IsHitTestVisible="False" />
                                <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Left" VerticalAlignment="Center" />
                            </Grid>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Sheen" Property="Opacity" Value="0.08" />
                                </Trigger>
                                <Trigger Property="IsPressed" Value="True">
                                    <Setter TargetName="Sheen" Property="Opacity" Value="0.16" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
```

- [ ] **Step 8: Create the clan search control**

Create `src/UI/Controls/ClanSearchBox.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.ClanSearchBox"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <TextBox x:Name="SearchText" TextChanged="OnTextChanged" PreviewKeyDown="OnSearchKeyDown" />
        <!-- Matches are buttons, so a click and UI Automation's Invoke both pick. Focus stays in the text box;
             Up and Down move the highlight and Enter picks it. -->
        <ListBox x:Name="SearchMatches" Visibility="Collapsed" Margin="0,4,0,0" MaxHeight="260" Focusable="False"
                 Background="{DynamicResource RowBgBrush}" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1"
                 Foreground="{DynamicResource WhiteBrush}"
                 AutomationProperties.Name="Names matching what you typed">
            <ListBox.ItemContainerStyle>
                <Style TargetType="ListBoxItem">
                    <Setter Property="Focusable" Value="False" />
                    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <Border x:Name="Item" Background="Transparent">
                                    <ContentPresenter />
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsSelected" Value="True">
                                        <Setter TargetName="Item" Property="Background" Value="{DynamicResource RowHoverBrush}" />
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ListBox.ItemContainerStyle>
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Button Content="{Binding}" Style="{StaticResource FlatButton}" Focusable="False" Click="OnMatchClick"
                            AutomationProperties.Name="{Binding StringFormat='Pick {0}'}" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <TextBlock x:Name="SearchStatus" Style="{StaticResource Muted}" Margin="0,4,0,0" />
    </StackPanel>
</UserControl>
```

Create `src/UI/Controls/ClanSearchBox.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>
/// Type-to-search over an input's search list (spec §7.1): matches anywhere in the name, ignoring case,
/// top 8. Enter or a click picks. The list is handed in; this control reads nothing itself.
/// </summary>
public partial class ClanSearchBox : UserControl
{
    private IReadOnlyList<string> _names = [];

    public ClanSearchBox() => InitializeComponent();

    public event Action<string>? Picked;

    /// <summary>When the list couldn't be read, or the input has none, Enter picks the typed text as it is.</summary>
    public bool AllowTyped { get; set; }

    public void SetLabel(string label) => AutomationProperties.SetName(SearchText, label);

    public void SetNames(IReadOnlyList<string> names)
    {
        _names = names;
        UpdateMatches();
    }

    public void SetStatus(string text)
    {
        SearchStatus.Text = text;
        SearchStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void FocusSearch() => SearchText.Focus();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateMatches();

    private void UpdateMatches()
    {
        var matches = SearchLists.Match(_names, SearchText.Text);
        SearchMatches.ItemsSource = matches;
        SearchMatches.SelectedIndex = matches.Count > 0 ? 0 : -1;
        SearchMatches.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var count = SearchMatches.Items.Count;

        switch (e.Key)
        {
            case Key.Down when count > 0:
                SearchMatches.SelectedIndex = Math.Min(count - 1, SearchMatches.SelectedIndex + 1);
                SearchMatches.ScrollIntoView(SearchMatches.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                SearchMatches.SelectedIndex = Math.Max(0, SearchMatches.SelectedIndex - 1);
                SearchMatches.ScrollIntoView(SearchMatches.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SearchMatches.SelectedItem is string chosen) Pick(chosen);
                else if (AllowTyped && SearchText.Text.Trim() is { Length: > 0 } typed) Pick(typed);
                e.Handled = true;
                break;
            case Key.Escape when SearchText.Text.Length > 0:
                SearchText.Text = "";
                e.Handled = true;
                break;
        }
    }

    private void OnMatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string name }) Pick(name);
    }

    private void Pick(string name)
    {
        SearchText.Text = "";
        Picked?.Invoke(name);
    }
}
```

- [ ] **Step 9: Create the services seam**

Create `src/Composition/ISetupServices.cs`

```csharp
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace Labs626.UrScore.Composition;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Everything a Setup page may use. <c>AppServices</c> (Task 14) implements it. Every member is used from
/// the UI thread, and <see cref="Changed"/> is raised on it.
/// </summary>
public interface ISetupServices
{
    /// <summary>Installed recipes as last loaded, in file order.</summary>
    IReadOnlyList<InstalledRecipe> Installed { get; }

    /// <summary>Recipe files that could not be read, already redacted.</summary>
    IReadOnlyList<string> RecipeProblems { get; }

    IReadOnlyList<Source> Sources { get; }

    RecipeStore Store { get; }

    IKeyStore Keys { get; }

    Redactor Redactor { get; }

    Settings Settings { get; }

    SharedAccounts Accounts { get; }

    AccountsCache AccountsCache { get; }

    /// <summary>RoRoRo's last list this session, else the saved one from <c>accounts.json</c>. Your accounts only.</summary>
    IReadOnlyList<HostAccount> KnownAccounts { get; }

    IScoreBook Book { get; }

    ScoreBookReader Reader { get; }

    bool ReaderLoaded { get; }

    bool Running { get; }

    /// <summary>The newest snapshot per source id, from a timed read, Test now, or a Setup read-once.</summary>
    IReadOnlyDictionary<string, RecipeSnapshot> Latest { get; }

    DateTimeOffset? LastReadAt(string sourceId);

    /// <summary>Reports sent and dropped this session, summed over a recipe's sources.</summary>
    (int Sent, int Dropped) PolicyCounts(string recipeSlug);

    /// <summary>The fetched icon file for a recipe, once a read has named one, else null.</summary>
    string? IconFileFor(string recipeSlug);

    /// <summary>The history-budget warning once RoRoRo's accounts are known, else null.</summary>
    string? BudgetWarning { get; }

    /// <summary>"host=1.28.0.0 reject=(none)", for diagnostics.</summary>
    string HostText { get; }

    string RawDirectory { get; }

    /// <summary>The newest trail lines, redacted, oldest first.</summary>
    IReadOnlyList<string> Trail { get; }

    event Action? Changed;

    /// <summary>Saves <c>sources.json</c>, applies it to the running watches at once, and raises <see cref="Changed"/>.</summary>
    void SaveSources(IReadOnlyList<Source> sources);

    /// <summary>Reloads recipes from disk, migrates sources for any new recipe, applies, and raises <see cref="Changed"/>.</summary>
    void ReloadRecipes();

    /// <summary>Saves one recipe's state (stats, Send per account, counter names) and updates its watches.</summary>
    void SaveRecipeState(Recipe recipe, RecipeState state);

    /// <summary>Removes a recipe file and its sources. Never touches its score book.</summary>
    void RemoveRecipe(string slug);

    /// <summary>Reads one source once, right now, and records it like any read. Null when that source has no watch yet.</summary>
    Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken);

    Task<SearchListResult> SearchListAsync(RecipeSearch search, CancellationToken cancellationToken);

    /// <summary>One read with every recipe value asked for, so the response can offer its counter names. Sends nothing.</summary>
    Task<CounterLookup> ReadCounterNamesAsync(Recipe recipe, CancellationToken cancellationToken);

    Task<AccountList> RefreshAccountsAsync(CancellationToken cancellationToken);

    void AddTrail(string text);
}
```

- [ ] **Step 10: Create the Setup window**

Create `src/UI/SetupWindow.xaml`

```xml
<Window x:Class="Labs626.UrScore.UI.SetupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Setup" Width="1000" Height="780" MinWidth="760" MinHeight="540"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <Window.Resources>
        <Style x:Key="NavItem" TargetType="ListBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
            <Setter Property="Padding" Value="10,7" />
            <Setter Property="Margin" Value="0,1" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRing}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Grid>
                            <Border x:Name="Item" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}">
                                <ContentPresenter />
                            </Border>
                            <Border x:Name="Bar" Width="2" HorizontalAlignment="Left" Margin="0,5"
                                    Background="{DynamicResource CyanBrush}" Visibility="Collapsed" />
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Item" Property="Background" Value="{DynamicResource RowHoverBrush}" />
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Item" Property="Background" Value="{DynamicResource RowBgBrush}" />
                                <Setter TargetName="Bar" Property="Visibility" Value="Visible" />
                                <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="210" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Border Grid.Column="0" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,0,1,0" Padding="8,12">
            <ListBox x:Name="SetupNav" DisplayMemberPath="Title" ItemContainerStyle="{StaticResource NavItem}"
                     Background="Transparent" BorderThickness="0" SelectionChanged="OnNavChanged"
                     AutomationProperties.Name="Setup pages" />
        </Border>
        <ScrollViewer Grid.Column="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <ContentControl x:Name="PageHost" Margin="24,18,24,24" Focusable="False" />
        </ScrollViewer>
    </Grid>
</Window>
```

Create `src/UI/SetupWindow.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// Everything that is setup (spec §7): a list of pages on the left, the page on the right. Pages re-render
/// whenever the services change, and the list follows installed recipes.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly ISetupServices _services;
    private string? _currentId;
    private bool _rebuilding;

    public SetupWindow(ISetupServices services, string? startPage = null)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += OnServicesChanged;
        Closed += (_, _) => _services.Changed -= OnServicesChanged;

        ShowPage(startPage ?? SetupPages.StartPage(services.Installed, services.Sources));
    }

    /// <summary>Shows a page by id, building it fresh.</summary>
    public void ShowPage(string pageId)
    {
        _currentId = null;
        RebuildNav(pageId);
    }

    private void RebuildNav(string? select)
    {
        var pages = SetupPages.For(_services.Installed);

        _rebuilding = true;
        try
        {
            SetupNav.ItemsSource = pages;
        }
        finally
        {
            _rebuilding = false;
        }

        SetupNav.SelectedItem = pages.FirstOrDefault(p => p.Id == select)
                                ?? pages.FirstOrDefault(p => p.Id == _currentId)
                                ?? pages[0];
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding || SetupNav.SelectedItem is not SetupPage page) return;

        if (page.Id == _currentId && PageHost.Content is ISetupPage current)
        {
            current.Refresh();
            return;
        }

        _currentId = page.Id;
        PageHost.Content = CreatePage(page);
    }

    private void OnServicesChanged()
    {
        var wanted = SetupPages.For(_services.Installed).Select(p => p.Id);
        var shown = (SetupNav.ItemsSource as IEnumerable<SetupPage>)?.Select(p => p.Id) ?? [];

        if (!wanted.SequenceEqual(shown))
        {
            RebuildNav(_currentId);
            return;
        }

        (PageHost.Content as ISetupPage)?.Refresh();
    }

    private FrameworkElement CreatePage(SetupPage page) => page.RecipeSlug is { } slug
        ? new ClansPage(_services, slug)
        : new TextBlock { Text = page.Title, Style = (Style)FindResource("Heading") };
}
```

The `TextBlock` stand-in for the other pages is replaced in Task 12. Nothing opens this window until Task 14.

- [ ] **Step 11: Create the Clans page**

Create `src/UI/Setup/ClansPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.ClansPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <UserControl.Resources>
        <DataTemplate x:Key="ClanRowTemplate">
            <Border Style="{StaticResource Card}" Padding="10,8" Margin="0,0,0,8">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Border Grid.Column="0" BorderBrush="{DynamicResource EdgeBrush}" BorderThickness="1" CornerRadius="9"
                            Padding="7,1" Margin="0,0,10,0" VerticalAlignment="Center">
                        <TextBlock Text="{Binding Chip}" FontFamily="{StaticResource MonoFont}" FontSize="10.5"
                                   Foreground="{DynamicResource MutedTextBrush}" />
                    </Border>
                    <TextBlock Grid.Column="1" TextWrapping="Wrap" VerticalAlignment="Center">
                        <Run Text="{Binding Name, Mode=OneWay}" FontWeight="SemiBold" />
                        <Run Text=" · " Foreground="{DynamicResource MutedTextBrush}" />
                        <Run Text="{Binding Who, Mode=OneWay}" Foreground="{DynamicResource MutedTextBrush}" />
                    </TextBlock>
                    <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="10,0,0,0">
                        <Button Content="Make main" Tag="{Binding SourceId}" Click="OnMakeMainClick" Margin="0,0,8,0"
                                Visibility="{Binding CanMakeMain, Converter={StaticResource BoolToVisible}}"
                                AutomationProperties.Name="{Binding MakeMainName}" />
                        <Button Content="Remove" Tag="{Binding SourceId}" Click="OnRemoveClick"
                                AutomationProperties.Name="{Binding RemoveName}" />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>
    </UserControl.Resources>

    <StackPanel MaxWidth="780" HorizontalAlignment="Left">
        <TextBlock x:Name="ClansPageTitle" Style="{StaticResource Heading}" />
        <TextBlock x:Name="RecipeLine" Style="{StaticResource Muted}" Margin="0,4,0,18" />

        <TextBlock x:Name="MainClanLabel" FontWeight="SemiBold" Margin="0,0,0,4" />
        <TextBlock x:Name="MainCurrentLine" Style="{StaticResource Muted}" Margin="0,0,0,8" />
        <ui:ClanSearchBox x:Name="MainClanSearch" />
        <TextBlock x:Name="MainFoundLine" TextWrapping="Wrap" Margin="0,8,0,0" Visibility="Collapsed" />
        <Button x:Name="MainWatchInsteadButton" Content="Watch it instead" HorizontalAlignment="Left" Margin="0,8,0,0"
                Visibility="Collapsed" Click="OnWatchInsteadClick" />

        <TextBlock x:Name="MineLabel" FontWeight="SemiBold" Margin="0,26,0,8" />
        <ItemsControl x:Name="MineList" ItemTemplate="{StaticResource ClanRowTemplate}"
                      AutomationProperties.Name="Sources your accounts are in" />
        <TextBlock x:Name="MineEmptyLine" Style="{StaticResource Muted}" Margin="0,0,0,8" Visibility="Collapsed" />
        <Button x:Name="AddMineButton" HorizontalAlignment="Left" Click="OnAddMineClick" />
        <ui:ClanSearchBox x:Name="MineClanSearch" Margin="0,8,0,0" Visibility="Collapsed" />
        <TextBlock x:Name="MineFoundLine" TextWrapping="Wrap" Margin="0,8,0,0" Visibility="Collapsed" />
        <Button x:Name="MineWatchInsteadButton" Content="Watch it instead" HorizontalAlignment="Left" Margin="0,8,0,0"
                Visibility="Collapsed" Click="OnWatchInsteadClick" />

        <TextBlock x:Name="WatchLabel" FontWeight="SemiBold" Margin="0,26,0,8" />
        <ItemsControl x:Name="WatchList" ItemTemplate="{StaticResource ClanRowTemplate}"
                      AutomationProperties.Name="Sources you're watching" />
        <TextBlock x:Name="WatchEmptyLine" Style="{StaticResource Muted}" Margin="0,0,0,8" Visibility="Collapsed" />
        <Button x:Name="WatchClanButton" HorizontalAlignment="Left" Click="OnWatchClanClick" />
        <ui:ClanSearchBox x:Name="WatchClanSearch" Margin="0,8,0,0" Visibility="Collapsed" />
        <TextBlock x:Name="WatchFoundLine" TextWrapping="Wrap" Margin="0,8,0,0" Visibility="Collapsed" />

        <Border x:Name="TopRow" Style="{StaticResource Card}" Margin="0,26,0,0" Visibility="Collapsed">
            <StackPanel>
                <CheckBox x:Name="TopSwitch" Checked="OnTopSwitched" Unchecked="OnTopSwitched" FontWeight="SemiBold" />
                <TextBlock x:Name="TopLine" Style="{StaticResource Muted}" Margin="22,4,0,0" />
            </StackPanel>
        </Border>

        <TextBlock x:Name="RequestsLine" Style="{StaticResource Muted}" Margin="0,22,0,0" />
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/ClansPage.xaml.cs`

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Setup › Clans for one recipe (spec §7.1): the main clan search, the clans your accounts are in, the
/// clans you watch, and the Top switch. Every change is saved and applied at once.
/// </summary>
public partial class ClansPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly string _slug;
    private readonly CancellationTokenSource _closing = new();
    private string? _mainProbeId;
    private string? _mineProbeId;
    private bool _rendering;

    public ClansPage(ISetupServices services, string recipeSlug)
    {
        InitializeComponent();
        _services = services;
        _slug = recipeSlug;

        MainClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Main);
        MineClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Mine);
        WatchClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Watch);
        Unloaded += (_, _) => _closing.Cancel();

        Refresh();
        _ = LoadNamesAsync();
    }

    private InstalledRecipe? Installed =>
        _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _slug, StringComparison.Ordinal));

    public void Refresh()
    {
        if (Installed is not { } installed)
        {
            ClansPageTitle.Text = "Not installed";
            RecipeLine.Text = "This recipe is no longer installed.";
            return;
        }

        var recipe = installed.Recipe;
        var group = RecipeWords.Group(recipe);
        var groups = RecipeWords.Groups(recipe);
        var accounts = _services.KnownAccounts;
        var lists = ClansModel.Lists(recipe, _services.Sources, _services.Latest, accounts);

        _rendering = true;
        try
        {
            ClansPageTitle.Text = groups;
            RecipeLine.Text = $"From {recipe.Name}. Changes apply at once. Removing a {group} keeps its score book.";

            MainClanLabel.Text = $"Your main {group}";
            MainCurrentLine.Text = lists.Main is { } main
                ? $"★ {main.Name} · {main.Who}"
                : $"No main {group} yet. Type a few letters of its name.";
            MainClanSearch.SetLabel($"Your main {group}");

            MineLabel.Text = $"{groups} your accounts are in";
            MineList.ItemsSource = lists.Mine;
            Show(MineEmptyLine, lists.Mine.Count == 0 ? "None yet." : "");
            AddMineButton.Content = $"Add a {group} your accounts are in";
            MineClanSearch.SetLabel($"Add a {group} your accounts are in");

            WatchLabel.Text = $"{groups} you're watching";
            WatchList.ItemsSource = lists.Watching;
            Show(WatchEmptyLine, lists.Watching.Count == 0 ? "None yet." : "");
            WatchClanButton.Content = $"Watch a {group}";
            WatchClanSearch.SetLabel($"Watch a {group}");

            var top = ClansModel.GroupListSource(_services.Sources, _services.Installed);
            TopRow.Visibility = top is null ? Visibility.Collapsed : Visibility.Visible;
            if (top is not null
                && _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, top.Recipe, StringComparison.Ordinal))?.Recipe is { } topRecipe)
            {
                var period = RecipeWords.Period(topRecipe.Period is not null ? topRecipe : recipe);
                TopSwitch.Content = $"Top of the {period}";
                AutomationProperties.SetName(TopSwitch, $"Top of the {period}");
                TopSwitch.IsChecked = top.Enabled;
                TopLine.Text = $"The leading {RecipeWords.GroupsLower(recipe)} from {topRecipe.Name}, for the Top of the {period} panel. Shown live, never kept.";
            }

            Show(RequestsLine, ClansModel.RequestsLine(ClansModel.RequestsPerHour(_services.Sources, _services.Installed, accounts.Count)));
        }
        finally
        {
            _rendering = false;
        }
    }

    private async Task LoadNamesAsync()
    {
        if (Installed?.Recipe is not { } recipe) return;

        ClanSearchBox[] boxes = [MainClanSearch, MineClanSearch, WatchClanSearch];
        var group = RecipeWords.Group(recipe);
        void Status(string text)
        {
            foreach (var box in boxes) box.SetStatus(text);
        }

        if (RecipeWords.MainInput(recipe)?.Search is not { } search)
        {
            foreach (var box in boxes) box.AllowTyped = true;
            Status($"Type the exact {group} name, then press Enter.");
            return;
        }

        Status($"Reading the {group} list once from {RecipeHosts.HostOf(search.Url)}…");

        SearchListResult result;
        try
        {
            result = await _services.SearchListAsync(search, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (result.Problem is not null)
        {
            foreach (var box in boxes) box.AllowTyped = true;
            Status(_services.Redactor.Redact($"{result.Problem} Type the exact {group} name, then press Enter."));
            return;
        }

        foreach (var box in boxes) box.SetNames(result.Names);
        Status($"Searches all {result.Names.Count.ToString("N0", CultureInfo.InvariantCulture)} {group} names. Enter or a click picks one.");
    }

    private async Task PickAsync(string picked, SourceRole role)
    {
        if (Installed?.Recipe is not { } recipe) return;

        var name = picked.Trim();
        var line = role switch { SourceRole.Main => MainFoundLine, SourceRole.Mine => MineFoundLine, _ => WatchFoundLine };
        var watchInstead = role switch { SourceRole.Main => MainWatchInsteadButton, SourceRole.Mine => MineWatchInsteadButton, _ => null };
        if (watchInstead is not null) watchInstead.Visibility = Visibility.Collapsed;

        var before = _services.Sources;
        SourceChange change;
        try
        {
            change = ClansModel.Pick(before, recipe, name, role);
        }
        catch (Exception ex)
        {
            Show(line, _services.Redactor.Redact($"Could not add that: {ex.Message}"));
            return;
        }

        if (change.Note is not null)
        {
            Show(line, change.Note);
            return;
        }

        if (before.All(s => s.Id != change.SourceId) && ClansModel.NeedsConfirmation(before, recipe.Slug))
        {
            var after = ClansModel.RequestsPerHour(change.Sources, _services.Installed, _services.KnownAccounts.Count);
            if (!Confirm(ClansModel.ConfirmText(recipe, after))) return;
        }

        try
        {
            _services.SaveSources(change.Sources);
        }
        catch (Exception ex)
        {
            Show(line, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return;
        }

        if (role == SourceRole.Watch)
        {
            WatchClanSearch.Visibility = Visibility.Collapsed;
            Show(line, $"Watching {name}. Only its own numbers are read; none of its members are matched to your accounts.");
            Refresh();
            return;
        }

        Show(line, $"Reading {name} once…");

        RecipeSnapshot? snapshot;
        try
        {
            snapshot = await _services.ReadOnceAsync(change.SourceId, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Show(line, _services.Redactor.Redact($"Added {name}, but the read failed: {ex.Message}"));
            return;
        }

        var probe = ClansModel.Probe(name, snapshot, _services.KnownAccounts);
        Show(line, _services.Redactor.Redact(probe.Text));

        if (role == SourceRole.Main) _mainProbeId = probe.OfferWatch ? change.SourceId : null;
        else _mineProbeId = probe.OfferWatch ? change.SourceId : null;

        if (watchInstead is not null) watchInstead.Visibility = probe.OfferWatch ? Visibility.Visible : Visibility.Collapsed;
        if (role == SourceRole.Mine && !probe.OfferWatch) MineClanSearch.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void OnAddMineClick(object sender, RoutedEventArgs e)
    {
        MineClanSearch.Visibility = Visibility.Visible;
        MineClanSearch.FocusSearch();
    }

    private void OnWatchClanClick(object sender, RoutedEventArgs e)
    {
        WatchClanSearch.Visibility = Visibility.Visible;
        WatchClanSearch.FocusSearch();
    }

    private void OnWatchInsteadClick(object sender, RoutedEventArgs e)
    {
        var fromMain = ReferenceEquals(sender, MainWatchInsteadButton);
        if ((fromMain ? _mainProbeId : _mineProbeId) is not { } id) return;

        if (!Save(ClansModel.WatchInstead(_services.Sources, id))) return;

        Show(fromMain ? MainFoundLine : MineFoundLine,
            "Watching it instead. Only its own numbers are read; none of its members are matched to your accounts.");
        ((Button)sender).Visibility = Visibility.Collapsed;
        if (fromMain) _mainProbeId = null;
        else _mineProbeId = null;
    }

    private void OnMakeMainClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Save(SourceRules.MakeMain(_services.Sources, id));
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Save(SourceRules.Remove(_services.Sources, id));
    }

    private void OnTopSwitched(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        if (ClansModel.GroupListSource(_services.Sources, _services.Installed) is { } top)
        {
            Save(ClansModel.SetEnabled(_services.Sources, top.Id, TopSwitch.IsChecked == true));
        }
    }

    private bool Save(IReadOnlyList<Source> sources)
    {
        try
        {
            _services.SaveSources(sources);
            Refresh();
            return true;
        }
        catch (Exception ex)
        {
            Show(RequestsLine, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return false;
        }
    }

    private bool Confirm(string text)
    {
        var owner = Window.GetWindow(this);
        var answer = owner is null
            ? MessageBox.Show(text, "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
            : MessageBox.Show(owner, text, "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.OK;
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

- [ ] **Step 12: Build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: every test passes. `NoHostnameFenceTests` stays green: the new files name no host, and the search-list host comes from the recipe.

- [ ] **Step 13: Commit**

```text
git add src/Recipes/SearchLists.cs src/Board/RecipeWords.cs src/UI/Setup/ClansModel.cs src/UI/Setup/SetupPages.cs src/Composition/ISetupServices.cs src/UI/Controls/ClanSearchBox.xaml src/UI/Controls/ClanSearchBox.xaml.cs src/UI/SetupWindow.xaml src/UI/SetupWindow.xaml.cs src/UI/Setup/ClansPage.xaml src/UI/Setup/ClansPage.xaml.cs src/App.xaml tests/SearchListsTests.cs tests/RecipeWordsTests.cs tests/ClansModelTests.cs tests/SetupPagesTests.cs
git commit -m "setup: the Setup window, the clan search and the Clans page

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 12: Setup pages: Your accounts, Stats, Recipes, Alerts, Score book, Diagnostics

Spec §7.2–§7.7. Each page is a `UserControl` that renders from `ISetupServices`. The logic each page needs lives in a pure model with tests:
- **Your accounts:** Send per account per recipe, stored as `RecipeState.ExcludedAccountIds` exactly as today, with the budget refusal. It also shows where each account was found, and "Accounts come from RoRoRo. Last listed 2 min ago."
- **Stats:** one `StatsTable` per recipe, saved through `RecipeStore.SaveState`, with the watches updated. A counter recipe with no saved names reads them once when the page opens.
- **Recipes:** the list, **Remove** (never deletes the book, forgets the recipe's sources), and **Import recipe…**. The import flow moves here from `MainWindow.OnImportRecipeClick`. After a first import of a recipe with inputs, Setup goes to its Clans page.
- **Alerts:** the rule card and the report policy card, moved from `MainWindow` with the same behaviour.
- **Score book:** the folder with **Open folder**, per recipe readings/first/finals/size, and each source that isn't recording with its reason.
- **Diagnostics:** per source state, detail, last and next read, and misses. **Copy diagnostics** goes through `Redactor` and never includes book content.

**Files:**
- Create: `src/UI/Setup/AccountsModel.cs`, `src/UI/Setup/AccountsPage.xaml`, `src/UI/Setup/AccountsPage.xaml.cs`, `tests/AccountsModelTests.cs`
- Create: `src/UI/Setup/StatsPage.xaml`, `src/UI/Setup/StatsPage.xaml.cs`
- Create: `src/UI/Setup/RecipesModel.cs`, `src/UI/Setup/RecipesPage.xaml`, `src/UI/Setup/RecipesPage.xaml.cs`, `src/UI/Setup/ImportFlow.cs`, `tests/RecipesModelTests.cs`
- Create: `src/UI/Setup/AlertsModel.cs`, `src/UI/Setup/AlertsPage.xaml`, `src/UI/Setup/AlertsPage.xaml.cs`, `tests/AlertsModelTests.cs`
- Create: `src/UI/Setup/ScoreBookModel.cs`, `src/UI/Setup/ScoreBookPage.xaml`, `src/UI/Setup/ScoreBookPage.xaml.cs`, `tests/ScoreBookModelTests.cs`
- Create: `src/UI/Setup/DiagnosticsModel.cs`, `src/UI/Setup/DiagnosticsPage.xaml`, `src/UI/Setup/DiagnosticsPage.xaml.cs`, `tests/DiagnosticsModelTests.cs`
- Modify: `src/UI/SetupWindow.xaml.cs` (`CreatePage`)

**Interfaces:**
- Consumes:
  - Task 11: `ISetupServices`, `ISetupPage`, `SetupPages`, `SetupWindow.ShowPage`, `ClansModel.NameOf`, `RecipeWords`
  - Task 10: `StatsTable`, `CounterLookup`
  - Task 9: `ScoreBookReader.Readings`/`FirstReading`/`Finals`/`Bytes`
  - Task 6: `RecipeSnapshot.Recorded`, `NotRecordingReason`
  - Task 4: `AccountList`, `AccountsCache.SavedAt`
  - Task 3: `Source`, `SourceRole`
  - `StatText.Span` (contract)
  - existing: `HistoryBudget`, `ReportPolicy.Describe`, `RulesFile`, `RuleInventory`, `ImportReview`, `RecipeParser`, `Redactor`, `WatchState`
- Produces (namespace `Labs626.UrScore.UI`):
  - Accounts:
    - `sealed class SendTick : INotifyPropertyChanged` (`RecipeSlug`, `AccountId`, `Name`, `On`)
    - `sealed record AccountRow(Guid AccountId, string DisplayName, string FoundIn, IReadOnlyList<SendTick> Sends)`
    - `sealed record SendChange(RecipeState State, string? Refusal)`
    - `static class AccountsModel`, with members:
      - `IReadOnlyList<InstalledRecipe> SendingRecipes(IReadOnlyList<InstalledRecipe>)`
      - `IReadOnlyList<AccountRow> Rows(IReadOnlyList<HostAccount>, IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>, IReadOnlyDictionary<string, RecipeSnapshot>)`
      - `string FoundIn(HostAccount, IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>, IReadOnlyDictionary<string, RecipeSnapshot>)`
      - `SendChange ToggleSend(InstalledRecipe recipe, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds, Guid accountId, bool on)`
      - `string ListedLine(AccountList? last, DateTimeOffset? savedAt, DateTimeOffset now)`
      - `string Ago(DateTimeOffset then, DateTimeOffset now)`
  - Recipes:
    - `sealed record RecipeItem(string Slug, string Name, string Hosts, string Every, string Sources, string? IconFile)`
    - `static class RecipesModel`, with members `Items(...)`, `SourcesText(Recipe, IReadOnlyList<Source>)`, `ConfirmRemove(Recipe)`, `const string KeepsBook`
  - Import flow:
    - `sealed record ImportOutcome(string Slug, string Message, bool ChooseSources)`
    - `static class ImportFlow`, with members `const string DialogTitle` and `Task<ImportOutcome?> RunAsync(Window owner, ISetupServices services)`
  - Alerts:
    - `sealed record RuleChoice(string MetricId, string Text)`
    - `sealed record PolicyItem(string RecipeName, string Line, string Counts)`
    - `static class AlertsModel`, with members:
      - constants `DefaultThreshold`, `DefaultWindowMinutes`, `MetricAlertsOff`, `NoSentStat`, `NoRecipe`
      - `IReadOnlyList<RuleChoice> Choices(IReadOnlyList<InstalledRecipe>)`
      - `(string Text, bool CanAdd) RuleSentence(string metricId, string? rulesPath = null, string? inventoryPath = null)`
      - `string Preview(bool canAdd)`
      - `IReadOnlyList<PolicyItem> Policies(IReadOnlyList<InstalledRecipe>, IReadOnlyList<HostAccount>, bool resolveNames, Func<string, (int Sent, int Dropped)> counts)`
  - Score book:
    - `sealed record BookRecipeItem(string Name, string Readings, string First, string Finals, string Size)` (with `Summary`)
    - `sealed record NotRecordingItem(string Source, string Reason)`
    - `static class ScoreBookModel`, with members:
      - `string Size(long bytes)`
      - `string SourceLabel(Recipe, Source)`
      - `IReadOnlyList<BookRecipeItem> Recipes(IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>, ScoreBookReader)`
      - `IReadOnlyList<NotRecordingItem> NotRecording(IReadOnlyList<InstalledRecipe>, IReadOnlyList<Source>, IReadOnlyDictionary<string, RecipeSnapshot>, bool running, bool accountsEverListed)`
  - Diagnostics:
    - `sealed record SourceDiagnostic(string SourceId, string Name, string State, string Detail, string LastRead, string NextRead, string Misses)`
    - `static class DiagnosticsModel`, with members:
      - `const int TrailLines = 40`
      - `string StateText(WatchState)`
      - `Sources(...)`
      - `string Misses(Recipe?, RecipeSnapshot?, IReadOnlyList<HostAccount>)`
      - `string CopyText(...)`
  - Pages: `AccountsPage`, `StatsPage`, `RecipesPage`, `AlertsPage`, `ScoreBookPage`, `DiagnosticsPage` (each `UserControl, ISetupPage`), and `sealed record RecipeChoice(string Slug, string Name)`

- [ ] **Step 1: Write the failing tests**

Create `tests/AccountsModelTests.cs`

```csharp
using System.ComponentModel;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AccountsModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static InstalledRecipe Sending(Recipe recipe, IReadOnlyList<string>? excluded = null) => new(recipe, "", new RecipeState(
        ExcludedAccountIds: excluded,
        Stats: new Dictionary<string, StatChoice> { [recipe.LastStep.Values[0].Id] = new(Send: true, MetricId: "test.metric") }));

    private static RecipeSnapshot Read(params long[] userIds) =>
        new(WatchState.NoMatches, null, [], [], userIds.Length, "battle=A", [.. userIds.Select(id => RecipeEngineTests.Row(id, 10))], []);

    [Fact]
    public void TheLineSaysWhenRoRoRoLastListedTheAccounts() =>
        Assert.Equal("Accounts come from RoRoRo. Last listed 2 min ago.",
            AccountsModel.ListedLine(new AccountList([Main], true, false, false, Now.AddMinutes(-2)), null, Now));

    [Fact]
    public void WithRoRoRoDownTheSavedListIsNamedAsSuch() =>
        Assert.Equal("Accounts come from RoRoRo. RoRoRo isn't answering, so these are the ones it listed 3 h ago.",
            AccountsModel.ListedLine(new AccountList([Main], false, true, false, null), Now.AddHours(-3), Now));

    [Fact]
    public void BeforeAnyListTheLineSaysSo() =>
        Assert.Equal("Accounts come from RoRoRo. RoRoRo hasn't listed them yet.", AccountsModel.ListedLine(null, null, Now));

    [Fact]
    public void ARefusedListNamesTheCapability() =>
        Assert.StartsWith("Accounts come from RoRoRo. RoRoRo refused to list them: host.queries.accounts is not granted.",
            AccountsModel.ListedLine(new AccountList([], true, false, true, null), null, Now));

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(5 * 3600, "5 h ago")]
    [InlineData(3 * 86400, "3 days ago")]
    public void AgoRoundsDown(int seconds, string text) => Assert.Equal(text, AccountsModel.Ago(Now.AddSeconds(-seconds), Now));

    [Fact]
    public void TurningSendOffExcludesTheAccountAndKeepsOtherEntries()
    {
        var recipe = Sending(Profile, ["not-a-guid", Alt.AccountId.ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], [Main.AccountId, Alt.AccountId], Main.AccountId, on: false);

        Assert.Null(change.Refusal);
        Assert.Equal(new[] { "not-a-guid", Alt.AccountId.ToString(), Main.AccountId.ToString() }, change.State.ExcludedAccountIds!.ToArray());
    }

    [Fact]
    public void TurningSendOnRemovesTheExclusion()
    {
        var recipe = Sending(Profile, [Alt.AccountId.ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], [Main.AccountId, Alt.AccountId], Alt.AccountId, on: true);

        Assert.Null(change.Refusal);
        Assert.Empty(change.State.ExcludedAccountIds!);
    }

    [Fact]
    public void TurningSendOnPastRoRoRosLimitIsRefusedAndChangesNothing()
    {
        var accounts = Enumerable.Range(0, HistoryBudget.Limit + 1).Select(_ => Guid.NewGuid()).ToList();
        var recipe = Sending(Profile, [accounts[0].ToString()]);

        var change = AccountsModel.ToggleSend(recipe, [recipe], accounts, accounts[0], on: true);

        Assert.StartsWith("Not allowed: that would use 257 of RoRoRo's 256 history slots.", change.Refusal);
        Assert.Same(recipe.State, change.State);
    }

    [Fact]
    public void FoundInNamesTheClansAnAccountWasReadInMainFirst()
    {
        var clan = Sending(Clan);
        Source[] sources =
        [
            new("s-00000002", Clan.Slug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine),
            new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main),
            new("s-00000003", Clan.Slug, new Dictionary<string, string> { ["clan"] = "NovaForge" }, SourceRole.Watch),
        ];
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            ["s-00000001"] = Read(101),
            ["s-00000002"] = Read(101),
            ["s-00000003"] = Read(101, 201),
        };

        Assert.Equal("★ CCGP, K0i2", AccountsModel.FoundIn(Main, [clan], sources, latest));
        Assert.Equal("Not in a watched clan", AccountsModel.FoundIn(Alt, [clan], sources, latest));
        Assert.Equal("", AccountsModel.FoundIn(Main, [Sending(Profile)], sources, latest));
    }

    [Fact]
    public void EachRowHasOneSendTickPerSendingRecipe()
    {
        var profile = Sending(Profile, [Alt.AccountId.ToString()]);

        var rows = AccountsModel.Rows([Main, Alt], [profile], [], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal("Send estehernandez for Pet Sim 99 profile", rows[0].Sends.Single().Name);
        Assert.True(rows[0].Sends.Single().On);
        Assert.False(rows[1].Sends.Single().On);

        var raised = new List<string?>();
        ((INotifyPropertyChanged)rows[1].Sends[0]).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        rows[1].Sends[0].On = true;
        Assert.Equal(new string?[] { nameof(SendTick.On) }, raised);
    }
}
```

Create `tests/RecipesModelTests.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class RecipesModelTests
{
    private const string GroupList = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static Source ClanSource(string id, string clan) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Mine);

    [Fact]
    public void AnItemNamesHostsIntervalSourcesAndIcon()
    {
        var items = RecipesModel.Items(
            [new InstalledRecipe(Clan, "", new RecipeState()), new InstalledRecipe(Profile, "", new RecipeState())],
            [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2")],
            slug => slug == Clan.Slug ? @"C:\cache\icon.png" : null);

        Assert.Equal("Pet Sim 99 clan battle points", items[0].Name);
        Assert.Equal("ps99.biggamesapi.io", items[0].Hosts);
        Assert.Equal($"Asks every {Clan.EffectiveEverySeconds} s", items[0].Every);
        Assert.Equal("2 clans", items[0].Sources);
        Assert.True(items[0].HasIcon);
        Assert.Equal("Remove Pet Sim 99 clan battle points", items[0].RemoveName);
        Assert.Equal("", items[1].Sources);
        Assert.False(items[1].HasIcon);
    }

    [Fact]
    public void SourcesTextCountsInTheRecipesWords()
    {
        Assert.Equal("No clans yet", RecipesModel.SourcesText(Clan, []));
        Assert.Equal("1 clan", RecipesModel.SourcesText(Clan, [ClanSource("s-00000001", "CCGP")]));
        Assert.Equal("Shown live, never kept", RecipesModel.SourcesText(RecipeParser.Parse(GroupList).Recipe!, []));
    }

    [Fact]
    public void RemovingSaysTheBookStays() =>
        Assert.Equal(
            "Remove Pet Sim 99 profile? It stops being read. Its score book stays on this PC, so importing it again carries on where it left off.",
            RecipesModel.ConfirmRemove(Profile));
}
```

Create `tests/AlertsModelTests.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertsModelTests
{
    private const string MetricId = "ps99.diamonds";

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    private static readonly HostAccount Alt = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");

    private static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    private static string TempFile(string? text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "urscore-alerts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "file.json");
        if (text is not null) File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void NoRulesFileCanBeCreatedByAddingOne() =>
        Assert.Equal(
            ("RoRoRo has no rules file yet, so nothing can alert until a rule is added. Adding one creates the file.", true),
            AlertsModel.RuleSentence(MetricId, TempFile(null), TempFile(null)));

    [Fact]
    public void RulesWithoutThisMetricSayReportsWillNeverAlert() =>
        Assert.Equal(
            ($"RoRoRo has rules, but none for {MetricId} — so reports will land and never alert.", true),
            AlertsModel.RuleSentence(MetricId, TempFile("""[ { "metricId": "other.one", "kind": "Rate", "threshold": 1 } ]"""), TempFile(null)));

    [Fact]
    public void OurRuleIntactIsReady() =>
        Assert.Equal(
            ($"Ready: RoRoRo has a rule for {MetricId} at 100.", false),
            AlertsModel.RuleSentence(MetricId,
                TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 100, "owner": "626labs.ur-score" } ]"""),
                TempFile(null)));

    [Fact]
    public void OurRuleChangedSinceWeAddedItIsLeftAlone() =>
        Assert.Equal(
            ($"Your rule for {MetricId} has been changed since Ur Score added it (now 50, was 100). Left exactly as it is.", false),
            AlertsModel.RuleSentence(MetricId,
                TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 50, "owner": "626labs.ur-score" } ]"""),
                TempFile($$"""{ "{{MetricId}}": 100 }""")));

    [Fact]
    public void AUsersOwnRuleIsNeverTouched() =>
        Assert.Equal(
            ($"You wrote the rule for {MetricId} yourself (threshold 25). Ur Score will not touch it.", false),
            AlertsModel.RuleSentence(MetricId, TempFile($$"""[ { "metricId": "{{MetricId}}", "kind": "Rate", "threshold": 25 } ]"""), TempFile(null)));

    [Fact]
    public void AnUnreadableFileIsNeverOverwritten() =>
        Assert.Equal(
            ("RoRoRo's rules file is not valid JSON. Ur Score will not overwrite it — check it by hand.", false),
            AlertsModel.RuleSentence(MetricId, TempFile("not json"), TempFile(null)));

    [Fact]
    public void RuleChoicesAreEverySentStatOnce()
    {
        var state = new RecipeState(Stats: new Dictionary<string, StatChoice>
        {
            ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds"),
            ["rank"] = new(Show: true, Send: true, MetricId: "ps99.rank"),
            ["eggs"] = new(Show: true, MetricId: "ps99.eggs-hatched"),
        });

        var choices = AlertsModel.Choices([new InstalledRecipe(Profile, "", state), new InstalledRecipe(Profile, "", state)]);

        Assert.Equal(new[] { "Diamonds (ps99.diamonds)", "Player rank (ps99.rank)" }, choices.Select(c => c.ToString()).ToArray());
    }

    [Fact]
    public void ThePolicyLineIsReportPolicysOwnSentence()
    {
        var state = new RecipeState(
            ExcludedAccountIds: [Alt.AccountId.ToString()],
            Stats: new Dictionary<string, StatChoice> { ["diamonds"] = new(Send: true, MetricId: "ps99.diamonds") });

        var item = Assert.Single(AlertsModel.Policies([new InstalledRecipe(Profile, "", state)], [Main, Alt], resolveNames: false, _ => (3, 1)));

        var expected = new ReportPolicy([new SentStat("diamonds", "Diamonds", "ps99.diamonds")], new HashSet<Guid> { Main.AccountId }).Describe(2, false);
        Assert.Equal(new PolicyItem("Pet Sim 99 profile", expected, "Sent 3, dropped 1 this session."), item);
    }

    [Fact]
    public void ThePreviewShowsOnlyWhenARuleCanBeAdded()
    {
        Assert.Equal("Rate, below 100 per minute over 10 minutes", AlertsModel.Preview(true));
        Assert.Equal("", AlertsModel.Preview(false));
    }
}
```

Create `tests/ScoreBookModelTests.cs`

```csharp
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ScoreBookModelTests
{
    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static InstalledRecipe Installed => new(Clan, "", new RecipeState());

    private static Source ClanSource(string id, string clan, bool enabled = true) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Main, enabled);

    private static RecipeSnapshot Snapshot(bool recorded, string? reason) =>
        new(WatchState.Reporting, null, [], [], 1) { Recorded = recorded, NotRecordingReason = reason, SourceId = "s-00000001" };

    [Theory]
    [InlineData(512L, "512 bytes")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(262144L, "256 KB")]
    [InlineData(5505024L, "5.25 MB")]
    public void SizeReadsInBytesKilobytesOrMegabytes(long bytes, string text) => Assert.Equal(text, ScoreBookModel.Size(bytes));

    [Fact]
    public void StoppedSourcesSayStopped()
    {
        var items = ScoreBookModel.NotRecording([Installed], [ClanSource("s-00000001", "CCGP")],
            new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Snapshot(true, null) }, running: false, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Stopped. Press Start on the board."), Assert.Single(items));
    }

    [Fact]
    public void ARunningSourceGivesItsOwnReasonAndARecordingOneIsNotListed()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2")];
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            ["s-00000001"] = Snapshot(false, "The battle ended and its final is saved."),
            ["s-00000002"] = Snapshot(true, null),
        };

        var items = ScoreBookModel.NotRecording([Installed], sources, latest, running: true, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "The battle ended and its final is saved."), Assert.Single(items));
    }

    [Fact]
    public void SwitchedOffNotReadAndNeverListedAreEachSaid()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", enabled: false), ClanSource("s-00000002", "K0i2")];

        var items = ScoreBookModel.NotRecording([Installed], sources, new Dictionary<string, RecipeSnapshot>(), running: true, accountsEverListed: false);

        Assert.Equal(new[]
        {
            new NotRecordingItem("Your accounts", "RoRoRo has never listed your accounts, so nothing per account is kept yet. Start RoRoRo while Ur Score runs."),
            new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Switched off."),
            new NotRecordingItem("K0i2 · Pet Sim 99 clan battle points", "Not read yet."),
        }, items.ToArray());
    }

    [Fact]
    public void AnEmptyBookCountsNothing()
    {
        var reader = new ScoreBookReader(Path.Combine(Path.GetTempPath(), "urscore-empty-" + Guid.NewGuid().ToString("N")), TimeProvider.System);

        var item = Assert.Single(ScoreBookModel.Recipes([Installed], [ClanSource("s-00000001", "CCGP")], reader));

        Assert.Equal(new BookRecipeItem("Pet Sim 99 clan battle points", "0 readings kept", "No reading yet", "0 finished battles kept", "0 bytes"), item);
        Assert.Equal("0 readings kept · No reading yet · 0 finished battles kept · 0 bytes", item.Summary);
    }
}
```

Create `tests/DiagnosticsModelTests.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class DiagnosticsModelTests
{
    private const string FakeKey = "SECRET-KEY-VALUE-123";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");

    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static InstalledRecipe Installed => new(Clan, "", new RecipeState());

    private static Source ForClan => new("s-00000001", Clan.Slug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

    [Theory]
    [InlineData(WatchState.Reporting, "Reporting to RoRoRo.")]
    [InlineData(WatchState.HostDown, "RoRoRo is not running.")]
    [InlineData(WatchState.SourceIdle, "Nothing to read right now.")]
    [InlineData(WatchState.Showing, "Reading. No stat is set to send to RoRoRo.")]
    public void StatesReadAsSentences(WatchState state, string text) => Assert.Equal(text, DiagnosticsModel.StateText(state));

    [Fact]
    public void EachSourceSaysWhenItWasReadAndWhenItReadsNext()
    {
        var source = ForClan;
        var snapshot = new RecipeSnapshot(WatchState.Reporting, $"Reporting 1 of 50 row(s). key={FakeKey}", [], [], 50);
        var latest = new Dictionary<string, RecipeSnapshot> { [source.Id] = snapshot };
        var redactor = new Redactor(() => [FakeKey]);

        var justRead = DiagnosticsModel.Sources([Installed], [source], latest, _ => Now.AddMinutes(-1), running: true, [Main], Now, redactor).Single();
        var late = DiagnosticsModel.Sources([Installed], [source], latest, _ => Now.AddMinutes(-5), running: true, [Main], Now, redactor).Single();
        var stopped = DiagnosticsModel.Sources([Installed], [source], latest, _ => null, running: false, [Main], Now, redactor).Single();

        Assert.Equal("CCGP · Pet Sim 99 clan battle points", justRead.Name);
        Assert.Equal("Reporting to RoRoRo.", justRead.State);
        Assert.Equal($"{StatText.Span(TimeSpan.FromMinutes(1))} ago", justRead.LastRead);
        Assert.Equal($"in {StatText.Span(TimeSpan.FromSeconds(Clan.EffectiveEverySeconds - 60))}", justRead.NextRead);
        Assert.DoesNotContain(FakeKey, justRead.Detail);
        Assert.Equal("due now", late.NextRead);
        Assert.Equal("never", stopped.LastRead);
        Assert.Equal("when you press Start", stopped.NextRead);
    }

    [Fact]
    public void MissesNameOnlyYourOwnAccounts()
    {
        var snapshot = new RecipeSnapshot(WatchState.Reporting, null, [], [], 2)
        {
            StatMisses = new Dictionary<string, string> { ["value"] = "no 'Points' in any row" },
            CellMisses = new Dictionary<(long UserId, string Stat), string>
            {
                [(101, "value")] = "no 'Points' here",
                [(999, "value")] = "someone else's miss",
            },
        };

        var misses = DiagnosticsModel.Misses(Clan, snapshot, [Main]);

        Assert.Contains("Points: no 'Points' in any row", misses);
        Assert.Contains("estehernandez · Points: no 'Points' here", misses);
        Assert.DoesNotContain("999", misses);
        Assert.DoesNotContain("someone else's", misses);
    }

    [Fact]
    public void CopiedDiagnosticsHideKeysKeepTheLastFortyTrailLinesAndNoBookContent()
    {
        var source = ForClan;
        var row = new SourceDiagnostic(source.Id, "CCGP", "Reporting to RoRoRo.", $"detail {FakeKey}", "1m ago", "in 2m", "");
        var trail = Enumerable.Range(0, 50).Select(i => $"trail-{i:00};").ToList();

        var text = DiagnosticsModel.CopyText(Now, [Installed], [source], [row], resolveNames: true, "host=1.28.0.0 reject=(none)",
            @"C:\data\last-response", @"C:\data\scorebook", bookPending: 2, bookDropped: 0, trail, new Redactor(() => [FakeKey]));

        Assert.DoesNotContain(FakeKey, text);
        Assert.Contains(Redactor.Mask, text);
        Assert.Contains("score book in C:\\data\\scorebook: pending=2 dropped=0 (no book content is included)", text);
        Assert.Contains($"source={source.Id} recipe={Clan.Slug} role=Main enabled=True inputs=clan=CCGP", text);
        Assert.DoesNotContain("trail-09;", text);
        Assert.Contains("trail-10;", text);
        Assert.Contains("trail-49;", text);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: the build FAILS with `error CS0103: The name 'AccountsModel' does not exist in the current context`, and the same for `RecipesModel`, `AlertsModel`, `ScoreBookModel` and `DiagnosticsModel`.

- [ ] **Step 3: Create the models**

Create `src/UI/Setup/AccountsModel.cs`

```csharp
using System.ComponentModel;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One account's Send for one recipe. Raises a change so the page can check the budget and save.</summary>
public sealed class SendTick : INotifyPropertyChanged
{
    private bool _on;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RecipeSlug { get; init; }

    public required Guid AccountId { get; init; }

    /// <summary>The accessible name: "Send estehernandez for Pet Sim 99 profile".</summary>
    public required string Name { get; init; }

    public bool On
    {
        get => _on;
        set
        {
            if (_on == value) return;
            _on = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(On)));
        }
    }
}

public sealed record AccountRow(Guid AccountId, string DisplayName, string FoundIn, IReadOnlyList<SendTick> Sends);

/// <summary>A recipe's state after a Send change, or the refusal that undid it.</summary>
public sealed record SendChange(RecipeState State, string? Refusal);

/// <summary>Setup › Your accounts (spec §7.2).</summary>
public static class AccountsModel
{
    /// <summary>Group lists never send, so they get no Send column.</summary>
    public static IReadOnlyList<InstalledRecipe> SendingRecipes(IReadOnlyList<InstalledRecipe> installed) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList)];

    public static IReadOnlyList<AccountRow> Rows(
        IReadOnlyList<HostAccount> accounts, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest)
    {
        var sending = SendingRecipes(installed);
        return [.. accounts.Select(account => new AccountRow(
            account.AccountId,
            account.DisplayName,
            FoundIn(account, installed, sources, latest),
            [.. sending.Select(r => new SendTick
            {
                RecipeSlug = r.Recipe.Slug,
                AccountId = account.AccountId,
                Name = $"Send {account.DisplayName} for {r.Recipe.Name}",
                On = !r.State.Excluded.Contains(account.AccountId),
            })]))];
    }

    /// <summary>The main and mine sources whose last read had this account, main first; else "Not in a watched clan".</summary>
    public static string FoundIn(
        HostAccount account, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest)
    {
        var withInputs = installed.Where(SetupPages.HasClansPage).ToList();
        if (withInputs.Count == 0) return "";
        if (account.RobloxUserId == 0) return "Not matched by RoRoRo yet";

        var names = new List<string>();
        foreach (var source in sources.Where(s => s.Enabled && s.Role != SourceRole.Watch).OrderBy(s => s.Role == SourceRole.Main ? 0 : 1))
        {
            if (withInputs.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;
            if (latest.GetValueOrDefault(source.Id)?.Rows is not { } rows || rows.All(r => r.UserId != account.RobloxUserId)) continue;

            var name = ClansModel.NameOf(recipe, source);
            names.Add(source.Role == SourceRole.Main ? $"★ {name}" : name);
        }

        return names.Count > 0
            ? string.Join(", ", names.Distinct(StringComparer.Ordinal))
            : $"Not in a watched {RecipeWords.Group(withInputs[0].Recipe)}";
    }

    /// <summary>
    /// Stats design §5.3, unchanged: a Send tick that would pass RoRoRo's history limit is refused. Entries
    /// for accounts RoRoRo isn't listing right now are kept as they are.
    /// </summary>
    public static SendChange ToggleSend(
        InstalledRecipe recipe, IReadOnlyList<InstalledRecipe> installed, IReadOnlyCollection<Guid> accountIds, Guid accountId, bool on)
    {
        var excluded = recipe.State.Excluded.ToHashSet();
        var sentStats = recipe.State.SentStats(recipe.Recipe).Count;
        var before = accountIds.Count(id => !excluded.Contains(id));

        if (on) excluded.Remove(accountId);
        else excluded.Add(accountId);

        var after = accountIds.Count(id => !excluded.Contains(id));

        if (on)
        {
            var budget = HistoryBudget.Check(
                HistoryBudget.Installed(installed, accountIds, exceptSlug: recipe.Recipe.Slug),
                (before, sentStats), (after, sentStats), accountsKnown: true);
            if (!budget.Allowed) return new SendChange(recipe.State, budget.Line);
        }

        var kept = (recipe.State.ExcludedAccountIds ?? [])
            .Where(id => !Guid.TryParse(id, out var parsed) || parsed != accountId)
            .ToList();
        if (!on) kept.Add(accountId.ToString());

        return new SendChange(recipe.State with { ExcludedAccountIds = kept }, null);
    }

    public static string ListedLine(AccountList? last, DateTimeOffset? savedAt, DateTimeOffset now)
    {
        const string Lead = "Accounts come from RoRoRo.";

        if (last is { Denied: true })
        {
            return $"{Lead} RoRoRo refused to list them: host.queries.accounts is not granted. "
                   + "Remove Ur Score from RoRoRo's Plugins page and reinstall it to be asked again.";
        }

        var listed = last?.ListedAt ?? savedAt;
        if (listed is null) return $"{Lead} RoRoRo hasn't listed them yet.";

        return last is { FromCache: true }
            ? $"{Lead} RoRoRo isn't answering, so these are the ones it listed {Ago(listed.Value, now)}."
            : $"{Lead} Last listed {Ago(listed.Value, now)}.";
    }

    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromHours(48)) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} days ago";
    }
}
```

Create `src/UI/Setup/RecipesModel.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record RecipeItem(string Slug, string Name, string Hosts, string Every, string Sources, string? IconFile)
{
    public string RemoveName => $"Remove {Name}";

    public bool HasIcon => IconFile is not null;

    public string Details => Sources.Length == 0 ? Every : $"{Every} · {Sources}";
}

/// <summary>Setup › Recipes (spec §7.4).</summary>
public static class RecipesModel
{
    public const string KeepsBook = "Removing a recipe never deletes its score book.";

    public static IReadOnlyList<RecipeItem> Items(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, Func<string, string?> iconFile) =>
        [.. installed.Select(i => new RecipeItem(
            i.Recipe.Slug,
            i.Recipe.Name,
            string.Join(", ", RecipeHosts.ContactedBy(i.Recipe).Order(StringComparer.Ordinal)),
            $"Asks every {i.Recipe.EffectiveEverySeconds} s",
            SourcesText(i.Recipe, sources),
            iconFile(i.Recipe.Slug)))];

    public static string SourcesText(Recipe recipe, IReadOnlyList<Source> sources)
    {
        if (recipe.IsGroupList) return "Shown live, never kept";
        if (recipe.Inputs.Count == 0) return "";

        var count = sources.Count(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        return count switch
        {
            0 => $"No {RecipeWords.GroupsLower(recipe)} yet",
            1 => $"1 {RecipeWords.Group(recipe)}",
            _ => $"{count} {RecipeWords.GroupsLower(recipe)}",
        };
    }

    public static string ConfirmRemove(Recipe recipe) =>
        $"Remove {recipe.Name}? It stops being read. Its score book stays on this PC, so importing it again carries on where it left off.";
}
```

Create `src/UI/Setup/AlertsModel.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>One sent stat in the Rule for list. Its text is its string, so a screen reader names the pick.</summary>
public sealed record RuleChoice(string MetricId, string Text)
{
    public override string ToString() => Text;
}

public sealed record PolicyItem(string RecipeName, string Line, string Counts);

/// <summary>
/// Setup › Alerts (spec §7.5): the rule card and the report policy card, moved from the retired main window
/// with their behaviour unchanged. The policy line is <see cref="ReportPolicy.Describe"/>'s own sentence.
/// </summary>
public static class AlertsModel
{
    public const double DefaultThreshold = 100;

    public const int DefaultWindowMinutes = 10;

    public const string MetricAlertsOff =
        "RoRoRo's metric alerts are off by default. A rule is not enough on its own — turn Metric alerts on in RoRoRo's Settings too, or a perfectly matching rule will still never ring your phone.";

    public const string NoSentStat = "No stat is set to send. Tick Send on a stat in Setup › Stats, then add its rule here.";

    public const string NoRecipe = "Import a recipe first.";

    /// <summary>Every sent stat across installed recipes, once per metric id.</summary>
    public static IReadOnlyList<RuleChoice> Choices(IReadOnlyList<InstalledRecipe> installed) =>
        [.. installed
            .Where(i => !i.Recipe.IsGroupList)
            .SelectMany(i => i.State.SentStats(i.Recipe))
            .GroupBy(s => s.MetricId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(s => new RuleChoice(s.MetricId, $"{s.Label} ({s.MetricId})"))];

    /// <summary>What RoRoRo's rules file holds for a metric id, and whether the helper can add one.</summary>
    public static (string Text, bool CanAdd) RuleSentence(string metricId, string? rulesPath = null, string? inventoryPath = null)
    {
        var status = RulesFile.Inspect(rulesPath, metricId);
        var recorded = RuleInventory.Recorded(metricId, inventoryPath);

        return status.State switch
        {
            RuleState.NoFile =>
                ("RoRoRo has no rules file yet, so nothing can alert until a rule is added. Adding one creates the file.", true),
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

    public static string Preview(bool canAdd) =>
        canAdd ? $"Rate, below {DefaultThreshold} per minute over {DefaultWindowMinutes} minutes" : "";

    /// <summary>One report policy card line per sending recipe, in <see cref="ReportPolicy.Describe"/>'s words.</summary>
    public static IReadOnlyList<PolicyItem> Policies(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<HostAccount> accounts, bool resolveNames,
        Func<string, (int Sent, int Dropped)> counts) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList).Select(i =>
        {
            var allowed = accounts.Select(a => a.AccountId).Where(id => !i.State.Excluded.Contains(id)).ToHashSet();
            var (sent, dropped) = counts(i.Recipe.Slug);
            return new PolicyItem(
                i.Recipe.Name,
                new ReportPolicy(i.State.SentStats(i.Recipe), allowed).Describe(accounts.Count, resolveNames),
                $"Sent {sent}, dropped {dropped} this session.");
        })];
}
```

Create `src/UI/Setup/ScoreBookModel.cs`

```csharp
using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record BookRecipeItem(string Name, string Readings, string First, string Finals, string Size)
{
    public string Summary => string.Join(" · ", new[] { Readings, First, Finals, Size }.Where(part => part.Length > 0));
}

public sealed record NotRecordingItem(string Source, string Reason);

/// <summary>Setup › Score book (spec §7.6). Counts only; no line of the book is ever shown or copied.</summary>
public static class ScoreBookModel
{
    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes"
        : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : (bytes / 1048576.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";

    public static string SourceLabel(Recipe recipe, Source source) =>
        recipe.Inputs.Count == 0 ? recipe.Name : $"{ClansModel.NameOf(recipe, source)} · {recipe.Name}";

    public static IReadOnlyList<BookRecipeItem> Recipes(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, ScoreBookReader reader) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList).Select(i =>
        {
            var recipe = i.Recipe;
            var slug = recipe.Slug;
            var readings = reader.Readings(slug);
            var first = reader.FirstReading(slug);
            var finals = sources
                .Where(s => string.Equals(s.Recipe, slug, StringComparison.Ordinal))
                .Select(s => s.InputsKey)
                .Distinct(StringComparer.Ordinal)
                .Sum(key => reader.Finals(slug, key).Select(f => f.Period).Distinct(StringComparer.Ordinal).Count());

            return new BookRecipeItem(
                recipe.Name,
                readings == 1 ? "1 reading kept" : $"{readings.ToString("N0", CultureInfo.InvariantCulture)} readings kept",
                first is { } at ? $"First reading {at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}" : "No reading yet",
                recipe.Period is null ? ""
                    : finals == 1 ? $"1 finished {RecipeWords.Period(recipe)} kept"
                    : $"{finals.ToString("N0", CultureInfo.InvariantCulture)} finished {RecipeWords.Periods(recipe)} kept",
                Size(reader.Bytes(slug)));
        })];

    /// <summary>Every source that isn't recording right now, and why (spec §7.6). Group lists never record and are not listed.</summary>
    public static IReadOnlyList<NotRecordingItem> NotRecording(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest,
        bool running, bool accountsEverListed)
    {
        var items = new List<NotRecordingItem>();

        if (!accountsEverListed && sources.Any(s => s.Enabled && s.Role != SourceRole.Watch))
        {
            items.Add(new NotRecordingItem("Your accounts",
                "RoRoRo has never listed your accounts, so nothing per account is kept yet. Start RoRoRo while Ur Score runs."));
        }

        foreach (var source in sources)
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe
                || recipe.IsGroupList)
            {
                continue;
            }

            var snapshot = latest.GetValueOrDefault(source.Id);
            var reason =
                !source.Enabled ? "Switched off."
                : !running ? "Stopped. Press Start on the board."
                : snapshot is null ? "Not read yet."
                : snapshot.Recorded ? null
                : snapshot.NotRecordingReason ?? "The last read kept nothing.";

            if (reason is not null) items.Add(new NotRecordingItem(SourceLabel(recipe, source), reason));
        }

        return items;
    }
}
```

Create `src/UI/Setup/DiagnosticsModel.cs`

```csharp
using System.Text;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record SourceDiagnostic(string SourceId, string Name, string State, string Detail, string LastRead, string NextRead, string Misses)
{
    public string Timing => $"Last read {LastRead} · next read {NextRead}";

    public bool HasDetail => Detail.Length > 0;

    public bool HasMisses => Misses.Length > 0;
}

/// <summary>Setup › Diagnostics (spec §7.7). Copy diagnostics carries counts about the book, never its lines.</summary>
public static class DiagnosticsModel
{
    public const int TrailLines = 40;

    public static string StateText(WatchState state) => state switch
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
        WatchState.Showing => "Reading. No stat is set to send to RoRoRo.",
        _ => state.ToString(),
    };

    public static IReadOnlyList<SourceDiagnostic> Sources(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest,
        Func<string, DateTimeOffset?> lastRead, bool running, IReadOnlyList<HostAccount> accounts, DateTimeOffset now, Redactor redactor)
    {
        var rows = new List<SourceDiagnostic>();

        foreach (var source in sources)
        {
            var recipe = installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe;
            var snapshot = latest.GetValueOrDefault(source.Id);
            var last = lastRead(source.Id);

            var state = !source.Enabled ? "Switched off."
                : snapshot is null ? (running ? "Waiting for its first read." : "Not started.")
                : StateText(snapshot.State);

            string next;
            if (!source.Enabled || recipe is null) next = StatText.Dash;
            else if (!running) next = "when you press Start";
            else if (last is null) next = "soon";
            else
            {
                var due = last.Value.AddSeconds(recipe.EffectiveEverySeconds);
                next = due <= now ? "due now" : $"in {StatText.Span(due - now)}";
            }

            rows.Add(new SourceDiagnostic(
                source.Id,
                recipe is null ? $"{source.Recipe} (not installed)" : ScoreBookModel.SourceLabel(recipe, source),
                state,
                redactor.Redact(snapshot?.Detail),
                last is null ? "never" : $"{StatText.Span(now - last.Value)} ago",
                next,
                redactor.Redact(Misses(recipe, snapshot, accounts))));
        }

        return rows;
    }

    /// <summary>Stat-wide misses, then each of your accounts' cell misses by display name. Another row's id is never named.</summary>
    public static string Misses(Recipe? recipe, RecipeSnapshot? snapshot, IReadOnlyList<HostAccount> accounts)
    {
        if (snapshot is null) return "";

        var parts = new List<string>();
        foreach (var (key, miss) in snapshot.StatMisses)
        {
            parts.Add($"{Label(recipe, key)}: {miss}");
        }

        foreach (var ((userId, stat), reason) in snapshot.CellMisses)
        {
            if (accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName is not { } name) continue;
            parts.Add($"{name} · {Label(recipe, stat)}: {reason}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    public static string CopyText(
        DateTimeOffset now, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyList<SourceDiagnostic> rows,
        bool resolveNames, string hostText, string rawDirectory, string bookRoot, int bookPending, int bookDropped,
        IReadOnlyList<string> trail, Redactor redactor)
    {
        // global:: because this namespace's Source alias hides the Labs626.UrScore.Source namespace; outside the
        // interpolation, since a colon inside a hole starts a format.
        var userAgent = global::Labs626.UrScore.Source.UrScoreIdentity.UserAgent;
        var text = new StringBuilder()
            .AppendLine($"Ur Score diagnostics {now:O}")
            .AppendLine($"user-agent={userAgent} resolveNames={resolveNames}")
            .AppendLine(hostText)
            .AppendLine($"raw responses kept in {rawDirectory}")
            .AppendLine($"score book in {bookRoot}: pending={bookPending} dropped={bookDropped} (no book content is included)");

        foreach (var item in installed)
        {
            var sent = string.Join(", ", item.State.SentStats(item.Recipe).Select(s => $"{s.Key}->{s.MetricId}"));
            var shown = string.Join(", ", item.State.ShownStats(item.Recipe).Select(s => s.Key));
            text.AppendLine($"recipe={item.Recipe.Slug} poll={item.Recipe.EffectiveEverySeconds}s groupList={item.Recipe.IsGroupList} "
                            + $"sent={(sent.Length == 0 ? "(none)" : sent)} shown={(shown.Length == 0 ? "(none)" : shown)}");
        }

        foreach (var source in sources)
        {
            var inputs = source.Inputs.Count == 0 ? "(none)" : string.Join(", ", source.Inputs.Select(kv => $"{kv.Key}={kv.Value}"));
            text.AppendLine($"source={source.Id} recipe={source.Recipe} role={source.Role} enabled={source.Enabled} inputs={inputs}");

            if (rows.FirstOrDefault(r => r.SourceId == source.Id) is not { } row) continue;
            text.AppendLine($"  state={row.State} last={row.LastRead} next={row.NextRead}");
            if (row.HasDetail) text.AppendLine($"  detail={row.Detail}");
            if (row.HasMisses) text.AppendLine($"  misses={row.Misses.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}");
        }

        text.AppendLine().AppendLine(string.Join(Environment.NewLine, trail.TakeLast(TrailLines)));

        // Redacted as a whole, last, so nothing added above can carry a key out.
        return redactor.Redact(text.ToString());
    }

    private static string Label(Recipe? recipe, string key) =>
        recipe is not null && RecipeStats.Find(recipe, key) is { } stat ? stat.Label : key;
}
```

- [ ] **Step 4: Run the model tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~AccountsModelTests|FullyQualifiedName~RecipesModelTests|FullyQualifiedName~AlertsModelTests|FullyQualifiedName~ScoreBookModelTests|FullyQualifiedName~DiagnosticsModelTests"`
Expected: PASS, 40 tests.

- [ ] **Step 5: Create the import flow**

Create `src/UI/Setup/ImportFlow.cs`

```csharp
using System.IO;
using System.Windows;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Recipes;
using Microsoft.Win32;

namespace Labs626.UrScore.UI;

/// <summary>What an import did, and whether Setup should go on to that recipe's Clans page.</summary>
public sealed record ImportOutcome(string Slug, string Message, bool ChooseSources);

/// <summary>
/// Import recipe…, moved from the retired main window with the same rules (spec §6.3, stats design §7.2):
/// an invalid file is refused, a clash of names is refused, an identical file asks nothing, an update with
/// no new contacts is listed not asked, and anything else goes through the import screen.
/// </summary>
public static class ImportFlow
{
    public const string DialogTitle = "Import a recipe";

    public static async Task<ImportOutcome?> RunAsync(Window owner, ISetupServices services)
    {
        var dialog = new OpenFileDialog
        {
            Title = DialogTitle,
            Filter = "Ur Score recipe (*.recipe.json)|*.recipe.json|JSON file (*.json)|*.json",
        };

        if (dialog.ShowDialog(owner) != true) return null;

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            Warn(owner, $"Could not read that file: {ex.Message}");
            return null;
        }

        var parsed = RecipeParser.Parse(text);
        if (!parsed.Ok)
        {
            Warn(owner, "That recipe could not be imported:\n\n" + string.Join("\n", parsed.Problems.Select(p => "• " + p)));
            return null;
        }

        var recipe = parsed.Recipe!;
        var installed = services.Store.Find(recipe.Slug);

        if (installed is not null
            && (!string.Equals(installed.Recipe.Name, recipe.Name, StringComparison.Ordinal)
                || !string.Equals(installed.Recipe.Author, recipe.Author, StringComparison.Ordinal)))
        {
            Warn(owner, $"A different recipe, {installed.Recipe.Name}, is already installed under the same file name. Rename one of them before importing.");
            return null;
        }

        if (installed is not null && string.Equals(installed.Text, text, StringComparison.Ordinal))
        {
            // Spec §6.3: an identical file imports without asking.
            return Outcome(services, recipe, $"{recipe.Name} is already installed.");
        }

        var review = ImportReview.Review(recipe, services.Keys);
        var comparison = ImportReview.CompareToInstalled(installed?.Recipe, recipe, services.Keys, installed?.State);

        try
        {
            if (installed is not null && review.CanImport && !comparison.AsksAgain)
            {
                // An update that contacts the same hosts with the same things: listed, not asked.
                services.Store.Save(recipe, text, installed.State);
                services.ReloadRecipes();
                return Outcome(services, recipe, $"Updated {recipe.Name}. {string.Join(" ", comparison.Changes)}".Trim());
            }

            // The history budget counts RoRoRo's accounts, so they are asked for before the screen that checks it.
            try
            {
                await services.RefreshAccountsAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The budget then counts the accounts already known.
            }

            var window = new ImportWindow(
                recipe, review, comparison, installed?.State, services.Installed,
                () => [.. services.KnownAccounts.Select(a => a.AccountId)],
                metricId => AlertsModel.RuleSentence(metricId).Text,
                recipe.LastStep.Counters is null ? null : _ => services.ReadCounterNamesAsync(recipe, CancellationToken.None))
            {
                Owner = owner,
            };

            if (window.ShowDialog() != true) return null;

            var state = (installed?.State ?? new RecipeState()) with
            {
                Stats = window.Stats,
                CounterNames = window.CounterNames,
            };

            services.Store.Save(recipe, text, state);
            services.ReloadRecipes();
            return Outcome(services, recipe, $"Imported {recipe.Name}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, services.Redactor.Redact($"Could not save that recipe: {ex.Message}"), "Ur Score",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>A recipe with inputs and no sources goes on to its Clans page, where the search replaces a plain text box.</summary>
    private static ImportOutcome Outcome(ISetupServices services, Recipe recipe, string message)
    {
        var installed = services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, recipe.Slug, StringComparison.Ordinal));
        var choose = installed is not null
                     && SetupPages.HasClansPage(installed)
                     && services.Sources.All(s => !string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        return new ImportOutcome(recipe.Slug, message, choose);
    }

    private static void Warn(Window owner, string text) =>
        MessageBox.Show(owner, text, "Ur Score", MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

- [ ] **Step 6: Create the pages**

Create `src/UI/Setup/AccountsPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.AccountsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel MaxWidth="900" HorizontalAlignment="Left">
        <TextBlock Text="Your accounts" Style="{StaticResource Heading}" />
        <TextBlock x:Name="ListedLine" Style="{StaticResource Muted}" Margin="0,4,0,4" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,0,0,12"
                   Text="Send reports an account's sent stats to RoRoRo. Reading and keeping your scores happen either way." />
        <TextBlock x:Name="AccountsBudgetLine" Style="{StaticResource Refusal}" Margin="0,0,0,10" Visibility="Collapsed" />

        <Grid Margin="10,0,10,6">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="Account" Style="{StaticResource Muted}" />
            <TextBlock Grid.Column="1" Text="Found in" Style="{StaticResource Muted}" />
            <ItemsControl x:Name="RecipeHeaders" Grid.Column="2">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding}" Width="160" Style="{StaticResource Muted}" TextTrimming="CharacterEllipsis" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Grid>

        <ItemsControl x:Name="AccountsTable" AutomationProperties.Name="Your accounts, where each was found, and Send for each recipe">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource Card}" Padding="10,7" Margin="0,0,0,6">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="220" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="{Binding DisplayName}" FontWeight="SemiBold" VerticalAlignment="Center"
                                       TextTrimming="CharacterEllipsis" />
                            <TextBlock Grid.Column="1" Text="{Binding FoundIn}" Style="{StaticResource Muted}" VerticalAlignment="Center" />
                            <ItemsControl Grid.Column="2" ItemsSource="{Binding Sends}">
                                <ItemsControl.ItemsPanel>
                                    <ItemsPanelTemplate>
                                        <StackPanel Orientation="Horizontal" />
                                    </ItemsPanelTemplate>
                                </ItemsControl.ItemsPanel>
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <CheckBox IsChecked="{Binding On}" Content="Send" Width="160" VerticalAlignment="Center"
                                                  AutomationProperties.Name="{Binding Name}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock x:Name="AccountsEmptyLine" Style="{StaticResource Muted}" Visibility="Collapsed" />
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/AccountsPage.xaml.cs`

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Your accounts (spec §7.2): Send per account per recipe, and where each account was found.</summary>
public partial class AccountsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<AccountRow> _rows = [];

    /// <summary>Why the last Send tick was undone, kept on screen until the next tick.</summary>
    private string? _refusal;

    private bool _reverting;

    public AccountsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
        _ = AskForAccountsAsync();
    }

    public void Refresh()
    {
        ListedLine.Text = AccountsModel.ListedLine(_services.Accounts.Last, _services.AccountsCache.SavedAt(), DateTimeOffset.UtcNow);

        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged -= OnTick;
        var accounts = _services.KnownAccounts;
        _rows = AccountsModel.Rows(accounts, _services.Installed, _services.Sources, _services.Latest);
        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged += OnTick;

        RecipeHeaders.ItemsSource = AccountsModel.SendingRecipes(_services.Installed).Select(r => r.Recipe.Name).ToList();
        AccountsTable.ItemsSource = _rows;
        Show(AccountsEmptyLine, accounts.Count == 0 ? "RoRoRo hasn't shared any accounts yet. Start RoRoRo and add your accounts there." : "");
        Show(AccountsBudgetLine, _refusal ?? _services.BudgetWarning ?? "");
    }

    private async Task AskForAccountsAsync()
    {
        try
        {
            await _services.RefreshAccountsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Show(AccountsBudgetLine, _services.Redactor.Redact($"Could not ask RoRoRo for your accounts: {ex.Message}"));
        }
    }

    private void OnTick(object? sender, PropertyChangedEventArgs e)
    {
        if (_reverting || sender is not SendTick tick) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, tick.RecipeSlug, StringComparison.Ordinal)) is not { } installed) return;

        var ids = _services.KnownAccounts.Select(a => a.AccountId).ToList();
        var change = AccountsModel.ToggleSend(installed, _services.Installed, ids, tick.AccountId, tick.On);

        // Deferred: saving re-renders the rows, and a row must not be replaced while its checkbox is mid-click.
        Dispatcher.BeginInvoke(() =>
        {
            if (change.Refusal is not null)
            {
                _refusal = change.Refusal;
                _reverting = true;
                tick.On = false;
                _reverting = false;
                Show(AccountsBudgetLine, _refusal);
                return;
            }

            _refusal = null;
            try
            {
                _services.SaveRecipeState(installed.Recipe, change.State);
            }
            catch (Exception ex)
            {
                _refusal = _services.Redactor.Redact($"Could not save that change: {ex.Message}");
                Show(AccountsBudgetLine, _refusal);
            }
        }, DispatcherPriority.Background);
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

Create `src/UI/Setup/StatsPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.StatsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <StackPanel MaxWidth="820" HorizontalAlignment="Left">
        <TextBlock Text="Stats" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="Every stat you tick is read, kept in your score book for your own accounts, and shown on your board." />
        <StackPanel x:Name="RecipeRow" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Recipe" VerticalAlignment="Center" Margin="0,0,8,0" />
            <ComboBox x:Name="StatsRecipeBox" MinWidth="320" SelectionChanged="OnRecipeChanged" AutomationProperties.Name="Recipe" />
        </StackPanel>
        <TextBlock x:Name="StatsGroupListLine" Style="{StaticResource Muted}" Margin="0,0,0,12" Visibility="Collapsed" />
        <StackPanel x:Name="StatsBody">
            <ui:StatsTable x:Name="StatsTable" />
            <StackPanel Orientation="Horizontal" Margin="0,16,0,0">
                <Button x:Name="SaveStatsButton" Content="Save stats" Style="{StaticResource PrimaryButton}" Click="OnSaveClick" />
                <TextBlock x:Name="StatsSavedLine" Style="{StaticResource Muted}" VerticalAlignment="Center" Margin="12,0,0,0" />
            </StackPanel>
        </StackPanel>
        <TextBlock x:Name="StatsEmptyLine" Style="{StaticResource Muted}" Visibility="Collapsed"
                   Text="No recipe with stats to choose yet. Import one in Recipes." />
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/StatsPage.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>A recipe in the Stats page's list. Its text is its name.</summary>
public sealed record RecipeChoice(string Slug, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Setup › Stats (spec §7.3): one Stats table per recipe, saved to the recipe's state and applied to its watches.</summary>
public partial class StatsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly CancellationTokenSource _closing = new();
    private IReadOnlyList<RecipeChoice> _choices = [];
    private string? _slug;

    public StatsPage(ISetupServices services, string? recipeSlug = null)
    {
        InitializeComponent();
        _services = services;
        _slug = recipeSlug;
        StatsTable.Changed += (_, _) => SaveStatsButton.IsEnabled = StatsTable.AnyTicked;
        Unloaded += (_, _) => _closing.Cancel();
        Refresh();
    }

    public void Refresh()
    {
        var groupLists = _services.Installed.Where(i => i.Recipe.IsGroupList).Select(i => i.Recipe.Name).ToList();
        Show(StatsGroupListLine, groupLists.Count == 0 ? ""
            : $"{string.Join(", ", groupLists)} {(groupLists.Count == 1 ? "has" : "have")} no stats to tick. Group rows are shown live on the board.");

        var choices = _services.Installed
            .Where(i => !i.Recipe.IsGroupList)
            .Select(i => new RecipeChoice(i.Recipe.Slug, i.Recipe.Name))
            .ToList();

        // Rebuilt only when the recipe list changes, so a snapshot arriving never resets ticks being made.
        if (choices.SequenceEqual(_choices)) return;
        _choices = choices;

        var any = choices.Count > 0;
        RecipeRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        StatsBody.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        StatsEmptyLine.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        StatsRecipeBox.ItemsSource = choices;
        StatsRecipeBox.SelectedItem = choices.FirstOrDefault(c => c.Slug == _slug) ?? choices.FirstOrDefault();
    }

    private void OnRecipeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatsRecipeBox.SelectedItem is not RecipeChoice choice) return;
        _slug = choice.Slug;
        Load();
    }

    private InstalledRecipe? Current =>
        _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _slug, StringComparison.Ordinal));

    private void Load()
    {
        if (Current is not { } installed) return;

        var recipe = installed.Recipe;
        StatsTable.Load(
            recipe, installed.State, _services.Installed,
            () => [.. _services.KnownAccounts.Select(a => a.AccountId)],
            metricId => AlertsModel.RuleSentence(metricId).Text,
            recipe.LastStep.Counters is null ? null : ct => _services.ReadCounterNamesAsync(recipe, ct),
            "Read stat names again");

        Show(StatsSavedLine, "");
        SaveStatsButton.IsEnabled = StatsTable.AnyTicked;

        // Spec §7.3: no saved names and a recipe with counters means one read when the page opens.
        if (recipe.LastStep.Counters is not null && !StatsTable.HasSavedNames) _ = StatsTable.ReadNamesAsync(_closing.Token);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Current is not { } installed) return;

        var problems = StatsTable.SaveProblems();
        if (problems.Count > 0)
        {
            StatsTable.ShowProblems(problems);
            return;
        }

        try
        {
            _services.SaveRecipeState(installed.Recipe, installed.State with
            {
                Stats = StatsTable.Choices,
                CounterNames = StatsTable.CounterNames,
            });
            Load();
            Show(StatsSavedLine, "Saved. The next read asks for these stats.");
        }
        catch (Exception ex)
        {
            Show(StatsSavedLine, _services.Redactor.Redact($"Could not save those stats: {ex.Message}"));
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

Create `src/UI/Setup/RecipesPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.RecipesPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel MaxWidth="820" HorizontalAlignment="Left">
        <TextBlock Text="Recipes" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="A recipe says where numbers are. Removing a recipe never deletes its score book." />
        <ItemsControl x:Name="RecipesList" AutomationProperties.Name="Installed recipes">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource Card}" Padding="12,10" Margin="0,0,0,8">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <Image Grid.Column="0" Width="32" Height="32" Margin="0,0,12,0" VerticalAlignment="Top"
                                   Source="{Binding IconFile}"
                                   Visibility="{Binding HasIcon, Converter={StaticResource BoolToVisible}}"
                                   AutomationProperties.Name="Recipe icon" />
                            <StackPanel Grid.Column="1">
                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Hosts}" FontFamily="{StaticResource MonoFont}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                                <TextBlock Text="{Binding Details}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                            </StackPanel>
                            <Button Grid.Column="2" Content="Remove" Tag="{Binding Slug}" Click="OnRemoveClick" VerticalAlignment="Center"
                                    AutomationProperties.Name="{Binding RemoveName}" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock x:Name="RecipesEmptyLine" Style="{StaticResource Muted}" Margin="0,0,0,8" Visibility="Collapsed"
                   Text="No recipes yet. Import one to start." />
        <TextBlock x:Name="RecipeProblemsLine" Style="{StaticResource Refusal}" Margin="0,0,0,8" Visibility="Collapsed" />
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
            <Button x:Name="ImportRecipeButton" Content="Import recipe…" Style="{StaticResource PrimaryButton}" Click="OnImportClick"
                    AutomationProperties.Name="Import a recipe file" />
            <TextBlock x:Name="RecipesLine" Style="{StaticResource Muted}" VerticalAlignment="Center" Margin="12,0,0,0" />
        </StackPanel>
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/RecipesPage.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Recipes (spec §7.4): the installed list, Remove, and Import recipe….</summary>
public partial class RecipesPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly SetupWindow _window;
    private bool _importing;

    public RecipesPage(ISetupServices services, SetupWindow window)
    {
        InitializeComponent();
        _services = services;
        _window = window;
        Refresh();
    }

    public void Refresh()
    {
        RecipesList.ItemsSource = RecipesModel.Items(_services.Installed, _services.Sources, _services.IconFileFor);
        RecipesEmptyLine.Visibility = _services.Installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Show(RecipeProblemsLine, _services.RecipeProblems.Count == 0
            ? ""
            : "Some recipe files could not be read: " + string.Join(" | ", _services.RecipeProblems));
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        // One import screen at a time, however long the accounts wait lasts.
        if (_importing) return;
        _importing = true;
        ImportRecipeButton.IsEnabled = false;

        try
        {
            var outcome = await ImportFlow.RunAsync(_window, _services);
            if (outcome is null) return;

            Show(RecipesLine, outcome.Message);
            if (outcome.ChooseSources) _window.ShowPage(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _importing = false;
            ImportRecipeButton.IsEnabled = true;
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slug }) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal))?.Recipe is not { } recipe) return;

        var answer = MessageBox.Show(_window, RecipesModel.ConfirmRemove(recipe), "Ur Score",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            _services.RemoveRecipe(slug);
            Show(RecipesLine, $"Removed {recipe.Name}. {RecipesModel.KeepsBook}");
        }
        catch (Exception ex)
        {
            Show(RecipesLine, _services.Redactor.Redact($"Could not remove it: {ex.Message}"));
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

Create `src/UI/Setup/AlertsPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.AlertsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <StackPanel MaxWidth="820" HorizontalAlignment="Left">
        <TextBlock Text="Alerts" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="RoRoRo does the judging. Ur Score hands it the stats you send, and can add a starting rule for one." />

        <!-- The rule in RoRoRo, for one sent stat at a time, and the click that adds it. -->
        <Border Style="{StaticResource Card}" Padding="12,10" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="ALERT RULE" Style="{StaticResource SectionLabel}" />
                <TextBlock Style="{StaticResource Muted}" FontStyle="Italic" Margin="0,0,0,6"
                           Text="{x:Static ui:AlertsModel.MetricAlertsOff}" />
                <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                    <TextBlock Text="Rule for" VerticalAlignment="Center" Margin="0,0,8,0" />
                    <ComboBox x:Name="RuleStatBox" MinWidth="280" DisplayMemberPath="Text"
                              SelectionChanged="OnRuleStatChanged" AutomationProperties.Name="Rule for" />
                </StackPanel>
                <TextBlock x:Name="RuleLine" TextWrapping="Wrap" />
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <Button x:Name="AddRuleButton" Content="Add this rule to RoRoRo"
                            Click="OnAddRuleClick" AutomationProperties.Name="Add this rule to RoRoRo" />
                    <TextBlock x:Name="RulePreview" Margin="12,0,0,0" FontFamily="{StaticResource MonoFont}"
                               Style="{StaticResource Muted}" VerticalAlignment="Center" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- The report policy, in the words ReportPolicy.Describe itself composes (F6), one card line per recipe. -->
        <Border Style="{StaticResource Card}" Padding="12,10">
            <StackPanel>
                <TextBlock Text="REPORT POLICY" Style="{StaticResource SectionLabel}" />
                <ItemsControl x:Name="PolicyList" AutomationProperties.Name="What each recipe sends to RoRoRo">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,10">
                                <TextBlock Text="{Binding RecipeName}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Line}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                                <TextBlock Text="{Binding Counts}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock x:Name="PolicyEmptyLine" Style="{StaticResource Muted}" Visibility="Collapsed"
                           Text="No recipe, so nothing is sent to RoRoRo." />
            </StackPanel>
        </Border>
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/AlertsPage.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Alerts (spec §7.5): the rule helper and the report policy, as the main window had them.</summary>
public partial class AlertsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<RuleChoice> _choices = [];

    public AlertsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    private string? RuleMetricId => (RuleStatBox.SelectedItem as RuleChoice)?.MetricId;

    public void Refresh()
    {
        var choices = AlertsModel.Choices(_services.Installed);
        if (!choices.SequenceEqual(_choices))
        {
            var picked = RuleMetricId;
            _choices = choices;
            RuleStatBox.ItemsSource = choices;
            RuleStatBox.SelectedItem = choices.FirstOrDefault(c => c.MetricId == picked) ?? choices.FirstOrDefault();
        }

        RuleStatBox.IsEnabled = choices.Count > 0;
        RenderRule();

        var policies = AlertsModel.Policies(_services.Installed, _services.KnownAccounts, _services.Settings.ResolveNames, _services.PolicyCounts);
        PolicyList.ItemsSource = policies;
        PolicyEmptyLine.Visibility = policies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRuleStatChanged(object sender, SelectionChangedEventArgs e) => RenderRule();

    private void RenderRule()
    {
        if (_services.Installed.Count == 0)
        {
            RuleLine.Text = AlertsModel.NoRecipe;
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        if (RuleMetricId is not { } metricId)
        {
            RuleLine.Text = AlertsModel.NoSentStat;
            AddRuleButton.IsEnabled = false;
            RulePreview.Text = "";
            return;
        }

        var (text, canAdd) = AlertsModel.RuleSentence(metricId);
        RuleLine.Text = text;
        AddRuleButton.IsEnabled = canAdd;
        RulePreview.Text = AlertsModel.Preview(canAdd);
    }

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (RuleMetricId is not { } metricId) return;
        var owner = Window.GetWindow(this)!;

        var preview = RulesFile.Preview(metricId, AlertsModel.DefaultThreshold, AlertsModel.DefaultWindowMinutes);
        var answer = MessageBox.Show(owner,
            $"Add this rule to RoRoRo's metric-rules.json?\n\n{preview}\n\n"
            + "Your existing rules are kept, and the file is backed up first.",
            "Ur Score", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        try
        {
            if (RulesFile.AddRule(null, metricId, AlertsModel.DefaultThreshold, AlertsModel.DefaultWindowMinutes))
            {
                RuleInventory.Record(metricId, AlertsModel.DefaultThreshold);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not add the rule: {ex.Message}", "Ur Score", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RenderRule();
    }
}
```

Create `src/UI/Setup/ScoreBookPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.ScoreBookPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel MaxWidth="820" HorizontalAlignment="Left">
        <TextBlock Text="Score book" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="Every successful read is kept on this PC: your own accounts' numbers and each source's headline. Nothing is thinned or deleted, and removing a recipe keeps its book." />

        <TextBlock Text="FOLDER" Style="{StaticResource SectionLabel}" />
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="BookFolderLine" FontFamily="{StaticResource MonoFont}" TextWrapping="Wrap" VerticalAlignment="Center" />
            <Button x:Name="OpenBookFolderButton" Grid.Column="1" Content="Open folder" Margin="12,0,0,0" Click="OnOpenFolderClick" />
        </Grid>
        <TextBlock x:Name="BookPendingLine" Style="{StaticResource Muted}" Margin="0,6,0,0" Visibility="Collapsed" />

        <TextBlock Text="PER RECIPE" Style="{StaticResource SectionLabel}" Margin="0,20,0,6" />
        <TextBlock x:Name="BookLoadingLine" Style="{StaticResource Muted}" Visibility="Collapsed" Text="Reading your score book…" />
        <ItemsControl x:Name="BookRecipesList" AutomationProperties.Name="What the score book keeps for each recipe">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource Card}" Padding="12,8" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Summary}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <TextBlock Text="NOT RECORDING" Style="{StaticResource SectionLabel}" Margin="0,20,0,6" />
        <ItemsControl x:Name="NotRecordingList" AutomationProperties.Name="Sources that are not recording, and why">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock TextWrapping="Wrap" Margin="0,0,0,6">
                        <Run Text="{Binding Source, Mode=OneWay}" FontWeight="SemiBold" />
                        <Run Text=" · " Foreground="{DynamicResource MutedTextBrush}" />
                        <Run Text="{Binding Reason, Mode=OneWay}" Foreground="{DynamicResource MutedTextBrush}" />
                    </TextBlock>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock x:Name="AllRecordingLine" Style="{StaticResource Muted}" Visibility="Collapsed" Text="Every source is recording." />
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/ScoreBookPage.xaml.cs`

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Score book (spec §7.6).</summary>
public partial class ScoreBookPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;

    public ScoreBookPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        BookFolderLine.Text = _services.Book.Root;

        var pending = _services.Book.Pending;
        var dropped = _services.Book.Dropped;
        Show(BookPendingLine, pending == 0 && dropped == 0
            ? ""
            : $"{pending} lines are waiting to be written, and {dropped} readings were dropped because the file couldn't be written.");

        BookLoadingLine.Visibility = _services.ReaderLoaded ? Visibility.Collapsed : Visibility.Visible;
        BookRecipesList.ItemsSource = _services.ReaderLoaded
            ? ScoreBookModel.Recipes(_services.Installed, _services.Sources, _services.Reader)
            : [];

        var everListed = _services.AccountsCache.SavedAt() is not null || _services.Accounts.Last is { Accounts.Count: > 0 };
        var items = ScoreBookModel.NotRecording(_services.Installed, _services.Sources, _services.Latest, _services.Running, everListed);
        NotRecordingList.ItemsSource = items;
        AllRecordingLine.Visibility = items.Count == 0 && _services.Sources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_services.Book.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_services.Book.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Show(BookPendingLine, $"Could not open the folder: {ex.Message}");
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

Create `src/UI/Setup/DiagnosticsPage.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.DiagnosticsPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel MaxWidth="860" HorizontalAlignment="Left">
        <TextBlock Text="Diagnostics" Style="{StaticResource Heading}" />
        <TextBlock Style="{StaticResource Muted}" Margin="0,4,0,14"
                   Text="What each source did last. Copy diagnostics puts this on the clipboard with keys hidden, and never includes your score book." />
        <ItemsControl x:Name="SourcesDiagnostics" AutomationProperties.Name="Each source's state">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource Card}" Padding="12,8" Margin="0,0,0,8">
                        <StackPanel>
                            <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding State}" Margin="0,2,0,0" TextWrapping="Wrap" />
                            <TextBlock Text="{Binding Detail}" Style="{StaticResource Muted}" Margin="0,2,0,0"
                                       Visibility="{Binding HasDetail, Converter={StaticResource BoolToVisible}}" />
                            <TextBlock Text="{Binding Timing}" FontFamily="{StaticResource MonoFont}" FontSize="11"
                                       Style="{StaticResource Muted}" Margin="0,4,0,0" />
                            <TextBlock Text="{Binding Misses}" Style="{StaticResource Muted}" Margin="0,4,0,0"
                                       Visibility="{Binding HasMisses, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <TextBlock x:Name="DiagnosticsEmptyLine" Style="{StaticResource Muted}" Visibility="Collapsed" Text="No sources yet." />
        <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
            <Button x:Name="CopyDiagnosticsButton" Content="Copy diagnostics" Click="OnCopyClick"
                    AutomationProperties.Name="Copy diagnostics to the clipboard" />
            <TextBlock x:Name="DiagnosticsLine" Style="{StaticResource Muted}" VerticalAlignment="Center" Margin="12,0,0,0" />
        </StackPanel>
    </StackPanel>
</UserControl>
```

Create `src/UI/Setup/DiagnosticsPage.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Diagnostics (spec §7.7).</summary>
public partial class DiagnosticsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<SourceDiagnostic> _rows = [];

    public DiagnosticsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
    }

    public void Refresh()
    {
        _rows = DiagnosticsModel.Sources(_services.Installed, _services.Sources, _services.Latest, _services.LastReadAt,
            _services.Running, _services.KnownAccounts, DateTimeOffset.UtcNow, _services.Redactor);
        SourcesDiagnostics.ItemsSource = _rows;
        DiagnosticsEmptyLine.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Refresh();
        var text = DiagnosticsModel.CopyText(DateTimeOffset.UtcNow, _services.Installed, _services.Sources, _rows,
            _services.Settings.ResolveNames, _services.HostText, _services.RawDirectory, _services.Book.Root,
            _services.Book.Pending, _services.Book.Dropped, _services.Trail, _services.Redactor);

        try
        {
            Clipboard.SetText(text);
            DiagnosticsLine.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception ex)
        {
            DiagnosticsLine.Text = $"Could not copy diagnostics: {ex.Message}";
        }
    }
}
```

- [ ] **Step 7: Wire the pages into the Setup window**

In `src/UI/SetupWindow.xaml.cs`, replace the `CreatePage` method with:

```csharp
    private FrameworkElement CreatePage(SetupPage page) => page.Id switch
    {
        _ when page.RecipeSlug is { } slug => new ClansPage(_services, slug),
        SetupPages.Accounts => new AccountsPage(_services),
        SetupPages.Stats => new StatsPage(_services),
        SetupPages.Recipes => new RecipesPage(_services, this),
        SetupPages.Alerts => new AlertsPage(_services),
        SetupPages.ScoreBook => new ScoreBookPage(_services),
        _ => new DiagnosticsPage(_services),
    };
```

- [ ] **Step 8: Build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: every test passes, including `ThemeFenceTests` (the pages use only `BoolToVisible`, `Card`, `Muted`, `Refusal`, `SectionLabel`, `Heading` and theme brushes) and `ReportPolicyTests` (no new caller of `ReportMetricAsync`).

- [ ] **Step 9: Commit**

```text
git add src/UI/Setup tests/AccountsModelTests.cs tests/RecipesModelTests.cs tests/AlertsModelTests.cs tests/ScoreBookModelTests.cs tests/DiagnosticsModelTests.cs src/UI/SetupWindow.xaml.cs
git commit -m "setup: accounts, stats, recipes, alerts, score book and diagnostics pages

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 13: The ten panels and their view-model builders

Spec §9.4–§9.6 and the approved mock. There are ten `UserControl` panels in `src/UI/Panels/`. Each renders a plain record built by a pure builder in `src/Board/PanelModels.cs`. A builder takes a `LiveBoard`, the `ScoreBookReader` (only for panels that read the book) and the panel's `PanelSettings`. The live snapshots, sources, recipes, your accounts, the clock and whether reading is running all come in through `LiveBoard`.

The panel rules:
- **Live only:** the Promotion check, Top and Live leaderboard builders take no reader at all, so they cannot touch the book, and a test pins that.
- **Would place** comes from `Records.WouldPlace`.
- **Missing values** sort last and never count as zero.
- **Change** text comes from `Records.Change`.
- **Overdue** comes from `Records.Overdue`, but only while reading is running and after a first read.
- **A removed source or stat** shows "This panel's clan was removed." (in the recipe's word) or "This panel's stat was removed."
- **Titles** use recipe words: "Clan standing", "Battle race", "Past battles", "Top of the battle".
- **Charts** are WPF `Polyline`s on a `Canvas` over a faint grid, painted only with theme brushes. The geometry is the pure `ChartGeometry`, with tests.

Every panel has a common `PanelFrame` header: title, subtitle, role chip, overdue mark, stale message, and the ⧉/⋯ tool buttons. The tools stay hidden in stage 1; stage 2 wires them. Nothing shows the panels until Task 14.

**Files:**
- Create: `src/Board/PanelText.cs`, `tests/PanelTextTests.cs`
- Create: `src/Board/ChartGeometry.cs`, `tests/ChartGeometryTests.cs`
- Create: `src/Board/PanelModels.cs`, `tests/BoardFixtures.cs`, `tests/PanelModelsTests.cs`
- Create: `src/UI/Controls/LineChart.cs`, `src/UI/Controls/ChartParts.cs`
- Create: `src/UI/Panels/PanelFrame.xaml(.cs)`
- Create the ten panels, each as `.xaml` + `.xaml.cs` in `src/UI/Panels/`: `StandingPanel`, `RacePanel`, `MyAccountsPanel`, `PromotionCheckPanel`, `AccountCardPanel`, `PastPeriodsPanel`, `RecordsPanel`, `TopPanel`, `ProfileStatPanel`, `LiveLeaderboardPanel`
- Create: `src/UI/Panels/PanelViews.cs`
- Modify: `src/App.xaml` (panel styles)

**Interfaces:**
- Consumes:
  - Task 1: `Recipe.Period`, `Recipe.IsGroupList`, `RecipeHeadline.Id`
  - Task 2: `ReadingPeriod`, `GroupRow`, `HeadlineValue.Id`/`Number`
  - Task 3: `Source`, `SourceRole`
  - Task 5: `BookLine` (tests only)
  - Task 6: `Ranking.Competition`, `RecipeSnapshot.Period`/`Groups`/`SourceId`
  - Task 9: `ScoreBookReader`, `SeriesPoint`, `FinalEntry`, `Records`, `AccountRecords`
  - Task 11: `RecipeWords`
  - `StatText.Abbrev`/`Span` (contract)
  - existing: `Leaderboard.Rank`, `StatText.Number`/`Dash`, `RecipeStats`, `HostAccount`, `AccountLine`
- Produces (namespace `Labs626.UrScore.Board`):
  - `enum PanelType { Standing, Race, MyAccounts, PromotionCheck, AccountCard, PastPeriods, Records, Top, ProfileStat, LiveLeaderboard }`
  - `sealed record PanelSettings(string Recipe = "", string? SourceId = null, IReadOnlyList<string>? SourceIds = null, string? ToSourceId = null, string? Stat = null, long? UserId = null)`
  - `sealed record PanelHead(string Title, string Subtitle = "", string Chip = "", bool Overdue = false, string? Stale = null, string Note = "")`
  - `sealed record LiveBoard(IReadOnlyList<Source> Sources, IReadOnlyList<InstalledRecipe> Installed, IReadOnlyDictionary<string, RecipeSnapshot> Snapshots, IReadOnlyDictionary<string, DateTimeOffset> LastRead, IReadOnlyList<HostAccount> Accounts, TimeProvider Time, bool Running)`
  - `static class PanelText`: `Ordinal`, `Full`, `Short`, `Signed`, `Chip`, `Ago`, `NextRead`, `PeriodLine`, `StaleSource`, `StaleStat`
  - `ChartPoint`, `ChartSeries`, `ChartLine`, `ChartGridLine`, `ChartLayout`, and `static class ChartGeometry` with `Layout(IReadOnlyList<ChartSeries>, double width, double height, bool fromZero, bool labels)`
  - `static class PanelModels`, with builders (models listed in Step 5):
    - `Standing(LiveBoard, ScoreBookReader, PanelSettings)`
    - `Race(LiveBoard, ScoreBookReader, PanelSettings)`
    - `MyAccounts(LiveBoard, ScoreBookReader, PanelSettings)`
    - `PromotionCheck(LiveBoard, PanelSettings)`
    - `AccountCard(LiveBoard, ScoreBookReader, PanelSettings)`
    - `PastPeriods(LiveBoard, ScoreBookReader, PanelSettings)`
    - `RecordsPanel(LiveBoard, ScoreBookReader, PanelSettings)`
    - `Top(LiveBoard, PanelSettings)`
    - `ProfileStat(LiveBoard, ScoreBookReader, PanelSettings)`
    - `LiveLeaderboard(LiveBoard, PanelSettings, IReadOnlyDictionary<long, string> names)`
    - `double? Gain(IReadOnlyList<SeriesPoint>)`
- Produces (namespace `Labs626.UrScore.UI`):
  - the ten panel controls, each with `void Render(<Model> model)`, and `PanelFrame`
  - `LineChart : Canvas`, with `Series`, `FromZero`, `ShowLabels` and `static string[] BrushKeys`
  - `LegendSwatch : Border`, `FillBar : Grid`
  - `static class PanelViews`, with members:
    - `FrameworkElement Create(PanelType)`
    - `void Render(FrameworkElement view, PanelSettings settings, LiveBoard live, ScoreBookReader reader, IReadOnlyDictionary<long, string> names)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PanelTextTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class PanelTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    [InlineData(2378, "2,378th")]
    public void OrdinalsReadAsPlaces(int place, string text) => Assert.Equal(text, PanelText.Ordinal(place));

    [Fact]
    public void AMissingValueIsADashNeverAZero()
    {
        Assert.Equal(StatText.Dash, PanelText.Full(null));
        Assert.Equal(StatText.Dash, PanelText.Short(null));
        Assert.Equal(StatText.Dash, PanelText.Signed(null));
        Assert.Equal("14,020,550", PanelText.Full(14_020_550));
    }

    [Fact]
    public void SignedChangesCarryTheirSign()
    {
        Assert.Equal("+" + StatText.Abbrev(220_000), PanelText.Signed(220_000));
        Assert.Equal("-" + StatText.Abbrev(1_500), PanelText.Signed(-1_500));
        Assert.Equal("+" + StatText.Abbrev(0), PanelText.Signed(0));
    }

    [Fact]
    public void RolesHaveChips()
    {
        Assert.Equal("★ main", PanelText.Chip(SourceRole.Main));
        Assert.Equal("yours", PanelText.Chip(SourceRole.Mine));
        Assert.Equal("watching", PanelText.Chip(SourceRole.Watch));
    }

    [Fact]
    public void ThePeriodLineNamesThePeriodItsEndAndTheNextRead()
    {
        var period = new ReadingPeriod("AutumnBattle", Now.AddDays(-2), Now.AddHours(76));

        Assert.Equal(
            $"AutumnBattle · ends in {StatText.Span(TimeSpan.FromHours(76))} · next read in {StatText.Span(TimeSpan.FromMinutes(2))}",
            PanelText.PeriodLine(period, Now, Now.AddMinutes(2)));
        Assert.Equal("AutumnBattle · ended", PanelText.PeriodLine(period with { Ends = Now.AddMinutes(-1) }, Now, null));
        Assert.Equal("next read due", PanelText.PeriodLine(null, Now, Now.AddSeconds(-5)));
    }

    [Fact]
    public void AgoAndStaleTexts()
    {
        Assert.Equal($"{StatText.Span(TimeSpan.FromMinutes(5))} ago", PanelText.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("never", PanelText.Ago(null, Now));
        Assert.Equal("This panel's clan was removed.", PanelText.StaleSource("clan"));
        Assert.Equal("This panel's stat was removed.", PanelText.StaleStat);
    }
}
```

Create `tests/ChartGeometryTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class ChartGeometryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheLowestValueSitsAtTheBottomTheHighestAtTheTopAndTimeRunsLeftToRight()
    {
        ChartSeries[] series =
        [
            new("A", [new(T0, 0), new(T0.AddHours(1), 50)], 0),
            new("B", [new(T0.AddMinutes(30), 100)], 1),
        ];

        var layout = ChartGeometry.Layout(series, width: 240, height: 112, fromZero: true, labels: true);

        // Plot area: left 40 (labels), right pad 4, top 6, bottom 6 -> 196 wide, 100 tall.
        Assert.Equal(new[] { (40.0, 106.0), (236.0, 56.0) }, layout.Lines[0].Points.ToArray());
        Assert.Equal(new[] { (138.0, 6.0) }, layout.Lines[1].Points.ToArray());
        Assert.Equal(0, layout.Lines[0].Colour);
        Assert.Equal(1, layout.Lines[1].Colour);
    }

    [Fact]
    public void TheGridHasFourLinesLabelledFromBottomToTop()
    {
        ChartSeries[] series = [new("A", [new(T0, 0), new(T0.AddHours(1), 90)], 0)];

        var layout = ChartGeometry.Layout(series, 240, 112, fromZero: true, labels: true);

        Assert.Equal(ChartGeometry.GridLines, layout.Grid.Count);
        Assert.Equal(106.0, layout.Grid[0].Y);
        Assert.Equal(6.0, layout.Grid[^1].Y);
        Assert.Equal(StatText.Abbrev(0), layout.Grid[0].Label);
        Assert.Equal(StatText.Abbrev(90), layout.Grid[^1].Label);
    }

    [Fact]
    public void WithoutLabelsTheGridIsStillDrawnButUnlabelled()
    {
        ChartSeries[] series = [new("A", [new(T0, 10), new(T0.AddHours(1), 20)], 0)];

        var layout = ChartGeometry.Layout(series, 200, 60, fromZero: false, labels: false);

        Assert.All(layout.Grid, line => Assert.Equal("", line.Label));
        Assert.Equal(0.0, layout.Lines[0].Points[0].X);
        Assert.Equal(54.0, layout.Lines[0].Points[0].Y);
        Assert.Equal(6.0, layout.Lines[0].Points[1].Y);
    }

    [Fact]
    public void ASinglePointSitsAtTheRightEdgeAndNoPointsDrawNothing()
    {
        var single = ChartGeometry.Layout([new ChartSeries("A", [new(T0, 5)], 0)], 240, 112, fromZero: true, labels: true);
        var empty = ChartGeometry.Layout([new ChartSeries("A", [], 0)], 240, 112, fromZero: true, labels: true);

        Assert.Equal(236.0, single.Lines[0].Points[0].X);
        Assert.Empty(empty.Lines);
        Assert.Empty(empty.Grid);
    }
}
```

Create `tests/BoardFixtures.cs`

```csharp
using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>A clock that stands still, in UTC, so "today" and "7 days" are fixed.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Shared data for the board tests: four of your accounts, the worked recipes, and book lines.</summary>
internal static class BoardFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    public static readonly HostAccount Main = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "estehernandez");
    public static readonly HostAccount AltOne = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "CElCPapa");
    public static readonly HostAccount AltTwo = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 202, "ItsJustEstePapa");
    public static readonly HostAccount Loose = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), 301, "ItsJustEste");

    public const string TopListJson = """
        {
          "recipe": 1, "name": "Top groups", "credit": "Test data.", "metricId": "test.points", "valueLabel": "Points",
          "everySeconds": 180,
          "steps": [ { "url": "https://example.test/top", "rows": "data.top", "groupName": "name", "value": "points", "rank": "rank" } ]
        }
        """;

    public static IReadOnlyList<HostAccount> Accounts => [Main, AltOne, AltTwo, Loose];

    public static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    public static Recipe Profile => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

    public static Recipe TopList => RecipeParser.Parse(TopListJson).Recipe!;

    public static InstalledRecipe Installed(Recipe recipe, params string[] shown) => new(recipe, "", new RecipeState(
        Stats: shown.ToDictionary(key => key, key => new StatChoice(Show: true, MetricId: "test." + key), StringComparer.Ordinal)));

    public static Source SourceOf(string id, Recipe recipe, string? clan, SourceRole role) =>
        new(id, recipe.Slug, clan is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["clan"] = clan }, role);

    public static RecipeRow Row(long userId, double? value, string stat = "value") =>
        new(userId, value is { } v ? new Dictionary<string, double> { [stat] = v } : new Dictionary<string, double>());

    public static HeadlineValue Place(double place) =>
        new("Clan place", place.ToString(CultureInfo.InvariantCulture)) { Id = "clan-place", Number = place };

    public static HeadlineValue Points(double points) =>
        new("Clan points", points.ToString(CultureInfo.InvariantCulture)) { Id = "clan-points", Number = points };

    public static RecipeSnapshot Snapshot(
        string sourceId, IReadOnlyList<RecipeRow>? rows, IReadOnlyList<HeadlineValue>? headline = null, ReadingPeriod? period = null,
        IReadOnlyList<GroupRow>? groups = null, IReadOnlyList<AccountLine>? sent = null) =>
        new(WatchState.Reporting, null, sent ?? [], [], rows?.Count ?? 0, "battle=A", rows, headline ?? [])
        {
            SourceId = sourceId,
            Period = period,
            Groups = groups ?? [],
        };

    public static LiveBoard Live(
        IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlyDictionary<string, RecipeSnapshot> snapshots,
        bool running = false, IReadOnlyDictionary<string, DateTimeOffset>? lastRead = null, IReadOnlyList<HostAccount>? accounts = null) =>
        new(sources, installed, snapshots, lastRead ?? new Dictionary<string, DateTimeOffset>(), accounts ?? Accounts, new FixedTime(Now), running);

    public static ScoreBookReader Reader(params BookLine[] lines)
    {
        var reader = new ScoreBookReader(Path.Combine(Path.GetTempPath(), "urscore-board-" + Guid.NewGuid().ToString("N")), new FixedTime(Now));
        foreach (var line in lines) reader.Apply(line);
        return reader;
    }

    public static BookLine Read(
        Source source, DateTimeOffset t, string? period, IReadOnlyDictionary<string, double>? headline, string stat,
        params (long UserId, double Value)[] accounts) =>
        new(BookLine.Version, BookLine.KindRead, t, 0, BookLine.TriggerTimer, new BookRecipeRef(source.Recipe, "0123456789abcdef"),
            source.Id, RoleText(source.Role), source.Inputs, period is null ? null : new BookPeriod(period),
            headline ?? new Dictionary<string, double>(), [stat],
            accounts.ToDictionary(
                a => a.UserId.ToString(CultureInfo.InvariantCulture),
                a => new BookAccount(new Dictionary<string, double> { [stat] = a.Value })));

    public static BookLine Final(
        Source source, DateTimeOffset t, string period, IReadOnlyDictionary<string, double> headline, string stat,
        params (long UserId, double Value)[] accounts) =>
        Read(source, t, period, headline, stat, accounts) with { Kind = BookLine.KindFinal, Trigger = BookLine.TriggerBackfill };

    private static string RoleText(SourceRole role) => role switch
    {
        SourceRole.Main => "main",
        SourceRole.Mine => "mine",
        _ => "watch",
    };
}
```

Create `tests/PanelModelsTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class PanelModelsTests
{
    private const string Period = "AutumnBattle";

    private static readonly ReadingPeriod LivePeriod = new(Period, Now.AddDays(-2), Now.AddHours(76));

    private static Dictionary<string, double> Headline(double points) => new() { ["clan-points"] = points };

    private static Dictionary<string, RecipeSnapshot> Snaps(params RecipeSnapshot[] snapshots) =>
        snapshots.ToDictionary(s => s.SourceId, s => s, StringComparer.Ordinal);

    // ---- Standing ----

    [Fact]
    public void StandingShowsPlaceTotalChangeAndTheRecipesWords()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [Row(101, 14_020_550), Row(5, 1)], [Place(14), Points(30_214_400)], LivePeriod)));
        var reader = Reader(
            Read(main, Now.AddHours(-1), Period, Headline(28_800_000), "value"),
            Read(main, Now.AddMinutes(-3), Period, Headline(30_214_400), "value"));

        var model = PanelModels.Standing(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(new PanelHead("Clan standing", "CCGP", "★ main"), model.Head);
        Assert.Equal("14th", model.Place);
        Assert.Equal("in the battle", model.PlaceSuffix);
        Assert.Equal("Clan points", model.TotalLabel);
        Assert.Equal("30,214,400", model.Total);
        Assert.Equal(Records.Change(reader.HeadlineSeries(main.Id, "clan-points", Period), Now), model.Change);
        Assert.Equal("1 of 2", model.Accounts);
        Assert.Equal(PanelText.PeriodLine(LivePeriod, Now, null), model.PeriodLine);
        Assert.False(model.HasGap);
    }

    [Fact]
    public void TheGapShowsOnlyWhenTheGroupListHasThisAndTheOneJustAbove()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        GroupRow Group(string name, double points, int rank) => new(name, new Dictionary<string, double> { ["value"] = points }, rank);

        LiveBoard With(params GroupRow[] groups) => Live([main, top], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(main.Id, [], [Place(14), Points(30_200_000)]), Snapshot(top.Id, null, groups: groups)));

        var withAbove = PanelModels.Standing(With(Group("Aurelian", 40_000_000, 13), Group("ccgp", 30_200_000, 14)), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        var withGap = PanelModels.Standing(With(Group("Other", 40_000_000, 12), Group("CCGP", 30_200_000, 14)), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.True(withAbove.HasGap);
        Assert.Equal("To 13th", withAbove.GapLabel);
        Assert.Equal($"{StatText.Abbrev(9_800_000)} behind", withAbove.Gap);
        Assert.Equal(30.2 / 40, withAbove.GapFill, 3);
        Assert.False(withGap.HasGap);
    }

    [Fact]
    public void APanelWhoseSourceWasRemovedSaysSoInTheRecipesWord()
    {
        var model = PanelModels.Standing(Live([], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, SourceId: "s-gone0000"));

        Assert.Equal("This panel's clan was removed.", model.Head.Stale);
        Assert.False(model.Head.HasBody);
        Assert.True(model.Head.HasStale);
    }

    [Fact]
    public void OverdueFollowsRecordsOnlyWhileRunning()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var lastRead = new Dictionary<string, DateTimeOffset> { [main.Id] = Now.AddMinutes(-10) };
        var snaps = Snaps(Snapshot(main.Id, [], [Place(14), Points(1)]));

        var running = PanelModels.Standing(Live([main], [Installed(Clan, "value")], snaps, running: true, lastRead), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));
        var stopped = PanelModels.Standing(Live([main], [Installed(Clan, "value")], snaps, running: false, lastRead), Reader(), new PanelSettings(Clan.Slug, SourceId: main.Id));

        Assert.Equal(Records.Overdue(Now.AddMinutes(-10), Clan.EffectiveEverySeconds, Now), running.Head.Overdue);
        Assert.False(stopped.Head.Overdue);
    }

    // ---- Race ----

    [Fact]
    public void TheRaceDrawsEachSourcesHeadlineFromTheBookPlusTheLiveRead()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
        var live = Live([main, rival], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, [], [Points(30_214_400)], LivePeriod), Snapshot(rival.Id, [], [Points(31_300_000)], LivePeriod)),
            lastRead: new Dictionary<string, DateTimeOffset> { [main.Id] = Now, [rival.Id] = Now });
        var reader = Reader(
            Read(main, Now.AddHours(-5), Period, Headline(20_000_000), "value"),
            Read(rival, Now.AddHours(-5), Period, Headline(21_000_000), "value"));

        var model = PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [main.Id, rival.Id]));

        Assert.Equal("Battle race", model.Head.Title);
        Assert.Equal("clan points since the battle started", model.Head.Subtitle);
        Assert.Equal(new[] { 0, 1 }, model.Series.Select(s => s.Colour).ToArray());
        Assert.Equal(30_214_400, model.Series[0].Points[^1].Value);
        Assert.Equal(new[] { $"★ CCGP {StatText.Abbrev(30_214_400)}", $"NovaForge · watching {StatText.Abbrev(31_300_000)}" },
            model.Legend.Select(l => l.Text).ToArray());
    }

    // ---- My accounts ----

    [Fact]
    public void MyAccountsGroupsMainFirstThenMineThenAccountsInNoWatchedClan()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        IReadOnlyList<RecipeRow> ccgpRows = [Row(101, 14_020_550), Row(5, 20_000_000), Row(6, 1_000)];
        IReadOnlyList<RecipeRow> altRows = [Row(202, null), Row(201, 12_418_220), Row(101, 9_000_000), Row(7, 50)];
        var sentLine = new AccountLine("estehernandez", Main.AccountId, new Dictionary<string, double> { ["value"] = 14_020_550 }, Now);
        var live = Live([alts, main], [Installed(Clan, "value")],
            Snaps(Snapshot(main.Id, ccgpRows, period: LivePeriod, sent: [sentLine]), Snapshot(alts.Id, altRows, period: LivePeriod)));
        var reader = Reader(
            Read(alts, Now.AddHours(-1), Period, null, "value", (201, 12_000_000)),
            Read(alts, Now.AddMinutes(-3), Period, null, "value", (201, 12_418_220)));

        var model = PanelModels.MyAccounts(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));

        Assert.Equal("by points", model.Head.Subtitle);
        Assert.Equal("In clan", model.GroupColumn);
        Assert.Equal(new[] { "★ CCGP", "K0i2", "Not in a watched clan" }, model.Groups.Select(g => g.Heading).ToArray());

        var ccgp = Assert.Single(model.Groups[0].Rows);
        Assert.Equal("estehernandez", ccgp.Name);
        Assert.Equal($"#{Ranking.Competition(ccgpRows, "value")[101]} of 3", ccgp.InGroup);
        Assert.True(ccgp.Sent);

        Assert.Equal(new[] { "CElCPapa", "ItsJustEstePapa" }, model.Groups[1].Rows.Select(r => r.Name).ToArray());
        var missing = model.Groups[1].Rows[1];
        Assert.True(missing.Missing);
        Assert.Equal(StatText.Dash, missing.Value);
        Assert.Equal(StatText.Dash, missing.InGroup);
        Assert.Equal(Records.Change(reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue), Now), model.Groups[1].Rows[0].Change);

        Assert.Equal("ItsJustEste", Assert.Single(model.Groups[2].Rows).Name);
    }

    [Fact]
    public void MyAccountsForAStatNoLongerOfferedIsStale()
    {
        var model = PanelModels.MyAccounts(Live([], [Installed(Clan, "value")], Snaps()), Reader(), new PanelSettings(Clan.Slug, Stat: "counter:Gone"));

        Assert.Equal(PanelText.StaleStat, model.Head.Stale);
    }

    // ---- Promotion check ----

    [Fact]
    public void PromotionCheckRanksEachAltAmongTheOtherSourcesLiveRows()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([main, alts], [Installed(Clan, "value")], Snaps(
            Snapshot(main.Id, [Row(101, 14_000_000), Row(7, 20_000_000), Row(8, 8_240_900)]),
            Snapshot(alts.Id, [Row(202, 3_050_118), Row(201, 12_418_220), Row(301, null)])));

        var model = PanelModels.PromotionCheck(live, new PanelSettings(Clan.Slug, SourceId: alts.Id, ToSourceId: main.Id, Stat: "value"));

        Assert.Equal("K0i2 → CCGP", model.Head.Subtitle);
        Assert.Equal("CCGP's lowest now", model.LowestLabel);
        Assert.Equal("8,240,900", model.Lowest);
        Assert.Equal(new[] { "CElCPapa", "ItsJustEstePapa", "ItsJustEste" }, model.Rows.Select(r => r.Name).ToArray());

        var place = Records.WouldPlace(12_418_220, [14_000_000, 20_000_000, 8_240_900])!.Value;
        Assert.Equal($"{PanelText.Ordinal(place.Place)} of {place.Of}", model.Rows[0].WouldPlace);
        Assert.True(model.Rows[0].Fits);
        Assert.Equal("below the lowest", model.Rows[1].WouldPlace);
        Assert.False(model.Rows[1].Fits);
        Assert.True(model.Rows[2].Missing);
        Assert.Equal(StatText.Dash, model.Rows[2].Value);
        Assert.Contains("if it were in CCGP now", model.Head.Note);
    }

    [Theory]
    [InlineData(nameof(PanelModels.PromotionCheck))]
    [InlineData(nameof(PanelModels.Top))]
    [InlineData(nameof(PanelModels.LiveLeaderboard))]
    public void LiveOnlyPanelsCannotReachTheBook(string builder)
    {
        var parameters = typeof(PanelModels).GetMethods().Where(m => m.Name == builder).SelectMany(m => m.GetParameters()).ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(ScoreBookReader) || p.ParameterType == typeof(IScoreBook));
    }

    // ---- Account card ----

    [Fact]
    public void TheAccountCardPicksTheTopAccountAndShowsItsRecords()
    {
        var alts = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
        var live = Live([alts], [Installed(Clan, "value")], Snaps(Snapshot(alts.Id, [Row(202, 9_104_330), Row(201, 12_418_220)], period: LivePeriod)));
        var reader = Reader(
            Read(alts, Now.AddHours(-2), Period, null, "value", (201, 11_000_000)),
            Read(alts, Now.AddMinutes(-3), Period, null, "value", (201, 12_418_220)),
            Final(alts, Now.AddDays(-9), "ArcadeBattle2026", Headline(18_000_000), "value", (201, 12_900_000)));

        var model = PanelModels.AccountCard(live, reader, new PanelSettings(Clan.Slug, Stat: "value"));
        var records = Records.For(reader, Clan.Slug, alts.InputsKey, [alts.Id], 201, "value", live.Time);
        var series = reader.Series(alts.Id, 201, "value", Period, DateTimeOffset.MinValue);

        Assert.Equal("CElCPapa · K0i2", model.Head.Subtitle);
        Assert.Equal("12,418,220", model.Big);
        Assert.Equal(new FactModel("In clan", "#1 of 2"), model.Facts[0]);
        Assert.Contains(new FactModel("Best battle",
            records.BestPeriodValue is { } best ? $"{StatText.Abbrev(best)} · {records.BestPeriod}" : StatText.Dash), model.Facts);
        Assert.Contains(new FactModel("Battles played", records.PeriodsPlayed.ToString()), model.Facts);
        Assert.Contains(new FactModel("Last read", PanelText.Ago(series[^1].T, Now)), model.Facts);
        Assert.Equal(series.Count, Assert.Single(model.Line).Points.Count);
    }

    // ---- Past periods ----

    [Fact]
    public void PastPeriodsListFinalsNewestFirstOneRowPerPeriodWithYourBest()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var arcade = new Dictionary<string, double> { ["clan-place"] = 16, ["clan-points"] = 30_000_000 };
        var cannon = new Dictionary<string, double> { ["clan-place"] = 435, ["clan-points"] = 67_104 };
        var reader = Reader(
            Final(main, Now.AddDays(-20), "Cannon", cannon, "value", (201, 40_210)),
            Final(main, Now.AddDays(-8), "Arcade2026", arcade, "value", (101, 14_100_000)),
            Final(main, Now.AddDays(-2), "Arcade2026", arcade, "value", (202, 2_000_000)));
        var live = Live([main], [Installed(Clan, "value")], Snaps());

        var model = PanelModels.PastPeriods(live, reader, new PanelSettings(Clan.Slug, SourceId: main.Id, Stat: "value"));

        Assert.Equal("Past battles", model.Head.Title);
        Assert.Equal("Filled in from the clan's own record.", model.Head.Note);
        Assert.Equal(new[]
        {
            new PastRow("Arcade2026", "16th", StatText.Abbrev(30_000_000), $"{StatText.Abbrev(14_100_000)} · estehernandez"),
            new PastRow("Cannon", "435th", StatText.Abbrev(67_104), $"{StatText.Abbrev(40_210)} · CElCPapa"),
        }, model.Rows.ToArray());
    }

    // ---- Records ----

    [Fact]
    public void RecordsNameWhichAccountHoldsEachAndSkipAccountsWithNoData()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var reader = Reader(
            Read(profile, Now.AddDays(-3), null, null, "diamonds", (201, 100), (202, 50)),
            Read(profile, Now.AddDays(-1), null, null, "diamonds", (201, 300), (202, 1_000)));
        var live = Live([profile], [Installed(Profile, "diamonds")], Snaps());

        var model = PanelModels.RecordsPanel(live, reader, new PanelSettings(Profile.Slug, Stat: "diamonds"));

        Assert.Equal(new[] { "Highest", "Biggest day", "Fastest 7 days" }, model.Facts.Select(f => f.Label).ToArray());
        var holder = new[] { Main, AltOne, AltTwo, Loose }
            .Select(a => (Account: a, Found: Records.For(reader, Profile.Slug, profile.InputsKey, [profile.Id], a.RobloxUserId, "diamonds", live.Time)))
            .Where(x => x.Found.Highest is not null)
            .MaxBy(x => x.Found.Highest)!;
        Assert.Equal($"{holder.Account.DisplayName} · {StatText.Abbrev(holder.Found.Highest!.Value)}", model.Facts[0].Value);
        Assert.Equal("Diamonds", model.Head.Subtitle);
    }

    // ---- Top ----

    [Fact]
    public void TopListsTheLeadersAndPlacesYourSourcesWhereTheydRank()
    {
        var top = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);
        var main = SourceOf("s-00000001", Clan, "G11", SourceRole.Main);
        var outsider = SourceOf("s-00000003", Clan, "Outsider", SourceRole.Watch);
        var groups = Enumerable.Range(1, 12)
            .Select(i => new GroupRow($"G{i}", new Dictionary<string, double> { ["value"] = 1_000 - i * 10 }, i))
            .ToList();
        var live = Live([top, main, outsider], [Installed(Clan, "value"), Installed(TopList)],
            Snaps(Snapshot(top.Id, null, groups: groups), Snapshot(main.Id, [], [Points(890)]), Snapshot(outsider.Id, [], [Points(5)])));

        var model = PanelModels.Top(live, new PanelSettings(TopList.Slug, SourceId: top.Id));

        Assert.Equal("Top of the battle", model.Head.Title);
        Assert.Equal("Clan", model.NameColumn);
        Assert.Equal("Points", model.ValueColumn);
        Assert.Equal(12, model.Rows.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"G{i}").ToArray(), model.Rows.Take(10).Select(r => r.Name).ToArray());
        Assert.Equal(new TopRow("11", "G11 ★", StatText.Abbrev(890), true, false), model.Rows[10]);

        var estimate = Records.WouldPlace(5, groups.Select(g => g.Values["value"]))!.Value;
        Assert.Equal(new TopRow($"~{estimate.Place}", "Outsider", StatText.Abbrev(5), true, true), model.Rows[11]);
    }

    // ---- Profile stat ----

    [Fact]
    public void ProfileStatShowsTodayAndSevenDayGainsAndKeepsMissingValuesLast()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var snapshot = Snapshot(profile.Id, [Row(101, 215_850_364, "diamonds"), Row(201, 3_957_873_882, "diamonds")]) with
        {
            Unavailable = new Dictionary<long, string> { [202] = "Profile is private." },
        };
        var reader = Reader(
            Read(profile, Now.AddDays(-6), null, null, "diamonds", (101, 200_000_000)),
            Read(profile, new DateTimeOffset(Now.Date.AddHours(1), TimeSpan.Zero), null, null, "diamonds", (101, 213_000_000)),
            Read(profile, Now.AddMinutes(-5), null, null, "diamonds", (101, 215_850_364)));
        var live = Live([profile], [Installed(Profile, "diamonds")], Snaps(snapshot));

        var model = PanelModels.ProfileStat(live, reader, new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "diamonds"));

        // Highest first; the two with no value keep your account list's order, after every value.
        Assert.Equal(new[] { "CElCPapa", "estehernandez", "ItsJustEstePapa", "ItsJustEste" }, model.Rows.Select(r => r.Name).ToArray());
        var mine = model.Rows[1];
        Assert.Equal(PanelText.Signed(PanelModels.Gain(reader.Series(profile.Id, 101, "diamonds", null, new DateTimeOffset(Now.Date, TimeSpan.Zero)))), mine.Today);
        Assert.Equal(PanelText.Signed(PanelModels.Gain(reader.Series(profile.Id, 101, "diamonds", null, Now.AddDays(-7)))), mine.Week);
        Assert.Equal(StatText.Dash, model.Rows[2].Value);
        Assert.Equal("Profile is private.", model.Rows[2].Note);
        Assert.True(model.Rows[3].Missing);
        Assert.Equal(StatText.Dash, model.Rows[3].Value);
    }

    [Fact]
    public void AGainNeedsTwoReadings()
    {
        Assert.Null(PanelModels.Gain([]));
        Assert.Null(PanelModels.Gain([new SeriesPoint(Now, 5, null, false, 0)]));
        Assert.Equal(7, PanelModels.Gain([new SeriesPoint(Now.AddHours(-1), 5, null, false, 0), new SeriesPoint(Now, 12, null, false, 0)]));
    }

    // ---- Live leaderboard ----

    [Fact]
    public void TheLiveLeaderboardMarksYourAccountsAndUsesNamesHeldInMemory()
    {
        var main = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
        var live = Live([main], [Installed(Clan, "value")], Snaps(Snapshot(main.Id, [Row(7, 3), Row(5, 20), Row(101, 14)])));

        var model = PanelModels.LiveLeaderboard(live, new PanelSettings(Clan.Slug, SourceId: main.Id), new Dictionary<long, string> { [5] = "Rival" });

        Assert.Equal(new[] { "Points" }, model.Columns.ToArray());
        Assert.Equal(new[] { "Rival", "estehernandez", "Member 7" }, model.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { false, true, false }, model.Rows.Select(r => r.Yours).ToArray());
        Assert.Equal("Live only. Never saved.", model.Head.Note);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: the build FAILS with `error CS0103: The name 'PanelText' does not exist in the current context`, `error CS0246: The type or namespace name 'ChartSeries' could not be found`, `error CS0246: The type or namespace name 'LiveBoard' could not be found` and `error CS0103: The name 'PanelModels' does not exist in the current context`.

- [ ] **Step 3: Create `src/Board/PanelText.cs`**

Create `src/Board/PanelText.cs`

```csharp
using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>How panels write numbers, places and times (spec §9.6: change states its span; a missing value is a dash).</summary>
public static class PanelText
{
    public const string StaleStat = "This panel's stat was removed.";

    public static string Ordinal(int place)
    {
        var tens = place % 100;
        var suffix = tens is >= 11 and <= 13 ? "th" : (place % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return place.ToString("N0", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>Every digit: "14,020,550". A missing value is a dash, never 0.</summary>
    public static string Full(double? value) => value is { } v ? StatText.Number(v) : StatText.Dash;

    /// <summary>Abbreviated: "12.4M".</summary>
    public static string Short(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : StatText.Abbrev(v);

    /// <summary>A change with its sign: "+220K", "-1.5K".</summary>
    public static string Signed(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : "+" + StatText.Abbrev(v);

    public static string Chip(SourceRole role) => role switch
    {
        SourceRole.Main => "★ main",
        SourceRole.Mine => "yours",
        _ => "watching",
    };

    public static string Ago(DateTimeOffset? then, DateTimeOffset now) =>
        then is { } at ? $"{StatText.Span(now - at)} ago" : "never";

    public static string NextRead(DateTimeOffset due, DateTimeOffset now) =>
        due <= now ? "next read due" : $"next read in {StatText.Span(due - now)}";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m" (spec §8's top bar line).</summary>
    public static string PeriodLine(ReadingPeriod? period, DateTimeOffset now, DateTimeOffset? nextRead)
    {
        var parts = new List<string>();
        if (period is not null)
        {
            parts.Add(period.Value);
            if (period.Ends is { } ends) parts.Add(ends > now ? $"ends in {StatText.Span(ends - now)}" : "ended");
        }

        if (nextRead is { } next) parts.Add(NextRead(next, now));
        return string.Join(" · ", parts);
    }

    public static string StaleSource(string group) => $"This panel's {group} was removed.";
}
```

- [ ] **Step 4: Create `src/Board/ChartGeometry.cs`**

Create `src/Board/ChartGeometry.cs`

```csharp
namespace Labs626.UrScore.Board;

public sealed record ChartPoint(DateTimeOffset T, double Value);

/// <summary>One line: its label, its points in time order, and which theme brush draws it (0 cyan, 1 magenta, 2 white, 3 muted, 4 edge).</summary>
public sealed record ChartSeries(string Label, IReadOnlyList<ChartPoint> Points, int Colour);

public sealed record ChartLine(int Colour, IReadOnlyList<(double X, double Y)> Points);

public sealed record ChartGridLine(double Y, string Label);

public sealed record ChartLayout(IReadOnlyList<ChartLine> Lines, IReadOnlyList<ChartGridLine> Grid);

/// <summary>
/// Where each point of a line chart lands in a box of pixels. Pure, so the chart control only draws. Time
/// runs left to right across the whole span of every series; values run bottom to top.
/// </summary>
public static class ChartGeometry
{
    public const int GridLines = 4;

    public const double LabelWidth = 40;

    private const double Top = 6;
    private const double Bottom = 6;
    private const double RightPad = 4;

    public static ChartLayout Layout(IReadOnlyList<ChartSeries> series, double width, double height, bool fromZero, bool labels)
    {
        var left = labels ? LabelWidth : 0;
        var plotWidth = width - left - RightPad;
        var plotHeight = height - Top - Bottom;
        var points = series.SelectMany(s => s.Points).ToList();
        if (points.Count == 0 || plotWidth <= 0 || plotHeight <= 0) return new ChartLayout([], []);

        var minT = points.Min(p => p.T);
        var maxT = points.Max(p => p.T);
        var minV = fromZero ? Math.Min(0, points.Min(p => p.Value)) : points.Min(p => p.Value);
        var maxV = points.Max(p => p.Value);
        if (maxV <= minV) maxV = minV + 1;
        var spanSeconds = (maxT - minT).TotalSeconds;

        double X(DateTimeOffset t) => left + (spanSeconds <= 0 ? plotWidth : (t - minT).TotalSeconds / spanSeconds * plotWidth);
        double Y(double value) => Top + (1 - (value - minV) / (maxV - minV)) * plotHeight;

        var lines = series
            .Where(s => s.Points.Count > 0)
            .Select(s => new ChartLine(s.Colour, [.. s.Points.OrderBy(p => p.T).Select(p => (X(p.T), Y(p.Value)))]))
            .ToList();

        var grid = Enumerable.Range(0, GridLines)
            .Select(i =>
            {
                var value = minV + (maxV - minV) * i / (GridLines - 1);
                return new ChartGridLine(Y(value), labels ? PanelText.Short(value) : "");
            })
            .ToList();

        return new ChartLayout(lines, grid);
    }
}
```

- [ ] **Step 5: Create `src/Board/PanelModels.cs`**

Create `src/Board/PanelModels.cs`

```csharp
using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

public enum PanelType { Standing, Race, MyAccounts, PromotionCheck, AccountCard, PastPeriods, Records, Top, ProfileStat, LiveLeaderboard }

/// <summary>What a panel shows. Stage 1 fills these from the starter board; stage 2 saves them in boards.json.</summary>
public sealed record PanelSettings(
    string Recipe = "",
    string? SourceId = null,
    IReadOnlyList<string>? SourceIds = null,
    string? ToSourceId = null,
    string? Stat = null,
    long? UserId = null);

/// <summary>Every panel's header: title, subtitle, role chip, overdue mark, and the stale message that replaces the body.</summary>
public sealed record PanelHead(string Title, string Subtitle = "", string Chip = "", bool Overdue = false, string? Stale = null, string Note = "")
{
    public bool HasBody => Stale is null;

    public bool HasStale => Stale is not null;

    public bool HasChip => Chip.Length > 0;

    public bool HasSubtitle => Subtitle.Length > 0;

    public bool HasNote => Note.Length > 0 && Stale is null;
}

/// <summary>Everything live a panel may use. Other players' rows live here in memory only.</summary>
public sealed record LiveBoard(
    IReadOnlyList<Source> Sources,
    IReadOnlyList<InstalledRecipe> Installed,
    IReadOnlyDictionary<string, RecipeSnapshot> Snapshots,
    IReadOnlyDictionary<string, DateTimeOffset> LastRead,
    IReadOnlyList<HostAccount> Accounts,
    TimeProvider Time,
    bool Running)
{
    public DateTimeOffset Now => Time.GetUtcNow();

    public IReadOnlySet<long> MyUserIds => Accounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).ToHashSet();

    public Source? FindSource(string? id) =>
        id is null ? null : Sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    public InstalledRecipe? FindRecipe(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    public RecipeSnapshot? SnapshotOf(string sourceId) => Snapshots.GetValueOrDefault(sourceId);

    /// <summary>The source's main input value ("CCGP"), else its recipe's name.</summary>
    public string SourceName(Source source)
    {
        var recipe = FindRecipe(source.Recipe)?.Recipe;
        if (recipe is not null && RecipeWords.MainInput(recipe) is { } input
            && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0)
        {
            return value.Trim();
        }

        return recipe?.Name ?? source.Recipe;
    }

    public string AccountName(long userId) =>
        Accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName ?? "One of your accounts";

    /// <summary>Spec §9.6: only while reading runs, and only once the source has been read.</summary>
    public bool IsOverdue(Source source) =>
        Running
        && LastRead.TryGetValue(source.Id, out var last)
        && FindRecipe(source.Recipe) is { } installed
        && Records.Overdue(last, installed.Recipe.EffectiveEverySeconds, Now);
}

public sealed record StandingModel(
    PanelHead Head, string Place, string PlaceSuffix, string TotalLabel, string Total, string Change,
    bool HasGap, string GapLabel, string Gap, double GapFill, bool HasAccounts, string Accounts, string PeriodLine);

public sealed record LegendItem(string Text, int Colour);

public sealed record RaceModel(PanelHead Head, IReadOnlyList<ChartSeries> Series, IReadOnlyList<LegendItem> Legend, string ChartName);

public sealed record AccountLineModel(long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing);

public sealed record AccountGroupModel(string Heading, IReadOnlyList<AccountLineModel> Rows);

public sealed record MyAccountsModel(PanelHead Head, string ValueColumn, string GroupColumn, IReadOnlyList<AccountGroupModel> Groups);

public sealed record PromotionRow(string Name, string Value, string WouldPlace, bool Fits, bool Missing);

public sealed record PromotionModel(PanelHead Head, string LowestLabel, string Lowest, string ValueColumn, IReadOnlyList<PromotionRow> Rows);

public sealed record FactModel(string Label, string Value);

public sealed record AccountCardModel(
    PanelHead Head, string BigLabel, string Big, IReadOnlyList<FactModel> Numbers, IReadOnlyList<ChartSeries> Line,
    IReadOnlyList<FactModel> Facts, string ChartName);

public sealed record PastRow(string Period, string Place, string Total, string YourBest);

public sealed record PastPeriodsModel(PanelHead Head, string PeriodColumn, IReadOnlyList<PastRow> Rows);

public sealed record RecordsModel(PanelHead Head, IReadOnlyList<FactModel> Facts);

public sealed record TopRow(string Rank, string Name, string Value, bool Yours, bool Estimate);

public sealed record TopModel(PanelHead Head, string NameColumn, string ValueColumn, IReadOnlyList<TopRow> Rows);

public sealed record ProfileRow(string Name, string Value, string Today, string Week, string Note, bool Missing)
{
    public bool HasNote => Note.Length > 0;
}

public sealed record ProfileStatModel(PanelHead Head, string ValueColumn, IReadOnlyList<ProfileRow> Rows);

public sealed record LeaderRow(string Position, string Name, IReadOnlyList<string> Cells, bool Yours);

public sealed record LeaderboardModel(PanelHead Head, IReadOnlyList<string> Columns, IReadOnlyList<LeaderRow> Rows);

/// <summary>
/// The ten panels' view models (spec §9.4), built from live snapshots and the score book. Pure: no WPF, no
/// disk, no clock but <see cref="LiveBoard.Time"/>. Live-only panels take no reader, so they cannot reach
/// the book.
/// </summary>
public static class PanelModels
{
    public const int MaxRace = 5;

    public const int TopCount = 10;

    private const string Dash = StatText.Dash;

    public static StandingModel Standing(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = recipe is null ? "Standing" : $"{RecipeWords.Capital(RecipeWords.Group(recipe))} standing";

        if (recipe is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new StandingModel(StaleSource(live, settings, title), Dash, "", "", Dash, "", false, "", "", 0, false, "", "");
        }

        var snapshot = live.SnapshotOf(source.Id);
        var name = live.SourceName(source);
        var totalId = TotalId(recipe);
        var place = HeadlineNumber(snapshot, PlaceId(recipe));
        var total = HeadlineNumber(snapshot, totalId);
        var change = totalId is null ? Dash : Records.Change(reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value), live.Now);
        var gap = Gap(live, name);

        var rows = snapshot?.Rows;
        var hasAccounts = source.Role != SourceRole.Watch && rows is not null;
        var mine = rows?.Count(r => live.MyUserIds.Contains(r.UserId)) ?? 0;

        return new StandingModel(
            new PanelHead(title, name, PanelText.Chip(source.Role), live.IsOverdue(source)),
            place is { } p ? PanelText.Ordinal((int)p) : Dash,
            recipe.Period is null || place is null ? "" : $"in the {RecipeWords.Period(recipe)}",
            recipe.Headline.FirstOrDefault(h => h.Id == totalId)?.Label ?? "Total",
            PanelText.Full(total),
            change,
            gap.Has, gap.Label, gap.Text, gap.Fill,
            hasAccounts,
            hasAccounts ? $"{mine} of {rows!.Count}" : "",
            PanelText.PeriodLine(snapshot?.Period, live.Now, null));
    }

    public static RaceModel Race(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = recipe is null ? "Race" : $"{RecipeWords.Capital(RecipeWords.Period(recipe))} race";
        var totalId = recipe is null ? null : TotalId(recipe);
        var sources = (settings.SourceIds ?? []).Select(live.FindSource).OfType<Source>().Take(MaxRace).ToList();

        if (recipe is null || totalId is null || sources.Count == 0)
        {
            return new RaceModel(StaleSource(live, settings, title), [], [], "");
        }

        var series = new List<ChartSeries>();
        var legend = new List<LegendItem>();
        var overdue = false;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var snapshot = live.SnapshotOf(source.Id);
            var points = reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value)
                .Select(p => new ChartPoint(p.T, p.Value))
                .ToList();

            // The live read, until the book has a line for it.
            if (HeadlineNumber(snapshot, totalId) is { } now && live.LastRead.TryGetValue(source.Id, out var at)
                && (points.Count == 0 || points[^1].T < at.AddSeconds(-30)))
            {
                points.Add(new ChartPoint(at, now));
            }

            var name = live.SourceName(source);
            var label = source.Role switch
            {
                SourceRole.Main => $"★ {name}",
                SourceRole.Watch => $"{name} · watching",
                _ => name,
            };

            series.Add(new ChartSeries(label, points, i));
            legend.Add(new LegendItem($"{label} {(points.Count > 0 ? StatText.Abbrev(points[^1].Value) : Dash)}", i));
            overdue |= live.IsOverdue(source);
        }

        var totalLabel = recipe.Headline.First(h => h.Id == totalId).Label;
        return new RaceModel(
            new PanelHead(title, $"{RecipeWords.Lower(totalLabel)} since the {RecipeWords.Period(recipe)} started", Overdue: overdue),
            series, legend, $"{title}: {string.Join(", ", legend.Select(l => l.Text))}");
    }

    public static MyAccountsModel MyAccounts(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        const string Title = "My accounts";
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new MyAccountsModel(StaleSource(live, settings, Title), "", "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new MyAccountsModel(new PanelHead(Title, Stale: PanelText.StaleStat), "", "", []);
        }

        var group = RecipeWords.Group(recipe);
        var assigned = new HashSet<long>();
        var groups = new List<AccountGroupModel>();
        var overdue = false;

        foreach (var source in SourcesYoursIn(live, recipe))
        {
            overdue |= live.IsOverdue(source);
            var snapshot = live.SnapshotOf(source.Id);
            if (snapshot?.Rows is not { } rows) continue;

            var mine = live.Accounts
                .Where(a => a.RobloxUserId != 0 && !assigned.Contains(a.RobloxUserId) && rows.Any(r => r.UserId == a.RobloxUserId))
                .ToList();
            if (mine.Count == 0) continue;

            var ranks = Ranking.Competition(rows, stat.Key);
            var period = snapshot.Period?.Value;
            var since = Since(recipe, live.Now);
            var series = mine.ToDictionary(a => a.RobloxUserId, a => reader.Series(source.Id, a.RobloxUserId, stat.Key, period, since));

            var lines = new List<(double? Value, AccountLineModel Line)>();
            foreach (var account in mine)
            {
                assigned.Add(account.RobloxUserId);
                var value = ValueOf(rows.First(r => r.UserId == account.RobloxUserId), stat.Key);
                var others = series.Where(kv => kv.Key != account.RobloxUserId).Select(kv => kv.Value);
                var sent = snapshot.Accounts.Any(l => l.AccountId == account.AccountId && l.LastValues.ContainsKey(stat.Key));

                lines.Add((value, new AccountLineModel(
                    account.RobloxUserId,
                    account.DisplayName,
                    PanelText.Full(value),
                    value is not null && ranks.TryGetValue(account.RobloxUserId, out var rank) ? $"#{rank} of {rows.Count}" : Dash,
                    Records.Change(series[account.RobloxUserId], live.Now),
                    sent,
                    Records.Stalled(series[account.RobloxUserId], others),
                    value is null)));
            }

            var heading = source.Role == SourceRole.Main ? $"★ {live.SourceName(source)}" : live.SourceName(source);
            groups.Add(new AccountGroupModel(heading, MissingLast(lines)));
        }

        var rest = live.Accounts.Where(a => a.RobloxUserId == 0 || !assigned.Contains(a.RobloxUserId)).ToList();
        if (rest.Count > 0)
        {
            groups.Add(new AccountGroupModel(
                recipe.Inputs.Count > 0 ? $"Not in a watched {group}" : "Not in the last read",
                [.. rest.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
                    .Select(a => new AccountLineModel(a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true))]));
        }

        return new MyAccountsModel(
            new PanelHead(Title, $"by {RecipeWords.Lower(stat.Label)}", Overdue: overdue, Note: "● sent to RoRoRo"),
            stat.Label, $"In {group}", groups);
    }

    /// <summary>Where each account in one source would place among another source's live rows (spec §9.4, §14). Live only.</summary>
    public static PromotionModel PromotionCheck(LiveBoard live, PanelSettings settings)
    {
        const string Title = "Promotion check";
        var installed = live.FindRecipe(settings.Recipe);
        var from = live.FindSource(settings.SourceId);
        var to = live.FindSource(settings.ToSourceId);

        if (installed is null || from is null || to is null) return new PromotionModel(StaleSource(live, settings, Title), "", Dash, "", []);
        if (settings.Stat is null || RecipeStats.Find(installed.Recipe, settings.Stat) is not { } stat)
        {
            return new PromotionModel(new PanelHead(Title, Stale: PanelText.StaleStat), "", Dash, "", []);
        }

        var fromName = live.SourceName(from);
        var toName = live.SourceName(to);
        var head = new PanelHead(Title, $"{fromName} → {toName}", Overdue: live.IsOverdue(from) || live.IsOverdue(to),
            Note: $"Where each account would place if it were in {toName} now. Live only; other members' numbers are never saved.");
        var lowestLabel = $"{toName}'s lowest now";

        var fromRows = live.SnapshotOf(from.Id)?.Rows;
        var toRows = live.SnapshotOf(to.Id)?.Rows;
        if (fromRows is null || toRows is null)
        {
            return new PromotionModel(head with { Note = $"Waiting for a read of {(fromRows is null ? fromName : toName)}." }, lowestLabel, Dash, stat.Label, []);
        }

        var toValues = new List<(long UserId, double Value)>();
        foreach (var row in toRows)
        {
            if (ValueOf(row, stat.Key) is { } v) toValues.Add((row.UserId, v));
        }

        double? lowest = toValues.Count == 0 ? null : toValues.Min(r => r.Value);
        var rows = new List<(double? Value, PromotionRow Row)>();

        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            if (fromRows.FirstOrDefault(r => r.UserId == account.RobloxUserId) is not { } row) continue;

            if (ValueOf(row, stat.Key) is not { } value)
            {
                rows.Add((null, new PromotionRow(account.DisplayName, Dash, Dash, false, true)));
                continue;
            }

            var others = toValues.Where(r => r.UserId != account.RobloxUserId).Select(r => r.Value).ToList();
            var place = Records.WouldPlace(value, others);
            var below = lowest is { } low && value < low;
            var text = place is not { } p ? Dash : below ? "below the lowest" : $"{PanelText.Ordinal(p.Place)} of {p.Of}";
            var fits = place is { } q && !below && q.Place <= others.Count;
            rows.Add((value, new PromotionRow(account.DisplayName, StatText.Abbrev(value), text, fits, false)));
        }

        return new PromotionModel(head, lowestLabel, PanelText.Full(lowest), stat.Label, MissingLast(rows));
    }

    public static AccountCardModel AccountCard(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        const string Title = "Account card";
        if (live.FindRecipe(settings.Recipe) is not { } installed) return EmptyCard(StaleSource(live, settings, Title));

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return EmptyCard(new PanelHead(Title, Stale: PanelText.StaleStat));
        }

        // Where each of your accounts was read; the first source wins, main first.
        var found = new List<(HostAccount Account, Source Source, RecipeRow Row)>();
        foreach (var source in SourcesYoursIn(live, recipe))
        {
            if (live.SnapshotOf(source.Id)?.Rows is not { } rows) continue;
            foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0 && found.All(f => f.Account.RobloxUserId != a.RobloxUserId)))
            {
                if (rows.FirstOrDefault(r => r.UserId == account.RobloxUserId) is { } row) found.Add((account, source, row));
            }
        }

        var picked = settings.UserId is { } userId
            ? found.Where(f => f.Account.RobloxUserId == userId).ToList()
            : [.. found.OrderBy(f => ValueOf(f.Row, stat.Key) is null).ThenByDescending(f => ValueOf(f.Row, stat.Key) ?? 0)];

        if (picked.Count == 0) return EmptyCard(new PanelHead(Title, Note: "No reading of your accounts yet."));

        var (pickedAccount, pickedSource, pickedRow) = picked[0];
        var snapshot = live.SnapshotOf(pickedSource.Id)!;
        var series = reader.Series(pickedSource.Id, pickedAccount.RobloxUserId, stat.Key, snapshot.Period?.Value, Since(recipe, live.Now));

        var numbers = installed.State.ShownStats(recipe)
            .Where(s => s.Key != stat.Key)
            .Select(s => new FactModel(s.Label, PanelText.Full(ValueOf(pickedRow, s.Key))))
            .ToList();

        var facts = new List<FactModel>();
        if (!recipe.LastStep.PerAccount && snapshot.Rows is { } listRows)
        {
            var ranks = Ranking.Competition(listRows, stat.Key);
            facts.Add(new FactModel($"In {RecipeWords.Group(recipe)}",
                ValueOf(pickedRow, stat.Key) is not null && ranks.TryGetValue(pickedAccount.RobloxUserId, out var rank) ? $"#{rank} of {listRows.Count}" : Dash));
        }

        var records = Records.For(reader, recipe.Slug, pickedSource.InputsKey, [pickedSource.Id], pickedAccount.RobloxUserId, stat.Key, live.Time);
        if (recipe.Period is not null)
        {
            facts.Add(new FactModel($"Best {RecipeWords.Period(recipe)}",
                records.BestPeriodValue is { } best ? $"{StatText.Abbrev(best)} · {records.BestPeriod}" : Dash));
            facts.Add(new FactModel("Best rank", records.BestRank is { } bestRank ? $"#{bestRank} · {records.BestRankPeriod}" : Dash));
            facts.Add(new FactModel($"{RecipeWords.Capital(RecipeWords.Periods(recipe))} played", records.PeriodsPlayed.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            facts.Add(new FactModel("Highest", PanelText.Short(records.Highest)));
            facts.Add(new FactModel("Biggest day", PanelText.Signed(records.BiggestDay)));
        }

        DateTimeOffset? lastRead = series.Count > 0 ? series[^1].T
            : live.LastRead.TryGetValue(pickedSource.Id, out var at) ? at : null;
        facts.Add(new FactModel("Last read", PanelText.Ago(lastRead, live.Now)));

        if (snapshot.CellMisses.GetValueOrDefault((pickedAccount.RobloxUserId, stat.Key)) is { } miss) facts.Add(new FactModel("Note", miss));

        IReadOnlyList<ChartSeries> line = series.Count >= 2
            ? [new ChartSeries(stat.Label, [.. series.Select(p => new ChartPoint(p.T, p.Value))], 0)]
            : [];

        return new AccountCardModel(
            new PanelHead(Title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}", Overdue: live.IsOverdue(pickedSource)),
            stat.Label, PanelText.Full(ValueOf(pickedRow, stat.Key)), numbers, line, facts,
            $"{pickedAccount.DisplayName}'s {stat.Label} over time");
    }

    public static PastPeriodsModel PastPeriods(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var title = installed is null ? "Past periods" : $"Past {RecipeWords.Periods(installed.Recipe)}";
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new PastPeriodsModel(StaleSource(live, settings, title), "", []);
        }

        var recipe = installed.Recipe;
        var placeId = PlaceId(recipe);
        var totalId = TotalId(recipe);

        var rows = reader.Finals(recipe.Slug, source.InputsKey)
            .GroupBy(f => f.Period, StringComparer.Ordinal)
            .Select(g => (
                Period: g.Key,
                T: g.Max(f => f.T),
                Headline: g.First().Headline,
                Accounts: g.SelectMany(f => f.Accounts).GroupBy(kv => kv.Key).ToDictionary(x => x.Key, x => x.First().Value)))
            .OrderByDescending(g => g.T)
            .Select(g =>
            {
                string best = Dash;
                if (settings.Stat is { } stat)
                {
                    double? top = null;
                    long holder = 0;
                    foreach (var (userId, account) in g.Accounts)
                    {
                        if (!account.V.TryGetValue(stat, out var v) || (top is { } t && v <= t)) continue;
                        top = v;
                        holder = userId;
                    }

                    if (top is { } value) best = $"{StatText.Abbrev(value)} · {live.AccountName(holder)}";
                }

                return new PastRow(
                    g.Period,
                    placeId is not null && g.Headline.TryGetValue(placeId, out var place) ? PanelText.Ordinal((int)place) : Dash,
                    totalId is not null && g.Headline.TryGetValue(totalId, out var total) ? StatText.Abbrev(total) : Dash,
                    best);
            })
            .ToList();

        var note = rows.Count == 0
            ? $"No finished {RecipeWords.Periods(recipe)} kept yet."
            : $"Filled in from the {RecipeWords.Group(recipe)}'s own record.";

        return new PastPeriodsModel(
            new PanelHead(title, live.SourceName(source), PanelText.Chip(source.Role), Note: note),
            RecipeWords.Capital(RecipeWords.Period(recipe)), rows);
    }

    public static RecordsModel RecordsPanel(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        const string Title = "Records";
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new RecordsModel(StaleSource(live, settings, Title), []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new RecordsModel(new PanelHead(Title, Stale: PanelText.StaleStat), []);
        }

        var all = (
            from account in live.Accounts
            where account.RobloxUserId != 0
            from source in SourcesYoursIn(live, recipe)
            select (Account: account, Found: Records.For(reader, recipe.Slug, source.InputsKey, [source.Id], account.RobloxUserId, stat.Key, live.Time))
        ).ToList();

        string Highest(Func<AccountRecords, double?> pick, Func<HostAccount, AccountRecords, double, string> text)
        {
            var best = all.Where(x => pick(x.Found) is not null).OrderByDescending(x => pick(x.Found)).FirstOrDefault();
            return best.Found is null ? Dash : text(best.Account, best.Found, pick(best.Found)!.Value);
        }

        var facts = new List<FactModel>();
        if (recipe.Period is not null)
        {
            facts.Add(new FactModel($"Best {RecipeWords.Period(recipe)}",
                Highest(r => r.BestPeriodValue, (a, r, v) => $"{a.DisplayName} · {StatText.Abbrev(v)} · {r.BestPeriod}")));

            var bestRank = all.Where(x => x.Found.BestRank is not null).OrderBy(x => x.Found.BestRank).FirstOrDefault();
            facts.Add(new FactModel("Best rank",
                bestRank.Found is null ? Dash : $"{bestRank.Account.DisplayName} · #{bestRank.Found.BestRank} · {bestRank.Found.BestRankPeriod}"));
        }

        facts.Add(new FactModel("Highest", Highest(r => r.Highest, (a, _, v) => $"{a.DisplayName} · {StatText.Abbrev(v)}")));
        facts.Add(new FactModel("Biggest day", Highest(r => r.BiggestDay, (a, _, v) => $"{a.DisplayName} · +{StatText.Abbrev(v)}")));
        facts.Add(new FactModel("Fastest 7 days", Highest(r => r.FastestWeek, (a, _, v) => $"{a.DisplayName} · +{StatText.Abbrev(v)}")));

        return new RecordsModel(new PanelHead(Title, stat.Label), facts);
    }

    /// <summary>A group list's rows live, with your sources' groups placed where they'd rank. Live only.</summary>
    public static TopModel Top(LiveBoard live, PanelSettings settings)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var periodRecipe = installed?.Recipe.Period is not null
            ? installed.Recipe
            : live.Installed.FirstOrDefault(i => i.Recipe.Period is not null && !i.Recipe.IsGroupList)?.Recipe;
        var groupRecipe = live.Installed.FirstOrDefault(i => i.Recipe.Inputs.Count > 0 && !i.Recipe.IsGroupList)?.Recipe;
        var title = $"Top of the {(periodRecipe is null ? "list" : RecipeWords.Period(periodRecipe))}";
        var nameColumn = groupRecipe is null ? "Name" : RecipeWords.Capital(RecipeWords.Group(groupRecipe));

        if (installed is not { Recipe.IsGroupList: true } || live.FindSource(settings.SourceId) is not { } source)
        {
            return new TopModel(StaleSource(live, settings, title), nameColumn, "", []);
        }

        var recipe = installed.Recipe;
        var key = recipe.LastStep.Values[0].Id;
        var valueColumn = recipe.LastStep.Values[0].Label;
        var head = new PanelHead(title, Overdue: live.IsOverdue(source), Note: "From the source's own top list. ~ marks an estimate from your own read.");

        if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups)
        {
            return new TopModel(head with { Note = "Waiting for the first read." }, nameColumn, valueColumn, []);
        }

        var ordered = OrderGroups(groups, key);
        var yours = live.Sources.Where(s => s.Enabled && live.FindRecipe(s.Recipe) is { Recipe.IsGroupList: false }).ToList();
        var mainNames = yours.Where(s => s.Role == SourceRole.Main).Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var yourNames = yours.Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<(double Sort, TopRow Row)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var group = ordered[i];
            var isYours = yourNames.Contains(group.Row.Name);
            if (i >= TopCount && !isYours) continue;

            var name = mainNames.Contains(group.Row.Name) ? $"{group.Row.Name} ★" : group.Row.Name;
            rows.Add((group.Rank, new TopRow(group.Rank.ToString(CultureInfo.InvariantCulture), name, PanelText.Short(group.Value), isYours, false)));
        }

        var values = ordered.Where(g => g.Value is not null).Select(g => g.Value!.Value).ToList();
        var placed = new HashSet<string>(ordered.Select(g => g.Row.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var mineSource in yours)
        {
            var name = live.SourceName(mineSource);
            if (!placed.Add(name)) continue;

            var mineRecipe = live.FindRecipe(mineSource.Recipe)!.Recipe;
            if (HeadlineNumber(live.SnapshotOf(mineSource.Id), TotalId(mineRecipe)) is not { } total) continue;
            if (Records.WouldPlace(total, values) is not { } place) continue;

            var shown = mainNames.Contains(name) ? $"{name} ★" : name;
            rows.Add((place.Place + 0.5, new TopRow($"~{place.Place}", shown, StatText.Abbrev(total), true, true)));
        }

        return new TopModel(head, nameColumn, valueColumn, [.. rows.OrderBy(r => r.Sort).Select(r => r.Row)]);
    }

    public static ProfileStatModel ProfileStat(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        const string Title = "Profile stat";
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new ProfileStatModel(StaleSource(live, settings, Title), "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new ProfileStatModel(new PanelHead(Title, Stale: PanelText.StaleStat), "", []);
        }

        var source = live.FindSource(settings.SourceId)
                     ?? live.Sources.FirstOrDefault(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
        if (source is null) return new ProfileStatModel(StaleSource(live, settings, Title), "", []);

        var snapshot = live.SnapshotOf(source.Id);
        var now = live.Now;
        var local = TimeZoneInfo.ConvertTime(now, live.Time.LocalTimeZone);
        var midnight = new DateTimeOffset(local.Date, local.Offset);

        var rows = new List<(double? Value, ProfileRow Row)>();
        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            var row = snapshot?.Rows?.FirstOrDefault(r => r.UserId == account.RobloxUserId);
            var value = row is null ? null : ValueOf(row, stat.Key);
            var unavailable = snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId);
            var missed = snapshot?.CellMisses.GetValueOrDefault((account.RobloxUserId, stat.Key));
            var today = Gain(reader.Series(source.Id, account.RobloxUserId, stat.Key, null, midnight));
            var week = Gain(reader.Series(source.Id, account.RobloxUserId, stat.Key, null, now.AddDays(-7)));

            rows.Add((value, new ProfileRow(
                account.DisplayName,
                PanelText.Full(value),
                PanelText.Signed(today),
                PanelText.Signed(week),
                unavailable ?? (value is null && missed is not null ? "can't read" : ""),
                value is null)));
        }

        return new ProfileStatModel(new PanelHead(Title, stat.Label, Overdue: live.IsOverdue(source)), stat.Label, MissingLast(rows));
    }

    /// <summary>Every row of a source live, your accounts marked (spec §9.4). Other members' names come from memory only.</summary>
    public static LeaderboardModel LiveLeaderboard(LiveBoard live, PanelSettings settings, IReadOnlyDictionary<long, string> names)
    {
        const string Title = "Live leaderboard";
        var installed = live.FindRecipe(settings.Recipe);
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new LeaderboardModel(StaleSource(live, settings, Title), [], []);
        }

        var shown = installed.State.ShownStats(installed.Recipe);
        var head = new PanelHead(Title, live.SourceName(source), PanelText.Chip(source.Role), live.IsOverdue(source), Note: "Live only. Never saved.");
        if (shown.Count == 0) return new LeaderboardModel(head with { Note = "Tick Show on a stat to fill this panel." }, [], []);

        IReadOnlyList<string> columns = [.. shown.Select(s => s.Label)];
        if (live.SnapshotOf(source.Id)?.Rows is not { } rows) return new LeaderboardModel(head, columns, []);

        var ranked = Leaderboard.Rank(rows, live.MyUserIds, shown[0].Key);
        return new LeaderboardModel(head, columns, [.. ranked.Select(r => new LeaderRow(
            r.Position.ToString(CultureInfo.InvariantCulture),
            r.IsMine ? live.AccountName(r.UserId) : names.GetValueOrDefault(r.UserId) ?? $"Member {r.UserId}",
            [.. shown.Select(s => r.Values.TryGetValue(s.Key, out var v) ? StatText.Number(v) : Dash)],
            r.IsMine))]);
    }

    /// <summary>The rise from the first to the last reading, or null with fewer than two.</summary>
    public static double? Gain(IReadOnlyList<SeriesPoint> points) =>
        points.Count < 2 ? null : points[^1].Value - points[0].Value;

    private sealed record RankedGroup(GroupRow Row, int Rank, double? Value);

    private static PanelHead StaleSource(LiveBoard live, PanelSettings settings, string title) =>
        new(title, Stale: PanelText.StaleSource(live.FindRecipe(settings.Recipe)?.Recipe is { } recipe ? RecipeWords.Group(recipe) : "source"));

    private static AccountCardModel EmptyCard(PanelHead head) => new(head, "", Dash, [], [], [], "");

    private static IEnumerable<Source> SourcesYoursIn(LiveBoard live, Recipe recipe) =>
        live.Sources
            .Where(s => s.Enabled && s.Role != SourceRole.Watch && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))
            .OrderBy(s => s.Role == SourceRole.Main ? 0 : 1);

    private static string? PlaceId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => !h.Sum)?.Id;

    private static string? TotalId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => h.Sum)?.Id;

    private static double? HeadlineNumber(RecipeSnapshot? snapshot, string? id) =>
        id is null ? null : snapshot?.Headline?.FirstOrDefault(h => h.Id == id)?.Number;

    private static double? ValueOf(RecipeRow row, string stat) => row.Values.TryGetValue(stat, out var value) ? value : null;

    /// <summary>A recipe with a period shows the current period; one without shows the last 30 days (spec §9.1).</summary>
    private static DateTimeOffset Since(Recipe recipe, DateTimeOffset now) => recipe.Period is null ? now.AddDays(-30) : DateTimeOffset.MinValue;

    /// <summary>Highest first; a missing value sorts last and never counts as zero (spec §9.6).</summary>
    private static IReadOnlyList<T> MissingLast<T>(IEnumerable<(double? Value, T Row)> rows) =>
        [.. rows.OrderBy(r => r.Value is null).ThenByDescending(r => r.Value ?? 0).Select(r => r.Row)];

    private static List<RankedGroup> OrderGroups(IReadOnlyList<GroupRow> groups, string key)
    {
        var ordered = groups
            .Select(g => (Row: g, Value: g.Values.TryGetValue(key, out var v) ? v : (double?)null))
            .OrderBy(g => g.Row.Rank ?? int.MaxValue)
            .ThenBy(g => g.Value is null)
            .ThenByDescending(g => g.Value ?? 0)
            .ToList();

        return [.. ordered.Select((g, i) => new RankedGroup(g.Row, g.Row.Rank ?? i + 1, g.Value))];
    }

    /// <summary>The gap to the group just above, only when a group list holds both (spec §9.4).</summary>
    private static (bool Has, string Label, string Text, double Fill) Gap(LiveBoard live, string name)
    {
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.FindRecipe(source.Recipe)?.Recipe is not { IsGroupList: true } recipe) continue;
            if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups) continue;

            var ordered = OrderGroups(groups, recipe.LastStep.Values[0].Id);
            var index = ordered.FindIndex(g => string.Equals(g.Row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index <= 0) continue;

            var above = ordered[index - 1];
            var here = ordered[index];
            if (above.Rank != here.Rank - 1 || above.Value is not { } a || here.Value is not { } h) continue;

            return (true, $"To {PanelText.Ordinal(above.Rank)}", $"{StatText.Abbrev(Math.Max(0, a - h))} behind", a <= 0 ? 0 : Math.Clamp(h / a, 0, 1));
        }

        return (false, "", "", 0);
    }
}
```

- [ ] **Step 6: Run the board tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~PanelTextTests|FullyQualifiedName~ChartGeometryTests|FullyQualifiedName~PanelModelsTests"`
Expected: PASS, 37 tests.

- [ ] **Step 7: Add the panel styles to `src/App.xaml`**

In `src/App.xaml`, directly after the `FlatButton` style, add:

```xml
            <!-- Board panels (spec §9.4 and the mock): a card, big numbers, key-value lines, column headers, notes. -->
            <Style x:Key="PanelCard" TargetType="Border">
                <Setter Property="Background" Value="{DynamicResource RowBgBrush}" />
                <Setter Property="BorderBrush" Value="{DynamicResource DividerBrush}" />
                <Setter Property="BorderThickness" Value="1" />
                <Setter Property="CornerRadius" Value="8" />
                <Setter Property="Padding" Value="14,12,14,14" />
            </Style>
            <Style x:Key="BigNumber" TargetType="TextBlock">
                <Setter Property="FontFamily" Value="{StaticResource DisplayFont}" />
                <Setter Property="FontSize" Value="28" />
                <Setter Property="FontWeight" Value="SemiBold" />
            </Style>
            <Style x:Key="KeyLabel" TargetType="TextBlock">
                <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                <Setter Property="FontSize" Value="12" />
                <Setter Property="VerticalAlignment" Value="Center" />
            </Style>
            <Style x:Key="ColumnHeader" TargetType="TextBlock">
                <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
                <Setter Property="FontSize" Value="10.5" />
                <Setter Property="Margin" Value="0,0,0,4" />
            </Style>
            <Style x:Key="PanelNoteText" TargetType="TextBlock">
                <Setter Property="Foreground" Value="{DynamicResource EdgeBrush}" />
                <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
                <Setter Property="FontSize" Value="11" />
                <Setter Property="TextWrapping" Value="Wrap" />
                <Setter Property="Margin" Value="0,10,0,0" />
            </Style>
            <!-- A cell that greys out when its row has no value. -->
            <Style x:Key="RowCell" TargetType="TextBlock">
                <Setter Property="Foreground" Value="{DynamicResource WhiteBrush}" />
                <Setter Property="TextTrimming" Value="CharacterEllipsis" />
                <Setter Property="VerticalAlignment" Value="Center" />
                <Style.Triggers>
                    <DataTrigger Binding="{Binding Missing}" Value="True">
                        <Setter Property="Foreground" Value="{DynamicResource MutedTextBrush}" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
            <Style x:Key="NumberCell" TargetType="TextBlock" BasedOn="{StaticResource RowCell}">
                <Setter Property="TextAlignment" Value="Right" />
            </Style>
```

- [ ] **Step 8: Create the chart controls**

Create `src/UI/Controls/LineChart.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// A line chart: <see cref="Polyline"/>s over a faint grid, placed by <see cref="ChartGeometry"/>. Every
/// brush is a theme brush referenced by key, so a theme switch repaints it (ThemeFenceTests).
/// </summary>
public sealed class LineChart : Canvas
{
    /// <summary>Series colours in order: cyan, magenta, white, muted, edge.</summary>
    public static readonly string[] BrushKeys = ["CyanBrush", "MagentaBrush", "WhiteBrush", "MutedTextBrush", "EdgeBrush"];

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart), new PropertyMetadata(null, (d, _) => ((LineChart)d).Redraw()));

    public static readonly DependencyProperty FromZeroProperty = DependencyProperty.Register(
        nameof(FromZero), typeof(bool), typeof(LineChart), new PropertyMetadata(false, (d, _) => ((LineChart)d).Redraw()));

    public static readonly DependencyProperty ShowLabelsProperty = DependencyProperty.Register(
        nameof(ShowLabels), typeof(bool), typeof(LineChart), new PropertyMetadata(true, (d, _) => ((LineChart)d).Redraw()));

    public LineChart()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Redraw();
    }

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public bool FromZero
    {
        get => (bool)GetValue(FromZeroProperty);
        set => SetValue(FromZeroProperty, value);
    }

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public static string BrushKeyFor(int colour) => BrushKeys[((colour % BrushKeys.Length) + BrushKeys.Length) % BrushKeys.Length];

    private void Redraw()
    {
        Children.Clear();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var layout = ChartGeometry.Layout(Series ?? [], ActualWidth, ActualHeight, FromZero, ShowLabels);
        var left = ShowLabels ? ChartGeometry.LabelWidth : 0;

        foreach (var grid in layout.Grid)
        {
            var rule = new Line { X1 = left, X2 = ActualWidth, Y1 = grid.Y, Y2 = grid.Y, StrokeThickness = 1, SnapsToDevicePixels = true };
            rule.SetResourceReference(Shape.StrokeProperty, "DividerBrush");
            Children.Add(rule);

            if (grid.Label.Length == 0) continue;

            var label = new TextBlock { Text = grid.Label, FontSize = 10, TextAlignment = TextAlignment.Right, Width = ChartGeometry.LabelWidth - 6 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            SetLeft(label, 0);
            SetTop(label, grid.Y - 7);
            Children.Add(label);
        }

        foreach (var line in layout.Lines)
        {
            var polyline = new Polyline
            {
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection(line.Points.Select(p => new Point(p.X, p.Y))),
            };
            polyline.SetResourceReference(Shape.StrokeProperty, BrushKeyFor(line.Colour));
            Children.Add(polyline);
        }
    }
}
```

Create `src/UI/Controls/ChartParts.cs`

```csharp
using System.Windows;
using System.Windows.Controls;

namespace Labs626.UrScore.UI;

/// <summary>The short coloured bar beside a legend entry, in the same theme brush as its line.</summary>
public sealed class LegendSwatch : Border
{
    public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(
        nameof(Colour), typeof(int), typeof(LegendSwatch), new PropertyMetadata(0, (d, e) => ((LegendSwatch)d).Paint((int)e.NewValue)));

    public LegendSwatch()
    {
        Width = 12;
        Height = 3;
        CornerRadius = new CornerRadius(2);
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, 6, 0);
        Paint(0);
    }

    public int Colour
    {
        get => (int)GetValue(ColourProperty);
        set => SetValue(ColourProperty, value);
    }

    private void Paint(int colour) => SetResourceReference(BackgroundProperty, LineChart.BrushKeyFor(colour));
}

/// <summary>A thin bar filled to a fraction: the gap to the group above.</summary>
public sealed class FillBar : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FillBar), new PropertyMetadata(0.0, (d, _) => ((FillBar)d).Update()));

    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };

    public FillBar()
    {
        Height = 6;
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "CyanBrush");
        Children.Add(track);
        Children.Add(_fill);
        SizeChanged += (_, _) => Update();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void Update() => _fill.Width = Math.Max(0, ActualWidth * Math.Clamp(Value, 0, 1));
}
```

- [ ] **Step 9: Create the panel frame and the ten panels**

Every panel's root is a `PanelCard` border holding the `PanelFrame` (bound to `Head`), a body that hides when the panel is stale, and a note. Every panel's code-behind is only `Render`, which sets the model as its data context.

Create `src/UI/Panels/PanelFrame.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.PanelFrame"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel>
        <DockPanel LastChildFill="False">
            <!-- Stage 2 wires pop out and panel settings (spec §9.2, §9.3). Hidden until then. -->
            <StackPanel x:Name="PanelTools" DockPanel.Dock="Right" Orientation="Horizontal" Visibility="Collapsed">
                <Button x:Name="PopOutButton" Content="⧉" Style="{StaticResource FlatButton}" Padding="6,2"
                        AutomationProperties.Name="Pop out" />
                <Button x:Name="PanelSettingsButton" Content="⋯" Style="{StaticResource FlatButton}" Padding="6,2"
                        AutomationProperties.Name="Panel settings" />
            </StackPanel>
            <TextBlock x:Name="PanelTitle" DockPanel.Dock="Left" Text="{Binding Title}" Style="{StaticResource SectionLabel}"
                       Margin="0" VerticalAlignment="Center" />
            <Border DockPanel.Dock="Left" Margin="8,0,0,0" Padding="7,1" CornerRadius="9" BorderThickness="1"
                    BorderBrush="{DynamicResource EdgeBrush}" VerticalAlignment="Center"
                    Visibility="{Binding HasChip, Converter={StaticResource BoolToVisible}}">
                <TextBlock Text="{Binding Chip}" FontFamily="{StaticResource MonoFont}" FontSize="10.5"
                           Foreground="{DynamicResource MutedTextBrush}" />
            </Border>
            <TextBlock x:Name="PanelOverdue" DockPanel.Dock="Left" Text="overdue" Margin="8,0,0,0" FontSize="11"
                       VerticalAlignment="Center" Foreground="{DynamicResource MagentaBrush}"
                       Visibility="{Binding Overdue, Converter={StaticResource BoolToVisible}}" />
        </DockPanel>
        <TextBlock x:Name="PanelSubtitle" Text="{Binding Subtitle}" Style="{StaticResource Muted}" Margin="0,4,0,0"
                   Visibility="{Binding HasSubtitle, Converter={StaticResource BoolToVisible}}" />
        <TextBlock x:Name="PanelStale" Text="{Binding Stale}" TextWrapping="Wrap" Margin="0,10,0,0"
                   Visibility="{Binding HasStale, Converter={StaticResource BoolToVisible}}" />
    </StackPanel>
</UserControl>
```

Create `src/UI/Panels/PanelFrame.xaml.cs`

```csharp
using System.Windows.Controls;

namespace Labs626.UrScore.UI;

/// <summary>A panel's header, bound to its <c>PanelHead</c>.</summary>
public partial class PanelFrame : UserControl
{
    public PanelFrame() => InitializeComponent();
}
```

Create `src/UI/Panels/StandingPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.StandingPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{Binding Place}" Style="{StaticResource BigNumber}" />
                    <TextBlock Text="{Binding PlaceSuffix}" Style="{StaticResource Muted}" Margin="10,0,0,5" VerticalAlignment="Bottom" />
                </StackPanel>
                <DockPanel Margin="0,8,0,0">
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Total}" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding TotalLabel}" Style="{StaticResource KeyLabel}" />
                </DockPanel>
                <StackPanel Margin="0,8,0,0" Visibility="{Binding HasGap, Converter={StaticResource BoolToVisible}}">
                    <DockPanel>
                        <TextBlock DockPanel.Dock="Right" Text="{Binding Gap}" Foreground="{DynamicResource MagentaBrush}" />
                        <TextBlock Text="{Binding GapLabel}" Style="{StaticResource KeyLabel}" />
                    </DockPanel>
                    <ui:FillBar Value="{Binding GapFill}" Margin="0,4,0,0" />
                </StackPanel>
                <DockPanel Margin="0,6,0,0" Visibility="{Binding HasAccounts, Converter={StaticResource BoolToVisible}}">
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Accounts}" />
                    <TextBlock Text="Your accounts" Style="{StaticResource KeyLabel}" />
                </DockPanel>
                <DockPanel Margin="0,6,0,0">
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Change}" Foreground="{DynamicResource CyanBrush}" />
                    <TextBlock Text="Change" Style="{StaticResource KeyLabel}" />
                </DockPanel>
                <TextBlock Text="{Binding PeriodLine}" Style="{StaticResource Muted}" FontSize="11" Margin="0,8,0,0" />
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/StandingPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class StandingPanel : UserControl
{
    public StandingPanel() => InitializeComponent();

    public void Render(StandingModel model) => DataContext = model;
}
```

Create `src/UI/Panels/RacePanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.RacePanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <ui:LineChart Height="170" Series="{Binding Series}" FromZero="True" ShowLabels="True"
                              AutomationProperties.Name="{Binding ChartName}" />
                <ItemsControl ItemsSource="{Binding Legend}" Margin="0,8,0,0">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="0,0,14,4">
                                <ui:LegendSwatch Colour="{Binding Colour}" />
                                <TextBlock Text="{Binding Text}" Style="{StaticResource Muted}" FontSize="12" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/RacePanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class RacePanel : UserControl
{
    public RacePanel() => InitializeComponent();

    public void Render(RaceModel model) => DataContext = model;
}
```

Create `src/UI/Panels/MyAccountsPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.MyAccountsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="110" />
                        <ColumnDefinition Width="80" />
                        <ColumnDefinition Width="120" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Account" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="1" Text="{Binding ValueColumn}" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="2" Text="{Binding GroupColumn}" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="3" Text="Change" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                </Grid>
                <ItemsControl ItemsSource="{Binding Groups}" AutomationProperties.Name="Your accounts by this stat">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <TextBlock Text="{Binding Heading}" Style="{StaticResource ColumnHeader}" Margin="0,8,0,2" />
                                <ItemsControl ItemsSource="{Binding Rows}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid Margin="0,2">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="110" />
                                                    <ColumnDefinition Width="80" />
                                                    <ColumnDefinition Width="120" />
                                                </Grid.ColumnDefinitions>
                                                <StackPanel Grid.Column="0" Orientation="Horizontal">
                                                    <Ellipse Width="6" Height="6" Margin="0,0,6,0" VerticalAlignment="Center"
                                                             Fill="{DynamicResource CyanBrush}"
                                                             Visibility="{Binding Sent, Converter={StaticResource BoolToVisible}}" />
                                                    <TextBlock Text="{Binding Name}" Style="{StaticResource RowCell}" />
                                                </StackPanel>
                                                <TextBlock Grid.Column="1" Text="{Binding Value}" Style="{StaticResource NumberCell}" />
                                                <TextBlock Grid.Column="2" Text="{Binding InGroup}" Style="{StaticResource NumberCell}" />
                                                <StackPanel Grid.Column="3" Orientation="Horizontal" HorizontalAlignment="Right">
                                                    <TextBlock Text="{Binding Change}" Style="{StaticResource NumberCell}" />
                                                    <TextBlock Text=" · stalled" FontSize="11" VerticalAlignment="Center"
                                                               Foreground="{DynamicResource MagentaBrush}"
                                                               Visibility="{Binding Stalled, Converter={StaticResource BoolToVisible}}" />
                                                </StackPanel>
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/MyAccountsPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class MyAccountsPanel : UserControl
{
    public MyAccountsPanel() => InitializeComponent();

    public void Render(MyAccountsModel model) => DataContext = model;
}
```

Create `src/UI/Panels/PromotionCheckPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.PromotionCheckPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <DockPanel Margin="0,0,0,8">
                    <TextBlock DockPanel.Dock="Right" Text="{Binding Lowest}" FontWeight="SemiBold" />
                    <TextBlock Text="{Binding LowestLabel}" Style="{StaticResource KeyLabel}" />
                </DockPanel>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="80" />
                        <ColumnDefinition Width="120" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Account" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="1" Text="{Binding ValueColumn}" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="2" Text="Would place" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                </Grid>
                <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.Name="Where each account would place">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="80" />
                                    <ColumnDefinition Width="120" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Text="{Binding Name}" Style="{StaticResource RowCell}" />
                                <TextBlock Grid.Column="1" Text="{Binding Value}" Style="{StaticResource NumberCell}" />
                                <TextBlock Grid.Column="2" Text="{Binding WouldPlace}" TextAlignment="Right" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock" BasedOn="{StaticResource NumberCell}">
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Fits}" Value="True">
                                                    <Setter Property="Foreground" Value="{DynamicResource CyanBrush}" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/PromotionCheckPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class PromotionCheckPanel : UserControl
{
    public PromotionCheckPanel() => InitializeComponent();

    public void Render(PromotionModel model) => DataContext = model;
}
```

Create `src/UI/Panels/AccountCardPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.AccountCardPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <TextBlock Text="{Binding BigLabel}" Style="{StaticResource KeyLabel}" />
                <TextBlock Text="{Binding Big}" Style="{StaticResource BigNumber}" />
                <ui:LineChart Height="48" Margin="0,6,0,0" Series="{Binding Line}" FromZero="False" ShowLabels="False"
                              AutomationProperties.Name="{Binding ChartName}" />
                <ItemsControl ItemsSource="{Binding Numbers}" Margin="0,6,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <DockPanel Margin="0,3,0,0">
                                <TextBlock DockPanel.Dock="Right" Text="{Binding Value}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Label}" Style="{StaticResource KeyLabel}" />
                            </DockPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl ItemsSource="{Binding Facts}" Margin="0,6,0,0">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <DockPanel Margin="0,3,0,0">
                                <TextBlock DockPanel.Dock="Right" Text="{Binding Value}" TextWrapping="Wrap" MaxWidth="220" TextAlignment="Right" />
                                <TextBlock Text="{Binding Label}" Style="{StaticResource KeyLabel}" />
                            </DockPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/AccountCardPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class AccountCardPanel : UserControl
{
    public AccountCardPanel() => InitializeComponent();

    public void Render(AccountCardModel model) => DataContext = model;
}
```

Create `src/UI/Panels/PastPeriodsPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.PastPeriodsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="70" />
                        <ColumnDefinition Width="70" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="{Binding PeriodColumn}" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="1" Text="Place" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="2" Text="Total" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="3" Text="Your best" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                </Grid>
                <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.Name="Finished periods, newest first">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Grid.Column="0" Text="{Binding Period}" TextTrimming="CharacterEllipsis" />
                                <TextBlock Grid.Column="1" Text="{Binding Place}" TextAlignment="Right" />
                                <TextBlock Grid.Column="2" Text="{Binding Total}" TextAlignment="Right" />
                                <TextBlock Grid.Column="3" Text="{Binding YourBest}" TextAlignment="Right" TextTrimming="CharacterEllipsis" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/PastPeriodsPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class PastPeriodsPanel : UserControl
{
    public PastPeriodsPanel() => InitializeComponent();

    public void Render(PastPeriodsModel model) => DataContext = model;
}
```

Create `src/UI/Panels/RecordsPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.RecordsPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <ItemsControl Margin="0,10,0,0" ItemsSource="{Binding Facts}" AutomationProperties.Name="Records across your accounts"
                          Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <DockPanel Margin="0,3,0,0">
                            <TextBlock DockPanel.Dock="Right" Text="{Binding Value}" TextWrapping="Wrap" MaxWidth="240" TextAlignment="Right" />
                            <TextBlock Text="{Binding Label}" Style="{StaticResource KeyLabel}" />
                        </DockPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/RecordsPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class RecordsPanel : UserControl
{
    public RecordsPanel() => InitializeComponent();

    public void Render(RecordsModel model) => DataContext = model;
}
```

Create `src/UI/Panels/TopPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.TopPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="44" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="90" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="#" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="1" Text="{Binding NameColumn}" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="2" Text="{Binding ValueColumn}" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                </Grid>
                <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.Name="The leading groups, with yours placed">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Padding="0,2" CornerRadius="3">
                                <Border.Style>
                                    <Style TargetType="Border">
                                        <Setter Property="Background" Value="Transparent" />
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding Yours}" Value="True">
                                                <Setter Property="Background" Value="{DynamicResource RowHoverBrush}" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="44" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="90" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Rank}" />
                                    <TextBlock Grid.Column="1" Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                                    <TextBlock Grid.Column="2" Text="{Binding Value}" TextAlignment="Right" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/TopPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class TopPanel : UserControl
{
    public TopPanel() => InitializeComponent();

    public void Render(TopModel model) => DataContext = model;
}
```

Create `src/UI/Panels/ProfileStatPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.ProfileStatPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <StackPanel Margin="0,10,0,0" Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="130" />
                        <ColumnDefinition Width="90" />
                        <ColumnDefinition Width="90" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Account" Style="{StaticResource ColumnHeader}" />
                    <TextBlock Grid.Column="1" Text="{Binding ValueColumn}" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="2" Text="Today" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                    <TextBlock Grid.Column="3" Text="7 days" Style="{StaticResource ColumnHeader}" TextAlignment="Right" />
                </Grid>
                <ItemsControl ItemsSource="{Binding Rows}" AutomationProperties.Name="Your accounts with this stat">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,2">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="130" />
                                        <ColumnDefinition Width="90" />
                                        <ColumnDefinition Width="90" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Name}" Style="{StaticResource RowCell}" />
                                    <TextBlock Grid.Column="1" Text="{Binding Value}" Style="{StaticResource NumberCell}" />
                                    <TextBlock Grid.Column="2" Text="{Binding Today}" Style="{StaticResource NumberCell}" />
                                    <TextBlock Grid.Column="3" Text="{Binding Week}" Style="{StaticResource NumberCell}" />
                                </Grid>
                                <TextBlock Text="{Binding Note}" Style="{StaticResource Muted}" FontSize="11"
                                           Visibility="{Binding HasNote, Converter={StaticResource BoolToVisible}}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/ProfileStatPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

public partial class ProfileStatPanel : UserControl
{
    public ProfileStatPanel() => InitializeComponent();

    public void Render(ProfileStatModel model) => DataContext = model;
}
```

Create `src/UI/Panels/LiveLeaderboardPanel.xaml`

```xml
<UserControl x:Class="Labs626.UrScore.UI.LiveLeaderboardPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:Labs626.UrScore.UI">
    <Border Style="{StaticResource PanelCard}">
        <StackPanel>
            <ui:PanelFrame DataContext="{Binding Head}" />
            <DataGrid x:Name="LeaderGrid" Margin="0,10,0,0" MaxHeight="360" AutoGenerateColumns="False" IsReadOnly="True"
                      ItemsSource="{Binding Rows}"
                      Visibility="{Binding Head.HasBody, Converter={StaticResource BoolToVisible}}"
                      AutomationProperties.Name="Every row read, your accounts marked">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="#" Binding="{Binding Position}" Width="50" />
                    <DataGridTextColumn Header="Member" Binding="{Binding Name}" Width="*" />
                </DataGrid.Columns>
            </DataGrid>
            <TextBlock x:Name="PanelNote" Text="{Binding Head.Note}" Style="{StaticResource PanelNoteText}"
                       Visibility="{Binding Head.HasNote, Converter={StaticResource BoolToVisible}}" />
        </StackPanel>
    </Border>
</UserControl>
```

Create `src/UI/Panels/LiveLeaderboardPanel.xaml.cs`

```csharp
using System.Windows.Controls;
using System.Windows.Data;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Every row live, a column per shown stat (bound by position: a stat key isn't a binding path), and a Yours column.</summary>
public partial class LiveLeaderboardPanel : UserControl
{
    private IReadOnlyList<string> _columns = [];

    public LiveLeaderboardPanel() => InitializeComponent();

    public void Render(LeaderboardModel model)
    {
        if (!model.Columns.SequenceEqual(_columns))
        {
            while (LeaderGrid.Columns.Count > 2) LeaderGrid.Columns.RemoveAt(2);

            for (var index = 0; index < model.Columns.Count; index++)
            {
                LeaderGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = model.Columns[index],
                    Binding = new Binding($"Cells[{index}]"),
                    Width = new DataGridLength(120),
                });
            }

            LeaderGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Yours",
                Binding = new Binding(nameof(LeaderRow.Yours)),
                Width = new DataGridLength(60),
                ElementStyle = (System.Windows.Style)FindResource("GridCheckBoxDisplay"),
            });

            _columns = model.Columns;
        }

        DataContext = model;
    }
}
```

- [ ] **Step 10: Create `src/UI/Panels/PanelViews.cs`**

Create `src/UI/Panels/PanelViews.cs`

```csharp
using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;

namespace Labs626.UrScore.UI;

/// <summary>The one switch from a panel type to its control and its builder.</summary>
public static class PanelViews
{
    public static FrameworkElement Create(PanelType type) => type switch
    {
        PanelType.Standing => new StandingPanel(),
        PanelType.Race => new RacePanel(),
        PanelType.MyAccounts => new MyAccountsPanel(),
        PanelType.PromotionCheck => new PromotionCheckPanel(),
        PanelType.AccountCard => new AccountCardPanel(),
        PanelType.PastPeriods => new PastPeriodsPanel(),
        PanelType.Records => new RecordsPanel(),
        PanelType.Top => new TopPanel(),
        PanelType.ProfileStat => new ProfileStatPanel(),
        _ => new LiveLeaderboardPanel(),
    };

    public static void Render(
        FrameworkElement view, PanelSettings settings, LiveBoard live, ScoreBookReader reader, IReadOnlyDictionary<long, string> names)
    {
        switch (view)
        {
            case StandingPanel panel: panel.Render(PanelModels.Standing(live, reader, settings)); break;
            case RacePanel panel: panel.Render(PanelModels.Race(live, reader, settings)); break;
            case MyAccountsPanel panel: panel.Render(PanelModels.MyAccounts(live, reader, settings)); break;
            case PromotionCheckPanel panel: panel.Render(PanelModels.PromotionCheck(live, settings)); break;
            case AccountCardPanel panel: panel.Render(PanelModels.AccountCard(live, reader, settings)); break;
            case PastPeriodsPanel panel: panel.Render(PanelModels.PastPeriods(live, reader, settings)); break;
            case RecordsPanel panel: panel.Render(PanelModels.RecordsPanel(live, reader, settings)); break;
            case TopPanel panel: panel.Render(PanelModels.Top(live, settings)); break;
            case ProfileStatPanel panel: panel.Render(PanelModels.ProfileStat(live, reader, settings)); break;
            case LiveLeaderboardPanel panel: panel.Render(PanelModels.LiveLeaderboard(live, settings, names)); break;
        }
    }
}
```

- [ ] **Step 11: Build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: every test passes. `ThemeFenceTests` scans `src/UI/Panels/*.xaml` and `src/UI/Controls/*.cs`: the only literal is `Transparent`, and the chart code references brushes by key with `SetResourceReference`, which the code fence allows.

- [ ] **Step 12: Commit**

```text
git add src/Board/PanelText.cs src/Board/ChartGeometry.cs src/Board/PanelModels.cs src/UI/Controls/LineChart.cs src/UI/Controls/ChartParts.cs src/UI/Panels src/App.xaml tests/PanelTextTests.cs tests/ChartGeometryTests.cs tests/BoardFixtures.cs tests/PanelModelsTests.cs
git commit -m "board: ten panels built from live reads and the score book

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 14: The starter board window and the composition root

Spec §8, §4.2, §7.1 first run, §14. The main window becomes the starter board. `MainWindow` is deleted; its orchestration moves to `AppServices` and its setup moves to the Setup window (Tasks 11–12).

**`src/Composition/AppServices.cs`** builds everything once and is the only place a `RecipeWatch` is constructed:
- the HTTP transport, wrapped in `SpacedTransport`;
- `KeyStore`, `Redactor`, `RecipeStore`;
- `SourceStore`, with `SourceRules.Migrate` on start;
- `AccountsCache` and `SharedAccounts`, `AccountClaims`;
- `ScoreBook`, plus `FinalsIndex.Load` and `ScoreBookReader.Load`, both on a worker thread before any watch exists. After that it subscribes to `ScoreBook.Written` and marshals each line to the UI thread;
- a report policy per watch (empty for `watch` and group-list sources);
- one `RecipeWatch` per source, created inside `SourceHost`;
- `NameClient`, `IconClient`, and following RoRoRo's theme.

**`src/Board/StarterBoards.cs`** decides, purely, which panels the fixed board shows and with which settings (§8). **`BoardWindow`** holds:
- the top bar: the period line, Start/Stop, Test now and Setup. Stage 2's tabs and Edit board are not drawn;
- a 12-column panel grid, laid out by the pure `BoardLayout` inside a small `PanelGrid` panel;
- the first-run states.

`App.xaml.cs` builds `AppServices` and shows the board, with a marked spot for Task 15's `--try` branch before the mutex. The window and taskbar icon follow the main source's recipe icon.

This task also removes the import screen's input boxes and its settings mode, since both belonged to `MainWindow`. A recipe with inputs now goes from the import screen to Setup › Clans, where the search replaces a plain text box. The first-run rule in §7.1 therefore holds after every first import, not only for a hand-placed recipe.

**Files:**
- Create: `src/Board/StarterBoards.cs`, `tests/StarterBoardsTests.cs`
- Create: `src/Board/BoardLayout.cs`, `tests/BoardLayoutTests.cs`
- Create: `src/UI/BoardText.cs`, `tests/BoardTextTests.cs`
- Create: `src/UI/Controls/PanelGrid.cs`
- Create: `src/Composition/AppServices.cs`
- Create: `src/UI/BoardWindow.xaml`, `src/UI/BoardWindow.xaml.cs`
- Modify: `src/App.xaml.cs`, `src/UI/ImportWindow.xaml`, `src/UI/ImportWindow.xaml.cs`, `src/UI/ImportText.cs`, `src/UI/Setup/ImportFlow.cs`
- Modify: `src/Recipes/RecipeEngine.cs` (one constant's text), `src/Core/ReportPolicy.cs` (one comment)
- Modify: `tests/RecipeWatchTests.cs`, `tests/ImportTextTests.cs`
- Delete: `src/UI/MainWindow.xaml`, `src/UI/MainWindow.xaml.cs`

**Interfaces:**
- Consumes:
  - Tasks 1–9 (the whole contract)
  - Task 10: `ImportText`, `CounterLookup`
  - Task 11: `ISetupServices`, `SetupWindow`, `SetupPages`, `SearchLists`, `RecipeWords`
  - Task 12: `ImportFlow`, `DiagnosticsModel.Misses`/`StateText`
  - Task 13: `PanelModels`, `PanelViews`, `LiveBoard`, `PanelType`, `PanelSettings`, `PanelText`
- Produces:
  - `Labs626.UrScore.Board`:
    - `sealed record PanelSpec(PanelType Type, int Span, PanelSettings Settings)`
    - `enum BoardEmpty { None, NoRecipes, NoStats, NoSources }`
    - `sealed record StarterBoard(string Name, BoardEmpty Empty, IReadOnlyList<PanelSpec> Panels, string? AnchorSourceId, string? RecipeSlug)` (with `string Key`)
    - `static class StarterBoards`, with members:
      - constants `Battle` and `Grind`
      - `StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources)`
      - `string? FirstStat(InstalledRecipe installed)`
    - `sealed record PanelPlacement(int Index, int Row, int Column, int Span)`
    - `static class BoardLayout`, with members:
      - constants `Columns = 12`, `NarrowWidth = 1100`, `SingleWidth = 720`
      - `int EffectiveSpan(int span, double width)`
      - `IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<int> spans, double width)`
  - `Labs626.UrScore.UI`:
    - `static class BoardText`, with members:
      - `string TopLine(LiveBoard, string? anchorSourceId)`, `string StateLine(LiveBoard, bool everStarted)`
      - `string DetailLine(LiveBoard, string? budgetWarning)`, `string Attribution(LiveBoard)`
      - `(string Line, string Detail, string Button) EmptyState(BoardEmpty, Recipe?)`
    - `sealed class PanelGrid : Panel` (attached `Span`)
    - `partial class BoardWindow : Window`, with `BoardWindow(AppServices services)`
    - `ImportText.InputsNote(Recipe)`
    - `ImportWindow` ctor becomes `(Recipe, ImportReviewResult, UpdateComparison, RecipeState?, IReadOnlyList<InstalledRecipe>, Func<IReadOnlyCollection<Guid>>, Func<string, string>, Func<Task<CounterLookup>>? readCounterNames)`
  - `Labs626.UrScore.Composition`: `sealed class AppServices : ISetupServices, IDisposable`, with everything in `ISetupServices` plus:
    - `AppServices(Dispatcher ui)`
    - `SourceHost Runner`, `NameClient Names`, `bool EverStarted`
    - `event Action<string?>? IconChanged`, `string? IconFileForBoard()`
    - `LiveBoard CurrentBoard()`
    - `Task LoadBookAsync()`, `Task StartAsync()`, `void Stop()`, `Task TestNowAsync()`, `void StartFollowingTheme()`

- [ ] **Step 1: Write the failing tests**

Create `tests/StarterBoardsTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class StarterBoardsTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);
    private static readonly Source Rival = SourceOf("s-00000003", Clan, "NovaForge", SourceRole.Watch);
    private static readonly Source SecondAltClan = SourceOf("s-00000004", Clan, "Z9", SourceRole.Mine);
    private static readonly Source TopSource = SourceOf("s-0000000a", TopList, null, SourceRole.Watch);

    [Fact]
    public void WithNoRecipesTheBoardAsksForAnImport()
    {
        var board = StarterBoards.Build([], []);

        Assert.Equal(BoardEmpty.NoRecipes, board.Empty);
        Assert.Empty(board.Panels);
    }

    [Fact]
    public void RecipesWithNothingTickedAskForStats()
    {
        var board = StarterBoards.Build([Installed(Clan), Installed(TopList)], [MainClan, TopSource]);

        Assert.Equal(BoardEmpty.NoStats, board.Empty);
        Assert.Equal(Clan.Slug, board.RecipeSlug);
    }

    [Fact]
    public void AClanRecipeWithNoSourcesAsksForTheMainClan()
    {
        var board = StarterBoards.Build([Installed(Clan, "value")], []);

        Assert.Equal(BoardEmpty.NoSources, board.Empty);
        Assert.Equal(Clan.Slug, board.RecipeSlug);
    }

    [Fact]
    public void TheBattleBoardFollowsTheSpecsGridOrder()
    {
        var slug = Clan.Slug;

        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [AltClan, Rival, MainClan, SecondAltClan, TopSource]);

        Assert.Equal(StarterBoards.Battle, board.Name);
        Assert.Equal(BoardEmpty.None, board.Empty);
        Assert.Equal(MainClan.Id, board.AnchorSourceId);
        Assert.Equal(
            new[]
            {
                (PanelType.Standing, 3), (PanelType.Standing, 3), (PanelType.Race, 6), (PanelType.MyAccounts, 5),
                (PanelType.PromotionCheck, 4), (PanelType.AccountCard, 3), (PanelType.Top, 4), (PanelType.PastPeriods, 4),
            },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id), board.Panels[0].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id), board.Panels[1].Settings);
        Assert.Equal(new[] { MainClan.Id, AltClan.Id, SecondAltClan.Id, Rival.Id }, board.Panels[2].Settings.SourceIds!.ToArray());
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[3].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: AltClan.Id, ToSourceId: MainClan.Id, Stat: "value"), board.Panels[4].Settings);
        Assert.Equal(new PanelSettings(slug, Stat: "value"), board.Panels[5].Settings);
        Assert.Equal(new PanelSettings(TopList.Slug, SourceId: TopSource.Id), board.Panels[6].Settings);
        Assert.Equal(new PanelSettings(slug, SourceId: MainClan.Id, Stat: "value"), board.Panels[7].Settings);
    }

    [Fact]
    public void WithoutAMainTheFirstClanLeadsAndThereIsNoPromotionCheck()
    {
        var board = StarterBoards.Build([Installed(Clan, "value")], [AltClan, SecondAltClan]);

        Assert.Equal(AltClan.Id, board.AnchorSourceId);
        Assert.Equal(SecondAltClan.Id, board.Panels[1].Settings.SourceId);
        Assert.Contains(board.Panels, p => p.Type == PanelType.Race);
        Assert.DoesNotContain(board.Panels, p => p.Type == PanelType.PromotionCheck);
    }

    [Fact]
    public void OneSourceHasNoRaceAndASwitchedOffTopListHasNoTopPanel()
    {
        var board = StarterBoards.Build([Installed(Clan, "value"), Installed(TopList)], [MainClan, TopSource with { Enabled = false }]);

        Assert.Equal(new[] { PanelType.Standing, PanelType.MyAccounts, PanelType.AccountCard, PanelType.PastPeriods },
            board.Panels.Select(p => p.Type).ToArray());
    }

    [Fact]
    public void WithNoPeriodRecipeTheBoardIsGrind()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

        var board = StarterBoards.Build([Installed(Profile, "diamonds", "eggs", "rank")], [profile]);

        Assert.Equal(StarterBoards.Grind, board.Name);
        Assert.Equal(new[] { (PanelType.ProfileStat, 6), (PanelType.ProfileStat, 6), (PanelType.Records, 3), (PanelType.AccountCard, 3) },
            board.Panels.Select(p => (p.Type, p.Span)).ToArray());
        Assert.Equal(new string?[] { "diamonds", "eggs", "diamonds", "diamonds" }, board.Panels.Select(p => p.Settings.Stat).ToArray());
        Assert.Equal(profile.Id, board.Panels[0].Settings.SourceId);
    }

    [Fact]
    public void TheFirstStatFallsBackToASentOneWhenNoneIsShown()
    {
        var installed = new InstalledRecipe(Profile, "", new RecipeState(
            Stats: new Dictionary<string, StatChoice> { ["rank"] = new(Send: true, MetricId: "ps99.rank") }));

        Assert.Equal("rank", StarterBoards.FirstStat(installed));
    }

    [Fact]
    public void TheKeyChangesOnlyWhenThePanelsDo()
    {
        var one = StarterBoards.Build([Installed(Clan, "value")], [MainClan]);
        var same = StarterBoards.Build([Installed(Clan, "value")], [MainClan]);
        var more = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        Assert.Equal(one.Key, same.Key);
        Assert.NotEqual(one.Key, more.Key);
    }
}
```

Create `tests/BoardLayoutTests.cs`

```csharp
using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class BoardLayoutTests
{
    private static readonly int[] Battle = [3, 3, 6, 5, 4, 3, 4, 4];

    [Fact]
    public void WideTheBattleBoardFillsTwelveColumnRows() =>
        Assert.Equal(
            new[] { (0, 0, 3), (0, 3, 3), (0, 6, 6), (1, 0, 5), (1, 5, 4), (1, 9, 3), (2, 0, 4), (2, 4, 4) },
            BoardLayout.Flow(Battle, 1280).Select(p => (p.Row, p.Column, p.Span)).ToArray());

    [Fact]
    public void NarrowSmallPanelsTakeHalfTheRowAndWiderOnesAllOfIt()
    {
        var placed = BoardLayout.Flow(Battle, 900);

        Assert.Equal(new[] { 6, 6, 6, 12, 6, 6, 6, 6 }, placed.Select(p => p.Span).ToArray());
        Assert.Equal(new[] { 0, 0, 1, 2, 3, 3, 4, 4 }, placed.Select(p => p.Row).ToArray());
    }

    [Fact]
    public void AtPhoneWidthEveryPanelTakesTheRow() =>
        Assert.All(BoardLayout.Flow(Battle, 600), p => Assert.Equal(BoardLayout.Columns, p.Span));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 12)]
    public void SpansStayInsideTheGrid(int span, int effective) =>
        Assert.Equal(effective, BoardLayout.EffectiveSpan(span, 1280));
}
```

Create `tests/BoardTextTests.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardTextTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);

    [Fact]
    public void TheTopLineGivesThePeriodAndTheNextRead()
    {
        var period = new ReadingPeriod("AutumnBattle", null, Now.AddHours(76));
        var live = Live([MainClan], [Installed(Clan, "value")],
            new Dictionary<string, RecipeSnapshot> { [MainClan.Id] = Snapshot(MainClan.Id, [], period: period) },
            running: true, lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-1) });

        Assert.Equal(PanelText.PeriodLine(period, Now, Now.AddMinutes(-1).AddSeconds(Clan.EffectiveEverySeconds)), BoardText.TopLine(live, MainClan.Id));
    }

    [Fact]
    public void WithoutAPeriodTheTopLineSaysHowOftenItReads()
    {
        var profile = SourceOf("s-00000009", Profile, null, SourceRole.Mine);
        var live = Live([profile], [Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>());

        Assert.Equal($"Reads every {StatText.Span(TimeSpan.FromSeconds(Profile.EffectiveEverySeconds))}", BoardText.TopLine(live, profile.Id));
    }

    [Fact]
    public void TheStateLineSaysStartedStoppedOrNamesASourceInTrouble()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        LiveBoard Board(bool running, params Source[] sources) => Live(sources, [Installed(Clan, "value")], snaps, running);

        Assert.Equal("Not started.", BoardText.StateLine(Board(false, MainClan, AltClan), everStarted: false));
        Assert.Equal("Stopped.", BoardText.StateLine(Board(false, MainClan, AltClan), everStarted: true));
        Assert.Equal("K0i2: Could not reach the data.", BoardText.StateLine(Board(true, MainClan, AltClan), everStarted: true));
        Assert.Equal("Reading 1 source.", BoardText.StateLine(Board(true, MainClan), everStarted: true));
    }

    [Fact]
    public void RoRoRoBeingDownTakesTheDetailLine()
    {
        var down = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = new RecipeSnapshot(WatchState.HostDown, null, [], [], 0) { SourceId = MainClan.Id },
        };

        Assert.Equal("RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.",
            BoardText.DetailLine(Live([MainClan], [Installed(Clan, "value")], down), "over budget"));
        Assert.Equal("over budget",
            BoardText.DetailLine(Live([MainClan], [Installed(Clan, "value")], new Dictionary<string, RecipeSnapshot>()), "over budget"));
    }

    [Fact]
    public void TheEmptyStatesUseTheSpecsWords()
    {
        var noRecipes = BoardText.EmptyState(BoardEmpty.NoRecipes, null);
        var noStats = BoardText.EmptyState(BoardEmpty.NoStats, Clan);
        var noSources = BoardText.EmptyState(BoardEmpty.NoSources, Clan);

        Assert.Equal(("Import a recipe to start", "Import recipe…"), (noRecipes.Line, noRecipes.Button));
        Assert.Equal(("No stats turned on yet", "Choose stats"), (noStats.Line, noStats.Button));
        Assert.Equal(("Choose your main clan", "Choose your main clan"), (noSources.Line, noSources.Button));
    }

    [Fact]
    public void TheAttributionCreditsRecipesThatAreRead() =>
        Assert.Equal(Clan.Credit, BoardText.Attribution(Live([MainClan], [Installed(Clan, "value"), Installed(Profile, "diamonds")], new Dictionary<string, RecipeSnapshot>())));
}
```

In `tests/ImportTextTests.cs`, add this test to the class:

```csharp
    [Fact]
    public void ARecipeWithInputsSaysWhereToPickThem()
    {
        var clan = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;
        var profile = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-profile.recipe.json")).Recipe!;

        Assert.Equal("After you import it, pick your clan in Setup › Clans.", ImportText.InputsNote(clan));
        Assert.Equal("", ImportText.InputsNote(profile));
    }
```

In `tests/RecipeWatchTests.cs`:
1. Add `using Labs626.UrScore.Book;` to the usings if it isn't there already.
2. Replace the whole `TheWindowConstructsExactlyOneRecipeWatch` test (the `[Fact]` and its method) with these two tests:

```csharp
    [Fact]
    public void OnlyTheCompositionRootConstructsARecipeWatch()
    {
        // F2: a watch built per cycle gets a fresh serialization guard, and a timer tick and a Test now
        // click could then both report one observation. The main window that held the one construction
        // is gone; AppServices now builds each source's watch, once, inside SourceHost's factory.
        var src = Path.Combine(RepoRoot(), "src");
        var builders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(src, f), Count: Regex.Matches(File.ReadAllText(f), Regex.Escape("new RecipeWatch(")).Count))
            .Where(f => f.Count > 0)
            .ToList();

        Assert.True(
            builders.Count == 1 && builders[0].Relative == Path.Combine("Composition", "AppServices.cs") && builders[0].Count == 1,
            $"RecipeWatch is constructed in: {string.Join(", ", builders.Select(b => $"{b.Relative} ({b.Count})"))}. "
            + "Expected exactly one construction, in Composition/AppServices.cs. A watch built per cycle regresses F2: "
            + "a fresh semaphore serializes nothing, and one observation can be reported twice.");
    }

    [Fact]
    public async Task TheSourceHostKeepsOneWatchPerSourceAcrossAppliesAndReads()
    {
        var built = 0;
        var engine = new FakeEngine(() => Reading("battle=A", Row(111, 1)));
        var host = new FakeHost(true, [MyAccount]);
        var source = new Source("s-0000abcd", PetSim.Slug, Clan, SourceRole.Mine);
        using var sources = new SourceHost(_ => { built++; return Watch(engine, host); }, _ => 180);

        sources.Apply([source]);
        await sources.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);
        var first = sources.WatchFor(source.Id);

        // A role change keeps the watch (RecipeWatch.UpdateSource); only a new source id gets a new one.
        sources.Apply([source with { Role = SourceRole.Main }]);
        await sources.RunAllNowAsync(BookLine.TriggerManual, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, sources.WatchFor(source.Id));
        Assert.Equal(1, built);
        Assert.Equal(2, engine.Calls);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: the build FAILS with `error CS0103: The name 'StarterBoards' does not exist in the current context`, `error CS0103: The name 'BoardLayout' does not exist in the current context`, `error CS0103: The name 'BoardText' does not exist in the current context` and `error CS0117: 'ImportText' does not contain a definition for 'InputsNote'`.

- [ ] **Step 3: Create the pure board classes**

Create `src/Board/StarterBoards.cs`

```csharp
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One panel on a board: its type, how many of the 12 columns it spans, and what it shows.</summary>
public sealed record PanelSpec(PanelType Type, int Span, PanelSettings Settings);

public enum BoardEmpty { None, NoRecipes, NoStats, NoSources }

public sealed record StarterBoard(string Name, BoardEmpty Empty, IReadOnlyList<PanelSpec> Panels, string? AnchorSourceId, string? RecipeSlug)
{
    /// <summary>Changes exactly when the panels or their settings change, so the window rebuilds only then.</summary>
    public string Key => string.Join("|", Panels.Select(p =>
        $"{p.Type}:{p.Span}:{p.Settings.Recipe}:{p.Settings.SourceId}:{string.Join(",", p.Settings.SourceIds ?? [])}:{p.Settings.ToSourceId}:{p.Settings.Stat}:{p.Settings.UserId}"));
}

/// <summary>
/// Stage 1's one fixed board (spec §8): Battle when a recipe with a period has ticked stats, else Grind. Pure,
/// so which panels appear for which sources is testable.
/// </summary>
public static class StarterBoards
{
    public const string Battle = "Battle";
    public const string Grind = "Grind";

    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources)
    {
        if (installed.Count == 0) return new StarterBoard(Battle, BoardEmpty.NoRecipes, [], null, null);

        var ticked = installed.Where(i => !i.Recipe.IsGroupList && i.State.TrackedStats(i.Recipe).Count > 0).ToList();
        if (ticked.Count == 0)
        {
            var first = installed.FirstOrDefault(i => !i.Recipe.IsGroupList) ?? installed[0];
            return new StarterBoard(Battle, BoardEmpty.NoStats, [], null, first.Recipe.Slug);
        }

        var enabled = sources.Where(s => s.Enabled).ToList();
        var withPeriod = ticked.Where(i => i.Recipe.Period is not null).ToList();
        return withPeriod.Count > 0 ? BattleBoard(installed, withPeriod, enabled) : GrindBoard(ticked, enabled);
    }

    /// <summary>The first shown stat in recipe order, else the first sent one.</summary>
    public static string? FirstStat(InstalledRecipe installed)
    {
        var shown = installed.State.ShownStats(installed.Recipe);
        if (shown.Count > 0) return shown[0].Key;

        var tracked = installed.State.TrackedStats(installed.Recipe);
        return RecipeStats.Offered(installed.Recipe, installed.State.StatChoices.Keys).FirstOrDefault(s => tracked.Contains(s.Key))?.Key;
    }

    private static StarterBoard BattleBoard(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<InstalledRecipe> withPeriod, IReadOnlyList<Source> enabled)
    {
        bool Of(Source s, InstalledRecipe r) => string.Equals(s.Recipe, r.Recipe.Slug, StringComparison.Ordinal);

        var main = enabled.FirstOrDefault(s => s.Role == SourceRole.Main && withPeriod.Any(r => Of(s, r)));
        var recipe = main is not null
            ? withPeriod.First(r => Of(main, r))
            : withPeriod.FirstOrDefault(r => enabled.Any(s => Of(s, r))) ?? withPeriod[0];
        var slug = recipe.Recipe.Slug;

        var mine = enabled.Where(s => Of(s, recipe) && s.Role == SourceRole.Mine).ToList();
        var watch = enabled.Where(s => Of(s, recipe) && s.Role == SourceRole.Watch).ToList();
        var anchor = main ?? mine.FirstOrDefault() ?? watch.FirstOrDefault();

        if (anchor is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Battle, BoardEmpty.NoSources, [], null, slug);

        var stat = FirstStat(recipe);
        var otherMine = mine.FirstOrDefault(s => s.Id != anchor?.Id);
        var panels = new List<PanelSpec>();

        // 1–2. Standing for the main (or leading) source and the first other clan your accounts are in.
        if (anchor is not null) panels.Add(new PanelSpec(PanelType.Standing, 3, new PanelSettings(slug, SourceId: anchor.Id)));
        if (otherMine is not null) panels.Add(new PanelSpec(PanelType.Standing, 3, new PanelSettings(slug, SourceId: otherMine.Id)));

        // 3. The race: main, mine and watch sources, up to five, when there are two to race.
        var race = new[] { main }.OfType<Source>().Concat(mine).Concat(watch)
            .DistinctBy(s => s.Id).Take(PanelModels.MaxRace).Select(s => s.Id).ToList();
        if (race.Count >= 2) panels.Add(new PanelSpec(PanelType.Race, 6, new PanelSettings(slug, SourceIds: race)));

        // 4. My accounts by the first shown stat.
        if (stat is not null) panels.Add(new PanelSpec(PanelType.MyAccounts, 5, new PanelSettings(slug, Stat: stat)));

        // 5. Promotion check: the first other clan -> main, when both exist.
        if (main is not null && otherMine is not null && stat is not null)
        {
            panels.Add(new PanelSpec(PanelType.PromotionCheck, 4, new PanelSettings(slug, SourceId: otherMine.Id, ToSourceId: main.Id, Stat: stat)));
        }

        // 6. Account card for the top account.
        if (stat is not null) panels.Add(new PanelSpec(PanelType.AccountCard, 3, new PanelSettings(slug, Stat: stat)));

        // 7. Top of the period, when its group-list source is on.
        var top = enabled.FirstOrDefault(s => installed.Any(i => Of(s, i) && i.Recipe.IsGroupList));
        if (top is not null) panels.Add(new PanelSpec(PanelType.Top, 4, new PanelSettings(top.Recipe, SourceId: top.Id)));

        // 8. Past periods for the main source, when the recipe reads them.
        if (anchor is not null && recipe.Recipe.Period?.Past is not null)
        {
            panels.Add(new PanelSpec(PanelType.PastPeriods, 4, new PanelSettings(slug, SourceId: anchor.Id, Stat: stat)));
        }

        return new StarterBoard(Battle, BoardEmpty.None, panels, anchor?.Id, slug);
    }

    private static StarterBoard GrindBoard(IReadOnlyList<InstalledRecipe> ticked, IReadOnlyList<Source> enabled)
    {
        var recipe = ticked.FirstOrDefault(r => enabled.Any(s => string.Equals(s.Recipe, r.Recipe.Slug, StringComparison.Ordinal))) ?? ticked[0];
        var slug = recipe.Recipe.Slug;
        var source = enabled.FirstOrDefault(s => string.Equals(s.Recipe, slug, StringComparison.Ordinal));

        if (source is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Grind, BoardEmpty.NoSources, [], null, slug);

        var stats = recipe.State.ShownStats(recipe.Recipe).Select(s => s.Key).Take(2).ToList();
        if (stats.Count == 0 && FirstStat(recipe) is { } only) stats.Add(only);

        var panels = stats
            .Select(stat => new PanelSpec(PanelType.ProfileStat, 6, new PanelSettings(slug, SourceId: source?.Id, Stat: stat)))
            .ToList();
        panels.Add(new PanelSpec(PanelType.Records, 3, new PanelSettings(slug, Stat: stats[0])));
        panels.Add(new PanelSpec(PanelType.AccountCard, 3, new PanelSettings(slug, Stat: stats[0])));

        return new StarterBoard(Grind, BoardEmpty.None, panels, source?.Id, slug);
    }
}
```

Create `src/Board/BoardLayout.cs`

```csharp
namespace Labs626.UrScore.Board;

public sealed record PanelPlacement(int Index, int Row, int Column, int Span);

/// <summary>
/// Panels flow in order across a 12-column grid (spec §9.2), wrapping to a new row when the next one doesn't
/// fit. Narrow windows widen panels the way the mock does: 3- and 4-wide become half, 5 and 7+ take the row.
/// </summary>
public static class BoardLayout
{
    public const int Columns = 12;
    public const double NarrowWidth = 1100;
    public const double SingleWidth = 720;

    public static int EffectiveSpan(int span, double width) =>
        width < SingleWidth ? Columns
        : width < NarrowWidth ? span switch { <= 4 => 6, 6 => 6, _ => Columns }
        : Math.Clamp(span, 1, Columns);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<int> spans, double width)
    {
        var placements = new List<PanelPlacement>();
        int row = 0, column = 0;

        for (var index = 0; index < spans.Count; index++)
        {
            var span = EffectiveSpan(spans[index], width);
            if (column + span > Columns)
            {
                row++;
                column = 0;
            }

            placements.Add(new PanelPlacement(index, row, column, span));
            column += span;
        }

        return placements;
    }
}
```

Create `src/UI/BoardText.cs`

```csharp
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

/// <summary>The board window's own lines: the top line, the state line, the empty states (spec §8).</summary>
public static class BoardText
{
    public const string HostDown = "RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m", or "Reads every 30m · next read in 12m" without a period.</summary>
    public static string TopLine(LiveBoard live, string? anchorSourceId)
    {
        var source = live.FindSource(anchorSourceId) ?? live.Sources.FirstOrDefault(s => s.Enabled);
        if (source is null || live.FindRecipe(source.Recipe)?.Recipe is not { } recipe) return "";

        DateTimeOffset? next = live.Running && live.LastRead.TryGetValue(source.Id, out var last)
            ? last.AddSeconds(recipe.EffectiveEverySeconds)
            : null;

        if (live.SnapshotOf(source.Id)?.Period is { } period) return PanelText.PeriodLine(period, live.Now, next);

        var every = $"Reads every {StatText.Span(TimeSpan.FromSeconds(recipe.EffectiveEverySeconds))}";
        return next is { } due ? $"{every} · {PanelText.NextRead(due, live.Now)}" : every;
    }

    public static string StateLine(LiveBoard live, bool everStarted)
    {
        if (!live.Running) return everStarted ? "Stopped." : "Not started.";

        var enabled = live.Sources.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0) return "Running, with nothing to read yet.";

        foreach (var source in enabled)
        {
            if (live.SnapshotOf(source.Id) is { } snapshot && !Healthy(snapshot.State))
            {
                return $"{live.SourceName(source)}: {DiagnosticsModel.StateText(snapshot.State)}";
            }
        }

        return enabled.Count == 1 ? "Reading 1 source." : $"Reading {enabled.Count} sources.";
    }

    public static string DetailLine(LiveBoard live, string? budgetWarning) =>
        live.Snapshots.Values.Any(s => s.State == WatchState.HostDown) ? HostDown : budgetWarning ?? "";

    /// <summary>Each recipe being read credits its data (spec §6.1 of the first design).</summary>
    public static string Attribution(LiveBoard live) =>
        string.Join(" ", live.Installed
            .Where(i => live.Sources.Any(s => s.Enabled && string.Equals(s.Recipe, i.Recipe.Slug, StringComparison.Ordinal)))
            .Select(i => i.Recipe.Credit)
            .Distinct(StringComparer.Ordinal));

    public static (string Line, string Detail, string Button) EmptyState(BoardEmpty empty, Recipe? recipe)
    {
        var group = recipe is null ? "source" : RecipeWords.Group(recipe);
        return empty switch
        {
            BoardEmpty.NoRecipes => ("Import a recipe to start",
                "A recipe says where numbers are. Ur Score reads them, keeps them in your score book, and hands the ones you choose to RoRoRo.",
                "Import recipe…"),
            BoardEmpty.NoStats => ("No stats turned on yet",
                "Choose which stats to show and send, and the board fills in from the next read.",
                "Choose stats"),
            BoardEmpty.NoSources => ($"Choose your main {group}",
                "Type a few letters of its name in Setup, and Ur Score finds which of your accounts are in it.",
                $"Choose your main {group}"),
            _ => ("", "", ""),
        };
    }

    private static bool Healthy(WatchState state) =>
        state is WatchState.Reporting or WatchState.Showing or WatchState.NoMatches or WatchState.HostDown or WatchState.SourceIdle;
}
```

In `src/UI/ImportText.cs`, add `using Labs626.UrScore.Board;` to the usings and add this method to the class:

```csharp
    /// <summary>A recipe with inputs has its values picked in Setup, where the search list helps.</summary>
    public static string InputsNote(Recipe recipe) =>
        recipe.IsGroupList || RecipeWords.MainInput(recipe) is not { } input
            ? ""
            : $"After you import it, pick {RecipeWords.Lower(input.Label)} in Setup › {RecipeWords.Groups(recipe)}.";
```

- [ ] **Step 4: Run the board tests**

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --filter "FullyQualifiedName~StarterBoardsTests|FullyQualifiedName~BoardLayoutTests|FullyQualifiedName~BoardTextTests|FullyQualifiedName~ImportTextTests"`
Expected: PASS, 24 tests. (`OnlyTheCompositionRootConstructsARecipeWatch` fails until Step 8, once `AppServices` exists and `MainWindow` is gone; it runs at the gate.)

- [ ] **Step 5: Create the panel grid**

Create `src/UI/Controls/PanelGrid.cs`

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>Lays panels out with <see cref="BoardLayout"/>: 12 columns, a gap between cells, each row as tall as its tallest panel.</summary>
public sealed class PanelGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(int), typeof(PanelGrid),
        new FrameworkPropertyMetadata(BoardLayout.Columns, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    public double Gap { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow([.. children.Select(GetSpan)], width);

        foreach (var placement in placements)
        {
            children[placement.Index].Measure(new Size(CellWidth(width, placement.Span), double.PositiveInfinity));
        }

        var rows = RowHeights(children, placements);
        return new Size(width, rows.Sum() + Gap * Math.Max(0, rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var placements = BoardLayout.Flow([.. children.Select(GetSpan)], finalSize.Width);
        var rows = RowHeights(children, placements);

        var tops = new double[rows.Count];
        for (var row = 1; row < rows.Count; row++) tops[row] = tops[row - 1] + rows[row - 1] + Gap;

        var column = ColumnWidth(finalSize.Width);
        foreach (var placement in placements)
        {
            var child = children[placement.Index];
            child.Arrange(new Rect(placement.Column * (column + Gap), tops[placement.Row], CellWidth(finalSize.Width, placement.Span), child.DesiredSize.Height));
        }

        return finalSize;
    }

    private static List<double> RowHeights(IReadOnlyList<UIElement> children, IReadOnlyList<PanelPlacement> placements)
    {
        var rows = new List<double>();
        foreach (var placement in placements)
        {
            while (rows.Count <= placement.Row) rows.Add(0);
            rows[placement.Row] = Math.Max(rows[placement.Row], children[placement.Index].DesiredSize.Height);
        }

        return rows;
    }

    private double ColumnWidth(double width) => Math.Max(0, (width - Gap * (BoardLayout.Columns - 1)) / BoardLayout.Columns);

    private double CellWidth(double width, int span) => ColumnWidth(width) * span + Gap * (span - 1);
}
```

- [ ] **Step 6: Create the composition root**

Create `src/Composition/AppServices.cs`

```csharp
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using Grpc.Core;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;
using Labs626.UrScore.UI;

namespace Labs626.UrScore.Composition;

using NameClient = Labs626.UrScore.Source.NameClient;
using IconClient = Labs626.UrScore.Source.IconClient;
using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Everything the app runs on, built once (spec §4.2, §5, §7). The one place a watch is constructed: each
/// source gets its watch from <see cref="CreateWatch"/>, called by <see cref="SourceHost"/> once per source
/// id, and updated in place after that (F2: a watch built per cycle has a fresh serialization guard, and
/// one observation could be reported twice).
/// </summary>
public sealed class AppServices : ISetupServices, IDisposable
{
    public const string PluginId = "626labs.ur-score";

    private const int TrailLimit = 400;

    /// <summary>How long to wait before asking RoRoRo for its theme again after it was not there.</summary>
    private static readonly TimeSpan ThemeRetry = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _ui;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly HttpClient _recipeHttp = new(HttpRecipeTransport.CreateHandler());
    private readonly HttpClient _namesHttp = new();
    private readonly HostClient _host = new(PluginId);

    /// <summary>Its own connection, so the long-lived theme stream never shares a channel with reports.</summary>
    private readonly HostClient _themeHost = new(PluginId);

    private readonly CancellationTokenSource _closing = new();
    private readonly RecipeEngine _engine;
    private readonly AccountClaims _claims;
    private readonly ScoreBook _book;
    private readonly IconClient _icons;
    private readonly SearchLists _searchLists;
    private readonly SourceStore _sourceStore = new(SourceStore.DefaultPath);
    private readonly List<string> _trail = [];
    private readonly Dictionary<string, RecipeSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastRead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _iconFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _iconTexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missesInTrail = new(StringComparer.Ordinal);
    private IReadOnlyList<HostAccount> _savedAccounts;
    private FinalsIndex? _finals;
    private int? _budgetWarnedCount;
    private bool _changePending;

    public AppServices(Dispatcher ui)
    {
        _ui = ui;
        Keys = new KeyStore(KeyStore.DefaultPath);
        var keys = Keys;
        Redactor = new Redactor(() => keys.Values());
        Store = new RecipeStore(RecipeStore.DefaultDirectory);
        Settings = Settings.Load();

        var transport = new SpacedTransport(new HttpRecipeTransport(_recipeHttp, RawDirectory, Redactor), _time, SpacedTransport.DefaultSpacing);
        _engine = new RecipeEngine(transport, Keys);
        _searchLists = new SearchLists(transport);

        AccountsCache = new AccountsCache(AccountsCache.DefaultPath);
        _savedAccounts = LoadSavedAccounts(AccountsCache);
        Accounts = new SharedAccounts(_host, AccountsCache, _time);
        _claims = new AccountClaims(_time);

        _book = new ScoreBook(BookFiles.DefaultRoot);
        Reader = new ScoreBookReader(BookFiles.DefaultRoot, _time);

        Names = new NameClient(_namesHttp);
        _icons = new IconClient(HttpRecipeTransport.CreateHandler(), IconClient.DefaultCacheDirectory, () => _time.GetUtcNow());

        Runner = new SourceHost(CreateWatch, IntervalFor);
        Runner.SnapshotReady += OnSnapshotReady;

        LoadRecipesAndSources();
    }

    // ---- ISetupServices ----

    public IReadOnlyList<InstalledRecipe> Installed { get; private set; } = [];

    public IReadOnlyList<string> RecipeProblems { get; private set; } = [];

    public IReadOnlyList<Source> Sources { get; private set; } = [];

    public RecipeStore Store { get; }

    public IKeyStore Keys { get; }

    public Redactor Redactor { get; }

    public Settings Settings { get; }

    public SharedAccounts Accounts { get; }

    public AccountsCache AccountsCache { get; }

    public IReadOnlyList<HostAccount> KnownAccounts => Accounts.Last?.Accounts is { Count: > 0 } listed ? listed : _savedAccounts;

    public IScoreBook Book => _book;

    public ScoreBookReader Reader { get; }

    public bool ReaderLoaded { get; private set; }

    public bool Running => Runner.Running;

    public IReadOnlyDictionary<string, RecipeSnapshot> Latest => _latest;

    public string? BudgetWarning { get; private set; }

    public string HostText => $"host={_host.HostVersion ?? "(not connected)"} reject={_host.RejectReason ?? "(none)"}";

    public string RawDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "last-response");

    public IReadOnlyList<string> Trail
    {
        get
        {
            lock (_trail) return [.. _trail];
        }
    }

    public event Action? Changed;

    // ---- the board ----

    public SourceHost Runner { get; }

    public NameClient Names { get; }

    /// <summary>Stopped after at least one Start this session, as opposed to never started.</summary>
    public bool EverStarted { get; private set; }

    /// <summary>The icon file the window should show, or null for Ur Score's own. Raised on the UI thread.</summary>
    public event Action<string?>? IconChanged;

    public DateTimeOffset? LastReadAt(string sourceId) => _lastRead.TryGetValue(sourceId, out var at) ? at : null;

    public (int Sent, int Dropped) PolicyCounts(string recipeSlug)
    {
        int sent = 0, dropped = 0;
        foreach (var source in Sources.Where(s => string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal)))
        {
            if (Runner.WatchFor(source.Id) is not { } watch) continue;
            sent += watch.Policy.Sent;
            dropped += watch.Policy.Dropped;
        }

        return (sent, dropped);
    }

    public string? IconFileFor(string recipeSlug) => _iconFiles.GetValueOrDefault(recipeSlug);

    /// <summary>The main source's recipe icon, else the first enabled source's that has one.</summary>
    public string? IconFileForBoard()
    {
        var source = Sources.FirstOrDefault(s => s.Enabled && s.Role == SourceRole.Main && _iconFiles.ContainsKey(s.Recipe))
                     ?? Sources.FirstOrDefault(s => s.Enabled && _iconFiles.ContainsKey(s.Recipe));
        return source is null ? null : _iconFiles[source.Recipe];
    }

    public LiveBoard CurrentBoard() => new(
        Sources, Installed,
        new Dictionary<string, RecipeSnapshot>(_latest, StringComparer.Ordinal),
        new Dictionary<string, DateTimeOffset>(_lastRead, StringComparer.Ordinal),
        KnownAccounts, _time, Runner.Running);

    /// <summary>
    /// Reads the finals index and the book on a worker thread, before any watch exists, then applies the
    /// sources. Nothing else touches the reader until this returns; after it, only the UI thread does.
    /// </summary>
    public async Task LoadBookAsync()
    {
        if (ReaderLoaded) return;

        var root = _book.Root;
        var reader = Reader;
        _finals = await Task.Run(() =>
        {
            var index = FinalsIndex.Load(root);
            reader.Load(BookFiles.Slugs(root));
            return index;
        });

        ReaderLoaded = true;
        _book.Written += OnWritten;
        Runner.Apply(Sources);
        AddTrail($"BOOK: loaded from {root}.");
        RaiseChanged();
    }

    public async Task StartAsync()
    {
        if (!ReaderLoaded || Runner.Running) return;

        await RefreshAccountsAsync(_closing.Token);
        Runner.Start();
        EverStarted = true;
        AddTrail("STARTED");
        RaiseChanged();
    }

    public void Stop()
    {
        Runner.Stop();
        AddTrail("STOPPED");
        RaiseChanged();
    }

    public async Task TestNowAsync()
    {
        if (!ReaderLoaded) return;

        await RefreshAccountsAsync(_closing.Token);
        await Runner.RunAllNowAsync(BookLine.TriggerManual, _closing.Token);
    }

    public void SaveSources(IReadOnlyList<Source> sources)
    {
        _sourceStore.Save(sources);
        Sources = sources;
        ApplySources();
    }

    public void ReloadRecipes()
    {
        LoadRecipesAndSources();
        ApplySources();
    }

    public void SaveRecipeState(Recipe recipe, RecipeState state)
    {
        Store.SaveState(recipe, state);
        LoadInstalled();
        ApplySources();
    }

    public void RemoveRecipe(string slug)
    {
        // The recipe file and its state go; its score book stays (spec §5.1).
        Store.Remove(slug);
        LoadInstalled();
        var sources = SourceRules.ForgetRecipe(Sources, slug);
        _sourceStore.Save(sources);
        Sources = sources;
        ApplySources();
    }

    public async Task<RecipeSnapshot?> ReadOnceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (!ReaderLoaded) return null;

        await RefreshAccountsAsync(cancellationToken);
        if (Runner.WatchFor(sourceId) is not { } watch) return null;

        var snapshot = await watch.RunOnceAsync(cancellationToken, BookLine.TriggerManual);
        Record(sourceId, snapshot, _time.GetUtcNow());
        return snapshot;
    }

    public Task<SearchListResult> SearchListAsync(RecipeSearch search, CancellationToken cancellationToken) =>
        _searchLists.GetAsync(search, cancellationToken);

    /// <summary>
    /// One read with every recipe value asked for, so the response can offer its counter names. Its reading
    /// goes to no report policy and no book: nothing read here can reach RoRoRo or disk.
    /// </summary>
    public async Task<CounterLookup> ReadCounterNamesAsync(Recipe recipe, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAccountsAsync(cancellationToken);
            var ids = KnownAccounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).Distinct().ToList();
            var everyValue = recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            var inputs = Sources.FirstOrDefault(s => string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))?.Inputs
                         ?? new Dictionary<string, string>();

            var reading = await _engine.ReadAsync(recipe, inputs, ids, everyValue, cancellationToken);
            AddTrail($"READ STAT NAMES: {reading.Outcome}, {reading.CounterNames.Count} name(s). {reading.Detail}");

            if (reading.CounterNames.Count > 0) return new CounterLookup(reading.CounterNames, null);

            var problem = reading.Outcome != ReadingOutcome.Read ? reading.Detail
                : recipe.LastStep.PerAccount && ids.Count == 0 ? "RoRoRo hasn't shared any accounts yet, so there was nothing to read."
                : $"The source answered, but no {recipe.LastStep.Counters?.Label ?? "statistic names"} came back.";
            return new CounterLookup([], Redactor.Redact(problem));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CounterLookup([], Redactor.Redact($"Could not read them: {ex.Message}"));
        }
    }

    public async Task<AccountList> RefreshAccountsAsync(CancellationToken cancellationToken)
    {
        var list = await Accounts.GetAsync(cancellationToken);
        if (list.Accounts.Count > 0) _savedAccounts = list.Accounts;
        if (list.Denied) AddTrail($"ACCOUNTS REFUSED: {RecipeWatch.RejectedMessage("host.queries.accounts")}");

        RefreshPolicies();
        WarnPastBudget();
        RaiseChanged();
        return list;
    }

    public void AddTrail(string text)
    {
        var line = $"{_time.GetUtcNow():O} {Redactor.Redact(text)}";
        lock (_trail)
        {
            _trail.Add(line);
            if (_trail.Count > TrailLimit) _trail.RemoveRange(0, _trail.Count - TrailLimit);
        }
    }

    /// <summary>Follows RoRoRo's theme for as long as the app runs, asking again every 15 s while RoRoRo is away.</summary>
    public void StartFollowingTheme() => _ = FollowThemeAsync(_closing.Token);

    public void Dispose()
    {
        _closing.Cancel();
        Runner.SnapshotReady -= OnSnapshotReady;
        Runner.Stop();
        Runner.Dispose();

        // Pending lines are flushed on exit (spec §5.7).
        _book.Flush();
        _book.Dispose();

        _themeHost.Dispose();
        _host.Dispose();
        _recipeHttp.Dispose();
        _namesHttp.Dispose();
    }

    // ---- watches ----

    private RecipeWatch? CreateWatch(Source source)
    {
        if (FindInstalled(source.Recipe) is not { } installed) return null;

        return new RecipeWatch(
            _engine, _host, Keys, PolicyFor(installed, source), installed.Recipe, source.Inputs, TrackedFor(installed),
            _book, source, Accounts, installed.Text, _claims, _finals, _time);
    }

    private int IntervalFor(Source source) =>
        FindInstalled(source.Recipe)?.Recipe.EffectiveEverySeconds ?? Recipe.MinimumEverySeconds;

    /// <summary>A group list has no ticks (it records and sends nothing), so it asks for every value it offers.</summary>
    private static IReadOnlySet<string> TrackedFor(InstalledRecipe installed) =>
        installed.Recipe.IsGroupList
            ? installed.Recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal)
            : installed.State.TrackedStats(installed.Recipe);

    /// <summary>A watched source and a group list send nothing, whatever the recipe's ticks say (spec §4.1, §3.5).</summary>
    private ReportPolicy PolicyFor(InstalledRecipe installed, Source source)
    {
        if (source.Role == SourceRole.Watch || installed.Recipe.IsGroupList) return new ReportPolicy([], new HashSet<Guid>());

        var allowed = KnownAccounts.Select(a => a.AccountId).Where(id => !installed.State.Excluded.Contains(id)).ToHashSet();
        return new ReportPolicy(installed.State.SentStats(installed.Recipe), allowed);
    }

    /// <summary>After a change to sources, recipes or ticks: each kept watch takes its source, recipe, stats and policy.</summary>
    private void RefreshWatches()
    {
        foreach (var source in Sources)
        {
            if (Runner.WatchFor(source.Id) is not { } watch || FindInstalled(source.Recipe) is not { } installed) continue;

            watch.UpdateSource(source);
            watch.UpdateRecipe(installed.Recipe, source.Inputs, TrackedFor(installed), installed.Text);
            var policy = PolicyFor(installed, source);
            watch.UpdatePolicy(policy.SentStats, policy.AllowedSubjects);
        }
    }

    /// <summary>After RoRoRo lists accounts: only the allow lists change, so a held stop stays held.</summary>
    private void RefreshPolicies()
    {
        foreach (var source in Sources)
        {
            if (Runner.WatchFor(source.Id) is not { } watch || FindInstalled(source.Recipe) is not { } installed) continue;
            var policy = PolicyFor(installed, source);
            watch.UpdatePolicy(policy.SentStats, policy.AllowedSubjects);
        }
    }

    private void ApplySources()
    {
        if (ReaderLoaded)
        {
            Runner.Apply(Sources);
            RefreshWatches();
        }

        foreach (var gone in _latest.Keys.Where(id => Sources.All(s => s.Id != id)).ToList())
        {
            _latest.Remove(gone);
            _lastRead.Remove(gone);
        }

        WarnPastBudget();
        RaiseChanged();
    }

    // ---- reads arriving ----

    private void OnSnapshotReady(string sourceId, RecipeSnapshot snapshot)
    {
        var at = _time.GetUtcNow();
        _ui.BeginInvoke(() => Record(sourceId, snapshot, at));
    }

    private void OnWritten(BookLine line) => _ui.BeginInvoke(() =>
    {
        Reader.Apply(line);
        RaiseChanged();
    });

    private void Record(string sourceId, RecipeSnapshot snapshot, DateTimeOffset at)
    {
        _latest[sourceId] = snapshot;
        _lastRead[sourceId] = at;
        AddTrail($"{sourceId} {snapshot.State}: {snapshot.Detail}");
        TrailMisses(sourceId, snapshot);
        SaveCounterNames(sourceId, snapshot);
        RefreshPolicies();
        WarnPastBudget();
        _ = ApplyIconAsync(sourceId, snapshot);
        RaiseChanged();
    }

    /// <summary>Each miss goes to the trail once (by source and text). Only your own accounts are ever named.</summary>
    private void TrailMisses(string sourceId, RecipeSnapshot snapshot)
    {
        var recipe = FindInstalled(Sources.FirstOrDefault(s => s.Id == sourceId)?.Recipe)?.Recipe;
        var misses = DiagnosticsModel.Misses(recipe, snapshot, KnownAccounts);
        if (misses.Length == 0) return;

        if (_missesInTrail.Count > 1000) _missesInTrail.Clear();
        foreach (var line in misses.Split(Environment.NewLine))
        {
            if (_missesInTrail.Add($"{sourceId}|{line}")) AddTrail($"NOT READ: {sourceId} {line}");
        }
    }

    /// <summary>Counter names from a successful read, kept in the recipe's state for the Stats table.</summary>
    private void SaveCounterNames(string sourceId, RecipeSnapshot snapshot)
    {
        if (snapshot.CounterNames.Count == 0) return;
        if (FindInstalled(Sources.FirstOrDefault(s => s.Id == sourceId)?.Recipe) is not { Recipe.LastStep.Counters: not null } installed) return;
        if (snapshot.CounterNames.SequenceEqual(installed.State.SavedCounterNames, StringComparer.Ordinal)) return;

        try
        {
            var state = installed.State with { CounterNames = [.. snapshot.CounterNames] };
            Store.SaveState(installed.Recipe, state);
            Installed = [.. Installed.Select(i => ReferenceEquals(i, installed) ? i with { State = state } : i)];
        }
        catch (Exception ex)
        {
            AddTrail($"COUNTER NAMES NOT SAVED: {ex.Message}");
        }
    }

    /// <summary>Accounts that arrive with Send on were never checked against RoRoRo's history limit. Says so once per count.</summary>
    private void WarnPastBudget()
    {
        try
        {
            var ids = KnownAccounts.Select(a => a.AccountId).ToList();
            var over = ids.Count == 0 ? null : HistoryBudget.AfterSeed(Installed.Where(i => !i.Recipe.IsGroupList), ids);
            BudgetWarning = over?.Line;
            if (over is null || _budgetWarnedCount == over.Count) return;

            _budgetWarnedCount = over.Count;
            AddTrail($"BUDGET: {over.Line}");
        }
        catch (Exception ex)
        {
            AddTrail($"BUDGET NOT CHECKED: {ex.Message}");
        }
    }

    /// <summary>The recipe's icon, fetched once per icon text (stats design §3.3). Anything that fails keeps Ur Score's own.</summary>
    private async Task ApplyIconAsync(string sourceId, RecipeSnapshot snapshot)
    {
        if (snapshot.IconText is not { } iconText) return;
        if (Sources.FirstOrDefault(s => s.Id == sourceId) is not { } source) return;
        if (FindInstalled(source.Recipe)?.Recipe is not { Icon: not null } recipe) return;
        if (string.Equals(_iconTexts.GetValueOrDefault(recipe.Slug), iconText, StringComparison.Ordinal)) return;

        _iconTexts[recipe.Slug] = iconText;

        string? file;
        try
        {
            file = await _icons.ResolveAsync(iconText, RecipeHosts.ContactedBy(recipe), _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A newer icon text arrived while this one was fetching.
        if (!string.Equals(_iconTexts.GetValueOrDefault(recipe.Slug), iconText, StringComparison.Ordinal)) return;

        if (file is null)
        {
            _iconFiles.Remove(recipe.Slug);
            AddTrail("ICON: the recipe's icon could not be fetched, so the window keeps Ur Score's.");
        }
        else
        {
            _iconFiles[recipe.Slug] = file;
        }

        IconChanged?.Invoke(IconFileForBoard());
        RaiseChanged();
    }

    // ---- loading ----

    private void LoadRecipesAndSources()
    {
        LoadInstalled();

        IReadOnlyList<Source> existing;
        try
        {
            existing = _sourceStore.Load();
        }
        catch (Exception ex)
        {
            AddTrail($"SOURCES NOT READ: {ex.Message}");
            existing = [];
        }

        // Part 2a's single state.json inputs become sources; a recipe without inputs gets its one source (spec §4.1).
        var migrated = SourceRules.Migrate(Installed, existing);
        try
        {
            _sourceStore.Save(migrated);
        }
        catch (Exception ex)
        {
            AddTrail($"SOURCES NOT SAVED: {ex.Message}");
        }

        Sources = migrated;
    }

    private void LoadInstalled()
    {
        var load = Store.LoadAll();
        Installed = load.Recipes;
        RecipeProblems = [.. load.Problems.Select(p => Redactor.Redact(p))];
        foreach (var problem in RecipeProblems) AddTrail($"RECIPE FILE SKIPPED: {problem}");
    }

    private InstalledRecipe? FindInstalled(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    private static IReadOnlyList<HostAccount> LoadSavedAccounts(AccountsCache cache)
    {
        try
        {
            return cache.Load();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Coalesced: many reads and lines in one dispatcher pass redraw once.</summary>
    private void RaiseChanged()
    {
        if (_changePending) return;
        _changePending = true;
        _ui.BeginInvoke(() =>
        {
            _changePending = false;
            Changed?.Invoke();
        }, DispatcherPriority.Background);
    }

    private async Task FollowThemeAsync(CancellationToken cancellationToken)
    {
        var following = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _themeHost.FollowThemeAsync(palette => _ui.InvokeAsync(() =>
                {
                    ThemeService.Apply(ThemeService.Current.Merge(palette));
                    if (following) return;
                    following = true;
                    AddTrail("THEME: following RoRoRo's theme.");
                }), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
            {
                AddTrail("THEME: this RoRoRo has no theme feed, so the window keeps RoRoRo's Brand colours.");
                return;
            }
            catch (Exception ex)
            {
                if (following)
                {
                    following = false;
                    AddTrail($"THEME: stopped following RoRoRo's theme ({ex.GetType().Name}); colours stay as they are.");
                }
            }

            try
            {
                await Task.Delay(ThemeRetry, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

- [ ] **Step 7: Create the board window**

Create `src/UI/BoardWindow.xaml`

```xml
<Window x:Class="Labs626.UrScore.UI.BoardWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:Labs626.UrScore.UI"
        Title="RoRoRo Ur Score" Height="900" Width="1280" MinHeight="560" MinWidth="720"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <DockPanel>
        <!-- The top bar (spec §8). Stage 2 adds tabs on the left and Edit board beside Setup. -->
        <Border DockPanel.Dock="Top" BorderBrush="{DynamicResource DividerBrush}" BorderThickness="0,0,0,1" Padding="16,10">
            <DockPanel LastChildFill="True">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <Button x:Name="StartStopButton" Content="Start" Style="{StaticResource PrimaryButton}" Click="OnStartStopClick"
                            AutomationProperties.Name="Start or stop reading" />
                    <Button x:Name="TestNowButton" Content="Test now" Margin="8,0,0,0" Click="OnTestNowClick"
                            AutomationProperties.Name="Read every source once now" />
                    <Button x:Name="SetupButton" Margin="8,0,0,0" Click="OnSetupClick" AutomationProperties.Name="Setup">
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text="&#xE713;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" VerticalAlignment="Center" Margin="0,0,6,0" />
                            <TextBlock Text="Setup" VerticalAlignment="Center" />
                        </StackPanel>
                    </Button>
                </StackPanel>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Image x:Name="BoardIcon" Width="22" Height="22" Margin="0,0,10,0" Visibility="Collapsed"
                           AutomationProperties.Name="Main source icon" />
                    <Ellipse x:Name="LiveDot" Width="8" Height="8" Margin="0,0,8,0" Fill="{DynamicResource CyanBrush}" Visibility="Collapsed" />
                    <TextBlock x:Name="PeriodLine" Style="{StaticResource Muted}" VerticalAlignment="Center" TextWrapping="NoWrap"
                               TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </DockPanel>
        </Border>

        <StackPanel DockPanel.Dock="Top" Margin="16,8,16,0">
            <TextBlock x:Name="StateLine" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.HelpText="What Ur Score is doing" />
            <TextBlock x:Name="DetailLine" Style="{StaticResource Muted}" Margin="0,2,0,0" />
        </StackPanel>

        <TextBlock x:Name="AttributionLine" DockPanel.Dock="Bottom" Style="{StaticResource Muted}" FontSize="11" Margin="16,4,16,8" />

        <Grid>
            <ScrollViewer x:Name="BoardScroll" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="14">
                <ui:PanelGrid x:Name="BoardPanels" AutomationProperties.Name="Board" />
            </ScrollViewer>
            <StackPanel x:Name="EmptyState" Visibility="Collapsed" HorizontalAlignment="Center" VerticalAlignment="Center" MaxWidth="480">
                <TextBlock x:Name="EmptyStateLine" Style="{StaticResource Heading}" TextAlignment="Center" />
                <TextBlock x:Name="EmptyStateDetail" Style="{StaticResource Muted}" TextAlignment="Center" Margin="0,8,0,0" />
                <Button x:Name="EmptyStateButton" Style="{StaticResource PrimaryButton}" HorizontalAlignment="Center" Margin="0,16,0,0"
                        Click="OnEmptyStateClick" />
            </StackPanel>
        </Grid>
    </DockPanel>
</Window>
```

Create `src/UI/BoardWindow.xaml.cs`

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The starter board (spec §8): the top bar, the fixed panels in a 12-column grid, and the first-run states.
/// Everything it shows comes from <see cref="AppServices"/>; everything that is setup is in the Setup window.
/// </summary>
public partial class BoardWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly List<(PanelSpec Spec, FrameworkElement View)> _panels = [];

    /// <summary>Other members' names for a Live leaderboard panel, in memory only, each id asked once.</summary>
    private readonly Dictionary<long, string> _names = [];
    private readonly HashSet<long> _askedNames = [];

    private SetupWindow? _setup;
    private StarterBoard? _board;
    private string? _boardKey;
    private bool _busy;

    public BoardWindow(AppServices services)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += Render;
        _services.IconChanged += ApplyIcon;
        _clock.Tick += (_, _) => RenderLines();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _services.Changed -= Render;
            _services.IconChanged -= ApplyIcon;
        };

        StartStopButton.IsEnabled = false;
        TestNowButton.IsEnabled = false;
        Render();
        StateLine.Text = "Reading your score book…";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _services.StartFollowingTheme();
        _clock.Start();

        try
        {
            await _services.LoadBookAsync();
        }
        catch (Exception ex)
        {
            StateLine.Text = "Your score book could not be read.";
            DetailLine.Text = _services.Redactor.Redact(ex.Message);
            _services.AddTrail($"BOOK NOT LOADED: {ex}");
            return;
        }

        StartStopButton.IsEnabled = true;
        TestNowButton.IsEnabled = true;
        Render();

        // Spec §7.1: a recipe with inputs and no sources opens Setup on its Clans page.
        if (SetupPages.FirstRunPage(_services.Installed, _services.Sources) is { } page) OpenSetup(page);
    }

    private void Render()
    {
        var board = StarterBoards.Build(_services.Installed, _services.Sources);
        _board = board;
        RenderEmpty(board);

        if (board.Key != _boardKey)
        {
            _boardKey = board.Key;
            BoardPanels.Children.Clear();
            _panels.Clear();

            var counts = new Dictionary<PanelType, int>();
            foreach (var spec in board.Panels)
            {
                counts[spec.Type] = counts.GetValueOrDefault(spec.Type) + 1;
                var view = PanelViews.Create(spec.Type);
                AutomationProperties.SetAutomationId(view, $"{spec.Type}Panel{counts[spec.Type]}");
                PanelGrid.SetSpan(view, spec.Span);
                BoardPanels.Children.Add(view);
                _panels.Add((spec, view));
            }
        }

        var live = _services.CurrentBoard();
        foreach (var (spec, view) in _panels)
        {
            try
            {
                PanelViews.Render(view, spec.Settings, live, _services.Reader, _names);
            }
            catch (Exception ex)
            {
                // A panel never takes the window down.
                _services.AddTrail($"PANEL NOT DRAWN: {spec.Type}: {ex.Message}");
            }
        }

        _ = ResolveNamesAsync(live);
        RenderLines(live);
    }

    private void RenderLines(LiveBoard? live = null)
    {
        live ??= _services.CurrentBoard();

        PeriodLine.Text = BoardText.TopLine(live, _board?.AnchorSourceId);
        LiveDot.Visibility = live.Running ? Visibility.Visible : Visibility.Collapsed;
        StartStopButton.Content = live.Running ? "Stop" : "Start";
        AttributionLine.Text = BoardText.Attribution(live);

        if (!_services.ReaderLoaded) return;
        StateLine.Text = BoardText.StateLine(live, _services.EverStarted);
        DetailLine.Text = BoardText.DetailLine(live, _services.BudgetWarning);
    }

    private void RenderEmpty(StarterBoard board)
    {
        var recipe = _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, board.RecipeSlug, StringComparison.Ordinal))?.Recipe;
        var (line, detail, button) = BoardText.EmptyState(board.Empty, recipe);
        var empty = board.Empty != BoardEmpty.None;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        BoardScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateLine.Text = line;
        EmptyStateDetail.Text = detail;
        EmptyStateButton.Content = button;
        AutomationProperties.SetName(EmptyStateButton, button);
    }

    /// <summary>Names for Live leaderboard panels only, and only when resolveNames allows; the stage 1 starter boards have none.</summary>
    private async Task ResolveNamesAsync(LiveBoard live)
    {
        if (!_services.Settings.ResolveNames) return;

        var mine = live.MyUserIds;
        var ids = _panels
            .Where(p => p.Spec.Type == PanelType.LiveLeaderboard)
            .Select(p => live.FindSource(p.Spec.Settings.SourceId))
            .OfType<Source>()
            .SelectMany(s => live.SnapshotOf(s.Id)?.Rows ?? [])
            .Select(r => r.UserId)
            .Where(id => !mine.Contains(id) && _askedNames.Add(id))
            .ToList();
        if (ids.Count == 0) return;

        var resolved = await _services.Names.ResolveAsync(ids, CancellationToken.None);
        foreach (var (id, name) in resolved) _names[id] = name;
        if (resolved.Count > 0) Render();
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        StartStopButton.IsEnabled = false;

        try
        {
            if (_services.Running)
            {
                _services.Stop();
            }
            else if (_services.Installed.Count == 0)
            {
                StateLine.Text = "No recipe to run.";
                DetailLine.Text = "Import a recipe first.";
            }
            else
            {
                await _services.StartAsync();
            }
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _busy = false;
            StartStopButton.IsEnabled = _services.ReaderLoaded;
            RenderLines();
        }
    }

    private async void OnTestNowClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_services.Installed.Count == 0)
        {
            StateLine.Text = "No recipe to test.";
            DetailLine.Text = "Import a recipe first.";
            return;
        }

        _busy = true;
        TestNowButton.IsEnabled = false;
        StateLine.Text = "Reading every source once…";

        try
        {
            await _services.TestNowAsync();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
        finally
        {
            _busy = false;
            TestNowButton.IsEnabled = _services.ReaderLoaded;
            RenderLines();
        }
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) => OpenSetup(null);

    private async void OnEmptyStateClick(object sender, RoutedEventArgs e)
    {
        if (_board is null || _busy) return;

        if (_board.Empty == BoardEmpty.NoStats)
        {
            OpenSetup(SetupPages.Stats);
            return;
        }

        if (_board.Empty == BoardEmpty.NoSources && _board.RecipeSlug is { } slug)
        {
            OpenSetup(SetupPages.ClansId(slug));
            return;
        }

        if (_board.Empty != BoardEmpty.NoRecipes) return;

        _busy = true;
        try
        {
            var outcome = await ImportFlow.RunAsync(this, _services);
            if (outcome is null) return;

            DetailLine.Text = outcome.Message;
            if (outcome.ChooseSources) OpenSetup(SetupPages.ClansId(outcome.Slug));
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenSetup(string? page)
    {
        if (_setup is not null)
        {
            if (page is not null) _setup.ShowPage(page);
            _setup.Activate();
            return;
        }

        _setup = new SetupWindow(_services, page) { Owner = this };
        _setup.Closed += (_, _) => _setup = null;
        _setup.Show();
    }

    private void ShowFailure(Exception ex)
    {
        // The window must never die on a cycle.
        StateLine.Text = "Something unexpected went wrong.";
        DetailLine.Text = _services.Redactor.Redact(ex.Message);
        _services.AddTrail($"EXCEPTION: {ex}");
    }

    /// <summary>The main source's icon on the window, the taskbar and the top bar; anything that fails keeps Ur Score's own.</summary>
    private void ApplyIcon(string? file)
    {
        if (file is null)
        {
            ResetIcon();
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();

            Icon = image;
            BoardIcon.Source = image;
            BoardIcon.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResetIcon();
            _services.AddTrail($"ICON: the picture did not decode ({ex.GetType().Name}), so the window keeps Ur Score's.");
        }
    }

    private void ResetIcon()
    {
        ClearValue(IconProperty);
        BoardIcon.Source = null;
        BoardIcon.Visibility = Visibility.Collapsed;
    }
}
```

- [ ] **Step 8: Build the app on the new window, and retire the old one**

Replace the whole of `src/App.xaml.cs` with

```csharp
using System.Threading;
using System.Windows;

namespace Labs626.UrScore;

public partial class App : Application
{
    /// <summary>
    /// One copy. Two instances polling the same clan would double the request rate against
    /// someone else's API and report every number twice, which the host would see as a rate
    /// computed over duplicated samples.
    /// </summary>
    private const string SingleInstanceName = @"Local\626labs.ur-score.single-instance";

    private Mutex? _instance;
    private bool _owns;
    private Composition.AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Task 15 adds the --try branch HERE, first, before the single-instance mutex: a try-out runs no
        // window, no RoRoRo, no state, no book and no mutex (spec §10).

        _instance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        _owns = isFirst;

        if (!isFirst)
        {
            // Do NOT release: this process never owned it, and releasing a mutex you do not own
            // throws ApplicationException — on the exact path this guard exists to serve.
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Theming.ThemeService.Start();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _services = new Composition.AppServices(Dispatcher);
        var board = new UI.BoardWindow(_services);
        MainWindow = board;
        board.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Flushes the score book's pending lines before the process goes.
        _services?.Dispose();
        if (_owns) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
```

Delete `src/UI/MainWindow.xaml` and `src/UI/MainWindow.xaml.cs`:

```text
git rm src/UI/MainWindow.xaml src/UI/MainWindow.xaml.cs
```

Replace the whole of `src/UI/ImportWindow.xaml` with (the input boxes are gone; a recipe with inputs names where they're picked)

```xml
<Window x:Class="Labs626.UrScore.UI.ImportWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="clr-namespace:Labs626.UrScore.UI"
        Title="Import recipe" Width="680" SizeToContent="Height" ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{DynamicResource BgBrush}" Foreground="{DynamicResource WhiteBrush}"
        FontFamily="{StaticResource BodyFont}">
    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="860">
        <StackPanel Margin="20,18,20,18">
            <TextBlock x:Name="NameLine" Style="{StaticResource Heading}" />
            <TextBlock x:Name="CreditLine" Margin="0,4,0,0" Style="{StaticResource Muted}" />
            <TextBlock x:Name="AuthorLine" Margin="0,2,0,0" Style="{StaticResource Muted}" FontStyle="Italic" />

            <TextBlock Text="YOUR PC WILL CONTACT" Style="{StaticResource SectionLabel}" Margin="0,18,0,6" />
            <ItemsControl x:Name="HostsList" AutomationProperties.Name="Every host this recipe contacts and what each receives">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Style="{StaticResource Card}" Padding="12,8" Margin="0,0,0,6">
                            <StackPanel>
                                <TextBlock Text="{Binding Host}" FontWeight="SemiBold" FontFamily="{StaticResource MonoFont}" />
                                <TextBlock Text="{Binding SendsText}" Style="{StaticResource Muted}" Margin="0,2,0,0" />
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <TextBlock x:Name="PollLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ReusedLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="ChangesLine" Margin="0,4,0,0" TextWrapping="Wrap" />
            <TextBlock x:Name="InputsNote" Margin="0,8,0,0" TextWrapping="Wrap" />

            <!-- Spec §7.3 and §14: what every successful read writes to the score book. -->
            <TextBlock Text="KEPT IN YOUR SCORE BOOK" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
            <TextBlock x:Name="KeptNote" Style="{StaticResource Muted}" />
            <ItemsControl x:Name="KeptList" Margin="0,4,0,0" AutomationProperties.Name="Kept in your score book">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding}" Margin="0,2,0,0" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Nothing is ticked for the user: a recipe that pre-ticked a stat would be deciding what gets
                 sent. A group list has no stats to tick at all. -->
            <StackPanel x:Name="StatsSection">
                <TextBlock Text="STATS" Style="{StaticResource SectionLabel}" Margin="0,16,0,4" />
                <ui:StatsTable x:Name="StatsTable" />
            </StackPanel>

            <TextBlock x:Name="ImportRefusalLine" Margin="0,12,0,0" Style="{StaticResource Refusal}" Visibility="Collapsed" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,18,0,0">
                <Button Content="Cancel" IsCancel="True" Margin="0,0,8,0" />
                <Button x:Name="ImportButton" Content="Import" IsDefault="True" Style="{StaticResource PrimaryButton}"
                        Click="OnImportClick" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Window>
```

Replace the whole of `src/UI/ImportWindow.xaml.cs` with

```csharp
using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// The safety screen (spec §6.2, stats design §7.1), shown before anything a recipe describes runs:
/// every host it contacts and exactly what each receives, what the score book keeps, and the Stats
/// table. Input values are picked afterwards in Setup, where the search list helps. Holds no HTTP of its
/// own; the one read it can ask for goes through the callback it is given.
/// </summary>
public partial class ImportWindow : Window
{
    public sealed record HostItem(string Host, string SendsText);

    private readonly Recipe _recipe;
    private readonly ImportReviewResult _review;
    private readonly RecipeState _existing;

    public ImportWindow(
        Recipe recipe, ImportReviewResult review, UpdateComparison comparison, RecipeState? existing,
        IReadOnlyList<InstalledRecipe> installed, Func<IReadOnlyCollection<Guid>> accountIds,
        Func<string, string> ruleSentence, Func<Task<CounterLookup>>? readCounterNames)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _recipe = recipe;
        _review = review;
        _existing = existing ?? new RecipeState();

        Title = comparison.IsUpdate ? "Update recipe" : "Import recipe";
        NameLine.Text = recipe.Name;
        CreditLine.Text = recipe.Credit;
        AuthorLine.Text = recipe.Author is null
            ? "No author given."
            : $"Says it is from {recipe.Author}. This is not verified.";

        HostsList.ItemsSource = review.Hosts
            .Select(h => new HostItem(h.Host, ImportReview.SendsText(h)))
            .ToList();

        PollLine.Text = $"Asks every {recipe.EffectiveEverySeconds} seconds.";
        Show(ReusedLine, string.Join(" ", review.ReusedKeys));
        Show(ChangesLine, comparison.Changes.Count == 0
            ? ""
            : "What changed:" + string.Concat(comparison.Changes.Select(c => Environment.NewLine + "• " + c)));
        Show(InputsNote, comparison.IsUpdate ? "" : ImportText.InputsNote(recipe));

        KeptNote.Text = ImportText.KeptNote(recipe);
        KeptList.ItemsSource = ImportText.Kept(recipe).Select(label => "• " + label).ToList();

        if (recipe.IsGroupList)
        {
            StatsSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatsTable.Load(recipe, _existing, installed, accountIds, ruleSentence,
                readCounterNames is null ? null : _ => readCounterNames(),
                ImportText.ShowEveryStat, review.Refusals);
            StatsTable.Changed += (_, _) => Refresh();
        }

        ImportButton.Content = comparison.IsUpdate ? "Update" : "Import";
        Refresh();
    }

    /// <summary>The choices to save. Set when Import is accepted.</summary>
    public IReadOnlyDictionary<string, StatChoice> Stats { get; private set; } = new Dictionary<string, StatChoice>();

    /// <summary>The counter names to keep in the recipe's state, read here or saved before.</summary>
    public IReadOnlyList<string> CounterNames => _recipe.IsGroupList ? _existing.SavedCounterNames : StatsTable.CounterNames;

    private void Refresh()
    {
        // With the table hidden, the review's own refusals need a line of their own.
        Show(ImportRefusalLine, _recipe.IsGroupList ? string.Join(Environment.NewLine, _review.Refusals) : "");
        ImportButton.IsEnabled = _review.CanImport && (_recipe.IsGroupList || StatsTable.AnyTicked);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (_recipe.IsGroupList)
        {
            Stats = _existing.StatChoices;
            DialogResult = true;
            return;
        }

        var problems = StatsTable.SaveProblems();
        if (problems.Count > 0)
        {
            StatsTable.ShowProblems([.. _review.Refusals, .. problems]);
            return;
        }

        Stats = StatsTable.Choices;
        DialogResult = true;
    }

    /// <summary>An empty line takes no space, so the screen has no gaps where nothing applies.</summary>
    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

In `src/UI/Setup/ImportFlow.cs`, replace

```csharp
                recipe.LastStep.Counters is null ? null : _ => services.ReadCounterNamesAsync(recipe, CancellationToken.None))
```

with

```csharp
                recipe.LastStep.Counters is null ? null : () => services.ReadCounterNamesAsync(recipe, CancellationToken.None))
```

In `src/Recipes/RecipeEngine.cs`, replace

```csharp
    public const string NothingTracked = "No stat is ticked to show or send. Choose some in Recipe settings.";
```

with

```csharp
    public const string NothingTracked = "No stat is ticked to show or send. Choose some in Setup › Stats.";
```

In `src/Core/ReportPolicy.cs`, replace the comment line

```csharp
    /// Rendered verbatim in the window (<c>MainWindow.RenderPolicy</c>), so what leaves is readable
```

with

```csharp
    /// Rendered verbatim in Setup › Alerts (<c>AlertsModel.Policies</c>), so what leaves is readable
```

Then search the tree for anything still naming the old window or its screen:

Run: `git grep -n "MainWindow\.\|UI\.MainWindow\|Recipe settings" -- src tests`
Expected: no output. (`App.xaml.cs` sets `Application.MainWindow = board`; the pattern skips that.) If a test asserts the old `NothingTracked` text, update its expected string to the new one in the same commit.

- [ ] **Step 9: Build gate**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Expected: `Build succeeded.` with 0 warnings.

Run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: every test passes, including:
- `OnlyTheCompositionRootConstructsARecipeWatch` (the text "new RecipeWatch(" appears exactly once in `src/`, in `Composition/AppServices.cs`; the doc comments above say "constructed" and never spell the call);
- `TheSourceHostKeepsOneWatchPerSourceAcrossAppliesAndReads`;
- `ReportPolicyTests` (no new caller of `ReportMetricAsync`);
- `NoHostnameFenceTests`;
- `ThemeFenceTests` (`BoardWindow.xaml` uses only theme brushes).

- [ ] **Step 10: Look at it**

Run: `dotnet build Ur-Score.csproj -c Release` then start `bin\Release\net10.0-windows\626labs.ur-score.exe` with your own data folder. Expect:
1. The window is titled "RoRoRo Ur Score" and reads "Reading your score book…", then "Not started.".
2. With the part 2a clan recipe installed, the board shows the Battle panels. If the recipe had no inputs saved, Setup opens on Clans.
3. **Start** turns into **Stop**, and the period line gains "next read in".

Close it before the next build.

- [ ] **Step 11: Commit**

```text
git add -A src/Board/StarterBoards.cs src/Board/BoardLayout.cs src/UI/BoardText.cs src/UI/Controls/PanelGrid.cs src/Composition/AppServices.cs src/UI/BoardWindow.xaml src/UI/BoardWindow.xaml.cs src/App.xaml.cs src/UI/ImportWindow.xaml src/UI/ImportWindow.xaml.cs src/UI/ImportText.cs src/UI/Setup/ImportFlow.cs src/Recipes/RecipeEngine.cs src/Core/ReportPolicy.cs src/UI/MainWindow.xaml src/UI/MainWindow.xaml.cs tests/StarterBoardsTests.cs tests/BoardLayoutTests.cs tests/BoardTextTests.cs tests/RecipeWatchTests.cs tests/ImportTextTests.cs
git commit -m "board: the window becomes the starter board, built by one composition root

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 15: The try-out command

Spec §10. It runs a recipe once from the command line, for agents and recipe authors. It prints no player's id or name.

**Files:**
- Create: `src/Cli/TryCommand.cs`
- Modify: `src/App.xaml.cs` (a `--try` branch before the single-instance mutex)
- Create: `tests/TryCommandTests.cs`

**Interfaces:**
- Consumes: `RecipeParser`, `RecipeEngine`, `ImportReview.Review`/`SendsText`, `IRecipeTransport`, `IKeyStore`, `HttpRecipeTransport.CreateHandler`, `KeyStore.DefaultPath`.
- Produces: `TryCommand` (`Ok`, `Refused`, `InputMissing`, `Stopped`, `BadArguments`, `Wants`, `RunAsync`), exactly as in the contract.

- [ ] **Step 1: Write the failing tests**

Create `tests/TryCommandTests.cs`:

```csharp
using System.Text.Json;
using Labs626.UrScore.Cli;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class TryCommandTests
{
    private sealed class RouteTransport(params (string Start, string Body)[] routes) : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var route = routes.FirstOrDefault(r => url.AbsoluteUri.StartsWith(r.Start, StringComparison.Ordinal));
            return Task.FromResult(route.Body is null ? new FetchResult(404, "{}", null) : new FetchResult(200, route.Body, null));
        }
    }

    private const string Battle = """{ "status": "ok", "data": { "configName": "B", "configData": { "FinishTime": 1757611800 } } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": {
            "A": { "Place": 40, "Points": 500, "PointContributions": [ { "UserID": 7001001, "Points": 300 } ] },
            "B": { "Place": 3, "Points": 999, "PointContributions": [
                { "UserID": 1647274201, "Points": 4200 }, { "UserID": 7002002, "Points": 3100 }, { "UserID": 7003003, "Points": 10 } ] }
        } } }
        """;

    private static RouteTransport Clan() => new(
        ("https://ps99.biggamesapi.io/api/activeClanBattle", Battle),
        ("https://ps99.biggamesapi.io/api/clan/", ClanResponse));

    private static string RecipeFile(TempDir.Scope dir, string fixture)
    {
        var path = Path.Combine(dir.Path, fixture);
        File.WriteAllText(path, RecipeParserTests.Fixture(fixture));
        return path;
    }

    private static async Task<(int Code, string Output)> Run(IRecipeTransport transport, params string[] args)
    {
        var output = new StringWriter();
        var code = await TryCommand.RunAsync(args, output, transport, new NoKeys(), CancellationToken.None);
        return (code, output.ToString());
    }

    [Fact]
    public async Task AReadPrintsCountsYourAccountAndNoOtherPlayer()
    {
        using var dir = TempDir.Create("urscore-try");
        var (code, output) = await Run(Clan(), "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2", "--account", "1647274201");

        Assert.Equal(TryCommand.Ok, code);
        Assert.Contains("Outcome: Read", output);
        Assert.Contains("Rows seen: 3", output);
        Assert.Contains("value: found in 3, missed in 0, smallest 10, median 3100, largest 4200", output);
        Assert.Contains("1647274201: value=4200, rank 1 of 3", output);
        Assert.Contains("clan-place = 3", output);
        Assert.Contains("Past periods: 2 (A, B)", output);
        Assert.Contains("ps99.biggamesapi.io", output);
        Assert.DoesNotContain("7002002", output);
        Assert.DoesNotContain("7003003", output);
        Assert.DoesNotContain("7001001", output);
    }

    [Fact]
    public async Task JsonOutputParsesAndCarriesNoOtherPlayer()
    {
        using var dir = TempDir.Create("urscore-try");
        var (code, output) = await Run(Clan(), "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2", "--json");

        Assert.Equal(TryCommand.Ok, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("Read", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("rowsSeen").GetInt32());
        Assert.DoesNotContain("7002002", output);
    }

    [Fact]
    public async Task AMissingInputSaysWhichAndHow()
    {
        using var dir = TempDir.Create("urscore-try");
        var (code, output) = await Run(Clan(), "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"));

        Assert.Equal(TryCommand.InputMissing, code);
        Assert.Contains("Set Your clan with --input clan=<value>.", output);
    }

    [Fact]
    public async Task ARefusedRecipeListsItsProblems()
    {
        using var dir = TempDir.Create("urscore-try");
        var path = Path.Combine(dir.Path, "bad.recipe.json");
        File.WriteAllText(path, """{ "recipe": 1, "name": "Bad" }""");

        var (code, output) = await Run(Clan(), "--try", path);

        Assert.Equal(TryCommand.Refused, code);
        Assert.Contains("has no 'credit'", output);
    }

    [Fact]
    public async Task AStoppedReadNamesItsOutcome()
    {
        using var dir = TempDir.Create("urscore-try");
        var idle = new RouteTransport(("https://ps99.biggamesapi.io/api/activeClanBattle", """{ "status": "ok", "data": null }"""));

        var (code, output) = await Run(idle, "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2");

        Assert.Equal(TryCommand.Stopped, code);
        Assert.Contains("Outcome: Idle", output);
    }

    [Theory]
    [InlineData("--try")]
    [InlineData("--try", "missing-file.json")]
    [InlineData("--try", "x.json", "--input", "no-equals-sign")]
    [InlineData("--try", "x.json", "--wat")]
    public async Task BadArgumentsPrintUsage(params string[] args)
    {
        var (code, output) = await Run(Clan(), args);

        Assert.Equal(TryCommand.BadArguments, code);
        Assert.Contains("Usage: 626labs.ur-score.exe --try", output);
    }

    [Fact]
    public void OnlyTryArgumentsAreClaimed()
    {
        Assert.True(TryCommand.Wants(["--try", "x.json"]));
        Assert.False(TryCommand.Wants([]));
        Assert.False(TryCommand.Wants(["--something"]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release`
Expected: FAIL with `The type or namespace name 'Cli' does not exist`.

- [ ] **Step 3: Create `src/Cli/TryCommand.cs`**

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Cli;

/// <summary>
/// Score book spec §10: runs a recipe once and prints what it found. No window, RoRoRo, state, book or
/// single-instance mutex. Never prints a player's id or name: only counts, the smallest, median and
/// largest value, and the accounts given with --account.
/// </summary>
public static class TryCommand
{
    public const int Ok = 0;
    public const int Refused = 2;
    public const int InputMissing = 3;
    public const int Stopped = 4;
    public const int BadArguments = 5;

    public const string Usage =
        "Usage: 626labs.ur-score.exe --try <recipe.json> [--input id=value]... [--stat key]... [--account robloxUserId]... [--json]";

    public static bool Wants(string[] args) => args.Contains("--try", StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] args, TextWriter output, IRecipeTransport transport, IKeyStore keys, CancellationToken cancellationToken)
    {
        if (!TryParseArguments(args, out var parsed, out var argumentProblem))
        {
            output.WriteLine(argumentProblem);
            output.WriteLine(Usage);
            return BadArguments;
        }

        var result = RecipeParser.Parse(await File.ReadAllTextAsync(parsed.File, cancellationToken).ConfigureAwait(false));
        if (!result.Ok)
        {
            output.WriteLine("This recipe was refused:");
            foreach (var problem in result.Problems) output.WriteLine("  " + problem);
            return Refused;
        }

        var recipe = result.Recipe!;
        foreach (var input in recipe.Inputs.Where(i => !parsed.Inputs.ContainsKey(i.Id)))
        {
            output.WriteLine($"Set {input.Label} with --input {input.Id}=<value>.");
            return InputMissing;
        }

        if (recipe.LastStep.PerAccount && parsed.Accounts.Count == 0)
        {
            output.WriteLine("This recipe reads per account. Add --account <robloxUserId> for each account to read.");
            return InputMissing;
        }

        var offered = RecipeStats.Offered(recipe, parsed.Stats).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = parsed.Stats.FirstOrDefault(s => !offered.Contains(s));
        if (unknown is not null)
        {
            output.WriteLine($"The recipe has no stat '{unknown}'.");
            output.WriteLine(Usage);
            return BadArguments;
        }

        IReadOnlySet<string> tracked = parsed.Stats.Count > 0
            ? parsed.Stats.ToHashSet(StringComparer.Ordinal)
            : recipe.LastStep.Values.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);

        var reading = await new RecipeEngine(transport, keys)
            .ReadAsync(recipe, parsed.Inputs, parsed.Accounts, tracked, cancellationToken)
            .ConfigureAwait(false);

        var report = Report(recipe, keys, reading, tracked, parsed.Accounts);
        if (parsed.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        }
        else
        {
            WriteText(output, report);
        }

        return reading.Outcome == ReadingOutcome.Read ? Ok : Stopped;
    }

    private sealed record Arguments(string File, Dictionary<string, string> Inputs, List<string> Stats, List<long> Accounts, bool Json);

    private static bool TryParseArguments(string[] args, out Arguments parsed, out string problem)
    {
        parsed = new Arguments("", new Dictionary<string, string>(StringComparer.Ordinal), [], [], false);
        problem = "";
        string? file = null;
        var json = false;

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (args[i])
            {
                case "--try":
                    file = Next();
                    break;
                case "--input":
                    if (Next() is not { } pair || pair.IndexOf('=') is var eq and <= 0)
                    {
                        problem = "--input needs id=value.";
                        return false;
                    }

                    parsed.Inputs[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    break;
                case "--stat":
                    if (Next() is not { Length: > 0 } stat)
                    {
                        problem = "--stat needs a stat key.";
                        return false;
                    }

                    parsed.Stats.Add(stat);
                    break;
                case "--account":
                    if (!long.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    {
                        problem = "--account needs a Roblox user id.";
                        return false;
                    }

                    parsed.Accounts.Add(id);
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    problem = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        if (file is null)
        {
            problem = "--try needs a recipe file.";
            return false;
        }

        if (!System.IO.File.Exists(file))
        {
            problem = $"No recipe file at {file}.";
            return false;
        }

        parsed = parsed with { File = file, Json = json };
        return true;
    }

    private sealed record StatSummary(string Key, int Found, int Missed, double? Smallest, double? Median, double? Largest);

    private sealed record AccountSummary(long UserId, IReadOnlyDictionary<string, double> Values, IReadOnlyDictionary<string, int> Rank, int? Of);

    private sealed record TryReport(
        string Recipe, IReadOnlyList<string> Contacts, string Outcome, string? Detail, int RowsSeen, string? Period, string? PeriodEnds,
        IReadOnlyList<string> PastPeriods, IReadOnlyDictionary<string, string?> Headline, IReadOnlyList<StatSummary> Stats,
        int CounterNames, IReadOnlyList<AccountSummary> Accounts, int Groups, IReadOnlyList<string> FirstGroups);

    private static TryReport Report(Recipe recipe, IKeyStore keys, RecipeReading reading, IReadOnlySet<string> tracked, IReadOnlyList<long> accounts)
    {
        var contacts = ImportReview.Review(recipe, keys).Hosts.Select(h => $"{h.Host}: {ImportReview.SendsText(h)}").ToList();

        var stats = tracked.Order(StringComparer.Ordinal).Select(key =>
        {
            var values = reading.Rows.Where(r => r.Values.ContainsKey(key)).Select(r => r.Values[key]).Order().ToList();
            return new StatSummary(key, values.Count, reading.Rows.Count - values.Count,
                values.Count == 0 ? null : values[0], values.Count == 0 ? null : values[(values.Count - 1) / 2], values.Count == 0 ? null : values[^1]);
        }).ToList();

        var ranks = tracked.ToDictionary(key => key, key => Ranking.Competition(reading.Rows, key), StringComparer.Ordinal);
        var yours = accounts.Distinct()
            .Select(id => reading.Rows.FirstOrDefault(r => r.UserId == id) is { } row
                ? new AccountSummary(id, row.Values,
                    ranks.Where(r => r.Value.ContainsKey(id)).ToDictionary(r => r.Key, r => r.Value[id], StringComparer.Ordinal),
                    recipe.LastStep.PerAccount ? null : reading.Rows.Count)
                : new AccountSummary(id, new Dictionary<string, double>(), new Dictionary<string, int>(), null))
            .ToList();

        return new TryReport(
            recipe.Name, contacts, reading.Outcome.ToString(), reading.Detail, reading.RowsSeen,
            reading.Period?.Value, reading.Period?.Ends?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            [.. reading.Past.Select(p => p.Value)],
            reading.Headline.ToDictionary(h => h.Id.Length > 0 ? h.Id : h.Label, h => h.Text, StringComparer.Ordinal),
            stats, reading.CounterNames.Count, yours, reading.Groups.Count, [.. reading.Groups.Take(10).Select(g => g.Name)]);
    }

    private static void WriteText(TextWriter output, TryReport report)
    {
        static string N(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "none";

        output.WriteLine($"Recipe: {report.Recipe}");
        output.WriteLine("Contacts:");
        foreach (var contact in report.Contacts) output.WriteLine("  " + contact);
        output.WriteLine($"Outcome: {report.Outcome}");
        if (report.Detail is not null) output.WriteLine($"Detail: {report.Detail}");
        output.WriteLine($"Rows seen: {report.RowsSeen}");
        if (report.Period is not null) output.WriteLine($"Period: {report.Period}{(report.PeriodEnds is null ? "" : $" (ends {report.PeriodEnds})")}");
        if (report.PastPeriods.Count > 0) output.WriteLine($"Past periods: {report.PastPeriods.Count} ({string.Join(", ", report.PastPeriods)})");

        if (report.Headline.Count > 0)
        {
            output.WriteLine("Headline:");
            foreach (var (id, text) in report.Headline) output.WriteLine($"  {id} = {text ?? "none"}");
        }

        output.WriteLine("Stats:");
        foreach (var stat in report.Stats)
        {
            output.WriteLine($"  {stat.Key}: found in {stat.Found}, missed in {stat.Missed}, smallest {N(stat.Smallest)}, median {N(stat.Median)}, largest {N(stat.Largest)}");
        }

        if (report.CounterNames > 0) output.WriteLine($"Counter names: {report.CounterNames}");

        foreach (var account in report.Accounts)
        {
            var values = account.Values.Count == 0 ? "not in the results" : string.Join(", ", account.Values.Select(v => $"{v.Key}={N(v.Value)}"));
            var rank = account.Rank.Count == 0 || account.Of is null ? "" : $", rank {string.Join(", ", account.Rank.Select(r => r.Value))} of {account.Of}";
            output.WriteLine($"  {account.UserId}: {values}{rank}");
        }

        if (report.Groups > 0) output.WriteLine($"Groups: {report.Groups} ({string.Join(", ", report.FirstGroups)})");
    }
}
```

**Check the median against the test.** The values are 10, 3100, 4200. `(3 - 1) / 2` is index 1, so the median is 3100, matching the test.

- [ ] **Step 4: Wire `--try` into `src/App.xaml.cs`**

At the very top of `OnStartup`, before the mutex line, add:

```csharp
        if (Cli.TryCommand.Wants(e.Args))
        {
            // A console to write to when launched from one; redirected output works without it.
            AttachConsole(-1);

            using var http = new System.Net.Http.HttpClient(Recipes.HttpRecipeTransport.CreateHandler());
            var keys = new Recipes.KeyStore(Recipes.KeyStore.DefaultPath);
            var transport = new Recipes.HttpRecipeTransport(http, rawDirectory: null, new Recipes.Redactor(() => keys.Values()));
            var code = Cli.TryCommand.RunAsync(e.Args, Console.Out, transport, keys, CancellationToken.None).GetAwaiter().GetResult();
            Console.Out.Flush();
            Shutdown(code);
            return;
        }
```

and add this member to the `App` class:

```csharp
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
```

**Leave `rawDirectory` null on purpose:** a try-out must not write raw responses, which hold other players' rows, to disk.

**This touches `App.xaml.cs`, and Task 14 changes it too.** Put this block first in `OnStartup`, above whatever Task 14 left there.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror`
Then run: `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: PASS.

- [ ] **Step 6: Try it for real**

Run: `dotnet build Ur-Score.csproj -c Release`, then `bin/Release/net10.0-windows/626labs.ur-score.exe --try tests/Fixtures/petsim99-clan-battle.recipe.json --input clan=CCGP --json > try.json; echo exit $?`
Expected: exit 4 with `"outcome": "Idle"` between battles, or exit 0 with `rowsSeen` and `pastPeriods` during one. Delete `try.json` afterwards. It must never be committed.

- [ ] **Step 7: Commit**

```bash
git add src/Cli/TryCommand.cs src/App.xaml.cs tests/TryCommandTests.cs
git commit -m "cli: --try runs a recipe once and prints counts, never another player

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 16: Commit the UI Automation smoke scripts, and walk stage 1 live

Spec §11, §13 Live. The scratch scripts that walked parts 2a and 2b become `tools/smoke/`:
- `uia.ps1` and `uia-import.ps1`, the helpers;
- `window-smoke.ps1`;
- walks for Setup › Clans, the Stats table, the starter board and the score book page;
- `shot.ps1`, which takes the output path;
- `check-book-privacy.ps1`;
- `README.md`.

The scripts use the automation ids from Tasks 10–14 (the table at the top of this file). They carry no machine paths: the repo root comes from `$PSScriptRoot`. Every walk that needs a clean data folder moves `%LOCALAPPDATA%\626labs.ur-score` aside and restores it in a `finally`. The scripts are ASCII only, so Windows PowerShell 5.1 reads them without a BOM; UI text with `…`, `·`, `›` or `★` is matched by pattern.

The task ends with the live walk of stage 1 against a running RoRoRo 1.28.

**Files:**
- Create: `tools/smoke/uia.ps1`, `tools/smoke/uia-import.ps1`
- Create: `tools/smoke/window-smoke.ps1`, `tools/smoke/walk-setup-clans.ps1`, `tools/smoke/walk-stats-table.ps1`, `tools/smoke/walk-starter-board.ps1`, `tools/smoke/walk-score-book.ps1`
- Create: `tools/smoke/shot.ps1`, `tools/smoke/check-book-privacy.ps1`, `tools/smoke/checks/invalid-no-step2-url.recipe.json`, `tools/smoke/README.md`

**Interfaces:**
- Consumes: the automation ids and window titles of Tasks 10–14; `tests/Fixtures/*.recipe.json`; the data files `sources.json`, `accounts.json`, `scorebook\<slug>\*.jsonl`, `recipes\*.state.json`.
- Produces: PowerShell functions, which the walks dot-source:
  - UI Automation: `Get-UrWindows`, `Wait-UrWindow`, `Get-BoardWindow`, `Get-SetupWindow`, `Find-ByAutomationId`, `Find-All`, `Get-Button`, `Get-Edit`, `Get-Check`, `Invoke-Element`, `Set-ElementValue`, `Set-Tick`, `Line`, `Wait-Line`, `Wait-Until`
  - the app: `Start-UrScore`, `Stop-UrScore`, `Open-SetupPage`, `Close-UrWindow`, `Select-SearchName`, `Select-FirstSearchMatch`
  - the data folder and results: `Move-UrDataAside`, `Restore-UrData`, `Check`, `Show-Results`
  - imports: `Start-Import`, `Get-AfterImport`, `Get-AllTexts`, `Close-MessageBox`, `Get-MessageBoxText`, `Read-Sources`, `Complete-ClanImport`

- [ ] **Step 1: Create the helpers**

Create `tools/smoke/uia.ps1`

```powershell
# UI Automation helpers for Ur Score's smoke scripts. Dot-source this file; it only defines things.
# ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less file as ANSI.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]
$script:Cond = [System.Windows.Automation.Condition]
$script:CT = [System.Windows.Automation.ControlType]

# Paths come from this file's own location, never from a machine.
$script:UrRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:UrExe = Join-Path $UrRepo 'bin\Release\net10.0-windows\626labs.ur-score.exe'
$script:UrFixtures = Join-Path $UrRepo 'tests\Fixtures'
$script:UrData = Join-Path $env:LOCALAPPDATA '626labs.ur-score'
$script:UrShots = Join-Path $UrRepo 'artifacts\smoke'
$script:Results = [System.Collections.Generic.List[object]]::new()

function Get-UrProcessId {
    (Get-Process | Where-Object { $_.ProcessName -match 'ur-score' } | Select-Object -First 1).Id
}

# Every top-level window of the Ur Score process: the board, Setup, the import screen, message boxes, the file picker.
function Get-UrWindows {
    $procId = Get-UrProcessId
    if (-not $procId) { return @() }
    $byProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
    $isWindow = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)
    $list = @()
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $byProcess)) {
        $list += $w
        # WPF dialogs and message boxes can also appear as children of their owner.
        foreach ($inner in $w.FindAll($TS::Children, $isWindow)) { $list += $inner }
    }
    return $list
}

function Wait-UrWindow([string]$titlePattern, [int]$seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $w = Get-UrWindows | Where-Object { $_.Current.Name -match $titlePattern } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-BoardWindow { Get-UrWindows | Where-Object { $_.Current.Name -eq 'RoRoRo Ur Score' } | Select-Object -First 1 }

function Get-SetupWindow { Get-UrWindows | Where-Object { $_.Current.Name -eq 'Setup' } | Select-Object -First 1 }

function Find-ByAutomationId($root, [string]$id) {
    if (-not $root) { return $null }
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $root.FindFirst($TS::Descendants, $c)
}

function Find-All($root, $controlType) {
    if (-not $root) { return @() }
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    @($root.FindAll($TS::Descendants, $c))
}

function Get-Button($root, [string]$name) { Find-All $root $CT::Button | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Get-Edit($root, [string]$name) { Find-All $root $CT::Edit | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Get-Check($root, [string]$name) { Find-All $root $CT::CheckBox | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Invoke-Element($el) {
    if (-not $el) { throw 'element not found' }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-ElementValue($el, [string]$value) {
    if (-not $el) { throw 'element not found' }
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Set-Tick($el, [bool]$on) {
    if (-not $el) { throw 'checkbox not found' }
    $p = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($p.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $on) {
        $p.Toggle()
        Start-Sleep -Milliseconds 400
    }
}

# The text of a TextBlock found by automation id; '(absent)' when it isn't shown (a collapsed line is not in the tree).
function Line($root, [string]$id) {
    $e = Find-ByAutomationId $root $id
    if ($e) { $e.Current.Name } else { '(absent)' }
}

function Wait-Line($root, [string]$id, [string]$pattern, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $text = Line $root $id
        if ($text -match $pattern) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Wait-Until([scriptblock]$test, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        try { if (& $test) { return $true } } catch { }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-AllTexts($root) {
    Find-All $root $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
}

function Stop-UrScore {
    Get-Process | Where-Object { $_.ProcessName -match 'ur-score' } | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(10000)) { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
    }
}

# Starts the Release build and waits until the board has read the score book.
function Start-UrScore([int]$seconds = 60) {
    if (-not (Test-Path $UrExe)) { throw "No build at $UrExe. Run: dotnet build Ur-Score.csproj -c Release" }
    Start-Process -FilePath $UrExe -WorkingDirectory (Split-Path $UrExe) | Out-Null
    $board = Wait-UrWindow '^RoRoRo Ur Score$' $seconds
    if (-not $board) { throw 'the board window never appeared' }
    # Start is enabled once the score book has been read.
    Wait-Until { $start = Find-ByAutomationId (Get-BoardWindow) 'StartStopButton'; $start -and $start.Current.IsEnabled } $seconds | Out-Null
    Start-Sleep -Seconds 1
    return Get-BoardWindow
}

function Close-UrWindow($w) {
    if (-not $w) { return }
    try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    Start-Sleep -Milliseconds 600
}

# Opens Setup from the board when it isn't open, then selects a page by its title in the left list.
function Open-SetupPage([string]$title) {
    $setup = Get-SetupWindow
    if (-not $setup) {
        Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'SetupButton')
        $setup = Wait-UrWindow '^Setup$' 15
    }
    if (-not $setup) { throw 'Setup did not open' }
    $nav = Find-ByAutomationId $setup 'SetupNav'
    $isItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)
    $item = @($nav.FindAll($TS::Children, $isItem)) | Where-Object { $_.Current.Name -eq $title } | Select-Object -First 1
    if (-not $item) { throw "Setup has no page named '$title'" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    return $setup
}

# Types a name into a ClanSearchBox (found by its accessible name) and invokes its "Pick <name>" match.
# The list is read once per session, so this retries until it has arrived.
function Select-SearchName($root, [string]$searchLabel, [string]$name, [int]$seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $edit = Get-Edit $root $searchLabel
        if ($edit) {
            Set-ElementValue $edit ''
            Set-ElementValue $edit $name
            Start-Sleep -Milliseconds 700
            $pick = Get-Button $root "Pick $name"
            if ($pick) { Invoke-Element $pick; Start-Sleep -Milliseconds 600; return }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "no search match named '$name' under '$searchLabel'"
}

# Types a query and picks the first match whose name is not in $except. Returns the name picked.
function Select-FirstSearchMatch($root, [string]$searchLabel, [string]$query, [string[]]$except, [int]$seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $edit = Get-Edit $root $searchLabel
        if ($edit) {
            Set-ElementValue $edit ''
            Set-ElementValue $edit $query
            Start-Sleep -Milliseconds 700
            $pick = Find-All $root $CT::Button |
                Where-Object { $_.Current.Name -like 'Pick *' -and ($except -notcontains $_.Current.Name.Substring(5)) } |
                Select-Object -First 1
            if ($pick) {
                $name = $pick.Current.Name.Substring(5)
                Invoke-Element $pick
                Start-Sleep -Milliseconds 600
                return $name
            }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "no search match for '$query' under '$searchLabel'"
}

# Moves your data folder aside so a walk starts clean. Returns the backup path, or $null when you had none.
function Move-UrDataAside {
    Stop-UrScore
    $backup = "$UrData.smoke-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    if (Test-Path $UrData) {
        Rename-Item $UrData (Split-Path $backup -Leaf)
    } else {
        $backup = ''
    }
    try {
        New-Item -ItemType Directory -Force $UrData | Out-Null
    } catch {
        if ($backup) { Rename-Item $backup (Split-Path $UrData -Leaf) }
        throw
    }
    return $backup
}

# Puts your data folder back. Always call it from a finally.
function Restore-UrData([string]$backup) {
    Stop-UrScore
    if ($null -eq $backup) { return }
    if (Test-Path $UrData) { Remove-Item $UrData -Recurse -Force }
    if ($backup -and (Test-Path $backup)) { Rename-Item $backup (Split-Path $UrData -Leaf) }
    "Your data folder is back: $(Test-Path $UrData)"
}

function Check([string]$step, [bool]$ok, [string]$seen) {
    $script:Results.Add([pscustomobject]@{ Step = $step; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Seen = $seen })
}

function Show-Results {
    $script:Results | Format-Table -AutoSize -Wrap | Out-String -Width 240
    $failed = @($script:Results | Where-Object { $_.Result -eq 'FAIL' }).Count
    "$($script:Results.Count - $failed) passed, $failed failed"
    if ($failed -gt 0) { $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
}

function Show-Tree($root, [int]$depth = 0, [int]$max = 8) {
    if ($depth -gt $max) { return }
    foreach ($k in $root.FindAll($TS::Children, $Cond::TrueCondition)) {
        $c = $k.Current
        ('  ' * $depth) + "[$($c.ControlType.ProgrammaticName -replace 'ControlType\.','')] id='$($c.AutomationId)' name='$($c.Name)'"
        Show-Tree $k ($depth + 1) $max
    }
}
```

Create `tools/smoke/uia-import.ps1`

```powershell
# Import helpers: the Windows file picker, the import screen, message boxes, and the data files an import writes.
. (Join-Path $PSScriptRoot 'uia.ps1')

if (-not ('UrWin32Msg' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UrWin32Msg {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
}

# Presses Import recipe... on Setup > Recipes, types the path into the file picker and presses Open.
function Start-Import([string]$path) {
    $setup = Open-SetupPage 'Recipes'
    Invoke-Element (Find-ByAutomationId $setup 'ImportRecipeButton')
    $dlg = Wait-UrWindow '^Import a recipe$' 20
    if (-not $dlg) { throw 'the file picker did not open' }
    Start-Sleep -Milliseconds 800
    # The common dialog's classic controls surface only as panes to this UIA client, so drive them by handle:
    # set the file name text (WM_SETTEXT), then click Open (BM_CLICK).
    $all = $dlg.FindAll($TS::Descendants, $Cond::TrueCondition)
    $edit = $all | Where-Object { $_.Current.AutomationId -eq '1148' -and $_.Current.ClassName -eq 'Edit' } | Select-Object -First 1
    $open = $all | Where-Object { $_.Current.AutomationId -eq '1' -and $_.Current.ClassName -eq 'Button' } | Select-Object -First 1
    if (-not $edit -or -not $open) { throw 'file picker controls not found' }
    [UrWin32Msg]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $path) | Out-Null
    Start-Sleep -Milliseconds 300
    [UrWin32Msg]::PostMessage([IntPtr]$open.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 2
}

# Whatever an import put up: a message box, the import screen, or nothing.
function Get-AfterImport([int]$seconds = 20) {
    Wait-UrWindow '^(Ur Score|Import recipe|Update recipe)$' $seconds
}

function Close-MessageBox($w) {
    $ok = $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Button' -and $_.Current.Name -eq 'OK' } | Select-Object -First 1
    [UrWin32Msg]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 800
}

# A message box's text lives in a Static control this UIA client names but types as a pane.
function Get-MessageBoxText($w) {
    $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Static' -and $_.Current.Name } | ForEach-Object { $_.Current.Name }
}

function Read-Sources {
    $file = Join-Path $UrData 'sources.json'
    if (-not (Test-Path $file)) { return @() }
    @(Get-Content $file -Raw | ConvertFrom-Json)
}

function Get-RoleText($source) { "$($source.role)".ToLowerInvariant() }

# Imports a fixture through the import screen with the given stats ticked. Returns the Setup window.
function Complete-ClanImport([string]$fixture, [string[]]$show, [string[]]$send) {
    Start-Import $fixture
    $screen = Wait-UrWindow '^(Import recipe|Update recipe)$' 30
    if (-not $screen) {
        $box = Get-AfterImport 2
        throw "no import screen; saw '$($box.Current.Name)': $((Get-MessageBoxText $box) -join ' ')"
    }
    foreach ($label in $show) { Set-Tick (Get-Check $screen "Show $label") $true }
    foreach ($label in $send) { Set-Tick (Get-Check $screen "Send $label") $true }
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2
    return Get-SetupWindow
}
```

Create `tools/smoke/shot.ps1`

```powershell
# Saves a screenshot of one Ur Score window. You pass the output path.
param(
    [Parameter(Mandatory = $true)][string]$OutPath,
    [string]$Title = 'RoRoRo Ur Score'
)

Add-Type -AssemblyName System.Drawing
if (-not ('UrShotWin' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UrShotWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
}
"@
}

[UrShotWin]::SetProcessDPIAware() | Out-Null
$h = [UrShotWin]::FindWindow([NullString]::Value, $Title)
if ($h -eq [IntPtr]::Zero) { "no window titled '$Title'"; exit 1 }

[UrShotWin]::ShowWindow($h, 9) | Out-Null
[UrShotWin]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object UrShotWin+RECT
[UrShotWin]::GetWindowRect($h, [ref]$r) | Out-Null
$width = $r.R - $r.L
$height = $r.B - $r.T
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($r.L, $r.T, 0, 0, $bitmap.Size)

$folder = Split-Path -Parent $OutPath
if ($folder) { New-Item -ItemType Directory -Force $folder | Out-Null }
$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
"$OutPath ${width}x${height}"
```

Create `tools/smoke/checks/invalid-no-step2-url.recipe.json`

```json
{
  "recipe": 1,
  "name": "Pet Sim 99 clan battle points",
  "credit": "Data from Big Games' public Pet Simulator 99 API.",
  "metricId": "clan.battle.points",
  "valueLabel": "Points",
  "everySeconds": 180,
  "inputs": [ { "id": "clan", "label": "Your clan", "search": { "url": "https://ps99.biggamesapi.io/api/clansList", "list": "data" } } ],
  "steps": [
    { "url": "https://ps99.biggamesapi.io/api/activeClanBattle", "take": { "battle": "data.configName" } },
    { "rows": "data.Battles.{battle}.PointContributions", "userId": "UserID", "value": "Points" }
  ]
}
```

- [ ] **Step 2: Create the window smoke**

Create `tools/smoke/window-smoke.ps1`

```powershell
# The whole window in one pass, on a clean data folder: first run, a refused file, the import screen,
# Setup opening on Clans, picking the main clan, the starter board, Test now, and copied diagnostics.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$invalid = Join-Path $PSScriptRoot 'checks\invalid-no-step2-url.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    $board = Start-UrScore

    Check '1 First run asks for a recipe' ((Line $board 'EmptyStateLine') -eq 'Import a recipe to start') (Line $board 'EmptyStateLine')

    Start-Import $invalid
    $box = Get-AfterImport 10
    $text = if ($box -and $box.Current.Name -eq 'Ur Score') { (Get-MessageBoxText $box) -join ' ' } else { "(no message box: '$($box.Current.Name)')" }
    if ($box -and $box.Current.Name -eq 'Ur Score') { Close-MessageBox $box }
    Check '2 An invalid file is refused and nothing is installed' (($text -match "Step 2 has no 'url'\.") -and -not (Test-Path (Join-Path $UrData 'recipes\*.recipe.json'))) $text

    Start-Import $clanFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    $texts = @(Get-AllTexts $screen)
    Check '3 The import screen names the host, the poll and what the book keeps' (
        ($texts -contains 'ps99.biggamesapi.io') -and (@($texts -match '^Asks every \d+ seconds\.$').Count -gt 0) -and
        ($texts -contains 'KEPT IN YOUR SCORE BOOK') -and (@($texts -like '*Clan place').Count -gt 0)) ($texts -join ' | ')
    Check '3b With nothing ticked, Import is refused' ((Line $screen 'RefusalLine') -match 'Tick at least one stat') (Line $screen 'RefusalLine')

    Set-Tick (Get-Check $screen 'Show Points') $true
    Set-Tick (Get-Check $screen 'Send Points') $true
    Check '3c A Send tick shows the name RoRoRo uses' ([bool](Get-Edit $screen 'Name RoRoRo uses for Points')) 'Name RoRoRo uses for Points'
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')

    $setup = Wait-UrWindow '^Setup$' 30
    $title = Wait-Line $setup 'ClansPageTitle' '^Clans$' 30
    Check '4 A recipe with inputs opens Setup on its Clans page' ($title -eq 'Clans') "title='$title'"

    Select-SearchName $setup 'Your main clan' $Main
    $found = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '5 Picking the main clan reads it once and says who was found' ($found -match "^(Found .+ in $Main\.|None of your accounts are in $Main yet\.|Read $Main\.|Added $Main)") $found

    $main = @(Read-Sources | Where-Object { (Get-RoleText $_) -in @('main', '0') })
    Check '5b sources.json holds it as the main clan' (($main.Count -eq 1) -and ($main[0].inputs.clan -eq $Main)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)

    Close-UrWindow $setup
    $board = Get-BoardWindow
    Wait-Until { [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') } 20 | Out-Null
    $standing = Find-ByAutomationId $board 'StandingPanel1'
    Check '6 The board shows the main clan standing' ((Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main) "title='$(Line $standing 'PanelTitle')' subtitle='$(Line $standing 'PanelSubtitle')'"

    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    $state = Wait-Line $board 'StateLine' '^(Not started\.|Stopped\.|Reading|.+: )' 120
    Check '7 Test now reads without a failure' ($state -notmatch 'Something unexpected') "state='$state' detail='$(Line $board 'DetailLine')'"

    $setup = Open-SetupPage 'Diagnostics'
    $saved = Get-Clipboard -Raw
    Invoke-Element (Find-ByAutomationId $setup 'CopyDiagnosticsButton')
    Start-Sleep -Seconds 1
    $diag = Get-Clipboard -Raw
    if ($null -ne $saved) { Set-Clipboard -Value $saved }
    Check '8 Copied diagnostics carry no book content' (($diag -match 'no book content is included') -and ($diag -notmatch '"kind":')) (($diag -split "`n" | Select-Object -First 6) -join ' | ')
    if ($rororo) {
        Check '8b With RoRoRo running, the theme is followed' ($diag -match "THEME: following RoRoRo's theme") (($diag -split "`n" | Where-Object { $_ -match 'THEME' }) -join ' ')
    }

    Close-UrWindow $setup
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'window-smoke-board.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
```

- [ ] **Step 3: Create the Setup › Clans walk**

Create `tools/smoke/walk-setup-clans.ps1`

```powershell
# Setup > Clans on a clean data folder: the main clan, a clan your accounts are in, a watched clan,
# Make main, Remove, a repeat pick, the request line, and the Top switch when a group-list fixture exists.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$topFixture = Get-ChildItem $UrFixtures -Filter *.recipe.json | Where-Object { (Get-Content $_.FullName -Raw) -match '"groupName"' } | Select-Object -First 1
$backup = $null

function Get-SourceRoles { Read-Sources | ForEach-Object { "$($_.inputs.clan)=$(Get-RoleText $_)" } }

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @()
    $setup = Wait-UrWindow '^Setup$' 30

    Select-SearchName $setup 'Your main clan' $Main
    $mainLine = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '1 Main clan picked and read once' ($mainLine -match $Main) $mainLine

    Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    $altLine = Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '2 A clan your accounts are in is added and read' ($altLine -match $Alt) $altLine

    Invoke-Element (Find-ByAutomationId $setup 'WatchClanButton')
    $rival = Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt)
    $watchLine = Wait-Line $setup 'WatchFoundLine' '^Watching ' 20
    Check '3 A watched clan is added and says it is clan-level only' ($watchLine -match "^Watching $([regex]::Escape($rival))\.") $watchLine

    $roles = @(Get-SourceRoles)
    Check '4 sources.json holds one main, one mine and one watch' (
        ($roles -contains "$Main=main" -or $roles -contains "$Main=0") -and
        ($roles -contains "$Alt=mine" -or $roles -contains "$Alt=1") -and
        ($roles -contains "$rival=watch" -or $roles -contains "$rival=2")) ($roles -join ', ')

    Invoke-Element (Get-Button $setup "Make $Alt main")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '5 Make main moves the star' (($roles -contains "$Alt=main" -or $roles -contains "$Alt=0") -and ($roles -contains "$Main=mine" -or $roles -contains "$Main=1")) ($roles -join ', ')
    Invoke-Element (Get-Button $setup "Make $Main main")
    Start-Sleep -Seconds 1

    Invoke-Element (Get-Button $setup "Remove $rival")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '6 Remove takes the watched clan out' (@($roles | Where-Object { $_ -like "$rival=*" }).Count -eq 0) ($roles -join ', ')

    Select-SearchName $setup 'Your main clan' $Main
    $again = Wait-Line $setup 'MainFoundLine' 'already your main' 10
    Check '7 Picking the main clan again says so and adds nothing' ($again -eq "$Main is already your main clan.") $again

    $requests = Line $setup 'RequestsLine'
    Check '8 The request line names the host and a count per hour' ($requests -match '^Your PC asks ps99\.biggamesapi\.io about \d+ times an hour\.') $requests

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-clans.png') | Out-Null

    if ($topFixture) {
        Close-UrWindow $setup
        Start-Import $topFixture.FullName
        $screen = Wait-UrWindow '^Import recipe$' 30
        Check '9 A group-list import shows no Stats table' (-not (Find-ByAutomationId $screen 'StatsRows')) 'StatsRows absent'
        Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
        Start-Sleep -Seconds 2
        $setup = Open-SetupPage 'Clans'
        $switch = Find-ByAutomationId $setup 'TopSwitch'
        Check '9b The Top switch appears, named in the recipe''s words' ($switch -and $switch.Current.Name -eq 'Top of the battle') "switch='$($switch.Current.Name)'"
        Set-Tick $switch $false
        $top = @(Read-Sources | Where-Object { -not $_.inputs.clan })
        Check '9c Switching it off is saved at once' (($top.Count -eq 1) -and ($top[0].enabled -eq $false)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)
    }
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
```

- [ ] **Step 4: Create the Stats table walk**

Create `tools/smoke/walk-stats-table.ps1`

```powershell
# The Stats table on a clean data folder: the import screen's table and search, ticked rows staying visible,
# the name column, Setup > Stats reading names once, saving, and "tick at least one stat".
. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null
    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30

    $read = Find-ByAutomationId $screen 'ReadNamesButton'
    Check '1 A counters recipe offers one read of every game statistic' ($read -and $read.Current.Name -like 'Show every game statistic*') "button='$($read.Current.Name)'"

    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') 'EGGS'
    $showing = Wait-Line $screen 'ShowingLine' '^Showing ' 5
    Check '2 Search filters by label ignoring case' (($showing -match '^Showing 1 of 3 ') -and [bool](Get-Check $screen 'Show Eggs hatched') -and -not (Get-Check $screen 'Show Diamonds')) $showing

    Set-Tick (Get-Check $screen 'Show Eggs hatched') $true
    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') 'dia'
    $showing = Wait-Line $screen 'ShowingLine' '^Showing 2 of 3 ' 5
    Check '3 A ticked row stays visible under another search' (($showing -match '^Showing 2 of 3 ') -and [bool](Get-Check $screen 'Show Eggs hatched')) $showing

    Check '4 No name column before Send' (-not (Get-Edit $screen 'Name RoRoRo uses for Eggs hatched')) 'absent'
    Set-Tick (Get-Check $screen 'Send Eggs hatched') $true
    $name = Get-Edit $screen 'Name RoRoRo uses for Eggs hatched'
    Check '4b Send shows the pinned name' ($name -and $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'ps99.eggs-hatched') 'ps99.eggs-hatched'
    Check '4c The slot line counts history slots' ((Line $screen 'SlotLine') -match "of RoRoRo's 256 history slots") (Line $screen 'SlotLine')

    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') ''
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2

    $state = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-profile.state.json') -Raw | ConvertFrom-Json
    Check '5 Import saved the ticks' (($state.stats.eggs.show -eq $true) -and ($state.stats.eggs.send -eq $true)) ($state.stats | ConvertTo-Json -Compress)

    $setup = Open-SetupPage 'Stats'
    if ($rororo) {
        $names = Wait-Line $setup 'NamesLine' '^(Found \d|No statistic names came back|RoRoRo hasn|Could not|The source answered|[\d,]+ statistic names from)' 120
        Check '6 Setup > Stats reads counter names once when none are saved' ($names -notmatch '^Reading stat names once') $names
    }

    Set-Tick (Get-Check $setup 'Show Diamonds') $true
    Set-Tick (Get-Check $setup 'Show Eggs hatched') $false
    Set-Tick (Get-Check $setup 'Send Eggs hatched') $false
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    $saved = Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5
    $state = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-profile.state.json') -Raw | ConvertFrom-Json
    Check '7 Saving applies the new ticks' (($saved -like 'Saved.*') -and ($state.stats.diamonds.show -eq $true) -and ($state.stats.eggs.show -eq $false)) "$saved / $($state.stats | ConvertTo-Json -Compress)"

    Set-Tick (Get-Check $setup 'Show Diamonds') $false
    $refusal = Line $setup 'RefusalLine'
    $save = Find-ByAutomationId $setup 'SaveStatsButton'
    Check '8 Nothing ticked asks for one stat and Save is off' (($refusal -match 'Tick at least one stat') -and -not $save.Current.IsEnabled) $refusal

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-stats.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
```

- [ ] **Step 5: Create the starter board walk**

Create `tools/smoke/walk-starter-board.ps1`

```powershell
# The starter board on a clean data folder: a main clan, a clan your accounts are in, a watched clan and (when
# the fixture exists) the top list, then every Battle panel with its title, Start, Test now and Stop.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$topFixture = Get-ChildItem $UrFixtures -Filter *.recipe.json | Where-Object { (Get-Content $_.FullName -Raw) -match '"groupName"' } | Select-Object -First 1
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @('Points')
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-Element (Find-ByAutomationId $setup 'WatchClanButton')
    Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt) | Out-Null

    if ($topFixture) {
        Start-Import $topFixture.FullName
        $screen = Wait-UrWindow '^Import recipe$' 30
        Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
        Start-Sleep -Seconds 2
    }

    Close-UrWindow (Get-SetupWindow)
    $board = Get-BoardWindow

    $expected = [ordered]@{
        'StandingPanel1'       = 'Clan standing'
        'StandingPanel2'       = 'Clan standing'
        'RacePanel1'           = 'Battle race'
        'MyAccountsPanel1'     = 'My accounts'
        'PromotionCheckPanel1' = 'Promotion check'
        'AccountCardPanel1'    = 'Account card'
        'PastPeriodsPanel1'    = 'Past battles'
    }
    if ($topFixture) { $expected['TopPanel1'] = 'Top of the battle' }

    Wait-Until { [bool](Find-ByAutomationId (Get-BoardWindow) 'PastPeriodsPanel1') } 20 | Out-Null
    foreach ($id in $expected.Keys) {
        $panel = Find-ByAutomationId $board $id
        Check "1 $id is on the board" ($panel -and (Line $panel 'PanelTitle') -eq $expected[$id]) "title='$(Line $panel 'PanelTitle')'"
    }
    Check '1b The board has no Grind panels' (-not (Find-ByAutomationId $board 'ProfileStatPanel1')) 'ProfileStatPanel1 absent'
    Check '1c The promotion check names both clans' ((Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle') -match "^$Alt .+ $Main$") (Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle')

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $period = Wait-Line $board 'PeriodLine' 'next read' 240
    Check '2 Start reads, and the period line gains the next read' ($period -match 'next read') $period
    $state = Wait-Line $board 'StateLine' '^(Reading \d+ sources?\.|.+: )' 60
    Check '2b The state line counts the sources or names one in trouble' ($state -match '^(Reading \d+ sources?\.|.+: )') $state

    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    Start-Sleep -Seconds 20
    $accounts = @(Get-AllTexts (Find-ByAutomationId $board 'MyAccountsPanel1'))
    $grouped = @($accounts | Where-Object { $_ -like "*$Main" -or $_ -eq $Alt -or $_ -eq 'Not in a watched clan' }).Count -gt 0
    Check '3 My accounts groups your accounts by clan' $grouped ($accounts -join ' | ')

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'starter-board.png') | Out-Null

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $stopped = Wait-Line $board 'StateLine' '^Stopped\.$' 20
    Check '4 Stop stops' ($stopped -eq 'Stopped.') $stopped
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
```

- [ ] **Step 6: Create the score book walk and the privacy check**

Create `tools/smoke/walk-score-book.ps1`

```powershell
# Setup > Score book on a clean data folder: one read of a main clan writes the book, the page counts it,
# says why a stopped source isn't recording, and diagnostics never copy book content.
# Needs a clan battle the source reports (activeClanBattle names one); an idle source writes nothing.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$backup = $null

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @()
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    Start-Sleep -Seconds 20

    $setup = Open-SetupPage 'Score book'
    $folder = Line $setup 'BookFolderLine'
    Check '1 The page names the book folder' ($folder -like '*626labs.ur-score\scorebook') $folder

    $recipes = @(Get-AllTexts (Find-ByAutomationId $setup 'BookRecipesList'))
    Check '2 Per recipe: readings, first reading, finals and size' (
        ($recipes -contains 'Pet Sim 99 clan battle points') -and (@($recipes -match '^\d[\d,]* readings? kept .+ (bytes|KB|MB)$').Count -eq 1)) ($recipes -join ' | ')

    $notRecording = @(Get-AllTexts (Find-ByAutomationId $setup 'NotRecordingList'))
    Check '3 A stopped source says it is stopped' (@($notRecording -like '*Stopped. Press Start on the board.*').Count -gt 0) ($notRecording -join ' | ')

    $slugDir = Join-Path $UrData 'scorebook\pet-sim-99-clan-battle-points'
    $months = @(Get-ChildItem $slugDir -Filter *.jsonl -ErrorAction SilentlyContinue)
    $recipeTexts = @(Get-ChildItem (Join-Path $slugDir 'recipes') -Filter *.json -ErrorAction SilentlyContinue)
    $lines = @($months | ForEach-Object { Get-Content $_.FullName } | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    Check '4 A read wrote v1 lines to a UTC month file and kept the recipe text once' (
        ($months.Count -ge 1) -and ($months[0].Name -match '^\d{4}-\d{2}\.jsonl$') -and ($recipeTexts.Count -eq 1) -and
        ($lines.Count -ge 1) -and (@($lines | Where-Object { $_.v -ne 1 }).Count -eq 0)) "months=$($months.Name -join ',') recipes=$($recipeTexts.Count) lines=$($lines.Count)"

    $setup = Open-SetupPage 'Diagnostics'
    $saved = Get-Clipboard -Raw
    Invoke-Element (Find-ByAutomationId $setup 'CopyDiagnosticsButton')
    Start-Sleep -Seconds 1
    $diag = Get-Clipboard -Raw
    if ($null -ne $saved) { Set-Clipboard -Value $saved }
    Check '5 Copied diagnostics count the book but carry none of it' (($diag -match 'pending=\d+ dropped=\d+ \(no book content is included\)') -and ($diag -notmatch '"accounts":')) 'checked clipboard'

    # Exit 2 means there was nothing to check (RoRoRo never listed accounts); the live walk runs it with RoRoRo up.
    & (Join-Path $PSScriptRoot 'check-book-privacy.ps1') -DataFolder $UrData
    Check '6 Every account in the book is one of yours' ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 2) "check-book-privacy exit $LASTEXITCODE"
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
```

Create `tools/smoke/check-book-privacy.ps1`

```powershell
# Spec 5.6: another player's id never reaches disk. Loads your own Roblox user ids from accounts.json and
# checks every score book line: its account keys, its unavailable ids, and that no line carries response text.
# Exit 0 when clean, 1 when anything is not yours, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$accountsFile = Join-Path $DataFolder 'accounts.json'
$bookRoot = Join-Path $DataFolder 'scorebook'
if (-not (Test-Path $accountsFile)) { "No accounts.json in $DataFolder. Start RoRoRo while Ur Score runs, then check again."; exit 2 }
if (-not (Test-Path $bookRoot)) { "No score book in $DataFolder yet."; exit 2 }

$mine = @(Get-Content $accountsFile -Raw | ConvertFrom-Json | ForEach-Object { [string]$_.robloxUserId } | Where-Object { $_ -and $_ -ne '0' })
if ($mine.Count -eq 0) { "accounts.json lists no Roblox user ids."; exit 2 }

$lines = 0; $reads = 0; $finals = 0; $bad = 0; $broken = 0
foreach ($file in Get-ChildItem $bookRoot -Recurse -Filter *.jsonl) {
    $number = 0
    foreach ($text in [System.IO.File]::ReadLines($file.FullName)) {
        $number++
        if (-not $text.Trim()) { continue }
        try { $line = $text | ConvertFrom-Json } catch { $broken++; continue }
        $lines++
        if ($line.kind -eq 'final') { $finals++ } else { $reads++ }

        if ($line.accounts) {
            foreach ($key in $line.accounts.PSObject.Properties.Name) {
                if ($mine -notcontains $key) { "NOT YOURS: $($file.Name):$number account key $key"; $bad++ }
            }
        }
        foreach ($id in @($line.unavail)) {
            if ($id -and ($mine -notcontains [string]$id)) { "NOT YOURS: $($file.Name):$number unavail $id"; $bad++ }
        }
        if ($line.role -eq 'watch' -and $line.accounts -and @($line.accounts.PSObject.Properties).Count -gt 0) {
            "WATCH LINE WITH ACCOUNTS: $($file.Name):$number"; $bad++
        }
        if ($text -match 'PointContributions|UserID|DisplayName|displayName') {
            "RESPONSE TEXT: $($file.Name):$number"; $bad++
        }
    }
}

"Your ids: $($mine.Count). Lines: $lines ($reads read, $finals final). Unparseable lines skipped: $broken. Problems: $bad."
if ($bad -gt 0) { exit 1 }
exit 0
```

- [ ] **Step 7: Create the README**

Create `tools/smoke/README.md`

```markdown
# Ur Score smoke scripts

UI Automation walks of the real window: they start the Release build, click through it the way a person would, and print PASS or FAIL per step. They are not part of `dotnet test`; run them by hand before a release and after UI changes.

## Before you run

- Windows PowerShell 5.1 or PowerShell 7, from any folder. Paths come from the scripts' own location.
- A Release build: `dotnet build Ur-Score.csproj -c Release` from the repo root. Close Ur Score first; a running copy locks `bin\Release`.
- RoRoRo running is optional. Without it, steps that need your accounts are skipped or accept the "RoRoRo hasn't listed your accounts" wording.
- Leave the mouse alone while a walk runs; `shot.ps1` brings windows to the front.

## Your data is safe

Every walk that needs a clean start moves `%LOCALAPPDATA%\626labs.ur-score` to `626labs.ur-score.smoke-backup-<time>` and puts it back in a `finally`, even when a step throws. If a run is killed mid-walk, rename the newest `.smoke-backup-*` folder back to `626labs.ur-score` yourself.

## The scripts

| Script | What it walks |
|---|---|
| `window-smoke.ps1 [-Main CCGP]` | First run, a refused file, the import screen, Setup opening on Clans, the main clan, the board, Test now, copied diagnostics |
| `walk-setup-clans.ps1 [-Main CCGP] [-Alt K0i2]` | Main, mine and watched clans, Make main, Remove, a repeat pick, the request line, the Top switch |
| `walk-stats-table.ps1` | The Stats table: search, ticked rows kept, the name column, Setup › Stats, saving, "tick at least one stat" |
| `walk-starter-board.ps1 [-Main CCGP] [-Alt K0i2]` | Every Battle panel by automation id and title, Start, Test now, Stop |
| `walk-score-book.ps1 [-Main CCGP]` | Setup › Score book counts, the not-recording reason, book files, diagnostics without book content, the privacy check |
| `check-book-privacy.ps1 [-DataFolder path]` | Every account key and unavailable id in every book line is one of yours (exit 0), else lists them (exit 1) |
| `shot.ps1 -OutPath file.png [-Title 'Setup']` | A screenshot of one window |

Screenshots from the walks go to `artifacts\smoke\` (gitignored build output).

## Helpers

`uia.ps1` (UI Automation, starting and stopping Ur Score, the data folder, results) and `uia-import.ps1` (the file picker, the import screen, `sources.json`) are dot-sourced by every walk. The automation ids they rely on are listed at the top of `docs/plans/2026-09-14-score-book-stage-1.md`'s UI tasks; a change to an id changes its script in the same commit.

To add a walk: dot-source `uia-import.ps1`, wrap the body in `try { $backup = Move-UrDataAside; ... } finally { if ($null -ne $backup) { Restore-UrData $backup }; Show-Results }`, and record each step with `Check 'n What it proves' <bool> <what was seen>`.
```

- [ ] **Step 8: Run the fast gates and the scripts' parse check**

Run: `dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror` and `dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build`
Expected: both pass (no source changed in this task).

Run each script through the PowerShell parser without executing it:

```powershell
Get-ChildItem tools/smoke -Filter *.ps1 | ForEach-Object {
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors) | Out-Null
    "{0}: {1} parse error(s)" -f $_.Name, @($errors).Count
}
```

Expected: every file reports `0 parse error(s)`.

Run: `git grep -n -I "C:\\\\Users\|estev" -- tools/smoke`
Expected: no output (no machine path in any script).

- [ ] **Step 9: Commit**

```text
git add tools/smoke
git commit -m "smoke: commit the UI Automation walks for Setup, the stats table, the board and the score book

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 10: Live walk of stage 1 (controller)**

Run this against RoRoRo 1.28 with your own accounts. Record each numbered result (PASS/FAIL plus what was seen) in the execution record.

1. **Build.** Quit Ur Score (and any RoRoRo-launched copy of the plugin) so `bin\Release` is free. From the repo root:
   ```text
   dotnet build Ur-Score.csproj -c Release
   dotnet build tests/Ur-Score.Tests.csproj -c Release -warnaserror
   dotnet test tests/Ur-Score.Tests.csproj -c Release --no-build
   ```
   All three succeed.
2. **RoRoRo.** Start RoRoRo 1.28 and confirm it lists your accounts. `Get-Process ROROROblox.App | Select-Object Path` shows the build you mean to test.
3. **Scripted walks, clean data.** Each moves your data aside and restores it. Run them one at a time, and read each results table:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/smoke/window-smoke.ps1 -Main CCGP
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-setup-clans.ps1 -Main CCGP -Alt K0i2
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-stats-table.ps1
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-starter-board.ps1 -Main CCGP -Alt K0i2
   powershell -ExecutionPolicy Bypass -File tools/smoke/walk-score-book.ps1 -Main CCGP
   ```
   Every step passes. After each, `Test-Path "$env:LOCALAPPDATA\626labs.ur-score"` is back to what it was, and no `626labs.ur-score.smoke-backup-*` folder remains.
4. **Real read with your own data folder** (don't move it aside). Launch `bin\Release\net10.0-windows\626labs.ur-score.exe`:
   1. If your part 2a clan recipe is installed, its saved clan now appears in Setup › Clans as a source (migration). Otherwise import `tests/Fixtures/petsim99-clan-battle.recipe.json`, tick Show and Send for Points, and let Setup open on Clans.
   2. **Your main clan:** pick CCGP. The line says `Found <your account> in CCGP.`
   3. **Add a clan your accounts are in:** pick K0i2. The line names the alts found there.
   4. **Watch a clan:** pick a rival none of your accounts are in, for example a clan you see near CCGP in the Top of the battle list. The line says it is watched.
   5. Import the group-list fixture from `tests/Fixtures/` (the one whose last step has `"groupName"`) and leave **Top of the battle** on.
   6. Close Setup. The board shows every Battle panel: Clan standing ×2, Battle race, My accounts, Promotion check (K0i2 → CCGP), Account card, Top of the battle, Past battles.
   7. Press **Start**, wait for two read cycles (about 6 minutes), then press **Test now** once.
5. **The book was written.** In PowerShell:
   ```powershell
   $book = Join-Path $env:LOCALAPPDATA '626labs.ur-score\scorebook\pet-sim-99-clan-battle-points'
   Get-ChildItem $book -Filter *.jsonl | Format-Table Name, Length
   $lines = Get-ChildItem $book -Filter *.jsonl | ForEach-Object { Get-Content $_.FullName } | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }
   "reads: $(@($lines | Where-Object kind -eq 'read').Count)  finals: $(@($lines | Where-Object kind -eq 'final').Count)"
   $lines | Where-Object kind -eq 'read' | Group-Object source | Format-Table Name, Count
   $lines | Where-Object { $_.kind -eq 'final' -and $_.inputs.clan -eq 'CCGP' } | Select-Object -ExpandProperty period | Select-Object -ExpandProperty value | Sort-Object -Unique
   ```
   Expect:
   - the UTC month file for today;
   - `read` lines for all three clan sources (the rival's with an empty `accounts`);
   - no `read` line for the group-list recipe (it never records);
   - `kind:"final"` lines for CCGP's past battles, the last list naming several battles backfilled from `data.Battles`, each with `trigger` `backfill` or `ended`.
6. **No other player's id on disk.**
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/smoke/check-book-privacy.ps1
   ```
   Expected: `Problems: 0.` and exit code 0.

   For a second look, list any account key in any line that isn't in `accounts.json`. It should print nothing:
   ```powershell
   $data = Join-Path $env:LOCALAPPDATA '626labs.ur-score'
   $mine = @(Get-Content (Join-Path $data 'accounts.json') -Raw | ConvertFrom-Json | ForEach-Object { [string]$_.robloxUserId })
   Get-ChildItem (Join-Path $data 'scorebook') -Recurse -Filter *.jsonl | ForEach-Object {
       foreach ($text in [System.IO.File]::ReadLines($_.FullName)) {
           if (-not $text.Trim()) { continue }
           $line = $text | ConvertFrom-Json
           if ($line.accounts) { $line.accounts.PSObject.Properties.Name | Where-Object { $mine -notcontains $_ } | ForEach-Object { "$($line.source) $($line.t): $_" } }
       }
   }
   ```
7. **Restart keeps everything.** Quit Ur Score from the board and start it again. Past battles still lists the backfilled battles, and the Battle race line starts from the earlier reads. The book files did not shrink, and no new `final` line was written for a battle that already had one: compare the finals count from step 5 before and after one more read.
8. **Setup pages.**
   - **Score book:** readings, first reading, "N finished battles kept" and a size; nothing listed under Not recording while running.
   - **Diagnostics:** each source's last and next read.
   - **Copy diagnostics:** the clipboard holds no key and no book line (`"accounts":` absent).
9. **RoRoRo down.** Quit RoRoRo while Ur Score runs.
   - The detail line says `RoRoRo is not running. Still reading and keeping your scores; nothing is being sent.`
   - Within one cycle, a new `read` line appears for CCGP with your accounts, using the saved `accounts.json`.
   - Start RoRoRo again: sending resumes, and Setup › Alerts shows its Sent count rising.
10. **Screenshots** for the record:
    ```powershell
    powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -OutPath artifacts/smoke/live-board.png
    powershell -ExecutionPolicy Bypass -File tools/smoke/shot.ps1 -Title Setup -OutPath artifacts/smoke/live-setup.png
    ```

## After stage 1: release (the controller does these)

1. **Version:** set `0.2.0` in `manifest.json` (`"version"`) and in `Ur-Score.csproj` (`<Version>`). Commit `release: 0.2.0`.
2. **Merge:** open the PR from `feat/score-book` to `master`, wait for the `test` workflow to pass, then merge.
3. **Make the repo public** (Este authorized this on 2026-09-14):
   `gh repo edit estevanhernandez-stack-ed/Ur-Score --visibility public --accept-visibility-change-consequences`.
4. **Tag and release:** tag `v0.2.0` on `master` and push the tag. The `release` workflow runs the tests, checks the version, builds the plugin with .NET included, and attaches `manifest.json`, `manifest.sha256` and `plugin.zip`. Confirm all three are on the release.
5. **Install from the release, not the build folder:**
   - Back up `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score` to the session scratchpad and remove it.
   - In RoRoRo's Plugins page, use Install from URL with `https://github.com/estevanhernandez-stack-ed/Ur-Score/releases/latest/download/` and accept the consent.
   - Walk `tools/smoke/window-smoke.ps1` against the installed copy.
6. **Clan post:** draft the how-to for the clan (install link, first-run clan search, ticking stats, metric alerts in RoRoRo, the webhook). Este posts it.

Stage 2 (boards you edit, the gallery, tabs, pop-outs) has its own plan, `docs/plans/2026-09-14-score-book-stage-2.md`, written while stage 1 is being built.
