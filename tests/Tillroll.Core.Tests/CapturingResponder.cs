using System.Collections.Generic;

namespace Tillroll.Core.Tests;

/// <summary>Records every byte the printer sends back to this "host".</summary>
internal sealed class CapturingResponder : IPrinterResponder
{
    private readonly object _gate = new();
    private readonly List<byte> _bytes = new();

    public List<byte> Bytes
    {
        get { lock (_gate) return new List<byte>(_bytes); }
    }

    public void Send(byte[] data)
    {
        lock (_gate) _bytes.AddRange(data);
    }
}
