using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Labs626.UrScore.Book;

public static class BookFiles
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "626labs.ur-score", "scorebook");

    public static string Hash(string recipeText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipeText)))[..16].ToLowerInvariant();

    public static string MonthFile(string root, string slug, DateTimeOffset t) =>
        Path.Combine(root, slug, t.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");

    public static string RecipeFile(string root, string slug, string hash) => Path.Combine(root, slug, "recipes", hash + ".json");

    /// <summary>Recipe slugs that have a book. A slug is letters, digits and hyphens, so it is always a safe folder name.</summary>
    public static IReadOnlyList<string> Slugs(string root) => Directory.Exists(root)
        ? [.. Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    /// <summary>Every readable line for a slug, oldest file first. A file another program holds open is still read.</summary>
    public static IEnumerable<BookLine> ReadAll(string root, string slug)
    {
        var folder = Path.Combine(root, slug);
        if (!Directory.Exists(folder)) yield break;

        foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl").Order(StringComparer.Ordinal))
        {
            List<string> texts;
            try
            {
                texts = ReadShared(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var text in texts)
            {
                if (BookJson.TryParse(text) is { } line) yield return line;
            }
        }
    }

    public static long Bytes(string root, string slug)
    {
        var folder = Path.Combine(root, slug);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.jsonl").Sum(f => new FileInfo(f).Length) : 0;
    }

    private static List<string> ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var texts = new List<string>();
        while (reader.ReadLine() is { } text)
        {
            if (text.Length > 0) texts.Add(text);
        }

        return texts;
    }
}
