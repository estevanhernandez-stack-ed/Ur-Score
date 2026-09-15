using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class PanelTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    [InlineData(2378, "2,378th")]
    public void OrdinalsReadAsPlaces(int place, string text) => Assert.Equal(text, PanelText.Ordinal(place));

    [Fact]
    public void AMissingValueIsADashNeverAZero()
    {
        Assert.Equal(StatText.Dash, PanelText.Full(null));
        Assert.Equal(StatText.Dash, PanelText.Short(null));
        Assert.Equal(StatText.Dash, PanelText.Signed(null));
        Assert.Equal("14,020,550", PanelText.Full(14_020_550));
    }

    [Fact]
    public void SignedChangesCarryTheirSign()
    {
        Assert.Equal("+" + StatText.Abbrev(220_000), PanelText.Signed(220_000));
        Assert.Equal("-" + StatText.Abbrev(1_500), PanelText.Signed(-1_500));
        Assert.Equal("+" + StatText.Abbrev(0), PanelText.Signed(0));
    }

    [Fact]
    public void RolesHaveChips()
    {
        Assert.Equal("★ main", PanelText.Chip(SourceRole.Main));
        Assert.Equal("yours", PanelText.Chip(SourceRole.Mine));
        Assert.Equal("watching", PanelText.Chip(SourceRole.Watch));
    }

    [Fact]
    public void ThePeriodLineNamesThePeriodItsEndAndTheNextRead()
    {
        var period = new ReadingPeriod("AutumnBattle", Now.AddDays(-2), Now.AddHours(76));

        Assert.Equal(
            $"AutumnBattle · ends in {StatText.Span(TimeSpan.FromHours(76))} · next read in {StatText.Span(TimeSpan.FromMinutes(2))}",
            PanelText.PeriodLine(period, Now, Now.AddMinutes(2)));
        Assert.Equal("AutumnBattle · ended", PanelText.PeriodLine(period with { Ends = Now.AddMinutes(-1) }, Now, null));
        Assert.Equal("next read due", PanelText.PeriodLine(null, Now, Now.AddSeconds(-5)));
    }

    [Fact]
    public void AgoAndStaleTexts()
    {
        Assert.Equal($"{StatText.Span(TimeSpan.FromMinutes(5))} ago", PanelText.Ago(Now.AddMinutes(-5), Now));
        Assert.Equal("never", PanelText.Ago(null, Now));
        Assert.Equal("This panel's clan was removed.", PanelText.StaleSource("clan"));
        Assert.Equal("This panel's stat was removed.", PanelText.StaleStat);
    }
}
