using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>A clock that stands still, in UTC unless a zone is supplied, so "today" and "7 days" are fixed.</summary>
internal sealed class FixedTime(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.Utc;
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

    /// <summary>
    /// A read of this session that found its source between periods, as <c>RecipeWatch</c> stops one: the state and the
    /// recipe's reason, and no rows or headline, because an idle source has no member list to give.
    /// </summary>
    public static RecipeSnapshot Idle(string sourceId) =>
        new(WatchState.SourceIdle, "No clan battle running", [], [], 0) { SourceId = sourceId };

    /// <summary>A read of this session that failed, as <c>RecipeWatch</c> stops one: nothing came back.</summary>
    public static RecipeSnapshot Unreachable(string sourceId) =>
        new(WatchState.SourceUnreachable, "The source could not be reached.", [], [], 0) { SourceId = sourceId };

    public static LiveBoard Live(
        IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, IReadOnlyDictionary<string, RecipeSnapshot> snapshots,
        bool running = false, IReadOnlyDictionary<string, DateTimeOffset>? lastRead = null, IReadOnlyList<HostAccount>? accounts = null,
        IReadOnlyDictionary<string, RecipeSnapshot>? remembered = null) =>
        new(sources, installed, snapshots, lastRead ?? new Dictionary<string, DateTimeOffset>(), accounts ?? Accounts, new FixedTime(Now), running,
            Remembered: remembered);

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
