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

    [Fact]
    public void NewPopOutsCascadeFromTheBoardsTopRight()
    {
        var board = new PopOutRect(100, 100, 1280, 900);

        Assert.Equal(new PopOutRect(996, 172, 360, 300), PopOutPlacement.Default(board, 0, Screen));
        Assert.Equal(new PopOutRect(968, 200, 360, 300), PopOutPlacement.Default(board, 1, Screen));
        Assert.Equal(new PopOutRect(1560, 780, 360, 300), PopOutPlacement.Default(new PopOutRect(1500, 800, 1280, 900), 0, Screen));
    }
}
