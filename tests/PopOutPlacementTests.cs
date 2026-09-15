using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class PopOutPlacementTests
{
    private static readonly PopOutRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void AWindowOnScreenStaysWhereItWas() =>
        Assert.Equal(new PopOutRect(100, 100, 360, 300), PopOutPlacement.Clamp(new PopOutRect(100, 100, 360, 300), Screen));

    [Fact]
    public void AWindowOffTheScreenIsPulledBackOn()
    {
        Assert.Equal(new PopOutRect(1560, 100, 360, 300), PopOutPlacement.Clamp(new PopOutRect(3000, 100, 360, 300), Screen));
        Assert.Equal(new PopOutRect(0, 780, 360, 300), PopOutPlacement.Clamp(new PopOutRect(-900, 2000, 360, 300), Screen));
    }

    [Fact]
    public void AMonitorLeftOfTheMainOneCounts() =>
        Assert.Equal(new PopOutRect(-1800, 50, 400, 300), PopOutPlacement.Clamp(new PopOutRect(-1800, 50, 400, 300), new PopOutRect(-1920, 0, 3840, 1080)));

    [Fact]
    public void SizeStaysBetweenTheSmallestUsefulAndTheScreen()
    {
        Assert.Equal(new PopOutRect(10, 10, PopOutPlacement.MinWidth, PopOutPlacement.MinHeight), PopOutPlacement.Clamp(new PopOutRect(10, 10, 100, 50), Screen));
        Assert.Equal(Screen, PopOutPlacement.Clamp(new PopOutRect(0, 0, 5000, 3000), Screen));
    }

    [Fact]
    public void ANonsenseRectOpensAtTheScreensCornerAtTheDefaultSize() =>
        Assert.Equal(
            new PopOutRect(0, 0, PopOutPlacement.DefaultWidth, PopOutPlacement.DefaultHeight),
            PopOutPlacement.Clamp(new PopOutRect(double.NaN, 10, double.PositiveInfinity, 300), Screen));

    /// <summary>An L: the main screen, and a shorter one to its right raised 600 up, leaving a corner below it that no screen covers.</summary>
    private static readonly IReadOnlyList<PopOutRect> LShaped = [new(0, 0, 1920, 1080), new(1920, -600, 1280, 1024)];

    [Fact]
    public void AWindowInTheCornerNoScreenCoversMovesOntoTheNearestScreen() =>
        Assert.Equal(new PopOutRect(2400, 124, 360, 300), PopOutPlacement.Clamp(new PopOutRect(2400, 700, 360, 300), LShaped));

    [Fact]
    public void AWindowAcrossTwoScreensWithItsTitleOnOneStaysPut()
    {
        Assert.Equal(new PopOutRect(1700, 200, 400, 300), PopOutPlacement.Clamp(new PopOutRect(1700, 200, 400, 300), LShaped));

        // Its foot hangs into the empty corner, but its title strip is on the right-hand screen, where it can be dragged.
        Assert.Equal(new PopOutRect(1800, 300, 400, 300), PopOutPlacement.Clamp(new PopOutRect(1800, 300, 400, 300), LShaped));
    }

    [Fact]
    public void AWindowOnTheSecondScreenStaysThereSizedToFitIt()
    {
        Assert.Equal(new PopOutRect(2200, -500, 500, 400), PopOutPlacement.Clamp(new PopOutRect(2200, -500, 500, 400), LShaped));
        Assert.Equal(new PopOutRect(2000, -500, 1280, 1024), PopOutPlacement.Clamp(new PopOutRect(2000, -500, 2000, 1500), LShaped));
    }

    [Fact]
    public void AWindowWithTooLittleOfItsTitleOnAScreenIsPulledWhollyOn()
    {
        // The title strip above the top of the screen.
        Assert.Equal(new PopOutRect(100, 0, 360, 300), PopOutPlacement.Clamp(new PopOutRect(100, -100, 360, 300), LShaped));

        // Only 100 of its title on the right-hand edge of the right-hand screen.
        Assert.Equal(new PopOutRect(2840, -300, 360, 300), PopOutPlacement.Clamp(new PopOutRect(3100, -300, 360, 300), LShaped));
    }

    [Fact]
    public void AWindowFarOffEveryScreenComesBackOnTheNearest() =>
        Assert.Equal(new PopOutRect(0, 780, 360, 300), PopOutPlacement.Clamp(new PopOutRect(-5000, 5000, 360, 300), LShaped));

    [Fact]
    public void ANewPopOutOpensWhollyOnTheScreenMostOfItFallsOn()
    {
        Assert.Equal(new PopOutRect(2816, -528, 360, 300), PopOutPlacement.Default(new PopOutRect(1920, -600, 1280, 1024), 0, LShaped));

        // Mostly on the main screen, hanging off its right edge: not just its title, all of it comes on.
        Assert.Equal(new PopOutRect(1560, 172, 360, 300), PopOutPlacement.Default(new PopOutRect(800, 100, 1280, 900), 0, LShaped));

        // In the corner no screen covers: onto the nearest.
        Assert.Equal(new PopOutRect(2396, 124, 360, 300), PopOutPlacement.Default(new PopOutRect(1500, 500, 1280, 900), 0, LShaped));
    }

    [Fact]
    public void NewPopOutsCascadeFromTheBoardsTopRight()
    {
        var board = new PopOutRect(100, 100, 1280, 900);

        Assert.Equal(new PopOutRect(996, 172, 360, 300), PopOutPlacement.Default(board, 0, Screen));
        Assert.Equal(new PopOutRect(968, 200, 360, 300), PopOutPlacement.Default(board, 1, Screen));
        Assert.Equal(new PopOutRect(1560, 780, 360, 300), PopOutPlacement.Default(new PopOutRect(1500, 800, 1280, 900), 0, Screen));
    }
}
