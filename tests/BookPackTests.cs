using System.IO.Compression;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// The stats file: Export stats writes one, Import stats reads one. It holds the score book and a one-line
/// manifest, nothing else — no keys (they cannot leave this user and machine), no sources, boards or settings —
/// so it is exactly as private as the book, which holds your own accounts alone by construction.
/// </summary>
public class BookPackTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARoundTripReturnsEveryLineAndTheManifestCountsThem()
    {
        using var dir = TempDir.Create("urscore-pack");
        var book = Path.Combine(dir.Path, "scorebook");
        var written = BookGenerator.Write(book, clanSources: 2, days: 9);
        var file = Path.Combine(dir.Path, BookPack.FileName(Now));

        var manifest = BookPack.Write(book, file, "0.5.4", Now);
        var opened = BookPack.Open(file);

        Assert.Equal((BookPack.Version, "0.5.4", Now), (manifest.V, manifest.App, manifest.TakenAt));
        Assert.Equal(written.Lines - written.Finals, manifest.Readings);
        Assert.Equal(written.Finals, manifest.Finals);
        Assert.Equal("", opened.Problem);
        Assert.Equal(manifest, opened.Manifest);
        var root = BookImport.ScoreBookRoot(opened.Folder!);
        Assert.NotNull(root);
        foreach (var slug in BookFiles.Slugs(book))
        {
            Assert.Equal(
                BookFiles.ReadAll(book, slug).Select(BookJson.Serialize),
                BookFiles.ReadAll(root, slug).Select(BookJson.Serialize));
        }

        Assert.Equal("ur-score-stats-2026-09-22.zip", Path.GetFileName(file));
        Assert.False(File.Exists(file + ".tmp"));
        BookPack.Discard(opened);
        Assert.False(Directory.Exists(opened.Folder));
    }

    /// <summary>
    /// An entry that would land outside the folder it is unpacked into is refused, and nothing is written there.
    /// The extractor refuses such an entry itself; this pins that the refusal reaches the person as words and not
    /// as an exception, and that the temp folder does not survive it.
    /// </summary>
    [Fact]
    public void AnEntryThatEscapesTheFolderIsRefused()
    {
        using var dir = TempDir.Create("urscore-pack");
        var file = Path.Combine(dir.Path, "escape.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            WriteEntry(zip, BookPack.ManifestName, """{"v":1,"takenAt":"2026-09-22T12:00:00+00:00","app":"0.5.4","readings":0,"finals":0}""");
            WriteEntry(zip, "../escaped.txt", "not yours");
        }

        var opened = BookPack.Open(file);

        Assert.Null(opened.Folder);
        Assert.Contains("could not be read", opened.Problem, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escaped.txt")));
    }

    [Fact]
    public void AFileWithNoManifestOrFromANewerUrScoreIsRefusedInPlainWords()
    {
        using var dir = TempDir.Create("urscore-pack");
        var noManifest = Path.Combine(dir.Path, "no-manifest.zip");
        using (var zip = ZipFile.Open(noManifest, ZipArchiveMode.Create)) WriteEntry(zip, "scorebook/readme.txt", "");
        var newer = Path.Combine(dir.Path, "newer.zip");
        using (var zip = ZipFile.Open(newer, ZipArchiveMode.Create))
        {
            WriteEntry(zip, BookPack.ManifestName, """{"v":3,"takenAt":"2026-09-22T12:00:00+00:00","app":"9.0.0","readings":0,"finals":0}""");
        }

        var first = BookPack.Open(noManifest);
        var second = BookPack.Open(newer);

        Assert.Null(first.Folder);
        Assert.Equal("That is not an Ur Score stats file: it has no manifest.", first.Problem);
        Assert.Null(second.Folder);
        Assert.Equal("That stats file was exported by a newer Ur Score (9.0.0). Update this one, then import it.", second.Problem);
    }

    /// <summary>A save that fails leaves no half-written file and no temp file beside it.</summary>
    [Fact]
    public void AFailedWriteLeavesNoHalfFile()
    {
        using var dir = TempDir.Create("urscore-pack");
        var book = Path.Combine(dir.Path, "scorebook");
        BookGenerator.Write(book, clanSources: 1, days: 1);
        var file = Path.Combine(dir.Path, "stats.zip");
        // A folder standing where the file must go: the final move cannot succeed.
        Directory.CreateDirectory(file);

        var ex = Record.Exception(() => BookPack.Write(book, file, "0.5.4", Now));
        Assert.True(ex is IOException or UnauthorizedAccessException, $"expected a file-system failure, got {ex?.GetType().Name ?? "nothing"}");

        Assert.True(Directory.Exists(file));
        Assert.Empty(Directory.EnumerateFiles(dir.Path, "*.tmp"));
    }

    /// <summary>Import stats takes the file; a month file picked inside a folder someone copied by hand still finds the book.</summary>
    [Fact]
    public void AStatsFileOrAMonthFileInsideACopiedFolderBothFindTheBook()
    {
        using var dir = TempDir.Create("urscore-pack");
        var book = Path.Combine(dir.Path, "626labs.ur-score", "scorebook");
        BookGenerator.Write(book, clanSources: 1, days: 1);
        var month = Directory.EnumerateFiles(Path.Combine(book, BookGenerator.ClanSlug), "*.jsonl").First();

        Assert.Equal(book, BookImport.ScoreBookRootOfPickedFile(month));
        Assert.Null(BookImport.ScoreBookRootOfPickedFile(Path.Combine(dir.Path, "nothing.txt")));
        // A .jsonl that is not in a book: a stray one two levels under a folder that is not a scorebook.
        var stray = Path.Combine(dir.Path, "stray", "deeper", "2026-09.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        File.WriteAllText(stray, "{}" + Environment.NewLine);
        Assert.Null(BookImport.ScoreBookRootOfPickedFile(stray));
    }

    /// <summary>A file with a setup in it carries it back out; the manifest says so; a v:1 file (0.5.5) opens as stats only.</summary>
    [Fact]
    public void ASetupTravelsWithTheStatsAndAStatsOnlyFileStillOpens()
    {
        using var dir = TempDir.Create("urscore-pack");
        var book = Path.Combine(dir.Path, "scorebook");
        BookGenerator.Write(book, clanSources: 1, days: 1);
        var setup = new SetupPack([], [BoardFixtures.MainClan], [], Labs626.UrScore.Core.Settings.Defaults, []);
        var withSetup = Path.Combine(dir.Path, "with.zip");
        var without = Path.Combine(dir.Path, "without.zip");

        var manifest = BookPack.Write(book, withSetup, "0.5.6", Now, setup);
        var plain = BookPack.Write(book, without, "0.5.6", Now);
        var opened = BookPack.Open(withSetup);
        var openedPlain = BookPack.Open(without);

        Assert.Equal((2, true), (manifest.V, manifest.Setup));
        Assert.False(plain.Setup);
        Assert.NotNull(opened.Setup);
        var back = Assert.Single(opened.Setup!.Sources);
        Assert.Equal(
            (BoardFixtures.MainClan.Id, BoardFixtures.MainClan.Recipe, BoardFixtures.MainClan.Role, BoardFixtures.MainClan.InputsKey),
            (back.Id, back.Recipe, back.Role, back.InputsKey));
        Assert.Null(openedPlain.Setup);
        BookPack.Discard(opened);
        BookPack.Discard(openedPlain);
    }

    [Fact]
    public void AFileFromTheVersionBeforeOpensAsStatsOnly()
    {
        using var dir = TempDir.Create("urscore-pack");
        var file = Path.Combine(dir.Path, "old.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            WriteEntry(zip, BookPack.ManifestName, """{"v":1,"takenAt":"2026-09-22T12:00:00+00:00","app":"0.5.5","readings":0,"finals":0}""");
        }

        var opened = BookPack.Open(file);

        Assert.Equal("", opened.Problem);
        Assert.NotNull(opened.Folder);
        Assert.Null(opened.Setup);
        Assert.False(opened.Manifest!.Setup);
        BookPack.Discard(opened);
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
