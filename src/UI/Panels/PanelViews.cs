using System.Windows;
using Labs626.UrScore.Board;
using Labs626.UrScore.Book;

namespace Labs626.UrScore.UI;

/// <summary>The one switch from a panel type to its control and its builder.</summary>
public static class PanelViews
{
    public static FrameworkElement Create(PanelType type) => type switch
    {
        PanelType.Standing => new StandingPanel(),
        PanelType.Race => new RacePanel(),
        PanelType.MyAccounts => new MyAccountsPanel(),
        PanelType.PromotionCheck => new PromotionCheckPanel(),
        PanelType.AccountCard => new AccountCardPanel(),
        PanelType.PastPeriods => new PastPeriodsPanel(),
        PanelType.Records => new RecordsPanel(),
        PanelType.Top => new TopPanel(),
        PanelType.ProfileStat => new ProfileStatPanel(),
        PanelType.AccountsTable => new AccountsTablePanel(),
        PanelType.Pace => new PacePanelView(),
        PanelType.LiveLeaderboard => new LiveLeaderboardPanel(),

        // Every type names its own view. The default used to BE the leaderboard, so a type added to the enum and
        // forgotten here drew a leaderboard on somebody's board with nothing anywhere saying why — a wrong panel
        // that looks like a working one (S1-13.13). An unknown type cannot arrive from disk (BoardsFile.TypeOf
        // rejects it with Enum.IsDefined), so the only way here is a new enum member, and that is a bug in this
        // file rather than anything a user did. `PanelViewsTests` walks every value so it fails in the suite
        // instead of on a board.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No view is built for this panel type."),
    };

    public static void Render(
        FrameworkElement view, PanelSettings settings, LiveBoard live, ScoreBookReader reader, IReadOnlyDictionary<long, string> names,
        PanelSession session)
    {
        switch (view)
        {
            case StandingPanel panel: panel.Render(PanelModels.Standing(live, reader, settings)); break;
            case RacePanel panel: panel.Render(PanelModels.Race(live, reader, settings)); break;
            case MyAccountsPanel panel: panel.Render(PanelModels.MyAccounts(live, reader, settings)); break;
            case PromotionCheckPanel panel: panel.Render(PanelModels.PromotionCheck(live, settings)); break;
            case AccountCardPanel panel: panel.Render(PanelModels.AccountCard(live, reader, settings, session.PickedUserId)); break;
            case PastPeriodsPanel panel: panel.Render(PanelModels.PastPeriods(live, reader, settings)); break;
            case RecordsPanel panel: panel.Render(PanelModels.RecordsPanel(live, reader, settings)); break;
            case TopPanel panel: panel.Render(PanelModels.Top(live, settings)); break;
            case ProfileStatPanel panel: panel.Render(PanelModels.ProfileStat(live, reader, settings)); break;
            case AccountsTablePanel panel: panel.Render(PanelModels.AccountsTable(live, reader, settings, session.Sort, session.PickedUserId)); break;
            case PacePanelView panel: panel.Render(PacePanel.Of(live, reader, settings)); break;
            case LiveLeaderboardPanel panel: panel.Render(PanelModels.LiveLeaderboard(live, settings, names)); break;
        }
    }
}
