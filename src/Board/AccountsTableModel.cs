namespace Labs626.UrScore.Board;

/// <summary>What a column of the accounts table holds: the account's name, a stat, or the sorted stat's change today or over 7 days.</summary>
public enum AccountColumnKind { Name, Stat, Today, Week }

/// <summary>One column: its key (a stat key, or <see cref="AccountSort.NameKey"/>), its heading, and whether rows are sorted by it.</summary>
public sealed record AccountColumn(string Key, string Header, AccountColumnKind Kind, bool Sorted = false, bool Descending = false)
{
    /// <summary>The change columns follow the sort; only the name and the stats take a click.</summary>
    public bool CanSort => Kind is AccountColumnKind.Name or AccountColumnKind.Stat;

    /// <summary>The heading as drawn: a sorted column ends in an arrow.</summary>
    public string Heading => Sorted ? $"{Header} {(Descending ? '↓' : '↑')}" : Header;
}

/// <summary>How one table is sorted this session (D15): a stat key or <see cref="NameKey"/>. Never saved.</summary>
public sealed record AccountSort(string Key, bool Descending)
{
    public const string NameKey = "";

    /// <summary>What a click on a heading does: the sorted column flips; another stat sorts highest first; the name A to Z.</summary>
    public static AccountSort Clicked(AccountColumn column) =>
        new(column.Key, column.Sorted ? !column.Descending : column.Kind == AccountColumnKind.Stat);
}

/// <summary>One of your accounts, or the totals row (user id 0). Cells line up with the columns, the name first.</summary>
public sealed record AccountRow(
    long UserId, string Name, IReadOnlyList<string> Cells, string Note, bool Missing, bool Picked, bool IsTotal = false, string? Avatar = null)
{
    public bool HasNote => Note.Length > 0;

    /// <summary>UI Automation names a table row by this: your own account's name, or "Total".</summary>
    public override string ToString() => Name;
}

public sealed record AccountsTableModel(PanelHead Head, IReadOnlyList<AccountColumn> Columns, IReadOnlyList<AccountRow> Rows);

/// <summary>What a panel shows this session that is never saved: a table's sort, and the account picked on its board (D15, D18).</summary>
public sealed record PanelSession(AccountSort? Sort = null, long? PickedUserId = null);
