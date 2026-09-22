using System.IO;
using System.Text;

namespace Labs626.UrScore.Book;

public interface IScoreBook
{
    void Append(BookLine line, string recipeText);

    /// <summary>
    /// Raised after a line reaches disk. On the writer's thread when the book runs one (the app), and on the
    /// caller's own thread when it does not (<c>background: false</c>, which the tests use so a write is done
    /// when <c>Append</c> returns). Either way the line is on disk before this fires (S1-5.2).
    /// </summary>
    event Action<BookLine>? Written;

    int Pending { get; }

    int Dropped { get; }

    void Flush();

    string Root { get; }
}

/// <summary>
/// The only code that writes the score book (score book spec §5.7). One queue with one consumer: two watches
/// never interleave bytes, a file another program holds keeps its lines in memory until it's free, and
/// past <see cref="MaxPending"/> the oldest reading lines are dropped, never a final.
/// </summary>
public sealed class ScoreBook : IScoreBook, IDisposable
{
    public const int MaxPending = 5000;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly object _drainGate = new();
    private readonly LinkedList<(BookLine Line, string RecipeText)> _pending = new();
    private readonly bool _background;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _hasFailed;
    private long _lastFailureTicks;
    private int _dropped;
    private int _disposed;

    public ScoreBook(string root, bool background = true)
    {
        Root = root;
        _background = background;
        if (background)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Ur Score book writer" };
            _thread.Start();
        }
    }

    public string Root { get; }

    public event Action<BookLine>? Written;

    public int Pending
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    public int Dropped => Volatile.Read(ref _dropped);

    public void Append(BookLine line, string recipeText)
    {
        lock (_gate)
        {
            _pending.AddLast((line, recipeText));
            while (_pending.Count > MaxPending)
            {
                var node = _pending.First;
                while (node is not null && node.Value.Line.Kind != BookLine.KindRead) node = node.Next;
                if (node is null) break;

                _pending.Remove(node);
                Interlocked.Increment(ref _dropped);
            }
        }

        if (_background) _signal.Set();
        // Synchronous mode drains on every append, except right after a failed write: a locked file or a
        // denied handle doesn't clear up between one append and the next, so retrying on every single call
        // (each a real, and expensive, failed I/O attempt) only burns time until RetryDelay has passed.
        // Flush() below is the escape hatch that ignores this backoff.
        else if (!_hasFailed || Environment.TickCount64 - _lastFailureTicks >= RetryDelay.TotalMilliseconds) Drain();
    }

    public void Flush() => Drain();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return; // a second Dispose is a no-op

        _stopping = true;
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        Drain();
        _signal.Dispose();
    }

    private void Run()
    {
        while (!_stopping)
        {
            _signal.WaitOne(RetryDelay);
            Turn(Drain);
        }
    }

    /// <summary>
    /// One turn of the writer loop: drain, and survive whatever comes back. The writer thread must never die
    /// from this loop — whatever happened, the next signal or timeout tries again — and losing it would stop
    /// every reading reaching disk with nothing raising a hand.
    /// <para>
    /// A method of its own, and visible to the tests, because the backstop CANNOT be reached through
    /// <see cref="Append"/>: <see cref="Drain"/> already contains every failure it knows how to handle — a
    /// locked file, a denied handle, a line that can never serialize, a subscriber that throws — so there is no
    /// input that makes it throw, which is exactly why this went untested for so long (S1-5.4). What is worth
    /// pinning is not the trigger, which is by definition the one nobody thought of, but the promise: the loop
    /// gets another turn.
    /// </para>
    /// </summary>
    internal static void Turn(Action drain)
    {
        try
        {
            drain();
        }
        catch (Exception)
        {
            // The last-resort backstop for a failure Drain() does not know about. Deliberately silent and
            // deliberately total: there is nowhere to report it from a background thread that is still running,
            // and rethrowing would take the writer down, which is the outcome this exists to prevent.
        }
    }

    private void Drain()
    {
        lock (_drainGate)
        {
            while (true)
            {
                LinkedListNode<(BookLine Line, string RecipeText)>? node;
                lock (_gate) node = _pending.First;
                if (node is null) return;

                try
                {
                    Write(node.Value.Line, node.Value.RecipeText);
                    _hasFailed = false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _lastFailureTicks = Environment.TickCount64;
                    _hasFailed = true;
                    return; // stays pending; the next append, flush or retry tries again
                }
                catch (Exception)
                {
                    // Not a locked file or a denied handle: this line can never be written (for example a
                    // non-finite headline value JSON can't serialize). It can't sit in the queue forever
                    // either, so it's dropped like an overflowed reading, and the rest of the queue still
                    // gets a turn.
                    // Counted only if THIS branch removed it. An append's overflow can take the same node out
                    // while the write is failing, and then the line was already counted there; counting it here
                    // too made one dropped line two on the Score book page (S1-5.1).
                    bool removedHere;
                    lock (_gate)
                    {
                        removedHere = node.List is not null;
                        if (removedHere) _pending.Remove(node);
                    }

                    if (removedHere) Interlocked.Increment(ref _dropped);
                    continue;
                }

                lock (_gate)
                {
                    if (node.List is not null) _pending.Remove(node);
                }

                try
                {
                    Written?.Invoke(node.Value.Line);
                }
                catch (Exception)
                {
                    // A throwing subscriber must not stop the writer: the line already reached disk.
                }
            }
        }
    }

    private void Write(BookLine line, string recipeText)
    {
        var slug = line.Recipe.Slug;

        var recipeFile = BookFiles.RecipeFile(Root, slug, line.Recipe.Hash);
        if (!File.Exists(recipeFile))
        {
            // Through a temp file and one move, like sources.json and boards.json: a crash mid-write used to
            // leave a torn recipe file, and because the name carries the hash it was then never rewritten, so a
            // half-file sat beside a whole book forever (S1-F.4). The move is atomic on the same volume.
            Directory.CreateDirectory(Path.GetDirectoryName(recipeFile)!);
            var temp = recipeFile + ".tmp";
            File.WriteAllText(temp, recipeText, Utf8);
            File.Move(temp, recipeFile, overwrite: true);
        }

        var file = BookFiles.MonthFile(Root, slug, line.T);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        var bytes = new List<byte>(512);
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n') bytes.Add((byte)'\n');
        }

        bytes.AddRange(Utf8.GetBytes(BookJson.Serialize(line)));
        bytes.Add((byte)'\n');

        stream.Seek(0, SeekOrigin.End);
        stream.Write(bytes.ToArray());
        stream.Flush(flushToDisk: true);
    }
}
