using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Composition;

namespace Labs626.UrScore.UI;

/// <summary>Setup › Your accounts (spec §7.2): Send per account per recipe, and where each account was found.</summary>
public partial class AccountsPage : UserControl, ISetupPage
{
    private readonly ISetupServices _services;
    private IReadOnlyList<AccountRow> _rows = [];

    /// <summary>Why the last Send tick was undone, kept on screen until the next tick.</summary>
    private string? _refusal;

    private bool _reverting;

    public AccountsPage(ISetupServices services)
    {
        InitializeComponent();
        _services = services;
        Refresh();
        _ = AskForAccountsAsync();
    }

    public void Refresh()
    {
        ListedLine.Text = AccountsModel.ListedLine(_services.Accounts.Last, _services.AccountsCache.SavedAt(), DateTimeOffset.UtcNow);

        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged -= OnTick;
        var accounts = _services.KnownAccounts;
        _rows = AccountsModel.Rows(accounts, _services.Installed, _services.Sources, _services.Latest);
        foreach (var tick in _rows.SelectMany(r => r.Sends)) tick.PropertyChanged += OnTick;

        RecipeHeaders.ItemsSource = AccountsModel.SendingRecipes(_services.Installed).Select(r => r.Recipe.Name).ToList();
        AccountsTable.ItemsSource = _rows;
        Show(AccountsEmptyLine, accounts.Count == 0 ? "RoRoRo hasn't shared any accounts yet. Start RoRoRo and add your accounts there." : "");
        Show(AccountsBudgetLine, _refusal ?? _services.BudgetWarning ?? "");
    }

    private async Task AskForAccountsAsync()
    {
        try
        {
            await _services.RefreshAccountsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Show(AccountsBudgetLine, _services.Redactor.Redact($"Could not ask RoRoRo for your accounts: {ex.Message}"));
        }
    }

    private void OnTick(object? sender, PropertyChangedEventArgs e)
    {
        if (_reverting || sender is not SendTick tick) return;
        if (_services.Installed.FirstOrDefault(i => string.Equals(i.Recipe.Slug, tick.RecipeSlug, StringComparison.Ordinal)) is not { } installed) return;

        var ids = _services.KnownAccounts.Select(a => a.AccountId).ToList();
        var change = AccountsModel.ToggleSend(installed, _services.Installed, ids, tick.AccountId, tick.On);

        // Deferred: saving re-renders the rows, and a row must not be replaced while its checkbox is mid-click.
        Dispatcher.BeginInvoke(() =>
        {
            if (change.Refusal is not null)
            {
                _refusal = change.Refusal;
                _reverting = true;
                tick.On = false;
                _reverting = false;
                Show(AccountsBudgetLine, _refusal);
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
                Show(AccountsBudgetLine, _refusal);
            }
        }, DispatcherPriority.Background);
    }

    private static void Show(TextBlock line, string text)
    {
        line.Text = text;
        line.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
