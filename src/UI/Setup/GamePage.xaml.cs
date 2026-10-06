using System.Diagnostics;
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
        ClansSection? Clans, TextBlock? LinkLine = null, Button? LinkButton = null);

    private readonly ISetupServices _services;
    private readonly string _gameId;
    private readonly List<ModeControls> _modes = [];

    /// <summary>
    /// Opens a mode's link in the browser. A seam so a test can click the button without launching one; the address is always
    /// the manifest's (<see cref="ModeLink"/>), https only, checked when the manifest was read.
    /// </summary>
    internal Action<Uri> OpenLink { get; set; } = url => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

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
            foreach (var row in Model(game).Modes) _modes.Add(BuildMode(row, game.Modes.First(m => m.Key == row.Key).Link));
        }

        Refresh();

        if (focusSlug is not null && _modes.FirstOrDefault(m => m.Clans?.Slug == focusSlug)?.Clans is { } focus)
        {
            // After layout: a control that isn't drawn yet can't take the keyboard.
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, focus.FocusMainSearch);
        }
    }

    private GameRow Model(GameDef game) =>
        GameModel.For(game, _services.Switches, _services.Installed, _services.KnownAccounts, _services.Sources, _services.Latest);

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

        var model = Model(game);

        _settingBox = true;
        try
        {
            GameTitle.Text = model.Name;
            AutomationProperties.SetName(GameSwitch, model.Name);
            GameSwitch.IsChecked = model.IsOn;
            ShowLine(RoRoRoAccountsLine, model.AccountsLine);

            foreach (var row in model.Modes)
            {
                if (_modes.FirstOrDefault(m => m.Key == row.Key) is not { } controls) continue;

                controls.Switch.IsChecked = row.IsSet;
                ShowLine(controls.Blurb, row.Blurb);
                ShowLine(controls.Reads, row.Reads ?? "");
                ShowLine(controls.Sends, row.Sends ?? "");
                ShowLine(controls.Note, row.Note ?? "");
                ShowLine(controls.Dimmed, row.DimmedLine ?? "");
                if (controls.LinkLine is { } linkLine) ShowLine(linkLine, row.LinkStatus is { } status ? string.Join('\n', status.Lines) : "");
                if (controls.LinkButton is { } linkButton)
                {
                    linkButton.Visibility = row.LinkStatus?.ShowButton == true ? Visibility.Visible : Visibility.Collapsed;
                }

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
            AutostartInTrayBox.IsChecked = _services.Settings.AutostartInTray;
        }
        finally
        {
            _settingBox = false;
        }
    }

    /// <param name="link">The mode's manifest link; a mode with one gets the link line and the button, shown as the model says.</param>
    private ModeControls BuildMode(GameModeRow row, ModeLink? link)
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

        // The clans belong to the mode, so they sit inside its card, under what the mode is, rather than floating below it.
        var inside = new StackPanel { Children = { toggle, blurb, reads, sends, note } };

        // Which accounts this mode can read and where to link the rest (only Profile carries a link today, so the ids are its).
        TextBlock? linkLine = null;
        Button? linkButton = null;
        if (link is not null)
        {
            linkLine = Line(new Thickness(24, 8, 0, 0));
            linkLine.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(linkLine, "ProfileLinkLine");

            linkButton = new Button
            {
                Content = link.Text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(24, 8, 0, 0),
                Padding = new Thickness(10, 3, 10, 3), Visibility = Visibility.Collapsed, ToolTip = link.Url.AbsoluteUri,
            };
            AutomationProperties.SetAutomationId(linkButton, "LinkAccountsButton");
            AutomationProperties.SetName(linkButton, link.Text);
            AutomationProperties.SetHelpText(linkButton, $"Opens {link.Url.AbsoluteUri} in your browser.");
            linkButton.Click += (_, _) => Open(link.Url);

            inside.Children.Add(linkLine);
            inside.Children.Add(linkButton);
        }

        var card = new Border { Child = inside };
        card.SetResourceReference(StyleProperty, "Card");

        var rowPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        rowPanel.Children.Add(card);

        var dimmed = Line(new Thickness(24, 12, 0, 0));
        inside.Children.Add(dimmed);

        ClansSection? clans = null;
        if (row.AskingSlug is { } slug)
        {
            clans = new ClansSection(_services, slug) { Margin = new Thickness(24, 14, 0, 0) };
            inside.Children.Add(clans);
        }

        ModesPanel.Children.Add(rowPanel);
        return new ModeControls(row.Key, toggle, blurb, reads, sends, note, dimmed, clans, linkLine, linkButton);
    }

    /// <summary>A browser that can't be started says so on the page, never in a box and never as an exception out of a click.</summary>
    private void Open(Uri url)
    {
        try
        {
            OpenLink(url);
            ShowLine(SwitchProblemLine, "");
        }
        catch (Exception ex)
        {
            ShowLine(SwitchProblemLine, _services.Redactor.Redact($"Could not open your browser: {ex.Message}"));
        }
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

    /// <summary>
    /// Tray mode's tick (<see cref="Core.LaunchMode"/>): read at the next autostart, so it saves and changes nothing running. A
    /// write that fails is said here and the box goes back to what is saved, as Start reading's does.
    /// </summary>
    private void OnAutostartInTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_settingBox) return;

        try
        {
            _services.SaveSettings(_services.Settings with { AutostartInTray = AutostartInTrayBox.IsChecked == true });
            ShowLine(AutostartInTrayProblemLine, "");
        }
        catch (Exception ex)
        {
            ShowLine(AutostartInTrayProblemLine, _services.Redactor.Redact($"Could not save that: {ex.Message}"));
            _settingBox = true;
            AutostartInTrayBox.IsChecked = _services.Settings.AutostartInTray;
            _settingBox = false;
        }
    }
}
