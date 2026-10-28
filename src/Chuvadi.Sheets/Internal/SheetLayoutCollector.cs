using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Collects sheet-level layout metadata (sheet format defaults, column spans, row attributes
/// and merged ranges) during the single streaming pass that
/// <see cref="Chuvadi.Sheets.Excel.RowEnumerator"/> already makes over a sheet's XML. Used by
/// <see cref="WorkbookLoader"/> so a sheet part is parsed exactly once.
///
/// The streaming <see cref="Chuvadi.Sheets.Excel.XlsxReader"/> API does not pass a collector,
/// so its hot path is unchanged.
/// </summary>
internal sealed class SheetLayoutCollector
{
    /// <summary>Merged ranges exactly as written in the file's &lt;mergeCell ref="..."/&gt;, in file order.</summary>
    public List<string> MergeRanges { get; } = new();

    /// <summary>&lt;col&gt; spans in file order.</summary>
    public List<ColumnRange> Columns { get; } = new();

    /// <summary>Rows that carried at least one layout attribute, keyed by 1-based row number.</summary>
    public Dictionary<int, RowData> Rows { get; } = new();

    /// <summary>The &lt;sheetFormatPr&gt; values, or null if the element was absent.</summary>
    public SheetFormatValues? Format { get; private set; }

    /// <summary>
    /// True if the given element namespace is SpreadsheetML main (transitional or strict).
    /// Layout elements are only honoured in the main namespace so that look-alike local names
    /// inside extension lists are not misread.
    /// </summary>
    public static bool IsMainNamespace(string ns)
        => string.Equals(ns, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal)
        || string.Equals(ns, "http://purl.oclc.org/ooxml/spreadsheetml/main", StringComparison.Ordinal);

    /// <summary>Handles a non-row worksheet element the reader is positioned on.</summary>
    public void OnElement(XmlReader x)
    {
        if (!IsMainNamespace(x.NamespaceURI)) return;
        switch (x.LocalName)
        {
            case "mergeCell":
            {
                var reference = x.GetAttribute("ref");
                if (!string.IsNullOrEmpty(reference)) MergeRanges.Add(reference);
                break;
            }
            case "col":
            {
                int min = ParseInt(x.GetAttribute("min"), 0);
                int max = ParseInt(x.GetAttribute("max"), min);
                if (min < 1 || max < min) break;   // malformed span: ignore rather than guess
                Columns.Add(new ColumnRange(min, max)
                {
                    Width = ParseDouble(x.GetAttribute("width"), 0),
                    CustomWidth = ParseBool(x.GetAttribute("customWidth")),
                    BestFit = ParseBool(x.GetAttribute("bestFit")),
                    Hidden = ParseBool(x.GetAttribute("hidden")),
                    Phonetic = ParseBool(x.GetAttribute("phonetic")),
                    OutlineLevel = ParseInt(x.GetAttribute("outlineLevel"), 0),
                    Collapsed = ParseBool(x.GetAttribute("collapsed")),
                    StyleIndex = ParseInt(x.GetAttribute("style"), -1),
                });
                break;
            }
            case "sheetFormatPr":
            {
                var dcw = x.GetAttribute("defaultColWidth");
                Format = new SheetFormatValues
                {
                    DefaultRowHeight = ParseDouble(x.GetAttribute("defaultRowHeight"), 15),
                    DefaultColumnWidth = dcw is null ? null : ParseDouble(dcw, 0),
                    BaseColumnWidth = ParseInt(x.GetAttribute("baseColWidth"), 8),
                    CustomHeight = ParseBool(x.GetAttribute("customHeight")),
                    ZeroHeight = ParseBool(x.GetAttribute("zeroHeight")),
                    ThickTop = ParseBool(x.GetAttribute("thickTop")),
                    ThickBottom = ParseBool(x.GetAttribute("thickBottom")),
                    OutlineLevelRow = ParseInt(x.GetAttribute("outlineLevelRow"), 0),
                    OutlineLevelColumn = ParseInt(x.GetAttribute("outlineLevelCol"), 0),
                };
                break;
            }
        }
    }

    /// <summary>Handles the attributes of a &lt;row&gt; element the reader is positioned on.</summary>
    public void OnRow(XmlReader x, int rowNumber)
    {
        var ht = x.GetAttribute("ht");
        var customHeight = x.GetAttribute("customHeight");
        var hidden = x.GetAttribute("hidden");
        var outline = x.GetAttribute("outlineLevel");
        var collapsed = x.GetAttribute("collapsed");
        var s = x.GetAttribute("s");
        var customFormat = x.GetAttribute("customFormat");
        var thickTop = x.GetAttribute("thickTop");
        var thickBot = x.GetAttribute("thickBot");
        var ph = x.GetAttribute("ph");

        if (ht is null && customHeight is null && hidden is null && outline is null && collapsed is null
            && s is null && customFormat is null && thickTop is null && thickBot is null && ph is null)
            return;   // nothing beyond r/spans: keep the dictionary sparse

        Rows[rowNumber] = new RowData
        {
            Height = ParseDouble(ht, 0),
            CustomHeight = ParseBool(customHeight),
            Hidden = ParseBool(hidden),
            OutlineLevel = ParseInt(outline, 0),
            Collapsed = ParseBool(collapsed),
            StyleIndex = ParseInt(s, -1),
            CustomFormat = ParseBool(customFormat),
            ThickTop = ParseBool(thickTop),
            ThickBottom = ParseBool(thickBot),
            Phonetic = ParseBool(ph),
        };
    }

    /// <summary>Copies everything collected into the sheet model.</summary>
    public void ApplyTo(Sheet sheet)
    {
        if (Format is { } f)
        {
            sheet.Format.DefaultRowHeight = f.DefaultRowHeight;
            sheet.Format.DefaultColumnWidth = f.DefaultColumnWidth;
            sheet.Format.BaseColumnWidth = f.BaseColumnWidth;
            sheet.Format.CustomHeight = f.CustomHeight;
            sheet.Format.ZeroHeight = f.ZeroHeight;
            sheet.Format.ThickTop = f.ThickTop;
            sheet.Format.ThickBottom = f.ThickBottom;
            sheet.Format.OutlineLevelRow = f.OutlineLevelRow;
            sheet.Format.OutlineLevelColumn = f.OutlineLevelColumn;
        }
        foreach (var c in Columns) sheet.AddColumnRange(c);
        foreach (var kv in Rows) sheet.SetRowData(kv.Key, kv.Value);
        foreach (var range in MergeRanges) sheet.MergeCells(range);
    }

    internal sealed class SheetFormatValues
    {
        public double DefaultRowHeight;
        public double? DefaultColumnWidth;
        public int BaseColumnWidth;
        public bool CustomHeight;
        public bool ZeroHeight;
        public bool ThickTop;
        public bool ThickBottom;
        public int OutlineLevelRow;
        public int OutlineLevelColumn;
    }

    // OOXML booleans are xsd:boolean: "1"/"true" or "0"/"false".
    internal static bool ParseBool(string? s) => s is "1" or "true";

    internal static int ParseInt(string? s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    internal static double ParseDouble(string? s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
