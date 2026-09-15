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
    public void MalformedJsonIsNotParsed() =>
        Assert.ThrowsAny<JsonException>(() => BoardsFile.Parse("{ \"id\": 1 }"));

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
    public void TheFollowingStarterHasFixedIdsAndAFreshCopyHasNewOnes()
    {
        var starter = StarterBoards.Build([Installed(Clan, "value")], [MainClan, AltClan]);

        var following = BoardDefs.FromStarter(starter, freshIds: false);
        var fresh = BoardDefs.FromStarter(starter, freshIds: true);

        Assert.Equal(BoardDefs.StarterBoardId, following.Id);
        Assert.Equal(StarterBoards.Battle, following.Name);
        Assert.Equal(starter.Panels.Select((_, i) => $"p-starter-{i + 1}"), following.Panels.Select(p => p.Id));
        Assert.Equal(starter.Panels.Select(p => (p.Type, p.Span, p.Settings)), following.Panels.Select(p => (p.Type, p.Size.Span, p.Settings)));
        Assert.All(following.Panels, p => Assert.False(p.Size.Tall));
        Assert.Matches("^b-[0-9a-f]{8}$", fresh.Id);
        Assert.All(fresh.Panels, p => Assert.Matches("^p-[0-9a-f]{8}$", p.Id));
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
