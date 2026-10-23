using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CrossEscPos.Controls;

/// <summary>
/// Shows a receipt bitmap the way a thermal printer prints it: every printer dot becomes a whole number of
/// screen pixels, with no smoothing, as large as fits the available width (up to the display scaling). A plain
/// <see cref="Image"/> draws a dot as 1.5 smoothed screen pixels at 150 %, which turns 1-bit receipts gray and
/// uneven.
/// </summary>
public class PixelPerfectImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<PixelPerfectImage, Bitmap?>(nameof(Source));

    static PixelPerfectImage()
    {
        AffectsMeasure<PixelPerfectImage>(SourceProperty);
        AffectsRender<PixelPerfectImage>(SourceProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Screen pixels per printer dot: the display scaling rounded to a whole number, at least 1.</summary>
    public static int ScreenPixelsPerDot(double renderScaling)
        => Math.Max(1, (int)Math.Round(renderScaling, MidpointRounding.AwayFromZero));

    /// <summary>How big one printer dot is drawn, and whether it's drawn sharp (whole screen pixels, unsmoothed).</summary>
    public readonly record struct DotFit(double DipsPerDot, bool Sharp);

    /// <summary>
    /// The largest whole number of screen pixels per dot that fits <paramref name="availableWidth"/> (DIPs), never
    /// more than the display scaling. Only when even one pixel per dot doesn't fit is the image shrunk smoothly.
    /// </summary>
    public static DotFit Fit(double renderScaling, double availableWidth, int imageWidthDots)
    {
        double scaling = renderScaling > 0 ? renderScaling : 1.0;
        int pixelsPerDot = ScreenPixelsPerDot(scaling);
        if (!double.IsInfinity(availableWidth) && imageWidthDots > 0)
            pixelsPerDot = Math.Min(pixelsPerDot, (int)Math.Floor(availableWidth * scaling / imageWidthDots));
        return pixelsPerDot >= 1
            ? new DotFit(pixelsPerDot / scaling, Sharp: true)
            : new DotFit(availableWidth / imageWidthDots, Sharp: false);
    }

    private TopLevel? _topLevel;
    private DotFit _fit = new(1, true);

    private double Scaling => _topLevel?.RenderScaling ?? 1.0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Source is not { } source)
            return default;
        _fit = Fit(Scaling, availableSize.Width, source.PixelSize.Width);
        return new Size(source.PixelSize.Width * _fit.DipsPerDot, source.PixelSize.Height * _fit.DipsPerDot);
    }

    protected override Size ArrangeOverride(Size finalSize) => DesiredSize;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source)
            return;
        var sourceRect = new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
        var destRect = new Rect(Bounds.Size);
        var interpolation = _fit.Sharp ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation }))
            context.DrawImage(source, sourceRect, destRect);
    }
}
