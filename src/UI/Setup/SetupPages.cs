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
