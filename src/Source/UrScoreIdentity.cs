using System.Reflection;

namespace Labs626.UrScore.Source;

/// <summary>How Ur Score introduces itself to every service it calls. Polling anonymously is rude.</summary>
public static class UrScoreIdentity
{
    public static string UserAgent { get; } =
        $"UrScore/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"} (RoRoRo plugin)";
}
