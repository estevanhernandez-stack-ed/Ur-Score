using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>BC2: one chip says what reading is doing; clicking it shows status and never pauses.</summary>
public class StatusChipTests
{
    [Theory]
    [InlineData(false, false, false, false, ChipState.StartReading)]
    [InlineData(false, false, true, false, ChipState.Starting)]
    [InlineData(true, true, false, false, ChipState.Live)]
    [InlineData(true, true, false, true, ChipState.Trouble)]
    [InlineData(false, true, false, false, ChipState.Paused)]
    [InlineData(false, true, false, true, ChipState.Paused)]   // paused wins: nothing is being read to be in trouble
    public void TheChipSaysWhatReadingIsDoing(bool running, bool everStarted, bool starting, bool trouble, ChipState expected) =>
        Assert.Equal(expected, StatusChip.StateOf(running, everStarted, starting, trouble));

    /// <summary>
    /// Review Focus 3: a read-now on a paused board closes the Start gate, but looking at the status must still work.
    /// Only "Start reading" (which starts) and "Starting" (which is busy) answer to the gate.
    /// </summary>
    [Fact]
    public void TheChipOpensWhateverTheStartGateSays()
    {
        Assert.True(StatusChip.Enabled(ChipState.Paused, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.Live, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.Trouble, loaded: true, startGate: false));
        Assert.False(StatusChip.Enabled(ChipState.StartReading, loaded: true, startGate: false));
        Assert.True(StatusChip.Enabled(ChipState.StartReading, loaded: true, startGate: true));
        Assert.False(StatusChip.Enabled(ChipState.Starting, loaded: true, startGate: true));
        Assert.False(StatusChip.Enabled(ChipState.Live, loaded: false, startGate: true));
    }

    /// <summary>Colour is never the only signal: every state has its own glyph and word, and a name that says it in words.</summary>
    [Fact]
    public void EveryStateHasItsOwnGlyphWordAndName()
    {
        var states = Enum.GetValues<ChipState>().Where(s => s != ChipState.Starting).ToList();

        Assert.Equal(states.Count, states.Select(StatusChip.Glyph).Distinct().Count());
        Assert.Equal(states.Count, states.Select(StatusChip.Word).Distinct().Count());
        Assert.Equal(Enum.GetValues<ChipState>().Length, Enum.GetValues<ChipState>().Select(StatusChip.Name).Distinct().Count());
        Assert.Equal("Reading is paused. Press for status.", StatusChip.Name(ChipState.Paused));
        Assert.Equal("AmberBrush", StatusChip.BrushKey(ChipState.Paused));
        Assert.Equal("MagentaBrush", StatusChip.BrushKey(ChipState.Trouble));
        Assert.NotEqual(StatusChip.BrushKey(ChipState.Paused), StatusChip.BrushKey(ChipState.Trouble));
    }

    [Fact]
    public void APausedBoardSaysSoInItsTitle()
    {
        Assert.Equal("RoRoRo Ur Score (Paused)", StatusChip.Title(ChipState.Paused));
        Assert.All(Enum.GetValues<ChipState>().Where(s => s != ChipState.Paused), s => Assert.Equal("RoRoRo Ur Score", StatusChip.Title(s)));
    }

    /// <summary>BC3: the line under the bar shows only when it has something to say, and A41's sentence always is.</summary>
    [Theory]
    [InlineData(true, ChipState.Live, false, "", false, false)]
    [InlineData(false, ChipState.Live, false, "", false, true)]      // the book is still being read
    [InlineData(true, ChipState.Paused, false, "", false, true)]
    [InlineData(true, ChipState.StartReading, false, "", false, true)]
    [InlineData(true, ChipState.Trouble, false, "", false, true)]
    [InlineData(true, ChipState.Live, true, "", false, true)]        // a press failed
    [InlineData(true, ChipState.Live, false, "Over budget", false, true)]
    [InlineData(true, ChipState.Live, false, "", true, true)]        // numbers from the score book (A41)
    public void TheLinesUnderTheBarShowOnlyWhenTheyMatter(bool loaded, ChipState state, bool failed, string detail, bool remembered, bool shows) =>
        Assert.Equal(shows, StatusChip.ShowsLines(loaded, state, failed, detail, remembered));

    [Fact]
    public void TheTabsTakeTheirShareOfWhatIsLeftAndNeverLessThanNothing()
    {
        Assert.Equal(300, StatusChip.TabBudget(1000, 500, 0.6), 6);
        Assert.Equal(0, StatusChip.TabBudget(400, 500, 0.6));
    }

    [Fact]
    public void TheCloseQuestionIsAskedOnlyWhileReadingAndSending()
    {
        Assert.True(BoardText.AsksBeforeClose(running: true, sending: true));
        Assert.False(BoardText.AsksBeforeClose(running: false, sending: true));
        Assert.False(BoardText.AsksBeforeClose(running: true, sending: false));
    }

    [Fact]
    public void TheCardSaysWhetherAlertsAreGoingOut()
    {
        Assert.Equal("Phone alerts: sending.", BoardText.AlertsLine(running: true, sending: true, hostDown: false));
        Assert.Equal("Phone alerts: off while reading is paused.", BoardText.AlertsLine(running: false, sending: true, hostDown: false));
        Assert.Equal("Phone alerts: nothing is set to send.", BoardText.AlertsLine(running: true, sending: false, hostDown: false));
        Assert.Equal("Phone alerts: RoRoRo is not running, so nothing is sent.", BoardText.AlertsLine(running: true, sending: true, hostDown: true));
    }

    [Fact]
    public void TheCardHasALinePerSwitchedOnSource()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = Snapshot(MainClan.Id, []),
            [AltClan.Id] = new RecipeSnapshot(WatchState.SourceUnreachable, "timed out", [], [], 0) { SourceId = AltClan.Id },
        };
        var live = Live([MainClan, AltClan], [Installed(Clan, "value")], snaps, running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-2) });

        var rows = BoardText.CardRows(live);

        Assert.Equal(2, rows.Count);
        Assert.Equal(live.SourceName(MainClan), rows[0].Name);
        // "· next in …" or "· next read due", depending on the fixture recipe's interval; either way it was read 2m ago.
        Assert.StartsWith("read 2m ago · next", rows[0].Line, StringComparison.Ordinal);
        Assert.Equal("Could not reach the data.", rows[1].Line);
        Assert.True(BoardText.InTrouble(live));
        Assert.False(BoardText.InTrouble(Live([MainClan], [Installed(Clan, "value")], snaps, running: true)));
    }

    /// <summary>
    /// RoRoRo down is trouble for the chip though the state line counts it as healthy (reading goes on, nothing reaches the
    /// phone), and the card still has the source's own line.
    /// </summary>
    [Fact]
    public void RoRoRoDownIsTroubleAndTheCardStillListsTheSource()
    {
        var snaps = new Dictionary<string, RecipeSnapshot>
        {
            [MainClan.Id] = new RecipeSnapshot(WatchState.HostDown, "RoRoRo is not running.", [], [], 0) { SourceId = MainClan.Id },
        };
        var live = Live([MainClan], [Installed(Clan, "value")], snaps, running: true,
            lastRead: new Dictionary<string, DateTimeOffset> { [MainClan.Id] = Now.AddMinutes(-2) });

        Assert.True(BoardText.InTrouble(live));
        var row = Assert.Single(BoardText.CardRows(live));
        Assert.Equal(live.SourceName(MainClan), row.Name);
        Assert.Equal(ChipState.Trouble, StatusChip.StateOf(live.Running, everStarted: true, starting: false, BoardText.InTrouble(live)));
    }
}
