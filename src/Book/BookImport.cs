using System.IO;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.Book;

/// <summary>What bringing in another PC's book did, in the words the page shows.</summary>
public sealed record BookImportOutcome(int Added, string Message, string Problem = "");

/// <summary>
/// Importing another PC's score book into this one: from the stats file Export stats wrote there, or from a folder
/// somebody copied by hand.
/// <para>
/// The file is a <see cref="BookPack"/>; it is unpacked, imported and the unpacked copy removed. The folder is that
/// machine's data folder or its <c>scorebook</c> — both are accepted, because "the Ur Score folder" is what a person
/// has in hand and which of the two they point at is an implementation detail; and since the file dialog cannot pick
/// a folder, a month file inside one finds it (<see cref="ScoreBookRootOfPickedFile"/>). Lines are matched to this
/// PC's sources by <see cref="BookMerge"/>, appended through the book's own writer so they land in the same files as
/// everything else, and anything already here is skipped.
/// </para>
/// </summary>
public static class BookImport
{
    /// <summary>What a person picked in the dialog: a stats file, or any file inside a copied folder.</summary>
    public static BookImportOutcome RunFile(string path, ISetupServices services)
    {
        if (string.Equals(Path.GetExtension(path), BookPack.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var opened = BookPack.Open(path);
            try
            {
                return opened.Folder is null ? new BookImportOutcome(0, "", opened.Problem) : Run(opened.Folder, services);
            }
            finally
            {
                BookPack.Discard(opened);
            }
        }

        return ScoreBookRootOfPickedFile(path) is { } root
            ? Run(root, services)
            : new BookImportOutcome(0, "", "That is not an Ur Score stats file, and no score book was found around it. Pick the file Export stats made on the other PC, or a month file inside a copied 626labs.ur-score folder.");
    }

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
        var finals = 0;
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
            finals += plan.Lines.Count(l => l.Kind == BookLine.KindFinal);
            already += plan.AlreadyHere;
            foreach (var name in plan.NotSetUp.Where(n => !notSetUp.Contains(n, StringComparer.OrdinalIgnoreCase))) notSetUp.Add(name);
        }

        if (added > 0) services.Book.Flush();

        return new BookImportOutcome(added, Said(added - finals, already, notSetUp, noRecipe, finals));
    }

    /// <summary>What happened, in one line, counting nothing twice and hiding nothing that was left out.</summary>
    internal static string Said(int readings, int already, IReadOnlyList<string> notSetUp, IReadOnlyList<string> noRecipe, int finals = 0)
    {
        // Readings and finished battles are counted apart, as the manifest and the preview count them.
        var what = new List<string>();
        if (readings > 0) what.Add(readings == 1 ? "1 reading" : $"{readings:N0} readings");
        if (finals > 0) what.Add(finals == 1 ? "1 finished battle" : $"{finals:N0} finished battles");
        var parts = new List<string> { what.Count == 0 ? "Nothing new to import." : $"Imported {string.Join(" and ", what)}." };

        if (already > 0) parts.Add(already == 1 ? "1 was already here." : $"{already:N0} were already here.");
        if (notSetUp.Count > 0) parts.Add($"Not set up on this PC, so left alone: {string.Join(", ", notSetUp)}.");
        if (noRecipe.Count > 0) parts.Add($"No recipe here for: {string.Join(", ", noRecipe)}.");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// The scorebook folder a picked month file sits in: a copied folder keeps the app's own layout, so the file
    /// is in a slug folder inside a folder named <c>scorebook</c>, and nothing else is looked at. Not a shape
    /// check on the folders around it — a stray month file's parent's parent could be a temp folder, and
    /// enumerating that to see whether it "looks like a book" is slow and can say yes. Null for anything else.
    /// </summary>
    internal static string? ScoreBookRootOfPickedFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase)) return null;

        var slugFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        var root = slugFolder is null ? null : Path.GetDirectoryName(slugFolder);
        return root is not null && string.Equals(Path.GetFileName(root), "scorebook", StringComparison.OrdinalIgnoreCase) ? root : null;
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
