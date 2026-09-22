using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

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

/// <summary>
/// Race (spec §9.4): your clans' totals over the period as lines, and the board around you from a clans list. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public const int MaxRace = 5;

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

    /// <summary>
    /// How many clans either side of yours are drawn: the ones that decide whether you move a place. Taken from
    /// <see cref="GroupRows"/>, never named again here — the chart can only draw what the book kept, so the writer
    /// sets the size and the reader follows it (V3-S.34).
    /// </summary>
    private const int Neighbours = GroupRows.Neighbours;

    /// <summary>How much of the board the standings list offers, names and all. Same rule, same reason.</summary>
    private const int StandingsShown = GroupRows.Top;

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
}
