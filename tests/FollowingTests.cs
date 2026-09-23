using Labs626.UrScore.Board;
using Labs626.UrScore.Core;
using Labs626.UrScore.UI;
using static UrScore.Tests.BoardFixtures;

namespace UrScore.Tests;

public class FollowingTests
{
    private static readonly Source ProfileSource = SourceOf("s-00000009", Profile, null, SourceRole.Mine);

    private static IReadOnlyList<StarterBoard> Both() =>
        StarterBoards.All([Installed(Clan, "value"), Installed(Profile, "diamonds")], [MainClan, AltClan, ProfileSource]);

    private static IReadOnlyList<StarterBoard> BattleOnly() => StarterBoards.All([Installed(Clan, "value")], [MainClan, AltClan]);

    [Fact]
    public void WithNothingSavedEveryStarterWithPanelsIsATabThatFollows()
    {
        var shown = Following.Shown(null, Both());

        (string Id, string Name, string? Follows)[] expected = [("b-starter-battle", "Battle", "battle"), ("b-starter-alts", "Alts", "alts")];
        Assert.Equal(expected, shown.Select(b => (b.Id, b.Name, b.Follows)).ToArray());
        Assert.Equal("p-alts-1", shown[1].Panels[0].Id);
        Assert.Equal(new[] { "b-starter-battle" }, Following.Shown(null, BattleOnly()).Select(b => b.Id).ToArray());
    }

    [Fact]
    public void AProfileOnlyUserSeesAltsAndAClanOnlyUserSeesBattleWithNoEmptyTabBeside()
    {
        // Only the profile recipe, ticked: Battle has nothing to build from, so it isn't a tab, and Alts has no empty state.
        var profileOnly = StarterBoards.All([Installed(Profile, "diamonds")], [ProfileSource]);
        var alts = Assert.Single(Following.Shown(null, profileOnly));
        Assert.Equal(("b-starter-alts", "Alts", "alts"), (alts.Id, alts.Name, alts.Follows));
        Assert.NotEmpty(alts.Panels);
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(profileOnly, alts));

        // Only the clan recipe: Battle alone, and no empty Alts tab.
        var clanOnly = BattleOnly();
        var battle = Assert.Single(Following.Shown(null, clanOnly));
        Assert.Equal(("b-starter-battle", "Battle", "battle"), (battle.Id, battle.Name, battle.Follows));
        Assert.Equal(BoardEmpty.None, BoardText.EmptyFor(clanOnly, battle));
    }

    [Fact]
    public void WhenNoStarterHasPanelsOneFollowingBoardShowsTheEmptyState()
    {
        var none = Assert.Single(Following.Shown(null, StarterBoards.All([], [])));
        Assert.Equal(("b-starter-battle", 0), (none.Id, none.Panels.Count));
        Assert.Equal("battle", none.Follows);

        // A clan recipe with no main and a profile recipe with nothing ticked: Battle asks for the main.
        var noMain = StarterBoards.All([Installed(Clan, "value"), Installed(Profile)], []);
        Assert.Equal("b-starter-battle", Assert.Single(Following.Shown(null, noMain)).Id);
    }

    [Fact]
    public void AChangeToOneTabWritesThatTabAndTheOtherKeepsFollowing()
    {
        var starters = Both();
        var shown = Following.Shown(null, starters);
        var alts = BoardEdits.RemovePanel(shown[1], shown[1].Panels[1].Id);

        var saved = Following.ToSave(null, starters, BoardEdits.Replace(shown, alts));

        Assert.Equal(("b-starter-battle", 0), (saved[0].Id, saved[0].Panels.Count));
        Assert.Equal("battle", saved[0].Follows);
        Assert.Null(saved[1].Follows);
        Assert.Equal(shown[1].Panels.Count - 1, saved[1].Panels.Count);

        // Read back later: Battle is rebuilt from the sources as they are then; Alts stays as it was written.
        var later = StarterBoards.All([Installed(Clan, "value"), Installed(Profile, "diamonds")], [MainClan, ProfileSource]);
        var again = Following.Shown(saved, later);
        Assert.Equal(later[0].Panels.Count, again[0].Panels.Count);
        Assert.Equal(saved[1], again[1]);
    }

    [Fact]
    public void AnUntouchedTabStaysFollowingAndARenameOrAPopOutIsAChange()
    {
        var starters = Both();
        var shown = Following.Shown(null, starters);
        var added = new BoardDef("b-00000001", "Board 3", []);

        var saved = Following.ToSave(null, starters, [.. shown, added]);
        Assert.Equal(new[] { "battle", "alts", null }, saved.Select(b => b.Follows).ToArray());
        Assert.All(saved.Take(2), board => Assert.Empty(board.Panels));

        var renamed = Following.ToSave(null, starters, BoardEdits.Rename(shown, "b-starter-alts", "Grinding"));
        Assert.Null(renamed[1].Follows);
        Assert.Equal("Grinding", renamed[1].Name);

        var popped = Following.ToSave(null, starters,
            BoardEdits.Replace(shown, BoardEdits.PopOut(shown[0], shown[0].Panels[0].Id, new PopOutRect(10, 10, 360, 300))));
        Assert.Null(popped[0].Follows);
        Assert.Equal("p-battle-1", popped[0].Panels[0].Id);
        Assert.Equal("alts", popped[1].Follows);
    }

    [Fact]
    public void ATabHandedOverFollowingNothingIsWrittenAsItIsEvenWhenItMatchesItsStarter()
    {
        var starters = Both();
        var shown = Following.Shown(null, starters);

        var saved = Following.ToSave(null, starters, BoardEdits.Replace(shown, shown[1] with { Follows = null }));

        Assert.Equal("battle", saved[0].Follows);
        Assert.Null(saved[1].Follows);
        Assert.Equal(shown[1].Panels, saved[1].Panels);
    }

    [Fact]
    public void ADeletedTabIsGoneForGoodButAHiddenOneKeepsFollowing()
    {
        // Only the clan recipe: Alts has nothing to show and is hidden, so + Board doesn't delete it.
        var starters = BattleOnly();
        var shown = Following.Shown(null, starters);
        var rivals = new BoardDef("b-00000001", "Rivals", []);

        var saved = Following.ToSave(null, starters, [.. shown, rivals]);
        (string Id, string? Follows)[] expected = [("b-starter-battle", "battle"), ("b-00000001", null), ("b-starter-alts", "alts")];
        Assert.Equal(expected, saved.Select(b => (b.Id, b.Follows)).ToArray());

        // The profile recipe arrives: Alts appears, after the boards you have.
        Assert.Equal(new[] { "Battle", "Rivals", "Alts" }, Following.Shown(saved, Both()).Select(b => b.Name).ToArray());

        // Battle deleted while it showed: it doesn't come back.
        var deleted = Following.ToSave(saved, Both(), BoardEdits.Delete(Following.Shown(saved, Both()), "b-starter-battle"));
        Assert.DoesNotContain(deleted, b => b.Id == "b-starter-battle");
        Assert.Equal(new[] { "Rivals", "Alts" }, Following.Shown(deleted, Both()).Select(b => b.Name).ToArray());
    }

    [Fact]
    public void AFirstRunBoardAddedBesideTheEmptyStateDoesntFreezeIt()
    {
        var none = StarterBoards.All([], []);
        var added = new BoardDef("b-00000001", "Board 2", []);

        var saved = Following.ToSave(null, none, [.. Following.Shown(null, none), added]);

        Assert.Equal(new[] { "battle", null, "alts" }, saved.Select(b => b.Follows).ToArray());
        Assert.Equal(new[] { "Board 2" }, Following.Shown(saved, none).Select(b => b.Name).ToArray());
    }

    [Fact]
    public void AHandEditedFileCantShowOneStarterTwice()
    {
        IReadOnlyList<BoardDef> saved = [new BoardDef("b-starter-alts", "Alts", []), new BoardDef("b-x", "Alts", [], Follows: "alts")];

        Assert.Single(Following.Shown(saved, Both()), b => b.Id == "b-starter-alts");
    }

    /// <summary>
    /// A setup import replacing a pristine PC's boards: what it brought plus every following tab the starters would
    /// draw here now, so an import never costs you a starter tab (spec §2). The following ones carry no panels — the
    /// same shape any following entry keeps in <c>boards.json</c>.
    /// </summary>
    [Fact]
    public void KeepFollowingAddsEveryFollowingStarterBesideWhatWasImported()
    {
        var starters = Both();
        IReadOnlyList<BoardDef> imported = [new BoardDef("b-00000001", "Rivals", [])];

        var kept = Following.KeepFollowing(null, starters, imported);

        Assert.Equal(3, kept.Count);
        Assert.Equal("Rivals", kept[0].Name);
        Assert.Equal(
            new (string, string?)[] { ("b-starter-battle", "battle"), ("b-starter-alts", "alts") },
            kept.Skip(1).Select(b => (b.Id, b.Follows)).ToArray());
        Assert.All(kept.Skip(1), b => Assert.Empty(b.Panels));
    }

    /// <summary>A following tab already in <c>boards.json</c> is kept once, not doubled, alongside what the import brought.</summary>
    [Fact]
    public void KeepFollowingKeepsAFollowingBoardAlreadySavedWithoutDoublingIt()
    {
        var starters = Both();
        IReadOnlyList<BoardDef> saved = [new BoardDef("b-starter-battle", "Battle", [], Follows: "battle")];
        IReadOnlyList<BoardDef> imported = [new BoardDef("b-00000001", "Rivals", [])];

        var kept = Following.KeepFollowing(saved, starters, imported);

        Assert.Equal(new[] { "b-00000001", "b-starter-battle" }, kept.Select(b => b.Id).ToArray());
    }

    [Fact]
    public void BoardsSavedBeforeFollowingStayExactlyAsSavedAndNoTabIsAdded()
    {
        // D3: a 0.3.0 file follows nothing. Its boards, their old starter ids included, show and save as they are.
        var json = """
            [ { "id": "b-starter", "name": "Battle", "panels": [
                  { "id": "p-starter-1", "type": "standing", "size": { "span": 4 }, "order": 0, "settings": { "recipe": "pet-sim-99-clan-battle", "sourceId": "s-00000001" } } ] },
              { "id": "b-00000001", "name": "Rivals", "panels": [] } ]
            """;
        var saved = BoardsFile.Parse(json);

        var shown = Following.Shown(saved, Both());

        Assert.Equal(saved, shown);
        Assert.All(shown, b => Assert.Null(b.Follows));
        Assert.Equal(saved, Following.ToSave(saved, Both(), shown));
        Assert.DoesNotContain("follows", BoardsFile.Serialize(Following.ToSave(saved, Both(), shown)));
    }
}
