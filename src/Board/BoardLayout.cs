namespace Labs626.UrScore.Board;

/// <summary>Where one panel sits: its row and column, how many columns, and how many rows (2 for a tall panel).</summary>
public sealed record PanelPlacement(int Index, int Row, int Column, int Span, int Rows = 1);

/// <summary>Where a panel was arranged, in the grid's own coordinates.</summary>
public sealed record CellRect(double Left, double Top, double Width, double Height);

/// <summary>The two grips that resize a panel: a strip on its right edge, and a square in its bottom-right corner.</summary>
public sealed record PanelHandles(CellRect Edge, CellRect Corner);

/// <summary>
/// Where to draw the mark that says where a dragged panel will land: a vertical line, in the grid's own
/// coordinates.
/// <para>
/// A caret and not an outline of the target cell, deliberately. A panel's position is derived from its ORDER
/// (<see cref="BoardLayout.Flow"/>), so a drop between two panels reflows everything after it and the dragged
/// panel does not come to rest in the cell the cursor was over. An outline would promise a place this layout
/// cannot keep. A caret promises only an order, which is all a drop decides.
/// </para>
/// </summary>
public sealed record DropCaret(double X, double Top, double Height);

/// <summary>
/// Panels flow in order across a 12-column grid (spec §9.2), each at the first free spot at or after the
/// previous one's, so a tall panel's second row pushes later panels along and order stays reading order (R6).
/// Narrow windows widen panels the way the mock does: 3- and 4-wide become half, 5 and 7+ take the row.
/// </summary>
public static class BoardLayout
{
    public const int Columns = 12;
    public const double NarrowWidth = 1100;
    public const double SingleWidth = 720;

    /// <summary>One of the twelve columns on a board of <paramref name="width"/>: the width less the gaps, shared twelve ways.</summary>
    public static double ColumnWidth(double width, double gap) => Math.Max(0, (width - gap * (Columns - 1)) / Columns);

    /// <summary>
    /// The width a panel of <paramref name="span"/> columns takes on a board of <paramref name="width"/>: its own
    /// columns and the gaps inside it. The grid arranges by this (<c>PanelGrid</c>), and a pop-out opens at it
    /// (<see cref="PopOutPlacement.SizeFor"/>), so a panel is the same width in its own window as on the board.
    /// </summary>
    public static double CellWidth(double width, int span, double gap) => ColumnWidth(width, gap) * span + gap * (span - 1);

    public static int EffectiveSpan(int span, double width) =>
        width < SingleWidth ? Columns
        : width < NarrowWidth ? span switch { <= 4 => 6, 6 => 6, _ => Columns }
        : Math.Clamp(span, 1, Columns);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<int> spans, double width) =>
        Flow(spans.Select(span => new PanelSize(span)).ToList(), width);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<PanelSize> sizes, double width)
    {
        var placements = new List<PanelPlacement>(sizes.Count);
        var taken = new HashSet<(int Row, int Column)>();
        int row = 0, column = 0;

        for (var index = 0; index < sizes.Count; index++)
        {
            var span = EffectiveSpan(sizes[index].Span, width);
            var rows = sizes[index].Tall ? 2 : 1;

            while (true)
            {
                if (column + span > Columns)
                {
                    row++;
                    column = 0;
                }

                if (Free(taken, row, column, span, rows)) break;
                column++;
            }

            for (var r = row; r < row + rows; r++)
            {
                for (var c = column; c < column + span; c++) taken.Add((r, c));
            }

            placements.Add(new PanelPlacement(index, row, column, span, rows));
            column += span;
        }

        return placements;
    }

    /// <summary>
    /// Each row is as tall as its tallest one-row panel. A tall panel that needs more than its two rows and the
    /// gap between them grows its second row by the difference.
    /// </summary>
    public static IReadOnlyList<double> RowHeights(IReadOnlyList<PanelPlacement> placements, IReadOnlyList<double> desired, double gap)
    {
        var rows = new List<double>();

        foreach (var placement in placements.Where(p => p.Rows == 1))
        {
            while (rows.Count <= placement.Row) rows.Add(0);
            rows[placement.Row] = Math.Max(rows[placement.Row], desired[placement.Index]);
        }

        foreach (var placement in placements.Where(p => p.Rows > 1))
        {
            var last = placement.Row + placement.Rows - 1;
            while (rows.Count <= last) rows.Add(0);

            var have = Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + gap * (placement.Rows - 1);
            if (desired[placement.Index] > have) rows[last] += desired[placement.Index] - have;
        }

        return rows;
    }

    /// <summary>
    /// A panel's slot: its rows and the gaps between them. <c>PanelGrid</c> places drops by the slot and arranges a tall
    /// panel at its full height; a one-row panel keeps its own height at the top of its slot (Task 8, which replaced D7).
    /// </summary>
    public static double CellHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap) =>
        Enumerable.Range(placement.Row, placement.Rows).Sum(r => rows[r]) + gap * (placement.Rows - 1);

    /// <summary>
    /// How tall a panel is drawn in its slot (the mock's align-items: start): a one-row panel at its own height, never
    /// taller than the slot, so a short card doesn't stretch beside a tall neighbour; a tall panel fills its slot, which is
    /// what Tall asks for.
    /// </summary>
    public static double ArrangedHeight(PanelPlacement placement, IReadOnlyList<double> rows, double gap, double desired)
    {
        var slot = CellHeight(placement, rows, gap);
        return placement.Rows > 1 ? slot : Math.Min(desired, slot);
    }

    /// <summary>
    /// The insertion index for a drop at (x, y), counting the dragged panel itself (<see cref="BoardEdits.MoveTo"/>).
    /// Over a panel: before it on its left half, after it on its right half. Elsewhere: after every panel that ends
    /// above the point or sits to its left on the same line.
    /// </summary>
    public static int DropIndex(IReadOnlyList<CellRect> cells, double x, double y)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (x >= cell.Left && x < cell.Left + cell.Width && y >= cell.Top && y < cell.Top + cell.Height)
            {
                return x < cell.Left + cell.Width / 2 ? i : i + 1;
            }
        }

        return cells.Count(cell => cell.Top + cell.Height <= y || (cell.Top <= y && cell.Left + cell.Width <= x));
    }

    /// <summary>How wide the edge grip is, and how big the corner one is. Both comfortably bigger than a line.</summary>
    public const double EdgeGrip = 10;

    public const double CornerGrip = 14;

    /// <summary>
    /// The grips for a panel occupying <paramref name="cell"/>, in the grid's own coordinates.
    /// <para>
    /// The edge grip STRADDLES the right edge rather than sitting inside it, so it can be caught from either side
    /// of the line the eye reads as the panel's boundary. It therefore reaches <see cref="EdgeGrip"/>/2 into the
    /// GAP beside the panel, which is empty — the caller's gap must stay at least <see cref="EdgeGrip"/> wide or a
    /// grip would reach its neighbour and take presses meant for it. The corner grip stays inside the cell, since
    /// it has no edge to straddle that is not already the edge grip's.
    /// </para>
    /// </summary>
    public static PanelHandles HandlesFor(CellRect cell)
    {
        var edgeWidth = Math.Min(EdgeGrip, cell.Width);
        var edge = new CellRect(cell.Left + cell.Width - (edgeWidth / 2), cell.Top, edgeWidth, cell.Height);

        var corner = Math.Min(CornerGrip, Math.Min(cell.Width, cell.Height));
        return new PanelHandles(edge, new CellRect(
            cell.Left + cell.Width - corner,
            cell.Top + cell.Height - corner,
            corner,
            corner));
    }

    /// <summary>
    /// The span an edge drag lands on: the OFFERED size whose drawn width is nearest <paramref name="wanted"/>.
    /// <para>
    /// A board has three sizes, not twelve. A drag free to produce a 5-wide panel would invent one the rest of the
    /// app does not handle — <see cref="EffectiveSpan"/> narrows a board by Small, Half and Wide — so the drag
    /// chooses between them rather than between columns.
    /// </para>
    /// </summary>
    public static int SpanFor(double wanted, double width, double gap)
    {
        int[] offered = [PanelSize.Small, PanelSize.Half, PanelSize.Wide];
        var best = offered[0];
        var closest = double.MaxValue;

        foreach (var span in offered)
        {
            var apart = Math.Abs(CellWidth(width, span, gap) - wanted);
            if (apart >= closest) continue;
            closest = apart;
            best = span;
        }

        return best;
    }

    /// <summary>
    /// Whether a bottom drag to <paramref name="wanted"/> means one row or two. A panel is tall or it is not, so
    /// this is a choice between two answers and not a height; two is taken at half way, which is what nearest means
    /// with two. A row of no height cannot say which is nearer and stays one rather than dividing by nothing.
    /// </summary>
    public static int RowsFor(double wanted, double rowHeight) =>
        rowHeight > 0 && wanted >= rowHeight * 1.5 ? 2 : 1;

    /// <summary>
    /// The mark for a drop at <paramref name="index"/>, an insertion index as <see cref="DropIndex"/> returns:
    /// the left edge of the cell it would insert before, or the right edge of the last cell when it goes at the
    /// end. It takes its top and height from THAT cell, so a caret on a shorter second row is drawn the height of
    /// that row rather than the first one's. Null for an empty board: nothing to insert between.
    /// </summary>
    public static DropCaret? CaretFor(IReadOnlyList<CellRect> cells, int index)
    {
        if (cells.Count == 0) return null;

        if (index >= cells.Count)
        {
            var last = cells[^1];
            return new DropCaret(last.Left + last.Width, last.Top, last.Height);
        }

        var cell = cells[Math.Max(0, index)];
        return new DropCaret(cell.Left, cell.Top, cell.Height);
    }

    private static bool Free(HashSet<(int Row, int Column)> taken, int row, int column, int span, int rows)
    {
        for (var r = row; r < row + rows; r++)
        {
            for (var c = column; c < column + span; c++)
            {
                if (taken.Contains((r, c))) return false;
            }
        }

        return true;
    }
}
