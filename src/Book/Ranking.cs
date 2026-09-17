using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

public static class Ranking
{
    /// <summary>
    /// Standard competition ranking (1, 2, 2, 4) over the rows that have the stat, highest first. The map holds exactly the
    /// players ranked, one per user id, so its <c>Count</c> is the N of "#rank of N": a row with no value for the stat is in
    /// neither the rank nor the N (backlog S1-6.9). Every place that writes a rank's N takes it from here.
    /// </summary>
    public static IReadOnlyDictionary<long, int> Competition(IEnumerable<RecipeRow> rows, string statKey)
    {
        var values = rows
            .Where(r => r.Values.TryGetValue(statKey, out var v) && double.IsFinite(v))
            .GroupBy(r => r.UserId)
            .Select(g => (UserId: g.Key, Value: g.First().Values[statKey]))
            .ToList();

        var descending = values.Select(v => v.Value).OrderByDescending(v => v).ToArray();
        return values.ToDictionary(v => v.UserId, v => 1 + CountGreater(descending, v.Value));
    }

    private static int CountGreater(double[] descending, double value)
    {
        var low = 0;
        var high = descending.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (descending[mid] > value) low = mid + 1;
            else high = mid;
        }

        return low;
    }
}
