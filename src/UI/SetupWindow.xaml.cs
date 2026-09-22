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

    /// <summary>What the next page built for this id opens saying; taken by the next page built, whichever it is.</summary>
    private (string PageId, string Text)? _note;

    /// <param name="note">What <paramref name="startPage"/> opens saying, as <see cref="ShowPage"/> takes it.</param>
    public SetupWindow(ISetupServices services, string? startPage = null, string? note = null)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        _services = services;
        _services.Changed += OnServicesChanged;
        Closed += (_, _) => _services.Changed -= OnServicesChanged;

        ShowPage(startPage ?? SetupPages.StartPage(services.Installed, services.Sources), note);
    }

    /// <summary>Shows a page by id, building it fresh.</summary>
    /// <param name="note">
    /// What that page opens saying: an import that goes on to a Clans page carries its result there, because the Recipes page
    /// it said it on is replaced at once (backlog S1-12.4). Said once, on that page only; a later visit builds the page without it.
    /// </param>
    public void ShowPage(string pageId, string? note = null)
    {
        _currentId = null;
        _note = note is null ? null : (pageId, note);
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

    private FrameworkElement CreatePage(SetupPage page)
    {
        var note = _note is { } pending && pending.PageId == page.Id ? pending.Text : null;
        _note = null;

        // Every id SetupPages.For can name has its own arm, and an id it cannot is a bug and says so, rather than
        // showing Diagnostics for whatever page somebody adds to the list and forgets here (S1-12.9).
        return page.Id switch
        {
            _ when page.RecipeSlug is { } slug => new ClansPage(_services, slug, note),
            SetupPages.Accounts => new AccountsPage(_services),
            SetupPages.Stats => new StatsPage(_services),
            SetupPages.Recipes => new RecipesPage(_services, this),
            SetupPages.Alerts => new AlertsPage(_services),
            SetupPages.ScoreBook => new ScoreBookPage(_services),
            SetupPages.Diagnostics => new DiagnosticsPage(_services),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page.Id, "A Setup page with no page to build for it."),
        };
    }
}
