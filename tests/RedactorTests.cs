using Labs626.UrScore.Recipes;

namespace UrScore.Tests;

public class RedactorTests
{
    private static Redactor With(params string[] secrets) => new(() => secrets);

    [Fact]
    public void MasksEverySavedValue() =>
        Assert.Equal("GET https://example.com/x?key=[key hidden] failed",
            With("abc123secret").Redact("GET https://example.com/x?key=abc123secret failed"));

    [Fact]
    public void MasksThePercentEncodedFormToo()
    {
        // A query-parameter key sits in the address encoded, so the plain form alone would miss it.
        Assert.Equal("?key=[key hidden]", With("a+b/c=d9").Redact("?key=a%2Bb%2Fc%3Dd9"));
    }

    [Fact]
    public void MasksTheLongestValueFirst() =>
        Assert.Equal("[key hidden]", With("secret", "secret-long").Redact("secret-long"));

    [Fact]
    public void IgnoresValuesTooShortToBeKeys() =>
        Assert.Equal("the cat sat", With("cat").Redact("the cat sat"));

    [Fact]
    public void NullBecomesEmpty() => Assert.Equal("", Redactor.None.Redact(null));
}
