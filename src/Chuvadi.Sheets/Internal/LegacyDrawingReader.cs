using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Reads comments (xl/commentsN.xml with their VML shapes) and header/footer pictures (the
/// legacyDrawingHF VML part). VML is read with tolerant pattern matching: Excel's VML is not
/// always well-formed XML (unclosed &lt;br&gt;), and only a few values are needed from it.
/// </summary>
internal static class LegacyDrawingReader
{
    private static readonly XmlReaderSettings Settings = new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = false,
        DtdProcessing = DtdProcessing.Prohibit,
        CloseInput = false,
    };

    // ---- Comments ----------------------------------------------------------------------

    public static List<CellComment> ReadComments(XlsxReader book, string sheetPartUri, string? legacyDrawingRelId)
    {
        var result = new List<CellComment>();
        var rels = book.PartRelationships(sheetPartUri).ToList();
        var commentsRel = rels.FirstOrDefault(r => !r.IsExternal && r.Type.EndsWith("/comments", StringComparison.Ordinal));
        if (commentsRel is null) return result;
        var commentsUri = DrawingReader.ResolveTarget(sheetPartUri, commentsRel.Target);
        if (!book.HasPart(commentsUri)) return result;

        // VML shapes: anchor and visibility per (row, column), 0-based as stored.
        var shapes = new Dictionary<(int, int), (CommentAnchor? Anchor, bool Visible)>();
        if (legacyDrawingRelId is not null && rels.FirstOrDefault(r => r.Id == legacyDrawingRelId && !r.IsExternal) is { } vmlRel)
        {
            var vmlUri = DrawingReader.ResolveTarget(sheetPartUri, vmlRel.Target);
            if (book.HasPart(vmlUri))
                foreach (var s in NoteShapes(ReadText(book, vmlUri))) shapes.TryAdd((s.Row, s.Column), (s.Anchor, s.Visible));
        }

        var authors = new List<string>();
        using var stream = book.OpenPartLimited(commentsUri);
        using var r = XmlReader.Create(stream, Settings);
        var ct = book.Options.CancellationToken;
        int n = 0;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "author")
            {
                var sb = new StringBuilder();
                if (!r.IsEmptyElement)
                {
                    int d = r.Depth;
                    while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == d))
                        if (r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) sb.Append(r.Value);
                }
                authors.Add(sb.ToString());
            }
            else if (r.LocalName == "comment")
            {
                if ((++n & 255) == 0) ct.ThrowIfCancellationRequested();
                var reference = r.GetAttribute("ref");
                int authorId = int.TryParse(r.GetAttribute("authorId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : -1;
                var text = new StringBuilder();
                RichText? rich = null;
                if (!r.IsEmptyElement)
                {
                    int d = r.Depth;
                    while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == d))
                    {
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "text" && r.Depth == d + 1)
                            rich = RichTextXml.ReadItem(r, text);
                    }
                }
                if (string.IsNullOrEmpty(reference) || !TryCell(reference, out int row, out int col)) continue;
                var author = authorId >= 0 && authorId < authors.Count ? authors[authorId] : string.Empty;
                if (rich is not null) RichTextXml.Resolve(rich, book.StyleTable);
                shapes.TryGetValue((row - 1, col - 1), out var shape);
                result.Add(CellComment.Loaded(reference, author, text.ToString(), rich, shape.Anchor, shape.Visible));
            }
        }
        return result;
    }

    private static bool TryCell(string reference, out int row, out int col)
    {
        row = col = 0;
        try { (row, col) = CellAddress.ParseA1(reference.Replace("$", "")); }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
        return row is >= 1 and <= SheetReader.MaxRow && col is >= 1 and <= SheetReader.MaxColumn;
    }

    private static readonly Regex ShapeRx = new(@"<(?:\w+:)?shape\b(?<attrs>[^>]*)>(?<body>.*?)</(?:\w+:)?shape\s*>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
    private static readonly Regex ClientDataRx = new(@"<(?:\w+:)?ClientData\b[^>]*ObjectType\s*=\s*""(?<type>[^""]*)""[^>]*>(?<body>.*?)</(?:\w+:)?ClientData\s*>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static string? Tag(string body, string name)
    {
        var m = Regex.Match(body, @"<(?:\w+:)?" + name + @"\s*>(?<v>[^<]*)</(?:\w+:)?" + name + @"\s*>",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    private static string? Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, @"(?:^|\s)" + Regex.Escape(name) + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value) : null;
    }

    private static IEnumerable<(int Row, int Column, CommentAnchor? Anchor, bool Visible)> NoteShapes(string vml)
    {
        foreach (Match m in ShapeRx.Matches(vml))
        {
            var cd = ClientDataRx.Match(m.Groups["body"].Value);
            if (!cd.Success || !string.Equals(cd.Groups["type"].Value, "Note", StringComparison.OrdinalIgnoreCase)) continue;
            var body = cd.Groups["body"].Value;
            if (!int.TryParse(Tag(body, "Row"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row)) continue;
            if (!int.TryParse(Tag(body, "Column"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var col)) continue;
            CommentAnchor? anchor = null;
            if (Tag(body, "Anchor") is { } a)
            {
                var p = a.Split(',');
                if (p.Length == 8)
                {
                    var v = new int[8];
                    bool ok = true;
                    for (int i = 0; i < 8; i++) ok &= int.TryParse(p[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i]);
                    if (ok) anchor = new CommentAnchor(v[0] + 1, v[1], v[2] + 1, v[3], v[4] + 1, v[5], v[6] + 1, v[7]);
                }
            }
            bool visible = Regex.IsMatch(body, @"<(?:\w+:)?Visible\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
            if (Attr(m.Groups["attrs"].Value, "style") is { } style && style.Contains("visibility:visible", StringComparison.OrdinalIgnoreCase))
                visible = true;
            yield return (row, col, anchor, visible);
        }
    }

    private static string ReadText(XlsxReader book, string partUri)
    {
        using var s = book.OpenPartLimited(partUri);
        using var sr = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    // ---- Header / footer pictures -------------------------------------------------------

    public static List<HeaderFooterPicture> ReadHeaderFooterPictures(XlsxReader book, string sheetPartUri, string? relId, bool eager, bool loadImages)
    {
        var result = new List<HeaderFooterPicture>();
        if (relId is null) return result;
        var rel = book.PartRelationships(sheetPartUri).FirstOrDefault(r => r.Id == relId && !r.IsExternal);
        if (rel is null) return result;
        var vmlUri = DrawingReader.ResolveTarget(sheetPartUri, rel.Target);
        if (!book.HasPart(vmlUri)) return result;
        var vml = ReadText(book, vmlUri);
        var vmlRels = book.PartRelationships(vmlUri).ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);

        foreach (Match m in ShapeRx.Matches(vml))
        {
            var attrs = m.Groups["attrs"].Value;
            var id = Attr(attrs, "id");
            if (id is null || !HeaderFooterPicture.TryParsePosition(id, out var section, out var part, out var kind)) continue;
            var img = Regex.Match(m.Groups["body"].Value, @"<(?:\w+:)?imagedata\b(?<a>[^>]*)/?>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
            if (!img.Success) continue;
            var relid = Attr(img.Groups["a"].Value, "o:relid") ?? Attr(img.Groups["a"].Value, "r:id");
            var pic = new HeaderFooterPicture
            {
                Id = id, Section = section, Part = part, Kind = kind,
                Title = Attr(img.Groups["a"].Value, "o:title"),
            };
            if (Attr(attrs, "style") is { } style)
            {
                pic.WidthPoints = StyleLength(style, "width");
                pic.HeightPoints = StyleLength(style, "height");
            }
            if (relid is not null && vmlRels.TryGetValue(relid, out var ir))
            {
                if (ir.IsExternal) pic.LinkedImageTarget = ir.Target;
                else
                {
                    var uri = DrawingReader.ResolveTarget(vmlUri, ir.Target);
                    if (book.HasPart(uri))
                    {
                        pic.ImagePartName = uri;
                        pic.ContentType = book.PartContentType(uri);
                        if (loadImages)
                        {
                            if (eager) pic.ImageData = ReadBytes(book, uri);
                            else pic.ImageLoader = () => ReadBytes(book, uri);
                        }
                    }
                }
            }
            result.Add(pic);
        }
        return result;
    }

    private static byte[] ReadBytes(XlsxReader book, string uri)
    {
        using var s = book.OpenPartLimited(uri);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A CSS length from a VML style ("width:192.75pt") in points; null when absent or not convertible.</summary>
    private static double? StyleLength(string style, string name)
    {
        var m = Regex.Match(style, @"(?:^|;)\s*" + name + @"\s*:\s*(?<n>[-+]?[0-9]*\.?[0-9]+)\s*(?<u>pt|px|in|cm|mm|pc)?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        if (!m.Success || !double.TryParse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "" or "px" => v * 0.75,
            "pt" => v,
            "in" => v * 72,
            "cm" => v * 72 / 2.54,
            "mm" => v * 72 / 25.4,
            "pc" => v * 12,
            _ => null,
        };
    }
}
