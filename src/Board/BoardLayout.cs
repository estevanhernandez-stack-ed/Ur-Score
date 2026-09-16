namespace Labs626.UrScore.Board;

/// <summary>Where one panel sits: its row and column, how many columns, and how many rows (2 for a tall panel).</summary>
public sealed record PanelPlacement(int Index, int Row, int Column, int Span, int Rows = 1);

/// <summary>Where a panel was arranged, in the grid's own coordinates.</summary>
public sealed record CellRect(double Left, double Top, double Width, double Height);

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
