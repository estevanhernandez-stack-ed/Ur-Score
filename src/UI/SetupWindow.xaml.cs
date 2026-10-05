using System.Windows;
using System.Windows.Controls;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

/// <summary>
/// Everything that is setup (spec §7): a list of pages on the left, the page on the right. Pages re-render
/// whenever the services change. The list is a page per game, then the fixed pages.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly ISetupServices _services;
    private string? _currentId;
    private bool _rebuilding;

    /// <summary>The clan search the next page built for this id focuses; taken by the next page built, whichever it is.</summary>
    private (string PageId, string Slug)? _focus;

    /// <param name="focusSlug">
    /// The asking reader whose clan search <paramref name="startPage"/> focuses, as <see cref="ShowPage"/> takes it. With no start
    /// page, Setup opens where <see cref="SetupPages.StartPage"/> says, focused on first run's clan search when that is where (A11).
    /// </param>
    public SetupWindow(ISetupServices services, string? startPage = null, string? focusSlug = null)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += OnServicesChanged;
        Closed += (_, _) => _services.Changed -= OnServicesChanged;

        if (startPage is null
            && SetupPages.FirstRun(services.Catalog, services.Switches, services.Installed, services.Sources) is { } first)
        {
            startPage = SetupPages.GamePage(first.GameId);
            focusSlug = first.AskingSlug;
        }

        ShowPage(startPage ?? SetupPages.StartPage(services.Catalog, services.Switches, services.Installed, services.Sources), focusSlug);
    }

    /// <summary>Shows a page by id, building it fresh.</summary>
    /// <param name="focusSlug">
    /// On a game page, the asking reader whose clan search takes the keyboard: first run, or the board's Pick your clan (A11).
    /// Once, on that page only; a later visit builds the page without it.
    /// </param>
    public void ShowPage(string pageId, string? focusSlug = null)
    {
        _currentId = null;
        _focus = focusSlug is null ? null : (pageId, focusSlug);
        RebuildNav(pageId);
    }

    private void RebuildNav(string? select)
    {
        var pages = SetupPages.For(_services.Catalog);

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
        var wanted = SetupPages.For(_services.Catalog).Select(p => p.Id);
        var shown = (SetupNav.ItemsSource as IEnumerable<SetupPage>)?.Select(p => p.Id) ?? [];

        if (!wanted.SequenceEqual(shown))
        {
            RebuildNav(_currentId);
            return;
        }

        (PageHost.Content as ISetupPage)?.Refresh();
    }

    private FrameworkElement CreatePage(SetupPage page)
    {
        var focus = _focus is { } pending && pending.PageId == page.Id ? pending.Slug : null;
        _focus = null;

        // Every id SetupPages.For can name has its own arm, and an id it cannot is a bug and says so, rather than
        // showing Diagnostics for whatever page somebody adds to the list and forgets here (S1-12.9).
        return page.Id switch
        {
            _ when page.GameId is { } game => new GamePage(_services, game, focus),
            SetupPages.Accounts => new AccountsPage(_services),
            SetupPages.Stats => new StatsPage(_services),
            SetupPages.Alerts => new AlertsPage(_services),
            SetupPages.ScoreBook => new ScoreBookPage(_services),
            SetupPages.Diagnostics => new DiagnosticsPage(_services),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page.Id, "A Setup page with no page to build for it."),
        };
    }
}
