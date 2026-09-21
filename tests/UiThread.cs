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
}
