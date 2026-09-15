using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

/// <summary>One panel on a board: its type, how many of the 12 columns it spans, and what it shows.</summary>
public sealed record PanelSpec(PanelType Type, int Span, PanelSettings Settings);

public enum BoardEmpty { None, NoRecipes, NoStats, NoSources, NoPanels }

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

    /// <summary>
    /// Stage 1's board, or a named starter for + Board (spec §9.2). With no name, Battle when a recipe with a
    /// period has ticked stats, else Grind. A named starter that can't be built has no panels.
    /// </summary>
    public static StarterBoard Build(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string? name = null)
    {
        if (installed.Count == 0) return new StarterBoard(name ?? Battle, BoardEmpty.NoRecipes, [], null, null);

        var ticked = installed.Where(i => !i.Recipe.IsGroupList && i.State.TrackedStats(i.Recipe).Count > 0).ToList();
        if (ticked.Count == 0)
        {
            var first = installed.FirstOrDefault(i => !i.Recipe.IsGroupList) ?? installed[0];
            return new StarterBoard(name ?? Battle, BoardEmpty.NoStats, [], null, first.Recipe.Slug);
        }

        var enabled = sources.Where(s => s.Enabled).ToList();
        var withPeriod = ticked.Where(i => i.Recipe.Period is not null).ToList();
        var withoutPeriod = ticked.Where(i => i.Recipe.Period is null).ToList();

        return name switch
        {
            Battle when withPeriod.Count == 0 => new StarterBoard(Battle, BoardEmpty.NoStats, [], null, ticked[0].Recipe.Slug),
            Battle => BattleBoard(installed, withPeriod, enabled),
            Grind when withoutPeriod.Count == 0 => new StarterBoard(Grind, BoardEmpty.NoStats, [], null, ticked[0].Recipe.Slug),
            Grind => GrindBoard(withoutPeriod, enabled),
            _ => withPeriod.Count > 0 ? BattleBoard(installed, withPeriod, enabled) : GrindBoard(ticked, enabled),
        };
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
