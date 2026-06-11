using System;
using System.IO;
using System.IO.Compression;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// Read-only descriptor for one entry in a zip archive. Returned by <see cref="ZipReader.Entries"/>
/// and <see cref="ZipExtensions.ListZipEntries"/>. The entry is "open for read" via the
/// <see cref="OpenRead"/> method as long as the parent reader/archive is still alive.
/// </summary>
public sealed class ZipEntryInfo
{
    private readonly ZipArchiveEntry _entry;

    internal ZipEntryInfo(ZipArchiveEntry entry) { _entry = entry; }

    /// <summary>The entry's full name (path inside the archive, e.g. "folder/file.txt").</summary>
    public string Name => _entry.FullName;

    /// <summary>The uncompressed size in bytes. Note: only reliable for entries the zip
    /// archive has fully indexed; for streaming-write archives this may be 0 until close.</summary>
    public long Size => _entry.Length;

    /// <summary>The compressed size in bytes.</summary>
    public long CompressedSize => _entry.CompressedLength;

    /// <summary>The entry's last-modified timestamp (UTC).</summary>
    public DateTimeOffset LastModified => _entry.LastWriteTime;

    /// <summary>True if the entry name ends with '/', the convention for empty directory entries.</summary>
    public bool IsDirectory => _entry.FullName.EndsWith("/", StringComparison.Ordinal)
                            || _entry.FullName.EndsWith("\\", StringComparison.Ordinal);

    /// <summary>Opens a read stream for the entry's content. Caller must dispose.</summary>
    public Stream OpenRead() => _entry.Open();
}
