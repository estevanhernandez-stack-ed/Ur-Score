using Labs626.UrScore.Core;
using Labs626.UrScore.Games;

namespace UrScore.Tests;

/// <summary>
/// Spec A5: an install from before mode switches keeps reading what it had installed. Variants of the 0.6.3 fixture,
/// copied into a folder of the test's own and trimmed to the recipes each case installs.
/// </summary>
public class UpgradeModesTests
{
    private const string Battle = "pet-sim-99/battle";
    private const string Profile = "pet-sim-99/profile";
    private const string Game = "pet-sim-99";
    private static readonly string[] BattleSlugs = ["pet-sim-99-clan-battle-points", "pet-sim-99-top-clans"];
    private static readonly string[] ProfileSlugs = ["pet-sim-99-profile"];

    /// <summary>The fixture, minus every recipe not in <paramref name="keep"/>.</summary>
    private static TempDir.Scope Install(params string[] keep)
    {
        var dir = TempDir.Create("urscore-upgrade-modes");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "old-install-0.6.3");
        Assert.True(Directory.Exists(source), "the old-install fixture was not copied to the test output");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir.Path, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var paths = new AppPaths(dir.Path);
        foreach (var slug in UpgradeModes.InstalledSlugs(paths).Where(s => !keep.Contains(s)))
        {
            File.Delete(Path.Combine(paths.Recipes, slug + ".recipe.json"));
        }

        return dir;
    }

    private static IReadOnlyDictionary<string, bool>? Run(AppPaths paths, IReadOnlyDictionary<string, bool>? current = null) =>
        UpgradeModes.FirstRun(GameCatalog.BuiltIn, UpgradeModes.DataFolderExisted(paths), UpgradeModes.InstalledSlugs(paths), current);

    [Fact]
    public void AllThreeReadersInstalledTurnsBothModesOn()
    {
        using var dir = Install([.. BattleSlugs, .. ProfileSlugs, "roblox-followers"]);

        var map = Run(new AppPaths(dir.Path));

        Assert.NotNull(map);
        Assert.True(map[Game]);
        Assert.True(map[Battle]);
        Assert.True(map[Profile]);
    }

    [Fact]
    public void ABattleOnlyInstallKeepsBattleAndLeavesProfileOff()
    {
        using var dir = Install(BattleSlugs);

        var map = Run(new AppPaths(dir.Path));

        Assert.NotNull(map);
        Assert.True(map[Game]);
        Assert.True(map[Battle]);
        Assert.False(map[Profile]);
        Assert.False(new ModeSwitches(GameCatalog.BuiltIn, map).IsOn(Profile));
    }

    [Fact]
    public void ABattleInstallWithOneOfItsTwoRecipesStillTurnsBattleOn()
    {
        using var dir = Install("pet-sim-99-top-clans");

        var map = Run(new AppPaths(dir.Path));

        Assert.NotNull(map);
        Assert.True(map[Battle]);
        Assert.False(map[Profile]);
    }

    [Fact]
    public void AProfileOnlyInstallKeepsProfileAndLeavesBattleOff()
    {
        using var dir = Install(ProfileSlugs);

        var map = Run(new AppPaths(dir.Path));

        Assert.NotNull(map);
        Assert.False(map[Battle]);
        Assert.True(map[Profile]);
    }

    [Fact]
    public void AnInstallWithOnlyTheOrphanTurnsEveryModeOff()
    {
        using var dir = Install("roblox-followers");

        var map = Run(new AppPaths(dir.Path));

        Assert.NotNull(map);
        Assert.True(map[Game]);
        Assert.False(map[Battle]);
        Assert.False(map[Profile]);
    }

    [Fact]
    public void AFreshInstallWritesNothing()
    {
        using var dir = TempDir.Create("urscore-upgrade-fresh");
        var paths = new AppPaths(Path.Combine(dir.Path, "never-created"));

        Assert.False(UpgradeModes.DataFolderExisted(paths));
        Assert.Null(Run(paths));
    }

    [Fact]
    public void AnInstallThatAlreadyDecidedWritesNothing()
    {
        using var dir = Install(BattleSlugs);

        Assert.Null(Run(new AppPaths(dir.Path), new Dictionary<string, bool> { [Profile] = true }));
        Assert.Null(Run(new AppPaths(dir.Path), new Dictionary<string, bool>()));
    }

    [Fact]
    public void TheFolderExistedWithASourcesFileAloneOrARecipeAlone()
    {
        using var onlySettings = TempDir.Create("urscore-upgrade-existed");
        var paths = new AppPaths(onlySettings.Path);
        File.WriteAllText(paths.Settings, "{}");
        Assert.False(UpgradeModes.DataFolderExisted(paths), "settings.json is written on every first start");

        File.WriteAllText(paths.Sources, "[]");
        Assert.True(UpgradeModes.DataFolderExisted(paths));

        using var onlyRecipe = TempDir.Create("urscore-upgrade-existed");
        var recipePaths = new AppPaths(onlyRecipe.Path);
        Directory.CreateDirectory(recipePaths.Recipes);
        File.WriteAllText(Path.Combine(recipePaths.Recipes, "x.recipe.json"), "{}");
        Assert.True(UpgradeModes.DataFolderExisted(recipePaths));
    }
}
