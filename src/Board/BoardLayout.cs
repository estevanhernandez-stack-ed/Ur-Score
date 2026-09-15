namespace Labs626.UrScore.Board;

public sealed record PanelPlacement(int Index, int Row, int Column, int Span);

/// <summary>
/// Panels flow in order across a 12-column grid (spec §9.2), wrapping to a new row when the next one doesn't
/// fit. Narrow windows widen panels the way the mock does: 3- and 4-wide become half, 5 and 7+ take the row.
/// </summary>
public static class BoardLayout
{
    public const int Columns = 12;
    public const double NarrowWidth = 1100;
    public const double SingleWidth = 720;

    public static int EffectiveSpan(int span, double width) =>
        width < SingleWidth ? Columns
        : width < NarrowWidth ? span switch { <= 4 => 6, 6 => 6, _ => Columns }
        : Math.Clamp(span, 1, Columns);

    public static IReadOnlyList<PanelPlacement> Flow(IReadOnlyList<int> spans, double width)
    {
        var placements = new List<PanelPlacement>();
        int row = 0, column = 0;

        for (var index = 0; index < spans.Count; index++)
        {
            var span = EffectiveSpan(spans[index], width);
            if (column + span > Columns)
            {
                row++;
                column = 0;
            }

            placements.Add(new PanelPlacement(index, row, column, span));
            column += span;
        }

        return placements;
    }
}
