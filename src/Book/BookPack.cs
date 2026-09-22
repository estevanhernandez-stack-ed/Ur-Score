using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Labs626.UrScore.Book;

/// <summary>What a stats file says about itself: one line, read before anything in it is trusted.</summary>
public sealed record BookPackManifest(int V, DateTimeOffset TakenAt, string App, int Readings, int Finals);

/// <summary>
/// A stats file opened for reading: the folder it was unpacked into, and its manifest, or the one sentence that says
/// why it could not be. <see cref="BookPack.Discard"/> removes the folder when the import is done.
/// </summary>
public sealed record BookPackOpened(string? Folder, BookPackManifest? Manifest, string Problem = "");

/// <summary>
/// The stats file: Export stats writes one, Import stats reads one (2026-09-22, the owner's second machine).
/// <para>
/// It holds the score book — the month files and the recipe texts the book keeps by hash — under a
/// <c>scorebook</c> folder, and a one-line <see cref="ManifestName"/>. Nothing else goes in. Not the keys, which
/// are bound to this user on this machine and could not be read anywhere else; not sources, boards or settings,
/// which are the setup rather than the stats and whose transfer is a design of its own (V3-S.46). So the file is
/// exactly as private as the book, which holds your own accounts alone by construction (<see cref="LineBuilder"/>).
/// </para>
/// <para>
/// Written through a temp file and one move, like every other writer here, so a save that fails leaves no
/// half-file. Read into a fresh folder under the temp path, where <see cref="BookImport"/> then finds a
/// <c>scorebook</c> exactly as it would in a folder somebody copied by hand.
/// </para>
/// </summary>
public static class BookPack
{
    public const int Version = 1;

    public const string ManifestName = "manifest.json";

    public const string Extension = ".zip";

    private const string BookFolder = "scorebook";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The name a save dialog offers: dated, so two exports a week apart are two files.</summary>
    public static string FileName(DateTimeOffset at) => $"ur-score-stats-{at:yyyy-MM-dd}{Extension}";

    /// <summary>Writes the book at <paramref name="bookRoot"/> into a stats file at <paramref name="path"/>. Returns what the manifest says.</summary>
    public static BookPackManifest Write(string bookRoot, string path, string appVersion, DateTimeOffset now)
    {
        var readings = 0;
        var finals = 0;
        foreach (var slug in BookFiles.Slugs(bookRoot))
        {
            foreach (var line in BookFiles.ReadAll(bookRoot, slug))
            {
                if (line.Kind == BookLine.KindRead) readings++;
                else if (line.Kind == BookLine.KindFinal) finals++;
            }
        }

        var manifest = new BookPackManifest(Version, now, appVersion, readings, finals);
        var temp = path + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(ManifestName).Open()))
                {
                    writer.Write(JsonSerializer.Serialize(manifest, Json));
                }

                if (Directory.Exists(bookRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(bookRoot, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(bookRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                        zip.CreateEntryFromFile(file, $"{BookFolder}/{relative}", CompressionLevel.Optimal);
                    }
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        return manifest;
    }

    /// <summary>
    /// Unpacks a stats file into a fresh folder and reads its manifest. Refuses, in words a person can act on, a
    /// file with no manifest, one from a newer Ur Score, and one that cannot be unpacked at all — an entry that
    /// would land outside the folder is one the extractor itself refuses.
    /// </summary>
    public static BookPackOpened Open(string path)
    {
        var folder = Path.Combine(Path.GetTempPath(), "626labs.ur-score", "import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            ZipFile.ExtractToDirectory(path, folder);

            var manifestFile = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifestFile))
            {
                return Refuse(folder, "That is not an Ur Score stats file: it has no manifest.");
            }

            var manifest = JsonSerializer.Deserialize<BookPackManifest>(File.ReadAllText(manifestFile), Json);
            if (manifest is null) return Refuse(folder, "That is not an Ur Score stats file: its manifest could not be read.");
            if (manifest.V > Version)
            {
                return Refuse(folder, $"That stats file was exported by a newer Ur Score ({manifest.App}). Update this one, then import it.");
            }

            return new BookPackOpened(folder, manifest);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return Refuse(folder, $"That stats file could not be read ({ex.GetType().Name}).");
        }
    }

    /// <summary>Removes the folder an <see cref="Open"/> made. Safe to call for a refused open, which has none.</summary>
    public static void Discard(BookPackOpened opened)
    {
        if (opened.Folder is { } folder) Remove(folder);
    }

    private static BookPackOpened Refuse(string folder, string problem)
    {
        Remove(folder);
        return new BookPackOpened(null, null, problem);
    }

    private static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that would not go is a temp folder; nothing of the import depends on it.
        }
    }
}
