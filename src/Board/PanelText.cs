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

    /// <summary>
    /// Whose picture an icon is, for a screen reader: "CCGP clan icon". The source's own name and the recipe's word for what it
    /// reads, so it never names a game.
    /// </summary>
    public static string IconName(string sourceName, Recipe recipe) => $"{sourceName} {RecipeWords.Group(recipe)} icon";

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
        PanelType.Pace => recipe is null ? "Pace" : $"{RecipeWords.Capital(RecipeWords.Group(recipe))} pace",
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

    /// <summary>
    /// "AutumnBattle · next read in 2m" (spec §8's top bar line). The period's end is not here: it ticks beside this
    /// line, from the same <see cref="ReadingPeriod.Ends"/>, so a reader is never told two things about one end.
    /// </summary>
    public static string PeriodLine(ReadingPeriod? period, DateTimeOffset now, DateTimeOffset? nextRead)
    {
        var parts = new List<string>();
        if (period is not null) parts.Add(period.Value);
        if (nextRead is { } next) parts.Add(NextRead(next, now));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// How long a period has left, to the second, and when it ends in the viewer's own zone:
    /// "ends in 5d 22:00:00 · Fri 25 Sep 11:00", or "ended" once it has. Asked for on battle day, where "ends in 3d"
    /// covers anything from 60 to 84 hours and the last hour is the one that matters. The end second itself still
    /// counts as time left; from the end onwards it is over.
    /// </summary>
    public static string Countdown(DateTimeOffset ends, DateTimeOffset now, TimeZoneInfo zone, bool withClock = true)
    {
        if (ends <= now) return "ended";

        var left = ends - now;
        var exact = left.Days > 0
            ? $"{left.Days}d {left.Hours}:{left.Minutes:00}:{left.Seconds:00}"
            : $"{left.Hours}:{left.Minutes:00}:{left.Seconds:00}";
        if (!withClock) return $"ends in {exact}";

        var clock = TimeZoneInfo.ConvertTime(ends, zone).ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
        return $"ends in {exact} · {clock}";
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

    /// <summary>
    /// Why a clans list has drawn no board. The recipe has to say its groups are clans before a name is kept
    /// (V3-S.25), and a chart that is simply empty gives nobody a way to find that out — least of all mid-battle.
    /// </summary>
    /// <param name="groups">
    /// What the rows ARE, lower case, from the PANEL's recipe rather than the list's. A group list has no inputs of
    /// its own by definition, so <see cref="RecipeWords.Groups"/> has nothing to read and falls through to its
    /// "Sources" default: the note read "No sources are named here" on a real board until a screenshot caught it
    /// on 2026-09-21. The panel's own recipe carries the input that names them ("plural": "Clans").
    /// </param>
    public static string GroupNamesNotKept(string groups, Recipe list) =>
        $"No {groups} are named here: {list.Name} does not say its groups are clans, so no names are kept.";

    /// <summary>
    /// A live-only panel (Live leaderboard, Top, Promotion check) whose source was read this session and brought nothing back: no
    /// battle, or a read that failed (backlog S1-F.6). The score book never keeps other members, so there is nothing true to draw
    /// in its place, and the panel says why it is empty instead of "waiting" for a read that already happened. The state line
    /// names a cause that needs you.
    /// </summary>
    public static string NothingBack(string? name = null) =>
        name is null ? "The last read brought nothing back." : $"The last read of {name} brought nothing back.";

    /// <summary>A panel reading a source that is off: "CCGP is switched off, so it isn't read." Its dashes say why.</summary>
    public static string SwitchedOff(string name) => $"{name} is switched off, so it isn't read.";

    /// <summary>One of your accounts RoRoRo hasn't matched to a Roblox account, so no read can have placed it.</summary>
    public const string NotMatched = "Not matched by RoRoRo yet";

    /// <summary>
    /// A row whose stat the last read did not bring back (S1-13.7). True, and in words a reader can use; the path the recipe
    /// looked in and the keys that came back instead are listed in Setup › Diagnostics, which is where they help.
    /// </summary>
    public const string NotInLastRead = "Not in the last read.";

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
    /// <para>
    /// A source read between periods WAS read, and its reading has no rows, because there is no member list to give (<see
    /// cref="ReadIdle"/>). With no rows on hand anywhere, that is what the line says: no group is in a period right now once
    /// every source was read so, and of the ones read so far while some are not. Counting it as unread said "No clans read
    /// yet" of clans that were read, which is the board's usual state, since a main clan sits between battles most of the time.
    /// </para>
    /// </summary>
    /// <param name="inNone">What is true once every source was read and the account is in none: "Not in a watched clan".</param>
    /// <param name="group">The recipe's word for one group, lower case: "clan".</param>
    /// <param name="groups">The recipe's word for several groups, lower case: "clans".</param>
    /// <param name="period">The recipe's word for its period, lower case: "battle".</param>
    /// <param name="inHand">Sources with a reading with rows on hand, from this session or remembered.</param>
    /// <param name="readNow">Sources with a reading with rows from this session.</param>
    /// <param name="idleNow">Sources whose reading from this session found them between periods.</param>
    /// <param name="sources">Every source the line is about.</param>
    public static string NotFound(
        string inNone, string group, string groups, string period, int inHand, int readNow, int idleNow, int sources) =>
        sources > 0 && readNow >= sources ? inNone
        : inHand > 0 ? $"Not found in the {groups} read so far"
        : idleNow == 0 ? $"No {groups} read yet"
        : idleNow >= sources ? $"No {group} is in {WithA(period)} right now"
        : $"No {group} read so far is in {WithA(period)}";

    /// <summary>
    /// Whether a reading from this session found its source between periods: read, with no rows, since there was no member
    /// list to read. Pass the session's own reading (<c>LiveOf</c>, Setup's latest), never a remembered one, which is no read.
    /// </summary>
    public static bool ReadIdle(RecipeSnapshot? live) => live is { State: WatchState.SourceIdle, Rows: null };

    /// <summary>"a battle", "an event": a recipe's period word is its own, so the article can't be written in.</summary>
    private static string WithA(string noun) =>
        (noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? "an " : "a ") + noun;

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
