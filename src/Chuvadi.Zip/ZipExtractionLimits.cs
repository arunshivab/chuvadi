namespace Chuvadi.Zip;

/// <summary>
/// Resource caps for reading untrusted archives. Every cap is enforced against the bytes
/// actually produced (never only the sizes the archive declares). A null value means "no cap".
/// Violations throw <see cref="ZipLimitExceededException"/>.
/// </summary>
public sealed class ZipExtractionLimits
{
    /// <summary>Maximum number of entries. Checked when the archive is opened.</summary>
    public int? MaxEntries { get; init; }
    /// <summary>Maximum decompressed bytes of any single entry.</summary>
    public long? MaxEntryBytes { get; init; }
    /// <summary>Maximum decompressed bytes summed over all entries of one extraction/reader.</summary>
    public long? MaxTotalBytes { get; init; }
    /// <summary>Maximum uncompressed/compressed ratio per entry (entries under 1 MiB are exempt).</summary>
    public double? MaxCompressionRatio { get; init; }
    /// <summary>Maximum directory depth of an entry name.</summary>
    public int? MaxPathDepth { get; init; }
    /// <summary>Maximum entry-name length in characters.</summary>
    public int? MaxNameLength { get; init; }
    /// <summary>Maximum size of the central directory in bytes.</summary>
    public long? MaxCentralDirectoryBytes { get; init; }

    /// <summary>No caps at all. Only for trusted archives.</summary>
    public static ZipExtractionLimits None { get; } = new();

    /// <summary>Conservative preset for untrusted input.</summary>
    public static ZipExtractionLimits Untrusted { get; } = new()
    {
        MaxEntries = 10_000,
        MaxEntryBytes = 512L * 1024 * 1024,
        MaxTotalBytes = 2L * 1024 * 1024 * 1024,
        MaxCompressionRatio = 200,
        MaxPathDepth = 32,
        MaxNameLength = 1024,
        MaxCentralDirectoryBytes = 64L * 1024 * 1024,
    };

    internal void ValidateEntryCount(long count)
    {
        if (MaxEntries is int max && count > max)
            throw new ZipLimitExceededException(ZipLimit.EntryCount, max,
                $"Archive has {count} entries, exceeding the limit of {max}.");
    }
}
