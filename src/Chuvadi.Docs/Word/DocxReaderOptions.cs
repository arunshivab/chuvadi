using System;
using System.IO;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Options for <see cref="DocxReader"/> and <see cref="Document.Load(Stream, string?, DocxReaderOptions?, System.Threading.CancellationToken)"/>:
/// what to read, and the resource limits applied to untrusted input.
///
/// <para><b>Security limits.</b> Every limit is a nullable property; <c>null</c> disables that
/// check. The defaults are generous enough for any real Word document but stop
/// decompression bombs, pathological nesting and key-derivation stalls. Exceeding a limit
/// throws <see cref="DocxSecurityLimitException"/> (which derives from
/// <see cref="DocxFormatException"/>) naming the limit, the observed value and the cap.
/// The package-level limits (<see cref="MaxEntries"/>, <see cref="MaxTotalUncompressedBytes"/>,
/// <see cref="MaxCompressionRatio"/>, <see cref="MaxSpinCount"/>) are checked when the file is
/// OPENED, before any part is decompressed; <see cref="MaxXmlDepth"/> and
/// <see cref="MaxPartBytes"/> are enforced while parts are READ.</para>
///
/// <code>
/// // Mail-attachment preview: tighter than the defaults.
/// var opts = new DocxReaderOptions
/// {
///     MaxTotalUncompressedBytes = 64L * 1024 * 1024,
///     MaxEntries = 2_000,
///     MaxSpinCount = 200_000,
/// };
/// using var r = DocxReader.Open(stream, password: null, opts, cancellationToken);
/// </code>
/// </summary>
public sealed class DocxReaderOptions
{
    /// <summary>Default for <see cref="MaxEntries"/>: 10,000 zip entries.</summary>
    public const int DefaultMaxEntries = 10_000;

    /// <summary>Default for <see cref="MaxTotalUncompressedBytes"/>: 1 GiB.</summary>
    public const long DefaultMaxTotalUncompressedBytes = 1L << 30;

    /// <summary>Default for <see cref="MaxCompressionRatio"/>: 1,000 : 1.</summary>
    public const int DefaultMaxCompressionRatio = 1_000;

    /// <summary>Default for <see cref="CompressionRatioThresholdBytes"/>: 1 MiB.</summary>
    public const long DefaultCompressionRatioThresholdBytes = 1L << 20;

    /// <summary>Default for <see cref="MaxXmlDepth"/>: 256 nested elements.</summary>
    public const int DefaultMaxXmlDepth = 256;

    /// <summary>
    /// Default for <see cref="MaxSpinCount"/>: 500,000 — five times Word's own 100,000.
    /// [MS-OFFCRYPTO] permits up to 10,000,000, but key derivation is linear in the spin count
    /// and cannot be interrupted (measured: 100,000 ≈ 0.25 s, 1,000,000 ≈ 2.4 s,
    /// 10,000,000 ≈ 23 s to open), so the default caps the worst-case stall at about a second.
    /// Raise it explicitly for trusted files written with unusually high spin counts.
    /// </summary>
    public const int DefaultMaxSpinCount = 500_000;

    /// <summary>
    /// Maximum DECOMPRESSED size, in bytes, permitted for any single package part (XML or
    /// media), enforced while the part is read. Exceeding it throws
    /// <see cref="DocxSecurityLimitException"/> with <see cref="DocxLimit.PartBytes"/>
    /// (before 1.2 this threw <see cref="InvalidDataException"/>). Null (default) = no per-part
    /// cap; the package-wide <see cref="MaxTotalUncompressedBytes"/> still applies.
    /// </summary>
    public long? MaxPartBytes { get; set; }

    /// <summary>Default for <see cref="MaxPackageBytes"/>: 1 GiB.</summary>
    public const long DefaultMaxPackageBytes = 1L << 30;

    /// <summary>
    /// Maximum size of the INPUT file or stream itself (compressed or encrypted), checked
    /// before it is buffered or decrypted — non-seekable streams are copied into memory, so
    /// this bounds that copy. Default <see cref="DefaultMaxPackageBytes"/>; null = unlimited.
    /// </summary>
    public long? MaxPackageBytes { get; set; } = DefaultMaxPackageBytes;

    /// <summary>
    /// When true (default), <see cref="DocxReader.Paragraphs()"/>, <see cref="DocxReader.Tables()"/>,
    /// <see cref="DocxReader.ExtractText()"/> and <see cref="DocxReader.Images()"/> include content
    /// from headers and footers in addition to the document body. Set to false to read only
    /// the body.
    /// </summary>
    public bool IncludeHeadersAndFooters { get; set; } = true;

    /// <summary>
    /// Maximum number of entries in the zip container. Checked at open.
    /// Default <see cref="DefaultMaxEntries"/>; null = unlimited.
    /// </summary>
    public int? MaxEntries { get; set; } = DefaultMaxEntries;

    /// <summary>
    /// Maximum sum of the declared uncompressed sizes of all zip entries. Checked at open,
    /// before anything is decompressed. The declared size is a hard ceiling: the runtime's
    /// zip reader never inflates an entry beyond it, so a lying header cannot get past this
    /// check. Default <see cref="DefaultMaxTotalUncompressedBytes"/>; null = unlimited.
    /// </summary>
    public long? MaxTotalUncompressedBytes { get; set; } = DefaultMaxTotalUncompressedBytes;

    /// <summary>
    /// Maximum uncompressed : compressed ratio for any single entry whose uncompressed size is
    /// at least <see cref="CompressionRatioThresholdBytes"/>. Real Word XML compresses roughly
    /// 5–50 : 1; bombs exceed 1,000 : 1. Checked at open. Default
    /// <see cref="DefaultMaxCompressionRatio"/>; null = unlimited.
    /// </summary>
    public int? MaxCompressionRatio { get; set; } = DefaultMaxCompressionRatio;

    /// <summary>
    /// Entries smaller than this (uncompressed) are exempt from <see cref="MaxCompressionRatio"/>,
    /// because tiny highly-repetitive parts legitimately compress very well and can't cause harm.
    /// Default <see cref="DefaultCompressionRatioThresholdBytes"/>.
    /// </summary>
    public long CompressionRatioThresholdBytes { get; set; } = DefaultCompressionRatioThresholdBytes;

    /// <summary>
    /// Maximum XML element nesting depth in any part read. Word documents rarely exceed ~40,
    /// even with nested tables. Default <see cref="DefaultMaxXmlDepth"/>; null = unlimited.
    /// </summary>
    public int? MaxXmlDepth { get; set; } = DefaultMaxXmlDepth;

    /// <summary>
    /// Maximum key-derivation spin count accepted from an ENCRYPTED file's EncryptionInfo,
    /// checked before any key derivation starts. Key derivation is CPU-bound and cannot be
    /// interrupted by a <see cref="System.Threading.CancellationToken"/>, so an attacker-chosen
    /// spin count is a denial-of-service lever (see <see cref="DefaultMaxSpinCount"/> for
    /// measured costs). Default <see cref="DefaultMaxSpinCount"/>; null = unlimited.
    /// </summary>
    public int? MaxSpinCount { get; set; } = DefaultMaxSpinCount;

    /// <summary>Creates options with every security limit disabled (trusted input only).</summary>
    public static DocxReaderOptions CreateUnrestricted() => new()
    {
        MaxPartBytes = null,
        MaxPackageBytes = null,
        MaxEntries = null,
        MaxTotalUncompressedBytes = null,
        MaxCompressionRatio = null,
        MaxXmlDepth = null,
        MaxSpinCount = null,
    };
}

/// <summary>Which resource limit a <see cref="DocxSecurityLimitException"/> reports.</summary>
public enum DocxLimit
{
    /// <summary><see cref="DocxReaderOptions.MaxEntries"/>.</summary>
    EntryCount,
    /// <summary><see cref="DocxReaderOptions.MaxTotalUncompressedBytes"/>.</summary>
    TotalUncompressedBytes,
    /// <summary><see cref="DocxReaderOptions.MaxCompressionRatio"/>.</summary>
    CompressionRatio,
    /// <summary><see cref="DocxReaderOptions.MaxXmlDepth"/>.</summary>
    XmlDepth,
    /// <summary><see cref="DocxReaderOptions.MaxSpinCount"/>.</summary>
    SpinCount,
    /// <summary><see cref="DocxReaderOptions.MaxPartBytes"/>.</summary>
    PartBytes,
    /// <summary><see cref="DocxReaderOptions.MaxPackageBytes"/>.</summary>
    PackageBytes,
}

/// <summary>
/// Thrown when a document exceeds one of the resource limits in <see cref="DocxReaderOptions"/>
/// — the signature of a decompression bomb, pathological nesting, or a key-derivation
/// stall. Derives from <see cref="DocxFormatException"/> so existing "cannot open this file"
/// handlers catch it too. The file is never partially processed past the point of detection.
/// </summary>
public sealed class DocxSecurityLimitException : DocxFormatException
{
    /// <summary>The limit that was exceeded.</summary>
    public DocxLimit Limit { get; }

    /// <summary>The value observed in the file (count, bytes, ratio, depth or spin count).</summary>
    public long Actual { get; }

    /// <summary>The configured maximum.</summary>
    public long Allowed { get; }

    /// <summary>The zip entry or package part concerned, when the limit is per-entry; otherwise null.</summary>
    public string? PartName { get; }

    public DocxSecurityLimitException(DocxLimit limit, long actual, long allowed, string? partName = null)
        : base(BuildMessage(limit, actual, allowed, partName))
    {
        Limit = limit;
        Actual = actual;
        Allowed = allowed;
        PartName = partName;
    }

    private static string BuildMessage(DocxLimit limit, long actual, long allowed, string? partName)
    {
        var where = partName is null ? "" : $" in '{partName}'";
        return limit switch
        {
            DocxLimit.EntryCount => $"Document has {actual:N0} zip entries; the limit is {allowed:N0}.",
            DocxLimit.TotalUncompressedBytes => $"Document would decompress to {actual:N0} bytes; the limit is {allowed:N0}.",
            DocxLimit.CompressionRatio => $"Entry{where} has a compression ratio of {actual:N0}:1; the limit is {allowed:N0}:1 (possible decompression bomb).",
            DocxLimit.XmlDepth => $"XML nesting depth {actual:N0}{where} exceeds the limit of {allowed:N0}.",
            DocxLimit.SpinCount => $"Encrypted document declares a key-derivation spin count of {actual:N0}; the limit is {allowed:N0}.",
            DocxLimit.PartBytes => $"Part{where} decompresses to more than {allowed:N0} bytes (read {actual:N0}).",
            DocxLimit.PackageBytes => $"Input is {actual:N0} bytes; the limit is {allowed:N0}.",
            _ => $"Document exceeds a security limit ({limit}): {actual:N0} > {allowed:N0}{where}.",
        };
    }
}
