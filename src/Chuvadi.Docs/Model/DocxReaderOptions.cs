using System;
using System.IO;

namespace Chuvadi.Docs.Model;

/// <summary>
/// Options for <see cref="DocxReader"/> and <see cref="Document.Load(Stream, string?, DocxReaderOptions?, System.Threading.CancellationToken)"/>:
/// what to read, and the resource limits applied to untrusted input.
///
/// <para><b>Security limits.</b> Every limit is a nullable property; <c>null</c> disables that
/// check. The defaults are safe for files from anywhere (2.4.1): they stop decompression bombs,
/// memory amplification (<see cref="MaxElements"/>, 2,000,000), pathological nesting
/// (<see cref="MaxNestingDepth"/>, 64) and key-derivation stalls, and still open documents of
/// several hundred pages. Three presets: <see cref="ForUntrustedInput"/> (tighter, for uploads and
/// mail attachments), <see cref="ForTrustedInput"/> (the 2.4.0 defaults, for very large documents
/// from a trusted source) and <see cref="CreateUnrestricted"/> (every limit off). Exceeding a limit
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

    /// <summary>
    /// Default for <see cref="MaxCompressionRatio"/>: 200 : 1 (2.4.1; 1,000 : 1 before). Real Word documents stay
    /// near 15 : 1 or below; every entry of 1 MB or more above 200 : 1 in a survey of 571 files was a synthetic bomb.
    /// </summary>
    public const int DefaultMaxCompressionRatio = 200;

    /// <summary>Default for <see cref="MaxElements"/>: 2,000,000 XML elements per operation (2.4.1; no limit before).</summary>
    public const int DefaultMaxElements = 2_000_000;

    /// <summary>Default for <see cref="MaxNestingDepth"/>: 64 levels (2.4.1).</summary>
    public const int DefaultMaxNestingDepth = 64;

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
    /// When true, <see cref="DocxReader.FormattedParagraphs()"/> also reads the footnotes and endnotes (2.4.0, D-387):
    /// the body's footnote and endnote reference runs are kept, as <see cref="Document.Load(System.IO.Stream, string?)"/>
    /// keeps them, and after the footers every note paragraph is yielded, footnotes then endnotes, in the parts' order,
    /// with <see cref="DocxFormattedParagraph.Note"/> set. Default false: without it the notes parts are not read and the
    /// reference runs are left out (the 2.2.0 stream). The notes are read before the body and held until the
    /// enumeration ends; their elements count towards its <see cref="MaxElements"/>.
    /// </summary>
    public bool IncludeNotes { get; set; }

    /// <summary>
    /// When true, <see cref="DocxReader.FormattedParagraphs()"/> also reads the comments (2.4.0, D-387): the comment
    /// reference runs are kept, as <see cref="Document.Load(System.IO.Stream, string?)"/> keeps them, and after the
    /// notes every comment paragraph is yielded, in the part's order, with <see cref="DocxFormattedParagraph.Comment"/>
    /// set (author, initials, date, done and the reply parent, as <c>Load</c> reads them). Default false. The comments
    /// are read before the body and held until the enumeration ends; their elements count towards its
    /// <see cref="MaxElements"/>.
    /// </summary>
    public bool IncludeComments { get; set; }

    /// <summary>
    /// When true, <see cref="DocxReader.FormattedParagraphs()"/> also yields the paragraphs of text boxes (2.4.0, D-387):
    /// right after the paragraph that holds the box, in run order, with
    /// <see cref="DocxFormattedParagraph.TextBox"/> and <see cref="DocxFormattedParagraph.Host"/> set. Default false:
    /// the text boxes stay inside their host's runs (<see cref="Run.TextBox"/>), as before.
    /// </summary>
    public bool IncludeTextBoxes { get; set; }

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
    /// 5–50 : 1; bombs exceed 300 : 1. Checked at open. Default
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

    /// <summary>
    /// Maximum number of XML elements read in one operation: one <see cref="Document.Load(System.IO.Stream, string?)"/>,
    /// one template call (<c>DocxTemplate.Fill</c>,
    /// <c>GetPlaceholders</c>,
    /// <c>CheckTemplate</c>), or one <see cref="DocxReader"/> read call (each enumeration of
    /// <see cref="DocxReader.Paragraphs()"/> or <see cref="DocxReader.Tables()"/>, each call of
    /// <see cref="DocxReader.ExtractText()"/>, <see cref="DocxReader.Images()"/>,
    /// <see cref="DocxReader.SaveImages(string)"/> or <see cref="DocxReader.GetActiveContent()"/>), counted over
    /// every part it reads (body, headers, footers, notes, comments, styles, numbering, settings, drawings).
    /// <para>The memory a loaded document takes follows its number of elements, not its size in bytes: a few
    /// hundred bytes per element (measured up to about 520 for empty paragraphs), so an 18 KB file of two
    /// million empty paragraphs takes about 1.1 GB to load, though it passes every byte limit. Breaches throw
    /// <see cref="DocxSecurityLimitException"/> with <see cref="DocxLimit.Elements"/> while reading, before most
    /// of the memory is used.</para>
    /// <para>Default <see cref="DefaultMaxElements"/> (2,000,000; measured worst case about 1 GB for Load, while the
    /// streaming reader stays near 175 MB), since 2.4.1; null before. <see cref="ForUntrustedInput"/> sets 1,000,000;
    /// <see cref="ForTrustedInput"/> and <see cref="CreateUnrestricted"/> set null (no limit). A document over the
    /// default (roughly 1,000+ pages of dense formatting) opens with <see cref="ForTrustedInput"/>.</para>
    /// </summary>
    public int? MaxElements { get; set; } = DefaultMaxElements;

    /// <summary>
    /// Maximum content nesting (2.4.1): the levels <see cref="Document.Load(System.IO.Stream, string?)"/> and
    /// <see cref="DocxReader.FormattedParagraphs()"/> read by recursion, counted together — each table (a nested
    /// table, and rows or cells inside nested content controls or custom XML) and each hyperlink, tracked change
    /// (ins, del, moveTo, moveFrom) or simple field inside another in a paragraph count one level; each text box counts
    /// four, as it takes about four times the stack (so the default admits 16 text boxes inside each other). Content
    /// controls, smart tags and custom XML around runs are read without recursion and do not count. Checked before each level is read;
    /// breaches throw <see cref="DocxSecurityLimitException"/> with <see cref="DocxLimit.NestingDepth"/>.
    /// <para>Recursion that goes too deep ends in a stack overflow, which .NET cannot catch: it ends the whole
    /// process. The default <see cref="DefaultMaxNestingDepth"/> (64) keeps loading, saving and converting a loaded
    /// document safe on a 256 KB thread stack (the stack of an IIS worker thread); real documents nest 1–5 levels.
    /// <see cref="ForUntrustedInput"/> sets 32, <see cref="ForTrustedInput"/> 64, <see cref="CreateUnrestricted"/> null
    /// (no limit). With no limit, each level still checks the stack that is left, so a file nested too deep for the
    /// thread throws <see cref="InsufficientExecutionStackException"/> instead of ending the process.</para>
    /// <para>Templates (<c>DocxTemplate</c>) and the plain readers (<see cref="DocxReader.Paragraphs()"/>,
    /// <see cref="DocxReader.Tables()"/>, <see cref="DocxReader.ExtractText()"/>) read without recursion and are bounded
    /// by <see cref="MaxXmlDepth"/> instead.</para>
    /// </summary>
    public int? MaxNestingDepth { get; set; } = DefaultMaxNestingDepth;

    /// <summary>
    /// Limits suitable for files from untrusted sources (uploads, e-mail attachments, a shared server):
    /// 50 MB package, 256 MB uncompressed in total, 64 MB per part, compression ratio 200:1 (for entries of
    /// 1 MB or more), 10,000 zip entries, XML depth 128, spin count 500,000, 1,000,000 XML elements per
    /// operation (<see cref="MaxElements"/>) and content nesting 32 (<see cref="MaxNestingDepth"/>, 2.4.1).
    /// Measured worst case about 600 MB and a few seconds per load; a
    /// Word document of about 23,000 paragraphs (roughly 900–1,000 pages) still loads. Returns a new instance
    /// on every call; adjust as needed. The same name as Chuvadi.Sheets' and Chuvadi.Layout's presets.
    /// </summary>
    public static DocxReaderOptions ForUntrustedInput() => new()
    {
        MaxPackageBytes = 50L * 1024 * 1024,
        MaxTotalUncompressedBytes = 256L * 1024 * 1024,
        MaxPartBytes = 64L * 1024 * 1024,
        MaxCompressionRatio = 200,
        MaxEntries = 10_000,
        MaxXmlDepth = 128,
        MaxSpinCount = 500_000,
        MaxElements = 1_000_000,
        MaxNestingDepth = 32,
    };

    /// <summary>
    /// Limits for very large documents from a trusted source (2.4.1): exactly the defaults of 2.4.0 — 1 GiB package,
    /// 1 GiB uncompressed in total, no per-part cap, compression ratio 1,000:1 (for entries of 1 MB or more),
    /// 10,000 zip entries, XML depth 256, spin count 500,000 and no element limit — plus content nesting 64
    /// (<see cref="MaxNestingDepth"/>, which 2.4.0 did not have; it keeps the process safe from stack overflow).
    /// Use it when a document is refused by the defaults' <see cref="MaxElements"/> or <see cref="MaxCompressionRatio"/>
    /// and you trust where it came from: memory then grows with the document (measured about 1 GB for a 28 MB,
    /// 5-million-element document). Returns a new instance on every call.
    /// </summary>
    public static DocxReaderOptions ForTrustedInput() => new()
    {
        MaxPackageBytes = DefaultMaxPackageBytes,
        MaxTotalUncompressedBytes = DefaultMaxTotalUncompressedBytes,
        MaxPartBytes = null,
        MaxCompressionRatio = 1_000,
        MaxEntries = DefaultMaxEntries,
        MaxXmlDepth = DefaultMaxXmlDepth,
        MaxSpinCount = DefaultMaxSpinCount,
        MaxElements = null,
        MaxNestingDepth = DefaultMaxNestingDepth,
    };

    /// <summary>
    /// The element count of the operation these options were copied for (see <see cref="ForOperation"/>);
    /// null on the caller's own options, which are never changed.
    /// </summary>
    internal Chuvadi.Docs.Internal.ElementBudget? OperationBudget { get; private set; }

    /// <summary>
    /// A copy of these options for one operation, carrying a fresh element count when
    /// <see cref="MaxElements"/> is set, so the parts of one operation share one count and the caller's
    /// options (which may be shared between threads) are never written to.
    /// </summary>
    internal DocxReaderOptions ForOperation()
    {
        if (MaxElements is null) return this;
        var copy = (DocxReaderOptions)MemberwiseClone();
        copy.OperationBudget = new Chuvadi.Docs.Internal.ElementBudget(MaxElements.Value);
        return copy;
    }

    /// <summary>A copy of these options carrying <paramref name="budget"/> as the operation's element count.</summary>
    internal DocxReaderOptions WithBudget(Chuvadi.Docs.Internal.ElementBudget? budget)
    {
        var copy = (DocxReaderOptions)MemberwiseClone();
        copy.OperationBudget = budget;
        return copy;
    }

    /// <summary>A new element count for one operation, or null when <see cref="MaxElements"/> is not set.</summary>
    internal Chuvadi.Docs.Internal.ElementBudget? NewBudget()
        => MaxElements is int max ? new Chuvadi.Docs.Internal.ElementBudget(max) : null;

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
        MaxElements = null,
        MaxNestingDepth = null,
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
    /// <summary><see cref="DocxReaderOptions.MaxElements"/> (since 2.1.0).</summary>
    Elements,
    /// <summary><see cref="DocxReaderOptions.MaxNestingDepth"/> (since 2.4.1).</summary>
    NestingDepth,
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

    /// <summary>The value observed in the file (count, bytes, ratio, depth, spin count or elements read).</summary>
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

    /// <summary>The way out for a large document from a trusted source (2.4.1), added to the limits the 2.4.1 defaults tightened.</summary>
    private const string Trusted = " If the document comes from a trusted source, open it with DocxReaderOptions.ForTrustedInput().";

    private static string BuildMessage(DocxLimit limit, long actual, long allowed, string? partName)
    {
        var where = partName is null ? "" : $" in '{partName}'";
        return limit switch
        {
            DocxLimit.EntryCount => $"Document has {actual:N0} zip entries; the limit is {allowed:N0}.",
            DocxLimit.TotalUncompressedBytes => $"Document would decompress to {actual:N0} bytes; the limit is {allowed:N0}.",
            DocxLimit.CompressionRatio => $"Entry{where} has a compression ratio of {actual:N0}:1; the limit is {allowed:N0}:1 (possible decompression bomb).{Trusted}",
            DocxLimit.XmlDepth => $"XML nesting depth {actual:N0}{where} exceeds the limit of {allowed:N0}.",
            DocxLimit.SpinCount => $"Encrypted document declares a key-derivation spin count of {actual:N0}; the limit is {allowed:N0}.",
            DocxLimit.PartBytes => $"Part{where} decompresses to more than {allowed:N0} bytes (read {actual:N0}).",
            DocxLimit.PackageBytes => $"Input is {actual:N0} bytes; the limit is {allowed:N0}.",
            DocxLimit.Elements => $"Document has more than {allowed:N0} XML elements (the limit was passed{where}).{Trusted}",
            DocxLimit.NestingDepth => $"Content{where} is nested {actual:N0} levels deep (tables, text boxes, hyperlinks, tracked changes and simple fields inside each other); the limit is {allowed:N0}.",
            _ => $"Document exceeds a security limit ({limit}): {actual:N0} > {allowed:N0}{where}.",
        };
    }
}
