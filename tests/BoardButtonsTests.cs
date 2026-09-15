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
}
