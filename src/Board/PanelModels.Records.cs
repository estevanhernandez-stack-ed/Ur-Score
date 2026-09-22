using System.Globalization;
using Labs626.UrScore.Book;
using Labs626.UrScore.Core;
using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Board;

public sealed record RecordsModel(PanelHead Head, IReadOnlyList<FactModel> Facts);

/// <summary>
/// Records (spec §9.4): best period, best rank, highest value, biggest day and fastest seven days. One of the panel-type files
/// <see cref="PanelModels"/> was split into on 2026-09-22 (S1-13.15); the shared helpers stay in PanelModels.cs.
/// </summary>
public static partial class PanelModels
{
    public static RecordsModel RecordsPanel(LiveBoard live, ScoreBookReader reader, PanelSettings settings)
    {
        var title = PanelText.Title(PanelType.Records, null, live.Installed);
        if (live.FindRecipe(settings.Recipe) is not { } installed) return new RecordsModel(StaleSource(live, settings, title), []);

        var recipe = installed.Recipe;
        if (settings.Stat is null || RecipeStats.Find(recipe, settings.Stat) is not { } stat)
        {
            return new RecordsModel(new PanelHead(title, Stale: PanelText.StaleStat), []);
        }

        var zone = live.Time.LocalTimeZone;

        // Each account's records in each of your sources, never a merge of two sources' readings (backlog S1-9.3): every fact
        // below is the best one source holds, so an account read by two clans can't be credited with a rise between them.
        var all = (
            from account in live.Accounts
            where account.RobloxUserId != 0
            from source in SourcesYoursIn(live, recipe)
            select (Account: account, Found: Records.For(reader, recipe.Slug, source.InputsKey, source.Id, account.RobloxUserId, stat.Key, live.Time))
        ).ToList();

        string Highest(Func<AccountRecords, double?> pick, Func<HostAccount, AccountRecords, double, string> text)
        {
            var best = all.Where(x => pick(x.Found) is not null).OrderByDescending(x => pick(x.Found)).FirstOrDefault();
            return best.Found is null ? Dash : text(best.Account, best.Found, pick(best.Found)!.Value);
        }

        var facts = new List<FactModel>();
        if (recipe.Period is not null)
        {
            facts.Add(new FactModel($"Best {RecipeWords.Period(recipe)}",
                Highest(r => r.BestPeriodValue, (a, r, v) => $"{a.DisplayName} · {ShortValue(v, stat.Format, zone)} · {r.BestPeriod}")));

            var bestRank = all.Where(x => x.Found.BestRank is not null).OrderBy(x => x.Found.BestRank).FirstOrDefault();
            facts.Add(new FactModel("Best rank",
                bestRank.Found is null ? Dash : $"{bestRank.Account.DisplayName} · #{bestRank.Found.BestRank} · {bestRank.Found.BestRankPeriod}"));
        }

        facts.Add(new FactModel("Highest", Highest(r => r.Highest, (a, _, v) => $"{a.DisplayName} · {ShortValue(v, stat.Format, zone)}")));
        facts.Add(new FactModel("Biggest day", Highest(r => r.BiggestDay, (a, _, v) => $"{a.DisplayName} · {PanelText.Change(v, stat.Format)}")));
        facts.Add(new FactModel("Fastest 7 days", Highest(r => r.FastestWeek, (a, _, v) => $"{a.DisplayName} · {PanelText.Change(v, stat.Format)}")));

        return new RecordsModel(new PanelHead(title, stat.Label), facts);
    }
}
