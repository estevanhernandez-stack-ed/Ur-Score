using System.IO;
using Labs626.UrScore.Core;

namespace UrScore.Tests;

/// <summary>
/// Which of your own accounts each clan's members list holds (backlog V3-S.20): kept per source, written to membership.json
/// so the board groups by it on open, refreshed no more than once per <see cref="Membership.Every"/>, and never holding an id
/// that isn't one of yours.
/// </summary>
public class MembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<long> Yours = new HashSet<long> { 101, 201, 301 };

    [Fact]
    public void ASetIsKeptPerSourceAndComesBackFromTheFile()
    {
        using var dir = TempDir.Create("urscore-membership");
        var path = Path.Combine(dir.Path, "membership.json");
        var membership = new Membership(path);
        Assert.Empty(membership.Ids);

        membership.Set("s-00000001", [101, 201], Now, Yours, keep: ["s-00000001", "s-00000002"]);
        membership.Set("s-00000002", [], Now, Yours, keep: ["s-00000001", "s-00000002"]);

        var again = new Membership(path);
        Assert.Equal(new long[] { 101, 201 }, again.Ids["s-00000001"].Order());
        Assert.Empty(again.Ids["s-00000002"]);
        Assert.Equal(Now, again.All["s-00000001"].ReadAt);
    }

    /// <summary>The store is the second check: only your ids are ever kept, whatever a caller hands it.</summary>
    [Fact]
    public void AnIdThatIsNotYoursIsNeverKeptOrWritten()
    {
        using var dir = TempDir.Create("urscore-membership-privacy");
        var path = Path.Combine(dir.Path, "membership.json");
        var membership = new Membership(path);

        membership.Set("s-00000001", [101, 987654321001, 987654321002], Now, Yours, keep: ["s-00000001"]);

        Assert.Equal(new long[] { 101 }, membership.Ids["s-00000001"].ToArray());
        Assert.DoesNotContain("987654321", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceThatIsGoneIsForgottenAtTheNextWrite()
    {
        using var dir = TempDir.Create("urscore-membership-prune");
        var path = Path.Combine(dir.Path, "membership.json");
        var membership = new Membership(path);
        membership.Set("s-00000001", [101], Now, Yours, keep: ["s-00000001", "s-00000002"]);
        membership.Set("s-00000002", [201], Now, Yours, keep: ["s-00000002"]);

        Assert.False(new Membership(path).Ids.ContainsKey("s-00000001"));
    }

    /// <summary>A read is due with nothing kept, and once <see cref="Membership.Every"/> has passed since the last read or try.</summary>
    [Fact]
    public void AReadIsDueEveryHalfHourCountingFailedTries()
    {
        using var dir = TempDir.Create("urscore-membership-due");
        var membership = new Membership(Path.Combine(dir.Path, "membership.json"));

        Assert.True(membership.IsDue("s-00000001", Now));
        membership.Set("s-00000001", [101], Now, Yours, keep: ["s-00000001"]);
        Assert.False(membership.IsDue("s-00000001", Now.AddMinutes(29)));
        Assert.True(membership.IsDue("s-00000001", Now.AddMinutes(30)));

        // A failed read keeps what was kept, and is not asked again for another half hour either.
        membership.Tried("s-00000001", Now.AddMinutes(30));
        Assert.False(membership.IsDue("s-00000001", Now.AddMinutes(59)));
        Assert.True(membership.IsDue("s-00000001", Now.AddMinutes(60)));
        Assert.Equal(new long[] { 101 }, membership.Ids["s-00000001"].ToArray());
    }

    [Fact]
    public void ABrokenFileIsNoMembershipNotAFailedStart()
    {
        using var dir = TempDir.Create("urscore-membership-broken");
        var path = Path.Combine(dir.Path, "membership.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(new Membership(path).Ids);
    }
}
