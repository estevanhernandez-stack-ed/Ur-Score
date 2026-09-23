namespace Labs626.UrScore.UI;

/// <summary>What the top bar's chip says (BC2). Starting is busy; Start reading is the one state a click acts on.</summary>
public enum ChipState { Starting, StartReading, Live, Paused, Trouble }

/// <summary>
/// The status chip that replaced Start/Stop and Test now as top-bar buttons (spec §3.2, BC2). A click on it never
/// pauses: it opens the status card, which holds Pause. Worked out from the same inputs as the state line, so the chip,
/// the card and the line tell one story.
/// </summary>
public static class StatusChip
{
    public const string AppTitle = "RoRoRo Ur Score";

    /// <summary>Paused wins over trouble: nothing is being read to be in trouble.</summary>
    public static ChipState StateOf(bool running, bool everStarted, bool starting, bool trouble) =>
        starting ? ChipState.Starting
        : running ? (trouble ? ChipState.Trouble : ChipState.Live)
        : everStarted ? ChipState.Paused
        : ChipState.StartReading;

    /// <summary>
    /// Looking at the status is always allowed once the book is read (Review Focus 3). Only Start reading, which
    /// starts, answers to the Start gate (<see cref="BoardButtons.For"/>), and Starting is busy.
    /// </summary>
    public static bool Enabled(ChipState state, bool loaded, bool startGate) => state switch
    {
        ChipState.Starting => false,
        ChipState.StartReading => startGate,
        _ => loaded,
    };

    public static string Glyph(ChipState state) => state switch
    {
        ChipState.Live => "●",
        ChipState.Paused => "❚❚",
        ChipState.Trouble => "▲",
        ChipState.StartReading => "▶",
        _ => "",
    };

    public static string Word(ChipState state) => state switch
    {
        ChipState.Live => "Live",
        ChipState.Paused => "Paused",
        ChipState.Trouble => "Trouble",
        ChipState.StartReading => "Start reading",
        _ => "Starting…",
    };

    /// <summary>The chip's automation name and tooltip: the state in words, and what a press does.</summary>
    public static string Name(ChipState state) => state switch
    {
        ChipState.Live => "Reading is on. Press for status.",
        ChipState.Paused => "Reading is paused. Press for status.",
        ChipState.Trouble => "Reading has a problem. Press for status.",
        ChipState.StartReading => "Start reading",
        _ => "Starting",
    };

    /// <summary>
    /// The glyph's colour. Paused is amber and the loudest state after first run, because paused silences the phone
    /// alerts; trouble is magenta, the colour this app already uses for a read that should have happened.
    /// </summary>
    public static string? BrushKey(ChipState state) => state switch
    {
        ChipState.Live => "CyanBrush",
        ChipState.Paused => "AmberBrush",
        ChipState.Trouble => "MagentaBrush",
        _ => null,
    };

    /// <summary>A paused board shows it on the taskbar and a second screen, where nobody is hovering over a chip.</summary>
    public static string Title(ChipState state) => state == ChipState.Paused ? $"{AppTitle} (Paused)" : AppTitle;

    /// <summary>
    /// BC3: the state and detail lines show only when there is something to say. A41's "the numbers on screen are …"
    /// sentence is a ruling, so a board drawing remembered numbers always shows it.
    /// </summary>
    public static bool ShowsLines(bool loaded, ChipState state, bool failed, string detail, bool remembered) =>
        !loaded || failed || state != ChipState.Live || detail.Length > 0 || remembered;

    /// <summary>The tabs' share of what the rest of the top bar leaves; the period line, never the chip or ⟳, gives way first.</summary>
    public static double TabBudget(double bar, double fixedWidth, double share) => Math.Max(0, (bar - fixedWidth) * share);

    /// <summary>
    /// BC2 pinned at the logic level: a click on the chip starts reading only from Start reading; every other state
    /// opens the card and never pauses. <see cref="BoardWindow"/>'s click handler reads this instead of comparing
    /// states inline, so the one exception can't quietly grow a second one.
    /// </summary>
    public static bool ClickStarts(ChipState state) => state == ChipState.StartReading;
}
