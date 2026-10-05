using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Games;

/// <summary>What a reader is called to a person: its mode, never its slug (an orphan has no mode, so it keeps its slug).</summary>
public static class ReaderNames
{
    public const string ClansList = " · clans list";

    /// <summary>
    /// The mode's name. A clans list gets <see cref="ClansList"/> added when its mode has another reader, because
    /// "Battle" alone would name two things: the clan's own read and the field it is racing.
    /// </summary>
    public static string For(string slug, GameCatalog catalog, IReadOnlyList<InstalledRecipe> installed)
    {
        if (catalog.ModeOf(slug) is not { } mode) return slug;

        var isList = installed.FirstOrDefault(i => i.Recipe.Slug == slug)?.Recipe.IsGroupList == true;
        return isList && mode.Reads.Count > 1 ? mode.Name + ClansList : mode.Name;
    }
}
