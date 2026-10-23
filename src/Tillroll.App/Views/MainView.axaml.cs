using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Tillroll.App.ViewModels;
using Tillroll.Controls;

namespace Tillroll.App.Views;

/// <summary>The shared main view (used inside a desktop window and as the browser single view).</summary>
public partial class MainView : UserControl
{
    private const double BoardWidth = 248;

    /// <summary>The receipt feed's horizontal margins (ReceiptView's ItemsControl margin, both sides).</summary>
    private const double FeedMargins = 32;

    private readonly ReceiptView? _receipts;
    private readonly ListBox? _board;
    private MainViewModel? _viewModel;
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();
        _receipts = this.FindControl<ReceiptView>("Receipts");
        _board = this.FindControl<ListBox>("Board");

        if (this.FindControl<Grid>("Canvas") is { } canvas)
            canvas.SizeChanged += (_, _) => UpdateLayoutForPaper();

        if (_receipts is not null)
        {
            _receipts.SizeChanged += (_, _) => UpdateScale();
            // A receipt counts as seen once it is on screen while the person is working the feed: moving the
            // pointer over it or scrolling it. Following new arrivals on its own doesn't count.
            _receipts.PointerMoved += (_, _) => MarkVisibleSeen();
            _receipts.AddHandler(PointerWheelChangedEvent, (_, _) => MarkVisibleSeen(),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
        UpdateLayoutForPaper();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, System.EventArgs e) => UpdateLayoutForPaper();

    /// <summary>Widest receipt on screen in printer dots, or the paper's width before the first one arrives.</summary>
    private int ReceiptDots()
    {
        if (_viewModel is not { } vm)
            return 0;
        int dots = vm.Receipts.Select(r => r.Image?.PixelSize.Width ?? 0).DefaultIfEmpty(0).Max();
        return dots > 0 ? dots : vm.PaperDots;
    }

    /// <summary>
    /// Shows the arrivals board only when the receipts stay pixel-exact beside it at the current width and display
    /// scaling. When they would not, the board steps aside and gives the feed its width; when even the whole canvas
    /// can't hold them pixel-exact, hiding the board wouldn't help, so it stays.
    /// </summary>
    private void UpdateLayoutForPaper()
    {
        if (this.FindControl<Grid>("Canvas") is not { } canvas || this.FindControl<Grid>("Body") is not { } body
            || this.FindControl<Border>("BoardPane") is not { } board)
            return;
        double scaling = _topLevel?.RenderScaling ?? 1.0;
        int dots = ReceiptDots();
        double width = canvas.Bounds.Width;
        bool fitsBeside = PixelPerfectImage.Fit(scaling, width - BoardWidth - FeedMargins, dots).Sharp;
        bool fitsAlone = PixelPerfectImage.Fit(scaling, width - FeedMargins, dots).Sharp;
        bool show = fitsBeside || !fitsAlone;
        board.IsVisible = show;
        body.ColumnDefinitions[1].Width = new GridLength(show ? BoardWidth : 0);
        UpdateScale();
    }

    private void MarkVisibleSeen()
    {
        if (_viewModel is not { HasNew: true } || _receipts is null)
            return;
        foreach (var item in _receipts.VisibleItems())
            if (item is ReceiptViewModel receipt)
                receipt.MarkSeen();
    }

    /// <summary>
    /// Works out how the receipts are drawn at the feed's current width (the same rule PixelPerfectImage uses) and
    /// reports it for the status strip, so a shrunk, no-longer-pixel-exact receipt never goes unremarked.
    /// </summary>
    private void UpdateScale()
    {
        if (_viewModel is not { } vm || _receipts is null || _receipts.Bounds.Width <= 0)
            return;
        double scaling = _topLevel?.RenderScaling ?? 1.0;
        vm.ReportScale(PixelPerfectImage.Fit(scaling, _receipts.Bounds.Width - FeedMargins, ReceiptDots()), scaling);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ReceiptsUpdated -= OnReceiptsUpdated;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ReceiptsUpdated += OnReceiptsUpdated;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateScale();
        }
    }

    private void OnReceiptsUpdated(object? sender, System.EventArgs e)
    {
        // Follow the newest arrival, on the paper feed and on the board.
        _receipts?.ScrollToEnd();
        UpdateLayoutForPaper();
        if (_viewModel is { Receipts.Count: > 0 } vm)
            _board?.ScrollIntoView(vm.Receipts[^1]);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedReceipt) && _viewModel?.SelectedReceipt is { } receipt)
            _receipts?.ScrollTo(receipt);
    }

}
