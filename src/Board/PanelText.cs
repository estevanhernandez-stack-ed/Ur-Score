using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

/// <summary>How panels write numbers, places and times (spec §9.6: change states its span; a missing value is a dash).</summary>
public static class PanelText
{
    public const string StaleStat = "This panel's stat was removed.";

    public static string Ordinal(int place)
    {
        var tens = place % 100;
        var suffix = tens is >= 11 and <= 13 ? "th" : (place % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return place.ToString("N0", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>Every digit: "14,020,550". A missing value is a dash, never 0.</summary>
    public static string Full(double? value) => value is { } v ? StatText.Number(v) : StatText.Dash;

    /// <summary>Abbreviated: "12.4M".</summary>
    public static string Short(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : StatText.Abbrev(v);

    /// <summary>A change with its sign: "+220K", "-1.5K".</summary>
    public static string Signed(double? value) =>
        value is not { } v ? StatText.Dash : v < 0 ? "-" + StatText.Abbrev(-v) : "+" + StatText.Abbrev(v);

    public static string Chip(SourceRole role) => role switch
    {
        SourceRole.Main => "★ main",
        SourceRole.Mine => "yours",
        _ => "watching",
    };

    /// <summary>A source by its role: "★ CCGP", "K0i2", "NovaForge · watching".</summary>
    public static string SourceLabel(string name, SourceRole role) => role switch
    {
        SourceRole.Main => $"★ {name}",
        SourceRole.Watch => $"{name} · watching",
        _ => name,
    };

    /// <summary>
    /// A panel's title in its recipe's words: "Clan standing", "Battle race", "Past battles". Top names its
    /// list recipe's period, else the first installed recipe's that has one: "Top of the battle".
    /// </summary>
    public static string Title(PanelType type, Recipe? recipe, IReadOnlyList<InstalledRecipe> installed) => type switch
    {
        PanelType.Standing => recipe is null ? "Standing" : $"{RecipeWords.Capital(RecipeWords.Group(recipe))} standing",
        PanelType.Race => recipe is null ? "Race" : $"{RecipeWords.Capital(RecipeWords.Period(recipe))} race",
        PanelType.MyAccounts => "My accounts",
        PanelType.PromotionCheck => "Promotion check",
        PanelType.AccountCard => "Account card",
        PanelType.PastPeriods => recipe is null ? "Past periods" : $"Past {RecipeWords.Periods(recipe)}",
        PanelType.Records => "Records",
        PanelType.Top => $"Top of the {(TopPeriodRecipe(recipe, installed) is { } periodRecipe ? RecipeWords.Period(periodRecipe) : "list")}",
        PanelType.ProfileStat => "Profile stat",
        _ => "Live leaderboard",
    };

    public static string Ago(DateTimeOffset? then, DateTimeOffset now) =>
        then is { } at ? $"{StatText.Span(now - at)} ago" : "never";

    public static string NextRead(DateTimeOffset due, DateTimeOffset now) =>
        due <= now ? "next read due" : $"next read in {StatText.Span(due - now)}";

    /// <summary>"AutumnBattle · ends in 3d · next read in 2m" (spec §8's top bar line).</summary>
    public static string PeriodLine(ReadingPeriod? period, DateTimeOffset now, DateTimeOffset? nextRead)
    {
        var parts = new List<string>();
        if (period is not null)
        {
            parts.Add(period.Value);
            if (period.Ends is { } ends) parts.Add(ends > now ? $"ends in {StatText.Span(ends - now)}" : "ended");
        }

        if (nextRead is { } next) parts.Add(NextRead(next, now));
        return string.Join(" · ", parts);
    }

    public static string StaleSource(string group) => $"This panel's {group} was removed.";

    /// <summary>The recipe whose period a Top panel names: its list recipe's own, else the first installed recipe that has one.</summary>
    internal static Recipe? TopPeriodRecipe(Recipe? list, IReadOnlyList<InstalledRecipe> installed) =>
        list?.Period is not null ? list : installed.FirstOrDefault(i => i.Recipe.Period is not null && !i.Recipe.IsGroupList)?.Recipe;

    /// <summary>The recipe that names groups where no recipe is picked (Top's name column, a blank form): the first non-list recipe with inputs.</summary>
    internal static Recipe? GroupRecipe(IReadOnlyList<InstalledRecipe> installed) =>
        installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Inputs.Count > 0)?.Recipe;
}
