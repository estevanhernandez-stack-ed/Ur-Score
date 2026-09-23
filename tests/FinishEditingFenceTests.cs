namespace UrScore.Tests;

/// <summary>
/// The final review's fix (task 18): once arranging has actually ended, whatever toast is still up belongs to
/// the draft (R10, <c>ChangeBoard</c>'s draft branch) and must go unless an "Arranged" toast replaces it —
/// left up, its Undo would reach past the ended draft into the saved history and pop an unrelated step. Read
/// from source, the way <c>PopOutLifecycleTests</c> already reads this same file: <c>FinishEditing</c> is tied
/// to <c>_services</c> and the window's real controls, so it isn't one a plain unit test can drive.
/// </summary>
public class FinishEditingFenceTests
{
    private static string FinishEditingBody()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);

        var editing = File.ReadAllText(Path.Combine(directory!.FullName, "src", "UI", "BoardWindow.Editing.cs"));
        var start = editing.IndexOf("private void FinishEditing()", StringComparison.Ordinal);
        var end = editing.IndexOf("private string DoneLabel()", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "FinishEditing has moved or been renamed; this fence is looking in the wrong place.");

        return editing[start..end];
    }

    [Fact]
    public void EndingArrangingWithoutAnArrangedToastHidesTheDraftsToast()
    {
        var method = FinishEditingBody();

        Assert.Contains("if (changed && before is not null)", method, StringComparison.Ordinal);
        Assert.Contains("else HideToast();", method, StringComparison.Ordinal);

        // Arranging must have actually ended — the draft cleared — before this decision runs. A failed save
        // returns earlier still, so the draft and its toast's Undo (pointed at the draft's own history) stay put.
        Assert.True(method.IndexOf("_draft = _draftBase = null;", StringComparison.Ordinal)
            < method.IndexOf("else HideToast();", StringComparison.Ordinal));
    }
}
