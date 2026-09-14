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
