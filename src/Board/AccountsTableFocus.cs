namespace Labs626.UrScore.Board;

/// <summary>
/// What the accounts table keeps as it is drawn again (D14, D18): which row is selected, and, when the redraw answers
/// the table's own sort or pick, where keyboard focus goes back to. The table control only carries these out.
/// </summary>
public static class AccountsTableFocus
{
    /// <summary>
    /// A redraw answering the table's own heading click or row pick puts keyboard focus back only when focus was in the
    /// table. Whether a redraw answers the table is <see cref="FocusRestoreGate"/>'s to say, and it is asked first: a data
    /// refresh keeps the selection and the sort but never moves focus, so it never scrolls the board or a pop-out back to
    /// the table while you read what is under it.
    /// </summary>
    public static bool RestoresFocus(bool focusInTable) => focusInTable;

    /// <summary>The account a selected row picks; the totals row, or no row, picks nothing.</summary>
    public static long? PickOf(AccountRow? selected) => selected is { IsTotal: false } row ? row.UserId : null;

    /// <summary>A pick changes the board only when it names another account than the one picked; only then is a redraw queued.</summary>
    public static bool PickChanges(long? current, long picked) => current != picked;

    /// <summary>The row selected after a redraw: the picked account, or the totals row when the keyboard was on it (it picks nothing).</summary>
    public static AccountRow? SelectedRow(AccountsTableModel model, bool totalSelected) =>
        model.Rows.FirstOrDefault(r => totalSelected ? r.IsTotal : r.Picked);

    /// <summary>The row focus goes back to: the same account wherever it sorted to (or the totals row), else the picked one, else the first.</summary>
    public static AccountRow? FocusRow(AccountsTableModel model, long userId, bool onTotal) =>
        model.Rows.FirstOrDefault(r => onTotal ? r.IsTotal : !r.IsTotal && r.UserId == userId)
        ?? model.Rows.FirstOrDefault(r => r.Picked)
        ?? model.Rows.FirstOrDefault();

    /// <summary>
    /// The column focus goes back to, by what it holds rather than where it was: a sort moves Today and 7 days next to
    /// the sorted stat. The name and a stat are found by key; a change column by kind, as it follows the sort. A column
    /// that is gone falls back to its old place, as far as the table reaches; -1 when there are no columns.
    /// </summary>
    public static int FocusColumn(IReadOnlyList<AccountColumn> columns, AccountColumn? was, int wasIndex)
    {
        if (columns.Count == 0) return -1;

        if (was is not null)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                var same = was.Kind is AccountColumnKind.Today or AccountColumnKind.Week
                    ? column.Kind == was.Kind
                    : column.Kind == was.Kind && column.Key == was.Key;
                if (same) return index;
            }
        }

        return Math.Clamp(wasIndex, 0, columns.Count - 1);
    }
}

/// <summary>
/// Which redraw may put a table's keyboard focus back (D14). A sort or pick that queues a redraw arms the gate and that
/// redraw carries the token it got; the redraw restores focus only with the token still pending, and uses it up. A
/// refresh carries no token, a sort or pick that changed nothing armed nothing, and a later arm supersedes an earlier
/// one whose redraw was folded into the same pass.
/// </summary>
public sealed class FocusRestoreGate
{
    private int _generation;
    private int? _pending;

    /// <summary>A sort or pick queued a redraw: the token that redraw carries.</summary>
    public int Arm()
    {
        _generation++;
        _pending = _generation;
        return _generation;
    }

    /// <summary>Whether the redraw carrying <paramref name="token"/> (null for a refresh) restores focus; a match is used up.</summary>
    public bool Restores(int? token)
    {
        if (token is null || token != _pending) return false;

        _pending = null;
        return true;
    }
}
