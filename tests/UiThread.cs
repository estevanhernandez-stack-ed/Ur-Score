namespace UrScore.Tests;

/// <summary>
/// The collection every test class that touches WPF belongs to — a control, a window, a XAML parse, the
/// application — so no two of them run at the same time.
/// <para>
/// WHY, with the evidence. On 2026-09-17 six STA tests blocked together and the run hung (V3-S.38 got the bounded
/// join; V3-S.40 asked why). On 2026-09-22 it happened again, with the suite four seconds long and nine WPF classes
/// starting cold within the same instant, and this time the stacks were taken (<c>dotnet-stack</c>, 28 threads).
/// Every stuck STA thread was inside WPF's FIRST-TOUCH static initialisation: <c>ContentPresenter..cctor</c> under
/// <c>ItemsControl..cctor</c> under a XAML parse on one thread, <c>WpfSharedBamlSchemaContext.GetKnownXamlType</c>
/// on the others, <c>Application</c>'s constructor on a third. A type's static constructor runs under a lock the
/// runtime holds per type, and WPF's schema context takes locks of its own inside those constructors; two threads
/// initialising overlapping sets of WPF types at once can each hold what the other needs, and the runtime does not
/// break that cycle. It is not one test's fault and no test can be written to avoid it: it is what happens when
/// WPF is started on several threads in the same instant, which a fast parallel suite does by default.
/// </para>
/// <para>
/// So WPF tests run one class at a time. The cost is nothing anybody can measure — they take milliseconds each —
/// and the alternative was a run that hung on a quiet machine roughly once a week. The one <c>Application</c> per
/// process (WPF permits no second; the flag is never cleared) is built by <see cref="UiThread.RunInApp"/> on its own
/// thread and shared, which this collection also protects. A WPF-touching class outside it is what
/// <c>WpfCollectionFenceTests</c> is for.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfCollection
{
    public const string Name = "WPF, one class at a time";
}

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

    /// <summary>
    /// Run <paramref name="work"/> inside the process's one WPF <c>Application</c>, with its resources loaded, for
    /// a control whose XAML reaches for an application-level style (<c>Muted</c>, <c>Refusal</c>) and cannot be
    /// built without one. Bounded by <see cref="Longest"/> like <see cref="Run"/>, and rethrows the same way.
    /// <para>
    /// ONE application, built on first use on its own STA thread with a running dispatcher, and never shut down:
    /// WPF permits one per process and does not permit a second after the first is gone (see
    /// <see cref="WpfCollection"/>). Every caller's work is invoked onto that dispatcher in turn, so two
    /// classes using this cannot collide whatever the runner does with them. <c>Application.Current</c> is
    /// therefore non-null for the rest of the process once any test has called this; the only code that reads it
    /// is <c>ThemeService</c>, which replaces brushes in its resources — harmless to a test application.
    /// </para>
    /// </summary>
    public static void RunInApp(Action work)
    {
        var dispatcher = Application.Value;
        var operation = dispatcher.InvokeAsync(work);
        if (!operation.Task.Wait(Longest))
        {
            operation.Abort();
            Assert.Fail($"the application's thread did not finish within {Longest}");
        }

        if (operation.Task.Exception?.InnerException is { } failure)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static readonly Lazy<System.Windows.Threading.Dispatcher> Application = new(() =>
    {
        var ready = new TaskCompletionSource<System.Windows.Threading.Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Labs626.UrScore.App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                application.InitializeComponent();
                ready.SetResult(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            }
            catch (Exception exception)
            {
                ready.SetException(exception);
                return;
            }

            System.Windows.Threading.Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF application (tests)",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }, LazyThreadSafetyMode.ExecutionAndPublication);
}
