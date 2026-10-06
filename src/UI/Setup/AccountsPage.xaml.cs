using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Games;
using static Labs626.UrScore.UI.TextLines;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Your accounts (spec §7.2): Send per account per mode, and where each account was found.</summary>
public partial class AccountsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<AccountRow> _rows = [];

    /// <summary>Why the last Send tick was undone, kept on screen until the next tick.</summary>
    private string? _refusal;

    /// <summary>What went wrong getting your accounts when this page asked, kept on screen for the page's life (S1-12.4).</summary>
    private string? _problem;

    private bool _reverting;

    /// <summary>RoRoRo is being asked for the accounts; the listed line says so until it answers or the wait runs out.</summary>
    private bool _asking;

    public AccountsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
        _ = AskForAccountsAsync();
    }

    public void Refresh()
    {
        ListedLine.Text = _asking
            ? AccountsModel.AskingForAccounts
            : AccountsModel.ListedLine(_services.Accounts.Last, _services.AccountsCache.SavedAt(), DateTimeOffset.UtcNow);

        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged -= OnTick;
        var accounts = _services.KnownAccounts;
        // The readers of modes that are on only: an off mode reads and sends nothing, so it has no Send column. Columns and
        // ticks are named by mode (ReaderNames), never by recipe.
        var labels = _services.Installed.ToDictionary(i => i.Recipe.Slug, i => ReaderNames.For(i.Recipe.Slug, _services.Catalog, _services.Installed), StringComparer.Ordinal);
        var active = _services.ActiveReaders;
        // Placed by each clan's members list as well as by who scored this battle (backlog V3-S.20).
        _rows = AccountsModel.Rows(accounts, active, _services.ActiveSources, _services.Latest, _services.AvatarFileFor, labels, _services.Members);
        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged += OnTick;

        RecipeHeaders.ItemsSource = AccountsModel.SendingRecipes(active).Select(r => labels[r.Recipe.Slug]).ToList();
        AccountsTable.ItemsSource = _rows;
        ShowLine(AccountsEmptyLine, accounts.Count == 0 ? "RoRoRo hasn't shared any accounts yet. Start RoRoRo and add your accounts there." : "");
        ShowMessage();
    }

    /// <summary>Every redraw says the message line from what the page keeps, so none wipes what it said (backlog S1-12.4).</summary>
    private void ShowMessage() => ShowLine(AccountsBudgetLine, AccountsModel.MessageLine(_refusal, _problem, _services.BudgetWarning));

    private async Task AskForAccountsAsync()
    {
        _asking = true;
        ListedLine.Text = AccountsModel.AskingForAccounts;
        try
        {
            await _services.RefreshAccountsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Unreachable for RoRoRo's sake (a slow or broken answer gives the saved list), so not "Could not ask RoRoRo": only
            // what Ur Score does with the answer can throw here. The type goes to the trail; the page says it in plain words.
            _problem = AccountsModel.AccountsNotUpdated;
            _services.AddTrail($"ACCOUNTS PAGE: {ex.GetType().Name} while getting your accounts.");
            ShowMessage();
        }
        finally
        {
            _asking = false;
            ListedLine.Text = AccountsModel.ListedLine(_services.Accounts.Last, _services.AccountsCache.SavedAt(), DateTimeOffset.UtcNow);
        }
    }

    private void OnTick(object? sender, PropertyChangedEventArgs e)
    {
        if (_reverting || sender is not SendTick tick) return;

        var slug = tick.RecipeSlug;
        var accountId = tick.AccountId;
        var on = tick.On;

        // Deferred: saving re-renders the rows, and a row must not be replaced while its checkbox is mid-click. The
        // change is worked out inside, from the recipe state as it is by then, so a second quick tick builds on the
        // first tick's save instead of on the state both ticks started from.
        Dispatcher.BeginInvoke(() =>
        {
            if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, slug, StringComparison.Ordinal)) is not { } installed) return;

            var ids = _services.KnownAccounts.Select(a => a.AccountId).ToList();
            // Only the readers that read count toward RoRoRo's history limit: an off mode sends nothing (review round 2).
            var change = AccountsModel.ToggleSend(installed, _services.ActiveReaders, ids, accountId, on);

            if (change.Refusal is not null)
            {
                _refusal = change.Refusal;
                _reverting = true;
                tick.On = false;
                _reverting = false;
                ShowMessage();
                return;
            }

            _refusal = null;
            try
            {
                _services.SaveRecipeState(installed.Recipe, change.State);
            }
            catch (Exception ex)
            {
                _refusal = _services.Redactor.Redact($"Could not save that change: {ex.Message}");
                ShowMessage();
            }
        }, DispatcherPriority.Background);
    }
}
