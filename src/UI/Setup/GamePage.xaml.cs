using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Games;
using static Labs626.UrScore.UI.TextLines;

namespace Labs626.UrScore.UI;

/// <summary>
/// Setup › a game (spec "Setup UI > GamePage"; PRD "The game page"): the game's switch, then one row per mode with its switch,
/// what it is, what it reads and sends, and for a mode that asks, its clans (<see cref="ClansSection"/>). It replaces the Recipes
/// and Clans pages, and carries Start reading when Ur Score opens, moved here unchanged from the Recipes page.
/// </summary>
public partial class GamePage : UserControl, ISetupPage
{
    /// <summary>The controls of one mode's row, built once; <see cref="Refresh"/> only updates them.</summary>
    private sealed record ModeControls(
        string Key, CheckBox Switch, TextBlock Blurb, TextBlock Reads, TextBlock Sends, TextBlock Note, TextBlock Dimmed,
        ClansSection? Clans);

    private readonly ISetupServices _services;
    private readonly string _gameId;
    private readonly List<ModeControls> _modes = [];

    /// <summary>The boxes are being set from the saved settings, not by a click, so the handlers don't write them back.</summary>
    private bool _settingBox;

    /// <param name="focusSlug">
    /// The asking reader whose clan search takes the keyboard once the page is drawn: first run, or the board's Pick your clan
    /// (A11). Null leaves focus alone.
    /// </param>
    public GamePage(ISetupServices services, string gameId, string? focusSlug = null)
    {
        InitializeComponent();
        _services = services;
        _gameId = gameId;

        if (Game is { } game)
        {
            foreach (var row in GameModel.For(game, _services.Switches, _services.Installed).Modes) _modes.Add(BuildMode(row));
        }

        Refresh();

        if (focusSlug is not null && _modes.FirstOrDefault(m => m.Clans?.Slug == focusSlug)?.Clans is { } focus)
        {
            // After layout: a control that isn't drawn yet can't take the keyboard.
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, focus.FocusMainSearch);
        }
    }

    private GameDef? Game => _services.Catalog.Games.FirstOrDefault(g => string.Equals(g.Id, _gameId, StringComparison.Ordinal));

    /// <summary>The clan section of a mode, for the render test and nothing else.</summary>
    internal ClansSection? ClansOf(string modeId) =>
        _modes.FirstOrDefault(m => m.Key.EndsWith("/" + modeId, StringComparison.Ordinal))?.Clans;

    public void Refresh()
    {
        if (Game is not { } game)
        {
            GameTitle.Text = "Not in this version";
            GameSwitch.IsEnabled = false;
            return;
        }

        var model = GameModel.For(game, _services.Switches, _services.Installed);

        _settingBox = true;
        try
        {
            GameTitle.Text = model.Name;
            AutomationProperties.SetName(GameSwitch, model.Name);
            GameSwitch.IsChecked = model.IsOn;

            foreach (var row in model.Modes)
            {
                if (_modes.FirstOrDefault(m => m.Key == row.Key) is not { } controls) continue;

                controls.Switch.IsChecked = row.IsSet;
                ShowLine(controls.Blurb, row.Blurb);
                ShowLine(controls.Reads, row.Reads ?? "");
                ShowLine(controls.Sends, row.Sends ?? "");
                ShowLine(controls.Note, row.Note ?? "");
                ShowLine(controls.Dimmed, row.DimmedLine ?? "");

                if (controls.Clans is { } clans)
                {
                    // Shown while off, dimmed and out of reach (PRD): the clans stay in view, and nothing in them can change.
                    clans.IsEnabled = row.IsOn;
                    clans.Opacity = row.IsOn ? 1 : 0.5;
                    if (row.IsOn) clans.Activate();
                    clans.Refresh();
                }
            }

            StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
        }
        finally
        {
            _settingBox = false;
        }
    }

    private ModeControls BuildMode(GameModeRow row)
    {
        var name = new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold };
        var toggle = new CheckBox { Content = name, Tag = row.Key, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(toggle, $"ModeSwitch_{row.Id}");
        AutomationProperties.SetName(toggle, row.Name);
        toggle.Checked += OnModeSwitched;
        toggle.Unchecked += OnModeSwitched;

        TextBlock Line(Thickness margin, bool mono = false)
        {
            var line = new TextBlock { Margin = margin, Visibility = Visibility.Collapsed };
            line.SetResourceReference(StyleProperty, "Muted");
            if (mono) line.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            return line;
        }

        var blurb = Line(new Thickness(24, 4, 0, 0));
        var reads = Line(new Thickness(24, 4, 0, 0), mono: true);
        var sends = Line(new Thickness(24, 2, 0, 0));
        var note = Line(new Thickness(24, 4, 0, 0));

        var card = new Border { Child = new StackPanel { Children = { toggle, blurb, reads, sends, note } } };
        card.SetResourceReference(StyleProperty, "Card");

        var rowPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        rowPanel.Children.Add(card);

        var dimmed = Line(new Thickness(24, 12, 0, 0));
        rowPanel.Children.Add(dimmed);

        ClansSection? clans = null;
        if (row.AskingSlug is { } slug)
        {
            clans = new ClansSection(_services, slug) { Margin = new Thickness(24, 14, 0, 0) };
            rowPanel.Children.Add(clans);
        }

        ModesPanel.Children.Add(rowPanel);
        return new ModeControls(row.Key, toggle, blurb, reads, sends, note, dimmed, clans);
    }

    private void OnGameSwitched(object sender, RoutedEventArgs e)
    {
        if (_settingBox) return;
        Switch(_gameId, GameSwitch.IsChecked == true);
    }

    private void OnModeSwitched(object sender, RoutedEventArgs e)
    {
        if (_settingBox || sender is not CheckBox { Tag: string key } toggle) return;
        Switch(key, toggle.IsChecked == true);
    }

    /// <summary>
    /// A game or mode switch, saved and applied at once; the services' change redraws this page. One that can't be saved says why
    /// on the page and the boxes go back to what is saved, so they never show something that isn't (and no message box, ever).
    /// </summary>
    private void Switch(string key, bool on)
    {
        try
        {
            _services.SetSwitch(key, on);
            ShowLine(SwitchProblemLine, "");
        }
        catch (Exception ex)
        {
            ShowLine(SwitchProblemLine, _services.Redactor.Redact(GameModel.SwitchProblem(ex)));
            Refresh();
        }
    }

    /// <summary>
    /// The one app-wide setting with a control (plan A32). A write that fails is said here and the box goes back to
    /// what is saved, so it never shows something the file doesn't — and no message box, ever (Global Constraints).
    /// </summary>
    private void OnStartOnOpenChanged(object sender, RoutedEventArgs e)
    {
        if (_settingBox) return;

        try
        {
            _services.SaveSettings(_services.Settings with { StartOnOpen = StartOnOpenBox.IsChecked == true });
            ShowLine(StartOnOpenProblemLine, "");
        }
        catch (Exception ex)
        {
            ShowLine(StartOnOpenProblemLine, _services.Redactor.Redact($"Could not save that: {ex.Message}"));
            _settingBox = true;
            StartOnOpenBox.IsChecked = _services.Settings.StartOnOpen;
            _settingBox = false;
        }
    }
}
