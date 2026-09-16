using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Labs626.UrScore.UI;

/// <summary>
/// Where the board tab's menu opens. Rename…, Duplicate and Delete… are one menu, shared by the tab's right-click
/// and by the ⋯ button beside the tabs, so the two can never come to offer different commands (backlog V3-S.8).
/// The two ways in want it in different places: a right-click at the mouse, ⋯ under itself.
/// </summary>
public static class BoardMenu
{
    /// <summary>Points the menu just under ⋯, for the way in that isn't a right-click.</summary>
    public static void AimAt(ContextMenu menu, UIElement button)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
    }

    /// <summary>
    /// Hands the menu back to the mouse as it closes, so the next right-click opens where you clicked instead of
    /// under ⋯. True when it was ⋯ that opened this one, which is when focus belongs back on ⋯.
    /// </summary>
    public static bool Release(ContextMenu menu, UIElement button)
    {
        var wasTheButtons = ReferenceEquals(menu.PlacementTarget, button);
        menu.ClearValue(ContextMenu.PlacementTargetProperty);
        menu.ClearValue(ContextMenu.PlacementProperty);
        return wasTheButtons;
    }
}
