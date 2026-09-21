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
                // Cancelled, then dropped. Its own loop disposes the linked source it is holding, so the only
                // thing left to release here is this one (S1-8.1). Not disposed here: the loop may be between
                // the cancel and its finally, and disposing a source another thread is still linked to is a
                // race for no gain. It is handed to the loop instead, which owns the rest of its lifetime.
                var going = _entries[id];
                going.Stop.Cancel();
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
        CancellationTokenSource? stopping;
        lock (_gate)
        {
            stopping = _run;
            _run = null;
        }

        if (stopping is null) return;

        // Cancelled OUTSIDE the lock. Cancel runs every linked source's callbacks on this thread, and doing that
        // while holding the gate means a callback that ever wanted the gate would deadlock against itself. None
        // does today; this is the cheap way to keep that from being a question anybody has to re-answer.
        stopping.Cancel();

        // Each Start made one of these and each Stop dropped it on the floor, so a session that started and
        // stopped reading repeatedly accumulated them along with one linked source per entry per start (S1-8.1).
        // Safe to dispose now: the loops link to it, and a linked source outlives the parent it was built from.
        stopping.Dispose();
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

        // The loop owns the linked source and releases it on the way out, whichever way it leaves — cancelled,
        // faulted, or simply finished. Before this, a Start/Stop cycle left one per entry behind, and each one
        // holds a registration on both of its parents (S1-8.1).
        _ = Task.Run(async () =>
        {
            try
            {
                await LoopAsync(entry, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                linked.Dispose();
            }
        });
    }

    private async Task LoopAsync(Entry entry, CancellationToken cancellationToken)
    {
        var trigger = BookLine.TriggerStart;
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOneAsync(entry, trigger, cancellationToken).ConfigureAwait(false);
            trigger = BookLine.TriggerTimer;

            var seconds = DelaySeconds(intervalSeconds, entry.Source);

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

    /// <summary>
    /// How long a source waits before its next read: what the caller's lookup says, never below
    /// <see cref="Recipe.MinimumEverySeconds"/>, and the floor again if the lookup throws.
    /// <para>
    /// A method of its own so both rules can be tested without waiting out an interval. The floor is a minute, so
    /// a test that drove the real loop would have to sit through one, and a rule nobody can afford to test is a
    /// rule that quietly stops holding. The clamp protects the sources a recipe asks to be read too often; the
    /// catch protects the LOOP — a caller's lookup throwing must never be the thing that ends a source's reading,
    /// because the failure would be total, permanent and silent (S1-8.7).
    /// </para>
    /// </summary>
    internal static int DelaySeconds(Func<Source, int> intervalSeconds, Source source)
    {
        try
        {
            return Math.Max(Recipe.MinimumEverySeconds, intervalSeconds(source));
        }
        catch (Exception)
        {
            return Recipe.MinimumEverySeconds;
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
        private Source _source = source;

        /// <summary>
        /// The source this entry is reading, written by <see cref="Apply"/> under the lock and read by the loop
        /// without it. Volatile rather than locked, and the distinction matters: a reference assignment is already
        /// atomic, so the value was never going to tear — what was missing is the guarantee that a loop running on
        /// another core SEES a new one rather than a cached old one, which without this the memory model does not
        /// give (S1-8.6). The staleness that remains is one read long and harmless: a read already in flight
        /// finishes against the source it started with, which is what it should do.
        /// </summary>
        public Source Source
        {
            get => Volatile.Read(ref _source);
            set => Volatile.Write(ref _source, value);
        }

        public RecipeWatch Watch { get; } = watch;

        public CancellationTokenSource Stop { get; } = new();
    }
}
