using System.Windows;
using System.Windows.Controls;

namespace Labs626.UrScore.UI;

/// <summary>The short coloured bar beside a legend entry, in the same theme brush as its line.</summary>
public sealed class LegendSwatch : Border
{
    public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(
        nameof(Colour), typeof(int), typeof(LegendSwatch), new PropertyMetadata(0, (d, e) => ((LegendSwatch)d).Paint((int)e.NewValue)));

    public LegendSwatch()
    {
        Width = 12;
        Height = 3;
        CornerRadius = new CornerRadius(2);
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 0, 6, 0);
        Paint(0);
    }

    public int Colour
    {
        get => (int)GetValue(ColourProperty);
        set => SetValue(ColourProperty, value);
    }

    private void Paint(int colour) => SetResourceReference(BackgroundProperty, LineChart.BrushKeyFor(colour));
}

/// <summary>A thin bar filled to a fraction: the gap to the group above.</summary>
public sealed class FillBar : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FillBar), new PropertyMetadata(0.0, (d, _) => ((FillBar)d).Update()));

    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };

    public FillBar()
    {
        Height = 6;
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "DividerBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "CyanBrush");
        Children.Add(track);
        Children.Add(_fill);
        SizeChanged += (_, _) => Update();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void Update() => _fill.Width = Math.Max(0, ActualWidth * Math.Clamp(Value, 0, 1));
}
