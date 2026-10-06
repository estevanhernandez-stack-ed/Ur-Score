using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The tray's pure parts (tray mode, RoRoRo 1.33): its tooltip from the status chip's state, its three lines, what each does,
/// and the one balloon on the first hide. <see cref="Tray"/> only draws these; no test builds a real tray icon.
/// </summary>
public class TrayModelTests
{
    [Theory]
    [InlineData(ChipState.Live, "Ur Score · keeping score")]
    [InlineData(ChipState.Trouble, "Ur Score · keeping score")]
    [InlineData(ChipState.Starting, "Ur Score · keeping score")]
    [InlineData(ChipState.Paused, "Ur Score · paused")]
    [InlineData(ChipState.StartReading, "Ur Score · paused")]
    public void TheTooltipSaysWhetherItIsKeepingScore(ChipState chip, string tooltip)
    {
        Assert.Equal(tooltip, TrayModel.Tooltip(chip));
    }

    [Fact]
    public void WhileReadingTheMenuOffersOpenPauseAndQuit()
    {
        var menu = TrayModel.Menu(running: true, canPauseOrResume: true);

        Assert.Equal(["Open board", "Pause reading", "", "Quit"], menu.Select(i => i.Header));
        Assert.Equal([TrayAction.Open, TrayAction.PauseOrResume, TrayAction.None, TrayAction.Quit], menu.Select(i => i.Action));
        Assert.True(menu[2].IsSeparator);
        Assert.All(menu, i => Assert.True(i.IsEnabled));
    }

    [Fact]
    public void WhilePausedItOffersResumeAndOnlyWhenTheBoardsPauseWouldTakeAPress()
    {
        Assert.Equal("Resume reading", TrayModel.Menu(running: false, canPauseOrResume: true)[1].Header);

        // The status card's Pause/Resume is disabled for a read in flight or a book not yet read; the tray's line is too.
        var busy = TrayModel.Menu(running: false, canPauseOrResume: false)[1];
        Assert.Equal(("Resume reading", false), (busy.Header, busy.IsEnabled));
    }

    [Fact]
    public void EachLineGoesWhereItSays()
    {
        var calls = new List<string>();
        var targets = new TrayTargets(() => calls.Add("open"), () => calls.Add("pause"), () => calls.Add("quit"));

        foreach (var item in TrayModel.Menu(running: true, canPauseOrResume: true)) TrayModel.Choose(item, targets);

        Assert.Equal(["open", "pause", "quit"], calls);
    }

    [Fact]
    public void TheFirstHideSaysWhereItWentOnceASession()
    {
        var model = new TrayModel();

        var first = model.NoticeOnHide();
        Assert.NotNull(first);
        Assert.Contains("tray icon", first, StringComparison.Ordinal);
        Assert.DoesNotContain("—", first, StringComparison.Ordinal);
        Assert.EndsWith(".", first, StringComparison.Ordinal);
        Assert.Null(model.NoticeOnHide());
    }
}
