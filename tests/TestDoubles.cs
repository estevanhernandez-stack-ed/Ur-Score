using Grpc.Core;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Host;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class StubHost(bool reachable, params HostAccount[] accounts) : IHostClient
{
    public bool Reachable { get; set; } = reachable;

    public bool DenyAccounts { get; set; }

    public int AccountCalls { get; private set; }

    public List<(Guid Subject, string MetricId, double Value, DateTimeOffset ObservedAt)> Reported { get; } = [];

    public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(Reachable);

    public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        AccountCalls++;
        return DenyAccounts
            ? throw new RpcException(new Status(StatusCode.PermissionDenied, "revoked"))
            : Task.FromResult<IReadOnlyList<HostAccount>>(accounts);
    }

    public Task ReportMetricAsync(Guid subject, string metricId, double value, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        Reported.Add((subject, metricId, value, observedAt));
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
