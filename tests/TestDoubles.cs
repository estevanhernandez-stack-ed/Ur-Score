using Grpc.Core;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// A clock tests move by hand — and, since 2026-09-22, the timers that run on it. <see cref="Advance"/> moves the
/// clock through every timer that falls due on the way, firing each at its own moment, so a loop written as
/// <c>await Task.Delay(interval, time, token)</c> takes its next turn when the test says the interval has passed
/// and never sooner. Before this a loop's interval was a minute of real waiting nobody could afford, so the loop
/// itself went untested (S1-8.7, Sweep E).
/// <para>
/// A callback runs on the thread that called <see cref="Advance"/>, and an awaiting continuation may run right
/// there inside it. A callback that creates a timer due within the same advance is served in the same call.
/// </para>
/// </summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = start;

    public DateTimeOffset Now
    {
        get
        {
            lock (_gate) return _now;
        }
        set => Advance(value - Now);
    }

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>How many timers are waiting to fire: a loop between turns holds one.</summary>
    public int Waiting
    {
        get
        {
            lock (_gate) return _timers.Count(t => t.Due is not null);
        }
    }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by), "the clock does not go back");

        DateTimeOffset target;
        lock (_gate) target = _now + by;

        while (true)
        {
            Timer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due is { } due && due <= target).MinBy(t => t.Due!.Value);
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? next.Due + next.Period : null;
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class Timer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._gate)
            {
                Period = period > TimeSpan.Zero ? period : TimeSpan.Zero;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
                if (!time._timers.Contains(this)) time._timers.Add(this);
            }

            return true;
        }

        public void Dispose()
        {
            lock (time._gate) time._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class StubHost(bool reachable, params HostAccount[] accounts) : IHostClient
{
    public bool Reachable { get; set; } = reachable;

    public bool DenyAccounts { get; set; }

    /// <summary>When set, listing the accounts throws this, as a transport failure after a good probe would.</summary>
    public Exception? AccountsFailure { get; set; }

    public int AccountCalls { get; private set; }

    public List<(Guid Subject, string MetricId, double Value, DateTimeOffset ObservedAt)> Reported { get; } = [];

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(Reachable);

    public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        AccountCalls++;
        if (AccountsFailure is not null) return Task.FromException<IReadOnlyList<HostAccount>>(AccountsFailure);
        return DenyAccounts
            ? throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked"))
            : Task.FromResult<IReadOnlyList<HostAccount>>(accounts);
    }

    /// <summary>
    /// Runs as each report is made, so a test can record WHEN it happened against another effect. The list alone
    /// says only that a number went; it cannot say whether the rule's label was written first, which is the one
    /// thing the ordering rule is about (design §1).
    /// </summary>
    public Action<string>? OnReport { get; set; }

    public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        Reported.Add((subject, metricId, value, observedAt));
        OnReport?.Invoke(metricId);
        return Task.CompletedTask;
    }
}

internal sealed class NoKeys : IKeyStore
{
    public SavedKey? Find(string keyId) => null;

    public void Save(string keyId, string host, string value) => throw new NotSupportedException();

    public bool Remove(string keyId) => false;

    public IReadOnlyCollection<string> Values() => [];
}

internal sealed class StubEngine(Func<RecipeReading> read) : IRecipeEngine
{
    public Func<RecipeReading> Read { get; set; } = read;

    public int Calls { get; private set; }

    public IReadOnlyCollection<long> LastIds { get; private set; } = [];

    public Task<RecipeReading> ReadAsync(Recipe recipe, IReadOnlyDictionary<string, string> inputs,
        IReadOnlyCollection<long> accountUserIds, IReadOnlySet<string> trackedStats, CancellationToken cancellationToken)
    {
        Calls++;
        LastIds = accountUserIds;
        return Task.FromResult(Read());
    }
}

internal static class TempDir
{
    /// <summary>A fresh folder under the system temp path, deleted when the returned scope is disposed.</summary>
    public static Scope Create(string prefix) => new(Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}"));

    internal sealed class Scope : IDisposable
    {
        public Scope(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class MemoryBook : IScoreBook
{
    public List<BookLine> Lines { get; } = [];

    public List<string> RecipeTexts { get; } = [];

    public event Action<BookLine>? Written;

    public int Pending => 0;

    public int Dropped => 0;

    public string Root => "memory";

    public void Append(BookLine line, string recipeText)
    {
        Lines.Add(line);
        RecipeTexts.Add(recipeText);
        Written?.Invoke(line);
    }

    public void Flush()
    {
    }
}
