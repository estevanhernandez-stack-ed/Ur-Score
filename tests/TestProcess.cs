using System.Reflection;
using System.Runtime.CompilerServices;
using Labs626.UrScore.Core;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("UrScore.Tests.FencedTestFramework", "Ur-Score.Tests")]

namespace UrScore.Tests;

/// <summary>
/// The test process, fenced at run time (port of K0ii Score's fe8d103, where the harness's <c>new App</c> had started the
/// app for real inside every suite run: WPF queues the app's start onto the dispatcher the harness runs). So before any
/// test runs, this switches the app's start off (<c>App.HostedByTests</c>) and latches the real data folder shut
/// (<see cref="AppPaths.RefuseDefault"/>): <c>AppPaths.Default</c>, every store's default path, and the app's own
/// composition all throw in this process. After the last test, <see cref="FencedTestFramework"/> checks the app never
/// started and was never composed.
/// </summary>
internal static class TestProcess
{
    [ModuleInitializer]
#pragma warning disable CA2255 // The one place a test assembly must act before any test: that is what the attribute is for.
    internal static void Initialize()
#pragma warning restore CA2255
    {
        Labs626.UrScore.App.HostedByTests = true;
        AppPaths.RefuseDefault();
    }

    /// <summary>Why this process broke the fence, or null: the app's start ran, or its own composition was built.</summary>
    internal static string? Breach(int startupsRun, int ownCompositions) =>
        startupsRun == 0 && ownCompositions == 0
            ? null
            : $"TEST PROCESS FENCE: Ur Score started {startupsRun} time(s) and composed itself over the real data folder "
              + $"{ownCompositions} time(s) inside the test run. Nothing in a test may start the app or build AppServices(dispatcher).";

    internal static string? BreachNow() => Breach(Labs626.UrScore.App.StartupsRun, Labs626.UrScore.Composition.AppServices.OwnCompositions);
}

/// <summary>
/// xunit 2.9's own framework, with one step added after the last test collection: the end-of-run check of
/// <see cref="TestProcess.BreachNow"/>. xunit 2 has no assembly fixture (that is v3), and K0ii tried a <c>ProcessExit</c>
/// check first and proved it useless: the test host's exit comes after the runner has taken the results, so
/// <c>dotnet test</c> still exited 0. Here a breach is reported to the runner as an error message and counted as a failure,
/// which fails the run. A breach can still print "Passed!" for every test, so judge a run by its exit code.
/// </summary>
public sealed class FencedTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(
            IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new Runner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class Runner(
        ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink,
        IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        protected override async Task<RunSummary> RunTestCollectionsAsync(IMessageBus messageBus, CancellationTokenSource cancellationTokenSource)
        {
            var summary = await base.RunTestCollectionsAsync(messageBus, cancellationTokenSource);

            if (TestProcess.BreachNow() is { } why)
            {
                messageBus.QueueMessage(new ErrorMessage(TestCases, new InvalidOperationException(why)));
                summary.Failed++;
                summary.Total++;
            }

            return summary;
        }
    }
}
