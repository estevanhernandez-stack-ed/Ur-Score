namespace Labs626.UrScore.Board;

/// <summary>
/// What the accounts table keeps as it is drawn again (D14, D18): which row is selected, and, when the redraw answers
/// the table's own sort or pick, where keyboard focus goes back to. The table control only carries these out.
/// </summary>
public static class AccountsTableFocus
{
    /// <summary>
    /// Only a redraw answering the table's own heading click or row pick puts keyboard focus back, and only when focus was
    /// in the table. A data refresh keeps the selection and the sort but never moves focus, so it never scrolls the board
    /// or a pop-out back to the table while you read what is under it.
    /// </summary>
    public static bool RestoresFocus(bool answersTable, bool focusInTable) => answersTable && focusInTable;

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
