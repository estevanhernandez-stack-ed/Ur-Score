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

    /// <summary>A sibling of RoRoRo's own folder under Local AppData, never inside it.</summary>
    public static AppPaths Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName));

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
