using System.Collections.Concurrent;
using Labs626.UrScore.Book;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Score book spec §4.2: one <see cref="RecipeWatch"/> per enabled source, each on its own recipe's interval.
/// A source keeps its watch for as long as its id exists (F2: a watch built per cycle has a fresh
/// serialization guard, and one observation could then be reported twice).
/// </summary>
public sealed class SourceHost(Func<Source, RecipeWatch?> createWatch, Func<Source, int> intervalSeconds) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecipeSnapshot> _latest = new(StringComparer.Ordinal);
    private CancellationTokenSource? _run;

    /// <summary>Raised on the thread pool after each read, with the source id.</summary>
    public event Action<string, RecipeSnapshot>? SnapshotReady;

    public bool Running
    {
        get
        {
            lock (_gate) return _run is not null;
        }
    }

    public IReadOnlyDictionary<string, RecipeSnapshot> Latest => _latest;

    public RecipeWatch? WatchFor(string sourceId)
    {
        lock (_gate) return _entries.TryGetValue(sourceId, out var entry) ? entry.Watch : null;
    }

    public void Apply(IReadOnlyList<Source> sources)
    {
        lock (_gate)
        {
            var wanted = sources
                .Where(s => s.Enabled)
                .GroupBy(s => s.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var id in _entries.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
            {
                _entries[id].Stop.Cancel();
                _entries.Remove(id);
                _latest.TryRemove(id, out _);
            }

            foreach (var source in wanted.Values)
            {
                if (_entries.TryGetValue(source.Id, out var existing))
                {
                    existing.Source = source;
                    existing.Watch.UpdateSource(source);
                    continue;
                }

                if (createWatch(source) is not { } watch) continue;

                var entry = new Entry(source, watch);
                _entries[source.Id] = entry;
                if (_run is not null) StartLoop(entry, _run.Token);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_run is not null) return;

            _run = new CancellationTokenSource();
            foreach (var entry in _entries.Values) StartLoop(entry, _run.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _run?.Cancel();
            _run = null;
        }
    }

    public Task RunAllNowAsync(string trigger, CancellationToken cancellationToken)
    {
        List<Entry> entries;
        lock (_gate) entries = [.. _entries.Values];

        return Task.WhenAll(entries.Select(entry => RunOneAsync(entry, trigger, cancellationToken)));
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            foreach (var entry in _entries.Values) entry.Stop.Cancel();
        }
    }

    private void StartLoop(Entry entry, CancellationToken runToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(runToken, entry.Stop.Token);
        _ = Task.Run(() => LoopAsync(entry, linked.Token));
    }

    private async Task LoopAsync(Entry entry, CancellationToken cancellationToken)
    {
        var trigger = BookLine.TriggerStart;
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOneAsync(entry, trigger, cancellationToken).ConfigureAwait(false);
            trigger = BookLine.TriggerTimer;

            int seconds;
            try
            {
                seconds = Math.Max(Recipe.MinimumEverySeconds, intervalSeconds(entry.Source));
            }
            catch (Exception)
            {
                // A caller's interval lookup must never end this source's loop; fall back to the floor.
                seconds = Recipe.MinimumEverySeconds;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOneAsync(Entry entry, string trigger, CancellationToken cancellationToken)
    {
        RecipeSnapshot snapshot;
        try
        {
            snapshot = await entry.Watch.RunOnceAsync(cancellationToken, trigger).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // The type only: an exception's message can carry an address with a key in it.
            snapshot = new RecipeSnapshot(WatchState.SourceUnreachable, $"The last read failed ({ex.GetType().Name}).", [], [], 0)
            {
                SourceId = entry.Source.Id,
                NotRecordingReason = "The last read failed.",
            };
        }

        bool written;
        lock (_gate)
        {
            // The identity check and the write must be one atomic step: Apply can remove this entry
            // (or replace it and re-add a same-id one) between a lock-free check and a later write,
            // which would bring a removed source's snapshot back or file an old watch's snapshot
            // under a re-added entry.
            written = _entries.TryGetValue(entry.Source.Id, out var current) && ReferenceEquals(current, entry);
            if (written) _latest[entry.Source.Id] = snapshot;
        }

        if (!written) return;

        try
        {
            SnapshotReady?.Invoke(entry.Source.Id, snapshot);
        }
        catch (Exception)
        {
            // A subscriber's fault must never stop this source's loop.
        }
    }

    private sealed class Entry(Source source, RecipeWatch watch)
    {
        public Source Source { get; set; } = source;

        public RecipeWatch Watch { get; } = watch;

        public CancellationTokenSource Stop { get; } = new();
    }
}
