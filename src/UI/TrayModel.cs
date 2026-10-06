namespace Labs626.UrScore.UI;

/// <summary>What a tray menu line does when it is chosen.</summary>
public enum TrayAction { None, Open, PauseOrResume, Quit }

/// <summary>One tray menu line; an empty header with no action is a separator.</summary>
public sealed record TrayItem(string Header, TrayAction Action = TrayAction.None, bool IsEnabled = true)
{
    public static TrayItem Separator { get; } = new("");

    public bool IsSeparator => Header.Length == 0 && Action == TrayAction.None;
}

/// <summary>What the tray is drawn from, read each time its menu opens or its tooltip is refreshed.</summary>
/// <param name="Chip">The board's status chip state (<see cref="StatusChip.StateOf"/>).</param>
/// <param name="CanPauseOrResume">The status card's Pause/Resume takes a press now.</param>
public sealed record TrayState(ChipState Chip, bool Running, bool CanPauseOrResume);

/// <summary>Where a chosen line goes: the board, the status card's Pause/Resume, and quitting.</summary>
public sealed record TrayTargets(Action Open, Action PauseOrResume, Action Quit);

/// <summary>
/// Everything the tray decides (tray mode, RoRoRo 1.33), without WPF: its tooltip, its three lines and what each does, and the
/// one balloon on the first hide. <see cref="Tray"/> only draws them and hands a click back to <see cref="Choose"/>. Ported from
/// K0ii Score's and cut to what Ur Score needs: no alerts to pause, no accounts to snooze, no first run to run again.
/// </summary>
public sealed class TrayModel
{
    public const string AppName = "Ur Score";
    public const string Open = "Open board";
    public const string Pause = "Pause reading";
    public const string Resume = "Resume reading";
    public const string Quit = "Quit";

    /// <summary>The first close to the tray in a session: where it went, and the way back.</summary>
    public const string StillKeepingScore =
        "Ur Score is still keeping score. Click its tray icon to see the board, or right-click it to quit.";

    private bool _toldOnce;

    /// <summary>
    /// From the status chip's state, so the tray and the board's chip tell one story. Starting and Trouble are still reading,
    /// so still keeping score; Start reading has read nothing yet, which the tray calls paused.
    /// </summary>
    public static string Tooltip(ChipState chip) => chip switch
    {
        ChipState.Live or ChipState.Trouble or ChipState.Starting => $"{AppName} · keeping score",
        _ => $"{AppName} · paused",
    };

    /// <param name="running">Reading is on, so the middle line pauses; otherwise it resumes.</param>
    /// <param name="canPauseOrResume">The status card's Pause/Resume takes a press now (<see cref="BoardButtons.For"/>'s StartStop).</param>
    public static IReadOnlyList<TrayItem> Menu(bool running, bool canPauseOrResume) =>
    [
        new TrayItem(Open, TrayAction.Open),
        new TrayItem(running ? Pause : Resume, TrayAction.PauseOrResume, canPauseOrResume),
        TrayItem.Separator,
        new TrayItem(Quit, TrayAction.Quit),
    ];

    /// <summary>Does what <paramref name="item"/> says; a separator does nothing.</summary>
    public static void Choose(TrayItem item, TrayTargets targets)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(targets);
        switch (item.Action)
        {
            case TrayAction.Open:
                targets.Open();
                break;
            case TrayAction.PauseOrResume:
                targets.PauseOrResume();
                break;
            case TrayAction.Quit:
                targets.Quit();
                break;
        }
    }

    /// <summary>The balloon for a hide: <see cref="StillKeepingScore"/> the first time this session, then nothing.</summary>
    public string? NoticeOnHide()
    {
        if (_toldOnce) return null;
        _toldOnce = true;
        return StillKeepingScore;
    }
}
