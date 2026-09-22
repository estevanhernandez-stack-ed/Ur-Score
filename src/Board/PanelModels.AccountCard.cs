using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

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

/// <summary>
/// Account card (spec §9.4): one account's big numbers, its line over time, rank and best period. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
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
}
