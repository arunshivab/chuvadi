using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

    /// <summary>Print settings collected from sheetPr, printOptions, pageMargins, pageSetup, headerFooter and breaks.</summary>
    public PageSetup Page { get; } = new() { Loading = true };

    public SheetView View { get; private set; } = new();
    public string? TabColor;
    public bool SummaryBelow = true;
    public bool SummaryRight = true;
    public string? UsedRange;
    public string? DrawingRelId;
    public List<ConditionalFormatting> ConditionalFormats { get; } = new();
    public List<(string Range, DataValidation Validation)> DataValidations { get; } = new();

    private readonly CellStyleTable _styles;
    private readonly Dictionary<string, ConditionalRule> _rulesByExtId = new(StringComparer.Ordinal);

    public SheetLayoutCollector(CellStyleTable? styles = null) { _styles = styles ?? CellStyleTable.Empty; }
    private string? _capture;                 // header/footer element whose text is being collected
    private readonly StringBuilder _text = new();
    private List<PageBreak>? _breaks;         // rowBreaks / colBreaks being collected
    private readonly List<PageBreak> _rowBreaks = new();
    private readonly List<PageBreak> _colBreaks = new();

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
            // Structured elements: consumed whole. After ReadSubtree's reader is closed the
            // caller's reader sits on the element's end tag, so its next Read() is correct.
            case "sheetViews":
                if (Subtree(x) is { } sv && SheetExtrasParser.ParseSheetViews(sv) is { } view) View = view;
                break;
            case "conditionalFormatting":
                if (Subtree(x) is { } cf)
                {
                    var fmt = SheetExtrasParser.ParseConditionalFormatting(cf, _styles, _rulesByExtId);
                    if (fmt.Rules.Count > 0) ConditionalFormats.Add(fmt);
                }
                break;
            case "dataValidations":
                if (Subtree(x) is { } dv) DataValidations.AddRange(SheetExtrasParser.ParseDataValidations(dv));
                break;
            case "extLst":
                if (x.Depth == 1 && Subtree(x) is { } ext)
                    SheetExtrasParser.ParseExtLst(ext, _styles, _rulesByExtId, ConditionalFormats, DataValidations);
                break;
            case "drawing":
                DrawingRelId ??= x.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                break;
            case "tabColor":
                TabColor = _styles.ResolveColor(ColorFrom(x));
                break;
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
            case "dimension":
                UsedRange = x.GetAttribute("ref");
                break;
            case "pageSetUpPr":
                Page.FitToPage = ParseBool(x.GetAttribute("fitToPage"));
                break;
            case "outlinePr":
                SummaryBelow = x.GetAttribute("summaryBelow") is not ("0" or "false");
                SummaryRight = x.GetAttribute("summaryRight") is not ("0" or "false");
                break;
            case "printOptions":
                Page.PrintGridlines = ParseBool(x.GetAttribute("gridLines"));
                Page.PrintHeadings = ParseBool(x.GetAttribute("headings"));
                Page.CenterHorizontally = ParseBool(x.GetAttribute("horizontalCentered"));
                Page.CenterVertically = ParseBool(x.GetAttribute("verticalCentered"));
                break;
            case "pageMargins":
            {
                var m = Page.Margins;
                m.IsDefined = true;
                m.Left = ParseDouble(x.GetAttribute("left"), m.Left);
                m.Right = ParseDouble(x.GetAttribute("right"), m.Right);
                m.Top = ParseDouble(x.GetAttribute("top"), m.Top);
                m.Bottom = ParseDouble(x.GetAttribute("bottom"), m.Bottom);
                m.Header = ParseDouble(x.GetAttribute("header"), m.Header);
                m.Footer = ParseDouble(x.GetAttribute("footer"), m.Footer);
                break;
            }
            case "pageSetup":
            {
                var p = Page;
                p.IsDefined = true;
                if (x.GetAttribute("paperSize") is string ps && int.TryParse(ps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                    p.PaperSizeCode = code;
                p.Orientation = x.GetAttribute("orientation") switch
                {
                    "portrait" => PageOrientation.Portrait,
                    "landscape" => PageOrientation.Landscape,
                    _ => PageOrientation.Default,
                };
                p.Scale = ParseInt(x.GetAttribute("scale"), 100);
                p.FitToWidth = ParseInt(x.GetAttribute("fitToWidth"), 1);
                p.FitToHeight = ParseInt(x.GetAttribute("fitToHeight"), 1);
                p.FirstPageNumber = ParseInt(x.GetAttribute("firstPageNumber"), 1);
                p.UseFirstPageNumber = ParseBool(x.GetAttribute("useFirstPageNumber"));
                p.PageOrder = x.GetAttribute("pageOrder") == "overThenDown" ? PageOrder.OverThenDown : PageOrder.DownThenOver;
                p.BlackAndWhite = ParseBool(x.GetAttribute("blackAndWhite"));
                p.Draft = ParseBool(x.GetAttribute("draft"));
                p.CellComments = x.GetAttribute("cellComments") switch
                {
                    "asDisplayed" => PrintCellComments.AsDisplayed,
                    "atEnd" => PrintCellComments.AtEnd,
                    _ => PrintCellComments.None,
                };
                p.Errors = x.GetAttribute("errors") switch
                {
                    "blank" => PrintErrors.Blank,
                    "dash" => PrintErrors.Dash,
                    "NA" => PrintErrors.NA,
                    _ => PrintErrors.Displayed,
                };
                if (x.GetAttribute("horizontalDpi") is string hd) p.HorizontalDpi = ParseInt(hd, 0);
                if (x.GetAttribute("verticalDpi") is string vd) p.VerticalDpi = ParseInt(vd, 0);
                p.Copies = ParseInt(x.GetAttribute("copies"), 1);
                break;
            }
            case "headerFooter":
            {
                var h = Page.HeaderFooter;
                h.DifferentOddEven = ParseBool(x.GetAttribute("differentOddEven"));
                h.DifferentFirst = ParseBool(x.GetAttribute("differentFirst"));
                h.ScaleWithDocument = x.GetAttribute("scaleWithDoc") is not ("0" or "false");
                h.AlignWithMargins = x.GetAttribute("alignWithMargins") is not ("0" or "false");
                break;
            }
            case "oddHeader": case "oddFooter": case "evenHeader": case "evenFooter": case "firstHeader": case "firstFooter":
                if (x.IsEmptyElement) StoreHeaderFooter(x.LocalName, string.Empty);
                else { _capture = x.LocalName; _text.Clear(); }
                break;
            case "rowBreaks":
                _breaks = x.IsEmptyElement ? null : _rowBreaks;
                break;
            case "colBreaks":
                _breaks = x.IsEmptyElement ? null : _colBreaks;
                break;
            case "brk":
                _breaks?.Add(new PageBreak(
                    ParseInt(x.GetAttribute("id"), 0),
                    ParseInt(x.GetAttribute("min"), 0),
                    ParseInt(x.GetAttribute("max"), 0),
                    ParseBool(x.GetAttribute("man"))));
                break;
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

    /// <summary>Text inside an element (only collected inside header/footer elements).</summary>
    public void OnText(string text)
    {
        if (_capture is not null) _text.Append(text);
    }

    /// <summary>End of an element: closes header/footer capture and break lists.</summary>
    public void OnEndElement(string localName)
    {
        if (_capture is not null && localName == _capture)
        {
            StoreHeaderFooter(_capture, _text.ToString());
            _capture = null;
        }
        else if (localName is "rowBreaks" or "colBreaks")
        {
            _breaks = null;
        }
    }

    private void StoreHeaderFooter(string element, string text)
    {
        var h = Page.HeaderFooter;
        switch (element)
        {
            case "oddHeader": h.OddHeader = text; break;
            case "oddFooter": h.OddFooter = text; break;
            case "evenHeader": h.EvenHeader = text; break;
            case "evenFooter": h.EvenFooter = text; break;
            case "firstHeader": h.FirstHeader = text; break;
            case "firstFooter": h.FirstFooter = text; break;
        }
    }

    /// <summary>Records a row's parsed attributes.</summary>
    public void SetRow(int rowNumber, RowData data) => Rows[rowNumber] = data;

    /// <summary>
    /// Parses the attributes of a &lt;row&gt; element the reader is positioned on; null when the
    /// row carries nothing beyond r/spans (keeps per-row storage sparse).
    /// </summary>
    public static RowData? ParseRow(XmlReader x)
    {
        if (!x.HasAttributes) return null;

        // Fast path: most rows carry only r= and spans= (one cheap pass over the attributes
        // instead of ten lookups).
        bool other = false;
        if (x.MoveToFirstAttribute())
        {
            do
            {
                var n = x.LocalName;
                if (n != "r" && n != "spans" && !n.StartsWith("dyDescent", StringComparison.Ordinal)) { other = true; break; }
            }
            while (x.MoveToNextAttribute());
            x.MoveToElement();
        }
        if (!other) return null;
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
            return null;   // nothing beyond r/spans

        return new RowData
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

    private static XElement? Subtree(XmlReader x)
    {
        using var sub = x.ReadSubtree();
        return XElement.Load(sub);
    }

    /// <summary>A &lt;color&gt;-like element's attributes as a <see cref="ColorRef"/>.</summary>
    private static ColorRef? ColorFrom(XmlReader x)
    {
        var c = new XElement("c");
        foreach (var name in new[] { "rgb", "theme", "indexed", "tint", "auto" })
            if (x.GetAttribute(name) is string v) c.SetAttributeValue(name, v);
        return c.HasAttributes ? StyleTableParser.ParseColor(c) : null;
    }

    private void FillFormat(SheetFormat target)
    {
        if (Format is { } f)
            target.Load(f.DefaultRowHeight, f.DefaultColumnWidth, f.BaseColumnWidth, f.CustomHeight, f.ZeroHeight,
                        f.ThickTop, f.ThickBottom, f.OutlineLevelRow, f.OutlineLevelColumn);
        target.OutlineSummaryBelow = SummaryBelow;
        target.OutlineSummaryRight = SummaryRight;
    }

    private void FinishPage()
    {
        Page.SetLoadedBreaks(_rowBreaks, _colBreaks);
        Page.Loading = false;
    }

    /// <summary>Builds the streaming reader's layout view from everything collected.</summary>
    public SheetLayoutInfo ToLayoutInfo(CellStyleTable styles)
    {
        var format = new SheetFormat();
        FillFormat(format);
        FinishPage();
        return new SheetLayoutInfo(Page, format, Columns.ToArray(), MergeRanges.ToArray(), UsedRange,
            View, TabColor, ConditionalFormats.ToArray(), DataValidations.ToArray(), styles) { DrawingRelId = DrawingRelId };
    }

    /// <summary>Copies everything collected into the sheet model.</summary>
    public void ApplyTo(Sheet sheet)
    {
        FillFormat(sheet.Format);
        FinishPage();
        sheet.SetLoaded(Page, View, TabColor, ConditionalFormats, DataValidations);
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
