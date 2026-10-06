using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

/// <summary>
/// Name your clan once (0.7.0), through the real composition and the real section: the main clan's members list places your
/// accounts, the question asks about the rest only, another clan places more, That's all keeps the question away until RoRoRo
/// lists a new account, and not one of the other members' ids reaches a file or the trail.
/// </summary>
[Collection(WpfCollection.Name)]
public class ClansSectionPlacementTests
{
    private const string ClanSlug = "pet-sim-99-clan-battle-points";

    private static readonly HostAccount Birch = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "BirchMain");
    private static readonly HostAccount Ash = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "AshAlt");
    private static readonly HostAccount Cedar = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 301, "CedarThird");
    private static readonly HostAccount Dune = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), 401, "DuneFourth");

    /// <summary>Seventy strangers per clan, ids long and distinctive enough that finding one in a file can't be a coincidence.</summary>
    private static IEnumerable<long> Strangers(int clan) => Enumerable.Range(0, 70).Select(i => 987654321000L + clan * 1000 + i);

    [Fact]
    public void TheMainClanPlacesYourAccountsAndAnotherClanPlacesTheRest() => UiThread.RunInApp(() =>
    {
        using var dir = TempDir.Create("urscore-placement");
        var transport = new Rosters();
        using var services = Services(dir.Path, transport, Birch, Ash, Cedar);
        var section = new ClansSection(services, ClanSlug);

        // Birch owns K0i2 and is not in its Members list (probed live): the owner counts all the same.
        Wait(section.PickAsync("K0i2", SourceRole.Main));
        Assert.Equal("Found 2 of your 3 accounts in K0i2: BirchMain, AshAlt.", section.MainFoundLine.Text);
        Assert.Equal("1 of your accounts isn't in K0i2 yet. Is it in another clan?", section.RemainingAccountsLine.Text);
        Assert.Equal(Visibility.Visible, section.OtherClanSearch.Visibility);
        Assert.Equal(Visibility.Visible, section.ThatsAllButton.Visibility);

        // A clan with none of the rest says so and offers to watch it instead, and the question stays.
        Wait(section.PickOtherAsync("NovaForge"));
        Assert.Equal("None of the rest are in NovaForge. You can still watch it.", section.PlaceAccountsLine.Text);
        Assert.Equal(Visibility.Visible, section.OtherWatchInsteadButton.Visibility);
        Assert.StartsWith("Watch ", (string)section.OtherWatchInsteadButton.Content);
        Assert.NotEqual("Watch it instead", (string)section.OtherWatchInsteadButton.Content);
        Assert.Equal("1 of your accounts isn't in K0i2 or NovaForge yet. Is it in another clan?", section.RemainingAccountsLine.Text);

        // For eyes, as the game page is (artifacts/smoke, ignored by git): the question with its search, Watch and That's all.
        var window = GamePageRenderTests.Show(section);
        try
        {
            GamePageRenderTests.Snap(section, "clan-placement.png");
        }
        finally
        {
            window.Close();
        }

        Wait(section.PickOtherAsync("K0i3"));
        Assert.Equal("K0i3 has 1 of them: CedarThird.", section.PlaceAccountsLine.Text);
        Assert.Equal(Visibility.Collapsed, section.RemainingAccountsLine.Visibility);
        Assert.Equal(Visibility.Collapsed, section.OtherClanSearch.Visibility);
        Assert.Equal(Visibility.Collapsed, section.ThatsAllButton.Visibility);

        // Added as one of yours, never main; and with every account placed, the question is settled for the next Setup.
        var k0i3 = Assert.Single(services.Sources, s => s.Recipe == ClanSlug && s.Inputs["clan"] == "K0i3");
        Assert.Equal(SourceRole.Mine, k0i3.Role);
        Assert.Equal(SourceRole.Main, Assert.Single(services.Sources, s => s.Recipe == ClanSlug && s.Inputs["clan"] == "K0i2").Role);
        Assert.Equal(new[] { Birch, Ash, Cedar }.Select(a => a.AccountId).Order(), State(services).Settled.Order());
    });

    /// <summary>
    /// "That's all" is kept in the reader's state: the next Setup asks nothing and reads no members list. An account RoRoRo
    /// lists afterwards is not settled, so the next Setup reads the clans again and asks about that account alone.
    /// </summary>
    [Fact]
    public void ThatsAllIsKeptAndANewAccountBringsTheQuestionBack() => UiThread.RunInApp(() =>
    {
        using var dir = TempDir.Create("urscore-thats-all");
        var transport = new Rosters();

        using (var first = Services(dir.Path, transport, Birch, Ash, Cedar))
        {
            var section = new ClansSection(first, ClanSlug);
            Wait(section.PickAsync("K0i2", SourceRole.Main));
            Assert.Equal(Visibility.Visible, section.ThatsAllButton.Visibility);

            section.ThatsAllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Collapsed, section.RemainingAccountsLine.Visibility);
            Assert.Equal("Ur Score won't ask about them again unless RoRoRo lists another account.", section.PlaceAccountsLine.Text);
        }

        Assert.Contains("\"settledAccountIds\"", File.ReadAllText(Path.Combine(new AppPaths(dir.Path).Recipes, ClanSlug + ".state.json")));

        using (var again = Services(dir.Path, transport, Birch, Ash, Cedar))
        {
            var reads = transport.MemberReads;
            var section = new ClansSection(again, ClanSlug);
            section.Activate();
            Wait(section.Placement);

            Assert.Equal(reads, transport.MemberReads);
            Assert.Equal(Visibility.Collapsed, section.RemainingAccountsLine.Visibility);
        }

        using (var later = Services(dir.Path, transport, Birch, Ash, Cedar, Dune))
        {
            var section = new ClansSection(later, ClanSlug);
            section.Activate();
            Wait(section.Placement);

            Assert.Equal("1 of your accounts isn't in K0i2 yet. Is it in another clan?", section.RemainingAccountsLine.Text);
            Assert.Equal(Visibility.Visible, section.ThatsAllButton.Visibility);
        }
    });

    /// <summary>
    /// README "What leaves your machine": a members list is every member's id, and only yours may outlive the read. The whole
    /// flow runs against 70 strangers a clan, then every file in the data folder and every trail line is searched for them.
    /// </summary>
    [Fact]
    public void NoOtherMembersIdIsWrittenAnywhereOrTrailed() => UiThread.RunInApp(() =>
    {
        using var dir = TempDir.Create("urscore-placement-privacy");
        var transport = new Rosters();
        using var services = Services(dir.Path, transport, Birch, Ash, Cedar, Dune);
        var section = new ClansSection(services, ClanSlug);

        Wait(section.PickAsync("K0i2", SourceRole.Main));
        Wait(section.PickOtherAsync("K0i3"));
        section.ThatsAllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        section.Activate();
        Wait(section.Placement);
        Assert.True(transport.MemberReads >= 2, "the flow never read a members list");

        var strangers = Strangers(1).Concat(Strangers(2)).Concat(Strangers(3)).Select(id => id.ToString()).ToList();
        var files = Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.True(strangers.All(id => !text.Contains(id, StringComparison.Ordinal)), $"{Path.GetRelativePath(dir.Path, file)} holds another member's id");
        }

        Assert.Contains(services.Trail, line => line.Contains("MEMBERS READ", StringComparison.Ordinal));
        Assert.True(services.Trail.All(line => strangers.All(id => !line.Contains(id, StringComparison.Ordinal))), "the trail holds another member's id");
    });

    private static AppServices Services(string folder, IRecipeTransport transport, params HostAccount[] accounts) =>
        new(Dispatcher.CurrentDispatcher, new AppPaths(folder), new StubHost(reachable: true, accounts), transport,
            new ManualTime(new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero)), Path.Combine(folder, "metric-rules.json"));

    private static RecipeState State(AppServices services) => services.Installed.Single(i => i.Recipe.Slug == ClanSlug).State;

    /// <summary>Runs the window's own queue until <paramref name="task"/> is done, then rethrows what it threw.</summary>
    private static void Wait(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// K0i2: owned by Birch, Ash a member. K0i3: Cedar a member. NovaForge: none of yours. Each with 70 strangers; no battle
    /// running, so the battle read is idle and only the members lists can place anyone.
    /// </summary>
    private sealed class Rosters : IRecipeTransport
    {
        private int _memberReads;

        public int MemberReads => _memberReads;

        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var address = url.AbsoluteUri;
            if (label == MemberLists.Label) Interlocked.Increment(ref _memberReads);

            string? body = address switch
            {
                _ when address.EndsWith("/api/clansList", StringComparison.Ordinal) => """{"data":["K0i2","K0i3","NovaForge"]}""",
                _ when address.EndsWith("/api/activeClanBattle", StringComparison.Ordinal) => """{"data":null}""",
                _ when address.EndsWith("/api/clan/K0i2", StringComparison.Ordinal) => Roster(1, owner: Birch.RobloxUserId, Ash.RobloxUserId),
                _ when address.EndsWith("/api/clan/K0i3", StringComparison.Ordinal) => Roster(2, owner: Strangers(2).First(), Cedar.RobloxUserId),
                _ when address.EndsWith("/api/clan/NovaForge", StringComparison.Ordinal) => Roster(3, owner: Strangers(3).First()),
                _ => null,
            };

            return Task.FromResult(body is null ? new FetchResult(404, "{}", null) : new FetchResult(200, body, null));
        }

        private static string Roster(int clan, long owner, params long[] yours)
        {
            var rows = Strangers(clan).Concat(yours).Select(id => "{\"UserID\":" + id + ",\"PermissionLevel\":10,\"JoinTime\":1758000000}");
            return "{\"data\":{\"Owner\":" + owner + ",\"Members\":[" + string.Join(",", rows) + "],\"Battles\":{}}}";
        }
    }
}
