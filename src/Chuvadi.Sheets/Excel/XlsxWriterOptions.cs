using System;
using System.IO;

namespace Chuvadi.Sheets.Excel;

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
    /// to <see cref="Path.GetTempPath"/>. Temp files are named <c>chuvadi_sheets_*.tmp</c> and
    /// deleted when the writer is disposed (or, on crash, eventually by the OS).
    /// </summary>
    public string? TempDirectory { get; set; }

    /// <summary>
    /// When set, <see cref="XlsxWriter.Save"/> produces a password-encrypted xlsx
    /// (OOXML agile encryption, AES-256). The package is assembled into a temp file and
    /// encrypted in 4096-byte segments, so even very large streamed exports can be
    /// encrypted without holding the plaintext workbook in memory. Null (default) = no
    /// encryption.
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
}
