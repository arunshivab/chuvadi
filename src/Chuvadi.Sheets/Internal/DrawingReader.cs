using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Reads a sheet's drawing part (xl/drawings/drawingN.xml): anchors, pictures (with their image
/// parts), shapes, connectors, groups and chart frames. Read limits apply to every part read.
/// </summary>
internal static class DrawingReader
{
    private const string XdrNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static readonly XNamespace Xdr = XdrNs, A = ANs, R = RNs, Mc = McNs;

    /// <summary>
    /// The drawing objects of a sheet, or an empty list when the sheet has no drawing.
    /// <paramref name="eager"/> loads image bytes now (Workbook model); otherwise they load on
    /// demand while the reader is open.
    /// </summary>
    public static List<DrawingObject> Read(XlsxReader book, string sheetPartUri, string? drawingRelId, bool eager, bool loadImages)
    {
        var result = new List<DrawingObject>();
        if (drawingRelId is null) return result;
        var rel = book.PartRelationships(sheetPartUri).FirstOrDefault(r => r.Id == drawingRelId && !r.IsExternal);
        if (rel is null) return result;
        var drawingUri = ResolveTarget(sheetPartUri, rel.Target);
        if (!book.HasPart(drawingUri)) return result;

        XDocument doc;
        using (var s = book.OpenPartLimited(drawingUri))
        using (var x = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, CloseInput = false }))
            doc = XDocument.Load(x);
        var root = doc.Root;
        if (root is null) return result;

        var rels = book.PartRelationships(drawingUri).ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);
        var ctx = new Ctx(book, drawingUri, rels, eager, loadImages);

        foreach (var anchorEl in root.Elements())
        {
            var anchor = ParseAnchor(anchorEl);
            if (anchor is null) continue;
            foreach (var objEl in ObjectElements(anchorEl))
            {
                var obj = ParseObject(objEl, anchor, ctx);
                if (obj is not null) result.Add(obj);
            }
        }
        return result;
    }

    private sealed record Ctx(XlsxReader Book, string DrawingUri, Dictionary<string, Chuvadi.Internal.PackageRelationship> Rels, bool Eager, bool LoadImages);

    /// <summary>The object elements directly inside an anchor (or a group), unwrapping mc:AlternateContent.</summary>
    private static IEnumerable<XElement> ObjectElements(XElement parent)
    {
        foreach (var e in parent.Elements())
        {
            if (e.Name == Mc + "AlternateContent")
            {
                // Prefer the fallback (plain DrawingML), else the first choice.
                var branch = e.Element(Mc + "Fallback") ?? e.Element(Mc + "Choice");
                if (branch is null) continue;
                foreach (var inner in branch.Elements())
                    if (IsObject(inner)) yield return inner;
            }
            else if (IsObject(e)) yield return e;
        }
    }

    private static bool IsObject(XElement e)
        => e.Name.Namespace == Xdr && e.Name.LocalName is "pic" or "sp" or "cxnSp" or "grpSp" or "graphicFrame" or "contentPart";

    private static DrawingAnchor? ParseAnchor(XElement a)
    {
        if (a.Name.Namespace != Xdr) return null;
        var anchor = new DrawingAnchor();
        switch (a.Name.LocalName)
        {
            case "twoCellAnchor":
                anchor.Type = DrawingAnchorType.TwoCell;
                anchor.EditAs = (string?)a.Attribute("editAs") switch { "oneCell" => DrawingEditAs.OneCell, "absolute" => DrawingEditAs.Absolute, _ => DrawingEditAs.TwoCell };
                anchor.From = Point(a.Element(Xdr + "from"));
                anchor.To = Point(a.Element(Xdr + "to"));
                break;
            case "oneCellAnchor":
                anchor.Type = DrawingAnchorType.OneCell;
                anchor.EditAs = DrawingEditAs.OneCell;
                anchor.From = Point(a.Element(Xdr + "from"));
                Ext(a.Element(Xdr + "ext"), anchor);
                break;
            case "absoluteAnchor":
                anchor.Type = DrawingAnchorType.Absolute;
                anchor.EditAs = DrawingEditAs.Absolute;
                if (a.Element(Xdr + "pos") is { } pos) { anchor.XEmu = Long(pos, "x"); anchor.YEmu = Long(pos, "y"); }
                Ext(a.Element(Xdr + "ext"), anchor);
                break;
            default:
                return null;
        }
        return anchor;
    }

    private static void Ext(XElement? e, DrawingAnchor anchor)
    {
        if (e is null) return;
        anchor.WidthEmu = Long(e, "cx");
        anchor.HeightEmu = Long(e, "cy");
    }

    private static DrawingPoint? Point(XElement? p)
    {
        if (p is null) return null;
        long V(string n) => long.TryParse(p.Element(Xdr + n)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return new DrawingPoint((int)V("col") + 1, V("colOff"), (int)V("row") + 1, V("rowOff"));
    }

    private static DrawingObject? ParseObject(XElement e, DrawingAnchor anchor, Ctx ctx)
    {
        var o = new DrawingObject { Anchor = anchor };
        var cNvPr = e.Descendants(Xdr + "cNvPr").FirstOrDefault();
        if (cNvPr is not null)
        {
            o.Id = (int)(Long(cNvPr, "id") ?? 0);
            o.Name = (string?)cNvPr.Attribute("name");
            o.Description = (string?)cNvPr.Attribute("descr");
            o.Title = (string?)cNvPr.Attribute("title");
            o.Hidden = (string?)cNvPr.Attribute("hidden") is "1" or "true";
        }
        var spPr = e.Element(Xdr + "spPr") ?? e.Element(Xdr + "grpSpPr");
        var xfrm = spPr?.Element(A + "xfrm") ?? e.Element(Xdr + "xfrm");
        if (xfrm is not null) Transform(o, xfrm);

        switch (e.Name.LocalName)
        {
            case "pic":
                o.Kind = DrawingKind.Picture;
                Picture(o, e, ctx);
                Shape(o, e, spPr, ctx);
                o.Geometry ??= spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
                break;
            case "sp":
                o.Kind = DrawingKind.Shape;
                Shape(o, e, spPr, ctx);
                o.Text = TextOf(e.Element(Xdr + "txBody"));
                break;
            case "cxnSp":
                o.Kind = DrawingKind.Connector;
                Shape(o, e, spPr, ctx);
                break;
            case "grpSp":
                o.Kind = DrawingKind.Group;
                var children = new List<DrawingObject>();
                foreach (var child in ObjectElements(e))
                    if (ParseObject(child, anchor, ctx) is { } c) children.Add(c);
                o.Children = children;
                break;
            case "graphicFrame":
                var gd = e.Descendants(A + "graphicData").FirstOrDefault();
                if ((string?)gd?.Attribute("uri") == ChartUri)
                {
                    o.Kind = DrawingKind.Chart;
                    var rid = gd!.Elements().FirstOrDefault()?.Attribute(R + "id")?.Value;
                    if (rid is not null && ctx.Rels.TryGetValue(rid, out var rel) && !rel.IsExternal)
                        o.ChartPartName = ResolveTarget(ctx.DrawingUri, rel.Target);
                }
                else o.Kind = DrawingKind.Other;
                break;
            default:
                o.Kind = DrawingKind.Other;
                break;
        }
        return o;
    }

    private static void Transform(DrawingObject o, XElement xfrm)
    {
        o.Rotation = (Long(xfrm, "rot") ?? 0) / 60000.0;
        o.FlipHorizontal = (string?)xfrm.Attribute("flipH") is "1" or "true";
        o.FlipVertical = (string?)xfrm.Attribute("flipV") is "1" or "true";
        if (xfrm.Element(A + "off") is { } off) { o.OffsetXEmu = Long(off, "x"); o.OffsetYEmu = Long(off, "y"); }
        if (xfrm.Element(A + "ext") is { } ext) { o.ExtentWidthEmu = Long(ext, "cx"); o.ExtentHeightEmu = Long(ext, "cy"); }
        if (xfrm.Element(A + "chOff") is { } co) { o.ChildOffsetXEmu = Long(co, "x"); o.ChildOffsetYEmu = Long(co, "y"); }
        if (xfrm.Element(A + "chExt") is { } ce) { o.ChildExtentWidthEmu = Long(ce, "cx"); o.ChildExtentHeightEmu = Long(ce, "cy"); }
    }

    private static void Picture(DrawingObject o, XElement pic, Ctx ctx)
    {
        var blipFill = pic.Element(Xdr + "blipFill");
        var blip = blipFill?.Element(A + "blip");
        if (blipFill?.Element(A + "srcRect") is { } sr)
        {
            double P(string n) => (Long(sr, n) ?? 0) / 100000.0;
            var crop = new PictureCrop(P("l"), P("t"), P("r"), P("b"));
            if (crop != default) o.Crop = crop;
        }
        if (blip is null) return;
        var embed = blip.Attribute(R + "embed")?.Value;
        var link = blip.Attribute(R + "link")?.Value;
        if (embed is not null && ctx.Rels.TryGetValue(embed, out var rel) && !rel.IsExternal)
        {
            var uri = ResolveTarget(ctx.DrawingUri, rel.Target);
            if (!ctx.Book.HasPart(uri)) return;
            o.ImagePartName = uri;
            o.ContentType = ctx.Book.PartContentType(uri);
            if (!ctx.LoadImages) return;
            var book = ctx.Book;
            if (ctx.Eager) o.ImageData = ReadAll(book, uri);
            else o.ImageLoader = () => ReadAll(book, uri);
        }
        else if (link is not null && ctx.Rels.TryGetValue(link, out var lrel))
        {
            o.LinkedImageTarget = lrel.Target;
        }
    }

    private static byte[] ReadAll(XlsxReader book, string uri)
    {
        using var s = book.OpenPartLimited(uri);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void Shape(DrawingObject o, XElement e, XElement? spPr, Ctx ctx)
    {
        o.Geometry = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        var styles = ctx.Book.StyleTable;
        if (spPr is not null)
        {
            if (spPr.Element(A + "noFill") is not null) o.NoFill = true;
            else if (spPr.Element(A + "solidFill") is { } sf) o.FillColor = Color(sf, styles);
            if (spPr.Element(A + "ln") is { } ln)
            {
                o.LineWidthEmu = Long(ln, "w");
                if (ln.Element(A + "noFill") is not null) o.NoLine = true;
                else if (ln.Element(A + "solidFill") is { } lf) o.LineColor = Color(lf, styles);
            }
        }
        // Colours from the shape style when spPr does not set them.
        if (e.Element(Xdr + "style") is { } st)
        {
            if (o.FillColor is null && !o.NoFill && st.Element(A + "fillRef") is { } fr && (Long(fr, "idx") ?? 0) > 0)
                o.FillColor = Color(fr, styles);
            if (o.LineColor is null && !o.NoLine && st.Element(A + "lnRef") is { } lr && (Long(lr, "idx") ?? 0) > 0)
                o.LineColor = Color(lr, styles);
        }
    }

    private static string? TextOf(XElement? txBody)
    {
        if (txBody is null) return null;
        var paras = new List<string>();
        foreach (var p in txBody.Elements(A + "p"))
        {
            var sb = new StringBuilder();
            foreach (var n in p.Elements())
            {
                if (n.Name == A + "r" || n.Name == A + "fld") sb.Append(n.Element(A + "t")?.Value);
                else if (n.Name == A + "br") sb.Append('\n');
            }
            paras.Add(sb.ToString());
        }
        var text = string.Join("\n", paras);
        return text.Length == 0 ? null : text;
    }

    // ---- DrawingML colours --------------------------------------------------------

    /// <summary>The first colour child of a fill/ref element, resolved to "RRGGBB" (srgbClr, sysClr, prstClr, schemeClr with modifiers).</summary>
    internal static string? Color(XElement holder, CellStyleTable styles)
    {
        var c = holder.Elements().FirstOrDefault(x => x.Name.Namespace == A && x.Name.LocalName.EndsWith("Clr", StringComparison.Ordinal));
        if (c is null) return null;
        string? hex = c.Name.LocalName switch
        {
            "srgbClr" => (string?)c.Attribute("val"),
            "sysClr" => (string?)c.Attribute("lastClr") ?? ((string?)c.Attribute("val") == "window" ? "FFFFFF" : "000000"),
            "schemeClr" => Scheme((string?)c.Attribute("val"), styles),
            "prstClr" => Preset((string?)c.Attribute("val")),
            "scrgbClr" => null,
            "hslClr" => null,
            _ => null,
        };
        if (hex is null || hex.Length != 6) return null;
        return ApplyModifiers(hex.ToUpperInvariant(), c);
    }

    private static string? Scheme(string? name, CellStyleTable styles)
    {
        int slot = name switch
        {
            "dk1" or "tx1" => 0, "lt1" or "bg1" => 1, "dk2" or "tx2" => 2, "lt2" or "bg2" => 3,
            "accent1" => 4, "accent2" => 5, "accent3" => 6, "accent4" => 7, "accent5" => 8, "accent6" => 9,
            "hlink" => 10, "folHlink" => 11, _ => -1,
        };
        var colors = styles.Theme.SchemeColors;
        if (slot >= 0 && slot < colors.Count && colors[slot] is string fromTheme) return fromTheme;
        // No theme in the file: Office defaults.
        string[] office = { "000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72" };
        return slot >= 0 ? office[slot] : null;
    }

    private static string? Preset(string? name) => name switch
    {
        "black" => "000000", "white" => "FFFFFF", "red" => "FF0000", "green" => "008000", "blue" => "0000FF",
        "yellow" => "FFFF00", "gray" => "808080", "grey" => "808080", "orange" => "FFA500", "purple" => "800080",
        _ => null,
    };

    private static string ApplyModifiers(string hex, XElement c)
    {
        double r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0, g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0, b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;
        foreach (var m in c.Elements())
        {
            double v = (Long(m, "val") ?? 0) / 100000.0;
            switch (m.Name.LocalName)
            {
                case "lumMod": { RgbToHsl(r, g, b, out var h, out var s, out var l); l *= v; HslToRgb(h, s, Math.Clamp(l, 0, 1), out r, out g, out b); break; }
                case "lumOff": { RgbToHsl(r, g, b, out var h, out var s, out var l); l += v; HslToRgb(h, s, Math.Clamp(l, 0, 1), out r, out g, out b); break; }
                case "tint": r = 1 - (1 - r) * v; g = 1 - (1 - g) * v; b = 1 - (1 - b) * v; break;
                case "shade": r *= v; g *= v; b *= v; break;
            }
        }
        static int B(double x) => (int)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return B(r).ToString("X2") + B(g).ToString("X2") + B(b).ToString("X2");
    }

    private static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max == min) { h = s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h /= 6;
    }

    private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
    {
        if (s == 0) { r = g = b = l; return; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    // ---- Helpers -------------------------------------------------------------------

    private static long? Long(XElement e, string name)
        => long.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Resolves a relationship target relative to its source part ("../media/image1.png" from "/xl/drawings/drawing1.xml").</summary>
    internal static string ResolveTarget(string sourceUri, string target)
    {
        if (target.StartsWith("/", StringComparison.Ordinal)) return target;
        var parts = new List<string>(sourceUri.Substring(0, sourceUri.LastIndexOf('/')).Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var seg in target.Split('/'))
        {
            if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (seg != "." && seg.Length > 0) parts.Add(seg);
        }
        return "/" + string.Join("/", parts);
    }
}
