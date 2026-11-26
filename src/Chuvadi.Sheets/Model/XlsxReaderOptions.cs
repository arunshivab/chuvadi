namespace Chuvadi.Sheets.Model;

/// <summary>
/// Options for <see cref="XlsxReader"/> and <see cref="Workbook.Load(string, string?, XlsxReaderOptions?)"/>.
/// All defaults are reasonable; you only need to pass an instance if you deviate.
/// <para>
/// Since 2.2.1 the defaults are safe for files from untrusted sources: the read limits below are on unless you turn
/// them off (<see cref="ForTrustedInput"/>). Every real workbook under 1 GB uncompressed, 1,048,576 rows per sheet and
/// 4 million cells loads as before.
/// </para>
/// </summary>
public sealed class XlsxReaderOptions
{
    /// <summary>
    /// For <see cref="Workbook.Load(string)"/>: read the bytes of pictures on sheets into memory
    /// (<see cref="DrawingObject.GetImageBytes"/>). Default true. Set false to keep anchors and
    /// metadata only. Read limits apply to the image parts. The streaming reader always reads
    /// image bytes on demand.
    /// </summary>
    public bool LoadImageData { get; set; } = true;

    /// <summary>
    /// If true (default), the first row of each sheet is treated as headers and used for
    /// name-based cell lookup (<c>row.GetString("Name")</c>).
    /// If false, only index-based access works.
    /// </summary>
    public bool TreatFirstRowAsHeaders { get; set; } = true;

    /// <summary>
    /// If true (default), numeric cells with a date-like number format are returned as
    /// <see cref="System.DateTime"/> instead of <see cref="double"/>.
    /// </summary>
    public bool AutoDetectDates { get; set; } = true;

    /// <summary>
    /// If true, throw <see cref="XlsxFormatException"/> on any malformed XML inside otherwise
    /// reachable parts (e.g. a sheet's XML is broken). If false (default), drop the sheet/row
    /// silently and continue. Note: structural problems (broken zip, missing required parts)
    /// always throw regardless.
    /// </summary>
    public bool StrictXml { get; set; } = false;

    /// <summary>
    /// Maximum DECOMPRESSED size, in bytes, permitted for any single package part
    /// (sheet XML, shared strings, styles, workbook.xml). Exceeding the limit throws
    /// <see cref="System.IO.InvalidDataException"/> (it becomes <see cref="XlsxLimitExceededException"/> in 3.0).
    /// Protects against decompression bombs — a tiny xlsx can legally inflate to gigabytes.
    /// Default 256 MB (since 2.2.1; null, unlimited, before). Null = unlimited.
    /// </summary>
    public long? MaxPartBytes { get; set; } = DefaultMaxPartBytes;

    /// <summary>
    /// Applies to <see cref="Workbook.Load(string, string?, XlsxReaderOptions?)"/> and
    /// <see cref="Workbook.Load(System.IO.Stream, XlsxReaderOptions?)"/> only. If true, cells
    /// that have no value but carry a non-default style (s&gt;0) — e.g. an empty bordered or
    /// shaded cell — are materialized in the model with their <see cref="Cell.Style"/>. If false
    /// (default), only cells with a value are loaded, as in earlier versions, so
    /// <see cref="Sheet.PopulatedCells"/> counts are unchanged. Turn on for rendering/conversion.
    /// </summary>
    public bool IncludeStyledEmptyCells { get; set; } = false;

    // ---- Hostile-input limits (1.4.0). On by default since 2.2.1; null = unlimited. ----

    /// <summary>
    /// Maximum number of &lt;row&gt; elements read from any one sheet. Reading row
    /// <c>MaxRowsPerSheet + 1</c> throws <see cref="XlsxLimitExceededException"/>
    /// (<see cref="XlsxLimitExceededException.LimitName"/> = "MaxRowsPerSheet"). Default 1,048,576, Excel's
    /// maximum (since 2.2.1; unlimited before). Null = unlimited.
    /// </summary>
    public int? MaxRowsPerSheet { get; set; } = DefaultMaxRowsPerSheet;

    /// <summary>
    /// 2.2.1: maximum number of cells <see cref="Workbook.Load(string, string?, XlsxReaderOptions?)"/> keeps in memory
    /// for the whole workbook, counted as it reads, so a file that would need gigabytes stops early. Reading cell
    /// <c>MaxCells + 1</c> throws <see cref="XlsxLimitExceededException"/> ("MaxCells"). Default 4,000,000 (a loaded
    /// cell takes about 127 to 155 bytes, depending on its value, so about 500 to 620 MB of model). The streaming <see cref="XlsxReader"/> keeps constant memory, so this limit does not apply to
    /// it (<see cref="MaxRowsPerSheet"/> still does). For a larger trusted workbook, stream it, or raise this limit or
    /// use <see cref="ForTrustedInput"/>. Null = unlimited.
    /// </summary>
    public long? MaxCells
    {
        get => _maxCells;
        set
        {
            if (value is <= 0) throw new System.ArgumentOutOfRangeException(nameof(value), value, "MaxCells must be positive, or null for no limit.");
            _maxCells = value;
        }
    }
    private long? _maxCells = DefaultMaxCells;

    /// <summary>
    /// Maximum total DECOMPRESSED bytes for the whole package, checked twice: against the sizes
    /// the zip declares before anything is read, and against the bytes actually inflated while
    /// reading (declared sizes can lie). Also caps the size of an encrypted file before it is
    /// decrypted. Exceeding it throws <see cref="XlsxLimitExceededException"/>
    /// ("MaxTotalUncompressedBytes"). Default 1 GB (since 2.2.1; unlimited before). Null = unlimited.
    /// </summary>
    public long? MaxTotalUncompressedBytes { get; set; } = DefaultMaxTotalUncompressedBytes;

    /// <summary>
    /// Maximum ratio of declared uncompressed size to compressed size for any zip entry larger
    /// than 1 MB uncompressed. Zip bombs typically exceed 1000:1; real spreadsheet XML rarely
    /// exceeds 100:1. Exceeding it throws <see cref="XlsxLimitExceededException"/>
    /// ("MaxCompressionRatio"). Default 500 (since 2.2.1; unchecked before). Null = unchecked.
    /// </summary>
    public double? MaxCompressionRatio { get; set; } = DefaultMaxCompressionRatio;

    /// <summary>
    /// Maximum number of entries in the zip package. Exceeding it throws
    /// <see cref="XlsxLimitExceededException"/> ("MaxPackageEntries"). Default 20,000 (since 2.2.1; unlimited
    /// before). Null = unlimited.
    /// </summary>
    public int? MaxPackageEntries { get; set; } = DefaultMaxPackageEntries;

    /// <summary>
    /// Maximum key-derivation spin count (iterations) accepted from an encrypted file. The
    /// count is declared by the file, so a hostile file could otherwise demand billions of hash
    /// rounds. It is checked after the encryption header is read and before any derivation;
    /// exceeding it throws <see cref="XlsxLimitExceededException"/> ("MaxSpinCount"). Default
    /// 500,000 (Excel writes 100,000; Chuvadi.Docs uses the same default). Null = unlimited.
    /// </summary>
    public int? MaxSpinCount { get; set; } = DefaultMaxSpinCount;

    /// <summary>
    /// Cancels reading. Checked while the package is opened (zip checks, shared strings), while
    /// an encrypted file is read and decrypted (inside key derivation, every 4,096 spins), on
    /// every row while streaming, and on every row by <see cref="Workbook.Load(string, string?, XlsxReaderOptions?)"/>,
    /// so cancellation takes effect well within a second; it surfaces as
    /// <see cref="System.OperationCanceledException"/>.
    /// </summary>
    public System.Threading.CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Culture used for display text (<see cref="RowReader.GetDisplayString(int)"/>,
    /// <see cref="Cell.DisplayText"/> on a loaded workbook): decimal and group separators,
    /// group sizes, date separators, month and day names. Null = invariant (Excel en-US).
    /// </summary>
    public System.Globalization.CultureInfo? DisplayCulture { get; set; }

    internal const long DefaultMaxPartBytes = 256L * 1024 * 1024;
    internal const long DefaultMaxTotalUncompressedBytes = 1024L * 1024 * 1024;
    internal const double DefaultMaxCompressionRatio = 500;
    internal const int DefaultMaxPackageEntries = 20_000;
    internal const int DefaultMaxRowsPerSheet = 1_048_576;
    internal const int DefaultMaxSpinCount = 500_000;
    internal const long DefaultMaxCells = 4_000_000;

    /// <summary>
    /// Limits suitable for files from untrusted sources (uploads, e-mail attachments):
    /// 256 MB per part, 1 GB in total, compression ratio 500:1, 20,000 zip entries,
    /// 1,048,576 rows per sheet (Excel's maximum), 500,000 key-derivation spins and 4,000,000 cells in a loaded
    /// workbook. Since 2.2.1 these are also the defaults of <c>new XlsxReaderOptions()</c>, so this method returns the
    /// same values; it stays for code that wants to say so. Adjust as needed.
    /// </summary>
    public static XlsxReaderOptions ForUntrustedInput() => new();

    /// <summary>
    /// 2.2.1: no read limits at all, for files you produced yourself or otherwise trust: <see cref="MaxPartBytes"/>,
    /// <see cref="MaxTotalUncompressedBytes"/>, <see cref="MaxCompressionRatio"/>, <see cref="MaxPackageEntries"/>,
    /// <see cref="MaxRowsPerSheet"/>, <see cref="MaxSpinCount"/> and <see cref="MaxCells"/> are all null. These are the
    /// defaults of 2.2.0 and earlier (apart from <see cref="MaxSpinCount"/>, which was 500,000). A hostile file can
    /// then use as much memory and time as it asks for; do not use this for uploads or e-mail attachments.
    /// </summary>
    public static XlsxReaderOptions ForTrustedInput() => new()
    {
        MaxPartBytes = null,
        MaxTotalUncompressedBytes = null,
        MaxCompressionRatio = null,
        MaxPackageEntries = null,
        MaxRowsPerSheet = null,
        MaxSpinCount = null,
        MaxCells = null,
    };

    /// <summary>True when any package-level limit is set (the zip is then pre-checked).</summary>
    internal bool HasPackageLimits =>
        MaxTotalUncompressedBytes is not null || MaxCompressionRatio is not null
        || MaxPackageEntries is not null || MaxPartBytes is not null;

    /// <summary>A copy of these options, for loaders that must override individual settings.</summary>
    internal XlsxReaderOptions Clone() => (XlsxReaderOptions)MemberwiseClone();
}

/// <summary>
/// Thrown when a file exceeds one of the limits added in 1.4.0 (<see cref="XlsxReaderOptions.MaxRowsPerSheet"/>,
/// <see cref="XlsxReaderOptions.MaxTotalUncompressedBytes"/>, <see cref="XlsxReaderOptions.MaxCompressionRatio"/>,
/// <see cref="XlsxReaderOptions.MaxPackageEntries"/>), since 1.7.0 <see cref="XlsxReaderOptions.MaxSpinCount"/> and,
/// since 2.2.1, <see cref="XlsxReaderOptions.MaxCells"/>.
/// <see cref="XlsxReaderOptions.MaxPartBytes"/> keeps
/// throwing <see cref="System.IO.InvalidDataException"/>, as it always has. Derives from
/// <see cref="System.IO.IOException"/>.
/// </summary>
[System.Serializable]
public sealed class XlsxLimitExceededException : System.IO.IOException
{
    /// <summary>The option that was exceeded, e.g. "MaxRowsPerSheet".</summary>
    public string LimitName { get; }

    /// <summary>The configured limit.</summary>
    public double Limit { get; }

    /// <summary>The value that exceeded it (as far as known when the check failed).</summary>
    public double Actual { get; }

    public XlsxLimitExceededException(string limitName, double limit, double actual, string detail)
        : base($"{limitName} exceeded ({actual} > {limit}): {detail}")
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>
    /// As the four-argument constructor, keeping the lower-level exception that detected the limit (since 2.1.0,
    /// the same constructor as Chuvadi.Docs' <c>DocxLimitExceededException</c>).
    /// </summary>
    public XlsxLimitExceededException(string limitName, double limit, double actual, string detail, System.Exception innerException)
        : base($"{limitName} exceeded ({actual} > {limit}): {detail}", innerException)
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }
}
