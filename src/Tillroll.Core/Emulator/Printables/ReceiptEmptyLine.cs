using Tillroll;
using Tillroll.Graphics;

namespace Tillroll.Emulator.Printables;

public class ReceiptEmptyLine : IReceiptPrintable
{
    private readonly int _height;

    public ReceiptEmptyLine(int height)
    {
        _height = height;
    }

    public int GetPrintHeight() => _height;

    public void Render(IReceiptCanvas canvas, int offsetX, int offsetY)
    {
    }
}
