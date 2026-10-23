# Bluetooth

CrossEscPos can make a Windows or Linux computer look like a Bluetooth receipt printer, so a phone app can
pair with it, send ESC/POS, and get status replies back, the same way it would with a real thermal printer.

There are two kinds, because phones use two kinds of Bluetooth printer:

| | Classic **SPP** (serial port) | **BLE** (Bluetooth Low Energy) |
|---|---|---|
| Who uses it | Android printing apps and SDKs | iPhone apps (iOS can't use SPP without Apple MFi), and Android apps written for BLE printers |
| Connections panel row | **Bluetooth (Android / SPP)** | **Bluetooth LE (iPhone / BLE)** |
| Windows 10 2004+ / 11 | Yes | Yes, if the adapter supports the peripheral role (most built-in adapters do) |
| Linux (BlueZ 5.50+) | Yes | Yes |
| macOS, browser | Not yet | Not yet |

Both are **off until you click Start**. While SPP is running, the computer is discoverable.

## Starting it

1. Turn Bluetooth on (Windows: Settings > Bluetooth & devices; Linux: `bluetoothctl power on` or your desktop's
   Bluetooth settings).
2. Start the app. In **Connections**, set a **Printer name** if you like, and click **Start** on one or both
   Bluetooth rows. The status line says what's advertised, under which name, and how many phones are connected.
   If something's wrong (Bluetooth off, no adapter, an adapter that can't do BLE, missing permission) it says so
   there.

From the command line (useful for automated tests):

| Variable | Effect |
|---|---|
| `ESCPOS_BT_SPP=1` | Start the SPP row at launch. |
| `ESCPOS_BT_BLE=1` | Start the BLE row at launch. |
| `ESCPOS_BT_NAME=Kitchen` | Printer name (default `CrossEscPos`). |
| `ESCPOS_BT_AUTO_PAIR=1` | Linux only: accept pairing requests automatically (see below). |

```sh
ESCPOS_BT_SPP=1 ESCPOS_BT_BLE=1 dotnet run --project src/CrossEscPos.App.Desktop
```

### Windows

- **SPP:** pair the phone with the PC from the **phone's** Bluetooth settings (the PC shows up under the PC's
  name while SPP is running). Windows pops up "Add a device?"/a pairing code: accept it. Then your app connects
  to the paired PC with the SPP UUID.
- **BLE:** no pairing needed. Phones find it by scanning for service `18F0`. **Windows always advertises the
  computer's name over BLE** (the printer name isn't used there); the status line shows which name.
- The Windows build of the app targets `net10.0-windows10.0.19041.0`; if you build the library yourself, use that
  target to get the Windows servers.

### Linux

- `bluetoothd` must be running (`systemctl status bluetooth`) and your user needs access to BlueZ over D-Bus:
  on most distributions that means being in the `bluetooth` group (`sudo usermod -aG bluetooth $USER`, then log
  in again). Desktop sessions usually already have access.
- While a row is running, CrossEscPos powers the adapter and makes it discoverable and pairable with no timeout.
  When you stop it, it puts back whatever it changed.
- Pairing on a desktop shows your desktop's normal pairing prompt. On a headless machine (no pairing prompt at
  all), set `ESCPOS_BT_AUTO_PAIR=1`: CrossEscPos then registers a "Just Works" pairing agent that accepts
  pairings while it runs, and only authorizes connections to the printer's serial port. It never replaces a
  desktop's pairing agent unless you set this.
- If BLE says the adapter can't advertise, the controller doesn't support the LE peripheral role (or another
  program is using all advertising slots).

## What phones see

| Purpose | UUID |
|---|---|
| SPP (Classic) | `00001101-0000-1000-8000-00805F9B34FB` |
| **BLE service (advertised, recommended)** | `000018F0-0000-1000-8000-00805F9B34FB` |
| ↳ write ESC/POS here (write / write without response) | `00002AF1-0000-1000-8000-00805F9B34FB` |
| ↳ replies arrive here as notifications | `00002AF0-0000-1000-8000-00805F9B34FB` |
| BLE service used by many iOS printing SDKs | `E7810A71-73AE-499D-8C15-FAA9AEF0C3F2` |
| ↳ one characteristic for writes, notifications and reads | `BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F` |
| BLE service of Microchip/ISSC "transparent UART" printers | `49535343-FE7D-4AE5-8FA9-9FAFD205E455` |
| ↳ write | `49535343-8841-43F4-A8D4-ECBE34729BB3` |
| ↳ notify | `49535343-1E4D-4BD9-BA61-23C647249616` |

Replies (to `DLE EOT`, `GS r`, `GS I`, and Automatic Status Back once enabled with `GS a`) come back on the same
service the job was written to, split to fit the connection's MTU. A phone must **enable notifications** on the
notify characteristic before it can receive them; replies sent before that are dropped.

For your own app, use the `18F0` service: it's the one that's advertised and verified on both systems.

Several phones (or a phone and a TCP client) can be connected at once. A job from one of them owns the printer
until that phone goes quiet for a second or disconnects; the others wait, so receipts never get mixed. Status
queries (`DLE EOT`) are answered straight away even while waiting.

## Client code for your app

### Android: Classic SPP (Kotlin)

```kotlin
val SPP_UUID: UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")

// Call off the main thread. Needs BLUETOOTH_CONNECT (Android 12+). The PC must already be paired.
fun printOverSpp(context: Context, pcAddress: String): Int {
    val adapter = context.getSystemService(BluetoothManager::class.java).adapter
    val pc = adapter.getRemoteDevice(pcAddress)           // or pick it from adapter.bondedDevices by name
    adapter.cancelDiscovery()
    pc.createRfcommSocketToServiceRecord(SPP_UUID).use { socket ->
        socket.connect()
        val out = socket.outputStream
        out.write("Hello from Android\n\n\n".toByteArray(Charsets.ISO_8859_1))
        out.write(byteArrayOf(0x1D, 0x56, 0x00))            // GS V 0: cut
        out.write(byteArrayOf(0x10, 0x04, 0x01))            // DLE EOT 1: printer status
        out.flush()
        return socket.inputStream.read()                    // 0x12 = online, no errors
    }
}
```

Printer libraries that only list paired devices whose Bluetooth class is "printer" (for example DantSu's
ESCPOS-ThermalPrinter-Android `BluetoothPrintersConnections`) won't list a PC, because a PC reports itself as a
computer. Connect by address instead (with DantSu: `BluetoothConnection(adapter.getRemoteDevice(address))`).

### Android: BLE (Kotlin)

```kotlin
val SERVICE = UUID.fromString("000018F0-0000-1000-8000-00805F9B34FB")
val WRITE   = UUID.fromString("00002AF1-0000-1000-8000-00805F9B34FB")
val NOTIFY  = UUID.fromString("00002AF0-0000-1000-8000-00805F9B34FB")
val CCCD    = UUID.fromString("00002902-0000-1000-8000-00805F9B34FB")

class BlePrinter(private val job: ByteArray) : BluetoothGattCallback() {
    private var chunks = ArrayDeque<ByteArray>()

    override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
        if (newState == BluetoothProfile.STATE_CONNECTED) gatt.requestMtu(185)
    }
    override fun onMtuChanged(gatt: BluetoothGatt, mtu: Int, status: Int) {
        chunks = ArrayDeque(job.toList().chunked(mtu - 3).map { it.toByteArray() })
        gatt.discoverServices()
    }
    override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
        val notify = gatt.getService(SERVICE).getCharacteristic(NOTIFY)
        gatt.setCharacteristicNotification(notify, true)
        gatt.writeDescriptor(notify.getDescriptor(CCCD), BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
    }
    override fun onDescriptorWrite(gatt: BluetoothGatt, d: BluetoothGattDescriptor, status: Int) = writeNext(gatt)
    override fun onCharacteristicWrite(gatt: BluetoothGatt, c: BluetoothGattCharacteristic, status: Int) = writeNext(gatt)

    private fun writeNext(gatt: BluetoothGatt) {   // one GATT operation at a time
        val next = chunks.removeFirstOrNull() ?: return
        val write = gatt.getService(SERVICE).getCharacteristic(WRITE)
        gatt.writeCharacteristic(write, next, BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE)
    }
    override fun onCharacteristicChanged(gatt: BluetoothGatt, c: BluetoothGattCharacteristic, value: ByteArray) {
        Log.i("printer", "reply: ${value.joinToString(" ") { "%02X".format(it) }}")
    }
}

// device: from a BluetoothLeScanner scan filtered on ScanFilter.Builder().setServiceUuid(ParcelUuid(SERVICE))
// device.connectGatt(context, false, BlePrinter(jobBytes), BluetoothDevice.TRANSPORT_LE)
```

### iOS: BLE (Swift, CoreBluetooth)

Add `NSBluetoothAlwaysUsageDescription` to Info.plist.

```swift
import CoreBluetooth

final class BlePrinter: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private let service = CBUUID(string: "18F0")
    private let writeUUID = CBUUID(string: "2AF1")
    private let notifyUUID = CBUUID(string: "2AF0")
    private var central: CBCentralManager!
    private var printer: CBPeripheral?
    private var writeChar: CBCharacteristic?
    private var pending = Data()

    init(job: Data) {
        pending = job
        super.init()
        central = CBCentralManager(delegate: self, queue: nil)
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        if central.state == .poweredOn { central.scanForPeripherals(withServices: [service]) }
    }
    func centralManager(_ central: CBCentralManager, didDiscover p: CBPeripheral,
                        advertisementData: [String: Any], rssi: NSNumber) {
        printer = p
        central.stopScan()
        central.connect(p)
    }
    func centralManager(_ central: CBCentralManager, didConnect p: CBPeripheral) {
        p.delegate = self
        p.discoverServices([service])
    }
    func peripheral(_ p: CBPeripheral, didDiscoverServices error: Error?) {
        if let s = p.services?.first(where: { $0.uuid == service }) {
            p.discoverCharacteristics([writeUUID, notifyUUID], for: s)
        }
    }
    func peripheral(_ p: CBPeripheral, didDiscoverCharacteristicsFor s: CBService, error: Error?) {
        for c in s.characteristics ?? [] {
            if c.uuid == notifyUUID { p.setNotifyValue(true, for: c) }   // subscribe before writing
            if c.uuid == writeUUID { writeChar = c }
        }
    }
    func peripheral(_ p: CBPeripheral, didUpdateNotificationStateFor c: CBCharacteristic, error: Error?) {
        sendMore(p)
    }
    func peripheralIsReady(toSendWriteWithoutResponse p: CBPeripheral) { sendMore(p) }

    private func sendMore(_ p: CBPeripheral) {
        guard let c = writeChar else { return }
        let size = p.maximumWriteValueLength(for: .withoutResponse)
        while !pending.isEmpty && p.canSendWriteWithoutResponse {
            let chunk = pending.prefix(size)
            p.writeValue(chunk, for: c, type: .withoutResponse)
            pending.removeFirst(chunk.count)
        }
    }
    func peripheral(_ p: CBPeripheral, didUpdateValueFor c: CBCharacteristic, error: Error?) {
        if let v = c.value { print("reply:", v.map { String(format: "%02X", $0) }.joined(separator: " ")) }
    }
}

// var job = Data("Hello from iPhone\n\n\n".utf8)
// job.append(contentsOf: [0x1D, 0x56, 0x00, 0x10, 0x04, 0x01])   // cut, then DLE EOT 1
// let printer = BlePrinter(job: job)                              // keep a strong reference
```

## Test it with a phone (no code needed)

Use any generic Bluetooth tool. On Android: **Serial Bluetooth Terminal** (SPP) and **nRF Connect** (BLE). On
iPhone: **nRF Connect** or **LightBlue** (BLE). Send the bytes as **hex**.

| # | Do | Send (hex) | You should see |
|---|---|---|---|
| 1 | SPP: pair the phone with the PC, connect to it in the terminal app | `48 65 6C 6C 6F 0A 0A 0A 1D 56 00` | A "Hello" receipt with a cut appears in CrossEscPos; the SPP status says 1 connected |
| 2 | SPP: ask for the status | `10 04 01` | One byte back: `12` (online, no errors) |
| 3 | In **Printer state**, switch **Cover** to open, then ask for the offline status | `10 04 02` | `16` (cover open). A print sent now is dropped and a toast says the cover is open |
| 4 | BLE: scan, connect to the device advertising `18F0` (on Windows it has the PC's name), enable notifications on `2AF0` | | The BLE status says 1 connected |
| 5 | BLE: write to `2AF1` (write without response) | `48 65 6C 6C 6F 0A 0A 0A 1D 56 00` | A "Hello" receipt appears |
| 6 | BLE: write the status query to `2AF1` | `10 04 01` | A `12` notification on `2AF0` |
| 7 | BLE: also check that the `E7810A71…` and `49535343…` services are listed (nRF Connect shows all services) | | Windows registers them without advertising them; if they're missing on your PC, apps built for those services won't find the printer there (use `18F0`) |

## Limitations

- On Linux, BlueZ doesn't say which phone turned notifications on, so a reply goes to every phone subscribed to
  that characteristic. With one phone at a time (the usual case) this makes no difference.
- On Windows, the BLE name is always the computer's name, and only `18F0` is advertised. The two compatibility
  services can't be advertised alongside it (they don't fit in one advertisement); see step 7 above.
- A PC doesn't have the Bluetooth "printer" device class, so apps that filter paired devices by class won't
  list it (see the Android SPP note).
- macOS and the browser version can't act as a Bluetooth printer yet.

## Troubleshooting

- **The phone can't see the PC (SPP).** Start the SPP row first (that's what makes the PC discoverable), then
  scan from the phone's Bluetooth settings. On Windows, keep Bluetooth on; on Linux, check `rfkill list` isn't
  blocking it.
- **The iPhone can't find the BLE printer.** Scan for service `18F0` (not by name: Windows uses the PC's name). If
  the BLE status says the adapter can't act as a peripheral, that adapter can't do BLE printing; most built-in
  laptop adapters can, some USB dongles can't.
- **Connected, but no status replies over BLE.** Enable notifications on `2AF0` (or the shared characteristic)
  before sending the query.
- **Linux says access was denied.** Add your user to the `bluetooth` group and log in again.
- **Linux says bluetoothd isn't running.** `sudo systemctl enable --now bluetooth`.

## Using the library directly

```csharp
using CrossEscPos.Transports.Bluetooth;

var options = new BluetoothPrinterOptions { Name = "Kitchen" };
await using var spp = BluetoothPrinter.CreateSpp(printer, options);
await using var ble = BluetoothPrinter.CreateBle(printer, options);

spp.StatusChanged += () => Console.WriteLine(spp.StatusText);
var result = await spp.StartAsync();          // result.IsSupported, result.State, result.Message
await ble.StartAsync();
```

Reference `CrossEscPos.Transports.Bluetooth`. On Windows, target `net10.0-windows10.0.19041.0` (or later) to get
the WinRT servers; on Linux, plain `net10.0` uses BlueZ. Elsewhere the servers start with
`BluetoothSupportState.UnsupportedPlatform` and a message saying why.
