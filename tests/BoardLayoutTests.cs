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
    public void AOneRowPanelKeepsItsOwnHeightAndATallPanelFillsItsSlot()
    {
        IReadOnlyList<double> rows = [120, 80];

        // One row: its own height at the top of the row, never taller than the row (Task 8, the mock's align-items: start).
        Assert.Equal(70, BoardLayout.ArrangedHeight(new PanelPlacement(0, 0, 0, 6), rows, 12, desired: 70));
        Assert.Equal(120, BoardLayout.ArrangedHeight(new PanelPlacement(0, 0, 0, 6), rows, 12, desired: 150));

        // Tall: both rows and the gap, however short its content.
        Assert.Equal(212, BoardLayout.ArrangedHeight(new PanelPlacement(2, 0, 6, 6, Rows: 2), rows, 12, desired: 70));
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

    /// <summary>
    /// Where the insertion mark is drawn for a drop index. A CARET rather than a filled outline of the target cell,
    /// because position is derived from order: dropping between two panels reflows everything after it, so the
    /// dragged panel does not end up in the cell the cursor was over. A rectangle would promise a place this layout
    /// cannot keep; a caret promises only an order, which is exactly what a drop decides.
    /// </summary>
    [Theory]
    [InlineData(0, 0.0, 0.0, 200.0)]      // before the first: its left edge, its own height
    [InlineData(1, 312.0, 0.0, 200.0)]    // before the second
    [InlineData(3, 0.0, 212.0, 150.0)]    // before the fourth: a different ROW, so a different top and height
    [InlineData(4, 600.0, 212.0, 150.0)]  // past the last: the right edge of it
    public void TheDropCaretSitsAtTheEdgeOfTheCellItWouldInsertBefore(int index, double x, double top, double height)
    {
        var caret = BoardLayout.CaretFor(Cells, index);

        Assert.NotNull(caret);
        Assert.Equal(x, caret!.X);
        Assert.Equal(top, caret.Top);
        Assert.Equal(height, caret.Height);
    }

    /// <summary>An empty board has nothing to insert between, so it draws no mark rather than one at the origin.</summary>
    [Fact]
    public void AnEmptyBoardHasNoDropCaret() => Assert.Null(BoardLayout.CaretFor([], 0));

    /// <summary>
    /// Dragging a panel's edge picks the OFFERED span whose drawn width is nearest the width you dragged to, not an
    /// arbitrary column count: a board only has Small, Half and Wide, and a drag that produced a 5-wide panel would
    /// invent a size the rest of the app does not handle (<see cref="BoardLayout.EffectiveSpan"/> narrows by those
    /// three). On a 1200-wide board with a 12 gap the three draw at 291, 594 and 1200, so the choices change at
    /// 442 and 897 — the values below sit either side of both, which is what makes this test able to fail.
    /// </summary>
    [Theory]
    [InlineData(0.0, PanelSize.Small)]
    [InlineData(280.0, PanelSize.Small)]
    [InlineData(440.0, PanelSize.Small)]   // just under the Small/Half midpoint of 442.5
    [InlineData(445.0, PanelSize.Half)]    // just over it
    [InlineData(890.0, PanelSize.Half)]    // just under the Half/Wide midpoint of 897
    [InlineData(905.0, PanelSize.Wide)]    // just over it
    [InlineData(99999.0, PanelSize.Wide)]
    public void AnEdgeDragSnapsToTheNearestOfferedSpan(double wanted, int expected) =>
        Assert.Equal(expected, BoardLayout.SpanFor(wanted, 1200, 12));

    /// <summary>
    /// A panel is one row or two, so dragging its bottom is a choice between them rather than a height. Two rows is
    /// taken at half way, which is what "nearest" means when there are only two answers.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(200.0, 1)]
    [InlineData(299.0, 1)]
    [InlineData(300.0, 2)]
    [InlineData(900.0, 2)]
    public void ABottomDragChoosesOneRowOrTwo(double wanted, int expected) =>
        Assert.Equal(expected, BoardLayout.RowsFor(wanted, 200));

    /// <summary>A row of no height cannot say which is nearer, so it stays one rather than dividing by nothing.</summary>
    [Fact]
    public void ARowWithNoHeightStaysOneRow() => Assert.Equal(1, BoardLayout.RowsFor(500, 0));

    /// <summary>
    /// Where a panel's resize grips sit: a strip straddling its right edge, and a square in its bottom-right
    /// corner. Straddling rather than inside, so the grip is reachable from either side of the line the eye reads
    /// as the panel's edge. The fixture is at 100,50 and 300x200, so a grip taken from the LEFT edge or the TOP
    /// would land on 100 or 50 and these numbers would not match.
    /// </summary>
    [Fact]
    public void APanelsGripsStraddleItsRightEdgeAndSitInItsCorner()
    {
        var grips = BoardLayout.HandlesFor(new CellRect(100, 50, 300, 200));

        Assert.Equal(new CellRect(395, 50, 10, 200), grips.Edge);
        Assert.Equal(new CellRect(386, 236, 14, 14), grips.Corner);
    }

    /// <summary>
    /// A panel smaller than its own grips shrinks them rather than growing grips bigger than the thing they
    /// resize. The corner stays wholly inside; the edge grip straddles, so it may reach HALF a grip into the gap
    /// beside the panel and no further — past that it would be over the neighbour, taking presses meant for it.
    /// </summary>
    [Fact]
    public void GripsShrinkWithASmallPanelAndNeverReachTheNeighbour()
    {
        var cell = new CellRect(0, 0, 8, 8);
        var grips = BoardLayout.HandlesFor(cell);

        Assert.Equal(new CellRect(0, 0, 8, 8), grips.Corner);

        Assert.True(grips.Edge.Left >= cell.Left, $"{grips.Edge} starts left of {cell}");
        Assert.True(
            grips.Edge.Left + grips.Edge.Width <= cell.Left + cell.Width + (BoardLayout.EdgeGrip / 2),
            $"{grips.Edge} reaches further than half a grip past {cell}");
    }

    /// <summary>
    /// A corner drag on a tall panel, let go at the height it already had, kept it tall only if the comparison is
    /// with ONE row. EndResize compared it with the whole two-row slot, and RowsFor answers two only at 1.5 times
    /// its row argument, so the same height came back short. Rows of unequal height, because they are unequal on
    /// every real board and an average of the two would hide the mistake.
    /// </summary>
    [Fact]
    public void ACornerDragOnATallPanelLetGoAtItsOwnHeightStaysTall()
    {
        const double gap = 12;
        var placed = BoardLayout.Flow([new PanelSize(6, Tall: true), new PanelSize(6), new PanelSize(6)], 1400);
        var rows = BoardLayout.RowHeights(placed, [100, 180, 90], gap);
        var tall = placed[0];
        var slot = BoardLayout.CellHeight(tall, rows, gap);

        Assert.Equal(2, BoardLayout.RowsFor(slot, BoardLayout.FirstRowHeight(tall, rows)));

        // What the old code asked, which is the defect: the slot measured against itself is "one row".
        Assert.Equal(1, BoardLayout.RowsFor(slot, slot));

        // A one-row panel let go at its own height stays one row.
        var single = placed[1];
        Assert.Equal(1, BoardLayout.RowsFor(BoardLayout.CellHeight(single, rows, gap), BoardLayout.FirstRowHeight(single, rows)));
    }

    [Fact]
    public void APlacementPastTheRowsHasNoFirstRow() =>
        Assert.Equal(0, BoardLayout.FirstRowHeight(new PanelPlacement(0, 3, 0, 6), [120, 80]));
}
