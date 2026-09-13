using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Labs626.UrScore.Recipes;

/// <summary>
/// Where numbers are and what the data means. Never what happens with a number (spec §2): nothing
/// here can name a threshold, a rule, an alert, an account to send, a stat to tick, or anything to
/// trigger.
/// </summary>
public sealed record Recipe(
    int Version,
    string Name,
    string Credit,
    string? Author,
    int EverySeconds,
    IReadOnlyList<RecipeInput> Inputs,
    IReadOnlyList<RecipeKey> Keys,
    IReadOnlyList<RecipeStep> Steps,
    IReadOnlyList<RecipeHeadline> Headline,
    string? Icon = null,
    string PlaceLabel = Recipe.DefaultPlaceLabel)
{
    public const int SupportedVersion = 1;

    /// <summary>Whatever a recipe asks for, Ur Score never polls faster than this (spec §4.4).</summary>
    public const int MinimumEverySeconds = 60;

    public const string DefaultPlaceLabel = "Place";

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

/// <summary>
/// One request. Only the last step reads: <see cref="Values"/> is empty on every other step, and on
/// the last step it always holds at least one stat, because the parser turns the single-<c>value</c>
/// shorthand into a one-item list with id <see cref="RecipeValue.ShorthandId"/>.
/// </summary>
public sealed record RecipeStep(
    string Url,
    IReadOnlyList<string> UseKeys,
    IReadOnlyDictionary<string, string> Take,
    string? IdleWithout,
    string? IdleMessage,
    string? Rows,
    string? UserId,
    bool PerAccount,
    IReadOnlyList<RecipeValue> Values,
    RecipeCounters? Counters = null,
    RecipeUnavailable? Unavailable = null,
    string? AbsentMessage = null);

/// <summary>
/// One stat the user can tick. <see cref="MetricId"/> is only a suggestion: the name RoRoRo gets is
/// whatever the user accepted, pinned in the recipe's state. <see cref="Sum"/> says whether adding
/// this stat up across accounts means anything; it describes the data and triggers nothing.
/// </summary>
public sealed record RecipeValue(string Id, string Label, string Path, string MetricId, bool Sum = true)
{
    /// <summary>The id a single <c>value</c> is given when the parser turns it into a list.</summary>
    public const string ShorthandId = "value";
}

/// <summary>An object of named numbers the user can search and pick from (spec §3.2).</summary>
public sealed record RecipeCounters(string Label, string Path, string MetricIdPrefix)
{
    /// <summary>A picked counter's stat key is this prefix plus its name, so it never collides with a value id.</summary>
    public const string KeyPrefix = "counter:";
}

/// <summary>
/// "This account's data is not readable, and here is why" (spec §3.2). <see cref="IsText"/> holds the
/// JSON value to compare against as text: the string itself, a number as written, or true/false.
/// </summary>
public sealed record RecipeUnavailable(string Path, JsonValueKind IsKind, string IsText, string Message)
{
    /// <summary>Same JSON kind and same value. A number compares by value, so 0 and 0.0 match.</summary>
    public bool Matches(JsonElement element) => element.ValueKind == IsKind && IsKind switch
    {
        JsonValueKind.True or JsonValueKind.False => true,
        JsonValueKind.String => string.Equals(element.GetString(), IsText, StringComparison.Ordinal),
        JsonValueKind.Number => element.TryGetDouble(out var number)
                                && double.TryParse(IsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected)
                                && number == expected,
        _ => false,
    };
}

public sealed record RecipeHeadline(string Label, string Path, bool Sum = true);
