using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

using Source = Labs626.UrScore.Core.Source;

public enum PanelType { Standing, Race, MyAccounts, PromotionCheck, AccountCard, PastPeriods, Records, Top, ProfileStat, LiveLeaderboard, AccountsTable }

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
    string Title, string Subtitle = "", SourceRole? ChipRole = null, bool Overdue = false, string? Stale = null, string Note = "")
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
    bool Running)
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

    public RecipeSnapshot? SnapshotOf(string sourceId) => Snapshots.GetValueOrDefault(sourceId);

    /// <summary>The source's main input value ("CCGP"), else its recipe's name.</summary>
    public string SourceName(Source source)
    {
        var recipe = FindRecipe(source.Recipe)?.Recipe;
        if (recipe is not null && RecipeWords.MainInput(recipe) is { } input
            && source.Inputs.TryGetValue(input.Id, out var value) && value.Trim().Length > 0)
        {
            return value.Trim();
        }

        return recipe?.Name ?? source.Recipe;
    }

    public string AccountName(long userId) =>
        Accounts.FirstOrDefault(a => a.RobloxUserId == userId && userId != 0)?.DisplayName ?? "One of your accounts";

    /// <summary>Spec §9.6: only while reading runs, and only once the source has been read.</summary>
    public bool IsOverdue(Source source) =>
        Running
        && LastRead.TryGetValue(source.Id, out var last)
        && FindRecipe(source.Recipe) is { } installed
        && Records.Overdue(last, installed.Recipe.EffectiveEverySeconds, Now);
}

public sealed record StandingModel(
    PanelHead Head, string Place, string PlaceSuffix, string TotalLabel, string Total, string Change,
    bool HasGap, string GapLabel, string Gap, double GapFill, bool HasAccounts, string Accounts, string PeriodLine);

public sealed record LegendItem(string Text, int Colour);

public sealed record RaceModel(PanelHead Head, IReadOnlyList<ChartSeries> Series, IReadOnlyList<LegendItem> Legend, string ChartName);

public sealed record AccountLineModel(long UserId, string Name, string Value, string InGroup, string Change, bool Sent, bool Stalled, bool Missing);

public sealed record AccountGroupModel(string Heading, IReadOnlyList<AccountLineModel> Rows);

public sealed record MyAccountsModel(PanelHead Head, string ValueColumn, string GroupColumn, IReadOnlyList<AccountGroupModel> Groups);

public sealed record PromotionRow(string Name, string Value, string WouldPlace, bool Fits, bool Missing);

public sealed record PromotionModel(PanelHead Head, string LowestLabel, string Lowest, string ValueColumn, IReadOnlyList<PromotionRow> Rows);

public sealed record FactModel(string Label, string Value);

/// <summary>One heading of an account card and its facts; the heading is the recipe's section name, empty for stats with none.</summary>
public sealed record CardSection(string Heading, IReadOnlyList<FactModel> Facts)
{
    public bool HasHeading => Heading.Length > 0;
}

public sealed record AccountCardModel(
    PanelHead Head, string BigLabel, string Big, IReadOnlyList<CardSection> Sections, IReadOnlyList<ChartSeries> Line,
    IReadOnlyList<FactModel> Facts, string ChartName)
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
        var change = totalId is null || !periodKnown
            ? Dash
            : Records.Change(reader.HeadlineSeries(source.Id, totalId, snapshot?.Period?.Value), live.Now);
        var gap = Gap(live, name);

        var rows = snapshot?.Rows;
        var hasAccounts = source.Role != SourceRole.Watch && rows is not null;
        var mine = rows?.Count(r => live.MyUserIds.Contains(r.UserId)) ?? 0;

        return new StandingModel(
            new PanelHead(title, name, source.Role, live.IsOverdue(source)),
            place is { } p ? PanelText.Ordinal((int)p) : Dash,
            recipe.Period is null || place is null ? "" : $"in the {RecipeWords.Period(recipe)}",
            recipe.Headline.FirstOrDefault(h => h.Id == totalId)?.Label ?? "Total",
            PanelText.Full(total),
            change,
            gap.Has, gap.Label, gap.Text, gap.Fill,
            hasAccounts,
            hasAccounts ? $"{mine} of {rows!.Count}" : "",
            PanelText.PeriodLine(snapshot?.Period, live.Now, null));
    }

    public static RaceModel Race(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var recipe = live.FindRecipe(settings.Recipe)?.Recipe;
        var title = PanelText.Title(PanelType.Race, recipe, live.Installed);
        var totalId = recipe is null ? null : TotalId(recipe);
        var sources = (settings.SourceIds ?? []).Select(live.FindSource).OfType<Source>().Take(MaxRace).ToList();

        if (recipe is null || totalId is null || sources.Count == 0)
        {
            return new RaceModel(StaleSource(live, settings, title), [], [], "");
        }

        var series = new List<ChartSeries>();
        var legend = new List<LegendItem>();
        var overdue = false;
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

            // The live read, until the book has a line for it.
            if (HeadlineNumber(snapshot, totalId) is { } now && live.LastRead.TryGetValue(source.Id, out var at)
                && (points.Count == 0 || points[^1].T < at.AddSeconds(-30)))
            {
                points.Add(new ChartPoint(at, now));
            }

            var label = PanelText.SourceLabel(live.SourceName(source), source.Role);

            series.Add(new ChartSeries(label, points, i));
            legend.Add(new LegendItem($"{label} {(points.Count > 0 ? StatText.Abbrev(points[^1].Value) : Dash)}", i));
            overdue |= live.IsOverdue(source);
        }

        var totalLabel = recipe.Headline.First(h => h.Id == totalId).Label;
        var head = new PanelHead(title, $"{RecipeWords.Lower(totalLabel)} since the {RecipeWords.Period(recipe)} started", Overdue: overdue);

        // No source's period is known yet: every point in "series" would be mixing periods together.
        if (!anyPeriodKnown)
        {
            return new RaceModel(head with { Note = "Waiting for the first read." }, [], [], "");
        }

        return new RaceModel(head, series, legend, $"{title}: {string.Join(", ", legend.Select(l => l.Text))}");
    }

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

        foreach (var source in SourcesYoursIn(live, recipe))
        {
            overdue |= live.IsOverdue(source);
            var snapshot = live.SnapshotOf(source.Id);
            if (snapshot?.Rows is not { } rows) continue;

            var mine = live.Accounts
                .Where(a => a.RobloxUserId != 0 && !assigned.Contains(a.RobloxUserId) && rows.Any(r => r.UserId == a.RobloxUserId))
                .ToList();
            if (mine.Count == 0) continue;

            var ranks = Ranking.Competition(rows, stat.Key);
            var period = snapshot.Period?.Value;
            var since = Since(recipe, live.Now);
            var series = mine.ToDictionary(a => a.RobloxUserId, a => reader.Series(source.Id, a.RobloxUserId, stat.Key, period, since));

            var lines = new List<(double? Value, AccountLineModel Line)>();
            foreach (var account in mine)
            {
                assigned.Add(account.RobloxUserId);
                var value = ValueOf(rows.First(r => r.UserId == account.RobloxUserId), stat.Key);
                var others = series.Where(kv => kv.Key != account.RobloxUserId).Select(kv => kv.Value);
                var sent = snapshot.Accounts.Any(l => l.AccountId == account.AccountId && l.LastValues.ContainsKey(stat.Key));

                lines.Add((value, new AccountLineModel(
                    account.RobloxUserId,
                    account.DisplayName,
                    PanelText.Value(value, stat.Format, zone),
                    value is not null && ranks.TryGetValue(account.RobloxUserId, out var rank) ? $"#{rank} of {rows.Count}" : Dash,
                    RecentChange(series[account.RobloxUserId], stat.Format),
                    sent,
                    Records.Stalled(series[account.RobloxUserId], others),
                    value is null)));
            }

            var heading = source.Role == SourceRole.Main ? $"★ {live.SourceName(source)}" : live.SourceName(source);
            groups.Add(new AccountGroupModel(heading, MissingLast(lines)));
        }

        var rest = live.Accounts.Where(a => a.RobloxUserId == 0 || !assigned.Contains(a.RobloxUserId)).ToList();
        if (rest.Count > 0)
        {
            groups.Add(new AccountGroupModel(
                recipe.Inputs.Count > 0 ? $"Not in a watched {group}" : "Not in the last read",
                [.. rest.OrderBy(a => a.DisplayName, StringComparer.Ordinal)
                    .Select(a => new AccountLineModel(a.RobloxUserId, a.DisplayName, Dash, Dash, Dash, false, false, true))]));
        }

        return new MyAccountsModel(
            new PanelHead(title, $"by {RecipeWords.Lower(stat.Label)}", Overdue: overdue, Note: "● sent to RoRoRo"),
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

        var fromRows = live.SnapshotOf(from.Id)?.Rows;
        var toRows = live.SnapshotOf(to.Id)?.Rows;
        if (fromRows is null || toRows is null)
        {
            return new PromotionModel(head with { Note = $"Waiting for a read of {(fromRows is null ? fromName : toName)}." }, lowestLabel, Dash, stat.Label, []);
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
                rows.Add((null, new PromotionRow(account.DisplayName, Dash, Dash, false, true)));
                continue;
            }

            var others = toValues.Where(r => r.UserId != account.RobloxUserId).Select(r => r.Value).ToList();
            var place = Records.WouldPlace(value, others);
            var below = lowest is { } low && value < low;
            var text = place is not { } p ? Dash : below ? "below the lowest" : $"{PanelText.Ordinal(p.Place)} of {p.Of}";
            var fits = place is { } q && !below && q.Place <= others.Count;
            rows.Add((value, new PromotionRow(account.DisplayName, StatText.Abbrev(value), text, fits, false)));
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
            if (settings.UserId is null && wanted is { } id)
            {
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
            var ranks = Ranking.Competition(listRows, stat.Key);
            facts.Add(new FactModel($"In {RecipeWords.Group(recipe)}",
                ValueOf(pickedRow, stat.Key) is not null && ranks.TryGetValue(pickedAccount.RobloxUserId, out var rank) ? $"#{rank} of {listRows.Count}" : Dash));
        }

        var records = Records.For(reader, recipe.Slug, pickedSource.InputsKey, [pickedSource.Id], pickedAccount.RobloxUserId, stat.Key, live.Time);
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

        DateTimeOffset? lastRead = series.Count > 0 ? series[^1].T
            : live.LastRead.TryGetValue(pickedSource.Id, out var at) ? at : null;
        facts.Add(new FactModel("Last read", PanelText.Ago(lastRead, live.Now)));

        if (snapshot.CellMisses.GetValueOrDefault((pickedAccount.RobloxUserId, stat.Key)) is { } miss) facts.Add(new FactModel("Note", miss));

        IReadOnlyList<ChartSeries> line = series.Count >= 2
            ? [new ChartSeries(stat.Label, [.. series.Select(p => new ChartPoint(p.T, p.Value))], 0)]
            : [];

        return new AccountCardModel(
            new PanelHead(title, $"{pickedAccount.DisplayName} · {live.SourceName(pickedSource)}", Overdue: live.IsOverdue(pickedSource)),
            stat.Label, PanelText.Value(ValueOf(pickedRow, stat.Key), stat.Format, zone), sections, line, facts,
            $"{pickedAccount.DisplayName}'s {stat.Label} over time");
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

        var rows = reader.Finals(recipe.Slug, source.InputsKey)
            .GroupBy(f => f.Period, StringComparer.Ordinal)
            .Select(g => (
                Period: g.Key,
                T: g.Max(f => f.T),
                Headline: g.First().Headline,
                Accounts: g.SelectMany(f => f.Accounts).GroupBy(kv => kv.Key).ToDictionary(x => x.Key, x => x.First().Value)))
            .OrderByDescending(g => g.T)
            .Select(g =>
            {
                string best = Dash;
                if (settings.Stat is { } stat)
                {
                    double? top = null;
                    long holder = 0;
                    foreach (var (userId, account) in g.Accounts)
                    {
                        if (!account.V.TryGetValue(stat, out var v) || (top is { } t && v <= t)) continue;
                        top = v;
                        holder = userId;
                    }

                    if (top is { } value) best = $"{StatText.Abbrev(value)} · {live.AccountName(holder)}";
                }

                return new PastRow(
                    g.Period,
                    placeId is not null && g.Headline.TryGetValue(placeId, out var place) ? PanelText.Ordinal((int)place) : Dash,
                    totalId is not null && g.Headline.TryGetValue(totalId, out var total) ? StatText.Abbrev(total) : Dash,
                    best);
            })
            .ToList();

        var note = rows.Count == 0
            ? $"No finished {RecipeWords.Periods(recipe)} kept yet."
            : $"Filled in from the {RecipeWords.Group(recipe)}'s own record.";

        return new PastPeriodsModel(
            new PanelHead(title, live.SourceName(source), source.Role, Note: note),
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

        var all = (
            from account in live.Accounts
            where account.RobloxUserId != 0
            from source in SourcesYoursIn(live, recipe)
            select (Account: account, Found: Records.For(reader, recipe.Slug, source.InputsKey, [source.Id], account.RobloxUserId, stat.Key, live.Time))
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

        if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups)
        {
            return new TopModel(head with { Note = "Waiting for the first read." }, nameColumn, valueColumn, []);
        }

        var ordered = OrderGroups(groups, key);
        var yours = live.Sources.Where(s => s.Enabled && live.FindRecipe(s.Recipe) is { Recipe.IsGroupList: false }).ToList();
        var mainNames = yours.Where(s => s.Role == SourceRole.Main).Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var yourNames = yours.Select(live.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<(double Sort, TopRow Row)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var group = ordered[i];
            var isYours = yourNames.Contains(group.Row.Name);
            if (i >= TopCount && !isYours) continue;

            var name = mainNames.Contains(group.Row.Name) ? $"{group.Row.Name} ★" : group.Row.Name;
            rows.Add((group.Rank, new TopRow(group.Rank.ToString(CultureInfo.InvariantCulture), name, PanelText.Short(group.Value), isYours, false)));
        }

        var values = ordered.Where(g => g.Value is not null).Select(g => g.Value!.Value).ToList();
        double? lowest = values.Count == 0 ? null : values.Min();
        var placed = new HashSet<string>(ordered.Select(g => g.Row.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var mineSource in yours)
        {
            var name = live.SourceName(mineSource);
            if (!placed.Add(name)) continue;

            var mineRecipe = live.FindRecipe(mineSource.Recipe)!.Recipe;
            if (HeadlineNumber(live.SnapshotOf(mineSource.Id), TotalId(mineRecipe)) is not { } total) continue;

            var shown = mainNames.Contains(name) ? $"{name} ★" : name;

            // Below every value the list itself shows, "~N+1" would claim a rank the list never proved.
            if (lowest is { } low && total < low)
            {
                rows.Add((double.MaxValue, new TopRow("below the list", shown, StatText.Abbrev(total), true, true)));
                continue;
            }

            if (Records.WouldPlace(total, values) is not { } place) continue;

            // Sits just before the group it would outrank, not after: "~2" among 990/980 lands between them.
            rows.Add((place.Place - 0.5, new TopRow($"~{place.Place}", shown, StatText.Abbrev(total), true, true)));
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

        var source = live.FindSource(settings.SourceId)
                     ?? live.Sources.FirstOrDefault(s => s.Enabled && string.Equals(s.Recipe, recipe.Slug, StringComparison.Ordinal));
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
                unavailable ?? (value is null && missed is not null ? "can't read" : ""),
                value is null)));
        }

        return new ProfileStatModel(new PanelHead(title, stat.Label, Overdue: live.IsOverdue(source)), stat.Label, MissingLast(rows));
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
                Picked: account.RobloxUserId == pickedUserId)));
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

        var note = stats.Count == 0 ? "Tick Show on a stat to fill this panel." : snapshot is null ? "Waiting for the first read." : "";
        return new AccountsTableModel(new PanelHead(title, live.SourceName(source), Overdue: live.IsOverdue(source), Note: note), columns, list);
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
        var head = new PanelHead(title, live.SourceName(source), source.Role, live.IsOverdue(source), Note: "Live only. Never saved.");
        if (shown.Count == 0) return new LeaderboardModel(head with { Note = "Tick Show on a stat to fill this panel." }, [], []);

        IReadOnlyList<string> columns = [.. shown.Select(s => s.Label)];
        if (live.SnapshotOf(source.Id)?.Rows is not { } rows) return new LeaderboardModel(head, columns, []);

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

    private static PanelHead StaleSource(LiveBoard live, PanelSettings settings, string title) =>
        new(title, Stale: PanelText.StaleSource(live.FindRecipe(settings.Recipe)?.Recipe is { } recipe ? RecipeWords.Group(recipe) : "source"));

    private static AccountCardModel EmptyCard(PanelHead head) => new(head, "", Dash, [], [], [], "");

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

    /// <summary>The gap to the group just above, only when a group list holds both (spec §9.4).</summary>
    private static (bool Has, string Label, string Text, double Fill) Gap(LiveBoard live, string name)
    {
        foreach (var source in live.Sources.Where(s => s.Enabled))
        {
            if (live.FindRecipe(source.Recipe)?.Recipe is not { IsGroupList: true } recipe) continue;
            if (live.SnapshotOf(source.Id)?.Groups is not { Count: > 0 } groups) continue;

            var ordered = OrderGroups(groups, recipe.LastStep.Values[0].Id);
            var index = ordered.FindIndex(g => string.Equals(g.Row.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index <= 0) continue;

            var above = ordered[index - 1];
            var here = ordered[index];
            if (above.Rank != here.Rank - 1 || above.Value is not { } a || here.Value is not { } h) continue;

            return (true, $"To {PanelText.Ordinal(above.Rank)}", $"{StatText.Abbrev(Math.Max(0, a - h))} behind", a <= 0 ? 0 : Math.Clamp(h / a, 0, 1));
        }

        return (false, "", "", 0);
    }
}
