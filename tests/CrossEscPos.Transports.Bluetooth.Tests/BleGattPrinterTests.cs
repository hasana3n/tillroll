using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrossEscPos.Core.Tests;
using CrossEscPos.Emulator;
using CrossEscPos.Transports.Bluetooth.Ble;
using Xunit;

namespace CrossEscPos.Transports.Bluetooth.Tests;

public class BleGattPrinterTests
{
    private static readonly BlePrinterProfile P18F0 = BlePrinterProfiles.Generic18F0;
    private static readonly BlePrinterProfile PMicrochip = BlePrinterProfiles.MicrochipTransparentUart;

    /// <summary>Records every notification the engine sends.</summary>
    private sealed class RecordingNotifier : IBleNotifier
    {
        public ConcurrentQueue<(BlePrinterProfile Profile, string Client, byte[] Bytes)> Sent { get; } = new();

        public Task NotifyAsync(BlePrinterProfile profile, string clientId, ReadOnlyMemory<byte> chunk)
        {
            Sent.Enqueue((profile, clientId, chunk.ToArray()));
            return Task.CompletedTask;
        }
    }

    /// <summary>A stand-in printer: records fed bytes per responder and lets a test reply from inside a feed.</summary>
    private sealed class FakePrinter
    {
        public readonly ConcurrentDictionary<IPrinterResponder, List<byte>> Fed = new();
        public readonly ConcurrentBag<IPrinterResponder> Released = new();
        public readonly ConcurrentBag<IPrinterResponder> Registered = new();
        public readonly ConcurrentBag<IPrinterResponder> Unregistered = new();
        public Action<byte[], IPrinterResponder>? OnFeed;

        public BleGattPrinter CreateEngine(IBleNotifier notifier) => new(
            (bytes, responder) =>
            {
                var list = Fed.GetOrAdd(responder, _ => new List<byte>());
                lock (list) list.AddRange(bytes);
                OnFeed?.Invoke(bytes, responder);
            },
            Released.Add, Registered.Add, Unregistered.Add, notifier);

        public byte[] AllFed() => Fed.Values.SelectMany(l => { lock (l) return l.ToArray(); }).ToArray();
    }

    [Fact]
    public async Task Writes_AreFedInOrder_ForLargeJob()
    {
        var printer = new FakePrinter();
        await using var engine = printer.CreateEngine(new RecordingNotifier());
        var job = new byte[100_000];
        new Random(42).NextBytes(job);

        for (int offset = 0; offset < job.Length; offset += 20)
            engine.OnWrite("phone", P18F0, job.AsSpan(offset, Math.Min(20, job.Length - offset)), 23);
        await engine.DrainAsync("phone");

        Assert.Equal(job, printer.AllFed());
    }

    [Fact]
    public async Task Reply_IsNotifiedOnSameProfile_ChunkedByMtu()
    {
        var printer = new FakePrinter { OnFeed = (_, responder) => responder.Send(new byte[50]) };
        var notifier = new RecordingNotifier();
        await using var engine = printer.CreateEngine(notifier);

        engine.OnSubscribed("phone", P18F0, 23);
        engine.OnWrite("phone", P18F0, new byte[] { 1 }, 23);
        await engine.DrainAsync("phone");

        Assert.Equal(new[] { 20, 20, 10 }, notifier.Sent.Select(s => s.Bytes.Length));
        Assert.All(notifier.Sent, s => { Assert.Equal(P18F0, s.Profile); Assert.Equal("phone", s.Client); });
    }

    [Fact]
    public async Task Reply_BeforeSubscribe_IsDroppedWithoutThrowing()
    {
        var printer = new FakePrinter { OnFeed = (_, responder) => responder.Send(new byte[] { 0x16 }) };
        var notifier = new RecordingNotifier();
        await using var engine = printer.CreateEngine(notifier);

        engine.OnWrite("phone", P18F0, new byte[] { 0x10, 0x04, 0x01 }, 23);
        engine.OnWrite("phone", P18F0, "later"u8, 23);
        await engine.DrainAsync("phone");

        Assert.Empty(notifier.Sent);
        Assert.EndsWith("later", System.Text.Encoding.ASCII.GetString(printer.AllFed()));
    }

    [Fact]
    public async Task Subscribe_RegistersResponder_UnsubscribeUnregisters()
    {
        var printer = new FakePrinter();
        await using var engine = printer.CreateEngine(new RecordingNotifier());

        engine.OnSubscribed("phone", P18F0, 23);
        var registered = Assert.Single(printer.Registered);

        engine.OnUnsubscribed("phone", P18F0);

        Assert.Same(registered, Assert.Single(printer.Unregistered));
    }

    [Fact]
    public async Task Disconnect_ReleasesOwnership_AndClearsClient()
    {
        var printer = new FakePrinter();
        await using var engine = printer.CreateEngine(new RecordingNotifier());
        engine.OnWrite("phone", P18F0, "job"u8, 23);
        await engine.DrainAsync("phone");
        Assert.Equal(1, engine.ConnectedClients);
        var writer = Assert.Single(printer.Fed.Keys);

        engine.OnDisconnected("phone");

        Assert.Equal(0, engine.ConnectedClients);
        Assert.Contains(writer, printer.Released);
    }

    [Fact]
    public async Task Disconnect_StillFeedsBytesAlreadyReceived()
    {
        var printer = new FakePrinter();
        await using var engine = printer.CreateEngine(new RecordingNotifier());

        engine.OnWrite("phone", P18F0, "last chunk"u8, 23);
        engine.OnDisconnected("phone");                       // phones often disconnect right after the last write
        await engine.DisposeAsync();

        Assert.Equal("last chunk", System.Text.Encoding.ASCII.GetString(printer.AllFed()));
    }

    [Fact]
    public async Task TwoClients_HaveSeparateQueuesAndResponders()
    {
        var printer = new FakePrinter();
        await using var engine = printer.CreateEngine(new RecordingNotifier());

        engine.OnWrite("a", P18F0, "AAA"u8, 23);
        engine.OnWrite("b", PMicrochip, "BBB"u8, 23);
        await engine.DrainAsync("a");
        await engine.DrainAsync("b");

        Assert.Equal(2, engine.ConnectedClients);
        Assert.Equal(2, printer.Fed.Count);
        Assert.Contains(printer.Fed.Values, l => System.Text.Encoding.ASCII.GetString(l.ToArray()) == "AAA");
        Assert.Contains(printer.Fed.Values, l => System.Text.Encoding.ASCII.GetString(l.ToArray()) == "BBB");
    }

    [Fact]
    public async Task Reply_GoesToWildcardSubscriber_WhenWriterHasNoOwnSubscription()
    {
        // BlueZ's StartNotify doesn't say which device subscribed, so the Linux server subscribes "*".
        var printer = new FakePrinter { OnFeed = (_, responder) => responder.Send(new byte[] { 0x16 }) };
        var notifier = new RecordingNotifier();
        await using var engine = printer.CreateEngine(notifier);

        engine.OnSubscribed(BleGattPrinter.AnyClient, P18F0, 23);
        engine.OnWrite("/org/bluez/hci0/dev_AA", P18F0, new byte[] { 0x10, 0x04, 0x01 }, 23);
        await engine.DrainAsync("/org/bluez/hci0/dev_AA");
        await engine.DrainAsync(BleGattPrinter.AnyClient);

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(BleGattPrinter.AnyClient, sent.Client);
        Assert.Equal(new byte[] { 0x16 }, sent.Bytes);
    }

    [Fact]
    public async Task EndToEnd_WithRealPrinter_StatusQueryNotifiesOneByte()
    {
        var printer = new ReceiptPrinter(PaperConfiguration.Default, new FakeImageFactory(), new FakeTypefaceProvider());
        var notifier = new RecordingNotifier();
        await using var engine = new BleGattPrinter(printer, notifier);

        engine.OnSubscribed("phone", P18F0, 185);
        engine.OnWrite("phone", P18F0, new byte[] { 0x10, 0x04, 0x01 }, 185);
        await engine.DrainAsync("phone");

        var sent = Assert.Single(notifier.Sent);
        Assert.Single(sent.Bytes);
        Assert.Equal(P18F0, sent.Profile);
    }
}
