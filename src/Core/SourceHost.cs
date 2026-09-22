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

    /// <summary>Every read currently running, so exit can wait for them (<see cref="Dispose"/>). Keyed by an id that is unique per read, never per source: two reads of one source can overlap across a Stop/Start.</summary>
    private readonly Dictionary<long, Task> _inFlight = [];
    private long _nextRead;

    /// <summary>
    /// Two sources per run, because exit and Stop want different things. <see cref="_run"/> stops the loops:
    /// no new read starts and a loop waiting out its interval wakes and leaves. <see cref="_reads"/> cancels
    /// the reads themselves. Stop cancels both at once, as it always did. Exit cancels the loops, gives reads
    /// already in flight <see cref="ExitGrace"/> to land, and only then cancels them (S1-8.2). With one source
    /// there was no way to say "no more reads, but finish the one you are on".
    /// </summary>
    private CancellationTokenSource? _run;
    private CancellationTokenSource _reads = new();

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
            if (_reads.IsCancellationRequested) _reads = new CancellationTokenSource();
            foreach (var entry in _entries.Values) StartLoop(entry, _run.Token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? stopping;
        CancellationTokenSource reads;
        lock (_gate)
        {
            stopping = _run;
            _run = null;
            reads = _reads;
        }

        if (stopping is null) return;

        // Stop means stop: the read in flight is cancelled too, as it always was. Exit is the path that waits.
        reads.Cancel();

        // Cancelled OUTSIDE the lock. Cancel runs every linked source's callbacks on this thread, and doing that
        // while holding the gate means a callback that ever wanted the gate would deadlock against itself. None
        // does today; this is the cheap way to keep that from being a question anybody has to re-answer.
        stopping.Cancel();

        // Each Start made one of these and each Stop dropped it on the floor, so a session that started and
        // stopped reading repeatedly accumulated them along with one linked source per entry per start (S1-8.1).
        // Safe to dispose now: the loops link to it, and a linked source outlives the parent it was built from.
        stopping.Dispose();
    }

    /// <summary>
    /// Every source read once, now. Each read runs under its own entry's token as well as the caller's, so a
    /// source removed or switched off while its read is in flight has that read cancelled — exactly as the
    /// timer loop's reads always were. Test now used to run under the caller's token alone, so a removal
    /// hid the snapshot and nothing else: the read went on to record and send for a source that was gone
    /// (S1-8.3).
    /// </summary>
    public Task RunAllNowAsync(string trigger, CancellationToken cancellationToken)
    {
        List<Entry> entries;
        lock (_gate) entries = [.. _entries.Values];

        CancellationToken reads;
        lock (_gate) reads = _reads.Token;

        return Task.WhenAll(entries.Select(async entry =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reads, entry.Stop.Token);
            await RunOneAsync(entry, trigger, linked.Token).ConfigureAwait(false);
        }));
    }

    /// <summary>
    /// How long exit waits for reads already in flight before cancelling them. Long enough for a read that has
    /// fetched to record and send; short enough that closing the window never feels stuck. A read that is still
    /// waiting on the network after this is cancelled and its lines are lost, which is the bargain exit makes
    /// — but only after it has offered the read a chance to land (S1-8.2).
    /// </summary>
    public static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Stops the timers first, so no NEW read starts, then gives reads already in flight <see cref="ExitGrace"/>
    /// to finish before cancelling whatever is left. Exit used to cancel everything at once, so a read that had
    /// just fetched lost its lines every time the window happened to close during one — at most one read's
    /// worth, and a read a person could have kept for the cost of a moment's patience (S1-8.2).
    /// </summary>
    public void Dispose()
    {
        // The loops, not the reads: no new read starts, and the ones in flight get their grace below.
        CancellationTokenSource? loops;
        lock (_gate)
        {
            loops = _run;
            _run = null;
        }
        loops?.Cancel();
        loops?.Dispose();

        Task[] inFlight;
        lock (_gate) inFlight = [.. _inFlight.Values];
        if (inFlight.Length > 0)
        {
            try
            {
                Task.WhenAll(inFlight).Wait(ExitGrace);
            }
            catch (AggregateException)
            {
                // A read that faulted on its way out has already had its say in its own catch; exit is not the
                // place to hear it again.
            }
        }

        CancellationTokenSource reads;
        lock (_gate)
        {
            reads = _reads;
            foreach (var entry in _entries.Values) entry.Stop.Cancel();
        }

        reads.Cancel();
    }

    private void StartLoop(Entry entry, CancellationToken runToken)
    {
        var loop = CancellationTokenSource.CreateLinkedTokenSource(runToken, entry.Stop.Token);
        var read = CancellationTokenSource.CreateLinkedTokenSource(_reads.Token, entry.Stop.Token);

        // The loop owns both linked sources and releases them on the way out, whichever way it leaves — cancelled,
        // faulted, or simply finished. Before this, a Start/Stop cycle left one per entry behind, and each one
        // holds a registration on both of its parents (S1-8.1).
        _ = Task.Run(async () =>
        {
            try
            {
                await LoopAsync(entry, loop.Token, read.Token).ConfigureAwait(false);
            }
            finally
            {
                loop.Dispose();
                read.Dispose();
            }
        });
    }

    /// <param name="loop">Ends the loop: no further read, and the wait between reads is cut short.</param>
    /// <param name="read">Cancels the read itself. Exit cancels this one only after its grace.</param>
    private async Task LoopAsync(Entry entry, CancellationToken loop, CancellationToken read)
    {
        var trigger = BookLine.TriggerStart;
        while (!loop.IsCancellationRequested)
        {
            await RunOneAsync(entry, trigger, read).ConfigureAwait(false);
            trigger = BookLine.TriggerTimer;

            var seconds = DelaySeconds(intervalSeconds, entry.Source);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), loop).ConfigureAwait(false);
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
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long readId;
        lock (_gate)
        {
            readId = ++_nextRead;
            _inFlight[readId] = tcs.Task;
        }

        try
        {
            await ReadAndPublishAsync(entry, trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _inFlight.Remove(readId);
            tcs.TrySetResult();
        }
    }

    private async Task ReadAndPublishAsync(Entry entry, string trigger, CancellationToken cancellationToken)
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
