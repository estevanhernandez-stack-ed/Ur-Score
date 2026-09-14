using Labs626.UrScore.Recipes;

namespace Labs626.UrScore.Book;

/// <summary>Filled in by Task 7.</summary>
public sealed class FinalsIndex
{
    public void Add(BookLine line)
    {
    }

    public bool HasClan(string slug, string inputsKey, string period) => false;

    public bool HasAccount(string slug, string inputsKey, string period, long userId) => false;

    public static FinalsIndex Load(string root) => new();
}

/// <summary>Filled in by Task 7.</summary>
public static class FinalsPlanner
{
    public static IReadOnlyList<BookLine> Plan(
        ReadContext context, RecipeReading reading, IReadOnlyDictionary<long, Guid> map, IReadOnlySet<string> tracked,
        FinalsIndex index, string? previousPeriod) => [];

    public static bool CurrentPeriodEnded(ReadContext context, RecipeReading reading, FinalsIndex index) => false;
}
