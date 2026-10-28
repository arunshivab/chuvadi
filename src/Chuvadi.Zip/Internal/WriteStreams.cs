using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Zip.Internal;

/// <summary>Terminal stream: forwards to the archive output and counts compressed bytes.</summary>
internal sealed class OutputSink : Stream
{
    private readonly ZipWriter _writer;
    private readonly bool _zip64;
    public long Count { get; private set; }

    public OutputSink(ZipWriter writer, bool zip64) { _writer = writer; _zip64 = zip64; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => Count; set => throw new NotSupportedException(); }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;
        Count += buffer.Length;
        if (!_zip64 && Count >= ZipFormat.Max32)
            throw new ZipLimitExceededException(ZipLimit.Zip64Required, ZipFormat.Max32,
                "Entry exceeds 4 GiB compressed. Declare ExpectedSize or set ZipEntryOptions.Zip64 (and do not use Zip64Mode.Never).");
        _writer.WriteRaw(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>WinZip AES encryption layer. Writes salt + verifier up front and the 10-byte MAC at <see cref="Finish"/>.</summary>
internal sealed class AesEncryptStream : Stream
{
    private readonly Stream _next;
    private readonly AesCtr _ctr;
    private readonly IncrementalHash _mac;
    private readonly byte[] _scratch = new byte[16 * 1024];

    public AesEncryptStream(Stream next, byte[] password, int strength)
    {
        _next = next;
        int keyBytes = ZipAes.KeyBytes(strength);
        var salt = new byte[keyBytes / 2];
        RandomNumberGenerator.Fill(salt);
        ZipAes.Derive(password, salt, keyBytes, out var enc, out var auth, out var pwv);
        _ctr = new AesCtr(enc);
        _mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, auth);
        next.Write(salt, 0, salt.Length);
        next.Write(pwv, 0, pwv.Length);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            int n = Math.Min(buffer.Length, _scratch.Length);
            buffer.Slice(0, n).CopyTo(_scratch);
            _ctr.Transform(_scratch.AsSpan(0, n));
            _mac.AppendData(_scratch.AsSpan(0, n));
            _next.Write(_scratch, 0, n);
            buffer = buffer.Slice(n);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public void Finish()
    {
        var mac = _mac.GetHashAndReset();
        _next.Write(mac, 0, ZipAes.MacLength);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _ctr.Dispose(); _mac.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
