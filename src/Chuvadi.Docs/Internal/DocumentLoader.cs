using System;
using System.IO;
using System.Xml;
using Chuvadi.Docs.Word;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Builds an editable <see cref="Document"/> from a docx package stream. Preserves body
/// paragraphs with basic run formatting (bold/italic/underline/strike, font, size, color,
/// highlight), paragraph style/alignment/list membership, and tables with text cells.
/// Everything the model can't represent (images, fields, comments, tracked changes,
/// content controls, section variations) is dropped — see <see cref="Document"/> docs.
/// </summary>
internal static class DocumentLoader
{
    private const string W = DocumentSerializer.W;

    public static Document Load(Stream packageStream)
    {
        // Buffer once: we read the package twice — for the editable model and (via DocxReader)
        // for image extraction — and OoxmlPackage.Open closes the stream it is given.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            packageStream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        var doc = BuildModel(new MemoryStream(bytes));

        // Populate the Images collection from body, headers, and footers.
        try
        {
            using var reader = DocxReader.Open(new MemoryStream(bytes));
            foreach (var img in reader.Images())
                doc.AddLoadedImage(img);
        }
        catch
        {
            // Image extraction is best-effort; a malformed drawing must not fail document load.
        }

        return doc;
    }

    private static Document BuildModel(Stream packageStream)
    {
        using var pkg = OoxmlPackage.Open(packageStream);

        string? documentUri = null;
        foreach (var rel in pkg.GetRelationships("/"))
        {
            if (rel.Type.EndsWith("/officeDocument", StringComparison.Ordinal))
            {
                documentUri = "/" + rel.Target.TrimStart('/');
                break;
            }
        }
        if (documentUri is null)
            throw new DocxFormatException("Package has no officeDocument relationship; not a valid docx.");

        var doc = new Document();
        using var s = pkg.OpenPart(documentUri);
        using var r = XmlReader.Create(s, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
        });

        if (!r.Read()) return doc;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.ReadSubtree()) table = LoadTable(sub);
                    r.Read();
                    doc.AddBlock(table);
                    continue;
                }
                if (r.LocalName == "p" && IsBodyLevel(r))
                {
                    Paragraph p;
                    using (var sub = r.ReadSubtree()) p = LoadParagraph(sub);
                    r.Read();
                    doc.AddBlock(p);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        return doc;
    }

    // Body-level check: with ReadSubtree-based table skipping above, any w:p we reach here
    // is outside tables already; headers/footers are separate parts and never traversed.
    private static bool IsBodyLevel(XmlReader r) => true;

    private static Paragraph LoadParagraph(XmlReader sub)
    {
        var p = new Paragraph();

        // Run-property state collected from rPr before the run's text arrives.
        bool b = false, i = false, u = false, strike = false;
        string? font = null, color = null, highlight = null;
        double sizePt = 0;
        bool inRunProps = false;

        void ResetRun()
        {
            b = i = u = strike = false;
            font = color = highlight = null;
            sizePt = 0;
        }
        TextFormat CurrentFormat() => new()
        {
            Bold = b, Italic = i, Underline = u, Strikethrough = strike,
            Font = font, ColorHex = color, Highlight = highlight, SizePt = sizePt,
        };

        if (!sub.Read()) return p;
        while (!sub.EOF)
        {
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == W)
            {
                switch (sub.LocalName)
                {
                    case "pStyle":
                        p.Style = StyleFromId(sub.GetAttribute("val", W));
                        break;
                    case "jc":
                        p.Alignment = (sub.GetAttribute("val", W)) switch
                        {
                            "center" => ParagraphAlignment.Center,
                            "right" or "end" => ParagraphAlignment.Right,
                            "both" or "distribute" => ParagraphAlignment.Justify,
                            _ => ParagraphAlignment.Left,
                        };
                        break;
                    case "numId":
                        // Our own files: 1 = bullet, 2 = number. Foreign files: treat any
                        // list as a bullet (the abstract definitions aren't resolved here).
                        p.List = sub.GetAttribute("val", W) == "2" ? ListKind.Number : ListKind.Bullet;
                        if (p.Style == ParagraphStyle.Normal) p.Style = ParagraphStyle.ListParagraph;
                        break;
                    case "ilvl":
                        if (int.TryParse(sub.GetAttribute("val", W), out var lvl)) p.ListLevel = lvl;
                        break;
                    case "r":
                        ResetRun();
                        break;
                    case "rPr":
                        inRunProps = true;
                        break;
                    case "b" when inRunProps:
                        b = sub.GetAttribute("val", W) is not ("0" or "false");
                        break;
                    case "i" when inRunProps:
                        i = sub.GetAttribute("val", W) is not ("0" or "false");
                        break;
                    case "u" when inRunProps:
                        u = sub.GetAttribute("val", W) is not "none";
                        break;
                    case "strike" when inRunProps:
                        strike = sub.GetAttribute("val", W) is not ("0" or "false");
                        break;
                    case "rFonts" when inRunProps:
                        font = sub.GetAttribute("ascii", W) ?? sub.GetAttribute("hAnsi", W);
                        break;
                    case "color" when inRunProps:
                        var c = sub.GetAttribute("val", W);
                        color = (c is null or "auto") ? null : c;
                        break;
                    case "highlight" when inRunProps:
                        highlight = sub.GetAttribute("val", W);
                        break;
                    case "sz" when inRunProps:
                        if (double.TryParse(sub.GetAttribute("val", W), out var half)) sizePt = half / 2.0;
                        break;
                    case "br" when sub.GetAttribute("type", W) == "page":
                        p.PageBreakBefore = true;
                        break;
                    case "t":
                        var text = sub.ReadElementContentAsString();
                        if (text.Length > 0) p.AddRun(new Run(text, CurrentFormat()));
                        continue; // cursor already advanced
                }
            }
            else if (sub.NodeType == XmlNodeType.EndElement && sub.NamespaceURI == W && sub.LocalName == "rPr")
            {
                inRunProps = false;
            }
            if (!sub.Read()) break;
        }
        return p;
    }

    private static DocTable LoadTable(XmlReader sub)
    {
        // First pass over the subtree: parse into row/cell text via DocxReader's grid
        // parser, then rebuild as a DocTable. Formatting inside foreign cells is reduced
        // to text — consistent with the documented load contract.
        var grid = DocxReader.ParseTable(sub);
        int columns = 1;
        foreach (var row in grid) columns = Math.Max(columns, row.Length);

        var t = new DocTable(columns);
        foreach (var row in grid)
        {
            var tr = t.AddRow();
            for (int c = 0; c < row.Length && c < columns; c++)
                tr.Cell(c).SetText(row[c]);
        }
        return t;
    }

    private static ParagraphStyle StyleFromId(string? id) => id switch
    {
        "Title" => ParagraphStyle.Title,
        "Heading1" => ParagraphStyle.Heading1,
        "Heading2" => ParagraphStyle.Heading2,
        "Heading3" => ParagraphStyle.Heading3,
        "Quote" => ParagraphStyle.Quote,
        "ListParagraph" => ParagraphStyle.ListParagraph,
        _ => ParagraphStyle.Normal,
    };
}
