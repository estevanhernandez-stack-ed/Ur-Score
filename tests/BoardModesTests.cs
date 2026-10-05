using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Fetch;
using Labs626.UrScore.Games;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

/// <summary>
/// The board and the modes (games-and-modes item 10): a panel of an off mode says so and turns it back on, an orphan's panel
/// says it belongs to no mode, the gallery leaves out what only an off mode could feed, and the empty states speak in modes.
/// </summary>
[Collection(WpfCollection.Name)]
public class BoardModesTests
{
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static readonly Dictionary<string, RecipeSnapshot> NoReads = new();

    private static LiveBoard BattleOff(params Source[] sources) =>
        Live(sources, [Installed(Clan, "value"), Installed(TopList), Installed(Profile, "diamonds")], NoReads) with
        {
            Offs = new Dictionary<string, ReaderOff>
            {
                [Clan.Slug] = new("pet-sim-99/battle", "Battle"),
                [TopList.Slug] = new("pet-sim-99/battle", "Battle"),
            },
        };

    [Fact]
    public void APanelOfAnOffModeSaysSoAndCarriesTheSwitchThatTurnsItOn()
    {
        var live = BattleOff(MainClan, AltClan);
        var reader = Reader();
        var settings = new PanelSettings(Clan.Slug, SourceId: MainClan.Id);

        PanelHead[] heads =
        [
            PanelModels.Standing(live, reader, settings).Head,
            PanelModels.Race(live, reader, new PanelSettings(Clan.Slug, SourceIds: [MainClan.Id, AltClan.Id])).Head,
            PanelModels.PastPeriods(live, reader, settings).Head,
            PanelModels.RecordsPanel(live, reader, new PanelSettings(Clan.Slug, Stat: "value")).Head,
            PacePanel.Of(live, reader, settings).Head,
        ];

        Assert.All(heads, head =>
        {
            Assert.Equal("Battle is off.", head.Stale);
            Assert.Equal("pet-sim-99/battle", head.TurnOnMode);
            Assert.True(head.HasTurnOn);
        });
    }

    /// <summary>A panel saved with only a source (no recipe) is found through the source's recipe.</summary>
    [Fact]
    public void ThePanelsReaderIsTheSourcesWhenTheSettingsNameNone()
    {
        var head = PanelModels.Standing(BattleOff(MainClan), Reader(), new PanelSettings("", SourceId: MainClan.Id)).Head;

        Assert.Equal(("Battle is off.", "pet-sim-99/battle"), (head.Stale, head.TurnOnMode));
    }

    [Fact]
    public void APanelOfAnOrphanReaderSaysItIsPartOfNoModeAndOffersNoButton()
    {
        var orphan = SourceOf("s-0000000b", Clan, "CCGP", SourceRole.Main);
        var live = Live([orphan], [], NoReads) with { Offs = new Dictionary<string, ReaderOff> { [Clan.Slug] = new(null) } };

        var head = PanelModels.Standing(live, Reader(), new PanelSettings(Clan.Slug, SourceId: orphan.Id)).Head;

        Assert.Equal("Not part of any mode.", head.Stale);
        Assert.Null(head.TurnOnMode);
        Assert.False(head.HasTurnOn);
    }

    [Fact]
    public void APanelOfAnOnModeIsUntouchedByOffsThatNameOtherReaders()
    {
        var live = BattleOff(MainClan, ProfileSource);

        var head = PanelModels.ProfileStat(live, Reader(), new PanelSettings(Profile.Slug, SourceId: ProfileSource.Id, Stat: "diamonds")).Head;

        Assert.DoesNotContain("is off", head.Stale ?? "", StringComparison.Ordinal);
        Assert.Null(head.TurnOnMode);
    }

    /// <summary>The frame draws the note and the Turn on button (TurnOnModeButton); an orphan's note has no button.</summary>
    [Fact]
    public void TheFrameShowsTurnOnForAnOffModeAndNothingForAnOrphan()
    {
        var off = BattleOff(MainClan);
        var orphanLive = Live([MainClan], [], NoReads) with { Offs = new Dictionary<string, ReaderOff> { [Clan.Slug] = new(null) } };
        var settings = new PanelSettings(Clan.Slug, SourceId: MainClan.Id);
        (Visibility Button, string? Stale, string? Id) offFrame = default, orphanFrame = default;

        UiThread.RunInApp(() =>
        {
            (Visibility, string?, string?) Draw(LiveBoard live)
            {
                var view = PanelViews.Create(PanelType.Standing);
                PanelViews.Render(view, settings, live, Reader(), new Dictionary<long, string>(), new PanelSession());
                // Bindings resolve with layout, so the frame is measured before it is asked what it shows.
                view.Measure(new Size(600, 600));
                view.Arrange(new Rect(0, 0, 600, 600));
                view.UpdateLayout();
                var frame = PanelFrame.Of(view)!;
                var button = (Button)frame.FindName("TurnOnModeButton");
                return (button.Visibility, (frame.FindName("PanelStale") as TextBlock)?.Text, AutomationProperties.GetAutomationId(button));
            }

            offFrame = Draw(off);
            orphanFrame = Draw(orphanLive);
        });

        Assert.Equal((Visibility.Visible, "Battle is off.", "TurnOnModeButton"), offFrame);
        Assert.Equal(Visibility.Collapsed, orphanFrame.Button);
        Assert.Equal("Not part of any mode.", orphanFrame.Stale);
    }

    /// <summary>
    /// The panel's Turn on and the empty state's Turn on both call <c>SetSwitch</c> with the mode's key: through the composed
    /// app, off says so on the panel and on the starter, and on gives both back.
    /// </summary>
    [Fact]
    public void TurningTheModeOnFromThePanelOrTheEmptyStateBringsItBack()
    {
        using var dir = TempDir.Create("urscore-board-modes");
        using var services = new AppServices(
            Dispatcher.CurrentDispatcher, new AppPaths(dir.Path), new StubHost(reachable: false), new NullTransport(),
            new ManualTime(new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero)), Path.Combine(dir.Path, "metric-rules.json"));
        var profile = services.Sources.First(s => s.Recipe == Profile.Slug);
        var settings = new PanelSettings(Profile.Slug, SourceId: profile.Id, Stat: "diamonds");
        PanelHead Head() => PanelModels.ProfileStat(services.CurrentBoard(), services.Reader, settings).Head;

        Assert.Null(Head().TurnOnMode);

        services.SetSwitch("pet-sim-99/profile", false);
        var off = Head();
        Assert.Equal(("Profile is off.", "pet-sim-99/profile"), (off.Stale, off.TurnOnMode));
        Assert.Equal(BoardEmpty.ModeOff, StarterBoards.Named(StarterBoards.All(services.Installed, services.Sources, services.OffModeName), "alts")!.Empty);

        services.SetSwitch(off.TurnOnMode!, true);

        Assert.True(services.Switches.IsOn("pet-sim-99/profile"));
        Assert.Null(Head().TurnOnMode);
        Assert.Null(services.OffModeName("alts"));
    }

    private sealed class NullTransport : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken) =>
            Task.FromResult(new FetchResult(404, "{}", null));
    }

    [Fact]
    public void TheGalleryLeavesOutTypesOnlyAnOffModeCouldFeed()
    {
        var cards = PanelGallery.Cards(BattleOff(MainClan, AltClan, ProfileSource));

        // Battle's clan and clans-list readers feed these; with Battle off nothing that is on can.
        PanelType[] battleOnly = [PanelType.Standing, PanelType.Race, PanelType.PromotionCheck, PanelType.PastPeriods, PanelType.Pace, PanelType.Top];
        Assert.All(battleOnly, type => Assert.DoesNotContain(cards, c => c.Type == type));
        // Profile is on, so what it feeds stays.
        Assert.Contains(cards, c => c.Type == PanelType.ProfileStat && c.CanAdd);
        Assert.Contains(cards, c => c.Type == PanelType.AccountsTable && c.CanAdd);
    }

    [Fact]
    public void TheGalleryKeepsATypeAnOnReaderCanStillFeedAndOrphansOmitNothing()
    {
        // Records is fed by the profile reader too, which is on.
        Assert.Contains(PanelGallery.Cards(BattleOff(MainClan, ProfileSource)), c => c.Type == PanelType.Records);

        var orphans = Live([MainClan], [Installed(Clan, "value")], NoReads) with { Offs = new Dictionary<string, ReaderOff> { [Clan.Slug] = new(null) } };
        Assert.Equal(PanelGallery.Order.Count, PanelGallery.Cards(orphans).Count);
        Assert.Equal(PanelGallery.Order.Count, PanelGallery.Cards(Live([MainClan], [Installed(Clan, "value")], NoReads)).Count);
    }

    [Fact]
    public void FormChoicesNameAReaderByItsModeNotItsRecipe()
    {
        var live = Live([MainClan, ProfileSource], [Installed(Clan, "value"), Installed(Profile, "diamonds")], NoReads) with
        {
            Labels = new Dictionary<string, string> { [Clan.Slug] = "Battle", [Profile.Slug] = "Profile" },
        };

        var labels = PanelForms.StatChoices(PanelType.MyAccounts, live, new FormValues(), null).Select(c => c.Label).ToList();

        Assert.Contains("Points · Battle", labels);
        Assert.Contains("Diamonds · Profile", labels);
        Assert.DoesNotContain(labels, l => l.Contains("Pet Sim 99", StringComparison.Ordinal));
    }

    [Fact]
    public void ACannotReadLineNamesTheModeWhenTheCallerHasLabels()
    {
        var installed = new[] { Installed(Profile, "diamonds") };
        var latest = new Dictionary<string, RecipeSnapshot>
        {
            [ProfileSource.Id] = Snapshot(ProfileSource.Id, []) with { Unavailable = new Dictionary<long, string> { [AltOne.RobloxUserId] = "Link this account first." } },
        };

        Assert.Equal("Profile: Link this account first.",
            PanelText.CannotRead(AltOne.RobloxUserId, installed, [ProfileSource], latest, nameTheRecipe: true,
                labels: new Dictionary<string, string> { [Profile.Slug] = "Profile" }));
    }

    [Fact]
    public void TheEmptyStatesAskForAModeAndAClanInModeWords()
    {
        var battle = GameCatalog.BuiltIn.ModeOf(Clan.Slug)!;
        var noSources = StarterBoards.Build([Installed(Clan, "value")], []);

        Assert.Equal((BoardEmpty.NoSources, Clan.Slug), (noSources.Empty, noSources.RecipeSlug));
        Assert.Equal(("Pick your clan", "Choose your clan and the Battle board fills on the next read.", "Pick clan"),
            BoardText.EmptyState(noSources.Empty, Clan, modeName: battle.Name));
        Assert.Equal(("Turn on a mode", "Pick what Ur Score shows for your game.", "Open setup"),
            BoardText.EmptyState(BoardEmpty.NoModes, null));
    }

    /// <summary>The one helper item 11 repoints: today a clan pick goes to that reader's clans page, anything else to Recipes.</summary>
    [Fact]
    public void TheGamePageHelperNamesTodaysBestTarget()
    {
        Assert.Equal(SetupPages.ClansId(Clan.Slug), SetupPages.GamePage("pet-sim-99", Clan.Slug));
        Assert.Equal(SetupPages.Recipes, SetupPages.GamePage("pet-sim-99"));
    }
}
