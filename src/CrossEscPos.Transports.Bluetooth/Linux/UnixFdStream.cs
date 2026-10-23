using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CrossEscPos.Transports.Bluetooth.Linux;

/// <summary>
/// A stream over a connected socket file descriptor that BlueZ handed us (an RFCOMM connection). Uses libc
/// read/write directly; reads block on a pool thread. Disposing shuts the socket down, which also wakes a
/// blocked read (it returns 0).
/// </summary>
internal sealed partial class UnixFdStream : Stream
{
    private const int EINTR = 4;
    private const int SHUT_RDWR = 2;

    private readonly SafeFileHandle _handle;
    private int _disposed;

    public UnixFdStream(SafeFileHandle handle) => _handle = handle;

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
                if (errno != EINTR)
                    throw new IOException($"write failed (errno {errno})");
            }
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint read(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint write(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int shutdown(int fd, int how);
}
