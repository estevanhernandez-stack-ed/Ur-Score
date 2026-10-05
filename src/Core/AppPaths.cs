using System.IO;

namespace Labs626.UrScore.Core;

/// <summary>
/// Where Ur Score keeps its files: one root, and every file's place under it. The composition root takes one, so a
/// test can point the whole app at a folder of its own and compose it for real — no pipe, no network, nothing under
/// the user's data folder — which is the seam S1-14.9 was waiting on. Before this, nine classes each worked out the
/// same root from <see cref="Environment.SpecialFolder.LocalApplicationData"/> on their own; their <c>DefaultPath</c>
/// members now read <see cref="Default"/>, so they agree by construction rather than by copy.
/// <para>
/// Not RoRoRo's rules file: that is RoRoRo's, lives in RoRoRo's folder, and is resolved by
/// <c>RulesFile.ResolvePath</c> from its own environment variable.
/// </para>
/// </summary>
public sealed record AppPaths(string Root)
{
    public const string FolderName = "626labs.ur-score";

    private static readonly Lazy<AppPaths> Real =
        new(() => new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName)));

    private static int _refused;

    /// <summary>
    /// A sibling of RoRoRo's own folder under Local AppData, never inside it: the user's real data folder. Throws once
    /// <see cref="RefuseDefault"/> has been called, which the test assembly does before any test runs, so nothing in a test
    /// process can resolve it: every store's default path and the app's own composition read it here.
    /// </summary>
    public static AppPaths Default
    {
        get
        {
            ThrowIfRefused();
            return Real.Value;
        }
    }

    /// <summary>
    /// Latches <see cref="Default"/> shut for the rest of the process; it can't be undone. Called by the test assembly's module
    /// initializer (port of K0ii Score's fe8d103: its harness had started the app for real inside every suite run). Never by the app.
    /// </summary>
    internal static void RefuseDefault() => Interlocked.Exchange(ref _refused, 1);

    /// <summary>Throws the refusal when the latch is set: the same gate <see cref="Default"/> uses, for other real paths (RoRoRo's rules file).</summary>
    internal static void ThrowIfRefused()
    {
        if (Volatile.Read(ref _refused) == 1)
            throw new InvalidOperationException("The real data folder is refused in a test process: compose over new AppPaths(<a folder of the test's own>).");
    }

    public string Keys => Path.Combine(Root, "keys.dat");

    public string Recipes => Path.Combine(Root, "recipes");

    public string Settings => Path.Combine(Root, "settings.json");

    public string Accounts => Path.Combine(Root, "accounts.json");

    public string Sources => Path.Combine(Root, "sources.json");

    public string Boards => Path.Combine(Root, "boards.json");

    public string Book => Path.Combine(Root, "scorebook");

    public string IconCache => Path.Combine(Root, "icon-cache");

    /// <summary>Where an older version kept raw responses; deleted at every start, never written now.</summary>
    public string LastResponse => Path.Combine(Root, "last-response");
}
