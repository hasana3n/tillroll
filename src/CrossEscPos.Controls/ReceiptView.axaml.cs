using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CrossEscPos.Controls;

/// <summary>
/// Displays the live stream of rendered receipts. Bind <see cref="Receipts"/> to an
/// <see cref="IEnumerable"/> of <see cref="ReceiptViewModel"/>. Backend-agnostic — the view models
/// carry already-rendered Avalonia bitmaps.
/// </summary>
public partial class ReceiptView : UserControl
{
    /// <summary>Space left above a receipt scrolled to with <see cref="ScrollTo"/>.</summary>
    private const double ScrollMargin = 16;

    public static readonly StyledProperty<IEnumerable?> ReceiptsProperty =
        AvaloniaProperty.Register<ReceiptView, IEnumerable?>(nameof(Receipts));

    public IEnumerable? Receipts
    {
        get => GetValue(ReceiptsProperty);
        set => SetValue(ReceiptsProperty, value);
    }

    public ReceiptView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Scrolls the receipt list to the most recent page.</summary>
    public void ScrollToEnd()
        => this.FindControl<ScrollViewer>("ReceiptScrollView")?.ScrollToEnd();

    /// <summary>Scrolls so the top of the given receipt sits at the top of the view.</summary>
    public void ScrollTo(object item)
    {
        var scroller = this.FindControl<ScrollViewer>("ReceiptScrollView");
        var items = this.FindControl<ItemsControl>("ReceiptItems");
        if (scroller is null || items?.ContainerFromItem(item) is not { } container)
            return;
        if (container.TranslatePoint(default, scroller) is not { } top)
            return;
        scroller.Offset = scroller.Offset.WithY(System.Math.Max(0, scroller.Offset.Y + top.Y - ScrollMargin));
    }
}
