using System;
using System.Linq;
using Tillroll.Transports.Bluetooth.Ble;
using Xunit;

namespace Tillroll.Transports.Bluetooth.Tests;

public class BleChunkerTests
{
    [Theory]
    [InlineData(23, 20)]
    [InlineData(185, 182)]
    [InlineData(517, 514)]
    [InlineData(0, 20)]
    public void Split_UsesMtuMinusThree_MinimumTwenty(int mtu, int expectedChunk)
    {
        var data = new byte[1000];
        var chunks = BleChunker.Split(data, mtu).ToList();
        Assert.All(chunks.SkipLast(1), c => Assert.Equal(expectedChunk, c.Length));
        Assert.Equal(1000, chunks.Sum(c => c.Length));
    }

    [Fact]
    public void Split_Empty_YieldsNothing() => Assert.Empty(BleChunker.Split(ReadOnlyMemory<byte>.Empty, 23));

    [Fact]
    public void Split_KeepsByteOrder()
    {
        var data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var joined = BleChunker.Split(data, 23).SelectMany(c => c.ToArray()).ToArray();
        Assert.Equal(data, joined);
    }
}
