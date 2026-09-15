using System.Globalization;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

public sealed record BookRecipeItem(string Name, string Readings, string First, string Finals, string Size)
{
    public string Summary => string.Join(" · ", new[] { Readings, First, Finals, Size }.Where(part => part.Length > 0));
}

public sealed record NotRecordingItem(string Source, string Reason);

/// <summary>Setup › Score book (spec §7.6). Counts only; no line of the book is ever shown or copied.</summary>
public static class ScoreBookModel
{
    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} bytes"
        : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
        : (bytes / 1048576.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";

    public static string SourceLabel(Recipe recipe, Source source) =>
        recipe.Inputs.Count == 0 ? recipe.Name : $"{ClansModel.NameOf(recipe, source)} · {recipe.Name}";

    public static IReadOnlyList<BookRecipeItem> Recipes(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, ScoreBookReader reader) =>
        [.. installed.Where(i => !i.Recipe.IsGroupList).Select(i =>
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
                recipe.Period is null ? ""
                    : finals == 1 ? $"1 finished {RecipeWords.Period(recipe)} kept"
                    : $"{finals.ToString("N0", CultureInfo.InvariantCulture)} finished {RecipeWords.Periods(recipe)} kept",
                Size(reader.Bytes(slug)));
        })];

    /// <summary>Every source that isn't recording right now, and why (spec §7.6). Group lists never record and are not listed.</summary>
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
            if (installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, source.Recipe, StringComparison.Ordinal))?.Recipe is not { } recipe
                || recipe.IsGroupList)
            {
                continue;
            }

            var snapshot = latest.GetValueOrDefault(source.Id);
            var reason =
                !source.Enabled ? "Switched off."
                : !running ? "Stopped. Press Start on the board."
                : snapshot is null ? "Not read yet."
                : snapshot.Recorded ? null
                : snapshot.NotRecordingReason ?? "The last read kept nothing.";

            if (reason is not null) items.Add(new NotRecordingItem(SourceLabel(recipe, source), reason));
        }

        return items;
    }
}
