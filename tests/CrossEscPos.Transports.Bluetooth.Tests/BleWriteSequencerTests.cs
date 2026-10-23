using System.Collections.Generic;
using CrossEscPos.Transports.Bluetooth.Ble;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests;

/// <summary>
/// Windows raises GattLocalCharacteristic.WriteRequested and then completes each GetRequestAsync on any pool
/// thread, so writes can finish out of order. The sequencer hands them on in the order the events arrived.
/// </summary>
public class BleWriteSequencerTests
{
    private static readonly BlePrinterProfile P = BlePrinterProfiles.Generic18F0;

    private readonly List<(string Client, byte Value)> _delivered = new();

    private BleWriteSequencer NewSequencer()
        => new((client, _, data, _) => _delivered.Add((client, data[0])));

    [Fact]
    public void WritesCompletedOutOfOrder_AreDeliveredInArrivalOrder()
    {
        var sequencer = NewSequencer();
        long first = sequencer.Reserve("phone");
        long second = sequencer.Reserve("phone");
        long third = sequencer.Reserve("phone");

        sequencer.Complete("phone", third, P, new byte[] { 3 }, 23);
        sequencer.Complete("phone", first, P, new byte[] { 1 }, 23);
        Assert.Equal(new[] { ("phone", (byte)1) }, _delivered);

        sequencer.Complete("phone", second, P, new byte[] { 2 }, 23);
        Assert.Equal(new[] { ("phone", (byte)1), ("phone", (byte)2), ("phone", (byte)3) }, _delivered);
    }

    [Fact]
    public void AFailedWrite_DoesNotBlockTheOnesAfterIt()
    {
        var sequencer = NewSequencer();
        long first = sequencer.Reserve("phone");
        long second = sequencer.Reserve("phone");

        sequencer.Complete("phone", second, P, new byte[] { 2 }, 23);
        sequencer.Complete("phone", first, P, null, 23);           // request couldn't be read

        Assert.Equal(new[] { ("phone", (byte)2) }, _delivered);
    }

    [Fact]
    public void Clients_AreSequencedIndependently()
    {
        var sequencer = NewSequencer();
        long a1 = sequencer.Reserve("a");
        long b1 = sequencer.Reserve("b");

        sequencer.Complete("b", b1, P, new byte[] { 9 }, 23);       // b isn't held up by a's pending write

        Assert.Equal(new[] { ("b", (byte)9) }, _delivered);
        sequencer.Complete("a", a1, P, new byte[] { 1 }, 23);
        Assert.Equal(("a", (byte)1), _delivered[1]);
    }

    [Fact]
    public void Forget_DropsAClientsPendingState()
    {
        var sequencer = NewSequencer();
        sequencer.Reserve("phone");
        sequencer.Forget("phone");                                  // disconnected

        long next = sequencer.Reserve("phone");                     // reconnected
        sequencer.Complete("phone", next, P, new byte[] { 5 }, 23);

        Assert.Equal(new[] { ("phone", (byte)5) }, _delivered);
    }
}
