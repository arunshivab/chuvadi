using System;
using System.IO;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// A write view of a seekable stream that starts at the stream's current position: position 0 of
/// the view is that position. Lets a writer that seeks to 0 (the compound-file writer of an
/// encrypted save) write after data the caller already put in the stream. Not closing.
/// </summary>
internal sealed class OffsetStream : Stream
{
    private readonly Stream _inner;
    private readonly long _base;

    public OffsetStream(Stream inner)
    {
        _inner = inner;
        _base = inner.Position;
    }

    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => Math.Max(0, _inner.Length - _base);

    public override long Position
    {
        get => _inner.Position - _base;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _inner.Position = _base + value;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        Position = target;
        return target;
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void SetLength(long value) => _inner.SetLength(_base + value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void WriteByte(byte value) => _inner.WriteByte(value);
}
