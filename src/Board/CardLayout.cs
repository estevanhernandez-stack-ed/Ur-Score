namespace Labs626.UrScore.Board;

/// <summary>
/// An account card's sections (D12) in equal columns: as many as fit at <see cref="MinColumnWidth"/> with
/// <see cref="ColumnGap"/> between them, never more than there are sections. Sections fill the columns in turn and each
/// column stacks its own, so a short section never waits under a tall neighbour's blank space.
/// </summary>
public static class CardLayout
{
    public const double MinColumnWidth = 200;
    public const double ColumnGap = 24;

    /// <summary>How many columns <paramref name="sections"/> sections take in <paramref name="width"/>: at least one.</summary>
    public static int SectionColumns(double width, int sections)
    {
        var fit = Math.Floor((width + ColumnGap) / (MinColumnWidth + ColumnGap));
        return (int)Math.Max(1, Math.Min(sections, double.IsNaN(fit) ? 1 : fit));
    }

    /// <summary>One column's width: the width less the gaps, shared equally.</summary>
    public static double ColumnWidth(double width, int columns) =>
        Math.Max(0, (width - ColumnGap * (columns - 1)) / Math.Max(1, columns));

    /// <summary>The column the section at <paramref name="index"/> goes in.</summary>
    public static int ColumnOf(int index, int columns) => index % Math.Max(1, columns);
}
