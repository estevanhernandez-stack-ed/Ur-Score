using System.Security.Cryptography;

namespace Labs626.UrScore.Board;

/// <summary>A panel's width in the 12-column grid, and whether it takes two rows (spec §9.2, R5).</summary>
public sealed record PanelSize(int Span = 6, bool Tall = false)
{
    public const int Small = 3;
    public const int Half = 6;
    public const int Wide = 12;
}

/// <summary>Where a popped-out panel's window sits, in device-independent pixels (spec §9.3).</summary>
public sealed record PopOutRect(double X, double Y, double W, double H);

/// <summary>One panel on a saved board. Its place in <see cref="BoardDef.Panels"/> is its order.</summary>
public sealed record PanelDef(string Id, PanelType Type, PanelSize Size, PanelSettings Settings, PopOutRect? PopOut = null);

/// <summary>One tab (spec §9.2). Holds no other player: see <see cref="BoardDefs.Sanitize"/>.</summary>
public sealed record BoardDef(string Id, string Name, IReadOnlyList<PanelDef> Panels);

public static class BoardDefs
{
    /// <summary>The following starter's board id (R2).</summary>
    public const string StarterBoardId = "b-starter";

    public const int MaxNameLength = 40;

    public static string NewBoardId() => "b-" + Hex();

    public static string NewPanelId() => "p-" + Hex();

    /// <summary>Trimmed and cut to <see cref="MaxNameLength"/>; null when nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) return null;
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }

    /// <summary>What a panel added from the gallery starts at.</summary>
    public static PanelSize DefaultSize(PanelType type) => type switch
    {
        PanelType.Standing or PanelType.AccountCard or PanelType.Records => new PanelSize(PanelSize.Small),
        PanelType.LiveLeaderboard => new PanelSize(PanelSize.Wide),
        _ => new PanelSize(PanelSize.Half),
    };

    /// <summary>
    /// A starter board as a saved board. The following starter (freshIds false) keeps fixed ids, so a pop-out
    /// made on it survives the first write (R2); a starter added with + Board gets new ones.
    /// </summary>
    public static BoardDef FromStarter(StarterBoard starter, bool freshIds) => new(
        freshIds ? NewBoardId() : StarterBoardId,
        starter.Name,
        [.. starter.Panels.Select((panel, index) => new PanelDef(
            freshIds ? NewPanelId() : $"p-starter-{index + 1}",
            panel.Type,
            new PanelSize(Math.Clamp(panel.Span, 1, BoardLayout.Columns)),
            panel.Settings))]);

    /// <summary>Changes exactly when the board's panels are drawn differently. Where a pop-out window sits is not drawn on the board.</summary>
    public static string Key(BoardDef board) =>
        board.Id + "#" + string.Join("|", board.Panels.Select(p =>
            $"{p.Id}:{p.Type}:{p.Size.Span}:{p.Size.Tall}:{p.Settings.Recipe}:{p.Settings.SourceId}:{string.Join(",", p.Settings.SourceIds ?? [])}:{p.Settings.ToSourceId}:{p.Settings.Stat}:{p.Settings.UserId}:{p.PopOut is not null}"));

    /// <summary>
    /// The privacy rule for <c>boards.json</c> (R17): an account id stays only when it is one of yours. The
    /// forms only offer your accounts, so this strips a hand-edited or stale id and nothing else.
    /// </summary>
    public static IReadOnlyList<BoardDef> Sanitize(IReadOnlyList<BoardDef> boards, IReadOnlySet<long> myUserIds) =>
        [.. boards.Select(board => board with
        {
            Panels = [.. board.Panels.Select(panel => panel.Settings.UserId is { } id && !myUserIds.Contains(id)
                ? panel with { Settings = panel.Settings with { UserId = null } }
                : panel)],
        })];

    private static string Hex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
