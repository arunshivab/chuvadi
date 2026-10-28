using System;
using System.IO;
using System.Threading;
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

    public static Document Load(Stream packageStream) => Load(packageStream, new DocxReaderOptions(), CancellationToken.None);

    public static Document Load(Stream packageStream, DocxReaderOptions options, CancellationToken ct)
    {
        // Buffer once: we read the package twice — for the editable model and (via DocxReader)
        // for image extraction — and OoxmlPackage.Open closes the stream it is given.
        byte[] bytes;
        using (var ms = PackageGuard.CopyBounded(packageStream, options, ct))
            bytes = ms.ToArray();

        using (var scanStream = new MemoryStream(bytes, writable: false))
            PackageGuard.Scan(scanStream, options, ct);
        var doc = BuildModel(new MemoryStream(bytes, writable: false), options, ct);

        // Populate the Images collection from body, headers, and footers.
        try
        {
            using var reader = DocxReader.Open(new MemoryStream(bytes, writable: false), null, options, ct);
            foreach (var img in reader.Images(ct))
                doc.AddLoadedImage(img);
        }
        catch (Exception ex) when (ex is not (DocxSecurityLimitException or OperationCanceledException))
        {
            // Image extraction is best-effort; a malformed drawing must not fail document load.
            // Security-limit breaches and cancellation are never swallowed.
        }

        return doc;
    }

    private static Document BuildModel(Stream packageStream, DocxReaderOptions options, CancellationToken ct)
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
        using var raw = pkg.OpenPart(documentUri);
        using var s = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, documentUri);
        using var r = GuardedXmlReader.Create(s, options.MaxXmlDepth, ct, documentUri);

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

    /// <summary>
    /// Loads one <c>w:p</c> subtree: paragraph properties from <c>w:pPr</c>, then every run
    /// (<c>w:r</c>) wherever it sits — directly, or inside hyperlinks, insertions, smart tags,
    /// simple fields or content controls — with its full run formatting.
    /// </summary>
    internal static Paragraph LoadParagraph(XmlReader sub)
    {
        var p = new Paragraph();
        if (!sub.Read()) return p; // positioned on <w:p>

        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element) continue;
            if (IsAlternateRepresentation(sub))
            {
                using (sub.ReadSubtree()) { } // mc:Fallback duplicates mc:Choice
                continue;
            }
            if (sub.NamespaceURI != W) continue;
            switch (sub.LocalName)
            {
                case "moveFrom":
                case "del":
                    // Moved-away / deleted content: not part of the current text. (Revision
                    // reading is a later phase; skipping avoids showing moved text twice.)
                    using (sub.ReadSubtree()) { }
                    break;
                case "pPr":
                    using (var pPr = sub.ReadSubtree()) LoadParagraphProperties(pPr, p);
                    break;
                case "r":
                    using (var r = sub.ReadSubtree()) LoadRun(r, p);
                    break;
            }
        }
        return p;
    }

    private static void LoadParagraphProperties(XmlReader sub, Paragraph p)
    {
        if (!sub.Read()) return;
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != W) continue;
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
                    var numId = sub.GetAttribute("val", W);
                    if (numId is null or "0") break; // numId 0 = explicitly not a list
                    p.List = numId == "2" ? ListKind.Number : ListKind.Bullet;
                    if (p.Style == ParagraphStyle.Normal) p.Style = ParagraphStyle.ListParagraph;
                    break;
                case "ilvl":
                    if (int.TryParse(sub.GetAttribute("val", W), out var lvl)) p.ListLevel = lvl;
                    break;
                case "rPr":        // paragraph-mark formatting: not part of the run model
                case "pPrChange":  // tracked-change record of the PREVIOUS properties
                case "sectPr":
                    // Disposing a subtree reader leaves the cursor on the element's end tag,
                    // so the loop's next Read continues with the following sibling.
                    using (sub.ReadSubtree()) { }
                    break;
            }
        }
    }

    /// <summary>Loads one <c>w:r</c>: its run properties, then each content child in order.</summary>
    private static void LoadRun(XmlReader sub, Paragraph p)
    {
        var format = TextFormat.None;
        if (!sub.Read()) return; // positioned on <w:r>

        // ReadElementContentAsString leaves the cursor on the NEXT node, so the loop only
        // advances when the previous step did not already do so.
        bool advance = true;
        while (true)
        {
            if (advance && !sub.Read()) break;
            advance = true;
            if (sub.EOF) break;
            if (sub.NodeType != XmlNodeType.Element) continue;

            if (IsAlternateRepresentation(sub))
            {
                using (sub.ReadSubtree()) { } // mc:Fallback duplicates mc:Choice (text boxes)
                continue;
            }

            if (sub.LocalName is "drawing" or "pict" or "object")
            {
                // Text boxes / shapes: keep their words, flattened (geometry is outside the model).
                AppendEmbeddedText(sub, p);
                continue;
            }
            if (sub.NamespaceURI != W) continue;

            switch (sub.LocalName)
            {
                case "rPr":
                    using (var rPr = sub.ReadSubtree()) format = RunProperties.Read(rPr, out _);
                    break;
                case "t":
                    var text = sub.ReadElementContentAsString();
                    if (text.Length > 0) p.AddRun(new Run(text, format));
                    advance = false;
                    break;
                case "tab":
                    p.AddRun(Run.TabRun(format));
                    break;
                case "br":
                    var type = sub.GetAttribute("type", W);
                    if (type == "page" && p.Runs.Count == 0)
                        p.PageBreakBefore = true; // the shape Document.AddPageBreak writes
                    else
                        p.AddRun(Run.BreakRun(ParseBreak(type, sub.GetAttribute("clear", W)), format));
                    break;
                case "cr":
                    p.AddRun(Run.BreakRun(BreakType.Line, format));
                    break;
                case "noBreakHyphen":
                    p.AddRun(new Run("\u2011", format));
                    break;
                case "softHyphen":
                    p.AddRun(new Run("\u00AD", format));
                    break;
            }
        }
    }

    /// <summary>
    /// Flattens the text of text boxes inside a drawing / VML shape into a plain run, so
    /// their words are not lost (shape geometry is outside the model; images are exposed
    /// separately through <see cref="Document.Images"/>). Consumes the element's subtree.
    /// </summary>
    private static void AppendEmbeddedText(XmlReader sub, Paragraph p)
    {
        var sb = new System.Text.StringBuilder();
        using (var inner = sub.ReadSubtree())
        {
            bool advance = true;
            while (true)
            {
                if (advance && !inner.Read()) break;
                advance = true;
                if (inner.EOF) break;
                if (IsAlternateRepresentation(inner))
                {
                    inner.Skip();
                    advance = false;
                    continue;
                }
                if (inner.NodeType == XmlNodeType.Element && inner.NamespaceURI == W && inner.LocalName == "t")
                {
                    sb.Append(inner.ReadElementContentAsString());
                    advance = false;
                }
            }
        }
        if (sb.Length > 0) p.AddRun(new Run(sb.ToString(), TextFormat.None));
    }

    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>
    /// True for <c>mc:Fallback</c>: Word writes text boxes, shapes and some newer features
    /// as mc:AlternateContent with a Choice and a Fallback carrying the SAME content in an
    /// older form. Reading both would duplicate it; the Choice is kept.
    /// </summary>
    internal static bool IsAlternateRepresentation(XmlReader r)
        => r.NodeType == XmlNodeType.Element && r.LocalName == "Fallback" && r.NamespaceURI == Mc;

    private static BreakType ParseBreak(string? type, string? clear) => type switch
    {
        "page" => BreakType.Page,
        "column" => BreakType.Column,
        _ => clear switch
        {
            "left" => BreakType.TextWrappingClearLeft,
            "right" => BreakType.TextWrappingClearRight,
            "all" => BreakType.TextWrappingClearAll,
            _ => BreakType.Line,
        },
    };

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
