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

    [Fact]
    public void ATallPanelTakesTwoRowsAndLaterPanelsFlowAroundIt() =>
        Assert.Equal(
            new[] { (0, 0, 6, 2), (0, 6, 6, 1), (1, 6, 3, 1), (1, 9, 3, 1), (2, 0, 12, 1) },
            BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(3), new PanelSize(3), new PanelSize(12)], 1280)
                .Select(p => (p.Row, p.Column, p.Span, p.Rows)).ToArray());

    [Fact]
    public void PanelsNeverJumpAheadOfATallPanelsSecondRow() =>
        Assert.Equal(
            new[] { (0, 0), (0, 3), (2, 0), (3, 0) },
            BoardLayout.Flow([new PanelSize(3), new PanelSize(3, Tall: true), new PanelSize(12), new PanelSize(6)], 1280)
                .Select(p => (p.Row, p.Column)).ToArray());

    [Fact]
    public void TheIntOverloadFlowsNonTallSizesOntoASecondRow() =>
        Assert.Equal(
            new[] { (0, 6, 0, 1), (6, 6, 0, 1), (0, 6, 1, 1) },
            BoardLayout.Flow([6, 6, 6], 1280).Select(p => (p.Column, p.Span, p.Row, p.Rows)).ToArray());

    [Fact]
    public void RowsAreAsTallAsTheirTallestPanelAndATallPanelGrowsItsSecondRow()
    {
        var placed = BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(6)], 1280);

        Assert.Equal(new[] { 200.0, 288.0 }, BoardLayout.RowHeights(placed, [500, 200, 150], 12).ToArray());
        Assert.Equal(new[] { 200.0, 150.0 }, BoardLayout.RowHeights(placed, [100, 200, 150], 12).ToArray());
    }

    [Fact]
    public void APanelsSlotIsAsTallAsItsRowsAndTheGapsBetweenThem()
    {
        IReadOnlyList<double> rows = [120, 80];

        Assert.Equal(120, BoardLayout.CellHeight(new PanelPlacement(0, 0, 0, 6), rows, 12));
        Assert.Equal(80, BoardLayout.CellHeight(new PanelPlacement(1, 1, 0, 12), rows, 12));
        Assert.Equal(212, BoardLayout.CellHeight(new PanelPlacement(2, 0, 6, 6, Rows: 2), rows, 12));
    }

    private static readonly CellRect[] Cells =
    [
        new(0, 0, 300, 200), new(312, 0, 300, 200), new(624, 0, 600, 200),
        new(0, 212, 600, 150),
    ];

    [Theory]
    [InlineData(100.0, 50.0, 0)]
    [InlineData(250.0, 50.0, 1)]
    [InlineData(400.0, 100.0, 1)]
    [InlineData(1000.0, 100.0, 3)]
    [InlineData(306.0, 100.0, 1)]
    [InlineData(900.0, 300.0, 4)]
    [InlineData(100.0, 206.0, 3)]
    [InlineData(100.0, 900.0, 4)]
    public void ADropLandsBeforeOrAfterThePanelUnderIt(double x, double y, int expected) =>
        Assert.Equal(expected, BoardLayout.DropIndex(Cells, x, y));
}
