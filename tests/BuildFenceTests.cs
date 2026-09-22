using System.IO;

namespace UrScore.Tests;

/// <summary>
/// Guards the build settings a person cannot see from inside the code.
/// </summary>
public class BuildFenceTests
{
    /// <summary>
    /// The response file is the one place a build setting reaches everybody: CI, a script, and a person typing
    /// `dotnet build` by hand. Without the node-reuse line, a worker node left running from an earlier build can
    /// still hold the generated WPF temp project, and the failure surfaces as CS5001 — an entry point that is
    /// plainly there. Deleting this line would not break a build today; it would make a future one lie (V3-S.39).
    /// </summary>
    [Fact]
    public void BuildsDoNotLeaveWorkerNodesRunning()
    {
        var rsp = Path.Combine(RepoRoot(), "Directory.Build.rsp");
        Assert.True(File.Exists(rsp), $"{rsp} is missing; builds would silently go back to reusing nodes.");

        var lines = File.ReadAllLines(rsp)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

        Assert.Contains("-nodeReuse:false", lines);
    }

    /// <summary>
    /// Design-time builds keep their intermediates apart from real ones, or C# Dev Kit's build-on-save and a
    /// `dotnet build` typed a moment later collide in one obj tree on the WPF temp project's files — which was
    /// V3-S.39's actual cause, found from the process tree after two other theories. As with the response file,
    /// deleting this would break no build today and would bring the flake back, so the line is pinned.
    /// </summary>
    [Fact]
    public void DesignTimeBuildsKeepTheirOwnIntermediates()
    {
        var props = Path.Combine(RepoRoot(), "Directory.Build.props");
        Assert.True(File.Exists(props), $"{props} is missing; the IDE and the command line would share one obj tree again.");

        var text = File.ReadAllText(props);
        Assert.Contains("'$(DesignTimeBuild)' == 'true'", text);
        Assert.Contains("<BaseIntermediateOutputPath>obj/designtime/</BaseIntermediateOutputPath>", text);
        Assert.Contains("obj/**", text);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
