using System.Diagnostics;
using System.Text.Json;
using Labs626.UrScore.Cli;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class TryCommandTests
{
    private sealed class RouteTransport(params (string Start, string Body)[] routes) : IRecipeTransport
    {
        public Task<FetchResult> GetAsync(Uri url, IReadOnlyDictionary<string, string> headers, string label, CancellationToken cancellationToken)
        {
            var route = routes.FirstOrDefault(r => url.AbsoluteUri.StartsWith(r.Start, StringComparison.Ordinal));
            return Task.FromResult(route.Body is null ? new FetchResult(404, "{}", null) : new FetchResult(200, route.Body, null));
        }
    }

    private const string Battle = """{ "status": "ok", "data": { "configName": "B", "configData": { "FinishTime": 1757611800 } } }""";

    private const string ClanResponse = """
        { "status": "ok", "data": { "Battles": {
            "A": { "Place": 40, "Points": 500, "PointContributions": [ { "UserID": 7001001, "Points": 300 } ] },
            "B": { "Place": 3, "Points": 999, "PointContributions": [
                { "UserID": 1647274201, "Points": 4200 }, { "UserID": 7002002, "Points": 3100 }, { "UserID": 7003003, "Points": 10 } ] }
        } } }
        """;

    private static RouteTransport Clan() => new(
        ("https://ps99.biggamesapi.io/api/activeClanBattle", Battle),
        ("https://ps99.biggamesapi.io/api/clan/", ClanResponse));

    private static string RecipeFile(TempDir.Scope dir, string fixture)
    {
        var path = Path.Combine(dir.Path, fixture);
        File.WriteAllText(path, RecipeParserTests.Fixture(fixture));
        return path;
    }

    private static async Task<(int Code, string Output)> Run(IRecipeTransport transport, params string[] args)
    {
        var output = new StringWriter();
        var code = await TryCommand.RunAsync(args, output, transport, new NoKeys(), CancellationToken.None);
        return (code, output.ToString());
    }

    [Fact]
    public async Task AReadPrintsCountsYourAccountAndNoOtherPlayer()
    {
        using var dir = TempDir.Create("urscore-try");
        var (code, output) = await Run(Clan(), "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2", "--account", "1647274201");

        Assert.Equal(TryCommand.Ok, code);
        Assert.Contains("Outcome: Read", output);
        Assert.Contains("Rows seen: 3", output);
        Assert.Contains("value: found in 3, missed in 0, smallest 10, median 3100, largest 4200", output);
        Assert.Contains("1647274201: value=4200, rank 1 of 3", output);
        Assert.Contains("clan-place = 3", output);
        Assert.Contains("Past periods: 2 (A, B)", output);
        Assert.Contains("ps99.biggamesapi.io", output);
        Assert.DoesNotContain("7002002", output);
        Assert.DoesNotContain("7003003", output);
        Assert.DoesNotContain("7001001", output);
    }

    /// <summary>
    /// Backlog S1-6.9: a rank is counted among the rows that have the stat, so "of" counts those rows too. A contributor with
    /// no points was counted, printing "rank 1 of 3" for a place among two.
    /// </summary>
    [Fact]
    public async Task ARankIsOfTheRowsThatHaveTheStat()
    {
        using var dir = TempDir.Create("urscore-try");
        var clan = new RouteTransport(
            ("https://ps99.biggamesapi.io/api/activeClanBattle", Battle),
            ("https://ps99.biggamesapi.io/api/clan/", """
                { "status": "ok", "data": { "Battles": { "B": { "Place": 3, "Points": 999, "PointContributions": [
                    { "UserID": 1647274201, "Points": 4200 }, { "UserID": 7002002, "Points": 3100 }, { "UserID": 7003003 } ] } } } }
                """));

        var (_, output) = await Run(clan, "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2", "--account", "1647274201");

        Assert.Contains("Rows seen: 3", output);
        Assert.Contains("value: found in 2, missed in 1", output);
        Assert.Contains("1647274201: value=4200, rank 1 of 2", output);
        Assert.DoesNotContain("7003003", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonOutputParsesAndCarriesNoOtherPlayer(bool includeAccount)
    {
        using var dir = TempDir.Create("urscore-try");
        var args = new List<string> { "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2", "--json" };
        if (includeAccount) args.AddRange(["--account", "1647274201"]);
        var (code, output) = await Run(Clan(), [.. args]);

        Assert.Equal(TryCommand.Ok, code);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("Read", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("rowsSeen").GetInt32());
        Assert.Equal(new[] { "A", "B" }, json.RootElement.GetProperty("pastPeriods").EnumerateArray().Select(period => period.GetString()));
        Assert.DoesNotContain("7002002", output);
        Assert.DoesNotContain("7003003", output);
        Assert.DoesNotContain("7001001", output);

        var accounts = json.RootElement.GetProperty("accounts").EnumerateArray();
        if (includeAccount)
        {
            var account = Assert.Single(accounts);
            Assert.Equal(1647274201L, account.GetProperty("userId").GetInt64());
            Assert.Equal(4200, account.GetProperty("values").GetProperty("value").GetDouble());
            Assert.Equal(1, account.GetProperty("rank").GetProperty("value").GetInt32());
            Assert.Equal(3, account.GetProperty("of").GetInt32());
        }
        else
        {
            Assert.Empty(accounts);
            Assert.DoesNotContain("1647274201", output);
        }
    }

    [Fact]
    public async Task AMissingInputSaysWhichAndHow()
    {
        using var dir = TempDir.Create("urscore-try");
        var (code, output) = await Run(Clan(), "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"));

        Assert.Equal(TryCommand.InputMissing, code);
        Assert.Contains("Set Your clan with --input clan=<value>.", output);
    }

    [Fact]
    public async Task ARefusedRecipeListsItsProblems()
    {
        using var dir = TempDir.Create("urscore-try");
        var path = Path.Combine(dir.Path, "bad.recipe.json");
        File.WriteAllText(path, """{ "recipe": 1, "name": "Bad" }""");

        var (code, output) = await Run(Clan(), "--try", path);

        Assert.Equal(TryCommand.Refused, code);
        Assert.Contains("has no 'credit'", output);
    }

    [Fact]
    public async Task AStoppedReadNamesItsOutcome()
    {
        using var dir = TempDir.Create("urscore-try");
        var idle = new RouteTransport(("https://ps99.biggamesapi.io/api/activeClanBattle", """{ "status": "ok", "data": null }"""));

        var (code, output) = await Run(idle, "--try", RecipeFile(dir, "petsim99-clan-battle.recipe.json"), "--input", "clan=K0i2");

        Assert.Equal(TryCommand.Stopped, code);
        Assert.Contains("Outcome: Idle", output);
    }

    [Theory]
    [InlineData("--try")]
    [InlineData("--try", "missing-file.json")]
    [InlineData("--try", "x.json", "--input", "no-equals-sign")]
    [InlineData("--try", "x.json", "--wat")]
    public async Task BadArgumentsPrintUsage(params string[] args)
    {
        var (code, output) = await Run(Clan(), args);

        Assert.Equal(TryCommand.BadArguments, code);
        Assert.Contains("Usage: 626labs.ur-score.exe --try", output);
    }

    [Fact]
    public void OnlyTryArgumentsAreClaimed()
    {
        Assert.True(TryCommand.Wants(["--try", "x.json"]));
        Assert.False(TryCommand.Wants([]));
        Assert.False(TryCommand.Wants(["--something"]));
    }

    [Fact]
    public async Task TryRunsEvenWhenTheApplicationMutexAlreadyExists()
    {
        using var instance = new Mutex(false, @"Local\626labs.ur-score.single-instance");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(TryCommand).Assembly.Location);
        start.ArgumentList.Add("--try");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token);

            Assert.Equal(TryCommand.BadArguments, process.ExitCode);
            Assert.Contains(TryCommand.Usage, await output);
            Assert.Contains("--try needs a recipe file.", await output);
            Assert.Equal("", await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
