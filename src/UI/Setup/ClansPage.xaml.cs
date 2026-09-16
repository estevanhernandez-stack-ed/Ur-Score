using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>
/// Setup › Clans for one recipe (spec §7.1): the main clan search, the clans your accounts are in, the
/// clans you watch, and the Top switch. Every change is saved and applied at once.
/// </summary>
public partial class ClansPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private readonly string _slug;
    private readonly CancellationTokenSource _closing = new();
    private string? _mainProbeId;
    private string? _mineProbeId;
    private bool _rendering;

    /// <param name="note">What the import that opened this page did, said above the page and left alone by every redraw (S1-12.4).</param>
    public ClansPage(ISetupServices services, string recipeSlug, string? note = null)
    {
        InitializeComponent();
        _services = services;
        _slug = recipeSlug;
        Show(ImportedLine, note ?? "");

        MainClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Main);
        MineClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Mine);
        WatchClanSearch.Picked += name => _ = PickAsync(name, SourceRole.Watch);
        Unloaded += (_, _) => _closing.Cancel();

        Refresh();
        _ = LoadNamesAsync();
    }

    private InstalledRecipe? Installed =>
        _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, _slug, StringComparison.Ordinal));

    public void Refresh()
    {
        if (Installed is not { } installed)
        {
            ClansPageTitle.Text = "Not installed";
            RecipeLine.Text = "This recipe is no longer installed.";
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
            ClansPageTitle.Text = groups;
            RecipeLine.Text = $"From {recipe.Name}. Changes apply at once. Removing a {group} keeps its score book.";

            MainClanLabel.Text = $"Your main {group}";
            MainCurrentLine.Text = lists.Main is { } main
                ? $"★ {main.Name} · {main.Who}"
                : $"No main {group} yet. Type a few letters of its name.";
            MainClanSearch.SetLabel($"Your main {group}");

            MineLabel.Text = $"{groups} your accounts are in";
            MineList.ItemsSource = lists.Mine;
            Show(MineEmptyLine, lists.Mine.Count == 0 ? "None yet." : "");
            AddMineButton.Content = $"Add a {group} your accounts are in";
            MineClanSearch.SetLabel($"Add a {group} your accounts are in");

            WatchLabel.Text = $"{groups} you're watching";
            WatchList.ItemsSource = lists.Watching;
            Show(WatchEmptyLine, lists.Watching.Count == 0 ? "None yet." : "");
            WatchClanButton.Content = $"Watch a {group}";
            WatchClanSearch.SetLabel($"Watch a {group}");

            var top = ClansModel.GroupListSource(_services.Sources, _services.Installed);
            TopRow.Visibility = top is null ? Visibility.Collapsed : Visibility.Visible;
            if (top is not null
                && _services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, top.Recipe, StringComparison.Ordinal))?.Recipe is { } topRecipe)
            {
                var period = RecipeWords.Period(topRecipe.Period is not null ? topRecipe : recipe);
                TopSwitch.Content = $"Top of the {period}";
                AutomationProperties.SetName(TopSwitch, $"Top of the {period}");
                TopSwitch.IsChecked = top.Enabled;
                TopLine.Text = $"The leading {RecipeWords.GroupsLower(recipe)} from {topRecipe.Name}, for the Top of the {period} panel. Shown live, never kept.";
            }

            Show(RequestsLine, ClansModel.RequestsLine(ClansModel.RequestsPerHour(_services.Sources, _services.Installed, accounts.Count)));
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
            Show(line, _services.Redactor.Redact($"Could not add that: {ex.Message}"));
            return;
        }

        if (change.Note is not null)
        {
            Show(line, change.Note);
            return;
        }

        // Asked in Ur Score's own window, in the theme, never a stock Windows box (owner rule, backlog V3-S.10).
        if (ClansModel.AddQuestion(before, change, recipe, _services.Installed, _services.KnownAccounts.Count, name) is { } question
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
            Show(line, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return;
        }

        if (role == SourceRole.Watch)
        {
            WatchClanSearch.Visibility = Visibility.Collapsed;
            Show(line, $"Watching {name}. Only its own numbers are read; none of its members are matched to your accounts.");
            Refresh();
            return;
        }

        Show(line, $"Reading {name} once…");

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
            Show(line, _services.Redactor.Redact($"Added {name}, but the read failed: {ex.Message}"));
            return;
        }

        var probe = ClansModel.Probe(name, snapshot, _services.KnownAccounts);
        Show(line, _services.Redactor.Redact(probe.Text));

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

        Show(fromMain ? MainFoundLine : MineFoundLine,
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
        if (ClansModel.GroupListSource(_services.Sources, _services.Installed) is { } top)
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
            Show(RequestsLine, _services.Redactor.Redact($"Could not save that change: {ex.Message}"));
            return false;
        }
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
