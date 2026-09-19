using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// A line that says how long a period has left, to the second, and keeps saying it while nothing else happens.
/// <para>
/// The board redraws on a read, every few minutes; a countdown drawn then would be minutes wrong by the time anyone
/// looked at it. So this keeps the period's end (<see cref="Ends"/>) rather than a worked-out number, and rewrites
/// itself once a second from the system clock. It is a <see cref="TextBlock"/>, so it reads to a screen reader and to
/// the smoke walks exactly as the line it replaced did, automation id and all.
/// </para>
/// <para>
/// The timer runs only while the control is loaded: a pop-out that is closed, or a board tab you are not on, costs
/// nothing. <see cref="Lead"/> is the part a read decides (the battle's name, the next read) and never ticks.
/// </para>
/// </summary>
public sealed class CountdownText : TextBlock
{
    public static readonly DependencyProperty LeadProperty = DependencyProperty.Register(
        nameof(Lead), typeof(string), typeof(CountdownText), new PropertyMetadata("", OnPartChanged));

    public static readonly DependencyProperty EndsProperty = DependencyProperty.Register(
        nameof(Ends), typeof(DateTimeOffset?), typeof(CountdownText), new PropertyMetadata(null, OnPartChanged));

    /// <summary>Whether to give the end as a time as well as a countdown: the panel has the room, the top bar doesn't.</summary>
    public static readonly DependencyProperty WithClockProperty = DependencyProperty.Register(
        nameof(WithClock), typeof(bool), typeof(CountdownText), new PropertyMetadata(true, OnPartChanged));

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };

    public CountdownText()
    {
        _timer.Tick += (_, _) => Draw();
        Loaded += (_, _) => { Draw(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>The part of the line a read decides, shown before the countdown: "SpaceMineBattle2026 · next read in 2m".</summary>
    public string Lead
    {
        get => (string)GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

    /// <summary>When the period ends, or null with no period or no end, which leaves <see cref="Lead"/> alone on the line.</summary>
    public DateTimeOffset? Ends
    {
        get => (DateTimeOffset?)GetValue(EndsProperty);
        set => SetValue(EndsProperty, value);
    }

    public bool WithClock
    {
        get => (bool)GetValue(WithClockProperty);
        set => SetValue(WithClockProperty, value);
    }

    /// <summary>The line as it reads at <paramref name="now"/>, which is what the control draws and what a test can check.</summary>
    public static string Line(string lead, DateTimeOffset? ends, DateTimeOffset now, TimeZoneInfo zone, bool withClock = true)
    {
        if (ends is not { } at) return lead;

        var countdown = PanelText.Countdown(at, now, zone, withClock);
        return lead.Length == 0 ? countdown : $"{lead} · {countdown}";
    }

    private static void OnPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CountdownText)d).Draw();

    private void Draw() => Text = Line(Lead ?? "", Ends, DateTimeOffset.Now, TimeZoneInfo.Local, WithClock);
}
