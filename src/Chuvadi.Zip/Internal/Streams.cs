using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Zip.Internal;

/// <summary>Read-only window over a shared seekable stream. Every read re-seeks under a lock, so several
/// entry streams can be open on one archive at the same time.</summary>
internal sealed class SharedRangeStream : Stream
{
    private readonly Stream _base;
    private readonly object _sync;
    private readonly long _start;
    private readonly long _length;
    private long _pos;

    public SharedRangeStream(Stream baseStream, object sync, long start, long length)
    {
        _base = baseStream; _sync = sync; _start = start; _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public long Remaining => _length - _pos;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long remaining = _length - _pos;
        if (remaining <= 0 || buffer.Length == 0) return 0;
        if (buffer.Length > remaining) buffer = buffer.Slice(0, (int)remaining);
        int n;
        lock (_sync)
        {
            _base.Position = _start + _pos;
            n = _base.Read(buffer);
        }
        if (n == 0) throw new ZipIntegrityException("Unexpected end of archive data (the file is truncated).");
        _pos += n;
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Decrypts a ZipCrypto stream (read only).</summary>
internal sealed class ZipCryptoDecryptStream : Stream
{
    private readonly Stream _inner;
    private ZipCryptoKeys _keys;

    public ZipCryptoDecryptStream(Stream inner, ZipCryptoKeys keys) { _inner = inner; _keys = keys; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int n = _inner.Read(buffer);
        if (n > 0) _keys.Decrypt(buffer.Slice(0, n));
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Decrypts WinZip AES ciphertext and authenticates it with HMAC-SHA1 (checked in <see cref="Finish"/>).</summary>
internal sealed class AesDecryptStream : Stream
{
    private readonly Stream _inner;           // positioned at the first ciphertext byte; MAC follows the ciphertext
    private readonly long _cipherLength;
    private readonly AesCtr _ctr;
    private readonly IncrementalHash _mac;
    private readonly string _entryName;
    private long _read;
    private bool _verified;

    public AesDecryptStream(Stream inner, long cipherLength, byte[] encKey, byte[] authKey, string entryName)
    {
        _inner = inner; _cipherLength = cipherLength; _entryName = entryName;
        _ctr = new AesCtr(encKey);
        _mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authKey);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long remaining = _cipherLength - _read;
        if (remaining <= 0 || buffer.Length == 0) return 0;
        if (buffer.Length > remaining) buffer = buffer.Slice(0, (int)remaining);
        int n = _inner.Read(buffer);
        if (n == 0) return 0;
        _read += n;
        _mac.AppendData(buffer.Slice(0, n));
        _ctr.Transform(buffer.Slice(0, n));
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    /// <summary>Consumes any unread ciphertext, then verifies the 10-byte authentication code.</summary>
    public void Finish()
    {
        if (_verified) return;
        var scratch = new byte[4096];
        while (_read < _cipherLength)
        {
            long want = Math.Min(scratch.Length, _cipherLength - _read);
            int n = _inner.Read(scratch.AsSpan(0, (int)want));
            if (n == 0) break;
            _read += n;
            _mac.AppendData(scratch.AsSpan(0, n));
        }
        var stored = new byte[ZipAes.MacLength];
        int got = 0;
        while (got < stored.Length)
        {
            int n = _inner.Read(stored.AsSpan(got));
            if (n == 0) break;
            got += n;
        }
        byte[] computed = _mac.GetHashAndReset();
        if (got != stored.Length || !CryptographicOperations.FixedTimeEquals(stored, computed.AsSpan(0, ZipAes.MacLength)))
            throw new ZipIntegrityException(
                $"AES authentication failed for entry '{_entryName}': the data is corrupt or has been tampered with.", _entryName);
        _verified = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _ctr.Dispose(); _mac.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Outermost stream handed to callers of <c>OpenRead</c>. Enforces declared size, per-entry and total
/// limits against the bytes actually produced, verifies CRC-32 at end of data, and finishes AES
/// authentication.
/// </summary>
internal sealed class EntryReadStream : Stream
{
    private readonly Stream _src;
    private readonly ZipEntryInfo _entry;
    private readonly ZipReader _owner;
    private readonly AesDecryptStream? _aes;
    private readonly bool _checkCrc;
    private long _count;
    private uint _crc;
    private bool _done;

    public EntryReadStream(Stream decompressed, ZipEntryInfo entry, ZipReader owner, AesDecryptStream? aes, bool checkCrc)
    {
        _src = decompressed; _entry = entry; _owner = owner; _aes = aes; _checkCrc = checkCrc;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _entry.Size;
    public override long Position { get => _count; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_done || buffer.Length == 0) return 0;
        long declared = _entry.Size;
        // Read at most one byte beyond the declared size so an overlong stream is detected.
        int cap = (int)Math.Min(buffer.Length, declared - _count + 1);
        int n;
        try { n = _src.Read(buffer.Slice(0, cap)); }
        catch (InvalidDataException ex)
        {
            throw new ZipIntegrityException($"Entry '{_entry.Name}' contains corrupt compressed data: {ex.Message}", _entry.Name);
        }

        if (n > 0)
        {
            _count += n;
            if (_count > declared)
                throw new ZipIntegrityException(
                    $"Entry '{_entry.Name}' decompresses to more data than its declared size of {declared} bytes.", _entry.Name);
            _owner.Charge(n, _entry);
            if (_checkCrc) _crc = Crc32.Update(_crc, buffer.Slice(0, n));
            return n;
        }

        if (_count != declared)
            throw new ZipIntegrityException(
                $"Entry '{_entry.Name}' ended after {_count} bytes but declares {declared}.", _entry.Name);
        _aes?.Finish();
        if (_checkCrc && _crc != _entry.Crc32)
            throw new ZipIntegrityException(
                $"CRC-32 mismatch for entry '{_entry.Name}' (stored 0x{_entry.Crc32:X8}, computed 0x{_crc:X8}).", _entry.Name);
        _done = true;
        return 0;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _src.Dispose(); _aes?.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
