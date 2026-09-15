using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Labs626.UrScore.Board;
using Labs626.UrScore.Recipes;
using Labs626.UrScore.Theming;

namespace Labs626.UrScore.UI;

using Source = Labs626.UrScore.Core.Source;

/// <summary>+ Board (spec §9.2): an empty board, or a starter built from your sources now.</summary>
public partial class AddBoardWindow : Window
{
    private readonly StarterBoard _battle;
    private readonly StarterBoard _grind;
    private readonly string _suggestedName;

    public AddBoardWindow(IReadOnlyList<InstalledRecipe> installed, IReadOnlyList<Source> sources, string suggestedName)
    {
        InitializeComponent();
        ThemeService.Attach(this);

        _suggestedName = suggestedName;
        _battle = StarterBoards.Build(installed, sources, StarterBoards.Battle);
        _grind = StarterBoards.Build(installed, sources, StarterBoards.Grind);

        NewBoardNameBox.Text = suggestedName;
        Describe(BattleBoardButton, _battle);
        Describe(GrindBoardButton, _grind);
        StarterLine.Text = _battle.Panels.Count == 0 && _grind.Panels.Count == 0
            ? "The starters fill in once a recipe has ticked stats and a source."
            : "A starter is built from your sources as they are now, and stays as you arrange it.";

        Loaded += (_, _) =>
        {
            NewBoardNameBox.Focus();
            NewBoardNameBox.SelectAll();
        };
    }

    /// <summary>The board to add, once a choice was made.</summary>
    public BoardDef? Result { get; private set; }

    private static void Describe(Button button, StarterBoard starter)
    {
        var count = starter.Panels.Count;
        button.Content = count == 0 ? $"{starter.Name}: nothing to show yet" : $"{starter.Name}: {count} panel{(count == 1 ? "" : "s")}";
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.IsEnabled = count > 0;
        AutomationProperties.SetName(button, $"{starter.Name} starter");
    }

    private void OnEmptyClick(object sender, RoutedEventArgs e) =>
        Finish(new BoardDef(BoardDefs.NewBoardId(), NameFor(null), []));

    private void OnBattleClick(object sender, RoutedEventArgs e) => FinishStarter(_battle);

    private void OnGrindClick(object sender, RoutedEventArgs e) => FinishStarter(_grind);

    private void FinishStarter(StarterBoard starter) =>
        Finish(BoardDefs.FromStarter(starter, freshIds: true) with { Name = NameFor(starter.Name) });

    /// <summary>The typed name. A starter picked with the suggested name left alone takes the starter's name.</summary>
    private string NameFor(string? starterName)
    {
        var typed = BoardDefs.CleanName(NewBoardNameBox.Text);
        if (starterName is not null && (typed is null || typed == _suggestedName)) return starterName;
        return typed ?? _suggestedName;
    }

    private void Finish(BoardDef board)
    {
        Result = board;
        DialogResult = true;
    }
}
