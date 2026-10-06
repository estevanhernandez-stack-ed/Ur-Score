using System.IO;
using System.Windows.Threading;
using Labs626.UrScore.Board;
using Labs626.UrScore.Composition;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

/// <summary>
/// Backlog V3-S.20 through the real composition: AppServices reads each clan's members list for your own accounts, at start and
/// then at most once per half hour per clan, keeps the result in membership.json so the board groups by it on open, skips off
/// modes and an empty account list, and never keeps another member's id.
/// </summary>
public class MembershipCompositionTests
{
    private const string ClanSlug = "pet-sim-99-clan-battle-points";

    private const string Battle = "pet-sim-99/battle";

    private static readonly DateTimeOffset Start = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private static readonly HostAccount Birch = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 101, "BirchMain");
    private static readonly HostAccount Ash = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 201, "AshAlt");
    private static readonly HostAccount Cedar = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 301, "CedarThird");

    private static readonly Source Ccgp = new("s-00000001", ClanSlug, new Dictionary<string, string> { ["clan"] = "CCGP" }, SourceRole.Main);

    private static readonly Source K0i2 = new("s-00000002", ClanSlug, new Dictionary<string, string> { ["clan"] = "K0i2" }, SourceRole.Mine);

    private static IEnumerable<long> Strangers(int clan) => Enumerable.Range(0, 70).Select(i => 987654321000L + clan * 1000 + i);

    /// <summary>Two clans' lists are read, your accounts kept per clan, and the file holds your ids alone.</summary>
    [Fact]
    public async Task ARefreshReadsEachClansListAndKeepsYourAccountsOnly()
    {
        using var dir = TempDir.Create("urscore-membership-fill");
        var transport = new Rosters();
        using var services = Compose(dir.Path, transport, [Ccgp, K0i2], Birch, Ash, Cedar);

        await services.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);

        Assert.Equal(2, transport.MemberReads);
        Assert.Equal(new long[] { 101, 201 }, services.Members[Ccgp.Id].Order());
        Assert.Equal(new long[] { 301 }, services.Members[K0i2.Id].ToArray());
        Assert.Equal(services.Members.Keys.Order(), services.CurrentBoard().Members!.Keys.Order());

        var file = File.ReadAllText(new AppPaths(dir.Path).Membership);
        Assert.Contains("101", file, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", file, StringComparison.Ordinal);
        Assert.True(services.Trail.All(line => !line.Contains("987654321", StringComparison.Ordinal)), "the trail holds another member's id");
    }

    /// <summary>The next start's board groups by what was kept, before it reads anything.</summary>
    [Fact]
    public async Task TheNextStartGroupsByTheKeptListsBeforeAnyRead()
    {
        using var dir = TempDir.Create("urscore-membership-kept");
        using (var first = Compose(dir.Path, new Rosters(), [Ccgp], Birch, Ash, Cedar))
        {
            await first.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);
        }

        var transport = new Rosters();
        using var again = Compose(dir.Path, transport, [Ccgp], Birch, Ash, Cedar);

        Assert.Equal(0, transport.MemberReads);
        var board = again.CurrentBoard();
        var model = PanelModels.MyAccounts(board, again.Reader, new PanelSettings(ClanSlug, Stat: "value"));
        Assert.Equal(new[] { "BirchMain", "AshAlt" }, Assert.Single(model.Groups, g => g.Heading == "★ CCGP").Rows.Select(r => r.Name));
    }

    /// <summary>One read per clan per half hour, a failed try counting as a read, so a broken list isn't asked on every battle read.</summary>
    [Fact]
    public async Task ADueRefreshReadsAClanAtMostOncePerHalfHour()
    {
        using var dir = TempDir.Create("urscore-membership-due");
        var transport = new Rosters();
        var time = new ManualTime(Start);
        using var services = Compose(dir.Path, transport, [Ccgp], time, Birch, Ash);

        await services.RefreshMembershipAsync(onlyDue: true, CancellationToken.None);
        await services.RefreshMembershipAsync(onlyDue: true, CancellationToken.None);
        Assert.Equal(1, transport.MemberReads);

        time.Advance(TimeSpan.FromMinutes(29));
        await services.RefreshMembershipAsync(onlyDue: true, CancellationToken.None);
        Assert.Equal(1, transport.MemberReads);

        time.Advance(TimeSpan.FromMinutes(1));
        transport.Broken = true;
        await services.RefreshMembershipAsync(onlyDue: true, CancellationToken.None);
        Assert.Equal(2, transport.MemberReads);

        // The failed read keeps the last good list, and waits its half hour like a read.
        Assert.Equal(new long[] { 101, 201 }, services.Members[Ccgp.Id].Order());
        time.Advance(TimeSpan.FromMinutes(10));
        await services.RefreshMembershipAsync(onlyDue: true, CancellationToken.None);
        Assert.Equal(2, transport.MemberReads);
    }

    /// <summary>An off mode reads nothing (A3), and with no accounts listed there is nothing to look for, so nothing is asked.</summary>
    [Fact]
    public async Task AnOffModeOrNoAccountsReadsNoList()
    {
        using var dir = TempDir.Create("urscore-membership-off");
        var transport = new Rosters();
        using (var services = Compose(dir.Path, transport, [Ccgp], Birch))
        {
            services.SetSwitch(Battle, false);
            await services.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);
            Assert.Equal(0, transport.MemberReads);
        }

        // A folder of its own: the first one's accounts.json would stand in for RoRoRo's list, as it does in the app.
        using var other = TempDir.Create("urscore-membership-none");
        using var none = Compose(other.Path, transport, [Ccgp]);
        none.SetSwitch(Battle, true);
        await none.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);
        Assert.Equal(0, transport.MemberReads);
        Assert.Empty(none.Members);
    }

    /// <summary>Setup's pick reads the list through the same path, so the clan's row and the board take it at once.</summary>
    [Fact]
    public async Task ASetupPickKeepsWhatItFoundForThatClan()
    {
        using var dir = TempDir.Create("urscore-membership-pick");
        var transport = new Rosters();
        using var services = Compose(dir.Path, transport, [Ccgp, K0i2], Birch, Ash, Cedar);

        var found = await services.FindOwnMembersAsync(BuiltInRecipes.BySlug[ClanSlug], " K0i2 ", CancellationToken.None);

        Assert.Equal(new long[] { 301 }, found!.Found.ToArray());
        Assert.Equal(new long[] { 301 }, services.Members[K0i2.Id].ToArray());
        Assert.False(services.Members.ContainsKey(Ccgp.Id));
    }

    /// <summary>Two refreshes at once (a battle read and Setup opening) share one GET per clan.</summary>
    [Fact]
    public async Task RefreshesAtTheSameTimeShareOneRead()
    {
        using var dir = TempDir.Create("urscore-membership-share");
        var transport = new Rosters { Hold = new TaskCompletionSource() };
        using var services = Compose(dir.Path, transport, [Ccgp], Birch, Ash);

        var one = services.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);
        var two = services.RefreshMembershipAsync(onlyDue: false, CancellationToken.None);
        transport.Hold.SetResult();
        await Task.WhenAll(one, two);

        Assert.Equal(1, transport.MemberReads);
    }

    /// <summary>At start, once RoRoRo has listed the accounts, every clan's list is read (not waiting for a battle read).</summary>
    [Fact]
    public async Task TheListsAreReadAtStart()
    {
        using var dir = TempDir.Create("urscore-membership-start");
        var transport = new Rosters();
        using var services = Compose(dir.Path, transport, [Ccgp], Birch, Ash);

        await services.LoadBookAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!services.Members.ContainsKey(Ccgp.Id) && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.Equal(new long[] { 101, 201 }, services.Members[Ccgp.Id].Order());
        Assert.Equal(1, transport.MemberReads);
    }

    private static AppServices Compose(string folder, IRecipeTransport transport, Source[] sources, params HostAccount[] accounts) =>
        Compose(folder, transport, sources, new ManualTime(Start), accounts);

    private static AppServices Compose(string folder, IRecipeTransport transport, Source[] sources, ManualTime time, params HostAccount[] accounts)
    {
        var paths = new AppPaths(folder);
        if (!File.Exists(paths.Sources)) new SourceStore(paths.Sources).Save(sources);
        var services = new AppServices(Dispatcher.CurrentDispatcher, paths, new StubHost(reachable: true, accounts), transport, time,
            Path.Combine(folder, "metric-rules.json"));
        // RoRoRo's list, as the app has it after its first ask at start.
        services.RefreshAccountsAsync(CancellationToken.None).GetAwaiter().GetResult();
        return services;
    }

    /// <summary>CCGP: Birch and Ash with 70 strangers, owned by a stranger. K0i2: Cedar. No battle running.</summary>
    private sealed class Rosters : IRecipeTransport
    {
        private int _memberReads;

        public int MemberReads => _memberReads;

        public bool Broken { get; set; }

        public TaskCompletionSource? Hold { get; init; }

        public async Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var address = url.AbsoluteUri;
            if (label == MemberLists.Label)
            {
                Interlocked.Increment(ref _memberReads);
                if (Hold is not null) await Hold.Task;
                if (Broken) return new FetchResult(500, "{}", null);
            }

            string? body = address switch
            {
                _ when address.EndsWith("/api/activeClanBattle", StringComparison.Ordinal) => """{"data":null}""",
                _ when address.EndsWith("/api/clan/CCGP", StringComparison.Ordinal) => Roster(1, Strangers(1).First(), Birch.RobloxUserId, Ash.RobloxUserId),
                _ when address.EndsWith("/api/clan/K0i2", StringComparison.Ordinal) => Roster(2, Strangers(2).First(), Cedar.RobloxUserId),
                _ => null,
            };

            return body is null ? new FetchResult(404, "{}", null) : new FetchResult(200, body, null);
        }

        private static string Roster(int clan, long owner, params long[] yours)
        {
            var rows = Strangers(clan).Concat(yours).Select(id => "{\"UserID\":" + id + ",\"PermissionLevel\":10,\"JoinTime\":1758000000}");
            return "{\"data\":{\"Owner\":" + owner + ",\"Members\":[" + string.Join(",", rows) + "],\"Battles\":{}}}";
        }
    }
}
