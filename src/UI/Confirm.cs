namespace Labs626.UrScore.UI;

/// <summary>
/// One question Ur Score asks before it does something a second click can't take back. Pure, so what a
/// confirmation says is decided and tested away from the window that draws it — the same split Setup › Alerts
/// makes between <see cref="AlertCards"/> and its page.
/// </summary>
/// <param name="Title">The window's own title, naming the job: "Remove recipe", "Add another clan".</param>
/// <param name="Question">What happens if you say yes, in your words, ending in a question mark.</param>
/// <param name="DoText">The button that acts, a verb: "Remove", "Add anyway".</param>
/// <param name="DoName">That button's accessible name, naming what it acts on: "Remove Pet Sim 99 profile".</param>
public sealed record Confirm(string Title, string Question, string DoText, string DoName)
{
    /// <summary>
    /// The answer that does nothing, and the one keyboard lands on: Enter, Escape and the close button all give it,
    /// so a question answered without reading it changes nothing.
    /// </summary>
    public const string CancelText = "Cancel";

    /// <summary>The safe answer's words: "Cancel", unless a question needs them plainer ("Keep running").</summary>
    public string CancelButton { get; init; } = CancelText;
}
