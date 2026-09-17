using Labs626.UrScore.Board;
using Labs626.UrScore.UI;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace UrScore.Tests;

public class PopOutLifecycleTests
{
    private static readonly PopOutRect Place = new(100, 200, 400, 300);

    private static BoardDef Saved() => new("b-1", "Battle",
        [new PanelDef("p-1", PanelType.Standing, new PanelSize(), new PanelSettings("recipe"), PopOut: Place)]);

    [Theory]
    [InlineData(0x0112, 0xF060, true)]
    [InlineData(0x0112, 0xF063, true)]
    [InlineData(0x0010, 0, false)]
    [InlineData(0x0011, 0, false)]
    [InlineData(0x0112, 0xF020, false)]
    public void OnlyTheUserSystemCloseCommandRequestsReturn(int message, long parameter, bool returns) =>
        Assert.Equal(returns, PopOutLifecycle.IsReturnCommand(message, new IntPtr(parameter)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalCloseKeepsSavedPopOutRegardlessOfMainWindowOrder(bool mainFirst)
    {
        var lifetime = new PopOutLifecycle();
        var saved = Saved();
        var moved = Place with { X = 650 };
        if (mainFirst) lifetime.BeginShutdown();

        var returns = lifetime.Closed("p-1", returnRequested: false);
        var boards = returns ? [BoardEdits.Return(saved, "p-1")] : new[] { saved };
        boards = BoardEdits.PlacePopOuts(boards, new Dictionary<string, PopOutRect> { ["p-1"] = moved }).ToArray();

        Assert.False(returns);
        Assert.False(lifetime.ShouldOpen("p-1", readerLoaded: true));
        Assert.False(lifetime.Closed("p-1", returnRequested: false));
        lifetime.BeginShutdown();
        Assert.Equal(moved, boards[0].Panels[0].PopOut);
        var nextSession = new PopOutLifecycle();
        Assert.True(nextSession.ShouldOpen("p-1", readerLoaded: true));
        Assert.Equal("p-1", Assert.Single(BoardEdits.PoppedOut(boards, null)).Id);
    }

    [Fact]
    public void ExplicitReturnRemovesPlacementAndDoesNotReopenNextSession()
    {
        var lifetime = new PopOutLifecycle();
        var board = Saved();

        Assert.True(lifetime.Closed("p-1", returnRequested: true));
        board = BoardEdits.Return(board, "p-1");
        lifetime.Returned("p-1");

        Assert.Null(board.Panels[0].PopOut);
        Assert.Empty(BoardEdits.PoppedOut([board], null));
        Assert.Equal(BoardDefs.Key(board), BoardDefs.Key(BoardEdits.Return(board, "p-1")));
    }

    [Fact]
    public void ShutdownOverridesUserReturnAndStopsEveryReopen()
    {
        var lifetime = new PopOutLifecycle();
        lifetime.BeginShutdown();

        Assert.False(lifetime.Closed("p-1", returnRequested: true));
        Assert.False(lifetime.ShouldOpen("p-1", readerLoaded: true));
        Assert.False(lifetime.ShouldOpen("p-2", readerLoaded: true));
    }

    [Fact]
    public void ReturningAnExternallyClosedPanelAllowsALaterExplicitPopOut()
    {
        var lifetime = new PopOutLifecycle();
        lifetime.Closed("p-1", returnRequested: false);
        Assert.True(lifetime.IsClosedExternally("p-1"));
        Assert.False(lifetime.ShouldOpen("p-1", readerLoaded: true));

        lifetime.Returned("p-1");

        Assert.False(lifetime.IsClosedExternally("p-1"));
        Assert.True(lifetime.ShouldOpen("p-1", readerLoaded: true));
        Assert.False(lifetime.ShouldOpen("p-1", readerLoaded: false));
    }

    [Theory]
    [InlineData(0x0112, 0xF060, true)]
    [InlineData(0x0010, 0, false)]
    public void NativeCloseIntentIsSeenBeforeWpfClosesTheHiddenWindow(int message, int parameter, bool expectedReturn)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var returnRequested = false;
                var closed = false;
                window = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
                window.SourceInitialized += (_, _) =>
                {
                    HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook(
                        (IntPtr handle, int seenMessage, IntPtr seenParameter, IntPtr data, ref bool handled) =>
                        {
                            if (PopOutLifecycle.IsReturnCommand(seenMessage, seenParameter)) returnRequested = true;
                            return IntPtr.Zero;
                        });
                };
                window.Closed += (_, _) => closed = true;
                var handle = new WindowInteropHelper(window).EnsureHandle();

                SendMessage(handle, message, new IntPtr(parameter), IntPtr.Zero);

                Assert.True(closed);
                Assert.Equal(expectedReturn, returnRequested);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    [Fact]
    public void ProductionWiringMarksShutdownBeforeDraftSaveAndOnlyReturnsExplicitCloses()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var ui = Path.Combine(directory.FullName, "src", "UI");
        var editing = File.ReadAllText(Path.Combine(ui, "BoardWindow.Editing.cs"));
        var popOuts = File.ReadAllText(Path.Combine(ui, "BoardWindow.PopOuts.cs"));
        var window = File.ReadAllText(Path.Combine(ui, "Boards", "PanelPopOutWindow.xaml.cs"));

        Assert.True(editing.IndexOf("_popOutLifecycle.BeginShutdown();", StringComparison.Ordinal)
            < editing.IndexOf("FinishEditing();", StringComparison.Ordinal));
        Assert.Contains("_popOutLifecycle.Closed(window.PanelId, window.ReturnRequested)", popOuts);
        Assert.Contains("!_popOutLifecycle.ShouldOpen(id, _services.ReaderLoaded)", popOuts);
        Assert.Contains("_lastPopOut.Where(pair => _popOutLifecycle.IsClosedExternally(pair.Key))", popOuts);
        Assert.Contains("AddHook(ReadCloseIntent)", window);
        Assert.Contains("if (PopOutLifecycle.IsReturnCommand(message, parameter)) ReturnRequested = true;", window);
        Assert.Contains("ReturnRequested = true;\n        Close();", window.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}