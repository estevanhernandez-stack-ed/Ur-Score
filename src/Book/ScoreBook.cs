using System.IO;
using System.Text;

namespace Labs626.UrScore.Book;

public interface IScoreBook
{
    void Append(BookLine line, string recipeText);

    /// <summary>Raised after a line reaches disk, on the writer's thread.</summary>
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
    private int _dropped;

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
        else Drain();
    }

    public void Flush() => Drain();

    public void Dispose()
    {
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
            Drain();
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
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return; // stays pending; the next append, flush or retry tries again
                }

                lock (_gate)
                {
                    if (node.List is not null) _pending.Remove(node);
                }

                Written?.Invoke(node.Value.Line);
            }
        }
    }

    private void Write(BookLine line, string recipeText)
    {
        var slug = line.Recipe.Slug;

        var recipeFile = BookFiles.RecipeFile(Root, slug, line.Recipe.Hash);
        if (!File.Exists(recipeFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recipeFile)!);
            File.WriteAllText(recipeFile, recipeText, Utf8);
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
