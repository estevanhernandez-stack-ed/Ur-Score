using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

/// <summary>
/// What an accounts table asks for and the board keeps for this session only, never in <c>boards.json</c>: each table's
/// sort by panel id (D15), and the account picked on each board by board id (D18). A table popped out sorts and picks
/// as it does on its board.
/// </summary>
public partial class BoardWindow
{
    private readonly Dictionary<string, AccountSort> _sorts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _picked = new(StringComparer.Ordinal);

    /// <summary>A redraw for a sort or a pick is waiting; more of them before it runs are drawn by that one.</summary>
    private bool _accountsRedrawQueued;

    /// <summary>The tables whose sort or pick the waiting redraw answers, each with the token that lets it put focus back.</summary>
    private readonly List<(AccountsTablePanel Table, int Token)> _answers = [];

    private void HookAccounts()
    {
        BoardPanels.AddHandler(AccountsTablePanel.SortEvent, new EventHandler<AccountSortEventArgs>(OnAccountSort));
        BoardPanels.AddHandler(AccountsTablePanel.PickEvent, new EventHandler<AccountPickEventArgs>(OnAccountPick));
    }

    private void HookAccounts(PanelPopOutWindow window)
    {
        window.View.AddHandler(AccountsTablePanel.SortEvent, new EventHandler<AccountSortEventArgs>(OnAccountSort));
        window.View.AddHandler(AccountsTablePanel.PickEvent, new EventHandler<AccountPickEventArgs>(OnAccountPick));
    }

    /// <summary>What a panel on <paramref name="boardId"/> shows this session.</summary>
    private PanelSession SessionFor(string boardId, PanelDef def) =>
        new(_sorts.GetValueOrDefault(def.Id), _picked.TryGetValue(boardId, out var userId) ? userId : null);

    private void OnAccountSort(object? sender, AccountSortEventArgs e)
    {
        if (TableAt(e.OriginalSource) is not { } at) return;

        e.Handled = true;
        _sorts[at.PanelId] = AccountSort.Clicked(e.Column);
        RedrawAccountsLater(e.OriginalSource as AccountsTablePanel);
    }

    private void OnAccountPick(object? sender, AccountPickEventArgs e)
    {
        if (TableAt(e.OriginalSource) is not { } at) return;

        e.Handled = true;
        // A pick that changes nothing queues nothing, and so arms nothing: no later redraw moves focus for it.
        if (!AccountsTableFocus.PickChanges(_picked.TryGetValue(at.BoardId, out var current) ? current : null, e.UserId)) return;

        _picked[at.BoardId] = e.UserId;
        RedrawAccountsLater(e.OriginalSource as AccountsTablePanel);
    }

    /// <summary>
    /// The sort or pick is kept at once; the board is drawn again once the grid's own Sorting or SelectionChanged has
    /// returned, and after input already waiting (the rest of a click, the next arrow key), so the grid is never
    /// rebuilt under its own handler. The Account cards on the same board show the pick in the same redraw. The asking
    /// table is armed now and handed its token once that redraw is done, so it, and no refresh, puts focus back.
    /// </summary>
    private void RedrawAccountsLater(AccountsTablePanel? asking)
    {
        if (asking is not null) _answers.Add((asking, asking.ArmFocusRestore()));
        if (_accountsRedrawQueued) return;

        _accountsRedrawQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _accountsRedrawQueued = false;
            var answers = _answers.ToList();
            _answers.Clear();

            Render();
            foreach (var (table, token) in answers) table.AnswerRedraw(token);
        }, DispatcherPriority.Background);
    }

    /// <summary>The panel and board of a table that raised an event: on the board on screen, or in a pop-out.</summary>
    private (string PanelId, string BoardId)? TableAt(object? source)
    {
        if (PanelAt(source) is { } def && _boardId is { } boardId) return (def.Id, boardId);

        foreach (var window in _popOuts.Values)
        {
            if (ReferenceEquals(window.View, source) && BoardEdits.Find(_services.Boards, window.PanelId) is { } found)
            {
                return (window.PanelId, found.Board.Id);
            }
        }

        return null;
    }
}
