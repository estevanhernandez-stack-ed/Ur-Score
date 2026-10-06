namespace Labs626.UrScore.Core;

/// <summary>Why RoRoRo started Ur Score, from <see cref="LaunchMode.ReasonVariable"/>. An older RoRoRo sets nothing: Unknown.</summary>
public enum LaunchReason { Unknown, Autostart, Manual, Install, Update }

/// <summary>How Ur Score runs: its board on screen, or keeping score from the tray with the board hidden.</summary>
public enum AppMode { Window, Tray }

/// <summary>
/// Window or tray at start (owner-approved, 2026-10-05). RoRoRo 1.33 says why it started a plugin in an environment variable;
/// an autostart is RoRoRo opening, not the player asking for the board, so Ur Score keeps score from the tray then, unless
/// <see cref="Settings.AutostartInTray"/> is off. A player's own start, an install and anything unknown open the window, as
/// every start did before. An update starts it the way it last ran (<see cref="Settings.LastMode"/>), so a quiet tray copy
/// stays quiet through an update. A first run always opens the window: Setup asking for a clan has to be seen.
/// <para>No WPF, no process state: the app reads the variable through <see cref="TakeReasonFromProcess"/> and decides here.</para>
/// </summary>
public static class LaunchMode
{
    /// <summary>RoRoRo's name for it (v1.33): autostart, manual, install or update.</summary>
    public const string ReasonVariable = "RORORO_LAUNCH_REASON";

    public const string TrayWord = "tray";
    public const string WindowWord = "window";

    public static LaunchReason Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "autostart" => LaunchReason.Autostart,
        "manual" => LaunchReason.Manual,
        "install" => LaunchReason.Install,
        "update" => LaunchReason.Update,
        _ => LaunchReason.Unknown,
    };

    /// <summary>
    /// Reads the reason once and clears it, so nothing Ur Score starts (the browser for a link, say) inherits
    /// RoRoRo's reason as if RoRoRo had started it. The read and the write are seams: a test never touches the real process
    /// environment, which the WPF harness shares with every other test.
    /// </summary>
    public static LaunchReason TakeReason(Func<string, string?> read, Action<string, string?> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        var reason = Parse(read(ReasonVariable));
        write(ReasonVariable, null);
        return reason;
    }

    /// <summary>The app's own read, at start: this process's environment, cleared after.</summary>
    public static LaunchReason TakeReasonFromProcess() =>
        TakeReason(Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);

    /// <param name="firstRunDue">Setup would open on its game page to ask for a clan (<c>SetupPages.FirstRun</c>).</param>
    public static AppMode Decide(LaunchReason reason, Settings settings, bool firstRunDue)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (firstRunDue) return AppMode.Window;

        return reason switch
        {
            LaunchReason.Autostart => settings.AutostartInTray ? AppMode.Tray : AppMode.Window,
            LaunchReason.Update => ParseMode(settings.LastMode),
            _ => AppMode.Window,
        };
    }

    /// <summary>"tray", in any case, is the tray; anything else, or nothing, is the window.</summary>
    public static AppMode ParseMode(string? word) =>
        string.Equals(word?.Trim(), TrayWord, StringComparison.OrdinalIgnoreCase) ? AppMode.Tray : AppMode.Window;

    public static string Word(AppMode mode) => mode == AppMode.Tray ? TrayWord : WindowWord;

    /// <summary>
    /// The mode right now, as an update should bring it back: the tray only while a tray start's board is hidden. A window
    /// start has no tray to be in, and a tray start whose board was opened is, for now, a window.
    /// </summary>
    public static AppMode Current(bool startedInTray, bool boardShown) =>
        startedInTray && !boardShown ? AppMode.Tray : AppMode.Window;

    /// <summary>
    /// Closing the board hides it to the tray only when Ur Score started there, and never when Windows is ending the session
    /// or the tray's Quit is closing it: those end the app. A window start closes as it always has.
    /// </summary>
    public static bool HidesOnClose(bool startedInTray, bool endingSession, bool quitting) =>
        startedInTray && !endingSession && !quitting;

    /// <summary>
    /// A second start while Ur Score runs brings its board forward, tray or not, except an autostart: RoRoRo opening again
    /// must never pop up a board the player left in the tray.
    /// </summary>
    public static bool SecondStartShows(LaunchReason reason) => reason != LaunchReason.Autostart;
}
