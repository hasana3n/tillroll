using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CrossEscPos.App.ViewModels;
using CrossEscPos.Controls;

namespace CrossEscPos.App.Views;

/// <summary>The shared main view (used inside a desktop window and as the browser single view).</summary>
public partial class MainView : UserControl
{
    private readonly ReceiptView? _receipts;
    private readonly ListBox? _board;
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        _receipts = this.FindControl<ReceiptView>("Receipts");
        _board = this.FindControl<ListBox>("Board");

        // Moving the pointer over the receipts counts as looking at them: the New lamps go out.
        if (this.FindControl<Grid>("Canvas") is { } canvas)
            canvas.PointerMoved += OnCanvasPointerMoved;

        DataContextChanged += OnDataContextChanged;
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
        }
    }

    private void OnReceiptsUpdated(object? sender, System.EventArgs e)
    {
        // Follow the newest arrival, on the paper feed and on the board.
        _receipts?.ScrollToEnd();
        if (_viewModel is { Receipts.Count: > 0 } vm)
            _board?.ScrollIntoView(vm.Receipts[^1]);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedReceipt) && _viewModel?.SelectedReceipt is { } receipt)
            _receipts?.ScrollTo(receipt);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is { HasNew: true } vm)
            vm.MarkAllSeenCommand.Execute(null);
    }
}
