using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// The owner looked at the board and said "I don't see a rename for boards and I don't see a way to delete them"
/// (backlog V3-S.8). All three were there the whole time, on the tab's right-click menu, with nothing on screen
/// to say so. ⋯ beside the tabs opens that same menu; opening it from ⋯ has to leave the right-click exactly
/// where it was, at the mouse, on whatever you clicked.
/// </summary>
public class BoardMenuTests
{
    [Fact]
    public void TheButtonOpensTheMenuUnderItself() => OnSta(() =>
    {
        var menu = new ContextMenu();
        var button = new Button();

        BoardMenu.AimAt(menu, button);

        Assert.Same(button, menu.PlacementTarget);
        Assert.Equal(PlacementMode.Bottom, menu.Placement);
    });

    [Fact]
    public void OnceTheButtonsMenuClosesTheNextRightClickIsBackAtTheMouse() => OnSta(() =>
    {
        var menu = new ContextMenu();
        var button = new Button();
        BoardMenu.AimAt(menu, button);

        var wasTheButtons = BoardMenu.Release(menu, button);

        Assert.True(wasTheButtons);
        Assert.Null(menu.PlacementTarget);
        Assert.Equal(PlacementMode.MousePoint, menu.Placement);
    });

    [Fact]
    public void AMenuTheButtonNeverOpenedIsNotTheButtonsToTakeFocusFrom() => OnSta(() =>
    {
        var menu = new ContextMenu();
        var button = new Button();

        // A right-click on a tab, then Escape: focus stays where the right-click left it, on the tab.
        Assert.False(BoardMenu.Release(menu, button));
        Assert.Null(menu.PlacementTarget);
        Assert.Equal(PlacementMode.MousePoint, menu.Placement);
    });

    [Fact]
    public void EveryCommandOnTheTabsRightClickMenuIsOnTheButtonToo()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml.cs"));

        // One menu, not two. A second list beside the right-click's is how the two come to offer different commands.
        Assert.Single(Regex.Matches(xaml, "<ContextMenu "));
        Assert.Contains("<ListBox.ContextMenu>", xaml);

        // The walk drives these by id, and Shift+F10 still reaches them (tools/smoke/walk-board-editing.ps1 step 9).
        foreach (var command in new[] { "RenameBoardItem", "DuplicateBoardItem", "DeleteBoardItem" })
        {
            Assert.Contains($"x:Name=\"{command}\"", xaml);
        }

        // ⋯ opens that same menu rather than one of its own.
        Assert.Contains("BoardMenu.AimAt(TabMenu, BoardMenuButton);", code);
        Assert.Contains("TabMenu.IsOpen = true;", code);
    }

    [Fact]
    public void TheButtonSitsOnTheTabRowAndSaysWhatItOpens()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UI", "BoardWindow.xaml.cs"));

        var button = Regex.Match(xaml, "<Button x:Name=\"BoardMenuButton\"[^>]*>");
        Assert.True(button.Success, "The board's tab row has no ⋯ button, so its commands are back behind a right-click.");
        Assert.Contains("Click=\"OnBoardMenuClick\"", button.Value);
        Assert.Matches("AutomationProperties\\.Name=\"[^\"]+\"", button.Value);

        // Owner rule (backlog V3-S.10): nothing new wears the Windows look, so no default tooltip.
        Assert.DoesNotContain("ToolTip", button.Value);

        // It goes off with the commands it opens, the way + Board and the tabs do while a board is being edited.
        Assert.Contains("BoardMenuButton.IsEnabled = states.BoardMenu;", code);
    }

    /// <summary>WPF elements need an STA thread; the test runner's threads are MTA.</summary>
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
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
