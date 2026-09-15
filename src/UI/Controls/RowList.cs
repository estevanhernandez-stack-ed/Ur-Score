using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Labs626.UrScore.UI;

/// <summary>
/// An <see cref="ItemsControl"/> whose rows' own controls reach UI Automation. The stock peer wraps each
/// row in an item element named by the row record's ToString and keeps the row's buttons, ticks and
/// lines out of the control view, so Narrator and the smoke walks could not find "Remove K0i2".
/// Every list in src/UI is one of these (RowListFenceTests).
/// </summary>
public sealed class RowList : ItemsControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RowListAutomationPeer(this);
}

/// <summary>A list whose children come from the visual tree: each row's real controls, with their accessible names.</summary>
public sealed class RowListAutomationPeer(RowList owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(RowList);
}
