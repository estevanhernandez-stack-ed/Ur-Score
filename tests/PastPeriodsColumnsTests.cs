using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Labs626.UrScore.Board;

namespace UrScore.Tests;

/// <summary>
/// Past battles carries four columns that can all be full at once: a battle name, a place, a total, and a total
/// with the account that set it. Two defects met here (owner's look, 2026-09-16): the pop-out opened at one size
/// for every panel, 360 x 300, where all four clipped at once; and the rebalance that stopped Total and Your best
/// running together (6a7981c) took the room out of the battle name. This holds both ends of the trade — the width
/// a pop-out opens at now, the width the panel has on the board, the gutter between the two numbers, and the equal
/// share the two columns that carry names get — against the widths the XAML actually declares.
/// </summary>
public partial class PastPeriodsColumnsTests
{
    // The chain from a 1920 screen to the grid a row is drawn on, in device-independent pixels.
    private const double Gap = 12;                 // PanelGrid.Gap
    private const double BoardGrid = 1875;         // 1920 less BoardScroll's 14 each side and the scroll bar
    private const double NarrowBoardGrid = 1055;   // the same at a 1100-wide window
    private const double CardEdge = 2;             // PanelCard BorderThickness, both sides
    private const double CardPadding = 28;         // PanelCard Padding 14,12,14,14
    private const double RowPadding = 12;          // PanelTableRowLine Padding 6,7
    private const double RowFontSize = 13;         // BoardWindow's and the pop-out's body size
    private const double HeaderFontSize = 10.5;    // ColumnHeader
    private const string BodyFont = "Segoe UI Variable Text, Segoe UI";
    private const string MonoFont = "Cascadia Code, Consolas";

    // The longest of each the live record actually holds, and the longest each can be.
    private const string LongestPeriod = "BackroomsBattle2026";
    private const string LongestPlace = "1,234th";
    private const string LongestTotal = "312.5M";
    private const string LongestBest = "312.5M · CECP_Estevan";

    [Fact]
    public void ABattleNameFitsWherePastBattlesIsDrawn()
    {
        var need = Width(LongestPeriod, BodyFont, RowFontSize);
        Assert.True(need > 80, $"'{LongestPeriod}' measured {need:0.#} px, too little to be measuring anything; the font did not load.");

        foreach (var (where, grid) in Places())
        {
            var columns = Allocate(grid);
            Assert.True(columns[0] >= need, $"{where}: the battle name has {columns[0]:0.#} px and needs {need:0.#}.");
        }
    }

    [Fact]
    public void AnAttributedBestFitsBesideIt()
    {
        var need = Width(LongestBest, BodyFont, RowFontSize);

        foreach (var (where, grid) in Places())
        {
            var columns = Allocate(grid);
            var room = columns[3] - Gutters()[3];
            Assert.True(room >= need, $"{where}: Your best has {room:0.#} px and needs {need:0.#}.");
        }
    }

    [Fact]
    public void TotalAndYourBestNeverRunTogether()
    {
        // 32 beside 32 read as 3232. Whatever the shares end up being, every column past the first opens with a
        // gutter no cell can be drawn into, so two right-aligned numbers always have space between them.
        var gutters = Gutters();

        Assert.Equal(0, gutters[0]);
        foreach (var column in new[] { 1, 2, 3 })
        {
            Assert.True(gutters[column] >= 12, $"column {column} opens with only {gutters[column]:0.#} px of gutter.");
        }

        foreach (var (where, grid) in Places())
        {
            var columns = Allocate(grid);
            var totalRoom = columns[2] - gutters[2];
            Assert.True(totalRoom >= Width(LongestTotal, BodyFont, RowFontSize), $"{where}: Total has only {totalRoom:0.#} px.");
            Assert.True(columns[1] - gutters[1] >= Width(LongestPlace, BodyFont, RowFontSize), $"{where}: Place has only {columns[1] - gutters[1]:0.#} px.");
        }
    }

    [Fact]
    public void EveryHeadingFitsItsColumn()
    {
        var gutters = Gutters();
        (int Column, string Heading)[] headings = [(1, "Place"), (2, "Total"), (3, "Your best")];

        foreach (var (where, grid) in Places())
        {
            var columns = Allocate(grid);
            foreach (var (column, heading) in headings)
            {
                var need = Width(heading, MonoFont, HeaderFontSize);
                Assert.True(columns[column] - gutters[column] >= need, $"{where}: '{heading}' has {columns[column] - gutters[column]:0.#} px and needs {need:0.#}.");
            }
        }
    }

    [Fact]
    public void TheTwoColumnsThatCarryNamesShareWhatIsLeftEqually()
    {
        // The two numbers take what they need and no more; neither name column is starved to feed the other,
        // because the gutters do the separating and the shares only divide the surplus.
        var declared = Declared();
        Assert.Equal(declared[0], declared[3]);

        var columns = Allocate(BoardGrid);
        Assert.Equal(columns[0], columns[3], 6);
    }

    [Fact]
    public void TheHeadingRowAndTheRowsUnderItKeepTheSameColumns()
    {
        var xaml = Xaml();
        var blocks = ColumnBlock().Matches(xaml);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(Widths(blocks[0].Value), Widths(blocks[1].Value));
    }

    /// <summary>Where a Past battles panel is drawn: its half-width tile on a board, and the pop-out that lifts it off.</summary>
    private static IEnumerable<(string Where, double Grid)> Places()
    {
        foreach (var board in new[] { BoardGrid, NarrowBoardGrid })
        {
            var tile = BoardLayout.CellWidth(board, PanelSize.Half, Gap);
            yield return ($"a half-width tile on a {board:0} board", RowGrid(tile));

            // The pop-out opens at the panel's own width plus the window's furniture, so the panel keeps that width.
            var popOut = PopOutPlacement.SizeFor(PanelSize.Half, board, Gap, 600).Width - PopOutPlacement.ChromeWidth;
            yield return ($"popped out from a {board:0} board", RowGrid(popOut));
        }
    }

    /// <summary>The width the row's Grid gets inside a panel of <paramref name="panelWidth"/>: the card, then the row line.</summary>
    private static double RowGrid(double panelWidth) => panelWidth - CardEdge - CardPadding - RowPadding;

    /// <summary>What each of the four columns is given at <paramref name="grid"/>, the way a WPF Grid gives it.</summary>
    private static double[] Allocate(double grid)
    {
        var declared = Declared();
        var fixedWidth = declared.Where(w => w > 0).Sum();
        var stars = declared.Where(w => w < 0).Sum(w => -w);
        var free = Math.Max(0, grid - fixedWidth);

        return [.. declared.Select(w => w > 0 ? w : free * (-w / stars))];
    }

    /// <summary>Each column as the XAML declares it: a pixel width as itself, a star width as its negative weight.</summary>
    private static double[] Declared() => Widths(ColumnBlock().Match(Xaml()).Value);

    private static double[] Widths(string block) =>
        [.. WidthAttribute().Matches(block).Select(m => m.Groups[1].Value).Select(w => w.EndsWith('*')
            ? -(w.Length == 1 ? 1 : double.Parse(w[..^1], CultureInfo.InvariantCulture))
            : double.Parse(w, CultureInfo.InvariantCulture))];

    /// <summary>The gutter each column's cell opens with, from the row template's own margins.</summary>
    private static double[] Gutters()
    {
        var row = Xaml()[Xaml().IndexOf("PanelTableRowLine", StringComparison.Ordinal)..];
        return [.. Enumerable.Range(0, 4).Select(column =>
        {
            var cell = Regex.Match(row, $"<TextBlock Grid.Column=\"{column}\"(.*?)/>", RegexOptions.Singleline);
            Assert.True(cell.Success, $"the row template has no cell for column {column}; this fence is looking in the wrong place.");

            var margin = Regex.Match(cell.Groups[1].Value, "Margin=\"([^\"]*)\"");
            return margin.Success ? double.Parse(margin.Groups[1].Value.Split(',')[0], CultureInfo.InvariantCulture) : 0;
        })];
    }

    private static double Width(string text, string family, double size) =>
        new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
            size, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

    private static string Xaml() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Panels", "PastPeriodsPanel.xaml"));

    [GeneratedRegex(@"<Grid\.ColumnDefinitions>.*?</Grid\.ColumnDefinitions>", RegexOptions.Singleline)]
    private static partial Regex ColumnBlock();

    [GeneratedRegex(@"<ColumnDefinition Width=""([^""]+)""")]
    private static partial Regex WidthAttribute();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
