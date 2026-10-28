using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Read-only decorator for a decompressing part stream: checks a <see cref="CancellationToken"/>
/// before every read and enforces <see cref="DocxReaderOptions.MaxPartBytes"/>. Checking the
/// token per buffer fill bounds cancellation latency even inside a single huge XML text node,
/// where the per-node check in <see cref="GuardedXmlReader"/> would not fire.
/// </summary>
internal sealed class GuardedPartStream : Stream
{
    private readonly Stream _inner;
    private readonly long? _maxBytes;
    private readonly CancellationToken _ct;
    private readonly string _partName;
    private long _read;

    private GuardedPartStream(Stream inner, long? maxBytes, CancellationToken ct, string partName)
    {
        _inner = inner;
        _maxBytes = maxBytes;
        _ct = ct;
        _partName = partName;
    }

    /// <summary>Wraps only when there is something to guard (zero cost otherwise).</summary>
    public static Stream Wrap(Stream inner, long? maxBytes, CancellationToken ct, string partName)
        => maxBytes is null && !ct.CanBeCanceled ? inner : new GuardedPartStream(inner, maxBytes, ct, partName);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        _ct.ThrowIfCancellationRequested();
        int n = _inner.Read(buffer);
        Account(n);
        return n;
    }

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 0 ? -1 : one[0];
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _ct.ThrowIfCancellationRequested();
        int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Account(n);
        return n;
    }

    private void Account(int n)
    {
        _read += n;
        if (_maxBytes is long max && _read > max)
            throw new DocxSecurityLimitException(DocxLimit.PartBytes, _read, max, _partName);
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
