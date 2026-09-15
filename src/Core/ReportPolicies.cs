using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Core;

/// <summary>
/// Which of your accounts a recipe may send for. One derivation, used by the running watches and by
/// Setup › Alerts, so what the card says and what is actually sent can't drift apart.
/// </summary>
public static class ReportPolicies
{
    /// <summary>
    /// Whether the recipe's roles let it send at all. A group list never sends (spec §3.5). A recipe whose
    /// switched-on sources are all watched sends nothing (spec §4.1). A recipe with no source yet is
    /// described by what its own sources would send.
    /// </summary>
    public static bool SendsByRole(InstalledRecipe installed, IReadOnlyList<Source> sources)
    {
        if (installed.Recipe.IsGroupList) return false;

        var own = sources.Where(s => s.Enabled && string.Equals(s.Recipe, installed.Recipe.Slug, StringComparison.Ordinal)).ToList();
        return own.Count == 0 || own.Any(s => s.Role != SourceRole.Watch);
    }

    /// <summary>Your known accounts minus the recipe's excluded ones; empty when its roles send nothing.</summary>
    public static IReadOnlySet<Guid> Allowed(InstalledRecipe installed, IReadOnlyCollection<HostAccount> known, IReadOnlyList<Source> sources)
    {
        if (!SendsByRole(installed, sources)) return new HashSet<Guid>();

        var excluded = installed.State.Excluded;
        return known.Select(a => a.AccountId).Where(id => !excluded.Contains(id)).ToHashSet();
    }
}
