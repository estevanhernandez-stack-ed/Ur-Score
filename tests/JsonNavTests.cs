using System.Globalization;
using System.Text.Json;
using Labs626.UrScore.Source;

namespace UrScore.Tests;

/// <summary>
/// <c>JsonNav.TryNumber</c> reads a string-encoded number for report values. "Report the raw value
/// as read" (spec §4.1) must not depend on which of RoRoRo's own UI languages is the current culture.
/// </summary>
public class JsonNavTests
{
    private static readonly string[] Cultures = ["de-DE", "fr-FR", "pt-BR", "ru-RU", "pl-PL", "es-ES", "en-US"];

    private static bool TryNumberOfString(string json, out double value)
    {
        using var document = JsonDocument.Parse(json);
        return JsonNav.TryNumber(document.RootElement, out value);
    }

    [Fact]
    public void ANumberInTextReadsTheSameUnderEveryCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in Cultures)
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

                var ok = TryNumberOfString("\"1234.5\"", out var value);

                Assert.True(ok, $"Expected \"1234.5\" to parse under {name}.");
                Assert.Equal(1234.5, value);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void AThousandsSeparatorIsRefusedNotMisread()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.False(TryNumberOfString("\"1,234\"", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
