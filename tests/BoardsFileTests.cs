using System.Text.Json;
using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class BoardsFileTests
{
    private static readonly Source MainClan = SourceOf("s-00000001", Clan, "CCGP", SourceRole.Main);
    private static readonly Source AltClan = SourceOf("s-00000002", Clan, "K0i2", SourceRole.Mine);

    private static BoardDef Battle() => new("b-0000aaaa", "Battle",
    [
        new PanelDef("p-00000001", PanelType.Standing, new PanelSize(3), new PanelSettings(Clan.Slug, SourceId: MainClan.Id)),
        new PanelDef("p-00000002", PanelType.Race, new PanelSize(6, Tall: true),
            new PanelSettings(Clan.Slug, SourceIds: new List<string> { MainClan.Id, AltClan.Id }), new PopOutRect(1400, 80, 360, 300)),
        new PanelDef("p-00000003", PanelType.AccountCard, new PanelSize(3), new PanelSettings(Clan.Slug, Stat: "value", UserId: Main.RobloxUserId)),
    ]);

    [Fact]
    public void BoardsRoundTripThroughTheFile()
    {
        using var dir = TempDir.Create("urscore-boards");
        var file = new BoardsFile(Path.Combine(dir.Path, "boards.json"), new FixedTime(Now));
        var second = new BoardDef("b-0000bbbb", "Rivals", []);

        Assert.Null(file.Save([Battle(), second]));
        var load = file.Load();

        Assert.True(load.Exists);
        Assert.True(load.Readable);
        Assert.Equal(2, load.Boards.Count);
        AssertSameBoard(Battle(), load.Boards[0]);
        AssertSameBoard(second, load.Boards[1]);
        Assert.False(File.Exists(Path.Combine(dir.Path, "boards.json.tmp")));

        // The panel type 0.3.0 drops (R4), which a changed Alts tab depends on, reads back as itself.
        var alts = new BoardDef("b-starter-alts", "Alts",
        [
            new PanelDef("p-alts-1", PanelType.AccountsTable, new PanelSize(12), new PanelSettings(Profile.Slug, SourceId: "s-00000009")),
        ]);
        AssertSameBoard(alts, Assert.Single(BoardsFile.Parse(BoardsFile.Serialize([alts]))));
    }

    [Fact]
    public void TheFileIsCamelCaseWithAnOrderAndNoEmptyFields()
    {
        var json = BoardsFile.Serialize([Battle()]);

        Assert.Contains("\"type\": \"standing\"", json);
        Assert.Contains("\"type\": \"accountCard\"", json);
        Assert.Contains("\"order\": 2", json);
        Assert.Contains("\"tall\": true", json);
        Assert.Contains("\"popout\": {", json);
        Assert.Single(json.Split("\"popout\"").Skip(1));
        Assert.DoesNotContain("\"toSourceId\"", json);
    }

    [Fact]
    public void AMissingFileIsNoBoardsAndStillReadable()
    {
        using var dir = TempDir.Create("urscore-boards");

        var load = new BoardsFile(Path.Combine(dir.Path, "boards.json"), new FixedTime(Now)).Load();

        Assert.False(load.Exists);
        Assert.True(load.Readable);
        Assert.Empty(load.Boards);
        Assert.Empty(BoardsFile.Parse("[]"));
    }

    [Fact]
    public void AnUnreadableFileIsKeptBesideTheNewOneOnTheFirstSave()
    {
        using var dir = TempDir.Create("urscore-boards");
        var path = Path.Combine(dir.Path, "boards.json");
        const string Broken = "[{ \"id\": \"b-1\", \"panels\": [ ";
        File.WriteAllText(path, Broken);
        var file = new BoardsFile(path, new FixedTime(Now));

        var load = file.Load();
        Assert.True(load.Exists);
        Assert.False(load.Readable);

        var kept = file.Save([Battle()]);

        Assert.Equal(Path.Combine(dir.Path, "boards.unreadable-20260919-180000.json"), kept);
        Assert.Equal(Broken, File.ReadAllText(kept!));
        Assert.True(file.Load().Readable);
        Assert.Null(file.Save([Battle()]));
    }

    [Fact]
    public void AFileThatCouldntBeReadAtStartIsKeptEvenWhenItReadsByTheFirstSave()
    {
        using var dir = TempDir.Create("urscore-boards");
        var path = Path.Combine(dir.Path, "boards.json");
        var file = new BoardsFile(path, new FixedTime(Now));
        file.Save([Battle()]);
        var saved = File.ReadAllText(path);

        // Another program holds the file while Ur Score starts, and lets go before the first change.
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(file.Load().Readable);
        }

        var kept = file.Save([Battle() with { Name = "Rivals" }], keepExisting: true);

        Assert.Equal(Path.Combine(dir.Path, "boards.unreadable-20260919-180000.json"), kept);
        Assert.Equal(saved, File.ReadAllText(kept!));
        Assert.Equal("Rivals", file.Load().Boards[0].Name);
    }

    [Fact]
    public void MalformedJsonIsNotParsed() =>
        Assert.ThrowsAny<JsonException>(() => BoardsFile.Parse("{ \"id\": 1 }"));

    [Fact]
    public void TextThatIsntJsonIsStillUnreadable()
    {
        using var dir = TempDir.Create("urscore-boards");
        var path = Path.Combine(dir.Path, "boards.json");
        File.WriteAllText(path, "[{ \"id\": \"b-1\", \"panels\": [ { \"type\": \"standing\", \"settings\": 5 ");

        Assert.ThrowsAny<JsonException>(() => BoardsFile.Parse(File.ReadAllText(path)));
        Assert.False(new BoardsFile(path, new FixedTime(Now)).Load().Readable);
    }

    [Fact]
    public void AWrongTypeInsideOnePanelDropsOrRepairsOnlyThatPanel()
    {
        const string Json = """
            [
              { "id": "b-1", "name": "Battle", "panels": [
                  { "id": "p-1", "type": "standing", "size": { "span": "wide", "tall": "yes" }, "order": 0, "settings": { "recipe": "r", "sourceId": "s-1" } },
                  { "id": "p-2", "type": "race", "order": 1, "settings": 5 },
                  { "id": "p-3", "type": "records", "order": 2, "settings": { "recipe": "r", "stat": "value", "userId": "123" } },
                  { "id": "p-4", "type": "top", "order": 3, "settings": { "recipe": "r", "sourceId": "s-9" }, "popout": { "x": "left", "y": 10, "w": 360, "h": 300 } },
                  { "id": 7, "type": 3, "order": 4 },
                  { "id": "p-6", "type": "accountCard", "size": 6, "order": "last", "settings": { "recipe": "r", "stat": "value", "userId": 101 } },
                  { "id": "p-7", "type": "pastPeriods", "size": { "span": 6, "tall": true }, "order": 6, "settings": { "recipe": "r", "sourceId": "s-1" } }
              ] },
              { "id": 12, "name": ["Rivals"], "panels": "none" },
              "not a board",
              { "id": "b-3", "name": "Grind", "panels": [ { "id": "p-8", "type": "profileStat", "order": 0, "settings": { "recipe": "p", "stat": "diamonds" } } ] }
            ]
            """;
        using var dir = TempDir.Create("urscore-boards");
        var path = Path.Combine(dir.Path, "boards.json");
        File.WriteAllText(path, Json);

        var load = new BoardsFile(path, new FixedTime(Now)).Load();

        Assert.True(load.Readable);
        var boards = load.Boards;
        Assert.Equal(3, boards.Count);

        // A size, order or pop-out of the wrong type is repaired; settings of the wrong type, or a type that isn't a name, drop the panel.
        var battle = boards[0];
        Assert.Equal(("b-1", "Battle"), (battle.Id, battle.Name));
        Assert.Equal(new[] { "p-1", "p-6", "p-4", "p-7" }, battle.Panels.Select(p => p.Id).ToArray());
        Assert.Equal(BoardDefs.DefaultSize(PanelType.Standing), battle.Panels[0].Size);
        Assert.Equal(new PanelSettings("r", SourceId: "s-1"), battle.Panels[0].Settings);
        Assert.Equal(BoardDefs.DefaultSize(PanelType.AccountCard), battle.Panels[1].Size);
        Assert.Equal(101, battle.Panels[1].Settings.UserId);
        Assert.Null(battle.Panels[2].PopOut);
        Assert.Equal(new PanelSettings("r", SourceId: "s-9"), battle.Panels[2].Settings);
        Assert.Equal(new PanelSize(6, Tall: true), battle.Panels[3].Size);

        // A board's own fields of the wrong type are repaired, and anything in the list that isn't a board is skipped.
        Assert.Matches("^b-[0-9a-f]{8}$", boards[1].Id);
        Assert.Equal("Board 2", boards[1].Name);
        Assert.Empty(boards[1].Panels);
        Assert.Equal(("b-3", "Grind", "p-8"), (boards[2].Id, boards[2].Name, boards[2].Panels.Single().Id));
    }

    [Fact]
    public void EachPanelIsRepairedOrDroppedOnItsOwn()
    {
        const string Json = """
            [
              { "id": "b-dup", "name": "  ", "panels": [
                  { "id": "p-a", "type": "standing", "size": { "span": 30 }, "order": 2, "settings": { "recipe": "r", "sourceId": "s-1" } },
                  { "id": "p-a", "type": "race", "order": 1, "settings": { "recipe": "r", "sourceIds": ["s-1", "", "s-2"] } },
                  { "id": "p-c", "type": "hologram", "order": 0 },
                  { "type": "3", "order": 3 },
                  { "id": "p-d", "type": "top", "order": 4, "popout": { "x": 10, "y": 10, "w": 0, "h": 200 } },
                  { "id": "p-e", "type": "records", "order": 5 }
              ] },
              { "id": "b-dup", "name": "Second", "panels": null },
              null
            ]
            """;

        var boards = BoardsFile.Parse(Json);

        Assert.Equal(2, boards.Count);
        Assert.Equal(("b-dup", "Board 1"), (boards[0].Id, boards[0].Name));
        Assert.Matches("^b-[0-9a-f]{8}$", boards[1].Id);
        Assert.Equal("Second", boards[1].Name);
        Assert.Empty(boards[1].Panels);

        var panels = boards[0].Panels;
        Assert.Equal(new[] { PanelType.Race, PanelType.Standing, PanelType.Top, PanelType.Records }, panels.Select(p => p.Type).ToArray());
        Assert.Equal("p-a", panels[0].Id);
        Assert.Matches("^p-[0-9a-f]{8}$", panels[1].Id);
        Assert.Equal(new[] { "s-1", "s-2" }, panels[0].Settings.SourceIds!.ToArray());
        Assert.Equal(BoardDefs.DefaultSize(PanelType.Race), panels[0].Size);
        Assert.Equal(new PanelSize(12), panels[1].Size);
        Assert.Null(panels[2].PopOut);
        Assert.Equal(new PanelSettings(), panels[3].Settings);
    }

    [Fact]
    public void SanitizingKeepsYourAccountsAndDropsAnyOtherId()
    {
        const long Stranger = 987654321;
        var board = Battle();
        var withStranger = board with
        {
            Panels = [.. board.Panels, new PanelDef("p-00000004", PanelType.AccountCard, new PanelSize(3), new PanelSettings(Clan.Slug, Stat: "value", UserId: Stranger))],
        };

        var clean = BoardDefs.Sanitize([withStranger], Accounts.Select(a => a.RobloxUserId).ToHashSet());

        Assert.Equal(Main.RobloxUserId, clean[0].Panels[2].Settings.UserId);
        Assert.Null(clean[0].Panels[3].Settings.UserId);
        Assert.DoesNotContain("987654321", BoardsFile.Serialize(clean));
        Assert.Null(BoardDefs.Sanitize([board], new HashSet<long>())[0].Panels[2].Settings.UserId);
    }

    [Fact]
    public void AFollowingStarterHasFixedIdsAndAFreshCopyHasNewOnes()
    {
        var starter = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        var following = BoardDefs.Following(starter);
        var fresh = BoardDefs.FromStarter(starter, freshIds: true);

        Assert.Equal(("b-starter-battle", StarterBoards.Battle), (following.Id, following.Name));
        Assert.Equal("battle", following.Follows);
        Assert.Equal(starter.Panels.Select((_, i) => $"p-battle-{i + 1}"), following.Panels.Select(p => p.Id));
        Assert.Equal(starter.Panels.Select(p => (p.Type, p.Span, p.Settings)), following.Panels.Select(p => (p.Type, p.Size.Span, p.Settings)));
        Assert.All(following.Panels, p => Assert.False(p.Size.Tall));
        Assert.Matches("^b-[0-9a-f]{8}$", fresh.Id);
        Assert.All(fresh.Panels, p => Assert.Matches("^p-[0-9a-f]{8}$", p.Id));
        Assert.Null(fresh.Follows);
    }

    [Fact]
    public void AFollowingTabIsWrittenByNameAloneAndOnlyAKnownStarterFollows()
    {
        IReadOnlyList<BoardDef> boards = [new BoardDef("b-starter-battle", "Battle", [], Follows: "battle"), new BoardDef("b-00000001", "Rivals", [])];

        var json = BoardsFile.Serialize(boards);
        Assert.Contains("\"follows\": \"battle\"", json);
        Assert.DoesNotContain("\"follows\": null", json);
        Assert.Equal(new[] { "battle", null }, BoardsFile.Parse(json).Select(b => b.Follows).ToArray());

        // An unknown starter follows nothing; a second board following the same starter follows nothing either.
        var odd = BoardsFile.Parse("""
            [ { "id": "a", "name": "A", "follows": "grind", "panels": [] },
              { "id": "b", "name": "B", "follows": "ALTS" },
              { "id": "c", "name": "C", "follows": "alts" } ]
            """);
        Assert.Equal(new[] { null, "alts", null }, odd.Select(b => b.Follows).ToArray());
    }

    [Fact]
    public void AFollowingEntryKeepsTheFieldsAnOlderVersionReadsAndAFollowsOfTheWrongTypeFollowsNothing()
    {
        // 0.3.0 reads a board's id, name and panels and ignores anything else, so a following entry loads there as
        // an empty board with its starter's name (D2).
        using var document = System.Text.Json.JsonDocument.Parse(BoardsFile.Serialize([new BoardDef("b-starter-alts", "Alts", [], Follows: "alts")]));
        var entry = Assert.Single(document.RootElement.EnumerateArray().ToList());
        Assert.Equal(new[] { "id", "name", "follows", "panels" }, entry.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(0, entry.GetProperty("panels").GetArrayLength());

        var wrongType = BoardsFile.Parse("""[ { "id": "b-1", "name": "Battle", "follows": 1, "panels": [] }, { "id": "b-2", "name": "Alts", "follows": ["alts"] } ]""");
        Assert.All(wrongType, b => Assert.Null(b.Follows));
        Assert.Equal(2, wrongType.Count);
    }

    [Fact]
    public void TheKeyFollowsWhatIsDrawnButNotWhereAPopOutSits()
    {
        var board = Battle();
        var moved = board with { Panels = [.. board.Panels.Select(p => p.PopOut is null ? p : p with { PopOut = new PopOutRect(10, 10, 400, 300) })] };
        var returned = board with { Panels = [.. board.Panels.Select(p => p with { PopOut = null })] };
        var resized = board with { Panels = [board.Panels[0] with { Size = new PanelSize(6) }, .. board.Panels.Skip(1)] };

        Assert.Equal(BoardDefs.Key(board), BoardDefs.Key(moved));
        Assert.NotEqual(BoardDefs.Key(board), BoardDefs.Key(returned));
        Assert.NotEqual(BoardDefs.Key(board), BoardDefs.Key(resized));
    }

    [Theory]
    [InlineData("  Rivals  ", "Rivals")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("A name that is much longer than forty characters in all", "A name that is much longer than forty ch")]
    public void NamesAreTrimmedAndCut(string? given, string? expected) => Assert.Equal(expected, BoardDefs.CleanName(given));

    private static void AssertSameBoard(BoardDef expected, BoardDef actual)
    {
        Assert.Equal((expected.Id, expected.Name, expected.Panels.Count), (actual.Id, actual.Name, actual.Panels.Count));
        for (var i = 0; i < expected.Panels.Count; i++)
        {
            var (e, a) = (expected.Panels[i], actual.Panels[i]);
            Assert.Equal((e.Id, e.Type, e.Size, e.PopOut), (a.Id, a.Type, a.Size, a.PopOut));
            Assert.Equal(e.Settings with { SourceIds = null }, a.Settings with { SourceIds = null });
            Assert.Equal(e.Settings.SourceIds ?? Array.Empty<string>(), a.Settings.SourceIds ?? Array.Empty<string>());
        }
    }
}
