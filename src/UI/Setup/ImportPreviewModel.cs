using System.ComponentModel;
using System.Runtime.CompilerServices;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.UI;

/// <summary>One row of the preview: a plan item with a tick where the item is a change, greyed while its recipe is not coming.</summary>
public sealed class ImportPreviewRow(SetupItem item) : INotifyPropertyChanged
{
    private bool _ticked = item.Ticked;
    private bool _canTick = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SetupItem Item { get; } = item;

    public bool HasTick { get; } = item.Outcome is SetupOutcome.Add or SetupOutcome.Update or SetupOutcome.Replace;

    public bool Ticked { get => _ticked; set { if (_ticked != value) { _ticked = value; Raise(); } } }

    /// <summary>False for a clan whose recipe is neither coming nor here: shown greyed, with the reason in <see cref="Text"/>.</summary>
    public bool CanTick { get => _canTick; set { if (_canTick != value) { _canTick = value; Raise(); Raise(nameof(Text)); } } }

    public string Name => Item.Name;

    public string Text => !CanTick ? "needs its recipe" : Item.Outcome switch
    {
        SetupOutcome.Add => "Add — " + Item.Note,
        SetupOutcome.Update => "Update — " + Item.Note,
        SetupOutcome.Replace => "Replace — " + Item.Note,
        SetupOutcome.Same => "Same as here",
        SetupOutcome.Kept => "Kept — " + Item.Note,
        _ => Item.Note,
    };

    public string TickName => "Import " + Item.Name;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ImportPreviewGroup(string Heading, IReadOnlyList<ImportPreviewRow> Rows);

/// <summary>What the preview shows for a plan, and the one line said after (spec §3, §4). No WPF here.</summary>
public static class ImportPreviewModel
{
    public const string NothingSent = "Nothing will be sent from this PC until you tick it in Setup › Stats.";

    public const string AsideNote = "Your clans, recipes and boards here are copied aside first, dated, in case.";

    public static IReadOnlyList<ImportPreviewGroup> Groups(SetupMergePlan plan)
    {
        var rows = plan.Items.Select(i => new ImportPreviewRow(i)).ToList();
        Regrey(rows, plan);
        (SetupKind Kind, string Heading)[] order =
        [
            (SetupKind.Recipe, "RECIPES"), (SetupKind.Clan, "CLANS"), (SetupKind.Board, "BOARDS"),
            (SetupKind.Key, "KEYS TO ENTER AGAIN"), (SetupKind.Stats, "STATS"),
        ];
        return [.. order.Select(o => new ImportPreviewGroup(o.Heading, rows.Where(r => r.Item.Kind == o.Kind).ToList())).Where(g => g.Rows.Count > 0)];
    }

    public static string Intro(BookPackManifest manifest) =>
        $"Exported {manifest.TakenAt.ToLocalTime():d MMM} by Ur Score {manifest.App} on the other PC.";

    public static IReadOnlySet<string> TickedKeys(IEnumerable<ImportPreviewRow> rows) =>
        rows.Where(r => r.HasTick && r.Ticked && r.CanTick).Select(r => r.Item.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>After any tick changes: a clan can be ticked only while its recipe is ticked or installed.</summary>
    public static void Regrey(IReadOnlyList<ImportPreviewRow> rows, SetupMergePlan plan)
    {
        var ticked = rows.Where(r => r.HasTick && r.Ticked).Select(r => r.Item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows) row.CanTick = plan.CanTick(row.Item, ticked);
    }

    /// <summary>
    /// Spec §4's after-line: what was imported, what was kept, what a version gap skipped, what needs a key, and
    /// where the setup that was here before stands — or, on a failed step, what stands and what does not.
    /// </summary>
    public static string AfterLine(SetupApplied applied, BookImportOutcome stats)
    {
        var aside = applied.AsideFolder is { } folder ? $" Your previous setup is in {System.IO.Path.GetFileName(folder)}." : "";
        if (applied.FailedStep is { } step)
        {
            var done = Parts(applied.Recipes, applied.Clans, applied.Boards);
            return done.Count == 0
                ? $"Nothing was imported: the {step} could not be written ({applied.FailureType}).{aside}"
                : $"Imported {Join(done)}, then the {step} could not be written ({applied.FailureType}); what was imported before that stands.{aside}";
        }

        var changed = Parts(applied.Recipes, applied.Clans, applied.Boards);
        var parts = new List<string> { changed.Count == 0 ? "Nothing new in the setup" : "Imported " + Join(changed) };
        if (applied.KeptClans > 0) parts.Add(applied.KeptClans == 1 ? "1 clan kept as it was" : $"{applied.KeptClans} clans kept as they were");
        if (applied.SkippedRecipes is { Count: > 0 } skipped)
        {
            parts.Add(skipped.Count == 1
                ? $"1 recipe skipped as it did not parse here ({skipped[0]})"
                : $"{skipped.Count} recipes skipped as they did not parse here ({string.Join(", ", skipped)})");
        }

        if (applied.Keys > 0) parts.Add(applied.Keys == 1 ? "1 key to enter in Setup › Recipes" : $"{applied.Keys} keys to enter in Setup › Recipes");
        var line = string.Join("; ", parts) + ".";
        if (stats.Message.Length > 0) line += " Then " + char.ToLowerInvariant(stats.Message[0]) + stats.Message[1..];
        return line + aside;
    }

    private static List<string> Parts(int recipes, int clans, int boards)
    {
        var parts = new List<string>();
        if (recipes > 0) parts.Add(recipes == 1 ? "1 recipe" : $"{recipes} recipes");
        if (clans > 0) parts.Add(clans == 1 ? "1 clan" : $"{clans} clans");
        if (boards > 0) parts.Add(boards == 1 ? "1 board" : $"{boards} boards");
        return parts;
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "nothing",
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
