using System;
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

    [ObservableProperty]
    private Bitmap? _image;

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

    public bool IsEmpty => _receipt.IsEmpty;

    /// <summary>Re-renders the receipt bitmap from its current state.</summary>
    public void Refresh()
    {
        if (_receipt.IsEmpty)
            return;

        var old = Image;
        using var image = RenderGuarded();
        Image = image.ToAvaloniaBitmap(_encoder);
        old?.Dispose();
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
