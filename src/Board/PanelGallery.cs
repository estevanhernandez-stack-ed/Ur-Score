using Labs626.UrScore.Core;

namespace Labs626.UrScore.Board;

/// <summary>One card in the gallery: what the panel needs and shows, and why it can't be added yet.</summary>
public sealed record GalleryCard(PanelType Type, string Title, string Needs, string Shows, bool CanAdd, string WhyNot);

/// <summary>
/// The panel gallery (spec §9.4): the ten panels in the spec's order, in the recipe's words (§3.4). A card
/// is enabled only when its form can be filled from what is installed now.
/// </summary>
public static class PanelGallery
{
    private const string TickFirst = "Tick Show or Send on a stat in Setup › Stats first.";

    public static IReadOnlyList<PanelType> Order { get; } =
    [
        PanelType.Standing, PanelType.Race, PanelType.MyAccounts, PanelType.PromotionCheck, PanelType.AccountCard,
        PanelType.PastPeriods, PanelType.Records, PanelType.Top, PanelType.ProfileStat, PanelType.LiveLeaderboard,
    ];

    /// <summary>The title <see cref="PanelModels"/> gives a panel of this type on the first installed recipe that fits it.</summary>
    public static string Title(PanelType type, LiveBoard live) =>
        PanelText.Title(type, live.Installed.FirstOrDefault(i => PanelForms.Fits(type, i.Recipe))?.Recipe, live.Installed);

    public static IReadOnlyList<GalleryCard> Cards(LiveBoard live)
    {
        var (group, groups) = PanelForms.GroupWords(live);
        var period = live.Installed.FirstOrDefault(i => !i.Recipe.IsGroupList && i.Recipe.Period is not null)?.Recipe is { } withPeriod
            ? RecipeWords.Period(withPeriod)
            : "period";

        return [.. Order.Select(type =>
        {
            var (needs, shows, whyNot) = type switch
            {
                PanelType.Standing => ($"Needs a {group}.", $"Place, total, the last hour's gain and the {period} line.", $"Add a {group} in Setup first."),
                PanelType.Race => ($"Needs 2 to {PanelModels.MaxRace} {groups} of one recipe.", $"Each {group}'s total over the current {period}, one line each.",
                    $"Needs at least 2 {groups} of one recipe. Add them in Setup."),
                PanelType.MyAccounts => ("Needs a stat.", $"Your accounts by that stat, grouped by {group}, with rank, change and what was sent.", TickFirst),
                PanelType.PromotionCheck => ($"Needs a {group} your accounts are in, and one to compare with (your main unless you pick another).",
                    $"Where each of your accounts would place in the other {group} now. Live only.",
                    $"Needs a {group} your accounts are in, another to compare with, and a ticked stat."),
                PanelType.AccountCard => ("Needs one of your accounts.", $"Big numbers, a line over time, rank, best {period} and when it was last read.", TickFirst),
                PanelType.PastPeriods => ($"Needs a {group}.", $"Finished {period}s newest first: place, total and your best account.",
                    $"Needs a {group} whose recipe keeps past {period}s."),
                PanelType.Records => ("Needs a stat.", $"Best {period}, best rank, highest value, biggest day and fastest 7 days.", TickFirst),
                PanelType.Top => ("Needs a recipe that lists groups.", $"The top of the {period} live, with your {groups} placed where they'd rank.",
                    "Import a recipe that lists groups, and turn it on in Setup."),
                PanelType.ProfileStat => ("Needs a stat from a recipe that reads without a period.", "Your accounts with value, today's gain and 7-day gain.",
                    "Needs a ticked stat from a recipe that reads without a period."),
                _ => ($"Needs a {group}.", "Every row live, your accounts marked. Never saved.", $"Add a {group} in Setup first."),
            };

            var canAdd = CanAdd(type, live);
            return new GalleryCard(type, Title(type, live), needs, shows, canAdd, canAdd ? "" : whyNot);
        })];
    }

    private static bool CanAdd(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        return type switch
        {
            PanelType.Race => live.Sources
                .Where(s => live.FindRecipe(s.Recipe) is { } installed && PanelForms.Fits(type, installed.Recipe))
                .GroupBy(s => s.Recipe, StringComparer.Ordinal)
                .Any(g => g.Count() >= 2),
            PanelType.PromotionCheck => PanelForms.SourceChoices(type, PanelField.Source, live, none).Any(origin =>
                PanelForms.SourceChoices(type, PanelField.ToSource, live, new FormValues(Source: origin.Key)).Count > 0
                && PanelForms.StatChoices(type, live, new FormValues(Source: origin.Key), null).Count > 0),
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard =>
                PanelForms.StatChoices(type, live, none, null).Count > 0,
            _ => PanelForms.SourceChoices(type, PanelField.Source, live, none).Count > 0,
        };
    }
}
