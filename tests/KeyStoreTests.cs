using System.Text;
using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class KeyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "urscore-keys-" + Guid.NewGuid().ToString("N"));

    private string KeyFile => Path.Combine(_dir, "keys.dat");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ASavedKeyComesBackBoundToItsHost()
    {
        new KeyStore(KeyFile).Save("tracker", "API.Tracker.example", "abc123secret");

        var found = new KeyStore(KeyFile).Find("tracker");

        Assert.NotNull(found);
        Assert.Equal("api.tracker.example", found!.Host);
        Assert.Equal("abc123secret", found.Value);
    }

    [Fact]
    public void TheFileOnDiskDoesNotContainTheKey()
    {
        new KeyStore(KeyFile).Save("tracker", "api.tracker.example", "abc123secret");

        var bytes = File.ReadAllBytes(KeyFile);
        Assert.DoesNotContain("abc123secret", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("tracker", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void SavingTheSameKeyForItsOwnHostReplacesTheValue()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "first-value");
        store.Save("tracker", "api.tracker.example", "second-value");

        Assert.Equal("second-value", store.Find("tracker")!.Value);
        Assert.Single(store.Values());
    }

    [Fact]
    public void AKeyIsNeverSilentlyRebound()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "abc123secret");

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save("tracker", "evil.example", "abc123secret"));
        Assert.Equal("Your 'tracker' key is saved for api.tracker.example. Remove it before saving one for evil.example.", ex.Message);
        Assert.Equal("api.tracker.example", store.Find("tracker")!.Host);
    }

    [Fact]
    public void AKeyTooShortToRedactIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => new KeyStore(KeyFile).Save("tracker", "api.tracker.example", " abc "));
        Assert.StartsWith("That key is 3 characters. A real API key is at least 6.", ex.Message);
    }

    [Fact]
    public void RemoveForgetsTheKey()
    {
        var store = new KeyStore(KeyFile);
        store.Save("tracker", "api.tracker.example", "abc123secret");

        Assert.True(store.Remove("tracker"));
        Assert.Null(store.Find("tracker"));
        Assert.False(store.Remove("tracker"));
    }

    [Fact]
    public void ValuesListsEveryKeyForTheRedactor()
    {
        var store = new KeyStore(KeyFile);
        store.Save("a", "a.example", "value-one");
        store.Save("b", "b.example", "value-two");

        Assert.Equal(new[] { "value-one", "value-two" }, store.Values().Order().ToArray());
    }

    [Fact]
    public void AnUnreadableFileMeansNoKeysRatherThanACrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(KeyFile, [1, 2, 3, 4, 5]);

        var store = new KeyStore(KeyFile);
        Assert.Null(store.Find("tracker"));
        Assert.Empty(store.Values());
    }

    [Fact]
    public void NoFileMeansNoKeys() => Assert.Empty(new KeyStore(KeyFile).Values());
}
