using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrossEscPos.Logging;

namespace CrossEscPos.Emulator;

/// <summary>
/// Pumps a byte stream (a serial port, a Bluetooth serial connection, …) into a printer. Shared by the
/// stream-based transports so they all handle end-of-stream, cancellation and job ownership the same way.
/// </summary>
public static class PrinterStreamFeeder
{
    /// <summary>
    /// Reads <paramref name="input"/> until end of stream (a read returns 0), cancellation, or an I/O error,
    /// feeding each chunk to <paramref name="printer"/> with <paramref name="responder"/> as the reply
    /// channel. Always releases the responder's ownership of the printer before returning, so another
    /// sender doesn't wait out <see cref="ReceiptPrinter.JobIdleTimeout"/>.
    /// </summary>
    /// <returns>The total number of bytes read.</returns>
    public static async Task<int> PumpAsync(ReceiptPrinter printer, Stream input, IPrinterResponder responder,
        CancellationToken cancellationToken, int bufferSize = 16 * 1024)
    {
        var buffer = new byte[bufferSize];
        int total = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break; // end of stream: the other side closed the connection

                total += read;
                printer.FeedEscPos(buffer.AsSpan(0, read), responder);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the caller.
        }
        catch (ObjectDisposedException)
        {
            // The stream was closed under us (transport stopping).
        }
        catch (IOException ex)
        {
            Logger.Exception(ex, "Stream read error");
        }
        finally
        {
            printer.ReleaseOwnership(responder);
        }

        return total;
    }
}
