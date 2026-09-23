using Labs626.UrScore.Board;
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

    /// <summary>What Export stats says beside the button, from the file's own manifest, with the file's name and where to go next.</summary>
    [Theory]
    [InlineData(1, 1, null, "Exported 1 reading and 1 finished battle to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book.")]
    [InlineData(1, 1, "1|1|1", "Exported 1 reading and 1 finished battle, with 1 recipe, 1 clan and 1 board, to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book.")]
    [InlineData(16_800, 0, "3|5|2", "Exported 16,800 readings and 0 finished battles, with 3 recipes, 5 clans and 2 boards, to ur-score-stats-2026-09-22.zip. Import it on the other PC from Setup › Score book.")]
    public void TheExportedLineCountsWhatWentIntoTheFile(int readings, int finals, string? setup, string expected)
    {
        SetupPack? pack = null;
        if (setup is not null)
        {
            var n = setup.Split('|').Select(int.Parse).ToArray();
            pack = new SetupPack(
                [.. Enumerable.Range(0, n[0]).Select(i => new SetupRecipe($"r{i}", $"R{i}", "", new RecipeState(), []))],
                [.. Enumerable.Range(0, n[1]).Select(i => new Source($"s-{i:x8}", "r0", new Dictionary<string, string>(), SourceRole.Mine))],
                [.. Enumerable.Range(0, n[2]).Select(i => new BoardDef($"b-{i}", $"B{i}", []))],
                Settings.Defaults, []);
        }

        Assert.Equal(expected, ScoreBookModel.ExportedLine(
            new BookPackManifest(BookPack.Version, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), "0.5.6", readings, finals, pack is not null),
            "ur-score-stats-2026-09-22.zip", pack));
    }

    [Theory]
    [InlineData(512L, "512 bytes")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(262144L, "256 KB")]
    [InlineData(5505024L, "5.25 MB")]
    public void SizeReadsInBytesKilobytesOrMegabytes(long bytes, string text) => Assert.Equal(text, ScoreBookModel.Size(bytes));

    /// <summary>Started this session, then paused: there is something to resume, so the text offers to.</summary>
    [Fact]
    public void StoppedSourcesSayStopped()
    {
        var items = ScoreBookModel.NotRecording([Installed], [ClanSource("s-00000001", "CCGP")],
            new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Snapshot(true, null) }, running: false, everStarted: true, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Paused. Resume from the status chip on the board."), Assert.Single(items));
    }

    /// <summary>
    /// Never started this session (e.g. only a Test now read has run): nothing has been paused, so the text says
    /// Start, not Resume, mirroring BoardText.StateLine's everStarted split (2026-09-23 review).
    /// </summary>
    [Fact]
    public void NeverStartedSourcesSayNotStartedRatherThanPaused()
    {
        var items = ScoreBookModel.NotRecording([Installed], [ClanSource("s-00000001", "CCGP")],
            new Dictionary<string, RecipeSnapshot> { ["s-00000001"] = Snapshot(true, null) }, running: false, everStarted: false, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "Not started. Start reading from the status chip on the board."), Assert.Single(items));
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

        var items = ScoreBookModel.NotRecording([Installed], sources, latest, running: true, everStarted: true, accountsEverListed: true);

        Assert.Equal(new NotRecordingItem("CCGP · Pet Sim 99 clan battle points", "The battle ended and its final is saved."), Assert.Single(items));
    }

    [Fact]
    public void SwitchedOffNotReadAndNeverListedAreEachSaid()
    {
        Source[] sources = [ClanSource("s-00000001", "CCGP", enabled: false), ClanSource("s-00000002", "K0i2")];

        var items = ScoreBookModel.NotRecording([Installed], sources, new Dictionary<string, RecipeSnapshot>(), running: true, everStarted: true, accountsEverListed: false);

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

    /// <summary>
    /// A clans list is on this page from 0.5.3. It kept nothing when the page was written and was left out; it has
    /// kept the field's numbers and the top clans by name since 0.3.10, so the thing growing fastest on disk was
    /// the one thing the page neither measured nor offered to clear. No finals count: it closes no period, so the
    /// row does not carry a number that could only ever be zero.
    /// </summary>
    [Fact]
    public void AClansListIsMeasuredLikeAnythingElseYouKeep()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var list = new InstalledRecipe(clans, "", new RecipeState());
        var field = new Source("s-00000009", clans.Slug, new Dictionary<string, string>(), SourceRole.Watch);
        var reader = new ScoreBookReader(Path.Combine(Path.GetTempPath(), "urscore-empty-" + Guid.NewGuid().ToString("N")), TimeProvider.System);

        var item = Assert.Single(ScoreBookModel.Recipes([list], [field], reader));

        Assert.Equal(new BookRecipeItem("Pet Sim 99 top clans", "0 readings kept", "No reading yet", "", "0 bytes"), item);
        Assert.Equal("0 readings kept · No reading yet · 0 bytes", item.Summary);
    }

    /// <summary>A clans list records, so it can fail to — and the page that explains a silence explains that one too.</summary>
    [Fact]
    public void AClansListThatKeptNothingSaysWhyLikeAnySource()
    {
        var clans = RecipeParser.Parse(RecipeParserTests.Fixture("petsim99-top-clans.recipe.json")).Recipe!;
        var list = new InstalledRecipe(clans, "", new RecipeState());
        var field = new Source("s-00000009", clans.Slug, new Dictionary<string, string>(), SourceRole.Watch);
        var latest = new Dictionary<string, RecipeSnapshot> { [field.Id] = Snapshot(false, "This read brought no clan with a number, so there was nothing to keep.") };

        var items = ScoreBookModel.NotRecording([list], [field], latest, running: true, everStarted: true, accountsEverListed: true);

        Assert.Equal(
            new NotRecordingItem("Pet Sim 99 top clans", "This read brought no clan with a number, so there was nothing to keep."),
            Assert.Single(items));
    }

    /// <summary>
    /// Backlog S1-12.4. "Could not open the folder" was written onto the line the page's redraw owns for pending lines, so the
    /// next redraw (any read, any book line) wiped it. It has a line of its own now, and says what you can do instead in plain
    /// words; the exception's own message (a path, a Win32 code) goes to the trail as its type.
    /// </summary>
    [Fact]
    public void AFolderThatCouldNotBeOpenedSaysSoInPlainWords()
    {
        var said = ScoreBookModel.FolderNotOpened(new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified."));

        Assert.Equal("Ur Score couldn't open the folder. Its path is above, so you can open it yourself.", said);
        Assert.Equal(said, ScoreBookModel.FolderNotOpened(new UnauthorizedAccessException("Access to the path 'C:\\x' is denied.")));
    }

    [Fact]
    public void ThePendingLineSaysNothingWhileNothingIsWaitingOrDropped()
    {
        Assert.Equal("", ScoreBookModel.PendingLine(0, 0));
        Assert.Equal("3 lines are waiting to be written, and 1 readings were dropped because the file couldn't be written.", ScoreBookModel.PendingLine(3, 1));
    }
}
