using System;
using System.IO;

namespace Chuvadi.Sheets.Model;

/// <summary>
/// Options for <see cref="XlsxWriter"/>. All fields have sensible defaults; you only need to
/// pass an instance if you want to deviate.
/// </summary>
public sealed class XlsxWriterOptions
{
    /// <summary>
    /// If true, string cells are written inline (<c>t="inlineStr"</c>) and no shared strings
    /// table is produced. If false (default), strings go through the shared strings table for
    /// smaller files when strings repeat.
    /// <para>
    /// Inline-strings mode avoids the temp file and post-processing pass at save time, at the
    /// cost of larger output files when strings repeat (which they usually do in tabular data).
    /// </para>
    /// </summary>
    public bool UseInlineStrings { get; set; } = false;

    /// <summary>
    /// Directory where temporary per-sheet files are created during streaming write. Defaults
    /// to <see cref="Path.GetTempPath"/>. Temp files are named <c>chuvadi_sheets_*.tmp</c>; since
    /// 2.0.1 each is opened exclusively, owner-only on Unix (mode 600) and deleted on close, so the
    /// operating system removes it even if the process dies. (A path save's own temporary file goes
    /// beside the target, not here.)
    /// </summary>
    /// <remarks>Ignored (not checked) when <see cref="TempStorage"/> is <see cref="Model.TempStorage.Memory"/>.</remarks>
    public string? TempDirectory { get; set; }

    private TempStorage _tempStorage = TempStorage.File;

    /// <summary>
    /// Where the save keeps its temporary working copies: one per sheet, and, for an encrypted save, the
    /// package before it is encrypted (since 2.1.0).
    /// <see cref="Model.TempStorage.File"/>, the default, is the temporary files described under
    /// <see cref="TempDirectory"/>. <see cref="Model.TempStorage.Memory"/> builds them in process memory
    /// instead: no file is created in the temp folder and <see cref="TempDirectory"/> is ignored, so the save
    /// works on a read-only file system.
    /// <para>With <see cref="Model.TempStorage.Memory"/> the in-memory copies are not encrypted: they hold the
    /// same data the caller already holds in memory, and they are gone when the save returns. Memory use
    /// grows by about two and a half times the package size at the peak, and a sheet or an encrypted package
    /// over about 2 GB fails with the system's <see cref="System.IO.IOException"/>. The saved file is the same
    /// either way. The same setting, with the same values and meaning, as Chuvadi.Docs'
    /// <c>EncryptionOptions.TempStorage</c>.</para>
    /// <para>A save to a path still writes its output to a temporary file beside the target
    /// (".&lt;name&gt;.&lt;8 hex&gt;.tmp") and replaces the target only when the save succeeds, so with
    /// <see cref="Model.TempStorage.Memory"/> there is no temporary file outside the target folder. A save to
    /// a stream creates no file at all.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Model.TempStorage"/>.</exception>
    public TempStorage TempStorage
    {
        get => _tempStorage;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined TempStorage value.");
            _tempStorage = value;
        }
    }

    /// <summary>
    /// When set, <see cref="XlsxWriter.Save"/> produces a password-encrypted xlsx
    /// (OOXML agile encryption, AES-256). The package is assembled into a temp file and
    /// encrypted in 4096-byte segments, so even very large streamed exports can be
    /// encrypted without holding the plaintext workbook in memory. Since 2.0.1 that temp file
    /// and the per-sheet temp files are themselves encrypted (AES-256-CTR, a one-time key held
    /// only in memory), so no plaintext reaches the disk. Null (default) = no encryption.
    /// </summary>
    public EncryptionOptions? Encryption { get; set; }

    /// <summary>
    /// The default date format used when a cell holds a DateTime/DateOnly/DateTimeOffset value
    /// and no style was supplied. Without this, Excel would display the underlying serial number
    /// (a large integer) rather than a date. Defaults to ISO-style "yyyy-mm-dd".
    /// </summary>
    public string DefaultDateFormat { get; set; } = "yyyy-mm-dd";

    /// <summary>
    /// The default format used for DateTime values that carry a non-zero time component when
    /// no style was supplied. Defaults to "yyyy-mm-dd hh:mm:ss".
    /// </summary>
    public string DefaultDateTimeFormat { get; set; } = "yyyy-mm-dd hh:mm:ss";

    /// <summary>
    /// The default format used for TimeSpan values when no style was supplied.
    /// Defaults to "[h]:mm:ss" — bracketed hours so durations &gt; 24h aren't truncated.
    /// </summary>
    public string DefaultTimeSpanFormat { get; set; } = "[h]:mm:ss";

    /// <summary>
    /// If true, workbook.xml carries &lt;calcPr fullCalcOnLoad="1"/&gt;, telling Excel (and other
    /// calculating applications) to recalculate every formula when the file is opened, instead
    /// of trusting stored results. Useful when formulas are written with cached values
    /// (<see cref="RowBuilder.Formula(string, object?)"/>) that may be approximate. Default false
    /// (no &lt;calcPr&gt; is written, as before).
    /// </summary>
    public bool FullCalculationOnLoad { get; set; }
}

/// <summary>
/// Where a save keeps its temporary working copies (<see cref="XlsxWriterOptions.TempStorage"/>, since 2.1.0).
/// The same type, values and meaning as Chuvadi.Docs' <c>TempStorage</c>.
/// </summary>
public enum TempStorage
{
    /// <summary>
    /// Temporary files in <see cref="XlsxWriterOptions.TempDirectory"/> (or the system temp folder), owner-only
    /// and deleted on close, and encrypted with a one-time key when the workbook is encrypted (the default).
    /// </summary>
    File = 0,

    /// <summary>Process memory: no temporary file in the temp folder.</summary>
    Memory = 1,
}
