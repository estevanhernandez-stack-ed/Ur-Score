using Labs626.UrScore.Board;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The boards' JSON on its own, so a pack can write boards without naming <c>BoardsFile</c>, which the
/// boards-file fence reserves for the one writer of boards.json. The shape is unchanged: what
/// <c>BoardsFile</c> wrote before, it writes now through this.
/// </summary>
public class BoardJsonTests
{
    [Fact]
    public void ABoardRoundTripsThroughItsJson()
    {
        var board = new BoardDef("b-1", "Battle", [
            new PanelDef("p-1", PanelType.Standing, new PanelSize(6), new PanelSettings(Clan.Slug, SourceId: "s-00000001")),
            new PanelDef("p-2", PanelType.AccountCard, new PanelSize(6, Tall: true), new PanelSettings(Clan.Slug, UserId: 101), new PopOutRect(10, 20, 300, 200)),
        ]);
        var following = new BoardDef("b-starter-alts", "Alts", [], Follows: "alts");

        var json = BoardJson.Serialize([board, following]);
        var back = BoardJson.Parse(json);

        // BoardDef's own equality compares Panels by reference (record-generated Equals over
        // IReadOnlyList<PanelDef>, which isn't itself IEquatable), so a round trip through a different
        // concrete list type never compares equal that way even when every panel matches. Compared the
        // same way BoardsFileTests.AssertSameBoard already does: fields by tuple, panels by sequence.
        Assert.Equal(2, back.Count);
        Assert.Equal((board.Id, board.Name, board.Follows), (back[0].Id, back[0].Name, back[0].Follows));
        Assert.Equal(board.Panels, back[0].Panels);
        Assert.Equal((following.Id, following.Name, following.Follows), (back[1].Id, back[1].Name, back[1].Follows));
        Assert.Equal(following.Panels, back[1].Panels);
        Assert.Contains("\"follows\"", json, StringComparison.Ordinal);
    }
}
