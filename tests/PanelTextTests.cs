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
    public void DurationsAndDatesReadAsTimeNotRawNumbers()
    {
        var utc = TimeZoneInfo.Utc;

        Assert.Equal("586d 5h", PanelText.Value(50_651_629, StatFormat.Duration, utc));
        Assert.Equal("5h 12m", PanelText.Value(18_720, StatFormat.Duration, utc));
        Assert.Equal("12m", PanelText.Value(720, StatFormat.Duration, utc));
        Assert.Equal("13 Sep 2020", PanelText.Value(1_600_000_000, StatFormat.Date, utc));
        Assert.Equal("14,020,550", PanelText.Value(14_020_550, StatFormat.Number, utc));
        Assert.Equal(StatText.Dash, PanelText.Value(null, StatFormat.Duration, utc));
        Assert.Equal(StatText.Dash, PanelText.Value(-5, StatFormat.Date, utc));

        Assert.Equal("+2h 0m", PanelText.Change(7_200, StatFormat.Duration));
        Assert.Equal("-12m", PanelText.Change(-720, StatFormat.Duration));
        Assert.Equal(StatText.Dash, PanelText.Change(86_400, StatFormat.Date));
        Assert.Equal(PanelText.Signed(220_000), PanelText.Change(220_000, StatFormat.Number));
        Assert.Equal(StatText.Dash, PanelText.Change(null, StatFormat.Duration));
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
    public void SourcesAreLabelledByRole()
    {
        Assert.Equal("★ CCGP", PanelText.SourceLabel("CCGP", SourceRole.Main));
        Assert.Equal("K0i2", PanelText.SourceLabel("K0i2", SourceRole.Mine));
        Assert.Equal("NovaForge · watching", PanelText.SourceLabel("NovaForge", SourceRole.Watch));
    }

    [Fact]
    public void WithoutARecipeTitlesStayGeneric()
    {
        Assert.Equal("Standing", PanelText.Title(PanelType.Standing, null, []));
        Assert.Equal("Race", PanelText.Title(PanelType.Race, null, []));
        Assert.Equal("Past periods", PanelText.Title(PanelType.PastPeriods, null, []));
        Assert.Equal("Top of the list", PanelText.Title(PanelType.Top, null, []));
        Assert.Equal("Top of the battle", PanelText.Title(PanelType.Top, null, [BoardFixtures.Installed(BoardFixtures.Clan)]));
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
