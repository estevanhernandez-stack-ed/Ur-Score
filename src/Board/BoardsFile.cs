using System.Globalization;
using System.IO;
using System.Text.Json;
using Labs626.UrScore.Core;

namespace Labs626.UrScore.Board;

/// <summary>What loading <c>boards.json</c> found: a file that isn't there is not the same as one that can't be read.</summary>
public sealed record BoardsLoad(IReadOnlyList<BoardDef> Boards, bool Exists, bool Readable);

/// <summary>
/// <c>boards.json</c> (spec §9.2). Written to a temp file and moved over, so a crash mid-write leaves the old
/// file. Repair is per panel (R4); malformed JSON is unreadable and its text is kept on the next save (R3).
/// </summary>
public sealed class BoardsFile(string path, TimeProvider time)
{
    public static string DefaultPath => AppPaths.Default.Boards;

    public BoardsLoad Load()
    {
        try
        {
            if (!File.Exists(path)) return new BoardsLoad([], Exists: false, Readable: true);
            return new BoardsLoad(Parse(File.ReadAllText(path)), Exists: true, Readable: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new BoardsLoad([], Exists: true, Readable: false);
        }
    }

    /// <summary>
    /// Writes the boards. Returns where the old file was kept, or null. The old file is kept when it doesn't
    /// parse, or, with <paramref name="keepExisting"/>, whatever it holds: a file that couldn't be read at start
    /// may read now, and was still never shown (R3). Throws when the folder can't be written — and also while
    /// READING the old file to decide whether to keep it, if that file is locked at that moment (S2-1.2, S2-1.1):
    /// the caller gets the same "not saved" outcome either way and nothing is lost, since the new boards are only
    /// written after the old file has been dealt with.
    /// </summary>
    public string? Save(IReadOnlyList<BoardDef> boards, bool keepExisting = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var kept = KeepUnreadable(keepExisting);

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(boards));
        File.Move(temp, path, overwrite: true);
        return kept;
    }

    /// <summary>Forwards to <see cref="BoardJson.Serialize"/>, kept for the callers already naming it.</summary>
    public static string Serialize(IReadOnlyList<BoardDef> boards) => BoardJson.Serialize(boards);

    /// <summary>Forwards to <see cref="BoardJson.Parse"/>, kept for the callers already naming it.</summary>
    public static IReadOnlyList<BoardDef> Parse(string json) => BoardJson.Parse(json);

    /// <summary>A file that no longer parses, or any file when asked to keep it, is copied aside before it is written over (R3).</summary>
    private string? KeepUnreadable(bool keepExisting)
    {
        if (!File.Exists(path)) return null;

        var text = File.ReadAllText(path);
        if (!keepExisting && Parses(text)) return null;

        // Once is enough. Save copies the old file and THEN writes, so a write that throws leaves the old file
        // where it was, still unreadable, and the retry arrived here again and made a second copy of the same
        // bytes under a new stamp (S2-5.10). A copy whose contents already sit beside the file is not made
        // twice; the existing one is the answer.
        var folder = Path.GetDirectoryName(path)!;
        foreach (var existing in Directory.EnumerateFiles(folder, "boards.unreadable-*.json").Order(StringComparer.Ordinal).Reverse())
        {
            if (string.Equals(File.ReadAllText(existing), text, StringComparison.Ordinal)) return existing;
        }

        var stamp = time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var copy = Path.Combine(folder, $"boards.unreadable-{stamp}.json");
        File.WriteAllText(copy, text);
        return copy;
    }

    private static bool Parses(string text)
    {
        try
        {
            Parse(text);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }
    }
}
