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

    // ---- What a pop-out opens at (V3-S.14) ----
    // One size for every panel put a four-column table in a 360 x 300 window, where every column clipped at once and
    // five of 23 rows showed. A pop-out now opens at what the panel already has on the board it came from.

    /// <summary>The gap <c>PanelGrid</c> leaves between cells, and the grid's width on a maximized board at 1920.</summary>
    private const double Gap = 12;
    private const double BoardGrid = 1875;

    [Fact]
    public void APanelOpensAtTheWidthItsOwnSpanHasOnTheBoard()
    {
        var small = PopOutPlacement.SizeFor(PanelSize.Small, BoardGrid, Gap, 400);
        var half = PopOutPlacement.SizeFor(PanelSize.Half, BoardGrid, Gap, 400);
        var wide = PopOutPlacement.SizeFor(PanelSize.Wide, BoardGrid, Gap, 400);

        // The same column maths the board lays the panel out with, and the window's own furniture around it.
        Assert.Equal(BoardLayout.CellWidth(BoardGrid, PanelSize.Half, Gap) + PopOutPlacement.ChromeWidth, half.Width, 6);
        Assert.True(small.Width < half.Width, $"small {small.Width} should be narrower than half {half.Width}");
        Assert.True(half.Width < wide.Width, $"half {half.Width} should be narrower than wide {wide.Width}");

        // Past battles is a half-width panel: four columns and an attributed best need more than the old one size.
        Assert.True(half.Width > PopOutPlacement.DefaultWidth * 2,
            $"a half-width panel opened at {half.Width}, barely more than the old {PopOutPlacement.DefaultWidth} for everything.");

        // A short list of label/value pairs is fine small: a 3-wide panel stays near the old size, not far above it.
        Assert.True(small.Width < PopOutPlacement.DefaultWidth * 2, $"a 3-wide panel opened at {small.Width}");
    }

    [Fact]
    public void APanelOpensAtTheHeightItAskedFor()
    {
        // 23 rows of a table ask for far more than 300; a four-line list asks for far less.
        Assert.Equal(880 + PopOutPlacement.ChromeHeight, PopOutPlacement.SizeFor(PanelSize.Half, BoardGrid, Gap, 880).Height, 6);
        Assert.Equal(154 + PopOutPlacement.ChromeHeight, PopOutPlacement.SizeFor(PanelSize.Small, BoardGrid, Gap, 154).Height, 6);
    }

    [Fact]
    public void ABoardOrAPanelNothingHasMeasuredFallsBackToTheOneOldSize()
    {
        Assert.Equal((PopOutPlacement.DefaultWidth, PopOutPlacement.DefaultHeight), PopOutPlacement.SizeFor(PanelSize.Half, 0, Gap, 0));
        Assert.Equal((PopOutPlacement.DefaultWidth, PopOutPlacement.DefaultHeight), PopOutPlacement.SizeFor(PanelSize.Half, double.NaN, Gap, double.NaN));
    }

    [Fact]
    public void ANarrowBoardWidensAPopOutTheWayItWidensThePanel()
    {
        // Under BoardLayout.SingleWidth every panel takes the whole row, so every pop-out opens at the row's width.
        Assert.Equal(PopOutPlacement.SizeFor(PanelSize.Wide, 600, Gap, 400), PopOutPlacement.SizeFor(PanelSize.Small, 600, Gap, 400));

        // And a panel on a narrow board is never wider than one on a roomy board.
        Assert.True(PopOutPlacement.SizeFor(PanelSize.Half, 900, Gap, 400).Width < PopOutPlacement.SizeFor(PanelSize.Half, BoardGrid, Gap, 400).Width);
    }

    [Fact]
    public void APopOutSizedFromItsPanelStillOpensWhollyOnTheScreen()
    {
        // A wide panel with 60 rows: bigger than the screen, so the screen wins and the window is still reachable.
        var size = PopOutPlacement.SizeFor(PanelSize.Wide, 3000, Gap, 4000);
        var rect = PopOutPlacement.Default(new PopOutRect(0, 0, 1920, 1040), 0, [Screen], size.Width, size.Height);

        Assert.True(size.Width > Screen.W && size.Height > Screen.H, "the panel asked for more than the screen");
        Assert.Equal(new PopOutRect(0, 0, Screen.W, Screen.H), rect);
    }

    [Fact]
    public void TheChromeIsWhatThePopOutWindowActuallyPutsAroundThePanel()
    {
        // The two constants are the window's own furniture added up. If the window's XAML changes, they must too,
        // or every pop-out opens that much too small or too large.
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "Boards", "PanelPopOutWindow.xaml"));

        Assert.Contains("BorderThickness=\"1\"", xaml);
        Assert.Contains("Height=\"30\"", xaml);          // the title strip
        Assert.Contains("Padding=\"6\"", xaml);          // the scroller around the panel
        Assert.Equal(1 + 1 + 6 + 6 + 17, PopOutPlacement.ChromeWidth);
        Assert.Equal(1 + 1 + 30 + 6 + 6, PopOutPlacement.ChromeHeight);

        // A pop-out draws its panel at the board's size, which is what SizeFor works its height out from.
        Assert.Contains("FontSize=\"13\"", xaml);
        Assert.Contains("FontSize=\"13\"", File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml")));

        // And the window is still the user's to resize, at whatever size it opened.
        Assert.Contains("ResizeMode=\"CanResize\"", xaml);
    }

    [Fact]
    public void ASizedPopOutStillCascadesFromTheBoardsTopRight()
    {
        var board = new PopOutRect(100, 100, 1280, 900);

        Assert.Equal(new PopOutRect(756, 172, 600, 500), PopOutPlacement.Default(board, 0, [Screen], 600, 500));
        Assert.Equal(new PopOutRect(728, 200, 600, 500), PopOutPlacement.Default(board, 1, [Screen], 600, 500));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
