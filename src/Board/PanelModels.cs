using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

public enum PanelType { Standing, Race, MyAccounts, PromotionCheck, AccountCard, PastPeriods, Records, Top, ProfileStat, LiveLeaderboard, AccountsTable, Pace }

/// <summary>What a panel shows. Stage 1 fills these from the starter board; stage 2 saves them in boards.json.</summary>
public sealed record PanelSettings(
    string Recipe = "",
    string? SourceId = null,
    IReadOnlyList<string>? SourceIds = null,
    string? ToSourceId = null,
    string? Stat = null,
    long? UserId = null);

/// <summary>
/// Every panel's header: title, subtitle, role chip, overdue mark, and the stale message that replaces the body.
/// The chip is its source's role: <see cref="Chip"/> is the role's words and the panel colours the chip by the role,
/// so words and colour can never disagree.
/// </summary>
public sealed record PanelHead(
    string Title, string Subtitle = "", SourceRole? ChipRole = null, bool Overdue = false, string? Stale = null, string Note = "",
    bool Remembered = false)
{
    public string Chip => ChipRole is { } role ? PanelText.Chip(role) : "";

    public bool HasBody => Stale is null;

    public bool HasStale => Stale is not null;

    public bool HasChip => ChipRole is not null;

    public bool HasSubtitle => Subtitle.Length > 0;

    public bool HasNote => Note.Length > 0 && Stale is null;
}

/// <summary>Everything live a panel may use. Other players' rows live here in memory only.</summary>
public sealed record LiveBoard(
    IReadOnlyList<Source> Sources,
    IReadOnlyList<InstalledRecipe> Installed,
    IReadOnlyDictionary<string, RecipeSnapshot> Snapshots,
    IReadOnlyDictionary<string, DateTimeOffset> LastRead,
    IReadOnlyList<HostAccount> Accounts,
    TimeProvider Time,
    bool Running,
    IReadOnlyDictionary<long, string>? Avatars = null,
    IReadOnlyDictionary<string, RecipeSnapshot>? Remembered = null,
    IReadOnlyDictionary<string, string>? Icons = null)
{
    public DateTimeOffset Now => Time.GetUtcNow();

    public IReadOnlySet<long> MyUserIds => UserIdsOf(Accounts);

    /// <summary>Your own Roblox user ids from a list of your accounts; an account with no id (0) has none.</summary>
    public static IReadOnlySet<long> UserIdsOf(IReadOnlyList<HostAccount> accounts) =>
        accounts.Where(a => a.RobloxUserId != 0).Select(a => a.RobloxUserId).ToHashSet();

    public Source? FindSource(string? id) =>
        id is null ? null : Sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    public InstalledRecipe? FindRecipe(string? slug) =>
        slug is null ? null : Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// What a panel draws for a source: the reading from this session, else the last one the score book kept (plan
    /// A38). A remembered one carries <see cref="RecipeSnapshot.RememberedAt"/>; a panel that needs another member's
    /// row takes <see cref="LiveOf"/> instead (plan A40).
    /// <para>
    /// A read that FAILED has replaced nothing, so it does not take the remembered numbers with it (review round 2).
    /// A source that cannot be reached is exactly when the last numbers are worth most, and going blank there would
    /// leave the board worse off than before it remembered anything. The failed reading is still what
    /// <see cref="LiveOf"/> answers with, so the state line goes on naming the fault while the numbers beside it stay
    /// honestly marked as remembered. Only a reading that came back clears them.
    /// </para>
    /// </summary>
    public RecipeSnapshot? SnapshotOf(string sourceId)
    {
        var live = Snapshots.GetValueOrDefault(sourceId);
        return live is not null && BroughtNumbers(live) ? live : Remembered?.GetValueOrDefault(sourceId) ?? live;
    }

    /// <summary>The reading from this session alone. What Ur Score is DOING is only ever answered from this one.</summary>
    public RecipeSnapshot? LiveOf(string sourceId) => Snapshots.GetValueOrDefault(sourceId);

    /// <summary>
    /// The role a panel's chip wears for a source. A clan added under "your accounts are in" is called yours only while the
    /// read in hand doesn't contradict it: when this session's reading has members and none of them is one of your
    /// accounts, it is a clan you are watching, and the chip says so rather than "yours" above "Your accounts 0 of 57". With
    /// no members read (not yet read, or between battles) there is no evidence either way, so it keeps what you chose.
    /// Only a live reading can prove "none of yours": a remembered one holds your own accounts alone (plan A40). Deciding
    /// membership from the clan's roster, which is there between battles too, is the larger change in the backlog.
    /// </summary>
    public SourceRole ChipRole(Source source) =>
        source.Role == SourceRole.Mine
        && LiveOf(source.Id)?.Rows is { Count: > 0 } rows
        && !rows.Any(r => MyUserIds.Contains(r.UserId))
            ? SourceRole.Watch
            : source.Role;

    /// <summary>
    /// Whether a reading came back with numbers at all. A read that failed carries its state and its reason and
    /// nothing else — <see cref="RecipeSnapshot.Rows"/> and <see cref="RecipeSnapshot.Headline"/> are both null,
    /// because no reading was ever attached to it — so it replaces nothing a panel is drawing.
    /// </summary>
    private static bool BroughtNumbers(RecipeSnapshot snapshot) =>
        snapshot.Rows is not null || snapshot.Headline is not null || snapshot.Groups.Count > 0;

    public bool IsRemembered(string sourceId) => SnapshotOf(sourceId)?.RememberedAt is not null;

    /// <summary>
    /// The oldest reading behind anything on screen, so a line about them never claims they are fresher than the
    /// oldest one a panel is showing. Null once every enabled source has been read this session.
    /// </summary>
    public DateTimeOffset? OldestRemembered =>
        Sources.Where(s => s.Enabled).Select(s => SnapshotOf(s.Id)?.RememberedAt).Min();

    /// <summary>The source's main input value ("CCGP"), else its recipe's name.</summary>
    public string SourceName(Source source) => NameOf(source, FindRecipe(source.Recipe)?.Recipe);

    /// <summary><see cref="SourceName"/> for a caller that already has the source's recipe, or knows it has none.</summary>
    public static string NameOf(Source source, Recipe? recipe)
    {
        if (recipe is not null && RecipeWords.MainInput(recipe) is { } input
            && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0)
        {
            return value.Trim();
        }

        return recipe?.Name ?? source.Recipe;
    }

    public string AccountName(long userId) =>
        Accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName ?? "One of your accounts";

    /// <summary>
    /// The cached picture for one of YOUR accounts, or null. An id that isn't yours has none, whatever the map holds:
    /// the leaderboard and Top show other members by name only, and this is the second of the two checks (plan A22).
    /// </summary>
    public string? AvatarFor(long userId) =>
        userId != 0 && MyUserIds.Contains(userId) ? Avatars?.GetValueOrDefault(userId) : null;

    /// <summary>
    /// The cached picture of <paramref name="source"/> itself, or null (backlog V3-S.7): kept per source, so it is that clan's
    /// and never another's. A recipe that no longer names an icon has none, whatever the map still holds.
    /// </summary>
    public string? IconFor(Source source) =>
        FindRecipe(source.Recipe)?.Recipe.Icon is null ? null : Icons?.GetValueOrDefault(source.Id);

    /// <summary>Spec §9.6: only while reading runs, and only once the source has been read.</summary>
    public bool IsOverdue(Source source) =>
        Running
        && LastRead.TryGetValue(source.Id, out var last)
        && FindRecipe(source.Recipe) is { } installed
        && Records.Overdue(last, installed.Recipe.EffectiveEverySeconds, Now);
}

public sealed record StandingModel(
    PanelHead Head, string Place, string PlaceSuffix, string TotalLabel, string Total, string Change,
    bool HasGap, string GapLabel, string Gap, double GapFill, bool HasAccounts, string Accounts, string PeriodLine)
{
    /// <summary>Which way <see cref="Change"/> went, so the panel paints a fall as one (backlog S1-13.14).</summary>
    public ChangeDirection ChangeDirection { get; init; }

    /// <summary>
    /// When this panel's period ends, for the clock that ticks beside <see cref="PeriodLine"/> between reads, or null
    /// with no period or no end. The panel keeps the instant, never a countdown worked out at read time: a number
    /// drawn three minutes ago would be three minutes wrong.
    /// </summary>
    public DateTimeOffset? Ends { get; init; }

    /// <summary>The picture of the clan this panel is about, or null while there is none: never the window's, never another clan's.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// This panel's recipe names an icon, so a picture belongs here: its space is kept while it isn't there yet or doesn't
    /// decode, and nothing moves when it lands (A26). Without one there is no slot at all.
    /// </summary>
    public bool HasIconSlot { get; init; }

    /// <summary>Whose picture it is, for a screen reader: "CCGP clan icon".</summary>
    public string IconName { get; init; } = "";
}

public sealed record LegendItem(string Text, int Colour);

/// <summary>
/// <paramref name="FromZero"/> keeps the axis anchored at zero for a race of your own clans, where the growth from
/// nothing is the story. With the whole board drawn it is false: anchoring twenty clans at zero squeezes the pack
/// into a band, and which of them you are gaining on is the story then.
/// </summary>
/// <summary>One clan on the standings list under the race chart: where it is, and how far from you.</summary>
public sealed record RaceStanding(string Place, string Name, string Points, string Gap, bool Yours);

public sealed record RaceModel(
    PanelHead Head, IReadOnlyList<ChartSeries> Series, IReadOnlyList<LegendItem> Legend, string ChartName, bool FromZero = true)
{
    /// <summary>The board as a list, names and all, for the card's own standings. Empty without a clans list.</summary>
    public IReadOnlyList<RaceStanding> Standings { get; init; } = [];

    /// <summary>Whether there is a board to offer: no clans list, no button.</summary>
    public bool HasStandings => Standings.Count > 0;
}

public sealed record AccountLineModel(
    long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing,
    string? Avatar = null, string Note = "")
{
    /// <summary>Why this row has no numbers, in the recipe's own words (plan A44). Empty for a row that was read.</summary>
    public bool HasNote => Note.Length > 0;
}

public sealed record AccountGroupModel(string Heading, IReadOnlyList<AccountLineModel> Rows);

public sealed record MyAccountsModel(PanelHead Head, string ValueColumn, string GroupColumn, IReadOnlyList<AccountGroupModel> Groups);

public sealed record PromotionRow(string Name, string Value, string WouldPlace, bool Fits, bool Missing, string? Avatar = null);

public sealed record PromotionModel(PanelHead Head, string LowestLabel, string Lowest, string ValueColumn, IReadOnlyList<PromotionRow> Rows);

public sealed record FactModel(string Label, string Value);

/// <summary>One heading of an account card and its facts; the heading is the recipe's section name, empty for stats with none.</summary>
public sealed record CardSection(string Heading, IReadOnlyList<FactModel> Facts)
{
    public bool HasHeading => Heading.Length > 0;
}

public sealed record AccountCardModel(
    PanelHead Head, string BigLabel, string Big, IReadOnlyList<CardSection> Sections, IReadOnlyList<ChartSeries> Line,
    IReadOnlyList<FactModel> Facts, string ChartName, string? Avatar = null)
{
    /// <summary>A line to draw: with none yet the card gives the chart no space.</summary>
    public bool HasLine => Line.Count > 0;

    public bool HasSections => Sections.Count > 0;
}

public sealed record PastRow(string Period, string Place, string Total, string YourBest);

public sealed record PastPeriodsModel(PanelHead Head, string PeriodColumn, IReadOnlyList<PastRow> Rows);

public sealed record RecordsModel(PanelHead Head, IReadOnlyList<FactModel> Facts);

public sealed record TopRow(string Rank, string Name, string Value, bool Yours, bool Estimate);

public sealed record TopModel(PanelHead Head, string NameColumn, string ValueColumn, IReadOnlyList<TopRow> Rows);

public sealed record ProfileRow(string Name, string Value, string Today, string Week, string Note, bool Missing)
{
    public bool HasNote => Note.Length > 0;
}

public sealed record ProfileStatModel(PanelHead Head, string ValueColumn, IReadOnlyList<ProfileRow> Rows);

public sealed record LeaderRow(string Position, string Name, IReadOnlyList<string> Cells, bool Yours);

public sealed record LeaderboardModel(PanelHead Head, IReadOnlyList<string> Columns, IReadOnlyList<LeaderRow> Rows);

/// <summary>
/// The ten panels' view models (spec §9.4), built from live snapshots and the score book. Pure: no WPF, no
/// disk, no clock but <see cref="LiveBoard.Time"/>. Live-only panels take no reader, so they cannot reach
/// the book.
/// </summary>
public static class PanelModels
{
    public const int MaxRace = 5;

    public const int TopCount = 10;

    /// <summary>What My accounts' dot means, said where the dot is (backlog S1-F.5).</summary>
    public const string SentLegend = "● sent to RoRoRo in the last read";

    private const string Dash = StatText.Dash;

    public static StandingModel Standing(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = PanelText.Title(PanelType.Standing, recipe, live.Installed);

        if (recipe is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new StandingModel(StaleSource(live, settings, title), Dash, "", "", Dash, "", false, "", "", 0, false, "", "");
        }

        var snapshot = live.SnapshotOf(source.Id);
        var name = live.SourceName(source);
        var totalId = TotalId(recipe);
        var place = HeadlineNumber(snapshot, PlaceId(recipe));
        var total = HeadlineNumber(snapshot, totalId);
        // Before the source's own period is known, "no period" reads the book as every period kept, not this one.
        var periodKnown = recipe.Period is null || snapshot?.Period is not null;
        // One series behind both the words and the colour, so the two can never tell different stories.
        IReadOnlyList<SeriesPoint>? totals = totalId is null || !periodKnown ? null : reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value);
        var change = totals is null ? Dash : Records.Change(totals, live.Now);
        var gap = Gap(live, name);

        var rows = snapshot?.Rows;
        // Your own rows are all a remembered snapshot has, so "4 of 4" would be a clan this never read (plan A40).
        var hasAccounts = source.Role != SourceRole.Watch && rows is not null && snapshot?.RememberedAt is null;
        var mine = rows?.Count(r => live.MyUserIds.Contains(r.UserId)) ?? 0;

        return new StandingModel(
            new PanelHead(title, name, live.ChipRole(source), live.IsOverdue(source), Remembered: live.IsRemembered(source.Id)),
            place is { } p ? PanelText.Ordinal((int)p) : Dash,
            recipe.Period is null || place is null ? "" : $"in the {RecipeWords.Period(recipe)}",
            recipe.Headline.FirstOrDefault(h => h.Id == totalId)?.Label ?? "Total",
            PanelText.Full(total),
            change,
            gap.Has, gap.Label, gap.Text, gap.Fill,
            hasAccounts,
            hasAccounts ? $"{mine} of {rows!.Count}" : "",
            PanelText.PeriodLine(snapshot?.Period, live.Now, null))
        {
            Ends = snapshot?.Period?.Ends,
            ChangeDirection = totals is null ? ChangeDirection.None : Records.Direction(totals),
            Icon = live.IconFor(source),
            HasIconSlot = recipe.Icon is not null,
            IconName = PanelText.IconName(name, recipe),
        };
    }

    public static RaceModel Race(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = PanelText.Title(PanelType.Race, recipe, live.Installed);
        if (recipe is null) return new RaceModel(StaleSource(live, settings, title), [], [], "");

        // Each problem says what it is (backlog S1-13.6). A recipe with no total read "This panel's clan was removed.", a line
        // that really was removed dropped off the chart without a word, and a race of the wrong size said nothing at all.
        if (TotalId(recipe) is not { } totalId) return new RaceModel(new PanelHead(title, Stale: PanelText.NoTotalToRace), [], [], "");

        var groupsWord = RecipeWords.GroupsLower(recipe);
        IReadOnlyList<string> ids = settings.SourceIds ?? [];
        var found = ids.Select(live.FindSource).OfType<Source>().ToList();
        if (found.Count == 0)
        {
            var nothing = ids.Count == 0 ? new PanelHead(title, Stale: PanelText.RaceTooFew(groupsWord)) : StaleSource(live, settings, title);
            return new RaceModel(nothing, [], [], "");
        }

        var sources = found.Take(MaxRace).ToList();
        var notes = new List<string>();
        if (found.Count < ids.Count) notes.Add(PanelText.RaceRemoved(ids.Count - found.Count, groupsWord));
        else if (found.Count < 2) notes.Add(PanelText.RaceTooFew(groupsWord));
        if (found.Count > MaxRace) notes.Add(PanelText.RaceOverLimit(groupsWord));

        var series = new List<ChartSeries>();
        var legend = new List<LegendItem>();
        var overdue = false;
        var remembered = false;
        var anyPeriodKnown = recipe.Period is null;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var snapshot = live.SnapshotOf(source.Id);
            // Before this source's own period is known, "no period" reads the book as every period kept.
            var periodKnown = recipe.Period is null || snapshot?.Period is not null;
            anyPeriodKnown |= periodKnown;

            var points = periodKnown
                ? reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value).Select(p => new ChartPoint(p.T, p.Value)).ToList()
                : new List<ChartPoint>();

            // The live read, until the book has a line for it — and from LIVE alone (review round 3). This point is
            // plotted at live.LastRead, which stamps every ATTEMPT, a read that brought nothing back included. Taking
            // its value from SnapshotOf would therefore draw an hours-old remembered total flat out to the current
            // minute, and on a chart a line to "now" IS the claim that it was read now. A reading that failed carries
            // no headline, so it plots nothing; a remembered one needs no help, since the reading behind it is already
            // on this chart at its own time, out of the book.
            if (HeadlineNumber(live.LiveOf(source.Id), totalId) is { } now && live.LastRead.TryGetValue(source.Id, out var at)
                && (points.Count == 0 || points[^1].T < at.AddSeconds(-30)))
            {
                points.Add(new ChartPoint(at, now));
            }

            var label = PanelText.SourceLabel(live.SourceName(source), source.Role);

            series.Add(new ChartSeries(label, points, i));
            legend.Add(new LegendItem($"{label} {(points.Count > 0 ? StatText.Abbrev(points[^1].Value) : Dash)}", i));
            overdue |= live.IsOverdue(source);
            remembered |= live.IsRemembered(source.Id);
        }

        // The clans you are actually racing: three above and three below, each its own line with its own name.
        // Twenty clans in one grey was unreadable ("I can barely see the other clan's lines", 2026-09-20), and the
        // ones that decide your place are the neighbours, not the leader.
        var board = BoardLines(live, reader, sources, series.Count);
        foreach (var (line, points) in board)
        {
            series.Add(line);
            legend.Add(new LegendItem($"{line.Label} {StatText.Abbrev(points)}", line.Colour));
        }

        // Every line over the same window. Your own clan is recorded from the battle's first minute, a rival only
        // from the first clans-list read that kept it — so untrimmed, yours spans the chart and the whole band is a
        // stub in its last tenth: "I can barely see the other clan's lines ... they're over to the right"
        // (2026-09-20). Trimmed to where the band begins, all of them use the full width and can be compared.
        var from = RaceWindow(series, board.Count);
        if (from is { } start)
        {
            for (var i = 0; i < series.Count; i++)
            {
                series[i] = series[i] with { Points = [.. series[i].Points.Where(p => p.T >= start)] };
            }
        }

        // An empty board has two very different causes, and only one of them is worth a sentence: nothing read
        // yet is ordinary, a recipe that never keeps names is a thing you have to be told (V3-S.25).
        if (FieldOf(live) is { } fieldSource
            && live.FindRecipe(fieldSource.Recipe)?.Recipe is { IsGroupList: true, GroupsAreClans: false } list)
        {
            notes.Add(PanelText.GroupNamesNotKept(groupsWord, list));
        }

        var totalLabel = recipe.Headline.First(h => h.Id == totalId).Label;
        var span = from is null
            ? $"since the {RecipeWords.Period(recipe)} started"
            : "since the clans list was first read";
        var head = new PanelHead(title, $"{RecipeWords.Lower(totalLabel)} {span}",
            Overdue: overdue, Note: string.Join(" ", notes), Remembered: remembered);

        // No source's period is known yet: every point in "series" would be mixing periods together.
        if (!anyPeriodKnown)
        {
            return new RaceModel(head with { Note = string.Join(" ", notes.Prepend("Waiting for the first read.")) }, [], [], "");
        }

        return new RaceModel(head, series, legend, $"{title}: {string.Join(", ", legend.Select(l => l.Text))}", board.Count == 0)
        {
            Standings = Standings(live, reader),
        };
    }

    /// <summary>
    /// Where the race chart starts, or null to draw everything there is.
    /// <para>
    /// The band's earliest reading: the whole band is kept, and only your own line — which starts far earlier —
    /// gives anything up. Null when there is no band to line up with, and null when the trim would leave one of
    /// your own lines with fewer than two points, because a squeezed line beats a missing one.
    /// </para>
    /// </summary>
    private static DateTimeOffset? RaceWindow(IReadOnlyList<ChartSeries> series, int bandCount)
    {
        if (bandCount == 0 || series.Count <= bandCount) return null;

        var mine = series.Count - bandCount;
        DateTimeOffset? start = null;
        for (var i = mine; i < series.Count; i++)
        {
            if (series[i].Points.Count == 0) continue;
            var first = series[i].Points[0].T;
            if (start is null || first < start) start = first;
        }

        if (start is not { } at) return null;

        // Only a line that HAS something to lose stops the trim. Seen on the owner's board 2026-09-20: CCGP is
        // watched and not in this battle, so it had no points at all, and a blanket "every line keeps two points"
        // refused the trim for a line that was already empty -- leaving the whole band crammed at the right.
        for (var i = 0; i < mine; i++)
        {
            var had = series[i].Points.Count;
            if (had >= 2 && series[i].Points.Count(p => p.T >= at) < 2) return null;
        }

        return at;
    }

    /// <summary>How many clans either side of yours are drawn: the ones that decide whether you move a place.</summary>
    private const int Neighbours = 3;

    /// <summary>How much of the board the standings list offers, names and all.</summary>
    private const int StandingsShown = 25;

    /// <summary>
    /// The clans you are racing, from the rows a clans list kept (<see cref="GroupRows"/>): three above and three
    /// below the best placed of yours, each with its own colour, and a dash pattern once the palette repeats. Nothing
    /// at all without a switched-on list, or before it has been read twice — a line needs more than one point.
    /// </summary>
    private static List<(ChartSeries Line, double Points)> BoardLines(
        LiveBoard live, ScoreBookReader reader, IReadOnlyList<Source> drawn, int colourFrom)
    {
        var lines = new List<(ChartSeries, double)>();
        if (FieldOf(live) is not { } field) return lines;

        var period = live.SnapshotOf(field.Id)?.Period?.Value;
        var board = reader.GroupsLatest(field.Id, period);
        if (board.Count == 0) return lines;

        var alreadyDrawn = DrawnNames(drawn);
        var yours = SourceRules.MyClanNames(live.Sources, live.Installed).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var at = -1;
        for (var i = 0; i < board.Count; i++)
        {
            if (!yours.Contains(board[i].Name)) continue;
            at = i;
            break;
        }

        // None of yours on the board yet: the top of it is the next best thing to show.
        var from = at < 0 ? 0 : Math.Max(0, at - Neighbours);
        var to = at < 0 ? Math.Min(board.Count, Neighbours * 2 + 1) : Math.Min(board.Count, at + Neighbours + 1);

        var colour = colourFrom;
        for (var i = from; i < to; i++)
        {
            var (name, points) = board[i];
            if (alreadyDrawn.Contains(name)) continue;

            var series = reader.GroupSeries(field.Id, name, period).Select(p => new ChartPoint(p.T, p.Value)).ToList();
            if (series.Count < 2) continue;

            lines.Add((new ChartSeries(name, series, colour, colour / ChartPalette.Count), points));
            colour++;
        }

        return lines;
    }

    /// <summary>
    /// The board as a list: place, clan, points, and how far each is from the best placed of yours. Names live here
    /// rather than on the chart, where seven lines is already as much as can be told apart.
    /// </summary>
    private static IReadOnlyList<RaceStanding> Standings(LiveBoard live, ScoreBookReader reader)
    {
        if (FieldOf(live) is not { } field) return [];

        var board = reader.GroupsLatest(field.Id, live.SnapshotOf(field.Id)?.Period?.Value);
        if (board.Count == 0) return [];

        var mine = SourceRules.MyClanNames(live.Sources, live.Installed).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var yours = board.FirstOrDefault(g => mine.Contains(g.Name)).Value;

        return
        [
            .. board.Take(StandingsShown).Select((g, i) => new RaceStanding(
                PanelText.Ordinal(i + 1),
                g.Name,
                StatText.Abbrev(g.Value),
                yours <= 0 || mine.Contains(g.Name) ? "" : PanelText.Signed(g.Value - yours),
                mine.Contains(g.Name))),
        ];
    }

    /// <summary>The switched-on clans list, whose readings carry the board.</summary>
    private static Source? FieldOf(LiveBoard live) =>
        live.Sources.FirstOrDefault(s => s.Enabled && live.FindRecipe(s.Recipe) is { Recipe.IsGroupList: true });

    /// <summary>
    /// The clan names this panel already draws, yours and watched alike, so the board never draws one of them twice.
    /// This is not the same question as "which clans are mine" — it was one name for both until V3-S.31, which is
    /// how a watched rival came to anchor the band and bold itself in the standings.
    /// </summary>
    private static HashSet<string> DrawnNames(IReadOnlyList<Source> drawn) =>
        drawn
            .SelectMany(s => s.Inputs.Values)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static MyAccountsModel MyAccounts(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.MyAccounts, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new MyAccountsModel(StaleSource(live, settings, title), "", "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new MyAccountsModel(new PanelHead(title, Stale: PanelText.StaleStat), "", "", []);
        }

        var group = RecipeWords.Group(recipe);
        var zone = live.Time.LocalTimeZone;
        var assigned = new HashSet<long>();
        var groups = new List<AccountGroupModel>();
        var overdue = false;
        var remembered = false;

        foreach (var source in SourcesYoursIn(live, recipe))
        {
            overdue |= live.IsOverdue(source);
            remembered |= live.IsRemembered(source.Id);
            var snapshot = live.SnapshotOf(source.Id);
            if (snapshot?.Rows is not { } rows) continue;

            var mine = live.Accounts
                .Where(a => a.RobloxUserId != 0 && !assigned.Contains(a.RobloxUserId) && rows.Any(r => r.UserId == a.RobloxUserId))
                .ToList();
            if (mine.Count == 0) continue;

            // Counted here only for a reading of this session, which holds every row. A remembered one holds your own
            // accounts alone, so InGroup answers it from the book instead (review C1).
            var ranks = snapshot.RememberedAt is null ? Ranking.Competition(rows, stat.Key) : null;
            var period = snapshot.Period?.Value;
            var since = Since(recipe, live.Now);
            var series = mine.ToDictionary(a => a.RobloxUserId, a => reader.Series(source.Id, a.RobloxUserId, stat.Key, period, since));

            var lines = new List<(double? Value, AccountLineModel Line)>();
            foreach (var account in mine)
            {
                assigned.Add(account.RobloxUserId);
                var value = ValueOf(rows.First(r => r.UserId == account.RobloxUserId), stat.Key);
                var others = series.Where(kv => kv.Key != account.RobloxUserId).Select(kv => kv.Value);
                // Sent by this source's last read, of this stat. The session's remembered sends outlive the read that made
                // them, so reading those kept a dot lit through reads with RoRoRo closed or Send off (backlog S1-F.5).
                var sent = snapshot.SentThisRead.Contains((account.AccountId, stat.Key));

                lines.Add((value, new AccountLineModel(
                    account.RobloxUserId,
                    account.DisplayName,
                    PanelText.Value(value, stat.Format, zone),
                    InGroup(snapshot, ranks, account.RobloxUserId, stat.Key, value),
                    RecentChange(series[account.RobloxUserId], stat.Format),
                    sent,
                    Records.Stalled(series[account.RobloxUserId], others),
                    value is null,
                    live.AvatarFor(account.RobloxUserId))));
            }

            var heading = source.Role == SourceRole.Main ? $"★ {live.SourceName(source)}" : live.SourceName(source);
            groups.Add(new AccountGroupModel(heading, MissingLast(lines)));
        }

        // Every account left over sat under "Not in a watched clan": before the first read, when that was true of none of
        // them yet, and for one a watched clan held, when it was the opposite of true (backlog S1-13.4). Each is headed by
        // what is known of it instead, in the words Setup › Your accounts uses (PanelText.NotFound).
        var rest = live.Accounts.Where(a => a.RobloxUserId == 0 || !assigned.Contains(a.RobloxUserId)).ToList();
        if (rest.Count > 0)
        {
            var ofRecipe = live.Sources.Where(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal)).ToList();
            // A watched clan's rows are never remembered (A40), so this is what was actually read.
            var inWatched = ofRecipe
                .Where(s => s.Role == SourceRole.Watch)
                .SelectMany(s => live.LiveOf(s.Id)?.Rows ?? [])
                .Select(r => r.UserId)
                .ToHashSet();
            var groupsWord = RecipeWords.GroupsLower(recipe);
            // Idle is asked of this session's reading alone: a remembered one is no read, between periods or otherwise.
            var notFound = PanelText.NotFound(
                recipe.Inputs.Count > 0 ? $"Not in a watched {group}" : "Not in the last read", group, groupsWord, RecipeWords.Period(recipe),
                inHand: ofRecipe.Count(s => live.SnapshotOf(s.Id)?.Rows is not null),
                readNow: ofRecipe.Count(s => live.LiveOf(s.Id)?.Rows is not null),
                idleNow: ofRecipe.Count(s => PanelText.ReadIdle(live.LiveOf(s.Id))),
                sources: ofRecipe.Count);

            string Heading(HostAccount account) =>
                account.RobloxUserId == 0 ? PanelText.NotMatched
                : inWatched.Contains(account.RobloxUserId) ? PanelText.OnlyWatched(groupsWord)
                : notFound;

            // Watched first (they are somewhere), then the rest, then the ones nothing can place yet.
            foreach (var heading in new[] { PanelText.OnlyWatched(groupsWord), notFound, PanelText.NotMatched })
            {
                var under = rest.Where(a => Heading(a) == heading).ToList();
                if (under.Count > 0) groups.Add(Leftovers(live, heading, under));
            }
        }

        return new MyAccountsModel(
            new PanelHead(title, $"by {RecipeWords.Lower(stat.Label)}", Overdue: overdue, Note: SentLegend, Remembered: remembered),
            stat.Label, $"In {group}", groups);
    }

    /// <summary>Where each account in one source would place among another source's live rows (spec §9.4, §14). Live only.</summary>
    public static PromotionModel PromotionCheck(LiveBoard live, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.PromotionCheck, null, live.Installed);
        var installed = live.FindRecipe(settings.Recipe);
        var from = live.FindSource(settings.SourceId);
        var to = live.FindSource(settings.ToSourceId);

        if (installed is null || from is null || to is null) return new PromotionModel(StaleSource(live, settings, title), "", Dash, "", []);
        if (settings.Stat is null || RecipeStats.Find(installed.Recipe, settings.Stat) is not { } stat)
        {
            return new PromotionModel(new PanelHead(title, Stale: PanelText.StaleStat), "", Dash, "", []);
        }

        var fromName = live.SourceName(from);
        var toName = live.SourceName(to);
        var head = new PanelHead(title, $"{fromName} → {toName}", Overdue: live.IsOverdue(from) || live.IsOverdue(to),
            Note: $"Where each account would place if it were in {toName} now. Live only; other members' numbers are never saved.");
        var lowestLabel = $"{toName}'s lowest now";

        // Live only (plan A40): the book never kept another member's row, so a remembered snapshot cannot place anyone.
        var fromRows = live.LiveOf(from.Id)?.Rows;
        var toRows = live.LiveOf(to.Id)?.Rows;
        if (fromRows is null || toRows is null)
        {
            var (missing, missingName) = fromRows is null ? (from, fromName) : (to, toName);
            // A read that happened and brought nothing back is said as that, not as a wait for one (S1-F.6).
            var why = live.LiveOf(missing.Id) is null ? $"Waiting for a read of {missingName}." : PanelText.NothingBack(missingName);
            return new PromotionModel(head with { Note = why }, lowestLabel, Dash, stat.Label, []);
        }

        var toValues = new List<(long UserId, double Value)>();
        foreach (var row in toRows)
        {
            if (ValueOf(row, stat.Key) is { } v) toValues.Add((row.UserId, v));
        }

        double? lowest = toValues.Count == 0 ? null : toValues.Min(r => r.Value);
        var rows = new List<(double? Value, PromotionRow Row)>();

        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            if (fromRows.FirstOrDefault(r => r.UserId == account.RobloxUserId) is not { } row) continue;

            if (ValueOf(row, stat.Key) is not { } value)
            {
                rows.Add((null, new PromotionRow(account.DisplayName, Dash, Dash, false, true, live.AvatarFor(account.RobloxUserId))));
                continue;
            }

            var others = toValues.Where(r => r.UserId != account.RobloxUserId).Select(r => r.Value).ToList();
            var place = Records.WouldPlace(value, others);
            var below = lowest is { } low && value < low;
            var text = place is not { } p ? Dash : below ? "below the lowest" : $"{PanelText.Ordinal(p.Place)} of {p.Of}";
            var fits = place is { } q && !below && q.Place <= others.Count;
            rows.Add((value, new PromotionRow(account.DisplayName, StatText.Abbrev(value), text, fits, false, live.AvatarFor(account.RobloxUserId))));
        }

        return new PromotionModel(head, lowestLabel, PanelText.Full(lowest), stat.Label, MissingLast(rows));
    }

    public static AccountCardModel AccountCard(LiveBoard live, ScoreBookReader reader, PanelSettings settings, long? pickedUserId = null)
    {
        var title = PanelText.Title(PanelType.AccountCard, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return EmptyCard(StaleSource(live, settings, title));

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return EmptyCard(new PanelHead(title, Stale: PanelText.StaleStat));
        }

        // Where each of your accounts was read; the first source wins, main first.
        var found = new List<(HostAccount Account, Source Source, RecipeRow Row)>();
        foreach (var source in SourcesYoursIn(live, recipe))
        {
            if (live.SnapshotOf(source.Id)?.Rows is not { } rows) continue;
            foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0 && found.All(f => f.Account.RobloxUserId != a.RobloxUserId)))
            {
                if (rows.FirstOrDefault(r => r.UserId == account.RobloxUserId) is { } row) found.Add((account, source, row));
            }
        }

        // A card pinned to an account shows it; else the account picked in a table on its board while RoRoRo lists it (D18); else the top one.
        var wanted = settings.UserId ?? (pickedUserId is { } asked && live.MyUserIds.Contains(asked) ? asked : (long?)null);
        var picked = wanted is { } userId
            ? found.Where(f => f.Account.RobloxUserId == userId).ToList()
            : [.. found.OrderBy(f => ValueOf(f.Row, stat.Key) is null).ThenByDescending(f => ValueOf(f.Row, stat.Key) ?? 0)];

        if (picked.Count == 0)
        {
            // A card about one account talks about that account, pinned or picked; "your accounts" is only true of a card
            // that chose none (backlog S1-13.8). A pick is always one RoRoRo lists; a pin may not be.
            if (wanted is { } id)
            {
                if (!live.MyUserIds.Contains(id)) return EmptyCard(new PanelHead(title, Note: "RoRoRo isn't listing this panel's account right now."));

                var name = live.AccountName(id);
                var why = SourcesYoursIn(live, recipe)
                    .Select(s => live.SnapshotOf(s.Id)?.Unavailable.GetValueOrDefault(id))
                    .FirstOrDefault(message => message is not null);
                return EmptyCard(new PanelHead(title, name, Note: why ?? $"No reading of {name} yet."));
            }

            return EmptyCard(new PanelHead(title, Note: "No reading of your accounts yet."));
        }

        var (pickedAccount, pickedSource, pickedRow) = picked[0];
        var snapshot = live.SnapshotOf(pickedSource.Id)!;
        var series = reader.Series(pickedSource.Id, pickedAccount.RobloxUserId, stat.Key, snapshot.Period?.Value, Since(recipe, live.Now));
        var zone = live.Time.LocalTimeZone;

        var sections = Sections(recipe, installed.State.ShownStats(recipe).Where(s => s.Key != stat.Key), pickedRow, zone);

        var facts = new List<FactModel>();
        if (!recipe.LastStep.PerAccount && snapshot.Rows is { } listRows)
        {
            // Counted only for a reading of this session; a remembered one is answered from the book (review C1).
            var ranks = snapshot.RememberedAt is null ? Ranking.Competition(listRows, stat.Key) : null;
            facts.Add(new FactModel($"In {RecipeWords.Group(recipe)}",
                InGroup(snapshot, ranks, pickedAccount.RobloxUserId, stat.Key, ValueOf(pickedRow, stat.Key))));
        }

        var records = Records.For(reader, recipe.Slug, pickedSource.InputsKey, pickedSource.Id, pickedAccount.RobloxUserId, stat.Key, live.Time);
        if (recipe.Period is not null)
        {
            facts.Add(new FactModel($"Best {RecipeWords.Period(recipe)}",
                records.BestPeriodValue is { } best ? $"{ShortValue(best, stat.Format, zone)} · {records.BestPeriod}" : Dash));
            facts.Add(new FactModel("Best rank", records.BestRank is { } bestRank ? $"#{bestRank} · {records.BestRankPeriod}" : Dash));
            facts.Add(new FactModel($"{RecipeWords.Capital(RecipeWords.Periods(recipe))} played", records.PeriodsPlayed.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            facts.Add(new FactModel("Highest", records.Highest is { } highest ? ShortValue(highest, stat.Format, zone) : Dash));
            facts.Add(new FactModel("Biggest day", PanelText.Change(records.BiggestDay, stat.Format)));
        }

        // This account's own last reading first; then, for a remembered card, the reading behind it. Only a card drawing
        // a LIVE reading may fall back to live.LastRead, which stamps every attempt and so would answer "0m ago" beside
        // numbers that were read hours before (review round 3, the same inheritance as the chart point above).
        DateTimeOffset? lastRead = series.Count > 0 ? series[^1].T
            : snapshot.RememberedAt ?? (live.LastRead.TryGetValue(pickedSource.Id, out var at) ? at : null);
        facts.Add(new FactModel("Last read", PanelText.Ago(lastRead, live.Now)));

        if (snapshot.CellMisses.GetValueOrDefault((pickedAccount.RobloxUserId, stat.Key)) is { } miss) facts.Add(new FactModel("Note", miss));

        IReadOnlyList<ChartSeries> line = series.Count >= 2
            ? [new ChartSeries(stat.Label, [.. series.Select(p => new ChartPoint(p.T, p.Value))], 0)]
            : [];

        return new AccountCardModel(
            new PanelHead(title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}",
                Overdue: live.IsOverdue(pickedSource), Remembered: live.IsRemembered(pickedSource.Id)),
            stat.Label, PanelText.Value(ValueOf(pickedRow, stat.Key), stat.Format, zone), sections, line, facts,
            $"{pickedAccount.DisplayName}'s {stat.Label} over time",
            live.AvatarFor(pickedAccount.RobloxUserId));
    }

    public static PastPeriodsModel PastPeriods(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var title = PanelText.Title(PanelType.PastPeriods, installed?.Recipe, live.Installed);
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new PastPeriodsModel(StaleSource(live, settings, title), "", []);
        }

        var recipe = installed.Recipe;
        var placeId = PlaceId(recipe);
        var totalId = TotalId(recipe);

        // Newest first, said out loud rather than inherited from whatever order the reader happens to return.
        // Two signals, and only two. T is when the book learned a period, so it separates cycles: a period
        // written later finished later, because a record only ever grows at its end. It cannot separate a
        // backfill, where one cycle writes the whole record and every entry carries the same T. There, Kept
        // does: the source lists its finished periods oldest first, so the last one it handed over is the most
        // recent (verified 2026-09-16 against the live record for the user's own group and that source's
        // published schedule, which agree key for key). No final carries a date of its own — the record gives
        // none — so no row is placed as though we knew when it ran. The reader already returns one entry per
        // period, so there is nothing to regroup here (backlog S1-13.9).
        var rows = reader.Finals(recipe.Slug, source.InputsKey)
            .OrderByDescending(f => f.T)
            .ThenByDescending(f => f.Kept)
            .Select(f =>
            {
                string best = Dash;
                if (settings.Stat is { } stat)
                {
                    double? top = null;
                    long holder = 0;
                    foreach (var (userId, account) in f.Accounts)
                    {
                        if (!account.V.TryGetValue(stat, out var v) || (top is { } t && v <= t)) continue;
                        top = v;
                        holder = userId;
                    }

                    if (top is { } value) best = $"{StatText.Abbrev(value)} · {live.AccountName(holder)}";
                }

                return new PastRow(
                    f.Period,
                    placeId is not null && f.Headline.TryGetValue(placeId, out var place) ? PanelText.Ordinal((int)place) : Dash,
                    totalId is not null && f.Headline.TryGetValue(totalId, out var total) ? StatText.Abbrev(total) : Dash,
                    best);
            })
            .ToList();

        // V3-S.1: "No finished battles kept yet" read as "this clan has never been in one". The truth is
        // that Ur Score hasn't read them, and the next read fills them in, idle source or not.
        var note = rows.Count == 0
            ? $"Ur Score hasn't read this {RecipeWords.Group(recipe)}'s finished {RecipeWords.Periods(recipe)} yet. "
              + $"The next read fills them in from the {RecipeWords.Group(recipe)}'s own record."
            : $"Filled in from the {RecipeWords.Group(recipe)}'s own record.";

        return new PastPeriodsModel(
            new PanelHead(title, live.SourceName(source), live.ChipRole(source), Note: note),
            RecipeWords.Capital(RecipeWords.Period(recipe)), rows);
    }

    public static RecordsModel RecordsPanel(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.Records, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new RecordsModel(StaleSource(live, settings, title), []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new RecordsModel(new PanelHead(title, Stale: PanelText.StaleStat), []);
        }

        var zone = live.Time.LocalTimeZone;

        // Each account's records in each of your sources, never a merge of two sources' readings (backlog S1-9.3): every fact
        // below is the best one source holds, so an account read by two clans can't be credited with a rise between them.
        var all = (
            from account in live.Accounts
            where account.RobloxUserId != 0
            from source in SourcesYoursIn(live, recipe)
            select (Account: account, Found: Records.For(reader, recipe.Slug, source.InputsKey, source.Id, account.RobloxUserId, stat.Key, live.Time))
        ).ToList();

        string Highest(Func<AccountRecords, double?> pick, Func<HostAccount, AccountRecords, double, string> text)
        {
            var best = all.Where(x => pick(x.Found) is not null).OrderByDescending(x => pick(x.Found)).FirstOrDefault();
            return best.Found is null ? Dash : text(best.Account, best.Found, pick(best.Found)!.Value);
        }

        var facts = new List<FactModel>();
        if (recipe.Period is not null)
        {
            facts.Add(new FactModel($"Best {RecipeWords.Period(recipe)}",
                Highest(r => r.BestPeriodValue, (a, r, v) => $"{a.DisplayName} · {ShortValue(v, stat.Format, zone)} · {r.BestPeriod}")));

            var bestRank = all.Where(x => x.Found.BestRank is not null).OrderBy(x => x.Found.BestRank).FirstOrDefault();
            facts.Add(new FactModel("Best rank",
                bestRank.Found is null ? Dash : $"{bestRank.Account.DisplayName} · #{bestRank.Found.BestRank} · {bestRank.Found.BestRankPeriod}"));
        }

        facts.Add(new FactModel("Highest", Highest(r => r.Highest, (a, _, v) => $"{a.DisplayName} · {ShortValue(v, stat.Format, zone)}")));
        facts.Add(new FactModel("Biggest day", Highest(r => r.BiggestDay, (a, _, v) => $"{a.DisplayName} · {PanelText.Change(v, stat.Format)}")));
        facts.Add(new FactModel("Fastest 7 days", Highest(r => r.FastestWeek, (a, _, v) => $"{a.DisplayName} · {PanelText.Change(v, stat.Format)}")));

        return new RecordsModel(new PanelHead(title, stat.Label), facts);
    }

    /// <summary>A group list's rows live, with your sources' groups placed where they'd rank. Live only.</summary>
    public static TopModel Top(LiveBoard live, PanelSettings settings)
    {
        var installed = live.FindRecipe(settings.Recipe);
        var groupRecipe = PanelText.GroupRecipe(live.Installed);
        var title = PanelText.Title(PanelType.Top, installed?.Recipe, live.Installed);
        var nameColumn = groupRecipe is null ? "Name" : RecipeWords.Capital(RecipeWords.Group(groupRecipe));

        if (installed is not { Recipe.IsGroupList: true } || live.FindSource(settings.SourceId) is not { } source)
        {
            return new TopModel(StaleSource(live, settings, title), nameColumn, "", []);
        }

        var recipe = installed.Recipe;
        var key = recipe.LastStep.Values[0].Id;
        var valueColumn = recipe.LastStep.Values[0].Label;
        var head = new PanelHead(title, Overdue: live.IsOverdue(source), Note: "From the source's own top list. ~ marks an estimate from your own read.");

        // Live only (plan A40): a group list's groups are shown and never kept, so the book has none to give back.
        if (live.LiveOf(source.Id)?.Groups is not { Count: > 0 } groups)
        {
            // After a read that brought nothing back, "waiting for the first read" is not true (S1-F.6).
            var why = live.LiveOf(source.Id) is null ? "Waiting for the first read." : PanelText.NothingBack();
            return new TopModel(head with { Note = why }, nameColumn, valueColumn, []);
        }

        var ordered = OrderGroups(groups, key);
        // Why a row is on this list and whether the row is YOURS are two questions, and one name answered both
        // until V3-S.33. A clan you watch earns its place past the cut, because seeing it is the point of watching
        // it, and is not tinted, because the tint says "this is mine". Ruled by the owner, 2026-09-20.
        var followed = live.Sources.Where(s => s.Enabled && live.FindRecipe(s.Recipe) is { Recipe.IsGroupList: false }).ToList();
        var mainNames = followed.Where(s => s.Role == SourceRole.Main).Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shownNames = followed.Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // SourceName is the recipe's MAIN input; MyClanNames takes every input a source carries, so every name this
        // list can show for one of yours is already in it. The derivations differ, the answer cannot disagree.
        var yourNames = SourceRules.MyClanNames(live.Sources, live.Installed).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<(double Sort, TopRow Row)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var group = ordered[i];
            var isYours = yourNames.Contains(group.Row.Name);
            if (i >= TopCount && !shownNames.Contains(group.Row.Name)) continue;

            var name = mainNames.Contains(group.Row.Name) ? $"{group.Row.Name} ★" : group.Row.Name;
            rows.Add((group.Rank, new TopRow(group.Rank.ToString(CultureInfo.InvariantCulture), name, PanelText.Short(group.Value), isYours, false)));
        }

        var values = ordered.Where(g => g.Value is not null).Select(g => g.Value!.Value).ToList();
        double? lowest = values.Count == 0 ? null : values.Min();
        var placed = new HashSet<string>(ordered.Select(g => g.Row.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var mineSource in followed)
        {
            var name = live.SourceName(mineSource);
            if (!placed.Add(name)) continue;

            var mineRecipe = live.FindRecipe(mineSource.Recipe)!.Recipe;
            if (HeadlineNumber(live.LiveOf(mineSource.Id), TotalId(mineRecipe)) is not { } total) continue;

            var shown = mainNames.Contains(name) ? $"{name} ★" : name;

            // Below every value the list itself shows, "~N+1" would claim a rank the list never proved.
            if (lowest is { } low && total < low)
            {
                rows.Add((double.MaxValue, new TopRow("below the list", shown, StatText.Abbrev(total), yourNames.Contains(name), true)));
                continue;
            }

            if (Records.WouldPlace(total, values) is not { } place) continue;

            // Sits just before the group it would outrank, not after: "~2" among 990/980 lands between them.
            rows.Add((place.Place - 0.5, new TopRow($"~{place.Place}", shown, StatText.Abbrev(total), yourNames.Contains(name), true)));
        }

        return new TopModel(head, nameColumn, valueColumn, [.. rows.OrderBy(r => r.Sort).Select(r => r.Row)]);
    }

    public static ProfileStatModel ProfileStat(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.ProfileStat, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new ProfileStatModel(StaleSource(live, settings, title), "", []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new ProfileStatModel(new PanelHead(title, Stale: PanelText.StaleStat), "", []);
        }

        // A pinned source that's gone is stale: falling back drew another source's numbers under the settings you chose, with
        // nothing on screen to say so (backlog S1-13.7). Only an unpinned panel reads "the recipe's source", the recipe's first
        // source that is on, else its first, which is the Accounts table's rule (D17) and what it was asked for.
        var source = settings.SourceId is { } pinned ? live.FindSource(pinned) : PanelForms.FirstSourceOfRecipe(live, recipe.Slug);
        if (source is null) return new ProfileStatModel(StaleSource(live, settings, title), "", []);

        var snapshot = live.SnapshotOf(source.Id);
        var now = live.Now;
        var midnight = PanelText.Midnight(now, live.Time.LocalTimeZone);

        var rows = new List<(double? Value, ProfileRow Row)>();
        foreach (var account in live.Accounts.Where(a => a.RobloxUserId != 0))
        {
            var row = snapshot?.Rows?.FirstOrDefault(r => r.UserId == account.RobloxUserId);
            var value = row is null ? null : ValueOf(row, stat.Key);
            var unavailable = snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId);
            var missed = snapshot?.CellMisses.GetValueOrDefault((account.RobloxUserId, stat.Key));
            // The full history, unclipped: a window that starts mid-series must still see what came before it.
            var series = reader.Series(source.Id, account.RobloxUserId, stat.Key, null, DateTimeOffset.MinValue);

            rows.Add((value, new ProfileRow(
                account.DisplayName,
                PanelText.Value(value, stat.Format, live.Time.LocalTimeZone),
                WindowGain(series, midnight, stat.Format),
                WindowGain(series, now.AddDays(-7), stat.Format),
                // The recipe's own sentence when it has one. Otherwise, a value this read did not bring back says so
                // plainly: "can't read" named no cause (S1-13.7), but the recorded miss is a path and a list of keys,
                // which is true and unreadable. That detail stays in Setup > Diagnostics.
                unavailable ?? (value is null && missed is not null ? PanelText.NotInLastRead : ""),
                value is null)));
        }

        // A source that is off is never read, so its dashes say so rather than wait for a read that won't come.
        return new ProfileStatModel(
            new PanelHead(title, stat.Label, Overdue: live.IsOverdue(source), Note: source.Enabled ? "" : PanelText.SwitchedOff(live.SourceName(source)),
                Remembered: live.IsRemembered(source.Id)),
            stat.Label, MissingLast(rows));
    }

    /// <summary>
    /// Your accounts side by side (the Alts tab): a column per shown stat sorted as this session asks (D15), the sorted
    /// column's change today and over 7 days from the book (D16), a totals row and an account that couldn't be read
    /// saying why (D17). Only your own accounts: other rows a list recipe read are never looked at.
    /// </summary>
    public static AccountsTableModel AccountsTable(
        LiveBoard live, ScoreBookReader reader, PanelSettings settings, AccountSort? sort = null, long? pickedUserId = null)
    {
        var title = PanelText.Title(PanelType.AccountsTable, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var recipe = installed.Recipe;

        // A pinned source that's gone is stale; an unpinned table reads the recipe's first source that is on, else its first (D17).
        var source = settings.SourceId is { } pinned ? live.FindSource(pinned) : PanelForms.FirstSourceOfRecipe(live, recipe.Slug);
        if (source is null) return new AccountsTableModel(StaleSource(live, settings, title), [], []);

        var stats = installed.State.ShownStats(recipe);
        var snapshot = live.SnapshotOf(source.Id);
        var zone = live.Time.LocalTimeZone;

        var effective = sort is { } asked && (asked.Key == AccountSort.NameKey || stats.Any(s => s.Key == asked.Key))
            ? asked
            : stats.Count > 0 ? new AccountSort(stats[0].Key, Descending: true) : new AccountSort(AccountSort.NameKey, Descending: false);
        var sorted = stats.FirstOrDefault(s => s.Key == effective.Key);
        var withChange = sorted is { Format: not StatFormat.Date };
        var byName = effective.Key == AccountSort.NameKey;

        var columns = new List<AccountColumn> { new(AccountSort.NameKey, "Account", AccountColumnKind.Name, byName, byName && effective.Descending) };
        foreach (var stat in stats)
        {
            var isSorted = stat.Key == effective.Key;
            columns.Add(new AccountColumn(stat.Key, stat.Label, AccountColumnKind.Stat, isSorted, isSorted && effective.Descending));
            if (!isSorted || !withChange) continue;

            columns.Add(new AccountColumn(stat.Key, "Today", AccountColumnKind.Today));
            columns.Add(new AccountColumn(stat.Key, "7 days", AccountColumnKind.Week));
        }

        var midnight = PanelText.Midnight(live.Now, zone);
        var read = live.Accounts
            .Where(a => a.RobloxUserId != 0)
            .DistinctBy(a => a.RobloxUserId)
            .Select(a => (Account: a, Row: snapshot?.Rows?.FirstOrDefault(r => r.UserId == a.RobloxUserId)))
            .ToList();

        var rows = new List<(double? Sort, AccountRow Row)>();
        foreach (var (account, row) in read)
        {
            IReadOnlyList<SeriesPoint> series = sorted is not null && withChange
                ? reader.Series(source.Id, account.RobloxUserId, sorted.Key, null, DateTimeOffset.MinValue)
                : [];
            var changeFormat = sorted?.Format ?? StatFormat.Number;

            var cells = columns.Select(column => column.Kind switch
            {
                AccountColumnKind.Name => account.DisplayName,
                AccountColumnKind.Today => WindowGain(series, midnight, changeFormat, Dash),
                AccountColumnKind.Week => WindowGain(series, live.Now.AddDays(-7), changeFormat, Dash),
                _ => PanelText.Value(row is null ? null : ValueOf(row, column.Key), StatOf(stats, column.Key).Format, zone),
            }).ToList();

            rows.Add((sorted is null || row is null ? null : ValueOf(row, sorted.Key), new AccountRow(
                account.RobloxUserId,
                account.DisplayName,
                cells,
                snapshot?.Unavailable.GetValueOrDefault(account.RobloxUserId) ?? "",
                Missing: row is null,
                Picked: account.RobloxUserId == pickedUserId,
                Avatar: live.AvatarFor(account.RobloxUserId))));
        }

        var names = StringComparer.OrdinalIgnoreCase;
        IEnumerable<(double? Sort, AccountRow Row)> ordered = byName
            ? effective.Descending ? rows.OrderByDescending(r => r.Row.Name, names) : rows.OrderBy(r => r.Row.Name, names)
            : effective.Descending
                ? rows.OrderBy(r => r.Sort is null).ThenByDescending(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names)
                : rows.OrderBy(r => r.Sort is null).ThenBy(r => r.Sort ?? 0).ThenBy(r => r.Row.Name, names);
        var list = ordered.Select(r => r.Row).ToList();

        if (stats.Count > 0 && list.Count > 0)
        {
            var totals = columns.Select(column =>
            {
                if (column.Kind == AccountColumnKind.Name) return "Total";
                if (column.Kind != AccountColumnKind.Stat || StatOf(stats, column.Key) is not { Sum: true, Format: not StatFormat.Date } stat) return "";

                var values = read.Where(x => x.Row is not null).Select(x => ValueOf(x.Row!, stat.Key)).OfType<double>().ToList();
                return values.Count == 0 ? Dash : PanelText.Value(values.Sum(), stat.Format, zone);
            }).ToList();
            list.Add(new AccountRow(0, "Total", totals, "", Missing: false, Picked: false, IsTotal: true));
        }

        // A source that is switched off is never read, so the table says so instead of waiting for a read that won't come.
        var note = stats.Count == 0 ? "Tick Show on a stat to fill this panel."
            : snapshot is not null ? ""
            : source.Enabled ? "Waiting for the first read."
            : PanelText.SwitchedOff(live.SourceName(source));
        return new AccountsTableModel(
            new PanelHead(title, live.SourceName(source), Overdue: live.IsOverdue(source), Note: note, Remembered: live.IsRemembered(source.Id)),
            columns, list);
    }

    /// <summary>Every row of a source live, your accounts marked (spec §9.4). Other members' names come from memory only.</summary>
    public static LeaderboardModel LiveLeaderboard(LiveBoard live, PanelSettings settings, IReadOnlyDictionary<long, string> names)
    {
        var title = PanelText.Title(PanelType.LiveLeaderboard, null, live.Installed);
        var installed = live.FindRecipe(settings.Recipe);
        if (installed is null || live.FindSource(settings.SourceId) is not { } source)
        {
            return new LeaderboardModel(StaleSource(live, settings, title), [], []);
        }

        var shown = installed.State.ShownStats(installed.Recipe);
        var head = new PanelHead(title, live.SourceName(source), live.ChipRole(source), live.IsOverdue(source), Note: "Live only. Never saved.");
        if (shown.Count == 0) return new LeaderboardModel(head with { Note = "Tick Show on a stat to fill this panel." }, [], []);

        IReadOnlyList<string> columns = [.. shown.Select(s => s.Label)];
        // Live only (plan A40): every row but yours is memory alone, so a remembered snapshot would show you by yourself.
        if (live.LiveOf(source.Id)?.Rows is not { } rows)
        {
            // Empty after a read this session is said, not left blank (S1-F.6); before one, "Live only" is all there is to say.
            return new LeaderboardModel(live.LiveOf(source.Id) is null ? head : head with { Note = $"{head.Note} {PanelText.NothingBack()}" }, columns, []);
        }

        var ranked = Leaderboard.Rank(rows, live.MyUserIds, shown[0].Key);
        var zone = live.Time.LocalTimeZone;
        return new LeaderboardModel(head, columns, [.. ranked.Select(r => new LeaderRow(
            r.Position.ToString(CultureInfo.InvariantCulture),
            r.IsMine ? live.AccountName(r.UserId) : names.GetValueOrDefault(r.UserId) ?? $"Member {r.UserId}",
            [.. shown.Select(s => PanelText.Value(r.Values.TryGetValue(s.Key, out var v) ? v : null, s.Format, zone))],
            r.IsMine))]);
    }

    /// <summary>The rise from the first to the last reading, or null with fewer than two.</summary>
    public static double? Gain(IReadOnlyList<SeriesPoint> points) =>
        points.Count < 2 ? null : points[^1].Value - points[0].Value;

    /// <summary>
    /// A window's gain: from the latest reading at or before <paramref name="since"/> to the last reading.
    /// When nothing was read that early, falls back to the series' own first reading and states the real
    /// span covered, rather than silently understating a shorter history as the full window (spec §9.4).
    /// Written in the stat's format; a date has no gain, and fewer than two readings is <paramref name="none"/>.
    /// </summary>
    private static string WindowGain(IReadOnlyList<SeriesPoint> series, DateTimeOffset since, StatFormat format = StatFormat.Number, string none = "no earlier read")
    {
        if (format == StatFormat.Date) return Dash;
        if (series.Count < 2) return none;

        var last = series[^1];
        SeriesPoint? baseline = null;
        for (var i = series.Count - 2; i >= 0; i--)
        {
            if (series[i].T <= since)
            {
                baseline = series[i];
                break;
            }
        }

        var from = baseline ?? series[0];
        var text = PanelText.Change(last.Value - from.Value, format);
        return baseline is null ? $"{text} in {StatText.Span(last.T - from.T)}" : text;
    }

    /// <summary>
    /// <see cref="Records.Change"/>'s recent change ("+220K in 1h") written in the stat's format: a duration's rise reads
    /// as time ("+2h 0m in 3h"), and a date has no change.
    /// </summary>
    private static string RecentChange(IReadOnlyList<SeriesPoint> series, StatFormat format) =>
        format == StatFormat.Date ? Dash
        : Records.Movement(series) is not { } moved ? Records.NoEarlierRead
        : $"{PanelText.Change(moved.Delta, format)} in {StatText.Span(moved.Span)}";

    private sealed record RankedGroup(GroupRow Row, int Rank, double? Value);

    internal static PanelHead StaleSource(LiveBoard live, PanelSettings settings, string title) =>
        new(title, Stale: PanelText.StaleSource(live.FindRecipe(settings.Recipe)?.Recipe is { } recipe ? RecipeWords.Group(recipe) : "source"));

    private static AccountCardModel EmptyCard(PanelHead head) => new(head, "", Dash, [], [], [], "");

    /// <summary>
    /// Your accounts under a heading with no value, rank or change, because no read of your own sources placed them. Each row's
    /// note comes from live.Snapshots, not live.SnapshotOf: a remembered snapshot never carries an Unavailable entry (A39), so
    /// this reads what was actually read, and says nothing at all before the first read.
    /// </summary>
    private static AccountGroupModel Leftovers(LiveBoard live, string heading, IEnumerable<HostAccount> accounts) =>
        new(heading, [.. accounts.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
            .Select(a => new AccountLineModel(
                a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true, live.AvatarFor(a.RobloxUserId),
                PanelText.CannotRead(a.RobloxUserId, live.Installed, live.Sources, live.Snapshots, nameTheRecipe: false)))]);

    /// <summary>
    /// A card's other shown stats under the recipe's own section names (D12): stats with no section first, with no
    /// heading; then sections in the order the recipe first names them; a picked counter's section (its counters'
    /// label) last. Each value reads as its format says.
    /// </summary>
    private static IReadOnlyList<CardSection> Sections(Recipe recipe, IEnumerable<RecipeStat> stats, RecipeRow row, TimeZoneInfo zone)
    {
        var order = recipe.LastStep.Values.Select(v => v.Section ?? "").Prepend("").Distinct(StringComparer.Ordinal).ToList();
        return [.. stats
            .GroupBy(s => s.Section ?? "", StringComparer.Ordinal)
            .OrderBy(g => order.IndexOf(g.Key) is var at && at >= 0 ? at : int.MaxValue)
            .Select(g => new CardSection(g.Key, [.. g.Select(s => new FactModel(s.Label, PanelText.Value(ValueOf(row, s.Key), s.Format, zone)))]))];
    }

    /// <summary>A record's own reading: abbreviated for a number ("12.4M"), written out for a duration or date (a value fact, not a gain — those don't further abbreviate).</summary>
    private static string ShortValue(double value, StatFormat format, TimeZoneInfo zone) =>
        format == StatFormat.Number ? PanelText.Short(value) : PanelText.Value(value, format, zone);

    private static IEnumerable<Source> SourcesYoursIn(LiveBoard live, Recipe recipe) =>
        live.Sources
            .Where(s => s.Enabled && s.Role != SourceRole.Watch && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal))
            .OrderBy(s => s.Role == SourceRole.Main ? 0 : 1);

    private static string? PlaceId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => !h.Sum)?.Id;

    private static string? TotalId(Recipe recipe) => recipe.Headline.FirstOrDefault(h => h.Sum)?.Id;

    private static double? HeadlineNumber(RecipeSnapshot? snapshot, string? id) =>
        id is null ? null : snapshot?.Headline?.FirstOrDefault(h => h.Id == id)?.Number;

    /// <summary>
    /// One account's place among the rows its source read that have the stat — "#7 of 49" — or a dash when there is no
    /// honest answer (plan A40, review C1). The one door for a rank, so neither panel can grow its own.
    /// <para>
    /// A reading from this session carries every row, so <paramref name="live"/> is counted from it, and N is how many rows
    /// it ranked: a row with no value is in neither (backlog S1-6.9). Counting every row made a member with no points part
    /// of a "#1 of 4" beside Promotion check's field of 3.
    /// </para>
    /// <para>
    /// A REMEMBERED reading carries your own accounts alone, and a place worked out from those would read "#1 of 4" of a
    /// group this never counted — so it is answered from what the reading itself kept (<see cref="RecipeSnapshot.RememberedRanks"/>),
    /// and from nothing else. A line that kept no place shows none: an empty "In clan" is honest, "#1 of 4" is not. A line
    /// that kept a place but not its field shows the place alone.
    /// </para>
    /// </summary>
    private static string InGroup(
        RecipeSnapshot snapshot, IReadOnlyDictionary<long, int>? live, long userId, string stat, double? value)
    {
        if (value is null) return Dash;

        if (snapshot.RememberedAt is not null)
        {
            if (!snapshot.RememberedRanks.TryGetValue((userId, stat), out var kept)) return Dash;
            return kept.Of is { } of ? $"#{kept.Rank} of {of}" : $"#{kept.Rank}";
        }

        return live is not null && live.TryGetValue(userId, out var rank) ? $"#{rank} of {live.Count}" : Dash;
    }

    private static double? ValueOf(RecipeRow row, string stat) => row.Values.TryGetValue(stat, out var value) ? value : null;

    private static RecipeStat StatOf(IReadOnlyList<RecipeStat> stats, string key) => stats.First(s => s.Key == key);

    /// <summary>A recipe with a period shows the current period; one without shows the last 30 days (spec §9.1).</summary>
    private static DateTimeOffset Since(Recipe recipe, DateTimeOffset now) => recipe.Period is null ? now.AddDays(-30) : DateTimeOffset.MinValue;

    /// <summary>Highest first; a missing value sorts last and never counts as zero (spec §9.6).</summary>
    private static IReadOnlyList<T> MissingLast<T>(IEnumerable<(double? Value, T Row)> rows) =>
        [.. rows.OrderBy(r => r.Value is null).ThenByDescending(r => r.Value ?? 0).Select(r => r.Row)];

    private static List<RankedGroup> OrderGroups(IReadOnlyList<GroupRow> groups, string key)
    {
        var ordered = groups
            .Select(g => (Row: g, Value: g.Values.TryGetValue(key, out var v) ? v : (double?)null))
            .OrderBy(g => g.Row.Rank ?? int.MaxValue)
            .ThenBy(g => g.Value is null)
            .ThenByDescending(g => g.Value ?? 0)
            .ToList();

        return [.. ordered.Select((g, i) => new RankedGroup(g.Row, g.Row.Rank ?? i + 1, g.Value))];
    }

    /// <summary>
    /// The gap to the group just above, only when a group list holds both (spec §9.4). A list ranks ties as competitions do
    /// (11, 12, 12, 14), so the group above is the nearest one ranked HIGHER, never one this group ties with; and the list
    /// holds every group ranked between them exactly when that group's rank plus how many share it is this group's rank.
    /// 14th measures to 12th, each 12th to 11th, and 12, 14 with no 13th still shows none. Looking only for rank minus one
    /// hid the gap behind every tie and inside it (backlog S1-13.5).
    /// </summary>
    private static (bool Has, string Label, string Text, double Fill) Gap(LiveBoard live, string name)
    {
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.FindRecipe(source.Recipe)?.Recipe is not { IsGroupList: true } recipe) continue;
            if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups) continue;

            var ordered = OrderGroups(groups, recipe.LastStep.Values[0].Id);
            var index = ordered.FindIndex(g => string.Equals(g.Row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index <= 0) continue;

            var here = ordered[index];
            var aboveIndex = ordered.FindLastIndex(index - 1, g => g.Rank < here.Rank);
            if (aboveIndex < 0) continue;

            var above = ordered[aboveIndex];
            if (above.Rank + ordered.Count(g => g.Rank == above.Rank) != here.Rank || above.Value is not { } a || here.Value is not { } h) continue;

            return (true, $"To {PanelText.Ordinal(above.Rank)}", $"{StatText.Abbrev(Math.Max(0, a - h))} behind", a <= 0 ? 0 : Math.Clamp(h / a, 0, 1));
        }

        return (false, "", "", 0);
    }
}
