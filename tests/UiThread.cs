namespace UrScore.Tests;

/// <summary>
/// How long a test will wait for the STA thread it started before calling it a failure.
/// <para>
/// Eight tests in this suite run their WPF work on an STA thread and wait for it. Until 2026-09-21 they all waited
/// forever: <c>Thread.Join()</c> with no timeout cannot fail, it can only not return. On 2026-09-17 six of those
/// eight were stuck on one simultaneously and the run hung with the host idle, which took a human noticing, a
/// process hunt and finally a leftover blame sequence file to explain (V3-S.38).
/// </para>
/// <para>
/// Generous on purpose. This is not a performance budget — these tests finish in milliseconds — it is the line past
/// which waiting has stopped being waiting. A number small enough to trip on a loaded machine would trade a rare
/// hang for a common false failure, which is the worse bargain.
/// </para>
/// </summary>
internal static class UiThread
{
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Run <paramref name="work"/> on an STA thread and wait <see cref="Longest"/> for it, failing by name if it
    /// does not finish and rethrowing whatever it threw with its original stack.
    /// <para>
    /// WPF controls can only be built on an STA thread, and eight test files each grew their own copy of this:
    /// a thread, a captured exception, a join, a rethrow. This is that shape written once, for tests written from
    /// here on. The existing eight are left alone deliberately — rewriting them would be a large diff across
    /// files whose bounded joins were only just proven, and proving them again is the cost of tidying them.
    /// </para>
    /// </summary>
    public static void Run(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(Longest), $"the UI thread did not finish within {Longest}");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
