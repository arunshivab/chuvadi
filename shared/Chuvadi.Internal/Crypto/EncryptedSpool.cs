using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// A seekable temporary spool for data that must not reach the disk in plaintext: the package
/// of an encrypted save, and (Chuvadi.Sheets) the per-sheet row spools of an encrypted export
/// (D-47, D-155, D-183). Shared by the format libraries since Chuvadi.Sheets 2.0.1.
/// <list type="bullet">
/// <item>Everything written is encrypted with AES-256 in counter mode under a one-time key and
/// nonce that exist only in this object's memory, so the plaintext package never reaches the disk.</item>
/// <item>The backing file is created owner-only (on Unix), opened exclusively and deleted when the
/// spool is disposed (FileOptions.DeleteOnClose). A file left by a crash holds only ciphertext
/// whose key is gone.</item>
/// <item>Counter mode keeps the stream seekable, so the package written into it is byte-for-byte
/// the package that would be written into a plain file (the zip writer can go back and fill in
/// each entry's header instead of appending data descriptors).</item>
/// </list>
/// </summary>
internal sealed class EncryptedSpool : Stream
{
    private const int BlockSize = 16;
    private const int ChunkSize = 64 * 1024;

    private readonly FileStream _file;
    private readonly Aes _aes;
    private readonly byte[] _nonce = new byte[8];
    private readonly byte[] _counters = new byte[ChunkSize + BlockSize];
    private readonly byte[] _keystream = new byte[ChunkSize + BlockSize];
    private readonly byte[] _work = new byte[ChunkSize];
    private long _position;
    private bool _disposed;

    /// <param name="directory">Folder for the backing file; null = <see cref="Path.GetTempPath"/>.</param>
    /// <param name="fileNamePrefix">Start of the backing file's name (a GUID and ".tmp" follow).</param>
    public EncryptedSpool(string? directory = null, string fileNamePrefix = "chuvadi_")
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose,
            BufferSize = ChunkSize,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var path = Path.Combine(directory ?? Path.GetTempPath(), $"{fileNamePrefix}{Guid.NewGuid():N}.tmp");
        _file = new FileStream(path, options);
        try
        {
            _aes = Aes.Create();
            _aes.KeySize = 256;
            _aes.GenerateKey();
            RandomNumberGenerator.Fill(_nonce);
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => !_disposed;

    public override long Length
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _file.Length; }
    }

    public override long Position
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _position; }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
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
        return _position;
    }

    public override void SetLength(long value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _file.SetLength(value);
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _file.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int total = 0;
        while (buffer.Length > 0)
        {
            int want = Math.Min(buffer.Length, ChunkSize);
            _file.Position = _position;
            int got = _file.Read(buffer[..want]);
            if (got == 0) break;
            XorKeystream(_position, buffer[..got]);
            _position += got;
            total += got;
            buffer = buffer[got..];
        }
        return total;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (buffer.Length > 0)
        {
            int n = Math.Min(buffer.Length, ChunkSize);
            var chunk = _work.AsSpan(0, n);
            buffer[..n].CopyTo(chunk);
            XorKeystream(_position, chunk);
            _file.Position = _position;
            _file.Write(chunk);
            _position += n;
            buffer = buffer[n..];
        }
    }

    public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 1 ? one[0] : -1;
    }

    /// <summary>XORs <paramref name="data"/> (at most one chunk) with the AES-CTR keystream at <paramref name="position"/>.</summary>
    private void XorKeystream(long position, Span<byte> data)
    {
        long firstBlock = position / BlockSize;
        int skip = (int)(position % BlockSize);
        int blocks = (skip + data.Length + BlockSize - 1) / BlockSize;
        var counters = _counters.AsSpan(0, blocks * BlockSize);
        for (int i = 0; i < blocks; i++)
        {
            var block = counters.Slice(i * BlockSize, BlockSize);
            _nonce.CopyTo(block);
            BinaryPrimitives.WriteInt64BigEndian(block[8..], firstBlock + i);
        }
        var keystream = _keystream.AsSpan(0, counters.Length);
        _aes.EncryptEcb(counters, keystream, PaddingMode.None);
        var ks = keystream.Slice(skip, data.Length);
        for (int i = 0; i < data.Length; i++) data[i] ^= ks[i];
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _aes.Dispose();
            CryptographicOperations.ZeroMemory(_nonce);
            CryptographicOperations.ZeroMemory(_work);
            CryptographicOperations.ZeroMemory(_keystream);
            _file.Dispose();   // DeleteOnClose removes the file
        }
        base.Dispose(disposing);
    }
}
