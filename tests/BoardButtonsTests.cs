using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The live walk pressed Stop during a Test now read and nothing happened: the press was dropped while the
/// button looked ready. A button is disabled for exactly the time its press would be ignored, and Stop is never
/// held up by a read.
/// </summary>
public class BoardButtonsTests
{
    [Fact]
    public void BeforeTheBookIsReadNeitherStartNorTestNowTakesAPress()
    {
        var states = BoardButtons.For(loaded: false, running: false, starting: false, testing: false, importing: false);

        Assert.False(states.StartStop);
        Assert.False(states.TestNow);
    }

    [Fact]
    public void IdleBothTakeAPress()
    {
        var states = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false);

        Assert.Equal(new BoardButtonStates(StartStop: true, TestNow: true, EmptyState: true), states);
    }

    [Fact]
    public void RunningStopAndTestNowBothTakeAPress()
    {
        var states = BoardButtons.For(loaded: true, running: true, starting: false, testing: false, importing: false);

        Assert.True(states.StartStop);
        Assert.True(states.TestNow);
    }

    [Fact]
    public void TestingWhileStoppedStartWaitsForTheRead()
    {
        var states = BoardButtons.For(loaded: true, running: false, starting: false, testing: true, importing: false);

        Assert.False(states.StartStop);
        Assert.False(states.TestNow);
    }

    [Fact]
    public void TestingWhileRunningStopStillWorks()
    {
        var states = BoardButtons.For(loaded: true, running: true, starting: false, testing: true, importing: false);

        Assert.True(states.StartStop);
        Assert.False(states.TestNow);
    }

    [Fact]
    public void StartingNeitherStartNorTestNowTakesAPress()
    {
        var states = BoardButtons.For(loaded: true, running: false, starting: true, testing: false, importing: false);

        Assert.False(states.StartStop);
        Assert.False(states.TestNow);
    }

    [Fact]
    public void AnImportHoldsOnlyTheEmptyStateButton()
    {
        var importing = BoardButtons.For(loaded: true, running: true, starting: false, testing: false, importing: true);
        var testing = BoardButtons.For(loaded: true, running: false, starting: false, testing: true, importing: false);

        Assert.Equal(new BoardButtonStates(StartStop: true, TestNow: true, EmptyState: false), importing);
        Assert.True(testing.EmptyState);
    }

    [Fact]
    public void TheLastBoardCantBeDeleted()
    {
        var one = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 1);
        var two = BoardButtons.For(loaded: false, running: true, starting: true, testing: true, importing: true, boards: 2);

        Assert.False(one.DeleteBoard);
        Assert.True(two.DeleteBoard);
    }

    [Fact]
    public void OutsideEditModeTheBoardCanBeEditedAndSwitched()
    {
        var states = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 2);

        Assert.True(states.EditBoard);
        Assert.False(states.AddPanel);
        Assert.False(states.Done);
        Assert.True(states.Tabs);
        Assert.True(states.AddBoard);
        Assert.True(states.RenameBoard);
        Assert.True(states.DuplicateBoard);
        Assert.True(states.DeleteBoard);
    }

    [Fact]
    public void WhileEditingOnlyTheDraftsBoardIsReachable()
    {
        // R8: one draft at a time, so the tabs, + Board and the tab menu wait for Done, and Edit board is Done.
        var states = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, boards: 2, editing: true);

        Assert.False(states.EditBoard);
        Assert.True(states.AddPanel);
        Assert.True(states.Done);
        Assert.False(states.Tabs);
        Assert.False(states.AddBoard);
        Assert.False(states.RenameBoard);
        Assert.False(states.DuplicateBoard);
        Assert.False(states.DeleteBoard);
    }

    [Fact]
    public void EditingHoldsUpNeitherReadingNorStop()
    {
        var testing = BoardButtons.For(loaded: true, running: true, starting: false, testing: true, importing: false, editing: true);
        var idle = BoardButtons.For(loaded: true, running: false, starting: false, testing: false, importing: false, editing: true);

        Assert.True(testing.StartStop);
        Assert.False(testing.TestNow);
        Assert.True(idle.StartStop);
        Assert.True(idle.TestNow);
        Assert.True(idle.EmptyState);
    }

    /// <summary>Plan A33: the board starts itself only when pressing Start yourself would have done something.</summary>
    [Theory]
    [InlineData(true, true, false, 1, true, false, true)]     // the one case that reads
    [InlineData(false, true, false, 1, true, false, false)]   // off, which is the default
    [InlineData(true, false, false, 1, true, false, false)]   // the score book hasn't been read yet
    [InlineData(true, true, true, 1, true, false, false)]     // already running
    [InlineData(true, true, false, 0, true, false, false)]    // nothing installed
    [InlineData(true, true, false, 1, false, false, false)]   // every source switched off
    [InlineData(true, true, false, 1, true, true, false)]     // Setup just opened on a recipe with no source
    public void StartingFromOpenNeedsSomethingToRead(
        bool startOnOpen, bool loaded, bool running, int installed, bool anySourceOn, bool firstRunPage, bool starts) =>
        Assert.Equal(starts, BoardButtons.StartsOnOpen(startOnOpen, loaded, running, installed, anySourceOn, firstRunPage));

    [Fact]
    public void StartingFromOpenAgreesWithTheStartButton()
    {
        // Two gates that can drift apart is how a board starts itself while the button that does the same thing is
        // disabled. This one is the button's, plus the reasons a press would have been a no-op.
        Assert.False(BoardButtons.For(loaded: false, running: false, starting: false, testing: false, importing: false).StartStop);
        Assert.False(BoardButtons.StartsOnOpen(startOnOpen: true, loaded: false, running: false, installed: 1, anySourceOn: true, firstRunPage: false));
    }
}
