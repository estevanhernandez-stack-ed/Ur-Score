using System.Globalization;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

// The bare name Source would find the Labs626.UrScore.Source namespace from in here, so the type is aliased where the
// namespace's own members can't reach: after the namespace line, which is consulted first.
using Source = Labs626.UrScore.Core.Source;

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

    /// <summary>A value as its recipe says it reads (D10): a number with every digit, seconds as a duration, unix seconds as a date in your time zone.</summary>
    public static string Value(double? value, StatFormat format, TimeZoneInfo zone) => value is not { } v ? StatText.Dash : format switch
    {
        StatFormat.Duration => Duration(v),
        StatFormat.Date => Date(v, zone),
        _ => StatText.Number(v),
    };

    /// <summary>A change as its recipe says it reads: "+220K", "+2h 0m". A date has no change.</summary>
    public static string Change(double? value, StatFormat format) => value is not { } v ? StatText.Dash : format switch
    {
        StatFormat.Duration => (v < 0 ? "" : "+") + Duration(v),
        StatFormat.Date => StatText.Dash,
        _ => Signed(v),
    };

    /// <summary>Seconds as the two largest whole units: "586d 5h", "5h 12m", "12m".</summary>
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return StatText.Dash;

        var minutes = (long)Math.Floor(Math.Abs(seconds) / 60);
        var (days, hours, rest) = (minutes / 1440, minutes / 60 % 24, minutes % 60);
        var text = days > 0 ? $"{days.ToString("N0", CultureInfo.InvariantCulture)}d {hours}h"
            : hours > 0 ? $"{hours}h {rest}m"
            : $"{rest}m";
        return seconds < 0 ? "-" + text : text;
    }

    /// <summary>Unix seconds as "13 Sep 2020" in your time zone; a time outside years 1970 to 9999 is a dash.</summary>
    private static string Date(double unixSeconds, TimeZoneInfo zone) =>
        unixSeconds is > 0 and <= 253402300799
            ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(unixSeconds)), zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : StatText.Dash;

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
        PanelType.AccountsTable => "Accounts table",
        _ => "Live leaderboard",
    };

    /// <summary>Midnight today in this zone (D16): the boundary a "today" change measures from. Shared so a table and Profile stat agree.</summary>
    public static DateTimeOffset Midnight(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return new DateTimeOffset(local.Date, local.Offset);
    }

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

    /// <summary>A race whose recipe has no summed headline (backlog S1-13.6): none of its lines was removed, there is simply nothing to race.</summary>
    public const string NoTotalToRace = "This panel's recipe has no total to race.";

    /// <summary>A race drawn without some of its lines (S1-13.6): "One of this race's clans was removed.", "2 of this race's clans were removed."</summary>
    public static string RaceRemoved(int removed, string groups) =>
        removed == 1 ? $"One of this race's {groups} was removed." : $"{removed} of this race's {groups} were removed.";

    /// <summary>A race with more lines than it draws, which only a hand-edited boards.json can hold (S1-13.6).</summary>
    public static string RaceOverLimit(string groups) => $"Only the first {PanelModels.MaxRace} {groups} are drawn.";

    /// <summary>A race with fewer than two lines and none of them removed: what a race needs, not a removal it never had (S1-13.6).</summary>
    public static string RaceTooFew(string groups) => $"A race needs at least 2 {groups}.";

    /// <summary>A panel reading a source that is off: "CCGP is switched off, so it isn't read." Its dashes say why.</summary>
    public static string SwitchedOff(string name) => $"{name} is switched off, so it isn't read.";

    /// <summary>One of your accounts RoRoRo hasn't matched to a Roblox account, so no read can have placed it.</summary>
    public const string NotMatched = "Not matched by RoRoRo yet";

    /// <summary>
    /// Where one of your accounts is when no read of your own sources placed it (backlog S1-13.4, S1-12.7): the one decision
    /// My accounts and Setup › Your accounts share, so the two screens can't disagree.
    /// <para>
    /// "Not in" is a claim about the account, and it is made only once every one of the <paramref name="sources"/> has a
    /// reading from this session (<paramref name="readNow"/>). Short of that the line is about how much has been read, which
    /// is a fact about Ur Score: none of them, or not all of them. A remembered reading counts as in hand but never as read
    /// now, because the score book keeps only the accounts it kept, so an account missing from one proves nothing about the
    /// group; the same inheritance as a rank counted from your own rows alone (plan A40).
    /// </para>
    /// </summary>
    /// <param name="inNone">What is true once every source was read and the account is in none: "Not in a watched clan".</param>
    /// <param name="groups">The recipe's word for several groups, lower case: "clans".</param>
    /// <param name="inHand">Sources with a reading on hand, from this session or remembered.</param>
    /// <param name="readNow">Sources with a reading from this session.</param>
    /// <param name="sources">Every source the line is about.</param>
    public static string NotFound(string inNone, string groups, int inHand, int readNow, int sources) =>
        sources > 0 && readNow >= sources ? inNone
        : inHand == 0 ? $"No {groups} read yet"
        : $"Not found in the {groups} read so far";

    /// <summary>
    /// One of your accounts seen only in the rows of groups you watch (S1-13.4, S1-12.7). A watched source is never matched to
    /// your accounts for keeping or sending, so the account has no numbers here, but it is not "not in" anything.
    /// </summary>
    public static string OnlyWatched(string groups) => $"Only in {groups} you're watching";

    /// <summary>
    /// Why a source could not read one of your accounts at its last read, in the recipe's own words, or empty
    /// (plan A44, A46). One line per recipe that said so, main source first.
    /// <para>
    /// The sentence is the recipe's: <c>unavailable.message</c>, which is the only thing here that knows what it
    /// reads and what you must do about it. The only words Ur Score adds are the recipe's own name, and only when
    /// <paramref name="nameTheRecipe"/> — a screen that already shows one recipe at a time doesn't need telling.
    /// </para>
    /// </summary>
    public static string CannotRead(
        long userId, IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources,
        IReadOnlyDictionary<string, RecipeSnapshot> latest, bool nameTheRecipe)
    {
        if (userId == 0) return "";

        var lines = new List<string>();
        foreach (var source in sources.Where(s => s.Enabled).OrderBy(s => s.Role == SourceRole.Main ? 0 : 1))
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe) continue;
            if (latest.GetValueOrDefault(source.Id)?.Unavailable.GetValueOrDefault(userId) is not { Length: > 0 } why) continue;

            var line = nameTheRecipe ? $"{recipe.Name}: {why}" : why;
            if (!lines.Contains(line, StringComparer.Ordinal)) lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The recipe whose period a Top panel names: its list recipe's own, else the first installed recipe that has one.</summary>
    internal static Recipe? TopPeriodRecipe(Recipe? list, IReadOnlyList<InstalledRecipe> installed) =>
        list?.Period is not null ? list : installed.FirstOrDefault(i => i.Recipe.Period is not null && !i.Recipe.IsGroupList)?.Recipe;

    /// <summary>The recipe that names groups where no recipe is picked (Top's name column, a blank form): the first non-list recipe with inputs.</summary>
    internal static Recipe? GroupRecipe(IReadOnlyList<InstalledRecipe> installed) =>
        installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Inputs.Count > 0)?.Recipe;
}
