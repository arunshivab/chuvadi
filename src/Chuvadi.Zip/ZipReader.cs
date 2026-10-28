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
/// Random-access zip reader with Zip64, AES-256/192/128 (WinZip AE-1/AE-2), legacy ZipCrypto,
/// CRC verification, zip-slip protection and zip-bomb limits. Works entirely in memory/streams.
/// </summary>
public sealed partial class ZipReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly object _sync = new();
    private readonly ZipReaderOptions _options;
    private readonly Encoding _nameEncoding;
    private readonly List<ZipEntryInfo> _entries = new();
    private readonly Dictionary<string, ZipEntryInfo> _byName = new(StringComparer.Ordinal);
    private long _total;
    private long _length;
    private bool _disposed;

    /// <summary>Archive comment.</summary>
    public string Comment { get; private set; } = string.Empty;
    /// <summary>True when the archive uses Zip64 structures.</summary>
    public bool IsZip64 { get; private set; }
    /// <summary>Bytes of data preceding the archive (self-extractor stub etc.).</summary>
    public long PrefixLength { get; private set; }
    /// <summary>All entries in central-directory order.</summary>
    public IReadOnlyList<ZipEntryInfo> Entries => _entries;
    /// <summary>Number of entries.</summary>
    public int Count => _entries.Count;
    /// <summary>Total decompressed bytes produced by this reader so far (counted against MaxTotalBytes).</summary>
    public long TotalBytesRead => Interlocked.Read(ref _total);

    private ZipReader(Stream stream, bool owns, ZipReaderOptions options)
    {
        _stream = stream; _ownsStream = owns; _options = options;
        _nameEncoding = options.NameEncoding ?? CodePagesEncodingProvider.Instance.GetEncoding(437)!;
        _length = stream.Length;
    }

    // ---- Construction -------------------------------------------------------------

    /// <summary>Opens an archive file.</summary>
    public static ZipReader Open(string path, ZipReaderOptions? options = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Create(fs, true, options ?? new ZipReaderOptions());
    }

    /// <summary>Opens an archive in memory.</summary>
    public static ZipReader Open(byte[] data, ZipReaderOptions? options = null)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        return Create(new MemoryStream(data, writable: false), true, options ?? new ZipReaderOptions());
    }

    /// <summary>Opens an archive in memory.</summary>
    public static ZipReader Open(ReadOnlyMemory<byte> data, ZipReaderOptions? options = null)
        => Open(data.ToArray(), options);

    /// <summary>
    /// Opens an archive from a stream. A seekable stream is used directly; a non-seekable one is
    /// buffered into memory (never to disk) up to <see cref="ZipReaderOptions.MaxBufferedBytes"/>.
    /// </summary>
    public static ZipReader Open(Stream input, ZipReaderOptions? options = null)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        options ??= new ZipReaderOptions();
        if (input.CanSeek && input.CanRead)
            return Create(input, owns: !options.LeaveOpen, options);
        var ms = BufferToMemory(input, options.MaxBufferedBytes);
        if (!options.LeaveOpen) input.Dispose();
        return Create(ms, true, options);
    }

    /// <summary>Async open; buffers a non-seekable input with cancellation support.</summary>
    public static async Task<ZipReader> OpenAsync(Stream input, ZipReaderOptions? options = null, CancellationToken ct = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        options ??= new ZipReaderOptions();
        if (input.CanSeek && input.CanRead)
            return Create(input, owns: !options.LeaveOpen, options);
        var ms = await BufferToMemoryAsync(input, options.MaxBufferedBytes, ct).ConfigureAwait(false);
        if (!options.LeaveOpen) await input.DisposeAsync().ConfigureAwait(false);
        return Create(ms, true, options);
    }

    private static ZipReader Create(Stream s, bool owns, ZipReaderOptions options)
    {
        var r = new ZipReader(s, owns, options);
        try { r.ReadDirectory(); }
        catch { r.Dispose(); throw; }
        return r;
    }

    private static MemoryStream BufferToMemory(Stream input, long max)
    {
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = input.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > max)
                throw new ZipLimitExceededException(ZipLimit.BufferedBytes, max,
                    $"Non-seekable input exceeds the in-memory buffering limit of {max} bytes.");
            ms.Write(buf, 0, n);
        }
        return ms;
    }

    private static async Task<MemoryStream> BufferToMemoryAsync(Stream input, long max, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await input.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > max)
                throw new ZipLimitExceededException(ZipLimit.BufferedBytes, max,
                    $"Non-seekable input exceeds the in-memory buffering limit of {max} bytes.");
            ms.Write(buf, 0, n);
        }
        return ms;
    }

    // ---- Central directory --------------------------------------------------------

    private void ReadExactAt(long pos, Span<byte> dest)
    {
        _stream.Position = pos;
        int got = 0;
        while (got < dest.Length)
        {
            int n = _stream.Read(dest.Slice(got));
            if (n == 0) throw new ZipFormatException("Unexpected end of archive.");
            got += n;
        }
    }

    private void ReadDirectory()
    {
        var limits = _options.Limits;
        long len = _length;
        if (len < ZipFormat.EocdFixed) throw new ZipFormatException("Not a zip archive (file too small).");

        int scan = (int)Math.Min(len, ZipFormat.EocdFixed + 65535L);
        var tail = new byte[scan];
        ReadExactAt(len - scan, tail);
        int found = -1;
        for (int i = scan - ZipFormat.EocdFixed; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != ZipFormat.EndOfCentralDirSig) continue;
            int cl = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20));
            if (i + ZipFormat.EocdFixed + cl <= scan) { found = i; break; }
        }
        if (found < 0) throw new ZipFormatException("Not a zip archive (end of central directory not found).");

        long eocdPos = len - scan + found;
        var e = tail.AsSpan(found);
        ushort diskNo = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(4));
        ushort cdDisk = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(6));
        ulong total = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(10));
        ulong cdSize = BinaryPrimitives.ReadUInt32LittleEndian(e.Slice(12));
        ulong cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(e.Slice(16));
        int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(20));
        Comment = DecodeLoose(e.Slice(22, commentLen));

        if (diskNo != 0 && diskNo != ZipFormat.Max16 || cdDisk != 0 && cdDisk != ZipFormat.Max16)
            throw new ZipUnsupportedException("Split / multi-disk archives are not supported.");

        bool sentinel = total == ZipFormat.Max16 || cdSize == ZipFormat.Max32 || cdOffset == ZipFormat.Max32;
        long cdEnd = eocdPos;
        long locPos = eocdPos - ZipFormat.Zip64LocatorSize;
        bool haveLocator = false;
        if (locPos >= 0)
        {
            var loc = new byte[ZipFormat.Zip64LocatorSize];
            ReadExactAt(locPos, loc);
            haveLocator = BinaryPrimitives.ReadUInt32LittleEndian(loc) == ZipFormat.Zip64LocatorSig;
            if (haveLocator)
            {
                uint totalDisks = BinaryPrimitives.ReadUInt32LittleEndian(loc.AsSpan(16));
                if (totalDisks > 1) throw new ZipUnsupportedException("Split / multi-disk archives are not supported.");
                ulong off64 = BinaryPrimitives.ReadUInt64LittleEndian(loc.AsSpan(8));
                long pos64 = -1;
                var rec = new byte[ZipFormat.Zip64EocdFixed];
                foreach (long cand in new[] { off64 > (ulong)long.MaxValue ? -1L : (long)off64, locPos - ZipFormat.Zip64EocdFixed })
                {
                    if (cand < 0 || cand + ZipFormat.Zip64EocdFixed > len) continue;
                    ReadExactAt(cand, rec);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(rec) == ZipFormat.Zip64EndOfCentralDirSig) { pos64 = cand; break; }
                }
                if (pos64 < 0) throw new ZipFormatException("Zip64 end-of-central-directory record not found.");
                total = BinaryPrimitives.ReadUInt64LittleEndian(rec.AsSpan(32));
                cdSize = BinaryPrimitives.ReadUInt64LittleEndian(rec.AsSpan(40));
                cdOffset = BinaryPrimitives.ReadUInt64LittleEndian(rec.AsSpan(48));
                cdEnd = pos64;
                IsZip64 = true;
            }
        }
        if (sentinel && !haveLocator)
            throw new ZipFormatException("Archive uses Zip64 sentinel values but has no Zip64 end-of-central-directory locator.");

        if (cdSize > (ulong)len || cdOffset > (ulong)len || total > (ulong)len)
            throw new ZipFormatException("Central directory fields are out of range.");

        if (limits.MaxCentralDirectoryBytes is long maxCd && (long)cdSize > maxCd)
            throw new ZipLimitExceededException(ZipLimit.CentralDirectoryBytes, maxCd,
                $"Central directory is {cdSize} bytes, exceeding the limit of {maxCd}.");
        limits.ValidateEntryCount((long)total);
        if (total * ZipFormat.CentralHeaderFixed > cdSize)
            throw new ZipFormatException("Entry count is inconsistent with the central directory size.");
        if (cdSize > int.MaxValue - 64) throw new ZipUnsupportedException("Central directory is too large.");

        long cdStart = cdEnd - (long)cdSize;
        long prefix = cdStart - (long)cdOffset;
        if (cdStart < 0 || prefix < 0) throw new ZipFormatException("Central directory offset is out of range.");
        PrefixLength = prefix;

        var cd = new byte[(int)cdSize];
        ReadExactAt(cdStart, cd);
        ParseEntries(cd, (int)total, prefix);
        if (_options.DetectOverlappingEntries) CheckOverlap(cdStart);
    }

    private string DecodeLoose(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty) return string.Empty;
        try { return new UTF8Encoding(false, true).GetString(raw); }
        catch (ArgumentException) { return _nameEncoding.GetString(raw); }
    }

    private void ParseEntries(byte[] cd, int count, long prefix)
    {
        var limits = _options.Limits;
        int p = 0;
        var utf8 = new UTF8Encoding(false);
        for (int i = 0; i < count; i++)
        {
            if (p + ZipFormat.CentralHeaderFixed > cd.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p)) != ZipFormat.CentralHeaderSig)
                throw new ZipFormatException($"Central directory entry {i} is missing or corrupt.");
            var h = cd.AsSpan(p);
            ushort versionMade = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(4));
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(8));
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(10));
            ushort dtime = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(12));
            ushort ddate = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(14));
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(16));
            ulong csize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(20));
            ulong usize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(24));
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(28));
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(30));
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(32));
            ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(34));
            uint extAttr = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(38));
            ulong offset = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(42));

            int need = ZipFormat.CentralHeaderFixed + nameLen + extraLen + commentLen;
            if (p + need > cd.Length) throw new ZipFormatException($"Central directory entry {i} is truncated.");
            var rawName = cd.AsSpan(p + 46, nameLen).ToArray();
            var extra = cd.AsSpan(p + 46 + nameLen, extraLen);
            var rawComment = cd.AsSpan(p + 46 + nameLen + extraLen, commentLen);
            p += need;

            bool zip64 = false;
            if (ZipFormat.TryGetExtra(extra, ZipFormat.ExtraZip64, out var z))
            {
                int zp = 0;
                byte[] za = z.ToArray();
                bool Take(ref ulong v)
                {
                    if (zp + 8 > za.Length) return false;
                    v = BinaryPrimitives.ReadUInt64LittleEndian(za.AsSpan(zp)); zp += 8; return true;
                }
                if (usize == ZipFormat.Max32) { if (!Take(ref usize)) throw new ZipFormatException($"Entry {i}: bad Zip64 extra."); zip64 = true; }
                if (csize == ZipFormat.Max32) { if (!Take(ref csize)) throw new ZipFormatException($"Entry {i}: bad Zip64 extra."); zip64 = true; }
                if (offset == ZipFormat.Max32) { if (!Take(ref offset)) throw new ZipFormatException($"Entry {i}: bad Zip64 extra."); zip64 = true; }
                if (diskStart == ZipFormat.Max16 && zp + 4 <= za.Length) { diskStart = 0; }
            }
            else if (usize == ZipFormat.Max32 || csize == ZipFormat.Max32 || offset == ZipFormat.Max32)
            {
                throw new ZipFormatException($"Entry {i}: Zip64 sentinel without Zip64 extra field.");
            }
            if (diskStart != 0) throw new ZipUnsupportedException("Split / multi-disk archives are not supported.");
            if (usize > long.MaxValue || csize > long.MaxValue || offset > (ulong)long.MaxValue)
                throw new ZipFormatException($"Entry {i}: sizes out of range.");
            if (zip64) IsZip64 = true;

            // name
            string name;
            bool utf8Name = (flags & ZipFormat.FlagUtf8) != 0;
            if (utf8Name) name = utf8.GetString(rawName);
            else if (ZipFormat.TryGetExtra(extra, ZipFormat.ExtraUnicodePath, out var up) && up.Length >= 5 && up[0] == 1 &&
                     BinaryPrimitives.ReadUInt32LittleEndian(up.Slice(1)) == Crc32.Update(0, rawName))
            { name = utf8.GetString(up.Slice(5)); utf8Name = true; }
            else name = _nameEncoding.GetString(rawName);

            string comment = (flags & ZipFormat.FlagUtf8) != 0 ? utf8.GetString(rawComment) : _nameEncoding.GetString(rawComment);

            if (limits.MaxNameLength is int mnl && name.Length > mnl)
                throw new ZipLimitExceededException(ZipLimit.NameLength, mnl, $"Entry name is {name.Length} characters, exceeding the limit of {mnl}.", name);

            // encryption / AES
            var enc = ZipEncryption.None;
            ZipAesVersion? aesVer = null;
            int strength = 0;
            ushort actualMethod = method;
            if ((flags & ZipFormat.FlagEncrypted) != 0)
            {
                if (method == ZipFormat.MethodAes)
                {
                    if (!ZipFormat.TryGetExtra(extra, ZipFormat.ExtraAes, out var a) || a.Length < 7)
                        throw new ZipFormatException($"Entry '{name}': AES entry without a valid 0x9901 extra field.");
                    int ver = BinaryPrimitives.ReadUInt16LittleEndian(a);
                    if (ver != 1 && ver != 2) throw new ZipUnsupportedException($"Entry '{name}': unsupported AES version {ver}.");
                    strength = a[4];
                    enc = ZipAes.EncryptionFor(strength);
                    aesVer = ver == 1 ? ZipAesVersion.Ae1 : ZipAesVersion.Ae2;
                    actualMethod = BinaryPrimitives.ReadUInt16LittleEndian(a.Slice(5));
                }
                else enc = ZipEncryption.ZipCrypto;
            }

            // timestamp
            DateTimeOffset modified;
            var local = ZipFormat.DosToLocal(ddate, dtime);
            if (ZipFormat.TryGetExtra(extra, ZipFormat.ExtraExtendedTimestamp, out var ts) && ts.Length >= 5 && (ts[0] & 1) != 0)
                modified = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt32LittleEndian(ts.Slice(1)));
            else
                modified = new DateTimeOffset(local);

            int hostCode = versionMade >> 8;
            var host = hostCode switch { 0 => ZipHostSystem.Dos, 3 => ZipHostSystem.Unix, 10 => ZipHostSystem.Ntfs, 19 => ZipHostSystem.MacOs, _ => ZipHostSystem.Other };
            int? unixMode = (host == ZipHostSystem.Unix || host == ZipHostSystem.MacOs) ? (int)(extAttr >> 16) : null;

            bool isDir = name.EndsWith('/') || name.EndsWith('\\');

            var info = new ZipEntryInfo
            {
                Owner = this,
                RawName = rawName,
                HeaderOffset = (long)offset + prefix,
                Flags = flags,
                RawMethodField = method,
                VersionMadeBy = versionMade,
                DosTime = dtime,
                DosDate = ddate,
                AesStrength = strength,
                Name = name,
                Index = i,
                Size = (long)usize,
                CompressedSize = (long)csize,
                Crc32 = crc,
                RawMethod = actualMethod,
                Encryption = enc,
                AesVersion = aesVer,
                IsZip64 = zip64,
                IsDirectory = isDir,
                LastModified = modified,
                Comment = comment,
                HostSystem = host,
                ExternalAttributes = extAttr,
                UnixMode = unixMode,
                IsUtf8Name = utf8Name,
            };

            if (PathSafety.TryNormalize(name, out var norm, out _, out var err))
            {
                info.NormalizedName = norm;
                if (limits.MaxPathDepth is int md && PathSafety.Depth(norm) > md)
                    throw new ZipLimitExceededException(ZipLimit.PathDepth, md, $"Entry '{name}' is nested deeper than {md} levels.", name);
            }
            else info.UnsafeReason = err;

            _entries.Add(info);
            _byName.TryAdd(name, info);
        }
    }

    private void CheckOverlap(long cdStart)
    {
        var sorted = new List<ZipEntryInfo>(_entries);
        sorted.Sort((a, b) => a.HeaderOffset.CompareTo(b.HeaderOffset));
        for (int i = 0; i < sorted.Count; i++)
        {
            var en = sorted[i];
            long minEnd = en.HeaderOffset + ZipFormat.LocalHeaderFixed + en.RawName.Length + en.CompressedSize;
            long limit = i + 1 < sorted.Count ? sorted[i + 1].HeaderOffset : cdStart;
            if (en.HeaderOffset < 0 || minEnd > limit)
                throw new ZipIntegrityException($"Entry '{en.Name}' overlaps another entry or the central directory (possible zip bomb).", en.Name);
        }
    }

    // ---- Lookup -------------------------------------------------------------------

    /// <summary>Finds an entry by exact name, then by normalised name. Null if absent.</summary>
    public ZipEntryInfo? FindEntry(string name)
    {
        EnsureNotDisposed();
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (_byName.TryGetValue(name, out var e)) return e;
        if (PathSafety.TryNormalize(name, out var norm, out _, out _))
        {
            norm = norm.TrimEnd('/');
            foreach (var x in _entries)
                if (x.NormalizedName is not null && string.Equals(x.NormalizedName.TrimEnd('/'), norm, StringComparison.Ordinal)) return x;
        }
        return null;
    }

    // ---- Opening entries ----------------------------------------------------------

    /// <summary>Opens an entry for reading. See <see cref="ZipEntryInfo.OpenRead"/>.</summary>
    public Stream OpenRead(ZipEntryInfo entry, string? password = null)
    {
        EnsureNotDisposed();
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (!ReferenceEquals(entry.Owner, this)) throw new ArgumentException("Entry belongs to a different reader.", nameof(entry));
        if (entry.IsDirectory && entry.Size == 0) return Stream.Null;
        var limits = _options.Limits;

        if (!entry.IsSupported)
            throw new ZipUnsupportedException(
                (entry.Flags & ZipFormat.FlagStrongEncryption) != 0
                    ? $"Entry '{entry.Name}' uses strong (PKWARE) encryption, which is not supported."
                    : $"Entry '{entry.Name}' uses compression method {entry.RawMethod}, which is not supported (Stored and Deflate only).");

        if (limits.MaxEntryBytes is long me && entry.Size > me)
            throw new ZipLimitExceededException(ZipLimit.EntryBytes, me, $"Entry '{entry.Name}' declares {entry.Size} bytes, exceeding the limit of {me}.", entry.Name);
        if (limits.MaxCompressionRatio is double ratio && entry.Size >= 1024 * 1024)
        {
            double r = entry.CompressedSize == 0 ? double.PositiveInfinity : (double)entry.Size / entry.CompressedSize;
            if (r > ratio)
                throw new ZipLimitExceededException(ZipLimit.CompressionRatio, (long)ratio,
                    $"Entry '{entry.Name}' has a compression ratio of {r:F0}:1, exceeding the limit of {ratio:F0}:1.", entry.Name);
        }

        password ??= _options.Password;
        if (entry.IsEncrypted && password is null)
            throw new ZipPasswordRequiredException($"Entry '{entry.Name}' is encrypted; a password is required.", false, entry.Name);

        // Local header
        var lh = new byte[ZipFormat.LocalHeaderFixed];
        long dataOffset;
        lock (_sync)
        {
            if (entry.HeaderOffset < 0 || entry.HeaderOffset + lh.Length > _length)
                throw new ZipIntegrityException($"Entry '{entry.Name}' has an invalid local header offset.", entry.Name);
            ReadExactAt(entry.HeaderOffset, lh);
            if (BinaryPrimitives.ReadUInt32LittleEndian(lh) != ZipFormat.LocalHeaderSig)
                throw new ZipIntegrityException($"Entry '{entry.Name}': local header signature not found.", entry.Name);
            int ln = BinaryPrimitives.ReadUInt16LittleEndian(lh.AsSpan(26));
            int le = BinaryPrimitives.ReadUInt16LittleEndian(lh.AsSpan(28));
            if (ln != entry.RawName.Length)
                throw new ZipIntegrityException($"Entry '{entry.Name}': local and central directory names differ.", entry.Name);
            var lname = new byte[ln];
            ReadExactAt(entry.HeaderOffset + ZipFormat.LocalHeaderFixed, lname);
            if (!lname.AsSpan().SequenceEqual(entry.RawName))
                throw new ZipIntegrityException($"Entry '{entry.Name}': local and central directory names differ.", entry.Name);
            dataOffset = entry.HeaderOffset + ZipFormat.LocalHeaderFixed + ln + le;
        }
        if (dataOffset + entry.CompressedSize > _length)
            throw new ZipIntegrityException($"Entry '{entry.Name}' extends past the end of the archive (truncated).", entry.Name);

        var range = new SharedRangeStream(_stream, _sync, dataOffset, entry.CompressedSize);
        Stream data = range;
        AesDecryptStream? aes = null;
        bool checkCrc = _options.VerifyCrc;

        try
        {
            if (entry.Encryption == ZipEncryption.ZipCrypto)
            {
                if (entry.CompressedSize < 12) throw new ZipIntegrityException($"Entry '{entry.Name}': encryption header is truncated.", entry.Name);
                var enc = _options.LegacyPasswordEncoding ?? Encoding.UTF8;
                var keys = new ZipCryptoKeys(enc.GetBytes(password!));
                var hdr = new byte[12];
                ReadFully(range, hdr);
                keys.Decrypt(hdr);
                byte check = hdr[11];
                bool ok = (entry.Flags & ZipFormat.FlagDataDescriptor) != 0
                    ? (check == (byte)(entry.DosTime >> 8) || check == (byte)(entry.Crc32 >> 24))
                    : check == (byte)(entry.Crc32 >> 24);
                if (!ok) throw new ZipPasswordRequiredException($"Wrong password for entry '{entry.Name}'.", true, entry.Name);
                data = new ZipCryptoDecryptStream(range, keys);
            }
            else if (entry.Encryption != ZipEncryption.None)
            {
                int keyBytes = ZipAes.KeyBytes(entry.AesStrength);
                int saltLen = keyBytes / 2;
                long overhead = saltLen + ZipAes.VerifierLength + ZipAes.MacLength;
                if (entry.CompressedSize < overhead) throw new ZipIntegrityException($"Entry '{entry.Name}': AES data is truncated.", entry.Name);
                var head = new byte[saltLen + ZipAes.VerifierLength];
                ReadFully(range, head);
                ZipAes.Derive(Encoding.UTF8.GetBytes(password!), head.AsSpan(0, saltLen).ToArray(), keyBytes,
                    out var encKey, out var authKey, out var pwv);
                if (!CryptographicOperations.FixedTimeEquals(pwv, head.AsSpan(saltLen, ZipAes.VerifierLength)))
                    throw new ZipPasswordRequiredException($"Wrong password for entry '{entry.Name}'.", true, entry.Name);
                aes = new AesDecryptStream(range, entry.CompressedSize - overhead, encKey, authKey, entry.Name);
                data = aes;
                if (entry.AesVersion == ZipAesVersion.Ae2) checkCrc = false;
            }

            if (entry.RawMethod == 8) data = new DeflateStream(data, CompressionMode.Decompress);
            return new EntryReadStream(data, entry, this, aes, checkCrc);
        }
        catch
        {
            data.Dispose();
            aes?.Dispose();
            throw;
        }
    }

    private static void ReadFully(Stream s, Span<byte> dest)
    {
        int got = 0;
        while (got < dest.Length)
        {
            int n = s.Read(dest.Slice(got));
            if (n == 0) throw new ZipIntegrityException("Unexpected end of entry data.");
            got += n;
        }
    }

    internal void Charge(int n, ZipEntryInfo entry)
    {
        long total = Interlocked.Add(ref _total, n);
        if (_options.Limits.MaxTotalBytes is long mt && total > mt)
            throw new ZipLimitExceededException(ZipLimit.TotalBytes, mt, $"Total decompressed size exceeds the limit of {mt} bytes (at entry '{entry.Name}').", entry.Name);
    }

    // ---- Convenience readers ------------------------------------------------------

    /// <summary>Reads a whole entry into memory.</summary>
    public byte[] ReadAllBytes(ZipEntryInfo entry, string? password = null)
    {
        if (entry.Size > Array.MaxLength) throw new ZipLimitExceededException(ZipLimit.EntryBytes, Array.MaxLength, $"Entry '{entry.Name}' is too large for a byte array.", entry.Name);
        using var s = OpenRead(entry, password);
        // The declared size is attacker-controlled: only pre-allocate when it is plausible for the
        // compressed size (Deflate cannot exceed ~1032:1); otherwise grow as data actually arrives.
        bool plausible = entry.Size <= 1024 * 1024 || entry.Size / 1032.0 <= entry.CompressedSize;
        if (plausible)
        {
            var buf = new byte[entry.Size];
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n == 0) break;
                got += n;
            }
            // drain to trigger end-of-data verification (CRC / HMAC)
            Span<byte> one = stackalloc byte[1];
            if (s.Read(one) != 0) throw new ZipIntegrityException($"Entry '{entry.Name}' is larger than declared.", entry.Name);
            if (got != buf.Length) throw new ZipIntegrityException($"Entry '{entry.Name}' ended after {got} bytes but declares {buf.Length}.", entry.Name);
            return buf;
        }
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Reads a whole entry as text (UTF-8 by default).</summary>
    public string ReadAllText(ZipEntryInfo entry, Encoding? encoding = null, string? password = null)
        => (encoding ?? new UTF8Encoding(false)).GetString(ReadAllBytes(entry, password));

    /// <summary>Copies an entry's content to <paramref name="destination"/>.</summary>
    public void CopyTo(ZipEntryInfo entry, Stream destination, string? password = null)
    {
        using var s = OpenRead(entry, password);
        s.CopyTo(destination);
    }

    /// <summary>Async copy with cancellation.</summary>
    public async Task CopyToAsync(ZipEntryInfo entry, Stream destination, string? password = null, CancellationToken ct = default)
    {
        await using var s = OpenRead(entry, password);
        await s.CopyToAsync(destination, 81920, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads every supported entry completely, verifying CRC-32 / AES authentication and limits,
    /// without writing anything. Throws on the first problem.
    /// </summary>
    public void Verify(string? password = null, CancellationToken ct = default)
    {
        var buf = new byte[81920];
        foreach (var e in _entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.IsDirectory && e.Size == 0) continue;
            using var s = OpenRead(e, password);
            while (s.Read(buf, 0, buf.Length) > 0) ct.ThrowIfCancellationRequested();
        }
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStream) { try { _stream.Dispose(); } catch { } }
    }
}
