using Labs626.UrScore.Recipes;

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
        PanelType.PastPeriods, PanelType.Records, PanelType.Pace, PanelType.Top, PanelType.ProfileStat, PanelType.AccountsTable, PanelType.LiveLeaderboard,
    ];

    /// <summary>
    /// The title <see cref="PanelModels"/> gives a panel of this type on the card's recipe (<see cref="CardRecipe"/>), for a panel
    /// not yet added. Works the card's recipe out each time, defaults included, and that is fine where it is called: once when
    /// Add panel opens its form, and once per card when the gallery opens — never on a render or a tick (S2-4.7).
    /// </summary>
    public static string Title(PanelType type, LiveBoard live) => PanelText.Title(type, CardRecipe(type, live), live.Installed);

    /// <summary>The title a saved panel shows itself, on its own recipe: its pop-out's title, its popped-out slot and its ⋯ form say the same.</summary>
    public static string TitleOf(PanelDef panel, LiveBoard live) =>
        PanelText.Title(panel.Type, live.FindRecipe(panel.Settings.Recipe)?.Recipe, live.Installed);

    /// <summary>
    /// Every line of a card speaks for one recipe, the one its title names. Top, like its panel, takes the
    /// period from its list (else the first recipe with one) and names groups by the group recipe.
    /// </summary>
    public static IReadOnlyList<GalleryCard> Cards(LiveBoard live) =>
    [
        .. Order.Where(type => !OnlyOffModes(type, live)).Select(type =>
        {
            var recipe = CardRecipe(type, live);
            var groupRecipe = type == PanelType.Top ? PanelText.GroupRecipe(live.Installed) : recipe;
            var periodRecipe = type == PanelType.Top ? PanelText.TopPeriodRecipe(recipe, live.Installed) : recipe;
            var (group, groups) = PanelForms.GroupWords(groupRecipe);
            var period = periodRecipe is null ? "period" : RecipeWords.Period(periodRecipe);

            var (needs, shows, whyNot) = type switch
            {
                PanelType.Standing => ($"Needs a {group}.", $"Place, total, the last hour's gain and the {period} line.", $"Add a {group} in Setup first."),
                PanelType.Race => ($"Needs 1 to {PanelModels.MaxRace} {groups} of one mode.", $"Each {group}'s total over the current {period}, one line each.",
                    RaceWhyNot(live, group)),
                PanelType.MyAccounts => ("Needs a stat.", $"Your accounts by that stat, grouped by {group}, with rank, change and what was sent.", TickFirst),
                PanelType.PromotionCheck => ($"Needs a {group} your accounts are in, and one to compare with (your main unless you pick another).",
                    $"Where each of your accounts would place in the other {group} now. Live only.",
                    $"Needs a {group} your accounts are in, another to compare with, and a ticked stat."),
                PanelType.AccountCard => ("Needs one of your accounts.", $"Big numbers, a line over time, rank, best {period} and when it was last read.", TickFirst),
                PanelType.PastPeriods => ($"Needs a {group}.", $"Finished {period}s newest first: place, total and your best account.",
                    $"Needs a {group} whose mode keeps past {period}s."),
                PanelType.Records => ("Needs a stat.", $"Best {period}, best rank, highest value, biggest day and fastest 7 days.", TickFirst),
                PanelType.Pace => ($"Needs a {group} whose mode has a total and a {period}.",
                    $"How fast it is going now, on average and at its best, where that lands by the end, and what catching the place above would take. The field's own pace once a {groups} list is switched on.",
                    $"Add a {group} in Setup first."),
                PanelType.Top => ("Needs a mode that lists groups.", $"The top of the {period} live, with your {groups} placed where they'd rank.",
                    "Turn on a mode that lists groups."),
                PanelType.ProfileStat => ("Needs a stat from a mode that reads without a period.", "Your accounts with value, today's gain and 7-day gain.",
                    "Needs a ticked stat from a mode that reads without a period."),
                PanelType.AccountsTable => ($"Needs a {group} whose mode reads without a period.",
                    "Your accounts side by side: a column per stat you show, sorted by any column, with totals and change.",
                    $"Add a {group} whose mode reads without a period, in Setup first."),
                _ => ($"Needs a {group}.", "Every row live, your accounts marked. Never saved.", $"Add a {group} in Setup first."),
            };

            var canAdd = CanAdd(type, live);
            return new GalleryCard(type, PanelText.Title(type, recipe, live.Installed), needs, shows, canAdd, canAdd ? "" : whyNot);
        }),
    ];

    /// <summary>
    /// Whether every reader a panel type could be fed by belongs to an off mode, so "+ Add panel" leaves the type out rather
    /// than offering a panel that could only say "Battle is off." A type no reader fits stays (its card says what it needs),
    /// and so does one a reader that is on can still feed.
    /// </summary>
    private static bool OnlyOffModes(PanelType type, LiveBoard live)
    {
        if (live.Offs is not { Count: > 0 } offs) return false;

        var fitting = live.Installed.Where(i => PanelForms.Fits(type, i.Recipe)).ToList();
        return fitting.Count > 0 && fitting.All(i => offs.TryGetValue(i.Recipe.Slug, out var off) && off.ModeKey is not null);
    }

    /// <summary>
    /// The recipe a card speaks for: the one a panel added from it starts on (<see cref="PanelForms.Defaults"/>),
    /// else the first installed recipe the type fits, else none. Promotion check prefers an addable recipe,
    /// the main's first, before these fallbacks.
    /// </summary>
    private static Recipe? CardRecipe(PanelType type, LiveBoard live) =>
        (type == PanelType.PromotionCheck ? PromotionRecipe(live) : null)
        ?? live.FindRecipe(PanelForms.Build(type, PanelForms.Defaults(type, live), live).Recipe)?.Recipe
        ?? live.Installed.FirstOrDefault(i => PanelForms.Fits(type, i.Recipe))?.Recipe;

    private static Recipe? PromotionRecipe(LiveBoard live)
    {
        var origin = PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.Source, live, new FormValues())
            .FirstOrDefault(choice =>
                PanelForms.SourceChoices(PanelType.PromotionCheck, PanelField.ToSource, live, new FormValues(Source: choice.Key)).Count > 0
                && PanelForms.StatChoices(PanelType.PromotionCheck, live, new FormValues(Source: choice.Key), null).Count > 0);

        return origin is null ? null : live.FindRecipe(live.FindSource(origin.Key)!.Recipe)?.Recipe;
    }

    /// <summary>
    /// Why the Race card is off, of two reasons. While an installed recipe can race, or nothing is switched on, what is
    /// missing is a clan, and "Add a clan in Setup first." is true. Only with sources on and NO installed recipe with a
    /// summed total is another clan of the same mode no help: then the card says the mode needs a total (0.6.3).
    /// A new player with only a profile source on is the first case, not the second (review, 2026-09-27).
    /// <para>
    /// In the second case no recipe fits Race, so the card has no recipe and <paramref name="group"/> is the generic
    /// "source". The word is taken from the first source that is on instead, the one the player would call a clan.
    /// </para>
    /// </summary>
    private static string RaceWhyNot(LiveBoard live, string group)
    {
        var on = live.Sources
            .Where(s => s.Enabled)
            .Select(s => live.FindRecipe(s.Recipe)?.Recipe)
            .FirstOrDefault(r => r is { IsGroupList: false });
        var canRace = live.Installed.Any(i => PanelForms.Fits(PanelType.Race, i.Recipe));

        return on is null || canRace
            ? $"Add a {group} in Setup first."
            : $"Needs a {PanelForms.GroupWords(on).Group} whose mode has a total.";
    }

    private static bool CanAdd(PanelType type, LiveBoard live)
    {
        var none = new FormValues();
        return type switch
        {
            PanelType.Race => live.Sources.Any(s => s.Enabled && live.FindRecipe(s.Recipe) is { } installed && PanelForms.Fits(type, installed.Recipe)),
            PanelType.PromotionCheck => PromotionRecipe(live) is not null,
            PanelType.MyAccounts or PanelType.Records or PanelType.ProfileStat or PanelType.AccountCard =>
                PanelForms.StatChoices(type, live, none, null).Count > 0,
            _ => PanelForms.SourceChoices(type, PanelField.Source, live, none).Count > 0,
        };
    }
}
