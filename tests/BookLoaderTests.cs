using Labs626.UrScore.Composition;

namespace UrScore.Tests;

public class BookLoaderTests
{
    [Fact]
    public async Task AFailedLoadCanRetryAndSuccessIsAppliedOnlyOnce()
    {
        var loader = new BookLoader();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var applied = 0;
        async Task Load()
        {
            attempts++;
            await (attempts == 1 ? first.Task : retry.Task);
            applied++;
        }

        var failed = loader.LoadAsync(Load);
        Assert.Same(failed, loader.LoadAsync(Load));
        first.SetException(new IOException("Injected unreadable score book"));
        await Assert.ThrowsAsync<IOException>(() => failed);
        Assert.Equal(0, applied);

        var succeeded = loader.LoadAsync(Load);
        Assert.NotSame(failed, succeeded);
        Assert.Same(succeeded, loader.LoadAsync(Load));
        retry.SetResult();
        await succeeded;

        Assert.Same(succeeded, loader.LoadAsync(Load));
        Assert.Equal(2, attempts);
        Assert.Equal(1, applied);
    }

    [Fact]
    public void BoardWindowsAndSlotsWaitForTheSuccessfulReaderLoad()
    {
        var root = RepoRoot();
        var board = File.ReadAllText(Path.Combine(root, "src", "UI", "BoardWindow.xaml.cs"));
        var popOuts = File.ReadAllText(Path.Combine(root, "src", "UI", "BoardWindow.PopOuts.cs"));
        var services = File.ReadAllText(Path.Combine(root, "src", "Composition", "AppServices.cs"));

        Assert.Contains("if (_services.ReaderLoaded && key != _renderedKey)", board);
        Assert.Contains("if (IsLoaded && _services.ReaderLoaded) SyncPopOuts();", board);
        Assert.Contains("if (!await ReadBookAsync() || _popOutLifecycle.ClosingApp) return;", board);
        Assert.Contains("BoardScroll.Visibility = empty || !_services.ReaderLoaded ? Visibility.Collapsed : Visibility.Visible;", board);
        Assert.Contains("if (_popOutLifecycle.ClosingApp || !_services.ReaderLoaded) return;", popOuts);
        Assert.Contains("public Task LoadBookAsync() => _bookLoader.LoadAsync(LoadBookOnceAsync);", services);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}