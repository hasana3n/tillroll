using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace Tillroll.Transports.Bluetooth.Windows;

/// <summary>Checks that this PC's Bluetooth adapter can run a printer server, with a plain-language reason when not.</summary>
internal static class WindowsBluetoothProbe
{
    internal sealed record Result(BluetoothSupport Support, BluetoothAdapter? Adapter, Radio? Radio);

    public static async Task<Result> ProbeAsync(bool needClassic, bool needPeripheral)
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
                return Fail(BluetoothSupportState.NoAdapter, "No Bluetooth adapter found on this PC.");

            var radio = await adapter.GetRadioAsync();
            if (radio is null || radio.State != RadioState.On)
                return new Result(new BluetoothSupport(BluetoothSupportState.AdapterOff,
                    "Bluetooth is turned off in Windows settings."), adapter, radio);

            if (needClassic && !adapter.IsClassicSupported)
                return new Result(new BluetoothSupport(BluetoothSupportState.NotSupportedByAdapter,
                    "This PC's Bluetooth adapter doesn't support Classic Bluetooth (needed for the serial port)."), adapter, radio);

            if (needPeripheral && !adapter.IsPeripheralRoleSupported)
                return new Result(new BluetoothSupport(BluetoothSupportState.NotSupportedByAdapter,
                    "This PC's Bluetooth adapter can't act as a BLE peripheral, so iPhones can't connect to it. A different adapter is needed."), adapter, radio);

            return new Result(BluetoothSupport.Ok("Bluetooth is ready."), adapter, radio);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(BluetoothSupportState.PermissionDenied,
                "Windows denied access to Bluetooth. Allow it in Settings > Privacy & security > Radios.");
        }
        catch (Exception ex)
        {
            return Fail(BluetoothSupportState.Error, $"Couldn't query the Bluetooth adapter: {ex.Message}");
        }
    }

    private static Result Fail(BluetoothSupportState state, string message) => new(new BluetoothSupport(state, message), null, null);
}
