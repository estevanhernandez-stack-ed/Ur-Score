using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record TopRow(string Rank, string Name, string Value, bool Yours, bool Estimate);

public sealed record TopModel(PanelHead Head, string NameColumn, string ValueColumn, IReadOnlyList<TopRow> Rows);

/// <summary>
/// Top (spec §9.4): the top of the period from a clans list, live, with your clans placed where they rank. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public const int TopCount = 10;

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

            var name = mainNames.Contains(group.Row.Name) ? $"{group.Row.Name} {PanelText.MainMark}" : group.Row.Name;
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

            var shown = mainNames.Contains(name) ? $"{name} {PanelText.MainMark}" : name;

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

    private sealed record RankedGroup(GroupRow Row, int Rank, double? Value);

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
}
