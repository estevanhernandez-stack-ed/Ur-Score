namespace Labs626.UrScore.Core;

/// <summary>
/// A task nobody awaits, with its failure sent to the trail instead of lost.
/// <para>
/// <c>_ = SomethingAsync()</c> is the honest way to start work a handler must not wait for — a clan pick, a
/// name lookup, an icon fetch — and it has one hole: a discarded task that faults raises nothing. It does not
/// reach <c>DispatcherUnhandledException</c> the way an <c>async void</c> handler's failure does, it leaves no
/// line, and the work simply did not happen. A bug on one of those paths was invisible (S1-11.1, S1-14.6).
/// </para>
/// <para>
/// This is the one shape those calls now take. The failure's TYPE goes to the trail and nothing more, as every
/// trail line about an exception does (S1-14.14): a message can carry a path, an address with a key in it, or
/// somebody else's words. A cancellation is not a failure — it is the stop the caller asked for — and says
/// nothing.
/// </para>
/// </summary>
public static class Unawaited
{
    /// <param name="what">What the work was, in the trail's own capitals: "CLAN PICK", "NAMES".</param>
    public static void TrailFailures(Task task, Action<string> addTrail, string what) =>
        task.ContinueWith(
            t =>
            {
                // An async method's fault arrives as an AggregateException around the cause; the line names the cause.
                var ex = t.Exception!.InnerException ?? t.Exception;
                if (ex is OperationCanceledException) return;

                addTrail($"{what}: {ex.GetType().Name}");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
