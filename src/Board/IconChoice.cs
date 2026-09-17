using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

// The bare name Source would find the Labs626.UrScore.Source namespace from in here, so the type is aliased after the namespace
// line, which is consulted first.
using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// The window's icon: the picture file, or null for Ur Score's own, and whose it is. <see cref="Name"/> is null when no source's
/// picture belongs on the window at all; otherwise it names the clan for a screen reader, whether or not the picture is there yet.
/// </summary>
public sealed record WindowIcon(string? File, string? Name)
{
    public static readonly WindowIcon None = new(null, null);

    /// <summary>A picture belongs here, so its space is kept while it isn't there yet or doesn't decode (A26): nothing moves when it lands.</summary>
    public bool HasSlot => Name is not null;
}

/// <summary>
/// Whose picture goes where (backlog V3-S.7). An icon belongs to a source, and every choice here is made from the sources and
/// the recipes alone: never from which picture arrived first, so a clan read last can never stand in for the main clan.
/// </summary>
public static class IconChoice
{
    /// <summary>The sources that may keep a picture: those whose installed recipe names an icon. Switched-off ones included (S1-14.1).</summary>
    public static IReadOnlySet<string> SourcesWithIcons(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed) =>
        sources.Where(s => IconRecipe(s, installed) is not null).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The source the window's icon belongs to: the first switched-on main whose recipe names an icon. No other role ever stands
    /// in, so with no such main the window keeps Ur Score's own.
    /// </summary>
    public static Source? WindowSource(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed) =>
        sources.FirstOrDefault(s => s.Enabled && s.Role == SourceRole.Main && IconRecipe(s, installed) is not null);

    /// <summary>The window's icon: its source's picture, which may not be there yet, named for that source.</summary>
    public static WindowIcon ForWindow(IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, Func<string, string?> fileFor)
    {
        if (WindowSource(sources, installed) is not { } source || IconRecipe(source, installed) is not { } recipe) return WindowIcon.None;

        return new WindowIcon(fileFor(source.Id), PanelText.IconName(LiveBoard.NameOf(source, recipe), recipe));
    }

    /// <summary>Setup › Recipes: a recipe's row shows its own main's picture, chosen as the window's is, and no other source's.</summary>
    public static string? ForRecipe(string recipeSlug, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed, Func<string, string?> fileFor) =>
        RecipeSource(recipeSlug, sources, installed) is { } main ? fileFor(main.Id) : null;

    /// <summary>The source a recipe's row draws the picture of: that recipe's own switched-on main, when the recipe names an icon.</summary>
    public static Source? RecipeSource(string recipeSlug, IReadOnlyList<Source> sources, IReadOnlyList<InstalledRecipe> installed) =>
        WindowSource([.. sources.Where(s => string.Equals(s.Recipe, recipeSlug, StringComparison.Ordinal))], installed);

    /// <summary>The source's installed recipe when it names an icon, else null.</summary>
    private static Recipe? IconRecipe(Source source, IReadOnlyList<InstalledRecipe> installed) =>
        installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is { Icon: not null } recipe
            ? recipe
            : null;
}
