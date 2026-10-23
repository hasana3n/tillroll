using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tillroll.Emulator;
using Tillroll;
using Tillroll.Logging;

namespace Tillroll.Transports;

public class NetClient : IPrinterResponder
{
    public readonly NetServer Server;
    public readonly EndPoint RemoteEndPoint;

    private readonly ReceiptPrinter _printer;
    private Socket _socket;
    private CancellationTokenSource _lifetimeCts;

    public bool IsConnected => _socket.Connected;

    public NetClient(NetServer server, ReceiptPrinter printer, Socket clientSocket)
    {
        Server = server;
        _printer = printer;
        RemoteEndPoint = clientSocket.RemoteEndPoint!;

        _socket = clientSocket;
        _lifetimeCts = new();

        _printer.RegisterResponder(this); // allow status/transmit-back commands to reply to us
    }

    /// <summary>Raised once, when the connection closes (by either side).</summary>
    internal event Action<NetClient>? Closed;

    private int _closed;

    /// <summary>Closes the connection. Safe to call more than once and from any thread; never throws.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;

        _lifetimeCts.Cancel();
        _printer.UnregisterResponder(this); // also releases this client's hold on the printer

        try { _socket.Shutdown(SocketShutdown.Both); }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { /* already gone */ }
        _socket.Close();

        Logger.Info($"Closed client connection {RemoteEndPoint}");
        Closed?.Invoke(this);
    }

    /// <summary>Writes a printer response (status bytes, printer ID, …) back to this client.</summary>
    public void Send(byte[] data)
    {
        try
        {
            if (_socket.Connected)
                _socket.Send(data);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"Failed to send {data.Length} bytes to {RemoteEndPoint}");
        }
    }

    public async Task ReceiveLoopAsync()
    {
        try
        {
            var receiveBuffer = GC.AllocateArray<byte>(1024000, true);
            var bufferMemory = receiveBuffer.AsMemory();

            while (!_lifetimeCts.Token.IsCancellationRequested)
            {
                var byteCount = await _socket.ReceiveAsync(bufferMemory, SocketFlags.None);

                if (byteCount <= 0)
                {
                    Close();
                    return;
                }

                Logger.Info($"Received TCP data (byteCount={byteCount}, RemoteEndPoint={RemoteEndPoint})");

                HandleIncomingData(bufferMemory.Span[..byteCount]);
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _closed) == 0) // errors after Close() are just the socket going away
                Logger.Exception(ex, "Receive error");
            Close();
        }
    }

    private void HandleIncomingData(ReadOnlySpan<byte> data) =>
        _printer.FeedEscPos(data, this);
}
