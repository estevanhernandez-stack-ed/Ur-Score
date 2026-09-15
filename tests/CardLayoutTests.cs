using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class CardLayoutTests
{
    [Theory]
    [InlineData(270, 2, 1)]     // a Small card at 1280: one column, never two of 123 px
    [InlineData(316, 3, 1)]     // a card popped out at its opening 360 px
    [InlineData(423.9, 2, 1)]   // just short of two 200 px columns and their gap
    [InlineData(424, 2, 2)]     // exactly two
    [InlineData(680, 2, 2)]     // the Alts starter's card: two sections, two columns
    [InlineData(680, 3, 3)]     // three sections fit three columns there
    [InlineData(680, 5, 3)]     // more sections than fit: as many columns as fit
    [InlineData(1200, 1, 1)]    // never more columns than sections
    [InlineData(1200, 0, 1)]    // no sections still lays out one column
    public void AsManySectionColumnsAsFitAtTwoHundredPixels(double width, int sections, int columns) =>
        Assert.Equal(columns, CardLayout.SectionColumns(width, sections));

    [Fact]
    public void AnUnboundedWidthTakesOneColumnPerSection() =>
        Assert.Equal(4, CardLayout.SectionColumns(double.PositiveInfinity, 4));

    [Fact]
    public void ColumnsShareTheWidthEquallyLessTheGaps()
    {
        Assert.Equal(328, CardLayout.ColumnWidth(680, 2));
        Assert.Equal(680, CardLayout.ColumnWidth(680, 1));
    }

    [Fact]
    public void SectionsFillColumnsInTurnSoEachColumnStacksItsOwn() =>
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, Enumerable.Range(0, 5).Select(i => CardLayout.ColumnOf(i, 2)).ToArray());
}
