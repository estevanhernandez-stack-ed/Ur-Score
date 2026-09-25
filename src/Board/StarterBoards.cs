using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>One panel on a board: its type, how many of the 12 columns it spans, and what it shows.</summary>
public sealed record PanelSpec(PanelType Type, int Span, PanelSettings Settings);

/// <summary>
/// Which empty state a board shows. <see cref="BookUnread"/> is the board window's own (the score book couldn't be read, backlog
/// S1-14.2); no starter board is ever built with it.
/// </summary>
public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels, BookUnread }

/// <summary>A starter board's panels, or the empty state it shows instead, and the recipe that empty state names.</summary>
public sealed record StarterBoard(string Name, BoardEmpty Empty, IReadOnlyList<PanelSpec> Panels, string? RecipeSlug);

/// <summary>
/// The default tabs (default views design): Battle, the tab you watch on battle day, and Alts, your accounts side by
/// side. Each is built from your recipes and sources as they are now, in rows that fill the 12 columns and close up
/// when a panel can't be built (D6). Pure, so which panels appear for which sources is testable.
/// </summary>
public static class StarterBoards
{
    public const string Battle = "Battle";
    public const string Alts = "Alts";

    /// <summary>Every starter, in tab order.</summary>
    public static IReadOnlyList<string> Names { get; } = [Battle, Alts];

    /// <summary>A starter's name in ids and in <c>boards.json</c>'s <c>follows</c>: "battle", "alts".</summary>
    public static string KeyOf(string name) => name.ToLowerInvariant();

    /// <summary>Every starter as your sources build it now, in tab order. Any of them may be an empty state.</summary>
    public static IReadOnlyList<StarterBoard> All(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources) =>
        [.. Names.Select(name => Build(installed, sources, name))];

    /// <summary>The starter a key names ("alts", in any letter case), or null.</summary>
    public static StarterBoard? Named(IReadOnlyList<StarterBoard> starters, string? key) =>
        key is null ? null : starters.FirstOrDefault(s => string.Equals(KeyOf(s.Name), key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The empty state shown when no starter has a panel (D4): import a recipe first, then choose the source a starter
    /// needs, else the first starter's own.
    /// </summary>
    public static StarterBoard EmptyState(IReadOnlyList<StarterBoard> starters) =>
        starters.FirstOrDefault(s => s.Empty == BoardEmpty.NoRecipes)
        ?? starters.FirstOrDefault(s => s.Empty == BoardEmpty.NoSources)
        ?? starters[0];

    /// <summary>
    /// One starter: Battle from a recipe with a period, Alts from one without (your accounts one by one first). A
    /// starter whose kind of recipe has no ticked stat is empty. <paramref name="name"/> is a starter's name or its key
    /// in any letter case ("Alts", "alts", as <c>follows</c> holds it); any other name throws.
    /// </summary>
    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string name = Battle)
    {
        var named = Names.FirstOrDefault(n => string.Equals(n, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No starter is named '{name}'.", nameof(name));
        var alts = named == Alts;
        if (installed.Count == 0) return new StarterBoard(named, BoardEmpty.NoRecipes, [], null);

        var ticked = installed.Where(i => !i.Recipe.IsGroupList && i.State.TrackedStats(i.Recipe).Count > 0).ToList();
        if (ticked.Count == 0)
        {
            var first = installed.FirstOrDefault(i => !i.Recipe.IsGroupList) ?? installed[0];
            return new StarterBoard(named, BoardEmpty.NoStats, [], first.Recipe.Slug);
        }

        var enabled = sources.Where(s => s.Enabled).ToList();
        var kind = ticked.Where(i => (i.Recipe.Period is null) == alts).ToList();
        if (kind.Count == 0) return new StarterBoard(named, BoardEmpty.NoStats, [], ticked[0].Recipe.Slug);

        return alts ? AltsBoard(kind, enabled) : BattleBoard(installed, kind, enabled);
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

        if (anchor is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Battle, BoardEmpty.NoSources, [], slug);

        var stat = FirstStat(recipe);
        var otherMine = mine.FirstOrDefault(s => s.Id != anchor?.Id);
        var top = enabled.FirstOrDefault(s => installed.Any(i => Of(s, i) && i.Recipe.IsGroupList));
        var race = new[] { main }.OfType<Source>().Concat(mine).Concat(watch)
            .DistinctBy(s => s.Id).Take(PanelModels.MaxRace).Select(s => s.Id).ToList();

        var panels = new List<PanelSpec>();

        // 1. Your main clan's standing, a clan your accounts are in, and the race beside them (the mock's first row). One
        // clan races only with a clans list on, which brings the rivals in; alone it would be a single line (2026-09-24).
        panels.AddRange(Row(
            (PanelType.Standing, 3, anchor is null ? null : new PanelSettings(slug, SourceId: anchor.Id)),
            (PanelType.Standing, 3, otherMine is null ? null : new PanelSettings(slug, SourceId: otherMine.Id)),
            (PanelType.Race, 6, race.Count >= 2 || (race.Count == 1 && top is not null) ? new PanelSettings(slug, SourceIds: race) : null)));

        // 2. My accounts, the promotion check from that clan to the main, and the top of the battle.
        panels.AddRange(Row(
            (PanelType.MyAccounts, 5, stat is null ? null : new PanelSettings(slug, Stat: stat)),
            (PanelType.PromotionCheck, 4, main is not null && otherMine is not null && stat is not null
                ? new PanelSettings(slug, SourceId: otherMine.Id, ToSourceId: main.Id, Stat: stat)
                : null),
            (PanelType.Top, 3, top is null ? null : new PanelSettings(top.Recipe, SourceId: top.Id))));

        // 3. Past battles of the main clan, and records.
        panels.AddRange(Row(
            (PanelType.PastPeriods, 6, anchor is not null && recipe.Recipe.Period?.Past is not null ? new PanelSettings(slug, SourceId: anchor.Id, Stat: stat) : null),
            (PanelType.Records, 6, stat is null ? null : new PanelSettings(slug, Stat: stat))));

        return new StarterBoard(Battle, BoardEmpty.None, panels, slug);
    }

    private static StarterBoard AltsBoard(IReadOnlyList<InstalledRecipe> withoutPeriod, IReadOnlyList<Source> enabled)
    {
        bool Of(Source s, InstalledRecipe r) => string.Equals(s.Recipe, r.Recipe.Slug, StringComparison.Ordinal);

        // A recipe that reads your accounts one by one first, then any other without a period; one with a source on first.
        var ordered = withoutPeriod.OrderBy(r => r.Recipe.LastStep.PerAccount ? 0 : 1).ToList();
        var recipe = ordered.FirstOrDefault(r => enabled.Any(s => Of(s, r))) ?? ordered[0];
        var slug = recipe.Recipe.Slug;
        var source = enabled.FirstOrDefault(s => Of(s, recipe));

        if (source is null && recipe.Recipe.Inputs.Count > 0) return new StarterBoard(Alts, BoardEmpty.NoSources, [], slug);

        var stat = FirstStat(recipe);
        var panels = new List<PanelSpec>();

        // 1. The accounts table, full width.
        panels.AddRange(Row((PanelType.AccountsTable, 12, new PanelSettings(slug, SourceId: source?.Id))));

        // 2. Records and the account card, which shows the account picked in the table.
        panels.AddRange(Row(
            (PanelType.Records, 5, stat is null ? null : new PanelSettings(slug, Stat: stat)),
            (PanelType.AccountCard, 7, stat is null ? null : new PanelSettings(slug, Stat: stat))));

        return new StarterBoard(Alts, BoardEmpty.None, panels, slug);
    }

    /// <summary>
    /// One row (D6): the panels that could be built keep the row's spans when all of them could, else share the
    /// 12 columns equally, so a missing panel never leaves a hole.
    /// </summary>
    private static IEnumerable<PanelSpec> Row(params (PanelType Type, int Span, PanelSettings? Settings)[] slots)
    {
        var present = slots.Where(s => s.Settings is not null).ToList();
        var share = BoardLayout.Columns / Math.Max(1, present.Count);
        return present.Select(s => new PanelSpec(s.Type, present.Count == slots.Length ? s.Span : share, s.Settings!));
    }
}
