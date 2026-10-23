using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;
using Xunit;

namespace CrossEscPos.Transports.Tests;

public class PrinterStreamFeederTests
{
    [Fact]
    public async Task Pump_FeedsAllBytes_AndReturnsOnEof()
    {
        var printer = TestPrinter.Create(out var factory);
        var input = new MemoryStream(Encoding.Latin1.GetBytes("Hello\n"));

        int total = await PrinterStreamFeeder.PumpAsync(printer, input, new CapturingResponder(), CancellationToken.None);

        Assert.Equal(6, total);
        Assert.Contains("Hello", TestPrinter.RenderedText(printer, factory));
    }

    [Fact]
    public async Task Pump_StopsOnCancellation()
    {
        var printer = TestPrinter.Create(out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var pump = PrinterStreamFeeder.PumpAsync(printer, new NeverEndingStream(), new CapturingResponder(), cts.Token);

        Assert.Same(pump, await Task.WhenAny(pump, Task.Delay(1000)));
        Assert.Equal(0, await pump);
    }

    [Fact]
    public async Task Pump_ReleasesOwnership_OnEof()
    {
        var printer = TestPrinter.Create(out _);
        printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        var a = new CapturingResponder();

        await PrinterStreamFeeder.PumpAsync(printer, new MemoryStream(Encoding.Latin1.GetBytes("A")), a, CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var fed = Task.Run(() => printer.FeedEscPos("B\n", new CapturingResponder()));
        Assert.Same(fed, await Task.WhenAny(fed, Task.Delay(5000)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Pump_RepliesThroughResponder()
    {
        var printer = TestPrinter.Create(out _);
        var responder = new CapturingResponder();

        await PrinterStreamFeeder.PumpAsync(printer, new MemoryStream(new byte[] { 0x10, 0x04, 0x01 }), responder, CancellationToken.None);

        Assert.Single(responder.Bytes);
    }

    [Fact]
    public async Task Pump_UnexpectedStreamError_EndsThePumpAndReleasesThePrinter()
    {
        // e.g. a USB-serial adapter unplugged mid-read can surface as InvalidOperationException or
        // UnauthorizedAccessException rather than IOException.
        var printer = TestPrinter.Create(out _);
        printer.JobIdleTimeout = TimeSpan.FromSeconds(30);
        var a = new CapturingResponder();
        printer.FeedEscPos("A", a);

        var ex = await Record.ExceptionAsync(() => PrinterStreamFeeder.PumpAsync(printer, new ThrowingStream(), a, CancellationToken.None));

        Assert.Null(ex);
        var sw = Stopwatch.StartNew();
        var fed = Task.Run(() => printer.FeedEscPos("B\n", new CapturingResponder()));
        Assert.Same(fed, await Task.WhenAny(fed, Task.Delay(5000)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The port is closed.");
    }
}
