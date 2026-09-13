namespace Labs626.UrScore.Recipes;

/// <summary>
/// Removes every saved key value from text before it is shown, saved or copied (spec §7.4). Reads
/// the current values on every call, so a key saved mid-session is masked from then on.
/// </summary>
public sealed class Redactor(Func<IReadOnlyCollection<string>> secrets)
{
    public const string Mask = "[key hidden]";

    /// <summary>
    /// Values shorter than this are not masked, because masking a three-character string would
    /// shred unrelated text. <c>KeyStore.Save</c> refuses keys this short, which is what keeps
    /// "a key never appears in text" true (plan Ruling 3).
    /// </summary>
    public const int MinimumLength = 6;

    public static Redactor None { get; } = new(() => []);

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        foreach (var secret in secrets().Where(s => s.Length >= MinimumLength).OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);

            var encoded = Uri.EscapeDataString(secret);
            if (!string.Equals(encoded, secret, StringComparison.Ordinal))
            {
                text = text.Replace(encoded, Mask, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }
}
