using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>Reads and writes rich-text runs (&lt;r&gt;&lt;rPr/&gt;&lt;t/&gt;&lt;/r&gt;) of shared and inline strings.</summary>
internal static class RichTextXml
{
    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Reads the content of a string item (&lt;si&gt; or &lt;is&gt;) the reader is positioned on,
    /// up to and including its end tag. Appends the plain text to <paramref name="text"/> and
    /// returns the runs when the item is rich (has &lt;r&gt; children), else null. Phonetic
    /// runs (&lt;rPh&gt;) and properties (&lt;phoneticPr&gt;) are skipped.
    /// </summary>
    public static RichText? ReadItem(XmlReader r, StringBuilder text)
    {
        if (r.IsEmptyElement) return null;
        int depth = r.Depth;
        List<RichTextRun>? runs = null;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
            if (r.NodeType != XmlNodeType.Element || r.Depth != depth + 1) continue;
            switch (r.LocalName)
            {
                case "t":
                    ReadText(r, text);
                    break;
                case "r":
                {
                    var run = ReadRun(r);
                    text.Append(run.Text);
                    (runs ??= new List<RichTextRun>()).Add(run);
                    break;
                }
                default:
                    Skip(r);   // rPh, phoneticPr, extensions
                    break;
            }
        }
        return runs is null ? null : new RichText(runs);
    }

    private static RichTextRun ReadRun(XmlReader r)
    {
        RichTextFont? font = null;
        var sb = new StringBuilder();
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
                if (r.NodeType != XmlNodeType.Element || r.Depth != depth + 1) continue;
                if (r.LocalName == "rPr") font = ReadFont(r);
                else if (r.LocalName == "t") ReadText(r, sb);
                else Skip(r);
            }
        }
        return new RichTextRun(sb.ToString(), font);
    }

    private static RichTextFont ReadFont(XmlReader r)
    {
        string? name = null; double? size = null; bool? b = null, i = null, strike = null, outline = null, shadow = null, condense = null, extend = null;
        UnderlineStyle? u = null; FontVerticalAlign? va = null; ColorRef? color = null; int? family = null, charset = null; FontScheme? scheme = null;
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
                if (r.NodeType != XmlNodeType.Element || r.Depth != depth + 1) continue;
                var val = r.GetAttribute("val");
                switch (r.LocalName)
                {
                    case "rFont": name = val; break;
                    case "sz": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var sz)) size = sz; break;
                    case "b": b = Flag(val); break;
                    case "i": i = Flag(val); break;
                    case "strike": strike = Flag(val); break;
                    case "outline": outline = Flag(val); break;
                    case "shadow": shadow = Flag(val); break;
                    case "condense": condense = Flag(val); break;
                    case "extend": extend = Flag(val); break;
                    case "u":
                        u = (val ?? "single") switch
                        {
                            "single" => UnderlineStyle.Single,
                            "double" => UnderlineStyle.Double,
                            "singleAccounting" => UnderlineStyle.SingleAccounting,
                            "doubleAccounting" => UnderlineStyle.DoubleAccounting,
                            _ => UnderlineStyle.None,
                        };
                        break;
                    case "vertAlign":
                        va = val switch
                        {
                            "superscript" => FontVerticalAlign.Superscript,
                            "subscript" => FontVerticalAlign.Subscript,
                            _ => FontVerticalAlign.Baseline,
                        };
                        break;
                    case "color": color = ReadColor(r); break;
                    case "family": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f)) family = f; break;
                    case "charset": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)) charset = c; break;
                    case "scheme":
                        scheme = val switch { "major" => FontScheme.Major, "minor" => FontScheme.Minor, _ => FontScheme.None };
                        break;
                }
                Skip(r);
            }
        }
        return new RichTextFont
        {
            Name = name, Size = size, Bold = b, Italic = i, Strikethrough = strike, Underline = u, VerticalAlign = va,
            Color = color, Family = family, Charset = charset, Scheme = scheme,
            Outline = outline, Shadow = shadow, Condense = condense, Extend = extend,
        };
    }

    private static ColorRef ReadColor(XmlReader r)
    {
        double tint = double.TryParse(r.GetAttribute("tint"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 0;
        if (tint is < -1 or > 1) tint = 0;
        if (r.GetAttribute("auto") is "1" or "true") return ColorRef.Automatic();
        var rgb = r.GetAttribute("rgb");
        if (rgb is not null)
        {
            var c = ColorRef.Automatic();
            c.Kind = ColorKind.Rgb; c.Rgb = rgb; c.Tint = tint;   // as stored, even if not valid hex
            return c;
        }
        if (int.TryParse(r.GetAttribute("theme"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var th) && th >= 0)
            return ColorRef.FromTheme(th, tint);
        if (int.TryParse(r.GetAttribute("indexed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ix) && ix >= 0)
            return ColorRef.FromIndexed(ix, tint);
        return ColorRef.Automatic();
    }

    private static bool Flag(string? val) => val is null || val is "1" or "true" or "on";

    private static void ReadText(XmlReader r, StringBuilder sb)
    {
        if (r.IsEmptyElement) return;
        int depth = r.Depth;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) return;
            if (r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                sb.Append(r.Value);
        }
    }

    /// <summary>Leaves the reader on the current element's end tag (or on the element if it is empty).</summary>
    private static void Skip(XmlReader r)
    {
        if (r.IsEmptyElement) return;
        int depth = r.Depth;
        while (r.Read())
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) return;
    }

    /// <summary>Resolves each run's colour against a style table (theme and palette).</summary>
    public static RichText Resolve(RichText rt, CellStyleTable styles)
    {
        foreach (var run in rt.Runs)
            if (run.Font?.Color is { } c) run.ResolvedColor = styles.ResolveColor(c);
        return rt;
    }

    // ---- Writing -----------------------------------------------------------------------

    /// <summary>Writes the runs as &lt;r&gt; children of the current &lt;si&gt; or &lt;is&gt;.</summary>
    public static void WriteRuns(XmlWriter w, RichText rt)
    {
        foreach (var run in rt.Runs)
        {
            w.WriteStartElement("r", SsNs);
            if (run.Font is { } f) WriteFont(w, f);
            w.WriteStartElement("t", SsNs);
            if (run.Text.Length > 0 && (char.IsWhiteSpace(run.Text[0]) || char.IsWhiteSpace(run.Text[^1])))
                w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(run.Text);
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }

    // Excel's order for CT_RPrElt children.
    private static void WriteFont(XmlWriter w, RichTextFont f)
    {
        w.WriteStartElement("rPr", SsNs);
        if (f.Bold is bool b) Bool(w, "b", b);
        if (f.Italic is bool i) Bool(w, "i", i);
        if (f.Strikethrough is bool st) Bool(w, "strike", st);
        if (f.Condense is bool cd) Bool(w, "condense", cd);
        if (f.Extend is bool ex) Bool(w, "extend", ex);
        if (f.Outline is bool ol) Bool(w, "outline", ol);
        if (f.Shadow is bool sh) Bool(w, "shadow", sh);
        if (f.Underline is { } u)
        {
            w.WriteStartElement("u", SsNs);
            if (u != UnderlineStyle.Single)
                w.WriteAttributeString("val", u switch
                {
                    UnderlineStyle.Double => "double",
                    UnderlineStyle.SingleAccounting => "singleAccounting",
                    UnderlineStyle.DoubleAccounting => "doubleAccounting",
                    _ => "none",
                });
            w.WriteEndElement();
        }
        if (f.VerticalAlign is { } va)
            Val(w, "vertAlign", va switch { FontVerticalAlign.Superscript => "superscript", FontVerticalAlign.Subscript => "subscript", _ => "baseline" });
        if (f.Size is double sz) Val(w, "sz", sz.ToString("R", CultureInfo.InvariantCulture));
        if (f.Color is { } c)
        {
            w.WriteStartElement("color", SsNs);
            switch (c.Kind)
            {
                case ColorKind.Rgb: w.WriteAttributeString("rgb", c.Rgb ?? "FF000000"); break;
                case ColorKind.Theme: w.WriteAttributeString("theme", c.Theme.ToString(CultureInfo.InvariantCulture)); break;
                case ColorKind.Indexed: w.WriteAttributeString("indexed", c.Indexed.ToString(CultureInfo.InvariantCulture)); break;
                default: w.WriteAttributeString("auto", "1"); break;
            }
            if (c.Tint != 0) w.WriteAttributeString("tint", c.Tint.ToString("R", CultureInfo.InvariantCulture));
            w.WriteEndElement();
        }
        if (f.Name is not null) Val(w, "rFont", f.Name);
        if (f.Family is int fam) Val(w, "family", fam.ToString(CultureInfo.InvariantCulture));
        if (f.Charset is int cs) Val(w, "charset", cs.ToString(CultureInfo.InvariantCulture));
        if (f.Scheme is { } sc) Val(w, "scheme", sc switch { FontScheme.Major => "major", FontScheme.Minor => "minor", _ => "none" });
        w.WriteEndElement();
    }

    private static void Bool(XmlWriter w, string name, bool value)
    {
        w.WriteStartElement(name, SsNs);
        if (!value) w.WriteAttributeString("val", "0");
        w.WriteEndElement();
    }

    private static void Val(XmlWriter w, string name, string value)
    {
        w.WriteStartElement(name, SsNs);
        w.WriteAttributeString("val", value);
        w.WriteEndElement();
    }
}
