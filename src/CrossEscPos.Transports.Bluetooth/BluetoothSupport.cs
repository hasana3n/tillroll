namespace CrossEscPos.Transports.Bluetooth;

/// <summary>Why a Bluetooth printer server can or can't run on this machine.</summary>
public enum BluetoothSupportState
{
    Supported,
    NoAdapter,
    AdapterOff,
    NotSupportedByAdapter,
    PermissionDenied,
    UnsupportedPlatform,
    Error,
}

/// <summary>The result of starting a Bluetooth printer server, with a message fit for a status line.</summary>
public sealed record BluetoothSupport(BluetoothSupportState State, string Message)
{
    public bool IsSupported => State == BluetoothSupportState.Supported;

    public static BluetoothSupport Ok(string message) => new(BluetoothSupportState.Supported, message);
}
