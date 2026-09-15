using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class ScoreBookModelTests
{
    private static Recipe Clan => RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json")).Recipe!;

    private static InstalledRecipe Installed => new(Clan, "", new RecipeState());

    private static Source ClanSource(string id, string clan, bool enabled = true) =>
        new(id, Clan.Slug, new Dictionary<string, string> { ["clan"] = clan }, SourceRole.Main, enabled);

    private static RecipeSnapshot Snapshot(bool recorded, string? reason) =>
        new(WatchState.Reporting, null, [], [], 1) { Recorded = recorded, NotRecordingReason = reason, SourceId = "s-00000001" };

    [Theory]
    [InlineData(512L, "512 bytes")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(262144L, "256 KB")]
    [InlineData(5505024L, "5.25 MB")]
    public void SizeReadsInBytesKilobytesOrMegabytes(long bytes, string text) => Assert.Equal(text, ScoreBookModel.Size(bytes));

    [Fact]
    public void StoppedSourcesSayStopped()
    {
        var items = ScoreBookModel.NotRecording([Installed], [ClanSource("s-00000001", "CCGP")],
            new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Snapshot(true, null) }, running: false, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Stopped. Press Start on the board."), Assert.Single(items));
    }

    [Fact]
    public void ARunningSourceGivesItsOwnReasonAndARecordingOneIsNotListed()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP"), ClanSource("s-00000002", "K0i2")];
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            ["s-00000001"] = Snapshot(false, "The battle ended and its final is saved."),
            ["s-00000002"] = Snapshot(true, null),
        };

        var items = ScoreBookModel.NotRecording([Installed], sources, latest, running: true, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "The battle ended and its final is saved."), Assert.Single(items));
    }

    [Fact]
    public void SwitchedOffNotReadAndNeverListedAreEachSaid()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", enabled: false), ClanSource("s-00000002", "K0i2")];

        var items = ScoreBookModel.NotRecording([Installed], sources, new Dictionary<string, RecipeSnapshot>(), running: true, accountsEverListed: false);

        Assert.Equal(new[]
        {
            new NotRecordingItem("Your accounts", "RoRoRo has never listed your accounts, so nothing per account is kept yet. Start RoRoRo while Ur Score runs."),
            new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Switched off."),
            new NotRecordingItem("K0i2 · Pet Sim 99 clan battle points", "Not read yet."),
        }, items.ToArray());
    }

    [Fact]
    public void AnEmptyBookCountsNothing()
    {
        var reader = new ScoreBookReader(Path.Combine(Path.GetTempPath(), "urscore-empty-" + Guid.NewGuid().ToString("N")), TimeProvider.System);

        var item = Assert.Single(ScoreBookModel.Recipes([Installed], [ClanSource("s-00000001", "CCGP")], reader));

        Assert.Equal(new BookRecipeItem("Pet Sim 99 clan battle points", "0 readings kept", "No reading yet", "0 finished battles kept", "0 bytes"), item);
        Assert.Equal("0 readings kept · No reading yet · 0 finished battles kept · 0 bytes", item.Summary);
    }
}
