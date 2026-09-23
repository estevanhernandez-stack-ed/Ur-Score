using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

public sealed record BookRecipeItem(string Name, string Readings, string First, string Finals, string Size)
{
    public string Summary => string.Join(" · ", new[] { Readings, First, Finals, Size }.Where(part => part.Length > 0));
}

public sealed record NotRecordingItem(string Source, string Reason);

/// <summary>
/// Setup › Score book (spec §7.6). Counts only; no line of the book is ever shown or copied.
/// <para>
/// A clans list is listed here from 0.5.3. It kept nothing when this page was written and was left out, then
/// started keeping the field's numbers and the top clans by name in 0.3.10 without ever appearing — so the one
/// thing growing fastest on disk was the one thing this page neither measured nor offered to clear. The owner's
/// direction, 2026-09-20: we are holding it, and they should be in control of how it is held.
/// </para>
/// </summary>
public static class ScoreBookModel
{
    /// <summary>
    /// Open folder didn't open it (backlog S1-12.4). Said on its own line, which no redraw touches, in plain words for any cause:
    /// the path is on the page, so you can open it yourself. The page puts the exception's type in the trail.
    /// </summary>
    public static string FolderNotOpened(Exception ex) => "Ur Score couldn't open the folder. Its path is above, so you can open it yourself.";

    /// <summary>What the book is still waiting to write and has dropped, or "" while it is neither.</summary>
    public static string PendingLine(int pending, int dropped) =>
        pending == 0 && dropped == 0
            ? ""
            : $"{pending} lines are waiting to be written, and {dropped} readings were dropped because the file couldn't be written.";

    /// <summary>
    /// What Export stats did, said beside the button: the counts from the file's own manifest, and the file's name.
    /// With a setup, the recipes, clans and boards it carried are said too, so the line matches what the other PC's
    /// preview will offer.
    /// </summary>
    public static string ExportedLine(BookPackManifest manifest, string fileName, SetupPack? setup)
    {
        var readings = manifest.Readings == 1 ? "1 reading" : $"{manifest.Readings:N0} readings";
        var finals = manifest.Finals == 1 ? "1 finished battle" : $"{manifest.Finals:N0} finished battles";
        var setupPart = setup is null ? "" : $", with {Count(setup.Recipes.Count, "recipe")}, {Count(setup.Sources.Count, "clan")} and {Count(setup.Boards.Count, "board")},";
        return $"Exported {readings} and {finals}{setupPart} to {fileName}. Import it on the other PC from Setup › Score book.";
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes"
        : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : (bytes / 1048576.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";

    public static string SourceLabel(Recipe recipe, Source source) =>
        recipe.Inputs.Count == 0 ? recipe.Name : $"{ClansModel.NameOf(recipe, source)} · {recipe.Name}";

    public static IReadOnlyList<BookRecipeItem> Recipes(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, ScoreBookReader reader) =>
        [.. installed.Select(i =>
        {
            var recipe = i.Recipe;
            var slug = recipe.Slug;
            var readings = reader.Readings(slug);
            var first = reader.FirstReading(slug);
            var finals = sources
                .Where(s => string.Equals(s.Recipe, slug, StringComparison.Ordinal))
                .Select(s => s.InputsKey)
                .Distinct(StringComparer.Ordinal)
                .Sum(key => reader.Finals(slug, key).Select(f => f.Period).Distinct(StringComparer.Ordinal).Count());

            return new BookRecipeItem(
                recipe.Name,
                readings == 1 ? "1 reading kept" : $"{readings.ToString("N0", CultureInfo.InvariantCulture)} readings kept",
                first is { } at ? $"First reading {at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture)}" : "No reading yet",
                // A clans list keeps no finals — it has no account to close a period for — so the row doesn't
                // offer a count that could only ever be zero.
                recipe.Period is null || recipe.IsGroupList ? ""
                    : finals == 1 ? $"1 finished {RecipeWords.Period(recipe)} kept"
                    : $"{finals.ToString("N0", CultureInfo.InvariantCulture)} finished {RecipeWords.Periods(recipe)} kept",
                Size(reader.Bytes(slug)));
        })];

    /// <summary>
    /// Every source that isn't recording right now, and why (spec §7.6). A clans list is one of them from 0.5.3:
    /// it records, so it can fail to, and the page that explains a silence should explain that one too.
    /// </summary>
    public static IReadOnlyList<NotRecordingItem> NotRecording(
        IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, IReadOnlyDictionary<string, RecipeSnapshot> latest,
        bool running, bool accountsEverListed)
    {
        var items = new List<NotRecordingItem>();

        if (!accountsEverListed && sources.Any(s => s.Enabled && s.Role != SourceRole.Watch))
        {
            items.Add(new NotRecordingItem("Your accounts",
                "RoRoRo has never listed your accounts, so nothing per account is kept yet. Start RoRoRo while Ur Score runs."));
        }

        foreach (var source in sources)
        {
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe)
            {
                continue;
            }

            var snapshot = latest.GetValueOrDefault(source.Id);
            var reason =
                !source.Enabled ? "Switched off."
                : !running ? "Paused. Resume from the status chip on the board."
                : snapshot is null ? "Not read yet."
                : snapshot.Recorded ? null
                : snapshot.NotRecordingReason ?? "The last read kept nothing.";

            if (reason is not null) items.Add(new NotRecordingItem(SourceLabel(recipe, source), reason));
        }

        return items;
    }
}
