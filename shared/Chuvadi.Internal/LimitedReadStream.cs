using System;
using System.IO;

namespace Chuvadi.Internal;

/// <summary>
/// Read-only stream wrapper that counts decompressed bytes flowing through it and throws
/// <see cref="InvalidDataException"/> once a caller-supplied budget is exceeded. Used to
/// protect readers against decompression bombs (small archives that expand enormously).
///
/// The limit applies to bytes READ through this wrapper, which for a DeflateStream source
/// equals the decompressed size — exactly the quantity an attacker inflates.
/// </summary>
internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _what;
    private long _read;

    /// <param name="inner">The stream to wrap. Disposed with this wrapper.</param>
    /// <param name="maxBytes">Maximum bytes that may be read before throwing.</param>
    /// <param name="what">Human-readable description used in the exception message
    /// (e.g. "package part 'xl/worksheets/sheet1.xml'" or "zip entry 'a/b.bin'").</param>
    public LimitedReadStream(Stream inner, long maxBytes, string what)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
        _what = what;
    }

    /// <summary>Total bytes read through the wrapper so far.</summary>
    public long BytesRead => _read;

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _inner.Read(buffer, offset, count);
        Account(n);
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        int n = _inner.Read(buffer);
        Account(n);
        return n;
    }

    public override int ReadByte()
    {
        int b = _inner.ReadByte();
        if (b >= 0) Account(1);
        return b;
    }

    private void Account(int n)
    {
        _read += n;
        if (_read > _maxBytes)
            throw new InvalidDataException(
                $"Decompressed size of {_what} exceeded the configured limit of {_maxBytes:N0} bytes. " +
                "The input may be a decompression bomb; raise the limit if the file is trusted.");
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
