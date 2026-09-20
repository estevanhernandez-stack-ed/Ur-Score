using System.IO;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.Book;

/// <summary>What bringing in another PC's book did, in the words the page shows.</summary>
public sealed record BookImportOutcome(int Added, string Message, string Problem = "");

/// <summary>
/// Bringing another PC's score book into this one, from a folder a person picked.
/// <para>
/// The folder is that machine's data folder or its <c>scorebook</c> — both are accepted, because "the Ur Score
/// folder" is what a person has in hand and which of the two they point at is an implementation detail. Lines are
/// matched to this PC's sources by <see cref="BookMerge"/>, appended through the book's own writer so they land in
/// the same files as everything else, and anything already here is skipped.
/// </para>
/// </summary>
public static class BookImport
{
    public static BookImportOutcome Run(string folder, ISetupServices services)
    {
        var root = ScoreBookRoot(folder);
        if (root is null)
        {
            return new BookImportOutcome(0, "", "There is no score book in that folder. Pick the other PC's 626labs.ur-score folder, the one with a scorebook folder inside it.");
        }

        if (string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(services.Book.Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return new BookImportOutcome(0, "", "That is this PC's own score book.");
        }

        var added = 0;
        var already = 0;
        var notSetUp = new List<string>();
        var noRecipe = new List<string>();

        foreach (var slug in BookFiles.Slugs(root))
        {
            // A recipe this PC hasn't installed has no text to write beside its lines, and no source to match them
            // to either: it is named so it can be imported, not quietly dropped.
            if (services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal)) is not { } installed)
            {
                noRecipe.Add(slug);
                continue;
            }

            var plan = BookMerge.Plan(
                [.. BookFiles.ReadAll(root, slug)],
                services.Sources,
                [.. BookFiles.ReadAll(services.Book.Root, slug)]);

            foreach (var line in plan.Lines) services.Book.Append(line, installed.Text);

            added += plan.Added;
            already += plan.AlreadyHere;
            foreach (var name in plan.NotSetUp.Where(n => !notSetUp.Contains(n, StringComparer.OrdinalIgnoreCase))) notSetUp.Add(name);
        }

        if (added > 0) services.Book.Flush();

        return new BookImportOutcome(added, Said(added, already, notSetUp, noRecipe));
    }

    /// <summary>What happened, in one line, counting nothing twice and hiding nothing that was left out.</summary>
    internal static string Said(int added, int already, IReadOnlyList<string> notSetUp, IReadOnlyList<string> noRecipe)
    {
        var parts = new List<string>
        {
            added switch
            {
                0 => "Nothing new to bring in.",
                1 => "Brought in 1 reading.",
                _ => $"Brought in {added:N0} readings.",
            },
        };

        if (already > 0) parts.Add(already == 1 ? "1 was already here." : $"{already:N0} were already here.");
        if (notSetUp.Count > 0) parts.Add($"Not set up on this PC, so left alone: {string.Join(", ", notSetUp)}.");
        if (noRecipe.Count > 0) parts.Add($"No recipe here for: {string.Join(", ", noRecipe)}.");

        return string.Join(" ", parts);
    }

    /// <summary>The scorebook folder inside what was picked, or the folder itself when that is already it.</summary>
    internal static string? ScoreBookRoot(string folder)
    {
        if (!Directory.Exists(folder)) return null;

        var inside = Path.Combine(folder, "scorebook");
        if (Directory.Exists(inside)) return inside;

        // Picked the scorebook folder itself: it holds a folder per recipe slug, each with jsonl files.
        return Directory.EnumerateDirectories(folder).Any(d => Directory.EnumerateFiles(d, "*.jsonl").Any()) ? folder : null;
    }
}
