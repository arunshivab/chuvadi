using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip;

/// <summary>
/// Streaming zip writer: Zip64, AES-128/192/256 (WinZip AE-1/AE-2), comments, timestamps and attributes.
/// Entries are streamed with bounded memory; seekable outputs get their headers patched, non-seekable
/// outputs use data descriptors. No temporary files are ever created.
/// </summary>
public sealed partial class ZipWriter : IDisposable, IAsyncDisposable
{
    internal sealed class Rec
    {
        public byte[] Name = Array.Empty<byte>();
        public byte[] Comment = Array.Empty<byte>();
        public ushort Flags, MethodField, ActualMethod, VersionMade, VersionNeeded, DosTime, DosDate;
        public uint Crc;
        public long CSize, USize, Offset;
        public bool Zip64;
        public uint ExtAttr;
        public int AesStrength;
        public ZipAesVersion AesVersion;
        public int UnixTime;
        public bool WriteUt;
        public bool Descriptor;
    }

    private readonly Stream _out;
    private readonly bool _owns;
    private readonly ZipWriterOptions _options;
    private readonly bool _canSeek;
    private readonly long _start;
    private readonly List<Rec> _recs = new();
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private long _pos;
    private ZipEntryWriteStream? _active;
    private bool _finished, _faulted, _disposed;
    internal string? OutputFullPath;

    /// <summary>Archive comment, written at <see cref="Finish"/> (max 65535 bytes).</summary>
    public string? Comment { get; set; }
    /// <summary>Entries written so far.</summary>
    public int Count => _recs.Count;
    /// <summary>Bytes written to the output so far.</summary>
    public long BytesWritten => _pos;
    /// <summary>The effective options.</summary>
    public ZipWriterOptions Options => _options;

    private ZipWriter(Stream output, bool owns, ZipWriterOptions options)
    {
        _out = output; _owns = owns; _options = options;
        _canSeek = output.CanSeek;
        _start = _canSeek ? output.Position : 0;
        Comment = options.Comment;
    }

    // ---- Construction -------------------------------------------------------------

    /// <summary>Creates (or overwrites) an archive file.</summary>
    public static ZipWriter Create(string path, ZipWriterOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 81920);
        var w = new ZipWriter(fs, true, options ?? new ZipWriterOptions());
        w.OutputFullPath = Path.GetFullPath(path);
        return w;
    }

    /// <summary>Writes to a stream. Seekable streams are patched in place; others use data descriptors.</summary>
    public static ZipWriter Create(Stream output, ZipWriterOptions? options = null)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (!output.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(output));
        options ??= new ZipWriterOptions();
        return new ZipWriter(output, !options.LeaveOpen, options);
    }

    /// <summary>Builds an archive entirely in memory.</summary>
    public static byte[] ToBytes(Action<ZipWriter> build, ZipWriterOptions? options = null)
    {
        if (build is null) throw new ArgumentNullException(nameof(build));
        using var ms = new MemoryStream();
        using (var w = Create(ms, options))
            build(w);
        return ms.ToArray();
    }

    internal void WriteRaw(ReadOnlySpan<byte> data)
    {
        _out.Write(data);
        _pos += data.Length;
    }

    // ---- Entry creation -----------------------------------------------------------

    /// <summary>
    /// Starts a new file entry and returns a write-only stream; dispose it to finish the entry.
    /// Only one entry may be open at a time.
    /// </summary>
    public Stream CreateEntry(string name, ZipEntryOptions? entryOptions = null)
        => BeginEntry(name, entryOptions, directory: false);

    private ZipEntryWriteStream BeginEntry(string name, ZipEntryOptions? eo, bool directory)
    {
        EnsureUsable();
        if (_active is not null) throw new InvalidOperationException("Dispose the previous entry stream before starting another.");
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (!PathSafety.TryNormalize(name, out var norm, out var isDirName, out var err))
            throw new ArgumentException($"Invalid entry name '{name}': {err}", nameof(name));
        if (norm.Length == 0) throw new ArgumentException("Entry name is empty after normalisation.", nameof(name));
        if (directory && !norm.EndsWith('/')) norm += "/";
        if (!directory && norm.EndsWith('/'))
            throw new ArgumentException("A file entry name must not end with '/'. Use AddDirectory for directories.", nameof(name));

        var nameBytes = new UTF8Encoding(false).GetBytes(norm);
        if (nameBytes.Length > _options.MaxNameBytes || nameBytes.Length > 65535)
            throw new ArgumentException($"Entry name is {nameBytes.Length} bytes, exceeding the limit.", nameof(name));
        if (!_options.AllowDuplicateNames && !_names.Add(norm))
            throw new ArgumentException($"Duplicate entry name '{norm}'.", nameof(name));

        eo ??= new ZipEntryOptions();
        var rec = new Rec { Name = nameBytes, Descriptor = !_canSeek };

        // compression
        var method = directory ? ZipCompressionMethod.Stored : (eo.Method ?? _options.DefaultMethod);
        var level = eo.Level ?? _options.DefaultLevel;
        if (method == ZipCompressionMethod.Deflate && level == CompressionLevel.NoCompression) method = ZipCompressionMethod.Stored;
        rec.ActualMethod = (ushort)method;

        // encryption
        string? password = eo.Password ?? _options.Password;
        ZipEncryption enc = eo.Encryption ?? (_options.Encryption != ZipEncryption.None
            ? _options.Encryption
            : (password is not null ? ZipEncryption.Aes256 : ZipEncryption.None));
        if (directory) enc = ZipEncryption.None;
        if (enc == ZipEncryption.ZipCrypto)
            throw new ZipUnsupportedException("Writing ZipCrypto is not supported because it is cryptographically broken. Use Aes256.");
        if (enc != ZipEncryption.None && string.IsNullOrEmpty(password))
            throw new ArgumentException("A password is required for encrypted entries.");
        if (enc != ZipEncryption.None)
        {
            rec.AesStrength = ZipAes.StrengthFor(enc);
            rec.AesVersion = _options.AesVersion;
            rec.MethodField = ZipFormat.MethodAes;
            rec.Flags |= ZipFormat.FlagEncrypted;
        }
        else rec.MethodField = rec.ActualMethod;

        // zip64
        long expected = eo.ExpectedSize ?? -1;
        bool want64 = eo.Zip64 || _options.Zip64 == Zip64Mode.Always || (expected >= 0 && (ulong)expected >= ZipFormat.Zip64AutoThreshold);
        if (want64 && _options.Zip64 == Zip64Mode.Never)
            throw new ZipLimitExceededException(ZipLimit.Zip64Required, ZipFormat.Max32,
                "This entry requires Zip64 but Zip64Mode.Never is set.", norm);
        rec.Zip64 = want64;

        // flags / names / comment
        string commentText = eo.Comment ?? string.Empty;
        rec.Comment = new UTF8Encoding(false).GetBytes(commentText);
        if (rec.Comment.Length > 65535) throw new ArgumentException("Entry comment is longer than 65535 bytes.");
        if (!ZipFormat.IsAscii(norm) || !ZipFormat.IsAscii(commentText)) rec.Flags |= ZipFormat.FlagUtf8;
        if (rec.Descriptor) rec.Flags |= ZipFormat.FlagDataDescriptor;

        // time
        var when = eo.LastModified ?? _options.DefaultLastModified ?? DateTimeOffset.Now;
        (rec.DosDate, rec.DosTime) = ZipFormat.LocalToDos(when.LocalDateTime);
        long unix = when.ToUnixTimeSeconds();
        rec.WriteUt = _options.WriteExtendedTimestamps && unix >= int.MinValue && unix <= int.MaxValue;
        rec.UnixTime = rec.WriteUt ? (int)unix : 0;

        // attributes
        var host = _options.HostSystem;
        if (eo.ExternalAttributes is uint ea) rec.ExtAttr = ea;
        else if (host == ZipHostSystem.Unix || host == ZipHostSystem.MacOs)
        {
            int mode = ((eo.UnixMode ?? (directory ? 0x1ED : 0x1A4)) & 0xFFF) | (directory ? 0x4000 : 0x8000);
            rec.ExtAttr = ((uint)mode << 16) | (eo.DosAttributes ?? (directory ? (byte)0x10 : (byte)0));
        }
        else rec.ExtAttr = eo.DosAttributes ?? (directory ? (byte)0x10 : (byte)0x20);
        rec.VersionMade = (ushort)(((byte)host << 8) | 63);
        rec.VersionNeeded = (ushort)(enc != ZipEncryption.None ? 51 : rec.Zip64 ? 45 : rec.ActualMethod == 8 || directory ? 20 : 10);

        rec.Offset = _pos;
        WriteLocalHeader(rec);

        var s = new ZipEntryWriteStream(this, rec, method == ZipCompressionMethod.Deflate ? level : CompressionLevel.NoCompression,
            enc != ZipEncryption.None ? new UTF8Encoding(false).GetBytes(password!) : null);
        _active = s;
        return s;
    }

    private static void Put16(List<byte> b, ushort v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
    private static void Put32(List<byte> b, uint v) { Put16(b, (ushort)v); Put16(b, (ushort)(v >> 16)); }
    private static void Put64(List<byte> b, ulong v) { Put32(b, (uint)v); Put32(b, (uint)(v >> 32)); }

    private static void PutCommonExtras(List<byte> x, Rec r)
    {
        if (r.WriteUt) { Put16(x, ZipFormat.ExtraExtendedTimestamp); Put16(x, 5); x.Add(1); Put32(x, (uint)r.UnixTime); }
        if (r.AesStrength != 0)
        {
            Put16(x, ZipFormat.ExtraAes); Put16(x, 7);
            Put16(x, (ushort)(int)r.AesVersion);
            x.Add((byte)'A'); x.Add((byte)'E');
            x.Add((byte)r.AesStrength);
            Put16(x, r.ActualMethod);
        }
    }

    private void WriteLocalHeader(Rec r)
    {
        var extra = new List<byte>();
        if (r.Zip64) { Put16(extra, ZipFormat.ExtraZip64); Put16(extra, 16); Put64(extra, 0); Put64(extra, 0); }
        PutCommonExtras(extra, r);
        if (extra.Count > 65535) throw new InvalidOperationException("Extra field too large.");

        var h = new List<byte>(ZipFormat.LocalHeaderFixed + r.Name.Length + extra.Count);
        Put32(h, ZipFormat.LocalHeaderSig);
        Put16(h, r.VersionNeeded);
        Put16(h, r.Flags);
        Put16(h, r.MethodField);
        Put16(h, r.DosTime);
        Put16(h, r.DosDate);
        Put32(h, 0);                                   // crc (patched / descriptor)
        Put32(h, r.Zip64 ? ZipFormat.Max32 : 0);       // compressed size
        Put32(h, r.Zip64 ? ZipFormat.Max32 : 0);       // uncompressed size
        Put16(h, (ushort)r.Name.Length);
        Put16(h, (ushort)extra.Count);
        h.AddRange(r.Name);
        h.AddRange(extra);
        WriteRaw(h.ToArray());
    }

    internal void CompleteEntry(ZipEntryWriteStream stream, Rec rec, bool failed)
    {
        _active = null;
        if (failed) { _faulted = true; return; }

        if (rec.Descriptor)
        {
            var d = new List<byte>(24);
            Put32(d, ZipFormat.DataDescriptorSig);
            Put32(d, rec.Crc);
            if (rec.Zip64) { Put64(d, (ulong)rec.CSize); Put64(d, (ulong)rec.USize); }
            else { Put32(d, (uint)rec.CSize); Put32(d, (uint)rec.USize); }
            WriteRaw(d.ToArray());
        }
        else
        {
            long end = _out.Position;
            var p = new List<byte>(24);
            _out.Position = _start + rec.Offset + 14;
            Put32(p, rec.Crc);
            if (!rec.Zip64) { Put32(p, (uint)rec.CSize); Put32(p, (uint)rec.USize); }
            _out.Write(p.ToArray(), 0, p.Count);
            if (rec.Zip64)
            {
                var z = new List<byte>(16);
                Put64(z, (ulong)rec.USize); Put64(z, (ulong)rec.CSize);
                _out.Position = _start + rec.Offset + ZipFormat.LocalHeaderFixed + rec.Name.Length + 4;
                _out.Write(z.ToArray(), 0, z.Count);
            }
            _out.Position = end;
        }
        _recs.Add(rec);
    }

    internal bool CanSeekOutput => _canSeek;

    // ---- Finish -------------------------------------------------------------------

    /// <summary>Writes the central directory and completes the archive. Called by Dispose.</summary>
    public void Finish()
    {
        if (_finished) return;
        EnsureUsable();
        if (_active is not null) throw new InvalidOperationException("An entry stream is still open.");
        var mode = _options.Zip64;

        long cdStart = _pos;
        foreach (var r in _recs) WriteCentral(r, mode);
        long cdSize = _pos - cdStart;
        long count = _recs.Count;

        bool need64 = mode == Zip64Mode.Always || count >= ZipFormat.Max16 || cdStart >= ZipFormat.Max32 || cdSize >= ZipFormat.Max32;
        if (need64 && mode == Zip64Mode.Never)
            throw new ZipLimitExceededException(ZipLimit.Zip64Required, ZipFormat.Max32, "The archive requires Zip64 but Zip64Mode.Never is set.");

        if (need64)
        {
            long pos64 = _pos;
            var z = new List<byte>(76);
            Put32(z, ZipFormat.Zip64EndOfCentralDirSig);
            Put64(z, 44);
            Put16(z, 63); Put16(z, 45);
            Put32(z, 0); Put32(z, 0);
            Put64(z, (ulong)count); Put64(z, (ulong)count);
            Put64(z, (ulong)cdSize); Put64(z, (ulong)cdStart);
            Put32(z, ZipFormat.Zip64LocatorSig);
            Put32(z, 0); Put64(z, (ulong)pos64); Put32(z, 1);
            WriteRaw(z.ToArray());
        }

        var commentBytes = new UTF8Encoding(false).GetBytes(Comment ?? string.Empty);
        if (commentBytes.Length > 65535) throw new InvalidOperationException("Archive comment is longer than 65535 bytes.");
        var e = new List<byte>(22 + commentBytes.Length);
        Put32(e, ZipFormat.EndOfCentralDirSig);
        Put16(e, 0); Put16(e, 0);
        Put16(e, count >= ZipFormat.Max16 || need64 ? ZipFormat.Max16 : (ushort)count);
        Put16(e, count >= ZipFormat.Max16 || need64 ? ZipFormat.Max16 : (ushort)count);
        Put32(e, cdSize >= ZipFormat.Max32 || need64 ? ZipFormat.Max32 : (uint)cdSize);
        Put32(e, cdStart >= ZipFormat.Max32 || need64 ? ZipFormat.Max32 : (uint)cdStart);
        Put16(e, (ushort)commentBytes.Length);
        e.AddRange(commentBytes);
        WriteRaw(e.ToArray());
        _out.Flush();
        _finished = true;
    }

    private void WriteCentral(Rec r, Zip64Mode mode)
    {
        bool offset64 = r.Offset >= ZipFormat.Max32;
        if (offset64 && mode == Zip64Mode.Never)
            throw new ZipLimitExceededException(ZipLimit.Zip64Required, ZipFormat.Max32, "The archive requires Zip64 offsets but Zip64Mode.Never is set.");
        var extra = new List<byte>();
        if (r.Zip64 || offset64)
        {
            var body = new List<byte>();
            if (r.Zip64) { Put64(body, (ulong)r.USize); Put64(body, (ulong)r.CSize); }
            if (offset64) Put64(body, (ulong)r.Offset);
            Put16(extra, ZipFormat.ExtraZip64); Put16(extra, (ushort)body.Count); extra.AddRange(body);
        }
        PutCommonExtras(extra, r);

        var h = new List<byte>(ZipFormat.CentralHeaderFixed + r.Name.Length + extra.Count + r.Comment.Length);
        Put32(h, ZipFormat.CentralHeaderSig);
        Put16(h, r.VersionMade);
        Put16(h, r.VersionNeeded);
        Put16(h, r.Flags);
        Put16(h, r.MethodField);
        Put16(h, r.DosTime);
        Put16(h, r.DosDate);
        Put32(h, r.Crc);
        Put32(h, r.Zip64 ? ZipFormat.Max32 : (uint)r.CSize);
        Put32(h, r.Zip64 ? ZipFormat.Max32 : (uint)r.USize);
        Put16(h, (ushort)r.Name.Length);
        Put16(h, (ushort)extra.Count);
        Put16(h, (ushort)r.Comment.Length);
        Put16(h, 0);                                   // disk number start
        Put16(h, 0);                                   // internal attributes
        Put32(h, r.ExtAttr);
        Put32(h, offset64 ? ZipFormat.Max32 : (uint)r.Offset);
        h.AddRange(r.Name);
        h.AddRange(extra);
        h.AddRange(r.Comment);
        WriteRaw(h.ToArray());
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) throw new InvalidOperationException("The archive has already been finished.");
        if (_faulted) throw new InvalidOperationException("The writer is in a failed state after an earlier error.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (!_finished && !_faulted && _active is null) Finish();
        }
        finally
        {
            _disposed = true;
            _active?.Abandon();
            if (_owns) { try { _out.Dispose(); } catch { } }
        }
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    // ---- The entry stream ---------------------------------------------------------

    internal sealed class ZipEntryWriteStream : Stream
    {
        private readonly ZipWriter _w;
        private readonly Rec _r;
        private readonly OutputSink _sink;
        private readonly AesEncryptStream? _aes;
        private readonly DeflateStream? _deflate;
        private readonly Stream _next;
        private readonly Stream _belowDeflate;
        private uint _crc;
        private long _count;
        private bool _closed, _failed;

        public ZipEntryWriteStream(ZipWriter w, Rec r, CompressionLevel level, byte[]? password)
        {
            _w = w; _r = r;
            _sink = new OutputSink(w, r.Zip64);
            Stream next = _sink;
            if (password is not null)
            {
                _aes = new AesEncryptStream(next, password, r.AesStrength);
                next = _aes;
            }
            _belowDeflate = next;
            if (r.ActualMethod == 8)
            {
                _deflate = new DeflateStream(next, level, leaveOpen: true);
                next = _deflate;
            }
            _next = next;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_closed;
        public override long Length => _count;
        public override long Position { get => _count; set => throw new NotSupportedException(); }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (buffer.IsEmpty) return;
            try
            {
                _count += buffer.Length;
                if (!_r.Zip64 && _count >= ZipFormat.Max32)
                    throw new ZipLimitExceededException(ZipLimit.Zip64Required, ZipFormat.Max32,
                        "Entry exceeds 4 GiB uncompressed. Declare ExpectedSize or set ZipEntryOptions.Zip64.");
                _crc = Crc32.Update(_crc, buffer);
                _next.Write(buffer);
            }
            catch { _failed = true; throw; }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }

        internal void Abandon() { if (!_closed) { _closed = true; _failed = true; } }

        protected override void Dispose(bool disposing)
        {
            if (_closed) { base.Dispose(disposing); return; }
            _closed = true;
            if (disposing)
            {
                try
                {
                    if (!_failed)
                    {
                        if (_deflate is not null)
                        {
                            _deflate.Dispose();
                            // DeflateStream emits nothing when no byte was written; a valid raw-deflate stream
                            // must still contain a final (empty) block.
                            if (_count == 0) _belowDeflate.Write(new byte[] { 0x03, 0x00 }, 0, 2);
                        }
                        _aes?.Finish();
                        _r.CSize = _sink.Count;
                        _r.USize = _count;
                        _r.Crc = (_r.AesStrength != 0 && _r.AesVersion == ZipAesVersion.Ae2) ? 0 : _crc;
                    }
                }
                catch { _failed = true; throw; }
                finally
                {
                    _aes?.Dispose();
                    _w.CompleteEntry(this, _r, _failed);
                }
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
