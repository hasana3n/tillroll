using Tillroll.Emulator;
using Xunit;

namespace Tillroll.Core.Tests;

public class PaperConfigurationTests
{
    [Fact]
    public void Mm58_PrintsThe384DotsOfA58mmHead()
    {
        var paper = PaperConfiguration.Mm58;
        Assert.Equal(58, paper.PaperWidthMm);
        Assert.Equal(384, paper.GetPrintWidthInPixels());
    }

    [Fact]
    public void Default_IsStill80mm()
        => Assert.Equal(80, PaperConfiguration.Default.PaperWidthMm);

    [Theory]
    [InlineData("58", 58)]
    [InlineData("58mm", 58)]
    [InlineData(" 58MM ", 58)]
    [InlineData("80", 80)]
    [InlineData(null, 80)]
    [InlineData("", 80)]
    [InlineData("nonsense", 80)]
    public void FromSetting_PicksAPreset(string? setting, double expectedWidth)
        => Assert.Equal(expectedWidth, PaperConfiguration.FromSetting(setting).PaperWidthMm);

    [Fact]
    public void Mm58_ReceiptRendersAtThePaperWidth()
    {
        var factory = new FakeImageFactory();
        var printer = new ReceiptPrinter(PaperConfiguration.Mm58, factory, new FakeTypefaceProvider());
        printer.FeedEscPos("58 mm\n");
        using var image = printer.CurrentReceipt.Render();
        Assert.Equal(PaperConfiguration.Mm58.GetPaperWidthInPixels(), image.Width);
    }
}
