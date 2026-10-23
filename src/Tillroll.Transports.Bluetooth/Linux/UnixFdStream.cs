using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Tillroll.Transports.Bluetooth.Linux;

/// <summary>
/// A stream over a connected socket file descriptor that BlueZ handed us (an RFCOMM connection). Uses libc
/// read/write directly; reads block on a pool thread. Disposing shuts the socket down, which also wakes a
/// blocked read (it returns 0).
/// </summary>
internal sealed partial class UnixFdStream : Stream
{
    private const int EINTR = 4;
    private const int EAGAIN = 11;
    private const short POLLIN = 0x001;
    private const short POLLOUT = 0x004;
    private const int SHUT_RDWR = 2;
    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_NONBLOCK = 0x800; // Linux (x86-64, arm64)

    private readonly SafeFileHandle _handle;
    private int _disposed;

    public UnixFdStream(SafeFileHandle handle)
    {
        _handle = handle;
        // bluetoothd hands the RFCOMM socket over in non-blocking mode (btio sets O_NONBLOCK on accepted
        // sockets). Our reads block on a pool thread, so switch it back; otherwise the first read with nothing
        // waiting fails with EAGAIN and the connection is dropped.
        int fd = (int)handle.DangerousGetHandle();
        int flags = fcntl(fd, F_GETFL, 0);
        if (flags >= 0 && (flags & O_NONBLOCK) != 0)
            _ = fcntl(fd, F_SETFL, flags & ~O_NONBLOCK);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return 0;

        // Hold a reference so the descriptor can't be closed (and its number reused) while a read is blocked
        // on it; Dispose shuts the socket down first, which makes the read return.
        bool added = false;
        try
        {
            _handle.DangerousAddRef(ref added);
            return ReadCore((int)_handle.DangerousGetHandle(), buffer);
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
        finally
        {
            if (added)
                _handle.DangerousRelease();
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(Task.Run(() => Read(buffer.Span), cancellationToken));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        bool added = false;
        try
        {
            _handle.DangerousAddRef(ref added); // throws ObjectDisposedException once closed
            WriteCore((int)_handle.DangerousGetHandle(), buffer);
        }
        finally
        {
            if (added)
                _handle.DangerousRelease();
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (!_handle.IsInvalid && !_handle.IsClosed)
                _ = shutdown((int)_handle.DangerousGetHandle(), SHUT_RDWR);
            _handle.Dispose(); // the descriptor closes once no read/write holds a reference
        }
        base.Dispose(disposing);
    }

    private unsafe int ReadCore(int fd, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            while (true)
            {
                nint n = read(fd, p, buffer.Length);
                if (n >= 0)
                    return (int)n;
                int errno = Marshal.GetLastPInvokeError();
                if (errno == EINTR)
                    continue;
                if (errno == EAGAIN && WaitUntilReady(fd, POLLIN))
                    continue; // still non-blocking after all: wait for data instead of failing
                if (Volatile.Read(ref _disposed) == 1)
                    return 0;
                throw new IOException($"read failed (errno {errno})");
            }
        }
    }

    private static unsafe void WriteCore(int fd, ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            int written = 0;
            while (written < buffer.Length)
            {
                nint n = write(fd, p + written, buffer.Length - written);
                if (n >= 0)
                {
                    written += (int)n;
                    continue;
                }
                int errno = Marshal.GetLastPInvokeError();
                if (errno == EINTR || (errno == EAGAIN && WaitUntilReady(fd, POLLOUT)))
                    continue;
                throw new IOException($"write failed (errno {errno})");
            }
        }
    }

    /// <summary>Blocks until the descriptor is readable/writable (or hung up). False if poll itself failed.</summary>
    private static unsafe bool WaitUntilReady(int fd, short events)
    {
        var request = new PollFd { Fd = fd, Events = events };
        while (true)
        {
            int result = poll(&request, 1, -1);
            if (result >= 0)
                return true;
            if (Marshal.GetLastPInvokeError() != EINTR)
                return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial int poll(PollFd* fds, nuint count, int timeout);

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint read(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint write(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int shutdown(int fd, int how);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int fcntl(int fd, int command, int argument);
}
