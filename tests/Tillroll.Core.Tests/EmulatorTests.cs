using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tillroll;
using Tillroll.Emulator;
using Xunit;
using static Tillroll.Core.Tests.EscPosSequence;

namespace Tillroll.Core.Tests;

/// <summary>
/// Exercises the emulator core against the <see cref="FakeImageFactory"/> — i.e. with no graphics
/// backend at all. These tests would not compile or run if Core had a hard dependency on SkiaSharp.
/// </summary>
public class EmulatorTests
{
    private static ReceiptPrinter NewPrinter(out FakeImageFactory factory)
    {
        factory = new FakeImageFactory();
        return new ReceiptPrinter(PaperConfiguration.Default, factory, new FakeTypefaceProvider());
    }

    [Fact]
    public void PlainText_ProducesNonEmptyReceipt()
    {
        var printer = NewPrinter(out _);

        printer.FeedEscPos("Hello world\n");

        Assert.Contains(printer.ReceiptStack, r => !r.IsEmpty);
    }

    [Fact]
    public void Core_RendersThroughAbstraction_WithNoSkia()
    {
        var printer = NewPrinter(out var factory);
        printer.FeedEscPos("Hello world\n");

        using var image = printer.CurrentReceipt.Render();

        // Rendered onto the fake backend at the paper's pixel width, and text was actually drawn.
        Assert.Equal(PaperConfiguration.Default.GetPaperWidthInPixels(), image.Width);
        Assert.True(image.Height > 0);
        Assert.True(factory.Canvases.Sum(c => c.TextDraws) > 0, "expected text to be drawn");
    }

    [Fact]
    public void Cut_StartsNewReceipt()
    {
        var printer = NewPrinter(out _);

        printer.FeedEscPos("First\n");
        printer.Cut();
        printer.FeedEscPos("Second\n");

        Assert.Equal(2, printer.ReceiptStack.Count(r => !r.IsEmpty));
    }

    [Fact]
    public void Offline_DropsPrint()
    {
        var printer = NewPrinter(out _);
        printer.State.Online = false;

        printer.FeedEscPos("Should not print\n");

        Assert.DoesNotContain(printer.ReceiptStack, r => !r.IsEmpty);
    }

    [Fact]
    public void PrinterState_RaisesChanged()
    {
        var printer = NewPrinter(out _);
        var raised = false;
        printer.State.Changed += () => raised = true;

        printer.State.CoverOpen = true;

        Assert.True(raised);
    }

    [Fact]
    public void AutomaticStatusBack_BroadcastsToResponder()
    {
        var printer = NewPrinter(out _);
        var responder = new CapturingResponder();
        printer.RegisterResponder(responder);

        printer.SetAutoStatusBack(0x01); // enable -> sends current status immediately

        Assert.NotEmpty(responder.Bytes);
    }


    // ---- concurrency ----------------------------------------------------------------------------

    private static ReceiptPrinter NewPrinter() => NewPrinter(out _);

    private static IReadOnlyList<string> RenderedText(ReceiptPrinter printer, FakeImageFactory factory)
    {
        var text = new List<string>();
        foreach (var receipt in printer.ReceiptStack.Where(r => !r.IsEmpty))
        {
            using var _ = receipt.Render();
            text.AddRange(factory.Canvases[^1].DrawnText);
        }
        return text;
    }

    [Fact]
    public async Task TwoSenders_CommandSplitAcrossChunks_IsNotCorruptedByOtherSender()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(300);
        printer.FeedEscPos(Bytes(GS, 'r'), a);                         // A: half of "GS r 1"
        var bTask = Task.Run(() => printer.FeedEscPos(Bytes(DLE, EOT, 1) + "B" + Bytes(LF), b));
        Thread.Sleep(50);
        printer.FeedEscPos(Bytes(1), a);                               // A completes GS r 1
        Assert.Same(bTask, await Task.WhenAny(bTask, Task.Delay(2000)));
        Assert.Single(a.Bytes);                                        // A got exactly its GS r reply
        Assert.Single(b.Bytes);                                        // B got exactly its DLE EOT reply
    }

    [Fact]
    public void RealtimeStatusFromWaitingSender_IsAnsweredWithoutWaiting()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(5);
        printer.FeedEscPos("A owns the printer", a);
        var sw = Stopwatch.StartNew();
        printer.FeedEscPos(Bytes(DLE, EOT, 1), b);
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Single(b.Bytes);
    }

    [Fact]
    public async Task OwnerDisconnect_ReleasesPrinterImmediately()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        printer.RegisterResponder(a);
        printer.FeedEscPos("A", a);
        printer.UnregisterResponder(a);                                 // phone went away
        var sw = Stopwatch.StartNew();
        var fed = Task.Run(() => printer.FeedEscPos("B" + Bytes(LF), b));
        Assert.Same(fed, await Task.WhenAny(fed, Task.Delay(5000)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ReleaseOwnership_LetsNextSenderInImmediately()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        printer.FeedEscPos("A", a);
        printer.ReleaseOwnership(a);
        var sw = Stopwatch.StartNew();
        var fed = Task.Run(() => printer.FeedEscPos("B" + Bytes(LF), b));
        Assert.Same(fed, await Task.WhenAny(fed, Task.Delay(5000)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void IdleOwner_IsReplacedAfterTimeout()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(200);
        printer.FeedEscPos("A", a);
        var sw = Stopwatch.StartNew();
        printer.FeedEscPos("B" + Bytes(LF), b);
        Assert.InRange(sw.ElapsedMilliseconds, 100, 1500);
    }

    [Fact]
    public void NullResponderFeeds_NeverWait()
    {
        var a = new CapturingResponder();
        var printer = NewPrinter(); printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        printer.FeedEscPos("A", a);
        var sw = Stopwatch.StartNew();
        printer.FeedEscPos("direct API" + Bytes(LF));
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ConcurrentFeeds_FromManyThreads_DoNotThrowOrLoseJobs()
    {
        var printer = NewPrinter(out var factory); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(20);
        Parallel.For(0, 8, i => printer.FeedEscPos($"job{i}" + Bytes(LF), new CapturingResponder()));
        var text = string.Join("|", RenderedText(printer, factory));
        for (int i = 0; i < 8; i++) Assert.Contains($"job{i}", text);
    }

    [Fact]
    public void IdleOwnersHalfCommand_DoesNotSwallowNextSendersBytes()
    {
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(out var factory); printer.JobIdleTimeout = TimeSpan.FromMilliseconds(100);
        printer.FeedEscPos(Bytes(GS, 'r'), a);                         // A stops mid-command
        Thread.Sleep(200);
        printer.FeedEscPos("Hello" + Bytes(LF), b);
        Assert.Empty(b.Bytes);                                          // 'H' was not read as GS r's argument
        Assert.Contains("Hello", string.Join("|", RenderedText(printer, factory)));
    }

    [Fact]
    public async Task StatusPolling_DoesNotHoldThePrinter()
    {
        // A POS app that keeps its connection open and polls DLE EOT more often than the idle timeout
        // must not lock every other sender out.
        var poller = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(out var factory); printer.JobIdleTimeout = TimeSpan.FromSeconds(1);
        using var stop = new CancellationTokenSource();
        var polling = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                printer.FeedEscPos(Bytes(DLE, EOT, 1), poller);
                await Task.Delay(300);
            }
        });
        await Task.Delay(400);

        var sw = Stopwatch.StartNew();
        var job = Task.Run(() => printer.FeedEscPos("B's job" + Bytes(LF), b));
        var finished = await Task.WhenAny(job, Task.Delay(3000)) == job;
        stop.Cancel();
        await polling;

        Assert.True(finished, "the polling sender kept the printer");
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Contains("B's job", string.Join("|", RenderedText(printer, factory)));
        Assert.NotEmpty(poller.Bytes);
    }

    [Fact]
    public void WithoutBlockingWaits_ASecondSenderTakesOverInsteadOfWaiting()
    {
        // Single-threaded runtimes (the browser) can't block in Monitor.Wait.
        var a = new CapturingResponder(); var b = new CapturingResponder();
        var printer = NewPrinter(out var factory);
        printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        printer.WaitForOtherSenders = false;
        printer.FeedEscPos(Bytes(GS, 'r'), a);                        // A stops mid-command

        var sw = Stopwatch.StartNew();
        var ex = Record.Exception(() => printer.FeedEscPos("Hello" + Bytes(LF), b));

        Assert.Null(ex);
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Empty(b.Bytes);                                         // B's bytes weren't read as A's argument
        Assert.Contains("Hello", string.Join("|", RenderedText(printer, factory)));
    }

    [Fact]
    public async Task ReadingReceiptsWhileAnotherThreadFeeds_IsSafeInsideRunExclusive()
    {
        // The app's UI thread lists and renders receipts while a transport thread is still feeding a job
        // (a BLE phone sends a raster in ~150 small writes); a cut adds a receipt and every line adds to the
        // current receipt. Unsynchronized, that throws "Collection was modified" and crashed the app.
        var printer = NewPrinter();
        var feeder = new CapturingResponder();
        using var stop = new CancellationTokenSource();
        var feeding = Task.Run(() =>
        {
            for (int i = 0; i < 4000 && !stop.IsCancellationRequested; i++)
            {
                printer.FeedEscPos($"Line {i}" + Bytes(LF), feeder);
                if (i % 3 == 0)
                    printer.FeedEscPos(Bytes(GS, 'V', 0), feeder);
            }
        });

        Exception? failure = null;
        var sw = Stopwatch.StartNew();
        while (!feeding.IsCompleted && sw.ElapsedMilliseconds < 5000 && failure is null)
        {
            try
            {
                printer.RunExclusive(() =>
                {
                    foreach (var receipt in printer.ReceiptStack)
                    {
                        if (receipt.IsEmpty)
                            continue;
                        using var _ = receipt.Render();
                    }
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }
        stop.Cancel();
        await feeding;

        Assert.Null(failure);
    }

    [Fact]
    public void ReceiptVersion_ChangesOnlyWhenTheReceiptChanges()
    {
        var printer = NewPrinter();
        printer.FeedEscPos("first\n");
        int afterFirst = printer.CurrentReceipt.Version;

        printer.FeedEscPos(Bytes(ESC, 'E', 1));                   // bold on: changes no content
        Assert.Equal(afterFirst, printer.CurrentReceipt.Version);

        printer.FeedEscPos("second\n");
        Assert.True(printer.CurrentReceipt.Version > afterFirst);
    }

    [Fact]
    public void JobIdleTimeout_DefaultsToOneSecond()
        => Assert.Equal(TimeSpan.FromSeconds(1), NewPrinter().JobIdleTimeout);
}
