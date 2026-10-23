using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace CrossEscPos.Controls.Tests;

public class PixelPerfectImageTests
{
    // Each printer dot becomes a whole number of screen pixels, so 1-bit receipts stay sharp at any
    // display scaling instead of being smoothed at 1.5 pixels per dot.
    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.25, 1)]
    [InlineData(1.5, 2)]
    [InlineData(1.75, 2)]
    [InlineData(2.0, 2)]
    [InlineData(2.5, 3)]
    [InlineData(3.0, 3)]
    [InlineData(0.0, 1)]
    public void ScreenPixelsPerDot_IsAWholeNumberNearTheDisplayScaling(double renderScaling, int expected)
        => Assert.Equal(expected, PixelPerfectImage.ScreenPixelsPerDot(renderScaling));

    [Theory]
    [InlineData(1.5, 960.0, 640, 2, true)]                         // room for 2 screen pixels per dot
    [InlineData(1.5, 630.0, 640, 1, true)]                         // only room for 1: smaller but still sharp
    [InlineData(1.5, double.PositiveInfinity, 640, 2, true)]       // unconstrained: follow the display scaling
    [InlineData(2.0, 2000.0, 640, 2, true)]                        // never more than the display scaling
    [InlineData(1.0, 300.0, 640, 0, false)]                        // even 1 per dot doesn't fit: shrink smoothly
    public void Fit_UsesTheLargestWholeNumberOfPixelsPerDotThatFits(
        double scaling, double availableWidth, int imageWidth, int expectedPixelsPerDot, bool expectedSharp)
    {
        var fit = PixelPerfectImage.Fit(scaling, availableWidth, imageWidth);
        Assert.Equal(expectedSharp, fit.Sharp);
        if (expectedSharp)
            Assert.Equal(expectedPixelsPerDot / scaling, fit.DipsPerDot, 6);
        else
            Assert.Equal(availableWidth / imageWidth, fit.DipsPerDot, 6);
    }

    [AvaloniaFact]
    public void Measures_ToAWholeNumberOfScreenPixelsPerDot()
    {
        var bitmap = new WriteableBitmap(new PixelSize(640, 100), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var image = new PixelPerfectImage { Source = bitmap };
        var window = new Window { Content = image, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        double scaling = window.RenderScaling;
        int dots = PixelPerfectImage.ScreenPixelsPerDot(scaling);
        Assert.Equal(640 * dots / scaling, image.DesiredSize.Width, 3);
        Assert.Equal(100 * dots / scaling, image.DesiredSize.Height, 3);
    }
}
