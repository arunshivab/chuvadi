using System;

namespace Chuvadi.Sheets.Zip;

/// <summary>
/// Resource caps applied during <see cref="ZipReader.ExtractTo(string, ZipExtractionLimits?)"/>.
/// Protects against decompression bombs and entry-flooding when extracting untrusted
/// archives — a kilobyte-sized zip can legally inflate to gigabytes or contain millions of
/// entries. All limits are null/unbounded by default; set the ones relevant to your trust
/// model. Exceeding any limit throws <see cref="ZipFormatException"/> and stops extraction
/// (already-extracted files are left in place for the caller to clean up).
/// </summary>
public sealed class ZipExtractionLimits
{
    /// <summary>Maximum number of entries the archive may contain. Null = unlimited.</summary>
    public int? MaxEntries { get; init; }

    /// <summary>Maximum DECOMPRESSED size, in bytes, of any single entry. Null = unlimited.</summary>
    public long? MaxEntryBytes { get; init; }

    /// <summary>Maximum total DECOMPRESSED bytes across all entries. Null = unlimited.</summary>
    public long? MaxTotalBytes { get; init; }

    internal void ValidateEntryCount(int count)
    {
        if (MaxEntries is int max && count > max)
            throw new ZipFormatException(
                $"Archive contains {count:N0} entries, exceeding the configured limit of {max:N0}. " +
                "The input may be hostile; raise the limit if the archive is trusted.");
    }
}
