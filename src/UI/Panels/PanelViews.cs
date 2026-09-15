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
        _ => new LiveLeaderboardPanel(),
    };

    public static void Render(
        FrameworkElement view, PanelSettings settings, LiveBoard live, ScoreBookReader reader, IReadOnlyDictionary<long, string> names)
    {
        switch (view)
        {
            case StandingPanel panel: panel.Render(PanelModels.Standing(live, reader, settings)); break;
            case RacePanel panel: panel.Render(PanelModels.Race(live, reader, settings)); break;
            case MyAccountsPanel panel: panel.Render(PanelModels.MyAccounts(live, reader, settings)); break;
            case PromotionCheckPanel panel: panel.Render(PanelModels.PromotionCheck(live, settings)); break;
            case AccountCardPanel panel: panel.Render(PanelModels.AccountCard(live, reader, settings)); break;
            case PastPeriodsPanel panel: panel.Render(PanelModels.PastPeriods(live, reader, settings)); break;
            case RecordsPanel panel: panel.Render(PanelModels.RecordsPanel(live, reader, settings)); break;
            case TopPanel panel: panel.Render(PanelModels.Top(live, settings)); break;
            case ProfileStatPanel panel: panel.Render(PanelModels.ProfileStat(live, reader, settings)); break;
            case LiveLeaderboardPanel panel: panel.Render(PanelModels.LiveLeaderboard(live, settings, names)); break;
        }
    }
}
