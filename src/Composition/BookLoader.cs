namespace Labs626.UrScore.Composition;

public sealed class BookLoader
{
    private Task? _loading;

    public Task LoadAsync(Func<Task> load)
    {
        if (_loading is null || _loading.IsFaulted) _loading = RunAsync(load);
        return _loading;
    }

    private static async Task RunAsync(Func<Task> load) => await load();
}