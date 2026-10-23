using System;
using System.Collections.Generic;

namespace CrossEscPos.Transports.Bluetooth.Ble;

/// <summary>
/// Keeps BLE writes in the order they arrived when the platform finishes reading them out of order. Windows
/// raises WriteRequested in order but each handler then awaits GetRequestAsync, and those complete on any
/// thread: a large job sent as many write-without-response packets would otherwise reach the printer
/// scrambled. Call <see cref="Reserve"/> synchronously when the event arrives, then <see cref="Complete"/> when
/// its data is known; writes are handed to the delivery callback strictly in reservation order, per client.
/// </summary>
internal sealed class BleWriteSequencer
{
    private readonly Action<string, BlePrinterProfile, byte[], int> _deliver;
    private readonly object _gate = new();
    private readonly Dictionary<string, ClientState> _clients = new();

    public BleWriteSequencer(Action<string, BlePrinterProfile, byte[], int> deliver) => _deliver = deliver;

    /// <summary>Takes the next ticket for <paramref name="clientId"/>. Call before any await in the event handler.</summary>
    public long Reserve(string clientId)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientId, out var state))
                _clients[clientId] = state = new ClientState();
            return state.NextTicket++;
        }
    }

    /// <summary>
    /// Reports the data for a ticket (null when the write couldn't be read). Delivers it and any later writes
    /// that were waiting for it, in order. Delivery happens under the sequencer's lock, so the callback must
    /// only enqueue (as <see cref="BleGattPrinter.OnWrite"/> does).
    /// </summary>
    public void Complete(string clientId, long ticket, BlePrinterProfile profile, byte[]? data, int attMtu)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientId, out var state) || ticket < state.NextToDeliver)
                return; // client forgotten (disconnected) or already handled

            state.Completed[ticket] = (profile, data, attMtu);
            while (state.Completed.Remove(state.NextToDeliver, out var write))
            {
                state.NextToDeliver++;
                if (write.Data is { Length: > 0 })
                    _deliver(clientId, write.Profile, write.Data, write.AttMtu);
            }
        }
    }

    /// <summary>Drops a disconnected client's state; writes still in flight for it are ignored.</summary>
    public void Forget(string clientId)
    {
        lock (_gate) _clients.Remove(clientId);
    }

    private sealed class ClientState
    {
        public long NextTicket;
        public long NextToDeliver;
        public readonly Dictionary<long, (BlePrinterProfile Profile, byte[]? Data, int AttMtu)> Completed = new();
    }
}
