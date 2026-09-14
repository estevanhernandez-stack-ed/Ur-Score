namespace Labs626.UrScore.Recipes;

/// <summary>The literal host of an https url, read from the raw text so it works before placeholders are filled.</summary>
internal static class RecipeHosts
{
    public const string Scheme = "https://";

    public static string HostOf(string url) => Authority(url).Host;

    /// <summary>Every host a recipe contacts on its own: each step's, and each search list's.</summary>
    public static IReadOnlySet<string> ContactedBy(Recipe recipe) =>
        recipe.Steps.Select(step => HostOf(step.Url))
            .Concat(recipe.Inputs.Where(input => input.Search is not null).Select(input => HostOf(input.Search!.Url)))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The authority split into host and whether it carried user info.</summary>
    public static (string Host, bool HasUserInfo) Authority(string url)
    {
        var rest = url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) ? url[Scheme.Length..] : url;
        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];

        var at = authority.LastIndexOf('@');
        var hasUserInfo = at >= 0;
        if (hasUserInfo) authority = authority[(at + 1)..];

        var colon = authority.LastIndexOf(':');
        if (colon >= 0) authority = authority[..colon];

        return (authority.ToLowerInvariant(), hasUserInfo);
    }
}
