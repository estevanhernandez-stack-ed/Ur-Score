using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class BoardLayoutTests
{
    private static readonly int[] Battle = [3, 3, 6, 5, 4, 3, 4, 4];

    [Fact]
    public void WideTheBattleBoardFillsTwelveColumnRows() =>
        Assert.Equal(
            new[] { (0, 0, 3), (0, 3, 3), (0, 6, 6), (1, 0, 5), (1, 5, 4), (1, 9, 3), (2, 0, 4), (2, 4, 4) },
            BoardLayout.Flow(Battle, 1280).Select(p => (p.Row, p.Column, p.Span)).ToArray());

    [Fact]
    public void NarrowSmallPanelsTakeHalfTheRowAndWiderOnesAllOfIt()
    {
        var placed = BoardLayout.Flow(Battle, 900);

        Assert.Equal(new[] { 6, 6, 6, 12, 6, 6, 6, 6 }, placed.Select(p => p.Span).ToArray());
        Assert.Equal(new[] { 0, 0, 1, 2, 3, 3, 4, 4 }, placed.Select(p => p.Row).ToArray());
    }

    [Fact]
    public void AtPhoneWidthEveryPanelTakesTheRow() =>
        Assert.All(BoardLayout.Flow(Battle, 600), p => Assert.Equal(BoardLayout.Columns, p.Span));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 12)]
    public void SpansStayInsideTheGrid(int span, int effective) =>
        Assert.Equal(effective, BoardLayout.EffectiveSpan(span, 1280));
}
