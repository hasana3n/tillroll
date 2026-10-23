using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossEscPos.Graphics;
using CrossEscPos.Controls.Services;
using CrossEscPos.Emulator;

namespace CrossEscPos.Controls;

/// <summary>
/// Wraps a single <see cref="Receipt"/> and exposes its rendered image for binding, plus a per-page
/// PNG export command. The render backend reaches the control only through <see cref="IImageEncoder"/>.
/// </summary>
public partial class ReceiptViewModel : ObservableObject
{
    private readonly Receipt _receipt;
    private readonly IImageEncoder _encoder;
    private readonly IFileDialogService _dialogs;
    private readonly int _index;
    private readonly Action<Action> _guard;
    private int _renderedVersion = -1;

    /// <summary>Printer dots per millimetre at 203 dpi, the resolution of 58 mm and 80 mm thermal printers.</summary>
    private const double DotsPerMm = 8;

    [ObservableProperty]
    private Bitmap? _image;

    /// <summary>True from the moment the receipt arrives until the person has looked at it.</summary>
    [ObservableProperty]
    private bool _isNew = true;

    [ObservableProperty]
    private string _lengthText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    public ReceiptViewModel(Receipt receipt, IImageEncoder encoder, IFileDialogService dialogs, int index)
        : this(receipt, encoder, dialogs, index, static render => render())
    {
    }

    /// <param name="renderGuard">
    /// Runs each render of the receipt. Pass <c>printer.RunExclusive</c> when the printer is fed from other
    /// threads (transports), so the receipt is never rendered while a job is still adding to it.
    /// </param>
    public ReceiptViewModel(Receipt receipt, IImageEncoder encoder, IFileDialogService dialogs, int index,
        Action<Action> renderGuard)
    {
        _receipt = receipt;
        _encoder = encoder;
        _dialogs = dialogs;
        _index = index;
        _guard = renderGuard;
        ArrivedAt = DateTime.Now;
        Refresh();
    }

    private IReceiptImage RenderGuarded()
    {
        IReceiptImage? image = null;
        _guard(() => image = _receipt.Render());
        return image!;
    }

    public string Id => _receipt.Guid;

    public string Title => $"Cut #{_index}";

    /// <summary>The receipt's position in the session, counting from 1.</summary>
    public int Number => _index;

    /// <summary>When the receipt started arriving.</summary>
    public DateTime ArrivedAt { get; }

    public string ArrivalTime => ArrivedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Clears <see cref="IsNew"/>: the person has seen this receipt.</summary>
    public void MarkSeen() => IsNew = false;

    public bool IsEmpty => _receipt.IsEmpty;

    /// <summary>Re-renders the receipt bitmap from its current state.</summary>
    public void Refresh()
    {
        if (_receipt.IsEmpty)
            return;

        // Skip the work when nothing changed since the last render: a phone sends a job as many small writes,
        // and each one asks every receipt on screen to refresh.
        IReceiptImage? image = null;
        _guard(() =>
        {
            if (_receipt.Version == _renderedVersion && Image is not null)
                return;
            _renderedVersion = _receipt.Version;
            image = _receipt.Render();
        });
        if (image is null)
            return;

        var old = Image;
        using (image)
            Image = image.ToAvaloniaBitmap(_encoder);
        old?.Dispose();

        var size = Image.PixelSize;
        SizeText = string.Create(CultureInfo.InvariantCulture, $"{size.Width} × {size.Height} dots");
        LengthText = string.Create(CultureInfo.InvariantCulture, $"{size.Height / DotsPerMm:0} mm");
    }

    [RelayCommand]
    private async Task Export()
    {
        using var image = RenderGuarded();
        var stream = await _dialogs.SavePngAsync($"receipt_{_index:D3}");
        if (stream is null)
            return;

        await using (stream)
            _encoder.EncodePng(image, stream);
    }
}
