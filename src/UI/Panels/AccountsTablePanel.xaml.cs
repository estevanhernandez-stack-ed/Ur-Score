using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Labs626.UrScore.Board;

namespace Labs626.UrScore.UI;

// Setup's Your accounts page has an AccountRow of its own in this namespace.
using TableRow = Labs626.UrScore.Board.AccountRow;

/// <summary>A heading of the accounts table was clicked: the board sorts by it for this session (D15).</summary>
public sealed class AccountSortEventArgs(RoutedEvent routedEvent, AccountColumn column) : RoutedEventArgs(routedEvent)
{
    public AccountColumn Column { get; } = column;
}

/// <summary>An account's row was picked, by a click or the arrow keys: the account cards on its board show it (D18).</summary>
public sealed class AccountPickEventArgs(RoutedEvent routedEvent, long userId) : RoutedEventArgs(routedEvent)
{
    public long UserId { get; } = userId;
}

/// <summary>
/// Your accounts side by side (D14). It decides nothing: a heading click and a row pick are raised for the board, which
/// keeps them for the session and draws the table again from <see cref="PanelModels.AccountsTable"/>, after the grid's
/// own handler has returned. A redraw replaces every row; the one redraw that answers a sort or pick that changed the
/// board puts keyboard focus that was in the table back on the same row and column once the new rows are laid out, so
/// the arrow keys carry on from there. A data refresh keeps the selection and the sort and leaves focus and scrolling
/// alone (<see cref="AccountsTableFocus"/>, <see cref="FocusRestoreGate"/>).
/// </summary>
public partial class AccountsTablePanel : UserControl
{
    public static readonly RoutedEvent SortEvent = EventManager.RegisterRoutedEvent(
        "Sort", RoutingStrategy.Bubble, typeof(EventHandler<AccountSortEventArgs>), typeof(AccountsTablePanel));

    public static readonly RoutedEvent PickEvent = EventManager.RegisterRoutedEvent(
        "Pick", RoutingStrategy.Bubble, typeof(EventHandler<AccountPickEventArgs>), typeof(AccountsTablePanel));

    private IReadOnlyList<AccountColumn> _columns = [];

    /// <summary>Set while a model is drawn, so selecting its picked row isn't taken for a new pick.</summary>
    private bool _rendering;

    /// <summary>Which redraw may put focus back: the one the board queued for this table's own sort or pick.</summary>
    private readonly FocusRestoreGate _restore = new();

    /// <summary>Where keyboard focus was as the last armed sort or pick was raised; null when it wasn't in the table.</summary>
    private FocusPlace? _armedPlace;

    public AccountsTablePanel() => InitializeComponent();

    public void Render(AccountsTableModel model)
    {
        var onTotal = AccountsGrid.SelectedItem is TableRow { IsTotal: true };

        _rendering = true;
        try
        {
            if (!model.Columns.SequenceEqual(_columns)) BuildColumns(model.Columns);
            _columns = model.Columns;
            DataContext = model;

            // The heading's arrow says it; this tells the grid's headers the same, for UI Automation. Only once the rows
            // are in: the grid clears every column's sort direction as its items change.
            for (var index = 0; index < model.Columns.Count && index < AccountsGrid.Columns.Count; index++)
            {
                var column = model.Columns[index];
                AccountsGrid.Columns[index].SortDirection = column.Sorted
                    ? column.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending
                    : null;
            }

            AccountsGrid.SelectedItem = AccountsTableFocus.SelectedRow(model, onTotal);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>
    /// The board has queued a redraw for a sort or pick this table raised, one that changed the board: the token that
    /// redraw carries. Where focus is now is kept, before any refresh can tear the focused cell down.
    /// </summary>
    public int ArmFocusRestore()
    {
        _armedPlace = AccountsGrid.IsKeyboardFocusWithin ? CurrentPlace() : null;
        return _restore.Arm();
    }

    /// <summary>
    /// The redraw carrying <paramref name="token"/> has drawn this table: focus goes back where it was, once the new rows
    /// are laid out, if the token is still the pending one and focus was in the table. Nothing else restores focus.
    /// </summary>
    public void AnswerRedraw(int token)
    {
        if (!_restore.Restores(token)) return;

        var place = _armedPlace;
        _armedPlace = null;
        if (!AccountsTableFocus.RestoresFocus(focusInTable: place is not null)) return;

        Dispatcher.BeginInvoke(() => RestoreFocus(place!.Value), DispatcherPriority.Loaded);
    }

    /// <summary>One grid column per model column; a column's sort member is its index, so a click names the model column.</summary>
    private void BuildColumns(IReadOnlyList<AccountColumn> columns)
    {
        AccountsGrid.Columns.Clear();
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            DataGridColumn built = column.Kind == AccountColumnKind.Name
                ? new DataGridTemplateColumn
                {
                    CellTemplate = (DataTemplate)FindResource("AccountNameCell"),
                    ClipboardContentBinding = new Binding(nameof(TableRow.Name)),

                    // The name takes the width the numbers leave, so the table spans its card as the mock's does.
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    MinWidth = 160,
                }
                : new DataGridTextColumn
                {
                    Binding = new Binding($"Cells[{index}]"),
                    ElementStyle = (Style)FindResource("NumberCell"),
                    HeaderStyle = (Style)FindResource("PanelTableNumberHeader"),
                    Width = DataGridLength.Auto,
                    MinWidth = column.Kind == AccountColumnKind.Stat ? 96 : 76,
                };

            built.Header = column.Heading;
            built.SortMemberPath = index.ToString(CultureInfo.InvariantCulture);
            built.CanUserSort = column.CanSort;
            AccountsGrid.Columns.Add(built);
        }
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        // The grid never sorts its own text; the board sorts the numbers.
        e.Handled = true;
        if (int.TryParse(e.Column.SortMemberPath, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < _columns.Count)
        {
            RaiseEvent(new AccountSortEventArgs(SortEvent, _columns[index]));
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selector's SelectionChanged bubbles; nothing above the table is asking about it.
        e.Handled = true;
        if (_rendering || AccountsTableFocus.PickOf(AccountsGrid.SelectedItem as TableRow) is not { } userId) return;

        RaiseEvent(new AccountPickEventArgs(PickEvent, userId));
    }

    /// <summary>The table grows to all its rows and never scrolls up or down itself, so the wheel scrolls the board.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        e.Handled = true;
        RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
    }

    /// <summary>Where keyboard focus is in the table: the row, by account (the totals row by itself), and the column, by what it holds and where it was.</summary>
    private readonly record struct FocusPlace(long UserId, bool IsTotal, AccountColumn? Column, int ColumnIndex);

    private FocusPlace CurrentPlace()
    {
        var cell = AccountsGrid.CurrentCell;
        var row = cell.Item as TableRow ?? AccountsGrid.SelectedItem as TableRow;
        var index = cell.Column is { } current ? AccountsGrid.Columns.IndexOf(current) : 0;
        var column = index >= 0 && index < _columns.Count ? _columns[index] : null;
        return new FocusPlace(row?.UserId ?? 0, row?.IsTotal ?? false, column, Math.Max(index, 0));
    }

    /// <summary>After a redraw has laid the new rows out: focus on the row and column <see cref="AccountsTableFocus"/> names.</summary>
    private void RestoreFocus(FocusPlace place)
    {
        if (!IsVisible || DataContext is not AccountsTableModel model || AccountsTableFocus.FocusRow(model, place.UserId, place.IsTotal) is not { } row) return;

        var at = AccountsTableFocus.FocusColumn(_columns, place.Column, place.ColumnIndex);
        if (at < 0 || at >= AccountsGrid.Columns.Count) return;

        var column = AccountsGrid.Columns[at];
        AccountsGrid.CurrentCell = new DataGridCellInfo(row, column);
        AccountsGrid.ScrollIntoView(row, column);
        AccountsGrid.UpdateLayout();

        if (AccountsGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow container
            && column.GetCellContent(container)?.Parent is DataGridCell cell)
        {
            cell.Focus();
        }
        else
        {
            AccountsGrid.Focus();
        }
    }
}
