using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UrScore.Tests;

/// <summary>
/// No recipe words in anything a player can read (PRD "No recipe words anywhere", spec A11). Scans the text-bearing
/// attributes of every src xaml and the string literals of src/UI and src/Board (interpolation holes and comments
/// excluded) for <c>\brecipes?\b</c>. Allow-list: src/Cli and src/Recipes (developer tooling and the engine, never a
/// player surface), code comments, and the Diagnostics copy-to-clipboard lines (they start with "recipe=" or
/// "source=" and are for bug reports).
/// </summary>
public class RecipeWordFenceTests
{
    private static readonly Regex Word = new(@"\brecipes?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] TextAttributes =
        ["Text", "Content", "Header", "Title", "ToolTip", "AutomationProperties.Name"];

    [Fact]
    public void NoXamlTextAttributeOrUiStringLiteralSaysRecipe()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
        {
            if (IsAllowListedPath(src, file)) continue;
            foreach (var attribute in XDocument.Load(file).Descendants().SelectMany(e => e.Attributes()))
            {
                if (TextAttributes.Contains(attribute.Name.LocalName) && Word.IsMatch(attribute.Value))
                {
                    hits.Add($"{Path.GetRelativePath(src, file)}: {attribute.Name.LocalName}=\"{attribute.Value}\"");
                }
            }
        }

        foreach (var root in new[] { "UI", "Board" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(src, root), "*.cs", SearchOption.AllDirectories))
            {
                foreach (var literal in StringLiterals(File.ReadAllText(file)))
                {
                    if (Word.IsMatch(literal) && !IsCopyLine(literal))
                    {
                        hits.Add($"{Path.GetRelativePath(src, file)}: \"{literal}\"");
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Recipe words on a player surface:\n" + string.Join("\n", hits));
    }

    [Fact]
    public void TheScannerSeesAPlantedRecipeStringButNotCommentsOrHoles()
    {
        const string code = """
            // a recipe in a comment
            /* a recipe in a block comment */
            var a = "Import a recipe";
            var b = $"From {list.Recipe.Name} with {n} items";
            var c = $"From {list.Name} and your recipes";
            var d = @"verbatim ""recipe"" text";
            var e = '"'; var f = "clean";
            """;

        var hits = StringLiterals(code).Where(l => Word.IsMatch(l)).ToList();

        Assert.Equal(3, hits.Count);
        Assert.Contains("Import a recipe", hits);
        Assert.DoesNotContain(hits, h => h.Contains("items"));
    }

    private static bool IsAllowListedPath(string src, string file)
    {
        var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
        return relative.StartsWith("Cli/") || relative.StartsWith("Recipes/");
    }

    private static bool IsCopyLine(string literal) =>
        literal.StartsWith("recipe=", StringComparison.Ordinal) || literal.StartsWith("source=", StringComparison.Ordinal);

    /// <summary>The literal text of every C# string in the source, comments skipped, interpolation holes removed.</summary>
    private static IEnumerable<string> StringLiterals(string code)
    {
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? code.Length : end + 2;
            }
            else if (c == '\'')
            {
                i++;
                while (i < code.Length && code[i] != '\'') i += code[i] == '\\' ? 2 : 1;
                i++;
            }
            else if (c is '"' or '$' or '@')
            {
                var start = i;
                var interpolated = false;
                var verbatim = false;
                while (i < code.Length && code[i] is '$' or '@')
                {
                    interpolated |= code[i] == '$';
                    verbatim |= code[i] == '@';
                    i++;
                }

                if (i >= code.Length || code[i] != '"') { i = Math.Max(i, start + 1); continue; }

                if (i + 2 < code.Length && code[i + 1] == '"' && code[i + 2] == '"')
                {
                    var end = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    var raw = end < 0 ? code[(i + 3)..] : code[(i + 3)..end];
                    yield return interpolated ? StripHoles(raw) : raw;
                    i = end < 0 ? code.Length : end + 3;
                    continue;
                }

                i++;
                var body = new StringBuilder();
                var depth = 0;
                while (i < code.Length)
                {
                    var ch = code[i];
                    if (interpolated && ch == '{')
                    {
                        if (depth == 0 && i + 1 < code.Length && code[i + 1] == '{') { body.Append('{'); i += 2; continue; }
                        depth++;
                    }
                    else if (interpolated && ch == '}' && depth > 0) { depth--; i++; continue; }
                    else if (depth == 0)
                    {
                        if (ch == '"')
                        {
                            if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { body.Append('"'); i += 2; continue; }
                            break;
                        }

                        if (!verbatim && ch == '\\') { body.Append(code[i + 1 < code.Length ? i + 1 : i]); i += 2; continue; }
                        body.Append(ch);
                    }

                    i++;
                }

                i++;
                yield return body.ToString();
            }
            else
            {
                i++;
            }
        }
    }

    private static string StripHoles(string raw) => Regex.Replace(raw, @"\{[^}]*\}", "");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ur-Score.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.False(dir is null, "Could not locate Ur-Score.csproj above the test assembly.");
        return dir!.FullName;
    }
}
