using System;
using System.IO;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip;

/// <summary>Metadata of one archive entry, as recorded in the central directory.</summary>
public sealed class ZipEntryInfo
{
    internal ZipReader Owner = null!;
    internal byte[] RawName = Array.Empty<byte>();
    internal long HeaderOffset;
    internal ushort Flags;
    internal ushort RawMethodField;          // as stored (99 for AES)
    internal ushort VersionMadeBy;
    internal ushort DosTime;
    internal ushort DosDate;
    internal int AesStrength;                // 0 when not AES

    /// <summary>The entry name exactly as stored (decoded), e.g. "dir/file.txt" or "dir/".</summary>
    public string Name { get; internal set; } = string.Empty;
    /// <summary>Last path segment of <see cref="Name"/>; empty for directories.</summary>
    public string FileName
    {
        get
        {
            if (IsDirectory) return string.Empty;
            int i = Name.LastIndexOfAny(new[] { '/', '\\' });
            return i < 0 ? Name : Name.Substring(i + 1);
        }
    }
    /// <summary>Safe, normalised forward-slash name, or null if the stored name is unsafe (see <see cref="UnsafeReason"/>).</summary>
    public string? NormalizedName { get; internal set; }
    /// <summary>Why the name is unsafe, or null when <see cref="IsSafePath"/>.</summary>
    public string? UnsafeReason { get; internal set; }
    /// <summary>True when the name cannot escape an extraction root.</summary>
    public bool IsSafePath => UnsafeReason is null;
    /// <summary>Zero-based position in the central directory.</summary>
    public int Index { get; internal set; }
    /// <summary>Uncompressed size in bytes.</summary>
    public long Size { get; internal set; }
    /// <summary>Compressed size in bytes (includes AES/ZipCrypto overhead).</summary>
    public long CompressedSize { get; internal set; }
    /// <summary>CRC-32 of the uncompressed data (0 for AE-2 entries).</summary>
    public uint Crc32 { get; internal set; }
    /// <summary>Raw compression method code actually applied to the data (AES wrapper resolved).</summary>
    public ushort RawMethod { get; internal set; }
    /// <summary>Compression method; may be an undefined value for unsupported methods - see <see cref="IsSupported"/>.</summary>
    public ZipCompressionMethod CompressionMethod => (ZipCompressionMethod)RawMethod;
    /// <summary>Encryption of this entry.</summary>
    public ZipEncryption Encryption { get; internal set; }
    /// <summary>AE-1 or AE-2 for AES entries; null otherwise.</summary>
    public ZipAesVersion? AesVersion { get; internal set; }
    /// <summary>True when the entry is encrypted by any scheme.</summary>
    public bool IsEncrypted => Encryption != ZipEncryption.None;
    /// <summary>True when the entry uses Zip64 fields.</summary>
    public bool IsZip64 { get; internal set; }
    /// <summary>True when this library can read the entry's compression and encryption scheme.</summary>
    public bool IsSupported => (RawMethod == 0 || RawMethod == 8) && (Flags & ZipFormat.FlagStrongEncryption) == 0;
    /// <summary>True for directory entries.</summary>
    public bool IsDirectory { get; internal set; }
    /// <summary>Last modification time (zone from the extended timestamp when present, else DOS local time).</summary>
    public DateTimeOffset LastModified { get; internal set; }
    /// <summary>Entry comment.</summary>
    public string Comment { get; internal set; } = string.Empty;
    /// <summary>Host system that created the entry.</summary>
    public ZipHostSystem HostSystem { get; internal set; }
    /// <summary>Raw 32-bit external attributes.</summary>
    public uint ExternalAttributes { get; internal set; }
    /// <summary>Unix mode bits (including file type) when the host is Unix/macOS; otherwise null.</summary>
    public int? UnixMode { get; internal set; }
    /// <summary>MS-DOS attribute byte (low byte of external attributes).</summary>
    public byte DosAttributes => (byte)(ExternalAttributes & 0xFF);
    /// <summary>True for Unix symbolic-link entries.</summary>
    public bool IsSymlink => UnixMode is int m && (m & 0xF000) == 0xA000;
    /// <summary>True if the name was UTF-8 flagged or carried an Info-ZIP Unicode path.</summary>
    public bool IsUtf8Name { get; internal set; }
    /// <summary>Bit flags from the general-purpose field.</summary>
    public ushort GeneralPurposeFlags => Flags;

    /// <summary>Opens a verifying, decompressing, decrypting read stream. Dispose it when done.</summary>
    public Stream OpenRead(string? password = null) => Owner.OpenRead(this, password);

    public override string ToString() => $"{Name} ({Size} bytes)";
}
