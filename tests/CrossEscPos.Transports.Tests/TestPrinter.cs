using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;

namespace CrossEscPos.Transports.Tests;

internal static class TestPrinter
{
    public static ReceiptPrinter Create(out FakeImageFactory factory)
    {
        factory = new FakeImageFactory();
        return new ReceiptPrinter(PaperConfiguration.Default, factory, new FakeTypefaceProvider());
    }

    /// <summary>All text drawn when every non-empty receipt is rendered, joined with '|'.</summary>
    public static string RenderedText(ReceiptPrinter printer, FakeImageFactory factory)
    {
        var text = new List<string>();
        foreach (var receipt in printer.ReceiptStack.Where(r => !r.IsEmpty))
        {
            using var _ = receipt.Render();
            text.AddRange(factory.Canvases[^1].DrawnText);
        }
        return string.Join("|", text);
    }

    /// <summary>Polls until <paramref name="condition"/> holds or the timeout passes.</summary>
    public static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }
}

/// <summary>A stream whose reads never complete until cancelled (like an idle serial port).</summary>
internal sealed class NeverEndingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
