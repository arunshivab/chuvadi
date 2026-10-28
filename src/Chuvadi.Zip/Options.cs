using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;

namespace Chuvadi.Zip;

/// <summary>Options for opening an archive for reading.</summary>
public sealed class ZipReaderOptions
{
    /// <summary>Resource caps. Default: <see cref="ZipExtractionLimits.None"/>.</summary>
    public ZipExtractionLimits Limits { get; init; } = ZipExtractionLimits.None;
    /// <summary>Default password for encrypted entries (a per-call password overrides it).</summary>
    public string? Password { get; init; }
    /// <summary>Encoding of the password for legacy ZipCrypto. Default UTF-8. AES always uses UTF-8.</summary>
    public Encoding? LegacyPasswordEncoding { get; init; }
    /// <summary>Leave the supplied stream open on dispose. Default true for streams.</summary>
    public bool LeaveOpen { get; init; } = true;
    /// <summary>Encoding for names/comments lacking the UTF-8 flag. Default code page 437.</summary>
    public Encoding? NameEncoding { get; init; }
    /// <summary>Cap for buffering a non-seekable input in memory (never spilled to disk). Default 256 MiB.</summary>
    public long MaxBufferedBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>Verify CRC-32 of every entry as it is read. Default true.</summary>
    public bool VerifyCrc { get; init; } = true;
    /// <summary>Reject archives whose entries overlap (zip-quine bombs). Default true.</summary>
    public bool DetectOverlappingEntries { get; init; } = true;
}

/// <summary>Options for extraction.</summary>
public sealed class ZipExtractOptions
{
    public ZipOverwritePolicy Overwrite { get; init; } = ZipOverwritePolicy.Overwrite;
    public ZipUnsafePathPolicy UnsafePaths { get; init; } = ZipUnsafePathPolicy.Throw;
    public ZipSymlinkPolicy Symlinks { get; init; } = ZipSymlinkPolicy.Skip;
    /// <summary>Password used for encrypted entries (overrides the reader's default).</summary>
    public string? Password { get; init; }
    /// <summary>Set file timestamps from the archive. Default true.</summary>
    public bool RestoreTimestamps { get; init; } = true;
    /// <summary>Apply rwx permission bits (mode &amp; 0777) on Unix. Default false.</summary>
    public bool RestoreUnixPermissions { get; init; }
    /// <summary>Only entries for which this returns true are extracted.</summary>
    public Func<ZipEntryInfo, bool>? Filter { get; init; }
    /// <summary>Progress callback.</summary>
    public IProgress<ZipProgress>? Progress { get; init; }
    /// <summary>Delete a partially written file when an entry fails. Default true.</summary>
    public bool DeletePartialFiles { get; init; } = true;
}

/// <summary>Progress report.</summary>
public readonly record struct ZipProgress(string EntryName, int EntryIndex, int EntryCount, long BytesProcessed);

/// <summary>Outcome of an extraction.</summary>
public sealed class ZipExtractionResult
{
    public int FilesExtracted { get; internal set; }
    public int DirectoriesCreated { get; internal set; }
    public long BytesWritten { get; internal set; }
    public List<string> SkippedUnsafe { get; } = new();
    public List<string> SkippedExisting { get; } = new();
    public List<string> SkippedSymlinks { get; } = new();
    public List<string> SkippedFiltered { get; } = new();
}

/// <summary>Per-entry options for writing.</summary>
public sealed class ZipEntryOptions
{
    /// <summary>Compression method. Default: writer default.</summary>
    public ZipCompressionMethod? Method { get; init; }
    /// <summary>Deflate level. Default: writer default.</summary>
    public CompressionLevel? Level { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    /// <summary>Unix mode including type bits optional (e.g. 0x1A4 = 0644). File-type bits are added automatically.</summary>
    public int? UnixMode { get; init; }
    public byte? DosAttributes { get; init; }
    /// <summary>Raw external attributes; overrides UnixMode and DosAttributes.</summary>
    public uint? ExternalAttributes { get; init; }
    /// <summary>Overrides the writer's encryption for this entry.</summary>
    public ZipEncryption? Encryption { get; init; }
    /// <summary>Overrides the writer's password for this entry.</summary>
    public string? Password { get; init; }
    /// <summary>Expected uncompressed size; lets the writer choose Zip64 up front for streamed entries.</summary>
    public long? ExpectedSize { get; init; }
    /// <summary>Force Zip64 for this entry (required for streamed entries that may exceed 4 GiB without ExpectedSize).</summary>
    public bool Zip64 { get; init; }
}

/// <summary>Options for creating an archive.</summary>
public sealed class ZipWriterOptions
{
    public CompressionLevel DefaultLevel { get; init; } = CompressionLevel.Optimal;
    public ZipCompressionMethod DefaultMethod { get; init; } = ZipCompressionMethod.Deflate;
    public Zip64Mode Zip64 { get; init; } = Zip64Mode.Auto;
    /// <summary>Archive-wide password; used with <see cref="Encryption"/>.</summary>
    public string? Password { get; init; }
    /// <summary>Default encryption. If a password is set and this is None, AES-256 is used.</summary>
    public ZipEncryption Encryption { get; init; } = ZipEncryption.None;
    public ZipAesVersion AesVersion { get; init; } = ZipAesVersion.Ae2;
    public string? Comment { get; init; }
    public ZipHostSystem HostSystem { get; init; } = ZipHostSystem.Unix;
    public bool LeaveOpen { get; init; } = true;
    /// <summary>Timestamp for entries without one. Set for reproducible archives. Default: now.</summary>
    public DateTimeOffset? DefaultLastModified { get; init; }
    /// <summary>Write the 0x5455 extended timestamp. Default true.</summary>
    public bool WriteExtendedTimestamps { get; init; } = true;
    /// <summary>Allow two entries with the same name. Default false.</summary>
    public bool AllowDuplicateNames { get; init; }
    /// <summary>Longest accepted entry name in UTF-8 bytes. Default 65535 (format maximum).</summary>
    public int MaxNameBytes { get; init; } = 65535;
}

/// <summary>Options for zipping a directory tree.</summary>
public sealed class ZipDirectoryOptions
{
    public ZipWriterOptions Writer { get; init; } = new();
    public ZipEntryOptions? Entry { get; init; }
    public bool Recurse { get; init; } = true;
    public bool IncludeEmptyDirectories { get; init; } = true;
    public bool FollowSymlinks { get; init; }
    /// <summary>Prefix for all entry names, e.g. "backup/".</summary>
    public string? EntryPrefix { get; init; }
    /// <summary>Return false to exclude a path (full path passed).</summary>
    public Func<string, bool>? Filter { get; init; }
    public bool PreserveTimestamps { get; init; } = true;
    public bool PreserveUnixPermissions { get; init; } = true;
    public IProgress<ZipProgress>? Progress { get; init; }
}
