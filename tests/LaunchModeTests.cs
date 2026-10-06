using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// Window or tray at start, from why RoRoRo started Ur Score (<c>RORORO_LAUNCH_REASON</c>, RoRoRo 1.33): only an autostart
/// goes to the tray, and only with the setting on; an update comes back as it was; everything else, an older RoRoRo
/// included, opens the window. A first run always opens the window.
/// </summary>
public class LaunchModeTests
{
    [Theory]
    // autostart: the setting decides, whatever the last mode was.
    [InlineData(LaunchReason.Autostart, true, null, AppMode.Tray)]
    [InlineData(LaunchReason.Autostart, true, "window", AppMode.Tray)]
    [InlineData(LaunchReason.Autostart, true, "tray", AppMode.Tray)]
    [InlineData(LaunchReason.Autostart, false, null, AppMode.Window)]
    [InlineData(LaunchReason.Autostart, false, "window", AppMode.Window)]
    [InlineData(LaunchReason.Autostart, false, "tray", AppMode.Window)]
    // manual, install, unknown: the window, whatever the setting and the last mode.
    [InlineData(LaunchReason.Manual, true, null, AppMode.Window)]
    [InlineData(LaunchReason.Manual, true, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Manual, false, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Manual, false, "window", AppMode.Window)]
    [InlineData(LaunchReason.Install, true, null, AppMode.Window)]
    [InlineData(LaunchReason.Install, true, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Install, false, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Install, false, "window", AppMode.Window)]
    [InlineData(LaunchReason.Unknown, true, null, AppMode.Window)]
    [InlineData(LaunchReason.Unknown, true, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Unknown, false, "tray", AppMode.Window)]
    [InlineData(LaunchReason.Unknown, false, "window", AppMode.Window)]
    // update: as it was when it last ran, whatever the setting; never recorded (or junk) is the window.
    [InlineData(LaunchReason.Update, true, "tray", AppMode.Tray)]
    [InlineData(LaunchReason.Update, false, "tray", AppMode.Tray)]
    [InlineData(LaunchReason.Update, true, "window", AppMode.Window)]
    [InlineData(LaunchReason.Update, false, "window", AppMode.Window)]
    [InlineData(LaunchReason.Update, true, null, AppMode.Window)]
    [InlineData(LaunchReason.Update, false, null, AppMode.Window)]
    [InlineData(LaunchReason.Update, true, "TRAY", AppMode.Tray)]
    [InlineData(LaunchReason.Update, true, "minimised", AppMode.Window)]
    public void DecideFollowsTheReasonTheSettingAndTheLastMode(LaunchReason reason, bool autostartInTray, string? lastMode, AppMode expected)
    {
        var settings = Settings.Defaults with { AutostartInTray = autostartInTray, LastMode = lastMode };

        Assert.Equal(expected, LaunchMode.Decide(reason, settings, firstRunDue: false));
    }

    [Theory]
    [InlineData(LaunchReason.Autostart, null)]
    [InlineData(LaunchReason.Update, "tray")]
    public void AFirstRunIsAlwaysSeen(LaunchReason reason, string? lastMode)
    {
        // Setup would open on its game page to ask for a clan: a start that hid that would read nothing and say nothing.
        var settings = Settings.Defaults with { AutostartInTray = true, LastMode = lastMode };

        Assert.Equal(AppMode.Tray, LaunchMode.Decide(reason, settings, firstRunDue: false));
        Assert.Equal(AppMode.Window, LaunchMode.Decide(reason, settings, firstRunDue: true));
    }

    [Theory]
    [InlineData("autostart", LaunchReason.Autostart)]
    [InlineData("AUTOSTART", LaunchReason.Autostart)]
    [InlineData(" Autostart ", LaunchReason.Autostart)]
    [InlineData("manual", LaunchReason.Manual)]
    [InlineData("Install", LaunchReason.Install)]
    [InlineData("update", LaunchReason.Update)]
    [InlineData(null, LaunchReason.Unknown)]
    [InlineData("", LaunchReason.Unknown)]
    [InlineData("reboot", LaunchReason.Unknown)]
    [InlineData("unknown", LaunchReason.Unknown)]
    [InlineData("3", LaunchReason.Unknown)]
    public void TheReasonParsesCaseInsensitivelyAndAnythingElseIsUnknown(string? value, LaunchReason expected)
    {
        Assert.Equal(expected, LaunchMode.Parse(value));
    }

    [Fact]
    public void TheReasonIsReadOnceAndThenClearedSoNothingUrScoreStartsInheritsIt()
    {
        var environment = new Dictionary<string, string?> { [LaunchMode.ReasonVariable] = "autostart" };
        var reads = 0;

        var reason = LaunchMode.TakeReason(
            name =>
            {
                reads++;
                return environment.GetValueOrDefault(name);
            },
            (name, value) => environment[name] = value);

        Assert.Equal(LaunchReason.Autostart, reason);
        Assert.Equal(1, reads);
        Assert.Null(environment[LaunchMode.ReasonVariable]);
    }

    [Fact]
    public void AnOlderRoRoRoSetsNothingAndTheReasonIsUnknown()
    {
        var environment = new Dictionary<string, string?>();

        var reason = LaunchMode.TakeReason(environment.GetValueOrDefault, (name, value) => environment[name] = value);

        Assert.Equal(LaunchReason.Unknown, reason);
    }

    [Fact]
    public void TheVariableIsRoRoRosName()
    {
        Assert.Equal("RORORO_LAUNCH_REASON", LaunchMode.ReasonVariable);
    }

    [Theory]
    [InlineData(true, false, AppMode.Tray)]
    [InlineData(true, true, AppMode.Window)]
    [InlineData(false, false, AppMode.Window)]
    [InlineData(false, true, AppMode.Window)]
    public void TheModeNowIsTheTrayOnlyWhileATrayStartsBoardIsHidden(bool startedInTray, bool boardShown, AppMode expected)
    {
        Assert.Equal(expected, LaunchMode.Current(startedInTray, boardShown));
    }

    [Theory]
    [InlineData(AppMode.Tray, "tray")]
    [InlineData(AppMode.Window, "window")]
    public void TheModeIsWrittenAsAWord(AppMode mode, string word)
    {
        Assert.Equal(word, LaunchMode.Word(mode));
    }

    [Theory]
    // Started in the tray: the close box hides, unless Windows is ending the session or the tray's Quit is closing.
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    // Started as a window: closing quits, as it always has.
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ClosingHidesToTheTrayOnlyWhenItStartedThere(bool startedInTray, bool endingSession, bool quitting, bool hides)
    {
        Assert.Equal(hides, LaunchMode.HidesOnClose(startedInTray, endingSession, quitting));
    }

    [Theory]
    [InlineData(LaunchReason.Autostart, false)]
    [InlineData(LaunchReason.Manual, true)]
    [InlineData(LaunchReason.Install, true)]
    [InlineData(LaunchReason.Update, true)]
    [InlineData(LaunchReason.Unknown, true)]
    public void ASecondStartBringsTheBoardBackUnlessItIsAnAutostart(LaunchReason reason, bool shows)
    {
        Assert.Equal(shows, LaunchMode.SecondStartShows(reason));
    }

    [Fact]
    public void ANewInstallKeepsScoreInTheTrayOnAutostartAndHasNoLastMode()
    {
        Assert.True(Settings.Defaults.AutostartInTray);
        Assert.Null(Settings.Defaults.LastMode);
    }
}
