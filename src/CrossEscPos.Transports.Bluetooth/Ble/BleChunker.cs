using System;
using System.Collections.Generic;

namespace CrossEscPos.Transports.Bluetooth.Ble;

/// <summary>Splits a reply into BLE notification-sized pieces.</summary>
public static class BleChunker
{
    /// <summary>The ATT MTU every BLE link starts with.</summary>
    public const int DefaultAttMtu = 23;

    private const int AttHeaderBytes = 3;
    private const int MinimumChunk = DefaultAttMtu - AttHeaderBytes;

    /// <summary>
    /// Splits <paramref name="data"/> into chunks of (<paramref name="attMtu"/> − 3) bytes, never fewer than 20,
    /// in order. Empty input yields nothing.
    /// </summary>
    public static IEnumerable<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> data, int attMtu)
    {
        int chunk = Math.Max(MinimumChunk, attMtu - AttHeaderBytes);
        for (int offset = 0; offset < data.Length; offset += chunk)
            yield return data.Slice(offset, Math.Min(chunk, data.Length - offset));
    }
}
