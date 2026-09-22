using System.Text;
using System.Text.RegularExpressions;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class ScoreBookTests
{
    private const string Slug = "pet-sim-99-clan-battle-points";

    private static BookLine Line(DateTimeOffset t, string kind = BookLine.KindRead, double points = 12418220) => new(
        BookLine.Version, kind, t, -300, BookLine.TriggerTimer,
        new BookRecipeRef(Slug, BookFiles.Hash("recipe text")), "s-7f3a0001", "mine",
        new Dictionary<string, string> { ["clan"] = "K0i2" },
        new BookPeriod("ArcadeBattle2026", new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), null),
        new Dictionary<string, double> { ["clan-place"] = 212, ["clan-points"] = 18902110 },
        ["value"],
        new Dictionary<string, BookAccount>
        {
            ["101"] = new(new Dictionary<string, double> { ["value"] = points }, new Dictionary<string, int> { ["value"] = 1 }, 48),
        });

    private static readonly DateTimeOffset T = new(2026, 9, 19, 18, 3, 0, 412, TimeSpan.Zero);

    [Fact]
    public void ALineSerializesToTheSpecShapeAndParsesBack()
    {
        var json = BookJson.Serialize(Line(T));

        Assert.StartsWith("""{"v":1,"kind":"read","t":"2026-09-19T18:03:00.412Z","off":-300,"trigger":"timer",""", json);
        Assert.Contains("\"recipe\":{\"slug\":\"pet-sim-99-clan-battle-points\",\"hash\":\"", json);
        Assert.Contains("\"period\":{\"value\":\"ArcadeBattle2026\",\"starts\":\"2026-08-29T18:00:00.000Z\"}", json);
        Assert.Contains("\"accounts\":{\"101\":{\"v\":{\"value\":12418220},\"rank\":{\"value\":1},\"of\":48}}", json);
        Assert.DoesNotContain("unavail", json);
        Assert.DoesNotContain("\n", json);

        var back = BookJson.TryParse(json)!;
        Assert.Equal((T, "K0i2", 12418220d, 48), (back.T, back.Inputs["clan"], back.Accounts["101"].V["value"], back.Accounts["101"].Of!.Value));
        // A line written before a rank kept its own field (backlog S1-6.9) has none, and reads as having none.
        Assert.Null(back.Accounts["101"].Ranked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("""{"v":2,"kind":"read"}""")]
    [InlineData("{\"v\":1,\"kind\":\"read\"\0}")]
    public void ALineThatIsBrokenOrFromANewerFormatIsSkipped(string text) => Assert.Null(BookJson.TryParse(text));

    [Theory]
    [InlineData(BookLine.KindRead, @"""inputs"":\{[^}]*\}", @"""inputs"":{""clan"":null}")]
    [InlineData(BookLine.KindFinal, @"""inputs"":\{[^}]*\}", @"""inputs"":{""clan"":null}")]
    [InlineData(BookLine.KindRead, @"""accounts"":\{.*\}\}$", @"""accounts"":{""1"":{}}}")]
    [InlineData(BookLine.KindFinal, @"""accounts"":\{.*\}\}$", @"""accounts"":{""1"":null}}")]
    [InlineData(BookLine.KindRead, @"""recipe"":\{[^}]*\}", @"""recipe"":{}")]
    [InlineData(BookLine.KindRead, @"""hash"":""[0-9a-f]+""", @"""hash"":""""")]
    [InlineData(BookLine.KindRead, @"""stats"":\[[^\]]*\]", @"""stats"":[""value"",null]")]
    [InlineData(BookLine.KindFinal, @"""period"":\{[^}]*\}", @"""period"":{""value"":""""}")]
    [InlineData(BookLine.KindFinal, @"""period"":\{[^}]*\},", "")]
    public void ALineWithANestedNullOrAMissingPartIsSkipped(string kind, string pattern, string replacement)
    {
        var good = BookJson.Serialize(Line(T, kind));
        var broken = Regex.Replace(good, pattern, replacement);

        Assert.NotNull(BookJson.TryParse(good));
        Assert.NotEqual(good, broken);
        Assert.Null(BookJson.TryParse(broken));
    }

    [Fact]
    public void TheHashIsSixteenLowercaseHexDigitsOfTheRecipeText()
    {
        Assert.Matches("^[0-9a-f]{16}$", BookFiles.Hash("recipe text"));
        Assert.NotEqual(BookFiles.Hash("recipe text"), BookFiles.Hash("recipe text 2"));
    }

    [Fact]
    public void EachAppendIsOneLineAndTheRecipeTextIsKeptOncePerHash()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);

        book.Append(Line(T), "recipe text");
        book.Append(Line(T.AddMinutes(3)), "recipe text");

        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Assert.Equal(2, File.ReadAllLines(file).Length);
        // Exactly one file and no .tmp beside it: the recipe text goes through a temp file and one move now, like
        // every other file this app writes, so a crash mid-write cannot leave a torn recipe under a name that,
        // carrying the hash, would never be rewritten (S1-F.4).
        Assert.Single(Directory.GetFiles(Path.Combine(dir.Path, Slug, "recipes")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dir.Path, Slug, "recipes"), "*.tmp"));
        Assert.Equal("recipe text", File.ReadAllText(BookFiles.RecipeFile(dir.Path, Slug, BookFiles.Hash("recipe text"))));
        Assert.Equal(2, BookFiles.ReadAll(dir.Path, Slug).Count());
        Assert.Equal(new[] { Slug }, BookFiles.Slugs(dir.Path));
    }

    [Fact]
    public void AHalfWrittenLastLineIsClosedOffAndSkipped()
    {
        using var dir = TempDir.Create("urscore-book");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, BookJson.Serialize(Line(T.AddMinutes(-3))) + "\n{\"v\":1,\"kind\":\"re", new UTF8Encoding(false));

        using var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T), "recipe text");

        Assert.Equal(new[] { T.AddMinutes(-3), T }, BookFiles.ReadAll(dir.Path, Slug).Select(l => l.T).ToArray());
    }

    [Fact]
    public void NulRunsFromAPowerLossAreSkipped()
    {
        using var dir = TempDir.Create("urscore-book");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new string('\0', 40) + "\n" + BookJson.Serialize(Line(T)) + "\n", new UTF8Encoding(false));

        Assert.Single(BookFiles.ReadAll(dir.Path, Slug));
    }

    [Fact]
    public void ALockedFileKeepsTheLineUntilItCanBeWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T.AddMinutes(-3)), "recipe text");

        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            book.Append(Line(T), "recipe text");
            Assert.Equal(1, book.Pending);
        }

        book.Flush();
        Assert.Equal(0, book.Pending);
        Assert.Equal(2, BookFiles.ReadAll(dir.Path, Slug).Count());
    }

    /// <summary>
    /// The limit at fifty rather than five thousand: the rule under test is "the oldest reading goes first and a
    /// final never goes", and the number changes nothing about that. At the real limit this took 24 s, all of it
    /// in the Flush at the end — five thousand lines each synced to disk, the durability the app is right to keep
    /// and a test has no use for (S1-5.6).
    /// </summary>
    [Fact]
    public void PastTheLimitTheOldestReadingsAreDroppedButNeverAFinal()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false, maxPending: 50);
        book.Append(Line(T), "recipe text");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            book.Append(Line(T, BookLine.KindFinal), "recipe text");
            for (var i = 0; i < book.MaxPending + 10; i++) book.Append(Line(T.AddSeconds(i + 1)), "recipe text");

            Assert.Equal(50, book.Pending);
            Assert.Equal(11, book.Dropped);
        }

        book.Flush();
        Assert.Contains(BookFiles.ReadAll(dir.Path, Slug), l => l.Kind == BookLine.KindFinal);
    }

    [Fact]
    public void TheMonthFileFollowsUtc()
    {
        var lateLocal = new DateTimeOffset(2026, 9, 30, 21, 30, 0, TimeSpan.FromHours(-5));
        Assert.EndsWith(Path.Combine(Slug, "2026-10.jsonl"), BookFiles.MonthFile("root", Slug, lateLocal));
    }

    [Fact]
    public void RemovingARecipeLeavesItsBook()
    {
        using var dir = TempDir.Create("urscore-book");
        var recipesRoot = Path.Combine(dir.Path, "recipes");
        var store = new RecipeStore(recipesRoot);
        var text = RecipeParserTests.Fixture("petsim99-clan-battle.recipe.json");
        var recipe = RecipeParser.Parse(text).Recipe!;
        store.Save(recipe, text, new RecipeState());
        var recipeFiles = Directory.GetFiles(recipesRoot);
        Assert.Equal(2, recipeFiles.Length);
        Assert.NotNull(store.Find(recipe.Slug));

        var root = Path.Combine(dir.Path, "scorebook");
        using var book = new ScoreBook(root, background: false);
        var reference = new BookRecipeRef(recipe.Slug, BookFiles.Hash(text));
        var reading = Line(T) with { Recipe = reference };
        var final = Line(T.AddMonths(1), BookLine.KindFinal) with { Recipe = reference };
        book.Append(reading, text);
        book.Append(final, text);
        var keptFiles = new[]
        {
            BookFiles.MonthFile(root, recipe.Slug, reading.T),
            BookFiles.MonthFile(root, recipe.Slug, final.T),
            BookFiles.RecipeFile(root, recipe.Slug, reference.Hash),
        }.ToDictionary(path => path, File.ReadAllBytes);

        Assert.True(store.Remove(recipe.Slug));

        Assert.Null(store.Find(recipe.Slug));
        Assert.All(recipeFiles, path => Assert.False(File.Exists(path)));
        Assert.All(keptFiles, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
        Assert.Equal(new[] { BookJson.Serialize(reading), BookJson.Serialize(final) },
            BookFiles.ReadAll(root, recipe.Slug).OrderBy(line => line.T).Select(BookJson.Serialize));
        Assert.Equal(text, File.ReadAllText(BookFiles.RecipeFile(root, recipe.Slug, reference.Hash)));
    }

    [Fact]
    public async Task TheBackgroundWriterWritesAndRaisesWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        var written = new TaskCompletionSource<BookLine>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var book = new ScoreBook(dir.Path))
        {
            book.Written += line => written.TrySetResult(line);
            book.Append(Line(T), "recipe text");
            var completed = await Task.WhenAny(written.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == written.Task, "the background writer never wrote the line");
        }

        Assert.Single(BookFiles.ReadAll(dir.Path, Slug));
    }

    [Fact]
    public void AThrowingWrittenSubscriberDoesNotStopTheNextLineFromBeingWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);
        book.Written += _ => throw new InvalidOperationException("boom");

        book.Append(Line(T), "recipe text");
        book.Append(Line(T.AddMinutes(3)), "recipe text");

        Assert.Equal(2, BookFiles.ReadAll(dir.Path, Slug).Count());
    }

    [Fact]
    public void ALineWithANonFiniteHeadlineValueIsDroppedAndTheNextLineIsStillWritten()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);

        var broken = Line(T) with { Headline = new Dictionary<string, double> { ["clan-points"] = double.NaN } };
        book.Append(broken, "recipe text");
        Assert.Equal(1, book.Dropped);
        Assert.Equal(0, book.Pending);

        book.Append(Line(T.AddMinutes(3)), "recipe text");

        Assert.Single(BookFiles.ReadAll(dir.Path, Slug));
    }

    [Fact]
    public void ALineWithANullTimeIsSkippedAndReadAllKeepsTheLinesAroundIt()
    {
        var broken = BookJson.Serialize(Line(T)).Replace("\"t\":\"2026-09-19T18:03:00.412Z\"", "\"t\":null");
        Assert.Null(BookJson.TryParse(broken));

        using var dir = TempDir.Create("urscore-book");
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(
            file,
            BookJson.Serialize(Line(T.AddMinutes(-3))) + "\n" + broken + "\n" + BookJson.Serialize(Line(T.AddMinutes(3))) + "\n",
            new UTF8Encoding(false));

        Assert.Equal(new[] { T.AddMinutes(-3), T.AddMinutes(3) }, BookFiles.ReadAll(dir.Path, Slug).Select(l => l.T).ToArray());
    }

    /// <summary>
    /// The writer loop's last-resort backstop. It cannot be reached through <c>Append</c> — <c>Drain</c> already
    /// contains every failure it knows about, so no input makes it throw — which is why it went untested from the
    /// start (S1-5.4). What matters is not the trigger, by definition the one nobody thought of, but the promise:
    /// the loop gets another turn. Losing the writer thread would stop every reading reaching disk with nothing
    /// raising a hand, so this is the difference between a bad hour and a silently empty score book.
    /// </summary>
    [Fact]
    public void AFailureNobodyForesawDoesNotTakeTheWriterDown()
    {
        var turns = 0;

        // Thrown on the first turn only, so the second turn also proves the loop CARRIED ON rather than merely
        // that one call was swallowed: a backstop that ate the exception and then stopped looping would pass a
        // test that only ever took one turn.
        ScoreBook.Turn(() =>
        {
            turns++;
            throw new InvalidOperationException("something Drain has never heard of");
        });
        ScoreBook.Turn(() => turns++);

        Assert.Equal(2, turns);
    }

    /// <summary>
    /// An unwritable line found by the drain is one dropped line. This pins the ordinary case; the race S1-5.1
    /// names cannot be reached from a test, and this says so rather than implying it can. The double count
    /// needed the drain to be holding a node mid-write when an append's overflow on another thread removed
    /// it, so the drain's own "can never be written" branch then counted a line it had nothing left to remove.
    /// That needs the writer thread paused between taking the node and its catch, and the book has no seam
    /// for that. The fix is by inspection — count only what this branch removed — and the mutation that
    /// counts regardless passes this test, which is the honest limit of what it checks. Sweep E's timer and
    /// gate work is where a seam would come from, if one is ever worth the code it adds to a writer loop.
    /// </summary>
    [Fact]
    public void AnUnwritableLineFoundByTheDrainIsOneDroppedLine()
    {
        using var dir = TempDir.Create("urscore-book");
        using var book = new ScoreBook(dir.Path, background: false);
        var unwritable = Line(T) with { Headline = new Dictionary<string, double> { ["clan-place"] = double.NaN } };

        // Held: the first append cannot drain while the month file is locked, so the line stays pending.
        var file = BookFiles.MonthFile(dir.Path, Slug, T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            book.Append(unwritable, "recipe text");
        }

        // Now the drain runs and finds it unwritable: dropped once, by that branch.
        book.Flush();
        Assert.Equal(1, book.Dropped);
        Assert.Equal(0, book.Pending);
    }

    [Fact]
    public void DisposingTwiceDoesNotThrow()
    {
        using var dir = TempDir.Create("urscore-book");
        var book = new ScoreBook(dir.Path, background: false);
        book.Append(Line(T), "recipe text");

        book.Dispose();
        book.Dispose();
    }
}
