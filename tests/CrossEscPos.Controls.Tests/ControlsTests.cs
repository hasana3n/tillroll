using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CrossEscPos.Controls.Services;
using CrossEscPos.Emulator;
using CrossEscPos.Emulator.Enums;
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
    public void PrinterStatePanel_PaperSegmentsSetPaperLevel()
    {
        var state = new PrinterState();
        var panel = new PrinterStatePanel { State = state };
        var window = new Window { Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var segments = panel.GetVisualDescendants().OfType<RadioButton>().ToList();
        Assert.Equal(new object?[] { "Plenty", "Near end", "Out" }, segments.Select(s => s.Content));
        Assert.True(segments[0].IsChecked);

        segments[2].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PaperLevel.Out, state.Paper);

        state.Paper = PaperLevel.NearEnd;
        Dispatcher.UIThread.RunJobs();
        Assert.True(segments[1].IsChecked);
    }

    [AvaloniaFact]
    public void PrinterStatePanel_CheckedSegmentStandsOut_WithOnlyTheControlsDefaultTheme()
    {
        // A NuGet consumer has no CrossEscPos.App theme: the controls' own default resources must still show which
        // paper segment is checked.
        var panel = new PrinterStatePanel { State = new PrinterState() };
        var window = new Window { Content = panel };
        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new System.Uri("avares://CrossEscPos.Controls/"))
            {
                Source = new System.Uri("avares://CrossEscPos.Controls/Themes/Default.axaml"),
            });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var faces = panel.GetVisualDescendants().OfType<RadioButton>()
            .Select(r => r.GetVisualDescendants().OfType<Border>().First(b => b.Name == "face").Background)
            .ToList();
        Assert.NotNull(faces[0]);                 // "Plenty" is checked by default
        Assert.NotEqual(faces[0], faces[1]);
    }

    [AvaloniaFact]
    public void ReceiptView_VisibleItems_AreOnlyTheReceiptsInTheViewport()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        for (int i = 0; i < 6; i++)
            printer.FeedEscPos(string.Concat(Enumerable.Repeat("A line of a long receipt\n", 30)) + "\x1dV\0");
        var receipts = new ObservableCollection<ReceiptViewModel>(
            printer.ReceiptStack.Where(r => !r.IsEmpty)
                .Select((r, i) => new ReceiptViewModel(r, new SkiaImageEncoder(), new StubFileDialogService(), i + 1)));

        var view = new ReceiptView { Receipts = receipts };
        var window = new Window { Content = view, Width = 900, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var visible = view.VisibleItems().ToList();
        Assert.Contains(receipts[0], visible);
        Assert.DoesNotContain(receipts[^1], visible);   // far below the fold: not seen yet
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

        Assert.Contains(view.GetVisualDescendants().OfType<PixelPerfectImage>(), image => image.Source is not null);
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

    [Fact]
    public void PrinterReadiness_NamesTheFirstBlockingCause()
    {
        Assert.Equal((PrinterHealth.Ready, "Ready"), PrinterReadiness.Describe(new PrinterState()));
        Assert.Equal((PrinterHealth.Blocked, "Offline"),
            PrinterReadiness.Describe(new PrinterState { Online = false, CoverOpen = true }));
        Assert.Equal((PrinterHealth.Blocked, "Cover open"), PrinterReadiness.Describe(new PrinterState { CoverOpen = true }));
        Assert.Equal((PrinterHealth.Blocked, "Out of paper"),
            PrinterReadiness.Describe(new PrinterState { Paper = PaperLevel.Out }));
        Assert.Equal((PrinterHealth.Blocked, "Unrecoverable error"),
            PrinterReadiness.Describe(new PrinterState { Error = PrinterErrorState.Unrecoverable }));
        Assert.Equal((PrinterHealth.Blocked, "Recoverable error"),
            PrinterReadiness.Describe(new PrinterState { Error = PrinterErrorState.Recoverable }));
        Assert.Equal((PrinterHealth.Warning, "Ready · paper near end"),
            PrinterReadiness.Describe(new PrinterState { Paper = PaperLevel.NearEnd }));
    }

    [AvaloniaFact]
    public void ReceiptViewModel_IsNewUntilSeen()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        printer.FeedEscPos("Hello\n");
        var vm = new ReceiptViewModel(printer.CurrentReceipt, new SkiaImageEncoder(), new StubFileDialogService(), 3);

        Assert.True(vm.IsNew);
        Assert.Equal(3, vm.Number);
        vm.MarkSeen();
        Assert.False(vm.IsNew);
    }

    [AvaloniaFact]
    public void ReceiptViewModel_ReportsItsSizeInDotsAndMillimetres()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new SkiaImageFactory(), new SkiaTypefaceProvider());
        printer.FeedEscPos("Hello\nWorld\n");
        var vm = new ReceiptViewModel(printer.CurrentReceipt, new SkiaImageEncoder(), new StubFileDialogService(), 1);

        var size = vm.Image!.PixelSize;
        Assert.Equal($"{size.Height / 8.0:0} mm", vm.LengthText);         // 203 dpi is 8 dots per millimetre
        Assert.Equal($"{size.Width} × {size.Height} dots", vm.SizeText);
        Assert.Matches(@"^\d\d:\d\d:\d\d$", vm.ArrivalTime);
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<Stream?> SavePngAsync(string suggestedName) => Task.FromResult<Stream?>(null);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
