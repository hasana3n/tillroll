using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CrossEscPos.Controls.Services;
using CrossEscPos.Emulator;
using CrossEscPos.Rendering.Skia;
using Xunit;

namespace CrossEscPos.Controls.Tests;

public class ControlsTests
{
    [AvaloniaFact]
    public void PrinterStatePanel_ToggleUpdatesBoundState()
    {
        var state = new PrinterState { Online = true };
        var panel = new PrinterStatePanel { State = state };
        var window = new Window { Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The first ToggleSwitch in the panel is the Online switch, two-way bound to State.Online.
        var online = panel.GetVisualDescendants().OfType<ToggleSwitch>().First();
        online.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(state.Online);
    }

    [AvaloniaFact]
    public void ReceiptView_RendersReceiptImages()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        printer.FeedEscPos("Hello control\n");

        var encoder = new SkiaImageEncoder();
        var dialogs = new StubFileDialogService();
        var receipts = new ObservableCollection<ReceiptViewModel>(
            printer.ReceiptStack.Where(r => !r.IsEmpty)
                .Select((r, i) => new ReceiptViewModel(r, encoder, dialogs, i + 1)));

        var view = new ReceiptView { Receipts = receipts };
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(view.GetVisualDescendants().OfType<Image>());
    }

    [AvaloniaFact]
    public void ReceiptViewModel_RendersInsideTheGivenGuard()
    {
        // The app passes printer.RunExclusive so a receipt is never rendered while a transport is adding to it.
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        printer.FeedEscPos("Hello guard\n");
        int guarded = 0;

        var vm = new ReceiptViewModel(printer.CurrentReceipt, new SkiaImageEncoder(), new StubFileDialogService(), 1,
            render => { guarded++; render(); });
        printer.FeedEscPos("More\n");
        vm.Refresh();

        Assert.Equal(2, guarded);           // the first render in the constructor, then the refresh after new content
        Assert.NotNull(vm.Image);
    }

    [AvaloniaFact]
    public void ReceiptViewModel_Refresh_SkipsRenderingWhenTheReceiptHasNotChanged()
    {
        // Each BLE write triggers a refresh; re-rendering unchanged receipts every time froze the window.
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        printer.FeedEscPos("Hello\n");
        var vm = new ReceiptViewModel(printer.CurrentReceipt, new SkiaImageEncoder(), new StubFileDialogService(), 1,
            render => render());
        var first = vm.Image;

        vm.Refresh();
        vm.Refresh();
        Assert.Same(first, vm.Image);                             // nothing changed: no new bitmap was rendered

        printer.FeedEscPos("More\n");
        vm.Refresh();
        Assert.NotSame(first, vm.Image);
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<Stream?> SavePngAsync(string suggestedName) => Task.FromResult<Stream?>(null);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
