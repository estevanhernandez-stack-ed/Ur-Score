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
