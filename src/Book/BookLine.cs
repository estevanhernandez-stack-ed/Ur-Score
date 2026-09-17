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
    bool? Stale = null,
    IReadOnlyDictionary<string, int>? Ranked = null);

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

    /// <summary>
    /// A line that isn't valid JSON, carries NUL, has an unknown <c>v</c>, or is missing a part a reader relies on
    /// (see <see cref="Complete"/>) is null, and a reader skips it.
    /// </summary>
    public static BookLine? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return null;

        try
        {
            var line = JsonSerializer.Deserialize<BookLine>(text, Options);
            return line is { V: BookLine.Version } && Complete(line) ? line : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// JSON nulls nested in a hand-edited or corrupted line (<c>"inputs":{"clan":null}</c>, an account with no
    /// <c>v</c>, <c>"recipe":{}</c>) deserialize without complaint and would throw later, in the finals index or a
    /// panel, taking the whole book load with them. Such a line is not a line.
    /// </summary>
    private static bool Complete(BookLine line) =>
        line is { Kind: not null, Recipe: not null, Inputs: not null, Headline: not null, Stats: not null, Accounts: not null }
        && !string.IsNullOrEmpty(line.Recipe.Slug)
        && !string.IsNullOrEmpty(line.Recipe.Hash)
        && line.Inputs.Values.All(v => v is not null)
        && line.Stats.All(s => s is not null)
        && line.Accounts.Values.All(a => a?.V is not null)
        && (line.Kind != BookLine.KindFinal || !string.IsNullOrEmpty(line.Period?.Value));

    /// <summary>Always UTC with a Z and milliseconds, so lines sort and compare as text.</summary>
    private sealed class UtcTimeConverter : JsonConverter<DateTimeOffset>
    {
        /// <summary>A null token (a hand-edited or corrupted line) is a format problem, not a framework crash: throwing
        /// <see cref="JsonException"/> here lets <see cref="BookJson.TryParse"/> skip the line like any other bad one.</summary>
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) throw new JsonException("expected a date-time, not null");
            return DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
