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
    private string? _otherProbeId;
    private bool _rendering;
    private bool _namesAsked;

    /// <summary>
    /// Your accounts a members read placed while this section has been open (name your clan once, 0.7.0). Your own account ids
    /// only: a members read hands back nothing else. Accounts placed or let go for good are in the reader's state instead.
    /// </summary>
    private readonly HashSet<Guid> _placed = [];

    /// <summary>A members read has answered here, so the question about the rest stands on evidence, not on nothing read yet.</summary>
    private bool _membersRead;

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
        OtherClanSearch.Picked += name => Unawaited.TrailFailures(PickOtherAsync(name), _services.AddTrail, "CLAN PICK");
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
        Placement = PlaceSavedClansAsync();
        Unawaited.TrailFailures(Placement, _services.AddTrail, "CLAN MEMBERS");
    }

    /// <summary>The members reads <see cref="Activate"/> started, for a test to wait on.</summary>
    internal Task Placement { get; private set; } = Task.CompletedTask;

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

            ShowQuestion(recipe, lists);
            ShowLine(RequestsLine, ClansModel.RequestsLine(ClansModel.RequestsPerHour(_services.ActiveSources, _services.Installed, accounts.Count), _services.OffModeOf(_slug)));
        }
        finally
        {
            _rendering = false;
        }
    }

    private async Task LoadNamesAsync()
    {
        if (Installed?.Recipe is not { } recipe) return;

        ClanSearchBox[] boxes = [MainClanSearch, MineClanSearch, WatchClanSearch, OtherClanSearch];
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

    internal async Task PickAsync(string picked, SourceRole role)
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

        ClanProbe probe;
        try
        {
            probe = await PlaceAsync(recipe, name, snapshot);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ShowLine(line, _services.Redactor.Redact(probe.Text));

        if (role == SourceRole.Main) _mainProbeId = probe.OfferWatch ? change.SourceId : null;
        else _mineProbeId = probe.OfferWatch ? change.SourceId : null;

        if (watchInstead is not null) watchInstead.Visibility = probe.OfferWatch ? Visibility.Visible : Visibility.Collapsed;
        if (role == SourceRole.Mine && !probe.OfferWatch) MineClanSearch.Visibility = Visibility.Collapsed;
        Refresh();
    }

    /// <summary>
    /// Who of yours a pick's clan holds. From its members list when the reader has one (name your clan once, 0.7.0), so it
    /// answers between battles too (V3-S.20), and from the battle read only when it has none or the list couldn't be read.
    /// </summary>
    private async Task<ClanProbe> PlaceAsync(Recipe recipe, string name, RecipeSnapshot? snapshot)
    {
        var members = await _services.FindOwnMembersAsync(recipe, name, _closing.Token);
        if (members is null) return ClansModel.Probe(name, snapshot, _services.KnownAccounts);

        Placed(recipe, members, _services.KnownAccounts);
        return ClansModel.Placed(name, members, _services.KnownAccounts, snapshot);
    }

    /// <summary>Takes in what a members read found among <paramref name="among"/>, and settles for good once every account is placed.</summary>
    private void Placed(Recipe recipe, MembersResult members, IReadOnlyList<HostAccount> among)
    {
        if (members.Problem is not null) return;

        _membersRead = true;
        _placed.UnionWith(ClansModel.InClan(among, members).Select(a => a.AccountId));
        var state = Installed?.State ?? new RecipeState();
        if (ClansModel.Listed(_services.KnownAccounts).Count > 0 && ClansModel.Remaining(_services.KnownAccounts, _placed, state).Count == 0)
        {
            Settle(recipe, state);
        }
    }

    /// <summary>
    /// Opening the page with a main clan and accounts nobody has placed yet: each clan your accounts are in is asked for its
    /// members once, main first, stopping when nothing is left to place. Nothing is asked once every account is settled, so
    /// <b>That's all</b> keeps this quiet until RoRoRo lists a new account. Only while the mode is on (<see cref="Activate"/>).
    /// </summary>
    private async Task PlaceSavedClansAsync()
    {
        if (Installed is not { } installed || RecipeWords.MainInput(installed.Recipe)?.Members is null) return;

        var recipe = installed.Recipe;
        if (ClansModel.Lists(recipe, _services.Sources, _services.Latest, _services.KnownAccounts).Main is null) return;

        // RoRoRo's list now, not the one saved last session: a new account there is exactly what brings the question back.
        try
        {
            await _services.RefreshAccountsAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var lists = ClansModel.Lists(recipe, _services.Sources, _services.Latest, _services.KnownAccounts);
        if (lists.Main is null || ClansModel.Remaining(_services.KnownAccounts, _placed, Installed?.State ?? installed.State).Count == 0) return;

        var said = new List<string>();
        foreach (var clan in lists.Mine)
        {
            var rest = ClansModel.Remaining(_services.KnownAccounts, _placed, Installed?.State ?? installed.State);
            if (rest.Count == 0) break;

            MembersResult? members;
            try
            {
                members = await _services.FindOwnMembersAsync(recipe, clan.Name, _closing.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (members is null) return;
            if (members.Problem is not null) continue;

            // Only what was found is said: the question below already counts the accounts nobody holds, and a "you can still
            // watch it" with no button to do so would be an offer the page can't keep.
            var found = ClansModel.InClan(rest, members);
            Placed(recipe, members, rest);
            if (found.Count == 0) continue;
            // The first sentence counts against all your accounts, and the rest say "of them" after it.
            said.Add(said.Count == 0
                ? ClansModel.Placed(clan.Name, members with { Found = found.Select(a => a.RobloxUserId).ToHashSet() }, _services.KnownAccounts, null).Text
                : ClansModel.PlacedOther(clan.Name, found).Text);
        }

        ShowLine(PlaceAccountsLine, _services.Redactor.Redact(string.Join(" ", said)));
        Refresh();
    }

    /// <summary>A clan picked under the question: added as one of yours, never main, and asked only about the accounts left.</summary>
    internal async Task PickOtherAsync(string picked)
    {
        if (Installed?.Recipe is not { } recipe) return;

        var name = picked.Trim();
        OtherWatchInsteadButton.Visibility = Visibility.Collapsed;
        _otherProbeId = null;

        var before = _services.Sources;
        SourceChange change;
        try
        {
            change = ClansModel.Pick(before, recipe, name, SourceRole.Mine);
        }
        catch (Exception ex)
        {
            ShowLine(PlaceAccountsLine, _services.Redactor.Redact($"Could not add that: {ex.Message}"));
            return;
        }

        if (change.Note is not null)
        {
            ShowLine(PlaceAccountsLine, change.Note);
            return;
        }

        // Asked in Ur Score's own window, as any other pick past the confirm line is (V3-S.10).
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
            ShowLine(PlaceAccountsLine, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return;
        }

        ShowLine(PlaceAccountsLine, $"Reading who is in {name}…");
        var rest = ClansModel.Remaining(_services.KnownAccounts, _placed, Installed?.State ?? new RecipeState());

        MembersResult? members;
        try
        {
            members = await _services.FindOwnMembersAsync(recipe, name, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (members?.Problem is { } problem)
        {
            ShowLine(PlaceAccountsLine, _services.Redactor.Redact($"Added {name}, but who is in it couldn't be read just now. {problem}"));
            Refresh();
            return;
        }

        var probe = ClansModel.PlacedOther(name, members is null ? [] : ClansModel.InClan(rest, members));
        if (members is not null) Placed(recipe, members, rest);
        ShowLine(PlaceAccountsLine, _services.Redactor.Redact(probe.Text));
        _otherProbeId = probe.OfferWatch ? change.SourceId : null;
        OtherWatchInsteadButton.Visibility = probe.OfferWatch ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <summary>
    /// The question about the accounts no clan here holds: shown once a members read has answered and while any remain,
    /// gone when every account is placed or <b>That's all</b> was pressed for them.
    /// </summary>
    private void ShowQuestion(Recipe recipe, ClanLists lists)
    {
        var state = Installed?.State ?? new RecipeState();
        var rest = ClansModel.Remaining(_services.KnownAccounts, _placed, state);
        var group = RecipeWords.Group(recipe);
        var question = _membersRead ? ClansModel.RemainingLine(rest.Count, [.. lists.Mine.Select(r => r.Name)], group) : null;

        ShowLine(RemainingAccountsLine, question ?? "");
        var asking = question is null ? Visibility.Collapsed : Visibility.Visible;
        OtherClanSearch.Visibility = asking;
        ThatsAllButton.Visibility = asking;
        OtherClanSearch.SetLabel($"Another {group} your accounts are in");

        PlacementPanel.Visibility = question is not null || OtherWatchInsteadButton.Visibility == Visibility.Visible
                                    || PlaceAccountsLine.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>"That's all": the accounts left are in no clan Ur Score should read, so it stops asking about them.</summary>
    private void OnThatsAllClick(object sender, RoutedEventArgs e)
    {
        if (Installed is not { } installed) return;

        if (!Settle(installed.Recipe, installed.State)) return;
        ShowLine(PlaceAccountsLine, "Ur Score won't ask about them again unless RoRoRo lists another account.");
        OtherWatchInsteadButton.Visibility = Visibility.Collapsed;
        _otherProbeId = null;
        Refresh();
    }

    private bool Settle(Recipe recipe, RecipeState state)
    {
        try
        {
            _services.SaveSettledAccounts(recipe, ClansModel.Settle(_services.KnownAccounts, _placed, state));
            return true;
        }
        catch (Exception ex)
        {
            ShowLine(PlaceAccountsLine, _services.Redactor.Redact($"Could not save that: {ex.Message}"));
            return false;
        }
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
        var fromOther = ReferenceEquals(sender, OtherWatchInsteadButton);
        if ((fromMain ? _mainProbeId : fromOther ? _otherProbeId : _mineProbeId) is not { } id) return;

        if (!Save(ClansModel.WatchInstead(_services.Sources, id))) return;

        ShowLine(fromMain ? MainFoundLine : fromOther ? PlaceAccountsLine : MineFoundLine,
            "Watching it instead. Only its own numbers are read; none of its members are matched to your accounts.");
        ((Button)sender).Visibility = Visibility.Collapsed;
        if (fromMain) _mainProbeId = null;
        else if (fromOther) _otherProbeId = null;
        else _mineProbeId = null;
        Refresh();
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
