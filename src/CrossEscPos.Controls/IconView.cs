using System;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CrossEscPos.Controls;

/// <summary>
/// Draws an outline icon given as a geometry on a 24-unit grid (the Lucide convention), scaled to the control's
/// size with a stroke of <see cref="StrokeWidth"/> screen units however large the icon is. The colour follows the
/// inherited <see cref="Foreground"/>, so an icon inside a button changes with the button's text.
/// </summary>
public class IconView : Avalonia.Controls.Control
{
    private const double Grid = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconView, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeWidth), 1.5);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<IconView>();

    static IconView()
    {
        AffectsRender<IconView>(DataProperty, StrokeWidthProperty, ForegroundProperty);
    }

    /// <summary>16 by 16 unless Width and Height say otherwise.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Foreground is not { } brush)
            return;
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        double scale = size / Grid;
        var pen = new Pen(brush, StrokeWidth / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
            context.DrawGeometry(null, pen, data);
    }
}
