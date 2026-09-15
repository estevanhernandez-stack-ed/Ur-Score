using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// Everything that is setup (spec §7): a list of pages on the left, the page on the right. Pages re-render
/// whenever the services change, and the list follows installed recipes.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly ISetupServices _services;
    private string? _currentId;
    private bool _rebuilding;

    public SetupWindow(ISetupServices services, string? startPage = null)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += OnServicesChanged;
        Closed += (_, _) => _services.Changed -= OnServicesChanged;

        ShowPage(startPage ?? SetupPages.StartPage(services.Installed, services.Sources));
    }

    /// <summary>Shows a page by id, building it fresh.</summary>
    public void ShowPage(string pageId)
    {
        _currentId = null;
        RebuildNav(pageId);
    }

    private void RebuildNav(string? select)
    {
        var pages = SetupPages.For(_services.Installed);

        _rebuilding = true;
        try
        {
            SetupNav.ItemsSource = pages;
        }
        finally
        {
            _rebuilding = false;
        }

        SetupNav.SelectedItem = pages.FirstOrDefault(p => p.Id == select)
                                ?? pages.FirstOrDefault(p => p.Id == _currentId)
                                ?? pages[0];
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding || SetupNav.SelectedItem is not SetupPage page) return;

        if (page.Id == _currentId && PageHost.Content is ISetupPage current)
        {
            current.Refresh();
            return;
        }

        _currentId = page.Id;
        PageHost.Content = CreatePage(page);
    }

    private void OnServicesChanged()
    {
        var wanted = SetupPages.For(_services.Installed).Select(p => p.Id);
        var shown = (SetupNav.ItemsSource as IEnumerable<SetupPage>)?.Select(p => p.Id) ?? [];

        if (!wanted.SequenceEqual(shown))
        {
            RebuildNav(_currentId);
            return;
        }

        (PageHost.Content as ISetupPage)?.Refresh();
    }

    private FrameworkElement CreatePage(SetupPage page) => page.Id switch
    {
        _ when page.RecipeSlug is { } slug => new ClansPage(_services, slug),
        SetupPages.Accounts => new AccountsPage(_services),
        SetupPages.Stats => new StatsPage(_services),
        SetupPages.Recipes => new RecipesPage(_services, this),
        SetupPages.Alerts => new AlertsPage(_services),
        SetupPages.ScoreBook => new ScoreBookPage(_services),
        _ => new DiagnosticsPage(_services),
    };
}
