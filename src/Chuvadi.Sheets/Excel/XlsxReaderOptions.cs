namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Options for <see cref="XlsxReader"/>. All defaults are reasonable; you only need to pass
/// an instance if you deviate.
/// </summary>
public sealed class XlsxReaderOptions
{
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
    /// <see cref="System.IO.InvalidDataException"/>. Protects against decompression bombs
    /// when opening untrusted files — a tiny xlsx can legally inflate to gigabytes.
    /// Null (default) = unlimited, preserving existing behavior for trusted input.
    /// A few hundred MB is a generous ceiling for real-world spreadsheets.
    /// </summary>
    public long? MaxPartBytes { get; set; }
}
