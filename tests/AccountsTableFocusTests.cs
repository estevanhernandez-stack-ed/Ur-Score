using Labs626.UrScore.Board;

namespace UrScore.Tests;

public class AccountsTableFocusTests
{
    private static readonly AccountColumn Name = new(AccountSort.NameKey, "Account", AccountColumnKind.Name);

    private static AccountRow Row(long id, string name, bool picked = false) => new(id, name, [name, "1"], "", false, picked);

    private static readonly AccountRow Total = new(0, "Total", ["Total", "2"], "", false, false, IsTotal: true);

    private static AccountsTableModel Model(params AccountRow[] rows) => new(new PanelHead("Accounts table"), [Name], rows);

    [Fact]
    public void OnlyARedrawAnsweringTheTablesOwnSortOrPickRestoresFocus()
    {
        Assert.True(AccountsTableFocus.RestoresFocus(answersTable: true, focusInTable: true));

        // A data refresh never moves keyboard focus, so it never scrolls the board back to the table.
        Assert.False(AccountsTableFocus.RestoresFocus(answersTable: false, focusInTable: true));

        // A heading clicked while focus was elsewhere doesn't take it.
        Assert.False(AccountsTableFocus.RestoresFocus(answersTable: true, focusInTable: false));
        Assert.False(AccountsTableFocus.RestoresFocus(answersTable: false, focusInTable: false));
    }

    [Fact]
    public void TheRedrawSelectsThePickedRow()
    {
        var model = Model(Row(11, "Alpha"), Row(22, "Bravo", picked: true), Total);

        Assert.Equal(22, AccountsTableFocus.SelectedRow(model, totalSelected: false)?.UserId);
    }

    [Fact]
    public void TheTotalsRowStaysSelectedWhenTheKeyboardWasOnIt()
    {
        var model = Model(Row(11, "Alpha"), Row(22, "Bravo", picked: true), Total);

        Assert.True(AccountsTableFocus.SelectedRow(model, totalSelected: true)?.IsTotal);
    }

    [Fact]
    public void NothingIsSelectedWithNoPick()
    {
        Assert.Null(AccountsTableFocus.SelectedRow(Model(Row(11, "Alpha"), Total), totalSelected: false));
    }

    [Fact]
    public void FocusGoesBackToTheSameAccountWhereverItSortedTo()
    {
        var model = Model(Row(22, "Bravo", picked: true), Row(11, "Alpha"), Total);

        Assert.Equal(11, AccountsTableFocus.FocusRow(model, userId: 11, onTotal: false)?.UserId);
        Assert.True(AccountsTableFocus.FocusRow(model, userId: 0, onTotal: true)?.IsTotal);
    }

    [Fact]
    public void FocusFallsBackToThePickedRowThenTheFirst()
    {
        Assert.Equal(22, AccountsTableFocus.FocusRow(Model(Row(11, "Alpha"), Row(22, "Bravo", picked: true), Total), userId: 33, onTotal: false)?.UserId);
        Assert.Equal(11, AccountsTableFocus.FocusRow(Model(Row(11, "Alpha"), Row(22, "Bravo")), userId: 33, onTotal: false)?.UserId);

        // A table without a totals row any more (no stat shown) still lands on a row.
        Assert.Equal(11, AccountsTableFocus.FocusRow(Model(Row(11, "Alpha")), userId: 0, onTotal: true)?.UserId);
        Assert.Null(AccountsTableFocus.FocusRow(Model(), userId: 11, onTotal: false));
    }

    [Fact]
    public void FocusStaysOnTheSameColumnByKeyAfterASortMovesTheChangeColumns()
    {
        // Sorted by diamonds, the Eggs column is fourth; sorted by eggs, Today and 7 days follow Eggs and it is second.
        AccountColumn[] before =
        [
            Name,
            new("d", "Diamonds", AccountColumnKind.Stat, Sorted: true, Descending: true),
            new("d", "Today", AccountColumnKind.Today),
            new("d", "7 days", AccountColumnKind.Week),
            new("e", "Eggs", AccountColumnKind.Stat),
        ];
        AccountColumn[] after =
        [
            Name,
            new("d", "Diamonds", AccountColumnKind.Stat),
            new("e", "Eggs", AccountColumnKind.Stat, Sorted: true, Descending: true),
            new("e", "Today", AccountColumnKind.Today),
            new("e", "7 days", AccountColumnKind.Week),
        ];

        Assert.Equal(2, AccountsTableFocus.FocusColumn(after, before[4], 4));
        Assert.Equal(1, AccountsTableFocus.FocusColumn(after, before[1], 1));
        Assert.Equal(0, AccountsTableFocus.FocusColumn(after, before[0], 0));

        // A change column follows the sort: Today stays Today.
        Assert.Equal(3, AccountsTableFocus.FocusColumn(after, before[2], 2));
    }

    [Fact]
    public void AColumnThatIsGoneFallsBackToItsPlace()
    {
        AccountColumn[] after = [Name, new("d", "Diamonds", AccountColumnKind.Stat)];

        Assert.Equal(1, AccountsTableFocus.FocusColumn(after, new AccountColumn("e", "Eggs", AccountColumnKind.Stat), 1));
        Assert.Equal(1, AccountsTableFocus.FocusColumn(after, new AccountColumn("x", "Gone", AccountColumnKind.Stat), 4));
        Assert.Equal(0, AccountsTableFocus.FocusColumn(after, null, 0));
        Assert.Equal(-1, AccountsTableFocus.FocusColumn([], null, 0));
    }
}
