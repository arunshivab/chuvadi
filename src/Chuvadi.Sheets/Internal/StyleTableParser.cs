using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Parses styles.xml into a <see cref="CellStyleTable"/> (every list, as stored) and the theme
/// part into <see cref="ThemeInfo"/>. Both parts are small, so they are loaded as XDocuments.
/// Element matching is by local name within the part's own root namespace, so transitional and
/// strict files both parse.
/// </summary>
internal static class StyleTableParser
{
    private const string DrawingMl = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string DrawingMlStrict = "http://purl.oclc.org/ooxml/drawingml/main";

    private static XmlReaderSettings Settings => new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = false,
    };

    // ---- styles.xml --------------------------------------------------------------------

    public static CellStyleTable ParseStyles(Stream stream)
    {
        XDocument doc;
        using (var r = XmlReader.Create(stream, Settings)) doc = XDocument.Load(r);
        var root = doc.Root ?? throw new XlsxFormatException("styles.xml has no root element.");
        var ns = root.Name.Namespace;
        var table = new CellStyleTable();

        // numFmts
        var custom = new Dictionary<int, string>();
        foreach (var nf in Children(root.Element(ns + "numFmts"), ns + "numFmt"))
        {
            var id = Int(nf, "numFmtId", -1);
            var code = (string?)nf.Attribute("formatCode");
            if (id >= 0 && code is not null) custom[id] = code;
        }
        table.CustomNumberFormats = new ReadOnlyDictionary<int, string>(custom);

        table.Fonts = Children(root.Element(ns + "fonts"), ns + "font").Select(f => ParseFont(f, ns)).ToArray();
        table.Fills = Children(root.Element(ns + "fills"), ns + "fill").Select(f => ParseFill(f, ns)).ToArray();
        table.Borders = Children(root.Element(ns + "borders"), ns + "border").Select(b => ParseBorder(b, ns)).ToArray();
        table.CellStyleFormats = Children(root.Element(ns + "cellStyleXfs"), ns + "xf").Select(x => ParseXf(x, ns, isCellXf: false)).ToArray();
        table.CellFormats = Children(root.Element(ns + "cellXfs"), ns + "xf").Select(x => ParseXf(x, ns, isCellXf: true)).ToArray();
        table.NamedStyles = Children(root.Element(ns + "cellStyles"), ns + "cellStyle").Select(ParseNamedStyle).ToArray();
        table.DifferentialFormats = Children(root.Element(ns + "dxfs"), ns + "dxf").Select(d => ParseDxf(d, ns)).ToArray();

        var colors = root.Element(ns + "colors");
        var indexed = colors?.Element(ns + "indexedColors");
        if (indexed is not null)
        {
            var list = Children(indexed, ns + "rgbColor")
                .Select(c => CellStyleTable.NormalizeHex((string?)c.Attribute("rgb")) ?? "000000")
                .ToArray();
            if (list.Length > 0)
            {
                table.IndexedColors = Array.AsReadOnly(list);
                table.HasCustomIndexedColors = true;
            }
        }
        var mru = colors?.Element(ns + "mruColors");
        if (mru is not null)
            table.RecentColors = Children(mru, ns + "color").Select(ParseColor).ToArray();

        return table;
    }

    private static FontEntry ParseFont(XElement f, XNamespace ns)
    {
        var font = new FontEntry
        {
            Name = Val(f.Element(ns + "name")),
            Size = Double(f.Element(ns + "sz")),
            Bold = Flag(f.Element(ns + "b")),
            Italic = Flag(f.Element(ns + "i")),
            Strikethrough = Flag(f.Element(ns + "strike")),
            Outline = Flag(f.Element(ns + "outline")),
            Shadow = Flag(f.Element(ns + "shadow")),
            Condense = Flag(f.Element(ns + "condense")),
            Extend = Flag(f.Element(ns + "extend")),
            Family = IntVal(f.Element(ns + "family")),
            Charset = IntVal(f.Element(ns + "charset")),
        };

        var u = f.Element(ns + "u");
        if (u is not null)
        {
            font.Underline = (Val(u) ?? "single") switch
            {
                "single" => UnderlineStyle.Single,
                "double" => UnderlineStyle.Double,
                "singleAccounting" => UnderlineStyle.SingleAccounting,
                "doubleAccounting" => UnderlineStyle.DoubleAccounting,
                _ => UnderlineStyle.None,
            };
        }

        font.VerticalAlign = Val(f.Element(ns + "vertAlign")) switch
        {
            "superscript" => FontVerticalAlign.Superscript,
            "subscript" => FontVerticalAlign.Subscript,
            _ => FontVerticalAlign.Baseline,
        };

        font.Scheme = Val(f.Element(ns + "scheme")) switch
        {
            "major" => FontScheme.Major,
            "minor" => FontScheme.Minor,
            _ => FontScheme.None,
        };

        var color = f.Element(ns + "color");
        if (color is not null) font.Color = ParseColor(color);
        return font;
    }

    private static FillEntry ParseFill(XElement f, XNamespace ns)
    {
        var fill = new FillEntry();
        var pf = f.Element(ns + "patternFill");
        if (pf is not null)
        {
            var pt = (string?)pf.Attribute("patternType");
            fill.Pattern = pt is null ? null : ParsePattern(pt);
            var fg = pf.Element(ns + "fgColor");
            var bg = pf.Element(ns + "bgColor");
            if (fg is not null) fill.ForegroundColor = ParseColor(fg);
            if (bg is not null) fill.BackgroundColor = ParseColor(bg);
        }

        var gf = f.Element(ns + "gradientFill");
        if (gf is not null)
        {
            fill.Gradient = new GradientFill
            {
                Type = (string?)gf.Attribute("type") == "path" ? GradientType.Path : GradientType.Linear,
                Degree = Dbl(gf, "degree"),
                Left = Dbl(gf, "left"),
                Right = Dbl(gf, "right"),
                Top = Dbl(gf, "top"),
                Bottom = Dbl(gf, "bottom"),
                Stops = gf.Elements(ns + "stop")
                    .Select(s => new GradientStop(Dbl(s, "position"),
                        s.Element(ns + "color") is { } c ? ParseColor(c) : new ColorRef { Kind = ColorKind.Auto }))
                    .ToArray(),
            };
        }
        return fill;
    }

    private static FillPattern ParsePattern(string s) => s switch
    {
        "solid" => FillPattern.Solid,
        "mediumGray" => FillPattern.MediumGray,
        "darkGray" => FillPattern.DarkGray,
        "lightGray" => FillPattern.LightGray,
        "darkHorizontal" => FillPattern.DarkHorizontal,
        "darkVertical" => FillPattern.DarkVertical,
        "darkDown" => FillPattern.DarkDown,
        "darkUp" => FillPattern.DarkUp,
        "darkGrid" => FillPattern.DarkGrid,
        "darkTrellis" => FillPattern.DarkTrellis,
        "lightHorizontal" => FillPattern.LightHorizontal,
        "lightVertical" => FillPattern.LightVertical,
        "lightDown" => FillPattern.LightDown,
        "lightUp" => FillPattern.LightUp,
        "lightGrid" => FillPattern.LightGrid,
        "lightTrellis" => FillPattern.LightTrellis,
        "gray125" => FillPattern.Gray125,
        "gray0625" => FillPattern.Gray0625,
        _ => FillPattern.None,
    };

    private static BorderEntry ParseBorder(XElement b, XNamespace ns)
    {
        return new BorderEntry
        {
            Left = Edge(b.Element(ns + "left") ?? b.Element(ns + "start"), ns),
            Right = Edge(b.Element(ns + "right") ?? b.Element(ns + "end"), ns),
            Top = Edge(b.Element(ns + "top"), ns),
            Bottom = Edge(b.Element(ns + "bottom"), ns),
            Diagonal = Edge(b.Element(ns + "diagonal"), ns),
            Vertical = Edge(b.Element(ns + "vertical"), ns),
            Horizontal = Edge(b.Element(ns + "horizontal"), ns),
            DiagonalUp = Bool(b, "diagonalUp", false),
            DiagonalDown = Bool(b, "diagonalDown", false),
            Outline = Bool(b, "outline", true),
        };
    }

    private static BorderEdge Edge(XElement? e, XNamespace ns)
    {
        if (e is null) return BorderEdge.None;
        var style = ParseBorderStyle((string?)e.Attribute("style"));
        var c = e.Element(ns + "color");
        return new BorderEdge(style, c is null ? null : ParseColor(c));
    }

    internal static BorderStyle ParseBorderStyle(string? s) => s switch
    {
        "thin" => BorderStyle.Thin,
        "medium" => BorderStyle.Medium,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "thick" => BorderStyle.Thick,
        "double" => BorderStyle.Double,
        "hair" => BorderStyle.Hair,
        "mediumDashed" => BorderStyle.MediumDashed,
        "dashDot" => BorderStyle.DashDot,
        "mediumDashDot" => BorderStyle.MediumDashDot,
        "dashDotDot" => BorderStyle.DashDotDot,
        "mediumDashDotDot" => BorderStyle.MediumDashDotDot,
        "slantDashDot" => BorderStyle.SlantDashDot,
        _ => BorderStyle.None,
    };

    private static CellFormatEntry ParseXf(XElement x, XNamespace ns, bool isCellXf)
    {
        var e = new CellFormatEntry
        {
            NumberFormatId = Int(x, "numFmtId", 0),
            FontId = Int(x, "fontId", 0),
            FillId = Int(x, "fillId", 0),
            BorderId = Int(x, "borderId", 0),
            ParentFormatIndex = isCellXf ? Int(x, "xfId", -1) : -1,
            ApplyNumberFormat = NBool(x, "applyNumberFormat"),
            ApplyFont = NBool(x, "applyFont"),
            ApplyFill = NBool(x, "applyFill"),
            ApplyBorder = NBool(x, "applyBorder"),
            ApplyAlignment = NBool(x, "applyAlignment"),
            ApplyProtection = NBool(x, "applyProtection"),
            QuotePrefix = Bool(x, "quotePrefix", false),
            PivotButton = Bool(x, "pivotButton", false),
        };
        var a = x.Element(ns + "alignment");
        if (a is not null) e.Alignment = ParseAlignment(a);
        var p = x.Element(ns + "protection");
        if (p is not null) e.Protection = ParseProtection(p);
        return e;
    }

    private static AlignmentInfo ParseAlignment(XElement a) => new()
    {
        Horizontal = (string?)a.Attribute("horizontal") switch
        {
            "left" => HorizontalAlign.Left,
            "center" => HorizontalAlign.Center,
            "right" => HorizontalAlign.Right,
            "fill" => HorizontalAlign.Fill,
            "justify" => HorizontalAlign.Justify,
            "centerContinuous" => HorizontalAlign.CenterContinuous,
            "distributed" => HorizontalAlign.Distributed,
            _ => HorizontalAlign.General,
        },
        Vertical = (string?)a.Attribute("vertical") switch
        {
            "top" => VerticalAlign.Top,
            "center" => VerticalAlign.Center,
            "justify" => VerticalAlign.Justify,
            "distributed" => VerticalAlign.Distributed,
            _ => VerticalAlign.Bottom,
        },
        WrapText = Bool(a, "wrapText", false),
        ShrinkToFit = Bool(a, "shrinkToFit", false),
        Indent = Int(a, "indent", 0),
        RelativeIndent = Int(a, "relativeIndent", 0),
        TextRotation = Int(a, "textRotation", 0),
        ReadingOrder = Int(a, "readingOrder", 0) switch
        {
            1 => ReadingOrder.LeftToRight,
            2 => ReadingOrder.RightToLeft,
            _ => ReadingOrder.Context,
        },
        JustifyLastLine = Bool(a, "justifyLastLine", false),
    };

    private static ProtectionInfo ParseProtection(XElement p) => new()
    {
        Locked = Bool(p, "locked", true),
        Hidden = Bool(p, "hidden", false),
    };

    private static NamedCellStyle ParseNamedStyle(XElement s) => new()
    {
        Name = (string?)s.Attribute("name") ?? string.Empty,
        FormatIndex = Int(s, "xfId", 0),
        BuiltinId = s.Attribute("builtinId") is null ? null : Int(s, "builtinId", 0),
        OutlineLevel = s.Attribute("iLevel") is null ? null : Int(s, "iLevel", 0),
        Hidden = Bool(s, "hidden", false),
        CustomBuiltin = Bool(s, "customBuiltin", false),
    };

    internal static DifferentialFormat ParseDxf(XElement d, XNamespace ns)
    {
        var dxf = new DifferentialFormat();
        if (d.Element(ns + "font") is { } f) dxf.Font = ParseFont(f, ns);
        if (d.Element(ns + "fill") is { } fi) dxf.Fill = ParseFill(fi, ns);
        if (d.Element(ns + "border") is { } b) dxf.Border = ParseBorder(b, ns);
        if (d.Element(ns + "alignment") is { } a) dxf.Alignment = ParseAlignment(a);
        if (d.Element(ns + "protection") is { } p) dxf.Protection = ParseProtection(p);
        if (d.Element(ns + "numFmt") is { } nf)
        {
            dxf.NumberFormatId = Int(nf, "numFmtId", 0);
            dxf.NumberFormatCode = (string?)nf.Attribute("formatCode");
        }
        return dxf;
    }

    internal static ColorRef ParseColor(XElement c)
    {
        var color = new ColorRef { Tint = Dbl(c, "tint") };
        if (Bool(c, "auto", false)) color.Kind = ColorKind.Auto;
        else if (c.Attribute("rgb") is { } rgb) { color.Kind = ColorKind.Rgb; color.Rgb = rgb.Value; }
        else if (c.Attribute("theme") is not null) { color.Kind = ColorKind.Theme; color.Theme = Int(c, "theme", -1); }
        else if (c.Attribute("indexed") is not null) { color.Kind = ColorKind.Indexed; color.Indexed = Int(c, "indexed", -1); }
        else color.Kind = ColorKind.Auto;
        return color;
    }

    // ---- theme -------------------------------------------------------------------------

    public static ThemeInfo ParseTheme(Stream stream)
    {
        XDocument doc;
        using (var r = XmlReader.Create(stream, Settings)) doc = XDocument.Load(r);
        var root = doc.Root ?? throw new XlsxFormatException("Theme part has no root element.");
        XNamespace a = root.Name.NamespaceName == DrawingMlStrict ? DrawingMlStrict : DrawingMl;

        var theme = new ThemeInfo { IsPresent = true, Name = (string?)root.Attribute("name") };
        var elements = root.Element(a + "themeElements");

        var scheme = elements?.Element(a + "clrScheme");
        var slots = new string?[12];
        if (scheme is not null)
        {
            theme.ColorSchemeName = (string?)scheme.Attribute("name");
            string[] order = { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3",
                               "accent4", "accent5", "accent6", "hlink", "folHlink" };
            for (int i = 0; i < order.Length; i++)
                slots[i] = SchemeColor(scheme.Element(a + order[i]), a);
        }
        theme.SchemeColors = Array.AsReadOnly(slots);

        var fonts = elements?.Element(a + "fontScheme");
        theme.MajorFont = ParseFontScheme(fonts?.Element(a + "majorFont"), a);
        theme.MinorFont = ParseFontScheme(fonts?.Element(a + "minorFont"), a);
        return theme;
    }

    private static string? SchemeColor(XElement? slot, XNamespace a)
    {
        if (slot is null) return null;
        if (slot.Element(a + "srgbClr") is { } s) return CellStyleTable.NormalizeHex((string?)s.Attribute("val"));
        if (slot.Element(a + "sysClr") is { } sys)
        {
            var last = CellStyleTable.NormalizeHex((string?)sys.Attribute("lastClr"));
            if (last is not null) return last;
            return (string?)sys.Attribute("val") switch
            {
                "windowText" => "000000",
                "window" => "FFFFFF",
                _ => null,
            };
        }
        return null;   // scrgbClr / hslClr / prstClr / schemeClr: not used by Office-authored themes
    }

    private static ThemeFontScheme ParseFontScheme(XElement? f, XNamespace a)
    {
        var scheme = new ThemeFontScheme();
        if (f is null) return scheme;
        scheme.Latin = (string?)f.Element(a + "latin")?.Attribute("typeface");
        scheme.EastAsian = (string?)f.Element(a + "ea")?.Attribute("typeface");
        scheme.ComplexScript = (string?)f.Element(a + "cs")?.Attribute("typeface");
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var font in f.Elements(a + "font"))
        {
            var script = (string?)font.Attribute("script");
            var face = (string?)font.Attribute("typeface");
            if (script is not null && face is not null) scripts[script] = face;
        }
        if (scripts.Count > 0) scheme.Scripts = new ReadOnlyDictionary<string, string>(scripts);
        return scheme;
    }

    // ---- attribute helpers -------------------------------------------------------------

    private static IEnumerable<XElement> Children(XElement? parent, XName name)
        => parent?.Elements(name) ?? Enumerable.Empty<XElement>();

    private static string? Val(XElement? e) => (string?)e?.Attribute("val");

    /// <summary>Boolean font element: &lt;b/&gt; = true, &lt;b val="0"/&gt; = false, absent = false.</summary>
    private static bool Flag(XElement? e) => e is not null && (Val(e) is null or "1" or "true");

    private static double? Double(XElement? e)
        => e is not null && double.TryParse(Val(e), System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? IntVal(XElement? e)
        => e is not null && int.TryParse(Val(e), System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int Int(XElement e, string attr, int fallback)
        => SheetLayoutCollector.ParseInt((string?)e.Attribute(attr), fallback);

    private static double Dbl(XElement e, string attr)
        => SheetLayoutCollector.ParseDouble((string?)e.Attribute(attr), 0);

    private static bool Bool(XElement e, string attr, bool fallback)
    {
        var s = (string?)e.Attribute(attr);
        return s is null ? fallback : s is "1" or "true";
    }

    private static bool? NBool(XElement e, string attr)
    {
        var s = (string?)e.Attribute(attr);
        return s is null ? null : s is "1" or "true";
    }
}
