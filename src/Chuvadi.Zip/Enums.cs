namespace Chuvadi.Zip;

/// <summary>Compression method stored for an entry.</summary>
public enum ZipCompressionMethod
{
    /// <summary>No compression (method 0).</summary>
    Stored = 0,
    /// <summary>Deflate (method 8).</summary>
    Deflate = 8,
}

/// <summary>Encryption applied to an entry.</summary>
public enum ZipEncryption
{
    /// <summary>Not encrypted.</summary>
    None = 0,
    /// <summary>Traditional PKWARE encryption. Broken; READ ONLY. Writing it throws <see cref="ZipUnsupportedException"/>.</summary>
    ZipCrypto = 1,
    /// <summary>WinZip AES, 128-bit key.</summary>
    Aes128 = 2,
    /// <summary>WinZip AES, 192-bit key.</summary>
    Aes192 = 3,
    /// <summary>WinZip AES, 256-bit key (recommended).</summary>
    Aes256 = 4,
}

/// <summary>WinZip AES format revision.</summary>
public enum ZipAesVersion
{
    /// <summary>AE-1: the CRC-32 is stored and verified in addition to the HMAC.</summary>
    Ae1 = 1,
    /// <summary>AE-2: CRC-32 is stored as 0 (hides plaintext fingerprint). Default.</summary>
    Ae2 = 2,
}

/// <summary>Zip64 usage when writing.</summary>
public enum Zip64Mode
{
    /// <summary>Use Zip64 only when a size, offset or entry count requires it.</summary>
    Auto = 0,
    /// <summary>Always write Zip64 structures.</summary>
    Always = 1,
    /// <summary>Never write Zip64; throw <see cref="ZipLimitExceededException"/> if the archive would need it.</summary>
    Never = 2,
}

/// <summary>Operating system recorded in an entry's "version made by" field.</summary>
public enum ZipHostSystem
{
    /// <summary>MS-DOS / FAT (0).</summary>
    Dos = 0,
    /// <summary>Unix (3).</summary>
    Unix = 3,
    /// <summary>Windows NTFS (10).</summary>
    Ntfs = 10,
    /// <summary>macOS (19).</summary>
    MacOs = 19,
    /// <summary>Any other host code.</summary>
    Other = 255,
}

/// <summary>What to do when an extraction target file already exists.</summary>
public enum ZipOverwritePolicy
{
    /// <summary>Replace the existing file.</summary>
    Overwrite = 0,
    /// <summary>Leave the existing file and skip the entry.</summary>
    Skip = 1,
    /// <summary>Throw <see cref="System.IO.IOException"/>.</summary>
    Fail = 2,
}

/// <summary>What to do with entries whose names are unsafe (zip-slip).</summary>
public enum ZipUnsafePathPolicy
{
    /// <summary>Throw <see cref="ZipUnsafePathException"/> (default).</summary>
    Throw = 0,
    /// <summary>Skip the entry and record it in <see cref="ZipExtractionResult.SkippedUnsafe"/>.</summary>
    Skip = 1,
}

/// <summary>What to do with symbolic-link entries on extraction.</summary>
public enum ZipSymlinkPolicy
{
    /// <summary>Skip symlink entries (default; they are never created).</summary>
    Skip = 0,
    /// <summary>Throw <see cref="ZipUnsafePathException"/>.</summary>
    Throw = 1,
    /// <summary>Extract the link target text as an ordinary file.</summary>
    ExtractAsFile = 2,
}

/// <summary>Which limit a <see cref="ZipLimitExceededException"/> refers to.</summary>
public enum ZipLimit
{
    /// <summary>Number of entries.</summary>
    EntryCount,
    /// <summary>Decompressed size of one entry.</summary>
    EntryBytes,
    /// <summary>Total decompressed size of the archive.</summary>
    TotalBytes,
    /// <summary>Uncompressed/compressed ratio of an entry.</summary>
    CompressionRatio,
    /// <summary>Directory nesting depth of an entry name.</summary>
    PathDepth,
    /// <summary>Length of an entry name.</summary>
    NameLength,
    /// <summary>Size of the central directory.</summary>
    CentralDirectoryBytes,
    /// <summary>In-memory buffering of a non-seekable input.</summary>
    BufferedBytes,
    /// <summary>The archive would require Zip64 but Zip64 is disabled.</summary>
    Zip64Required,
}
