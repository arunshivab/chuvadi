using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// The styles.xml model. xlsx stores styles in a layered structure: separate dedup'd lists
/// of fonts, fills, borders, and number formats, with a "cellXfs" list of records that
/// each combine one of each (plus alignment). Cells reference a cellXf by integer index.
///
/// Two design constraints come from Excel's strictness:
///   1. The first cellXf (index 0) is the workbook default. Even if no cell references it,
///      it must exist or Excel rejects the file.
///   2. Built-in numFmt IDs (0..49) and the first two fill slots (0 = none, 1 = gray125)
///      are reserved by the spec. We pre-populate fills[0..1] and skip built-ins when
///      writing numFmts.
///
/// This class is NOT thread-safe; it's used from the single-threaded writer.
/// </summary>
internal sealed class StyleRegistry
{
    // ---- Built-in number format IDs ------------------------------------------------
    //
    // The OOXML spec reserves numFmtIds 0..163 for built-ins (well, 0..49 are most common
    // and universally implemented; some between 50..163 vary by locale and are best avoided).
    // When a user supplies one of these format strings, we should reuse the built-in ID
    // and NOT emit a <numFmt> entry. When they supply a custom string, we allocate an ID
    // starting at 164.

    private const int FirstCustomNumFmtId = 164;

    /// <summary>Maps a built-in format code (as a string) to its reserved numFmtId.</summary>
    private static readonly Dictionary<string, int> BuiltInNumFmts = new(StringComparer.Ordinal)
    {
        // ID 0 = "General" is the default and is referenced implicitly by numFmtId="0".
        ["General"] = 0,
        ["0"] = 1,
        ["0.00"] = 2,
        ["#,##0"] = 3,
        ["#,##0.00"] = 4,
        ["0%"] = 9,
        ["0.00%"] = 10,
        ["0.00E+00"] = 11,
        ["# ?/?"] = 12,
        ["# ??/??"] = 13,
        ["mm-dd-yy"] = 14,
        ["d-mmm-yy"] = 15,
        ["d-mmm"] = 16,
        ["mmm-yy"] = 17,
        ["h:mm AM/PM"] = 18,
        ["h:mm:ss AM/PM"] = 19,
        ["h:mm"] = 20,
        ["h:mm:ss"] = 21,
        ["m/d/yy h:mm"] = 22,
        ["#,##0 ;(#,##0)"] = 37,
        ["#,##0 ;[Red](#,##0)"] = 38,
        ["#,##0.00;(#,##0.00)"] = 39,
        ["#,##0.00;[Red](#,##0.00)"] = 40,
        ["mm:ss"] = 45,
        ["[h]:mm:ss"] = 46,
        ["mmss.0"] = 47,
        ["##0.0E+0"] = 48,
        ["@"] = 49,
    };

    // ---- Dedup dictionaries --------------------------------------------------------

    private readonly Dictionary<FontKey, int>    _fonts    = new();
    private readonly Dictionary<FillKey, int>    _fills    = new();
    private readonly Dictionary<BorderKey, int>  _borders  = new();
    private readonly Dictionary<string, int>     _numFmts  = new(StringComparer.Ordinal);
    private readonly Dictionary<CellXfKey, int>  _cellXfs  = new();

    // Ordered lists for deterministic output. Indices into these lists are what cell XFs reference.
    private readonly List<FontKey>    _orderedFonts    = new();
    private readonly List<FillKey>    _orderedFills    = new();
    private readonly List<BorderKey>  _orderedBorders  = new();
    private readonly List<(int Id, string Code)> _orderedCustomNumFmts = new();
    private readonly List<CellXfKey>  _orderedCellXfs  = new();

    private int _nextCustomNumFmtId = FirstCustomNumFmtId;

    public StyleRegistry()
    {
        // Slot 0 — the workbook default font. Excel always expects something here.
        var defaultFont = new FontKey("Calibri", 11.0, false, false, false, null);
        AddFont(defaultFont);

        // Slots 0 and 1 of <fills> are reserved by the spec: "none" and "gray125".
        AddFill(FillKey.None);
        AddFill(FillKey.Gray125);

        // Slot 0 of <borders> is the empty/no-border style.
        AddBorder(BorderKey.None);

        // Slot 0 of <cellXfs> is the workbook default style. Excel requires it to exist
        // even if no cell references it.
        var defaultXf = new CellXfKey(
            FontId: 0,
            FillId: 0,
            BorderId: 0,
            NumFmtId: 0,
            ApplyAlignment: false,
            HAlign: HorizontalAlign.General,
            VAlign: VerticalAlign.Top,
            WrapText: false);
        AddCellXf(defaultXf);
    }

    // ---- Public API ----------------------------------------------------------------

    /// <summary>
    /// Returns the cellXf index for the given style, registering all of its constituent
    /// pieces (font, fill, borders, numFmt) if they haven't been seen before. The returned
    /// integer is what cells write into their s="..." attribute.
    /// </summary>
    public int GetCellXfId(CellStyle style)
    {
        // The default style always returns 0; we registered it in the constructor.
        if (style.IsDefault) return 0;

        var fontId   = GetOrAddFont(style);
        var fillId   = GetOrAddFill(style);
        var borderId = GetOrAddBorder(style);
        var numFmtId = GetOrAddNumFmt(style.NumberFormat);

        var hasAlignment = style.HAlign != HorizontalAlign.General
                        || style.VAlign != VerticalAlign.Top
                        || style.WrapText;

        var xf = new CellXfKey(
            FontId: fontId,
            FillId: fillId,
            BorderId: borderId,
            NumFmtId: numFmtId,
            ApplyAlignment: hasAlignment,
            HAlign: style.HAlign,
            VAlign: style.VAlign,
            WrapText: style.WrapText);

        return GetOrAddCellXf(xf);
    }

    /// <summary>
    /// Writes a complete styles.xml document to the given stream.
    /// The order of child elements under &lt;styleSheet&gt; is FIXED by the OOXML schema:
    /// numFmts, fonts, fills, borders, cellStyleXfs, cellXfs, cellStyles, dxfs, tableStyles.
    /// Get this wrong and Excel rejects the file.
    /// </summary>
    public void WriteTo(Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));

        using var writer = XmlWriter.Create(output, MakeSettings());

        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("styleSheet", SsNs);

        WriteNumFmts(writer);
        WriteFonts(writer);
        WriteFills(writer);
        WriteBorders(writer);
        WriteCellStyleXfs(writer);  // The "named style" XFs — we keep this minimal (just the default).
        WriteCellXfs(writer);       // The actual cell-level XFs.
        WriteCellStyles(writer);    // Maps "Normal" → cellStyleXf 0.

        writer.WriteEndElement(); // </styleSheet>
        writer.WriteEndDocument();
        writer.Flush();
    }

    // ---- Add / lookup helpers ------------------------------------------------------

    private int GetOrAddFont(CellStyle s)
    {
        // null name → use the workbook default font name ("Calibri").
        // null size → use 11.0 (Excel's default).
        var key = new FontKey(
            Name: s.FontName ?? "Calibri",
            Size: s.FontSize ?? 11.0,
            Bold: s.Bold,
            Italic: s.Italic,
            Underline: s.Underline,
            Color: s.FontColor);
        return AddFont(key);
    }

    private int AddFont(FontKey key)
    {
        if (_fonts.TryGetValue(key, out var id)) return id;
        id = _orderedFonts.Count;
        _orderedFonts.Add(key);
        _fonts[key] = id;
        return id;
    }

    private int GetOrAddFill(CellStyle s)
    {
        if (s.FillColor is null) return 0; // index 0 = "none"
        var key = new FillKey("solid", s.FillColor);
        return AddFill(key);
    }

    private int AddFill(FillKey key)
    {
        if (_fills.TryGetValue(key, out var id)) return id;
        id = _orderedFills.Count;
        _orderedFills.Add(key);
        _fills[key] = id;
        return id;
    }

    private int GetOrAddBorder(CellStyle s)
    {
        if (s.BorderTop.IsNone && s.BorderBottom.IsNone &&
            s.BorderLeft.IsNone && s.BorderRight.IsNone)
            return 0;

        var key = new BorderKey(s.BorderLeft, s.BorderRight, s.BorderTop, s.BorderBottom);
        return AddBorder(key);
    }

    private int AddBorder(BorderKey key)
    {
        if (_borders.TryGetValue(key, out var id)) return id;
        id = _orderedBorders.Count;
        _orderedBorders.Add(key);
        _borders[key] = id;
        return id;
    }

    private int GetOrAddNumFmt(string? formatCode)
    {
        if (string.IsNullOrEmpty(formatCode)) return 0; // "General"
        if (BuiltInNumFmts.TryGetValue(formatCode, out var builtIn)) return builtIn;
        if (_numFmts.TryGetValue(formatCode, out var id)) return id;

        id = _nextCustomNumFmtId++;
        _numFmts[formatCode] = id;
        _orderedCustomNumFmts.Add((id, formatCode));
        return id;
    }

    private int AddCellXf(CellXfKey key)
    {
        if (_cellXfs.TryGetValue(key, out var id)) return id;
        id = _orderedCellXfs.Count;
        _orderedCellXfs.Add(key);
        _cellXfs[key] = id;
        return id;
    }

    private int GetOrAddCellXf(CellXfKey key) => AddCellXf(key);

    // ---- XML writers ---------------------------------------------------------------

    private void WriteNumFmts(XmlWriter w)
    {
        if (_orderedCustomNumFmts.Count == 0) return;

        w.WriteStartElement("numFmts", SsNs);
        w.WriteAttributeString("count", _orderedCustomNumFmts.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, code) in _orderedCustomNumFmts)
        {
            w.WriteStartElement("numFmt", SsNs);
            w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("formatCode", code);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private void WriteFonts(XmlWriter w)
    {
        w.WriteStartElement("fonts", SsNs);
        w.WriteAttributeString("count", _orderedFonts.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var f in _orderedFonts)
        {
            w.WriteStartElement("font", SsNs);
            // Order matters: Excel writes them in a particular order. We follow suit.
            if (f.Bold)      { w.WriteStartElement("b", SsNs); w.WriteEndElement(); }
            if (f.Italic)    { w.WriteStartElement("i", SsNs); w.WriteEndElement(); }
            if (f.Underline) { w.WriteStartElement("u", SsNs); w.WriteEndElement(); }

            w.WriteStartElement("sz", SsNs);
            w.WriteAttributeString("val", f.Size.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();

            if (f.Color is not null)
            {
                w.WriteStartElement("color", SsNs);
                // OOXML uses ARGB; we pad with FF for full opacity.
                w.WriteAttributeString("rgb", "FF" + f.Color);
                w.WriteEndElement();
            }

            w.WriteStartElement("name", SsNs);
            w.WriteAttributeString("val", f.Name);
            w.WriteEndElement();

            w.WriteEndElement(); // </font>
        }
        w.WriteEndElement();
    }

    private void WriteFills(XmlWriter w)
    {
        w.WriteStartElement("fills", SsNs);
        w.WriteAttributeString("count", _orderedFills.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var fill in _orderedFills)
        {
            w.WriteStartElement("fill", SsNs);
            w.WriteStartElement("patternFill", SsNs);
            w.WriteAttributeString("patternType", fill.Pattern);

            if (fill.Color is not null)
            {
                w.WriteStartElement("fgColor", SsNs);
                w.WriteAttributeString("rgb", "FF" + fill.Color);
                w.WriteEndElement();
                // bgColor — Excel commonly writes <bgColor indexed="64"/> for solid fills.
                w.WriteStartElement("bgColor", SsNs);
                w.WriteAttributeString("indexed", "64");
                w.WriteEndElement();
            }

            w.WriteEndElement(); // </patternFill>
            w.WriteEndElement(); // </fill>
        }
        w.WriteEndElement();
    }

    private void WriteBorders(XmlWriter w)
    {
        w.WriteStartElement("borders", SsNs);
        w.WriteAttributeString("count", _orderedBorders.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var b in _orderedBorders)
        {
            w.WriteStartElement("border", SsNs);
            // OOXML requires the edges in this exact order: left, right, top, bottom, diagonal.
            WriteBorderEdge(w, "left",     b.Left);
            WriteBorderEdge(w, "right",    b.Right);
            WriteBorderEdge(w, "top",      b.Top);
            WriteBorderEdge(w, "bottom",   b.Bottom);
            WriteBorderEdge(w, "diagonal", Border.None);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void WriteBorderEdge(XmlWriter w, string elementName, Border edge)
    {
        w.WriteStartElement(elementName, SsNs);
        if (!edge.IsNone)
        {
            w.WriteAttributeString("style", BorderStyleToString(edge.Style));
            if (edge.Color is not null)
            {
                w.WriteStartElement("color", SsNs);
                w.WriteAttributeString("rgb", "FF" + edge.Color);
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }

    private static string BorderStyleToString(BorderStyle s) => s switch
    {
        BorderStyle.None    => "none",
        BorderStyle.Thin    => "thin",
        BorderStyle.Medium  => "medium",
        BorderStyle.Dashed  => "dashed",
        BorderStyle.Dotted  => "dotted",
        BorderStyle.Thick   => "thick",
        BorderStyle.Double  => "double",
        BorderStyle.Hair    => "hair",
        _ => "none",
    };

    /// <summary>
    /// Writes the minimal "cellStyleXfs" element required by Excel. We expose one entry,
    /// the workbook default, which the "Normal" cellStyle references.
    /// </summary>
    private void WriteCellStyleXfs(XmlWriter w)
    {
        w.WriteStartElement("cellStyleXfs", SsNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("xf", SsNs);
        w.WriteAttributeString("numFmtId", "0");
        w.WriteAttributeString("fontId", "0");
        w.WriteAttributeString("fillId", "0");
        w.WriteAttributeString("borderId", "0");
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private void WriteCellXfs(XmlWriter w)
    {
        w.WriteStartElement("cellXfs", SsNs);
        w.WriteAttributeString("count", _orderedCellXfs.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var xf in _orderedCellXfs)
        {
            w.WriteStartElement("xf", SsNs);
            w.WriteAttributeString("numFmtId",  xf.NumFmtId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId",    xf.FontId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId",    xf.FillId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId",  xf.BorderId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("xfId",      "0");
            // "applyXxx" attributes tell Excel that this xf overrides the parent cellStyleXf's
            // value for that aspect. We set them when the field differs from the default.
            if (xf.NumFmtId != 0) w.WriteAttributeString("applyNumberFormat", "1");
            if (xf.FontId   != 0) w.WriteAttributeString("applyFont", "1");
            if (xf.FillId   != 0) w.WriteAttributeString("applyFill", "1");
            if (xf.BorderId != 0) w.WriteAttributeString("applyBorder", "1");
            if (xf.ApplyAlignment) w.WriteAttributeString("applyAlignment", "1");

            if (xf.ApplyAlignment)
            {
                w.WriteStartElement("alignment", SsNs);
                if (xf.HAlign != HorizontalAlign.General)
                    w.WriteAttributeString("horizontal", HAlignToString(xf.HAlign));
                if (xf.VAlign != VerticalAlign.Top)
                    w.WriteAttributeString("vertical", VAlignToString(xf.VAlign));
                if (xf.WrapText)
                    w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement(); // </xf>
        }
        w.WriteEndElement();
    }

    private static string HAlignToString(HorizontalAlign a) => a switch
    {
        HorizontalAlign.Left              => "left",
        HorizontalAlign.Center            => "center",
        HorizontalAlign.Right             => "right",
        HorizontalAlign.Fill              => "fill",
        HorizontalAlign.Justify           => "justify",
        HorizontalAlign.CenterContinuous  => "centerContinuous",
        HorizontalAlign.Distributed       => "distributed",
        _ => "general",
    };

    private static string VAlignToString(VerticalAlign a) => a switch
    {
        VerticalAlign.Top          => "top",
        VerticalAlign.Center       => "center",
        VerticalAlign.Bottom       => "bottom",
        VerticalAlign.Justify      => "justify",
        VerticalAlign.Distributed  => "distributed",
        _ => "top",
    };

    private void WriteCellStyles(XmlWriter w)
    {
        // We expose one named cellStyle ("Normal") referencing cellStyleXf 0. This is what
        // Excel writes for any workbook regardless of how many cellXfs exist.
        w.WriteStartElement("cellStyles", SsNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", SsNs);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();
    }

    // ---- Internal key types --------------------------------------------------------

    /// <summary>Value-equality key for a font entry.</summary>
    private readonly record struct FontKey(
        string Name, double Size, bool Bold, bool Italic, bool Underline, string? Color);

    /// <summary>Value-equality key for a fill entry.</summary>
    private readonly record struct FillKey(string Pattern, string? Color)
    {
        public static readonly FillKey None     = new("none", null);
        public static readonly FillKey Gray125  = new("gray125", null);
    }

    /// <summary>Value-equality key for a border entry (four edges; diagonal is unused for now).</summary>
    private readonly record struct BorderKey(Border Left, Border Right, Border Top, Border Bottom)
    {
        public static readonly BorderKey None = new(Border.None, Border.None, Border.None, Border.None);
    }

    /// <summary>Value-equality key for a cellXf entry (the actual style record cells reference).</summary>
    private readonly record struct CellXfKey(
        int FontId, int FillId, int BorderId, int NumFmtId,
        bool ApplyAlignment, HorizontalAlign HAlign, VerticalAlign VAlign, bool WrapText);

    // ---- Constants -----------------------------------------------------------------

    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
