using Labs626.UrScore.Book;

namespace UrScore.Tests;

/// <summary>
/// What a person meets when they bring another PC's book in: the folder they have in hand, and one line saying what
/// happened to it. Nothing that was left out is left unsaid.
/// </summary>
public class BookImportTests
{
    [Fact]
    public void EitherFolderIsTheRightFolder()
    {
        using var dir = TempDir.Create("urscore-import");
        var data = Directory.CreateDirectory(Path.Combine(dir.Path, "626labs.ur-score")).FullName;
        var book = Directory.CreateDirectory(Path.Combine(data, "scorebook", "pet-sim-99-clan-battle-points")).FullName;
        File.WriteAllText(Path.Combine(book, "2026-09.jsonl"), "{}\n");

        // The data folder, which is what "the Ur Score folder" means to a person...
        Assert.Equal(Path.Combine(data, "scorebook"), BookImport.ScoreBookRoot(data));

        // ...and the scorebook folder itself, if that is what they picked.
        Assert.Equal(Path.Combine(data, "scorebook"), BookImport.ScoreBookRoot(Path.Combine(data, "scorebook")));
    }

    [Fact]
    public void AFolderWithNoBookIsNotOne()
    {
        using var dir = TempDir.Create("urscore-import");

        Assert.Null(BookImport.ScoreBookRoot(dir.Path));
        Assert.Null(BookImport.ScoreBookRoot(Path.Combine(dir.Path, "nope")));
    }

    [Fact]
    public void TheLineSaysWhatHappenedToEveryPart()
    {
        Assert.Equal("Imported 1,204 readings. 310 were already here.", BookImport.Said(1204, 310, [], []));
        Assert.Equal("Imported 1 reading.", BookImport.Said(1, 0, [], []));
        Assert.Equal("Nothing new to import. 12 were already here.", BookImport.Said(0, 12, [], []));

        Assert.Equal(
            "Imported 40 readings. Not set up on this PC, so left alone: CCGP, K0i2.",
            BookImport.Said(40, 0, ["CCGP", "K0i2"], []));

        Assert.Equal(
            "Nothing new to import. No recipe here for: pet-sim-99-profile.",
            BookImport.Said(0, 0, [], ["pet-sim-99-profile"]));
    }
}
