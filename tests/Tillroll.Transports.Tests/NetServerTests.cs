using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Tillroll.Transports.Tests;

public class NetServerTests : IDisposable
{
    private readonly NetServer _server;

    public NetServerTests()
    {
        _server = new NetServer(TestPrinter.Create(out _));
        Assert.True(_server.Start(IPAddress.Loopback, 0));
    }

    public void Dispose() => _server.Stop();

    private async Task<Socket> ConnectAsync()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, _server.BoundEndPoint!.Port);
        Assert.True(await TestPrinter.WaitUntil(() => _server.ClientCount == 1), $"ClientCount={_server.ClientCount}");
        return socket;
    }

    [Fact]
    public void BoundEndPoint_ReportsTheActualPort()
        => Assert.NotEqual(0, _server.BoundEndPoint!.Port);

    [Fact]
    public async Task ClientDisconnect_IsRemovedFromServer()
    {
        using var client = await ConnectAsync();
        client.Send(Encoding.Latin1.GetBytes("x\n"));
        client.Shutdown(SocketShutdown.Both);
        client.Close();

        Assert.True(await TestPrinter.WaitUntil(() => _server.ClientCount == 0), $"ClientCount={_server.ClientCount}");
    }

    [Fact]
    public async Task Stop_ClosesConnectedClients()
    {
        using var client = await ConnectAsync();

        _server.Stop();

        var buffer = new byte[16];
        var receive = client.ReceiveAsync(buffer, SocketFlags.None);
        Assert.Same(receive, await Task.WhenAny(receive, Task.Delay(2000)));
        Assert.Equal(0, await receive);            // orderly close from the server side
        Assert.Equal(0, _server.ClientCount);
    }

    [Fact]
    public async Task StatusQuery_GetsReplyOverTcp()
    {
        using var client = await ConnectAsync();
        client.Send(new byte[] { 0x10, 0x04, 0x01 });

        var buffer = new byte[16];
        var receive = client.ReceiveAsync(buffer, SocketFlags.None);
        Assert.Same(receive, await Task.WhenAny(receive, Task.Delay(2000)));
        Assert.Equal(1, await receive);
    }

    [Fact]
    public async Task Close_Twice_DoesNotThrow()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var accepted = await listener.AcceptSocketAsync();
            var netClient = new NetClient(_server, TestPrinter.Create(out _), accepted);

            netClient.Close();
            var second = Record.Exception(netClient.Close);

            Assert.Null(second);
        }
        finally
        {
            listener.Stop();
        }
    }
}
