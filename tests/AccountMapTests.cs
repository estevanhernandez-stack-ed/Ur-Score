using Labs626.UrScore.Core;

namespace UrScore.Tests;

public class AccountMapTests
{
    private static readonly Guid A = Guid.Parse("9ad5e605-6b41-478c-add3-b916a31a5ab2");
    private static readonly Guid B = Guid.Parse("88dc7685-3a36-4f93-b526-a9bff2d7da6c");

    [Fact]
    public void JoinsOnRobloxUserId()
    {
        var map = AccountMap.Build([new(A, 111, "BirchMain"), new(B, 222, "AshAlt")]);

        Assert.Equal(A, map[111]);
        Assert.Equal(B, map[222]);
    }

    [Fact]
    public void SkipsAccountsWithNoRobloxUserIdYet()
    {
        // The contract documents roblox_user_id as 0 when not yet resolved. Zero is not an id, and
        // mapping it would put every unresolved account on one key.
        var map = AccountMap.Build([new(A, 0, "Unresolved"), new(B, 222, "AshAlt")]);

        Assert.False(map.ContainsKey(0));
        Assert.Single(map);
    }

    [Fact]
    public void NamesTheUnresolvedSoTheyCanBeShown()
    {
        // A silently unwatched account is the failure a user would never diagnose.
        var unresolved = AccountMap.Unresolved([new(A, 0, "Unresolved"), new(B, 222, "AshAlt")]);

        Assert.Single(unresolved);
        Assert.Equal("Unresolved", unresolved[0].DisplayName);
    }

    [Fact]
    public void DuplicateUserIdsKeepTheFirstRatherThanThrowing()
    {
        // Two saved accounts resolving to one Roblox user is a state the host permits. A
        // duplicate-key throw here would take the whole poll loop down over it.
        var map = AccountMap.Build([new(A, 111, "first"), new(B, 111, "second")]);

        Assert.Single(map);
        Assert.Equal(A, map[111]);
    }
}
