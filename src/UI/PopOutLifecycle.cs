namespace Labs626.UrScore.UI;

public sealed class PopOutLifecycle
{
    private readonly HashSet<string> _closedExternally = new(StringComparer.Ordinal);

    public bool ClosingApp { get; private set; }

    public void BeginShutdown() => ClosingApp = true;

    public bool ShouldOpen(string panelId, bool readerLoaded) =>
        readerLoaded && !ClosingApp && !_closedExternally.Contains(panelId);

    public bool IsClosedExternally(string panelId) => _closedExternally.Contains(panelId);

    public bool Closed(string panelId, bool returnRequested)
    {
        if (ClosingApp) return false;
        if (returnRequested) return true;
        _closedExternally.Add(panelId);
        return false;
    }

    public void Returned(string panelId) => _closedExternally.Remove(panelId);

    public static bool IsReturnCommand(int message, IntPtr parameter) =>
        message == 0x0112 && (parameter.ToInt64() & 0xFFF0) == 0xF060;
}