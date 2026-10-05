using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using static Labs626.UrScore.UI.TextLines;

namespace Labs626.UrScore.UI;

/// <summary>
/// The clans of one mode, inside its row on the game page (spec "ClansSection"; it was Setup › Clans, spec §7.1): the main
/// clan search, the clans your accounts are in, the clans you watch, and the Top switch. Every change is saved and applied at
/// once. The game page owns it and redraws it; while the mode is off the page disables it, and the clans list is not fetched
/// until it is first enabled (<see cref="Activate"/>), so an off mode contacts nothing.
/// </summary>
public partial class ClansSection : UserControl
{
    private readonly ISetupServices _services;
    private readonly string _slug;
    private readonly CancellationTokenSource _closing = new();
    private string? _mainProbeId;
    private string? _mineProbeId;
    private bool _rendering;
    private bool _namesAsked;

    /// <param name="recipeSlug">The mode's asking reader (<see cref="GameModel.AskingSlug"/>).</param>
    public ClansSection(ISetupServices services, string recipeSlug)
    {
        InitializeComponent();
        _services = services;
        _slug = recipeSlug;

        // Not awaited, and not lost: a pick or the name list failing past its own catches used to vanish, since a
        // discarded task's fault reaches no handler (S1-11.1). Its type goes to the trail now.
        MainClanSearch.Picked += name => Unawaited.TrailFailures(PickAsync(name, SourceRole.Main), _services.AddTrail, "CLAN PICK");
        MineClanSearch.Picked += name => Unawaited.TrailFailures(PickAsync(name, SourceRole.Mine), _services.AddTrail, "CLAN PICK");
        WatchClanSearch.Picked += name => Unawaited.TrailFailures(PickAsync(name, SourceRole.Watch), _services.AddTrail, "CLAN PICK");
        Unloaded += (_, _) => _closing.Cancel();

        Refresh();
    }

    /// <summary>The reader this section picks clans for.</summary>
    public string Slug => _slug;

    /// <summary>
    /// Reads the clans list for the search boxes, once. Called by the game page while the mode is on, never while it is off:
    /// the list comes from the game's host, and an off mode contacts nothing.
    /// </summary>
    public void Activate()
    {
        if (_namesAsked) return;
        _namesAsked = true;
        Unawaited.TrailFailures(LoadNamesAsync(), _services.AddTrail, "CLAN NAMES");
    }

    /// <summary>First run lands here (A11): the main clan's search takes the keyboard.</summary>
    public void FocusMainSearch() => MainClanSearch.FocusSearch();

    private InstalledRecipe? Installed =>
        _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _slug, StringComparison.Ordinal));

    public void Refresh()
    {
        if (Installed is not { } installed)
        {
            // Readers ship with the app, so this is a build whose reader failed to load; Diagnostics says why.
            ClansTitle.Text = "";
            ClansLine.Text = "This version of Ur Score can't read this mode. Diagnostics says why.";
            return;
        }

        var recipe = installed.Recipe;
        var group = RecipeWords.Group(recipe);
        var groups = RecipeWords.Groups(recipe);
        var accounts = _services.KnownAccounts;
        var lists = ClansModel.Lists(recipe, _services.Sources, _services.Latest, accounts);

        _rendering = true;
        try
        {
            ClansTitle.Text = groups.ToUpperInvariant();
            ClansLine.Text = $"Changes apply at once. Removing a {group} keeps its score book.";

            MainClanLabel.Text = $"Your main {group}";
            MainCurrentLine.Text = lists.Main is { } main
                ? $"★ {main.Name} · {main.Who}"
                : $"No main {group} yet. Type a few letters of its name.";
            MainClanSearch.SetLabel($"Your main {group}");

            MineLabel.Text = $"{groups} your accounts are in";
            MineList.ItemsSource = lists.Mine;
            ShowLine(MineEmptyLine, lists.Mine.Count == 0 ? "None yet." : "");
            AddMineButton.Content = $"Add a {group} your accounts are in";
            MineClanSearch.SetLabel($"Add a {group} your accounts are in");

            WatchLabel.Text = $"{groups} you're watching";
            WatchList.ItemsSource = lists.Watching;
            ShowLine(WatchEmptyLine, lists.Watching.Count == 0 ? "None yet." : "");
            WatchClanButton.Content = $"Watch a {group}";
            WatchClanSearch.SetLabel($"Watch a {group}");

            var top = ClansModel.GroupListSource(_services.Sources, _services.Installed, _slug);
            TopRow.Visibility = top is null ? Visibility.Collapsed : Visibility.Visible;
            if (top is not null
                && _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, top.Recipe, StringComparison.Ordinal))?.Recipe is { } topRecipe)
            {
                var period = RecipeWords.Period(topRecipe.Period is not null ? topRecipe : recipe);
                TopSwitch.Content = $"Top of the {period}";
                AutomationProperties.SetName(TopSwitch, $"Top of the {period}");
                TopSwitch.IsChecked = top.Enabled;
                // "Shown live, never kept" stopped being true in 0.3.10, and it was the screen where you switch the
                // list ON, the worst place for it (2026-09-20). The clans list is named by what it is, never by its file.
                TopLine.Text = $"The leading {RecipeWords.GroupsLower(recipe)} of the {period}, for the Top of the {period} panel. "
                    + $"Every read keeps where yours stands, how the field is doing, and the top {GroupRows.Top} by name.";
            }

            ShowLine(RequestsLine, ClansModel.RequestsLine(ClansModel.RequestsPerHour(_services.ActiveSources, _services.Installed, accounts.Count)));
        }
        finally
        {
            _rendering = false;
        }
    }

    private async Task LoadNamesAsync()
    {
        if (Installed?.Recipe is not { } recipe) return;

        ClanSearchBox[] boxes = [MainClanSearch, MineClanSearch, WatchClanSearch];
        var group = RecipeWords.Group(recipe);
        void Status(string text)
        {
            foreach (var box in boxes) box.SetStatus(text);
        }

        if (RecipeWords.MainInput(recipe)?.Search is not { } search)
        {
            foreach (var box in boxes) box.AllowTyped = true;
            Status($"Type the exact {group} name, then press Enter.");
            return;
        }

        Status($"Reading the {group} list once from {RecipeHosts.HostOf(search.Url)}…");

        SearchListResult result;
        try
        {
            result = await _services.SearchListAsync(search, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (result.Problem is not null)
        {
            foreach (var box in boxes) box.AllowTyped = true;
            Status(_services.Redactor.Redact($"{result.Problem} Type the exact {group} name, then press Enter."));
            return;
        }

        foreach (var box in boxes) box.SetNames(result.Names);
        Status($"Searches all {result.Names.Count.ToString("N0", CultureInfo.InvariantCulture)} {group} names. Enter or a click picks one.");
    }

    private async Task PickAsync(string picked, SourceRole role)
    {
        if (Installed?.Recipe is not { } recipe) return;

        var name = picked.Trim();
        var line = role switch { SourceRole.Main => MainFoundLine, SourceRole.Mine => MineFoundLine, _ => WatchFoundLine };
        var watchInstead = role switch { SourceRole.Main => MainWatchInsteadButton, SourceRole.Mine => MineWatchInsteadButton, _ => null };
        if (watchInstead is not null) watchInstead.Visibility = Visibility.Collapsed;

        var before = _services.Sources;
        SourceChange change;
        try
        {
            change = ClansModel.Pick(before, recipe, name, role);
        }
        catch (Exception ex)
        {
            ShowLine(line, _services.Redactor.Redact($"Could not add that: {ex.Message}"));
            return;
        }

        if (change.Note is not null)
        {
            ShowLine(line, change.Note);
            return;
        }

        // Asked in Ur Score's own window, in the theme, never a stock Windows box (owner rule, backlog V3-S.10).
        // The request count is about what will read: an off mode's clans are not (review round 2).
        if (ClansModel.AddQuestion(before, change, recipe, _services.ActiveReaders, _services.KnownAccounts.Count, name) is { } question
            && !ConfirmWindow.Ask(Window.GetWindow(this), question))
        {
            return;
        }

        try
        {
            _services.SaveSources(change.Sources);
        }
        catch (Exception ex)
        {
            ShowLine(line, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return;
        }

        if (role == SourceRole.Watch)
        {
            WatchClanSearch.Visibility = Visibility.Collapsed;
            ShowLine(line, $"Watching {name}. Only its own numbers are read; none of its members are matched to your accounts.");
            Refresh();
            return;
        }

        ShowLine(line, $"Reading {name} once…");

        RecipeSnapshot? snapshot;
        try
        {
            snapshot = await _services.ReadOnceAsync(change.SourceId, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowLine(line, _services.Redactor.Redact($"Added {name}, but the read failed: {ex.Message}"));
            return;
        }

        var probe = ClansModel.Probe(name, snapshot, _services.KnownAccounts);
        ShowLine(line, _services.Redactor.Redact(probe.Text));

        if (role == SourceRole.Main) _mainProbeId = probe.OfferWatch ? change.SourceId : null;
        else _mineProbeId = probe.OfferWatch ? change.SourceId : null;

        if (watchInstead is not null) watchInstead.Visibility = probe.OfferWatch ? Visibility.Visible : Visibility.Collapsed;
        if (role == SourceRole.Mine && !probe.OfferWatch) MineClanSearch.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void OnAddMineClick(object sender, RoutedEventArgs e)
    {
        MineClanSearch.Visibility = Visibility.Visible;
        MineClanSearch.FocusSearch();
    }

    private void OnWatchClanClick(object sender, RoutedEventArgs e)
    {
        WatchClanSearch.Visibility = Visibility.Visible;
        WatchClanSearch.FocusSearch();
    }

    private void OnWatchInsteadClick(object sender, RoutedEventArgs e)
    {
        var fromMain = ReferenceEquals(sender, MainWatchInsteadButton);
        if ((fromMain ? _mainProbeId : _mineProbeId) is not { } id) return;

        if (!Save(ClansModel.WatchInstead(_services.Sources, id))) return;

        ShowLine(fromMain ? MainFoundLine : MineFoundLine,
            "Watching it instead. Only its own numbers are read; none of its members are matched to your accounts.");
        ((Button)sender).Visibility = Visibility.Collapsed;
        if (fromMain) _mainProbeId = null;
        else _mineProbeId = null;
    }

    private void OnMakeMainClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Save(SourceRules.MakeMain(_services.Sources, id));
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) Save(SourceRules.Remove(_services.Sources, id));
    }

    private void OnTopSwitched(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;
        if (ClansModel.GroupListSource(_services.Sources, _services.Installed, _slug) is { } top)
        {
            Save(ClansModel.SetEnabled(_services.Sources, top.Id, TopSwitch.IsChecked == true));
        }
    }

    private bool Save(IReadOnlyList<Source> sources)
    {
        try
        {
            _services.SaveSources(sources);
            Refresh();
            return true;
        }
        catch (Exception ex)
        {
            ShowLine(RequestsLine, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return false;
        }
    }
}
