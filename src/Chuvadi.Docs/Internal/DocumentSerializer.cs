using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Chuvadi.Docs.Word;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Serializes a <see cref="Document"/> into a complete WordprocessingML package:
/// /word/document.xml (+ .rels), /word/styles.xml, /word/numbering.xml (when lists exist),
/// /word/settings.xml, header/footer parts, docProps, [Content_Types].xml and root .rels —
/// all through the shared <see cref="OoxmlPackage"/> plumbing. All XML is emitted via
/// XmlWriter (automatic escaping; no string-concatenated markup).
///
/// "Word as ground truth": the structures here follow ECMA-376 WordprocessingML and are
/// cross-validated against python-docx (an independent OOXML implementation) in the test
/// suite.
/// </summary>
internal static class DocumentSerializer
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string CtDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string CtStyles = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    private const string CtNumbering = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    private const string CtSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    private const string CtHeader = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string CtFooter = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    private const string CtCore = "application/vnd.openxmlformats-package.core-properties+xml";
    private const string CtApp = "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    private const string RelOfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    private const string RelSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    private const string RelHeader = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    private const string RelFooter = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
    private const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string RelCore = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string RelApp = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    private const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    // DrawingML namespaces for images.
    internal const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    internal const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    internal const string PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>English Metric Units per point (1 pt = 12,700 EMU).</summary>
    internal const long EmuPerPoint = 12700;

    /// <summary>
    /// Per-part write context: accumulates the hyperlink and image relationships that a
    /// single part (document.xml, a header, or a footer) needs, and assigns shared media
    /// parts through a package-wide registry. Each host part has its own relationship id
    /// space, so ids only need to be unique within the part.
    /// </summary>
    internal sealed class PartContext
    {
        public string PartUri { get; }
        public List<(string RelId, string Url)> Hyperlinks { get; } = new();
        public List<(string RelId, string MediaUri)> Images { get; } = new();

        private readonly Func<ImageSpec, string> _registerMedia;
        private readonly Func<int> _nextDrawingId;
        private int _linkCounter;
        private int _imageCounter;

        public PartContext(string partUri, Func<ImageSpec, string> registerMedia, Func<int> nextDrawingId)
        {
            PartUri = partUri;
            _registerMedia = registerMedia;
            _nextDrawingId = nextDrawingId;
        }

        public string AddHyperlink(string url)
        {
            var id = $"rIdLink{++_linkCounter}";
            Hyperlinks.Add((id, url));
            return id;
        }

        /// <summary>Registers the image as a media part and returns the relationship id for this part.</summary>
        public string AddImage(ImageSpec image)
        {
            var mediaUri = _registerMedia(image);
            var id = $"rIdImg{++_imageCounter}";
            Images.Add((id, mediaUri));
            return id;
        }

        public int NextDrawingId() => _nextDrawingId();
    }

    public static void Write(Stream output, Document doc)
    {
        using var pkg = OoxmlPackage.Create(output);
        bool hasLists = DocumentHasLists(doc);

        // Package-wide media registry: each image becomes a unique /word/media/imageN.ext
        // part, written after all the XML parts. A global drawing-id counter keeps every
        // <wp:docPr id="..."> unique across the whole document, as Word expects.
        var mediaParts = new List<(string Uri, ImageSpec Image)>();
        int mediaCounter = 0;
        int drawingIdCounter = 0;
        string RegisterMedia(ImageSpec img)
        {
            mediaCounter++;
            var ext = ImageMetadata.ExtensionFor(img.ContentType);
            var uri = $"/word/media/image{mediaCounter}.{ext}";
            mediaParts.Add((uri, img));
            return uri;
        }
        int NextDrawingId() => ++drawingIdCounter;

        var bodyCtx = new PartContext("/word/document.xml", RegisterMedia, NextDrawingId);

        // ---- /word/document.xml ----
        using (var s = pkg.CreatePart("/word/document.xml", CtDocument))
        using (var x = CreateXml(s))
        {
            x.WriteStartDocument(standalone: true);
            x.WriteStartElement("w", "document", W);
            x.WriteAttributeString("xmlns", "r", null, R);
            x.WriteStartElement("body", W);

            object? previous = null;
            foreach (var block in doc.Blocks)
            {
                // Two adjacent tables merge into one in Word; separate them with an
                // empty paragraph so authoring intent is preserved.
                if (block is DocTable && previous is DocTable)
                    WriteEmptyParagraph(x);

                if (block is Paragraph p) WriteParagraph(x, p, bodyCtx);
                else if (block is DocTable t) WriteTable(x, t, bodyCtx);
                previous = block;
            }

            // A table as the last body child (directly before sectPr) trips some Word
            // versions; always close with a paragraph in that case.
            if (previous is DocTable) WriteEmptyParagraph(x);

            WriteSectionProperties(x, doc);

            x.WriteEndElement(); // body
            x.WriteEndElement(); // document
            x.WriteEndDocument();
        }

        // ---- /word/styles.xml ----
        using (var s = pkg.CreatePart("/word/styles.xml", CtStyles))
        using (var x = CreateXml(s))
        {
            WriteStyles(x);
        }

        // ---- /word/numbering.xml (only when lists are used) ----
        if (hasLists)
        {
            using var s = pkg.CreatePart("/word/numbering.xml", CtNumbering);
            using var x = CreateXml(s);
            WriteNumbering(x);
        }

        // ---- /word/settings.xml (always present; carries documentProtection when set) ----
        using (var s = pkg.CreatePart("/word/settings.xml", CtSettings))
        using (var x = CreateXml(s))
        {
            WriteSettings(x, doc);
        }

        // ---- header/footer parts ----
        int hfIndex = 1;
        var hfParts = new List<(string Uri, string RelId, string RelType, HeaderFooterContent Content, bool IsHeader)>();
        void AddHf(HeaderFooterContent c, bool isHeader)
        {
            if (!c.HasContent) return;
            var uri = $"/word/{(isHeader ? "header" : "footer")}{hfIndex}.xml";
            hfParts.Add((uri, $"rIdHf{hfIndex}", isHeader ? RelHeader : RelFooter, c, isHeader));
            hfIndex++;
        }
        AddHf(doc.Header, isHeader: true);
        AddHf(doc.Footer, isHeader: false);
        AddHf(doc.FirstPageHeader, isHeader: true);
        AddHf(doc.FirstPageFooter, isHeader: false);

        var hfContexts = new List<PartContext>();
        foreach (var (uri, _, _, content, isHeader) in hfParts)
        {
            var hfCtx = new PartContext(uri, RegisterMedia, NextDrawingId);
            hfContexts.Add(hfCtx);
            using var s = pkg.CreatePart(uri, isHeader ? CtHeader : CtFooter);
            using var x = CreateXml(s);
            x.WriteStartDocument(standalone: true);
            x.WriteStartElement("w", isHeader ? "hdr" : "ftr", W);
            x.WriteAttributeString("xmlns", "r", null, R);
            foreach (var p in content) WriteParagraph(x, p, hfCtx);
            x.WriteEndElement();
            x.WriteEndDocument();
        }

        // ---- media parts (binary) ----
        foreach (var (uri, img) in mediaParts)
        {
            using var s = pkg.CreatePart(uri, img.ContentType);
            s.Write(img.Bytes, 0, img.Bytes.Length);
        }

        // ---- docProps ----
        WriteCoreProps(pkg);
        WriteAppProps(pkg);

        // ---- relationships ----
        pkg.AddRelationship("/", "/word/document.xml", RelOfficeDocument, "rId1");
        pkg.AddRelationship("/", "/docProps/core.xml", RelCore, "rId2");
        pkg.AddRelationship("/", "/docProps/app.xml", RelApp, "rId3");

        pkg.AddRelationship("/word/document.xml", "/word/styles.xml", RelStyles, "rIdStyles");
        pkg.AddRelationship("/word/document.xml", "/word/settings.xml", RelSettings, "rIdSettings");
        if (hasLists)
            pkg.AddRelationship("/word/document.xml", "/word/numbering.xml", RelNumbering, "rIdNumbering");
        foreach (var (uri, relId, relType, _, _) in hfParts)
            pkg.AddRelationship("/word/document.xml", uri, relType, relId);

        // Per-part hyperlink + image relationships (body and each header/footer).
        void WriteContextRels(PartContext ctx)
        {
            foreach (var (relId, url) in ctx.Hyperlinks)
                pkg.AddExternalRelationship(ctx.PartUri, url, RelHyperlink, relId);
            foreach (var (relId, mediaUri) in ctx.Images)
                pkg.AddRelationship(ctx.PartUri, mediaUri, RelImage, relId);
        }
        WriteContextRels(bodyCtx);
        foreach (var ctx in hfContexts) WriteContextRels(ctx);

        pkg.Close();
    }

    // ---- Body blocks -----------------------------------------------------------------

    private static void WriteEmptyParagraph(XmlWriter x)
    {
        x.WriteStartElement("p", W);
        x.WriteEndElement();
    }

    internal static void WriteParagraph(XmlWriter x, Paragraph p, PartContext? ctx)
    {
        x.WriteStartElement("p", W);

        // pPr — paragraph properties. Child order matters: pStyle, numPr, spacing, jc...
        bool needsPpr = p.Style != ParagraphStyle.Normal
            || p.Alignment != ParagraphAlignment.Left
            || p.List != ListKind.None;
        if (needsPpr)
        {
            x.WriteStartElement("pPr", W);
            if (p.Style != ParagraphStyle.Normal)
            {
                x.WriteStartElement("pStyle", W);
                x.WriteAttributeString("val", W, StyleId(p.Style));
                x.WriteEndElement();
            }
            if (p.List != ListKind.None)
            {
                x.WriteStartElement("numPr", W);
                x.WriteStartElement("ilvl", W);
                x.WriteAttributeString("val", W, Math.Clamp(p.ListLevel, 0, 8).ToString(CultureInfo.InvariantCulture));
                x.WriteEndElement();
                x.WriteStartElement("numId", W);
                x.WriteAttributeString("val", W, p.List == ListKind.Bullet ? "1" : "2");
                x.WriteEndElement();
                x.WriteEndElement();
            }
            if (p.Alignment != ParagraphAlignment.Left)
            {
                x.WriteStartElement("jc", W);
                x.WriteAttributeString("val", W, p.Alignment switch
                {
                    ParagraphAlignment.Center => "center",
                    ParagraphAlignment.Right => "right",
                    ParagraphAlignment.Justify => "both",
                    _ => "left",
                });
                x.WriteEndElement();
            }
            x.WriteEndElement(); // pPr
        }

        if (p.PageBreakBefore)
        {
            x.WriteStartElement("r", W);
            x.WriteStartElement("br", W);
            x.WriteAttributeString("type", W, "page");
            x.WriteEndElement();
            x.WriteEndElement();
        }

        foreach (var run in p.Runs)
        {
            if (run.FieldInstruction is not null)
            {
                // <w:fldSimple w:instr=" PAGE "><w:r><w:t>1</w:t></w:r></w:fldSimple>
                x.WriteStartElement("fldSimple", W);
                x.WriteAttributeString("instr", W, run.FieldInstruction);
                x.WriteStartElement("r", W);
                WriteText(x, run.TextContent);
                x.WriteEndElement();
                x.WriteEndElement();
                continue;
            }

            if (run.Image is not null && ctx is not null)
            {
                WriteDrawingRun(x, run.Image, ctx);
                continue;
            }

            if (run.HyperlinkUrl is not null && ctx is not null)
            {
                var relId = ctx.AddHyperlink(run.HyperlinkUrl);
                x.WriteStartElement("hyperlink", W);
                x.WriteAttributeString("id", R, relId);
                x.WriteAttributeString("history", W, "1");
                x.WriteStartElement("r", W);
                x.WriteStartElement("rPr", W);
                x.WriteStartElement("rStyle", W);
                x.WriteAttributeString("val", W, "Hyperlink");
                x.WriteEndElement();
                x.WriteEndElement();
                WriteText(x, run.TextContent);
                x.WriteEndElement();
                x.WriteEndElement();
                continue;
            }

            // Skip empty non-image runs (e.g. an image run reached without a context).
            if (run.Image is not null) continue;

            x.WriteStartElement("r", W);
            WriteRunProperties(x, run.Format);
            WriteText(x, run.TextContent);
            x.WriteEndElement();
        }

        x.WriteEndElement(); // p
    }

    // ---- Drawing (image) emission ----------------------------------------------------

    /// <summary>Writes a run containing a w:drawing: inline or floating (wp:anchor).</summary>
    private static void WriteDrawingRun(XmlWriter x, ImageSpec image, PartContext ctx)
    {
        var relId = ctx.AddImage(image);
        int docPrId = ctx.NextDrawingId();
        long cx = (long)Math.Round(image.WidthPt * EmuPerPoint);
        long cy = (long)Math.Round(image.HeightPt * EmuPerPoint);
        string name = $"Picture {docPrId}";
        string? alt = image.AltText;

        x.WriteStartElement("r", W);
        x.WriteStartElement("drawing", W);

        if (image.Placement == ImagePlacement.Floating && image.Position is not null)
            WriteAnchor(x, image.Position, cx, cy, docPrId, name, alt, relId);
        else
            WriteInline(x, cx, cy, docPrId, name, alt, relId);

        x.WriteEndElement(); // drawing
        x.WriteEndElement(); // r
    }

    private static void WriteInline(XmlWriter x, long cx, long cy, int docPrId, string name, string? alt, string relId)
    {
        x.WriteStartElement("wp", "inline", WP);
        x.WriteAttributeString("distT", "0");
        x.WriteAttributeString("distB", "0");
        x.WriteAttributeString("distL", "0");
        x.WriteAttributeString("distR", "0");
        WriteExtent(x, cx, cy);
        WriteEffectExtent(x);
        WriteDocPr(x, docPrId, name, alt);
        WriteGraphicFrameLocks(x);
        WriteGraphic(x, cx, cy, name, relId);
        x.WriteEndElement(); // inline
    }

    private static void WriteAnchor(XmlWriter x, FloatingPosition pos, long cx, long cy,
        int docPrId, string name, string? alt, string relId)
    {
        long dist = (long)Math.Round(pos.DistanceFromTextPt * EmuPerPoint);
        x.WriteStartElement("wp", "anchor", WP);
        x.WriteAttributeString("distT", dist.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("distB", dist.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("distL", dist.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("distR", dist.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("simplePos", "0");
        x.WriteAttributeString("relativeHeight",
            (pos.RelativeHeight ?? (251658240L + docPrId)).ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("behindDoc", pos.Wrap == TextWrap.None && pos.BehindText ? "1" : "0");
        x.WriteAttributeString("locked", pos.LockAnchor ? "1" : "0");
        x.WriteAttributeString("layoutInCell", "1");
        x.WriteAttributeString("allowOverlap", pos.AllowOverlap ? "1" : "0");

        x.WriteStartElement("wp", "simplePos", WP);
        x.WriteAttributeString("x", "0");
        x.WriteAttributeString("y", "0");
        x.WriteEndElement();

        // Horizontal position.
        x.WriteStartElement("wp", "positionH", WP);
        x.WriteAttributeString("relativeFrom", HorizontalAnchorValue(pos.HorizontalAnchor));
        if (pos.HAlign is HorizontalAlignment ha)
        {
            x.WriteStartElement("wp", "align", WP);
            x.WriteString(ha switch
            {
                HorizontalAlignment.Left => "left",
                HorizontalAlignment.Center => "center",
                HorizontalAlignment.Right => "right",
                HorizontalAlignment.Inside => "inside",
                HorizontalAlignment.Outside => "outside",
                _ => "left",
            });
            x.WriteEndElement();
        }
        else
        {
            x.WriteStartElement("wp", "posOffset", WP);
            x.WriteString(((long)Math.Round(pos.HorizontalOffsetPt * EmuPerPoint)).ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        x.WriteEndElement(); // positionH

        // Vertical position.
        x.WriteStartElement("wp", "positionV", WP);
        x.WriteAttributeString("relativeFrom", VerticalAnchorValue(pos.VerticalAnchor));
        if (pos.VAlign is VerticalAlignment va)
        {
            x.WriteStartElement("wp", "align", WP);
            x.WriteString(va switch
            {
                VerticalAlignment.Top => "top",
                VerticalAlignment.Center => "center",
                VerticalAlignment.Bottom => "bottom",
                VerticalAlignment.Inside => "inside",
                VerticalAlignment.Outside => "outside",
                _ => "top",
            });
            x.WriteEndElement();
        }
        else
        {
            x.WriteStartElement("wp", "posOffset", WP);
            x.WriteString(((long)Math.Round(pos.VerticalOffsetPt * EmuPerPoint)).ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        x.WriteEndElement(); // positionV

        WriteExtent(x, cx, cy);
        WriteEffectExtent(x);
        WriteWrap(x, pos);
        WriteDocPr(x, docPrId, name, alt);
        WriteGraphicFrameLocks(x);
        WriteGraphic(x, cx, cy, name, relId);
        x.WriteEndElement(); // anchor
    }

    private static void WriteWrap(XmlWriter x, FloatingPosition pos)
    {
        switch (pos.Wrap)
        {
            case TextWrap.None:
                x.WriteStartElement("wp", "wrapNone", WP);
                x.WriteEndElement();
                break;
            case TextWrap.Square:
                x.WriteStartElement("wp", "wrapSquare", WP);
                x.WriteAttributeString("wrapText", "bothSides");
                x.WriteEndElement();
                break;
            case TextWrap.Tight:
                x.WriteStartElement("wp", "wrapTight", WP);
                x.WriteAttributeString("wrapText", "bothSides");
                WriteDefaultWrapPolygon(x);
                x.WriteEndElement();
                break;
            case TextWrap.Through:
                x.WriteStartElement("wp", "wrapThrough", WP);
                x.WriteAttributeString("wrapText", "bothSides");
                WriteDefaultWrapPolygon(x);
                x.WriteEndElement();
                break;
            case TextWrap.TopAndBottom:
                x.WriteStartElement("wp", "wrapTopAndBottom", WP);
                x.WriteEndElement();
                break;
        }
    }

    private static void WriteDefaultWrapPolygon(XmlWriter x)
    {
        // A simple rectangular wrap polygon (full extent) — Tight/Through still need one.
        x.WriteStartElement("wp", "wrapPolygon", WP);
        x.WriteAttributeString("edited", "0");
        void Pt(string elem, long xx, long yy)
        {
            x.WriteStartElement("wp", elem, WP);
            x.WriteAttributeString("x", xx.ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("y", yy.ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        Pt("start", 0, 0);
        Pt("lineTo", 0, 21600);
        Pt("lineTo", 21600, 21600);
        Pt("lineTo", 21600, 0);
        Pt("lineTo", 0, 0);
        x.WriteEndElement();
    }

    private static void WriteExtent(XmlWriter x, long cx, long cy)
    {
        x.WriteStartElement("wp", "extent", WP);
        x.WriteAttributeString("cx", cx.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("cy", cy.ToString(CultureInfo.InvariantCulture));
        x.WriteEndElement();
    }

    private static void WriteEffectExtent(XmlWriter x)
    {
        x.WriteStartElement("wp", "effectExtent", WP);
        x.WriteAttributeString("l", "0");
        x.WriteAttributeString("t", "0");
        x.WriteAttributeString("r", "0");
        x.WriteAttributeString("b", "0");
        x.WriteEndElement();
    }

    private static void WriteDocPr(XmlWriter x, int id, string name, string? alt)
    {
        x.WriteStartElement("wp", "docPr", WP);
        x.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("name", name);
        if (!string.IsNullOrEmpty(alt)) x.WriteAttributeString("descr", alt);
        x.WriteEndElement();
    }

    private static void WriteGraphicFrameLocks(XmlWriter x)
    {
        x.WriteStartElement("wp", "cNvGraphicFramePr", WP);
        x.WriteStartElement("a", "graphicFrameLocks", A);
        x.WriteAttributeString("xmlns", "a", null, A);
        x.WriteAttributeString("noChangeAspect", "1");
        x.WriteEndElement();
        x.WriteEndElement();
    }

    private static void WriteGraphic(XmlWriter x, long cx, long cy, string name, string relId)
    {
        x.WriteStartElement("a", "graphic", A);
        x.WriteAttributeString("xmlns", "a", null, A);
        x.WriteStartElement("a", "graphicData", A);
        x.WriteAttributeString("uri", PIC);

        x.WriteStartElement("pic", "pic", PIC);
        x.WriteAttributeString("xmlns", "pic", null, PIC);

        // nvPicPr
        x.WriteStartElement("pic", "nvPicPr", PIC);
        x.WriteStartElement("pic", "cNvPr", PIC);
        x.WriteAttributeString("id", "0");
        x.WriteAttributeString("name", name);
        x.WriteEndElement();
        x.WriteStartElement("pic", "cNvPicPr", PIC);
        x.WriteEndElement();
        x.WriteEndElement(); // nvPicPr

        // blipFill
        x.WriteStartElement("pic", "blipFill", PIC);
        x.WriteStartElement("a", "blip", A);
        x.WriteAttributeString("r", "embed", R, relId);
        x.WriteEndElement();
        x.WriteStartElement("a", "stretch", A);
        x.WriteStartElement("a", "fillRect", A);
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndElement(); // blipFill

        // spPr
        x.WriteStartElement("pic", "spPr", PIC);
        x.WriteStartElement("a", "xfrm", A);
        x.WriteStartElement("a", "off", A);
        x.WriteAttributeString("x", "0");
        x.WriteAttributeString("y", "0");
        x.WriteEndElement();
        x.WriteStartElement("a", "ext", A);
        x.WriteAttributeString("cx", cx.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("cy", cy.ToString(CultureInfo.InvariantCulture));
        x.WriteEndElement();
        x.WriteEndElement(); // xfrm
        x.WriteStartElement("a", "prstGeom", A);
        x.WriteAttributeString("prst", "rect");
        x.WriteStartElement("a", "avLst", A);
        x.WriteEndElement();
        x.WriteEndElement(); // prstGeom
        x.WriteEndElement(); // spPr

        x.WriteEndElement(); // pic
        x.WriteEndElement(); // graphicData
        x.WriteEndElement(); // graphic
    }

    private static string HorizontalAnchorValue(HorizontalAnchor a) => a switch
    {
        HorizontalAnchor.Page => "page",
        HorizontalAnchor.Margin => "margin",
        HorizontalAnchor.Column => "column",
        HorizontalAnchor.Character => "character",
        HorizontalAnchor.LeftMargin => "leftMargin",
        HorizontalAnchor.RightMargin => "rightMargin",
        HorizontalAnchor.InsideMargin => "insideMargin",
        HorizontalAnchor.OutsideMargin => "outsideMargin",
        _ => "column",
    };

    private static string VerticalAnchorValue(VerticalAnchor a) => a switch
    {
        VerticalAnchor.Page => "page",
        VerticalAnchor.Margin => "margin",
        VerticalAnchor.Line => "line",
        VerticalAnchor.Paragraph => "paragraph",
        VerticalAnchor.TopMargin => "topMargin",
        VerticalAnchor.BottomMargin => "bottomMargin",
        VerticalAnchor.InsideMargin => "insideMargin",
        VerticalAnchor.OutsideMargin => "outsideMargin",
        _ => "paragraph",
    };

    /// <summary>rPr children in schema order: rFonts, b, i, strike, color, sz, u, highlight.</summary>
    private static void WriteRunProperties(XmlWriter x, TextFormat f)
    {
        if (f.IsDefault) return;
        x.WriteStartElement("rPr", W);
        if (f.Font is not null)
        {
            x.WriteStartElement("rFonts", W);
            x.WriteAttributeString("ascii", W, f.Font);
            x.WriteAttributeString("hAnsi", W, f.Font);
            x.WriteEndElement();
        }
        if (f.Bold) { x.WriteStartElement("b", W); x.WriteEndElement(); }
        if (f.Italic) { x.WriteStartElement("i", W); x.WriteEndElement(); }
        if (f.Strikethrough) { x.WriteStartElement("strike", W); x.WriteEndElement(); }
        if (f.ColorHex is not null)
        {
            x.WriteStartElement("color", W);
            x.WriteAttributeString("val", W, f.ColorHex.TrimStart('#'));
            x.WriteEndElement();
        }
        if (f.SizePt > 0)
        {
            // Half-points.
            x.WriteStartElement("sz", W);
            x.WriteAttributeString("val", W, ((int)Math.Round(f.SizePt * 2)).ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        if (f.Underline)
        {
            x.WriteStartElement("u", W);
            x.WriteAttributeString("val", W, "single");
            x.WriteEndElement();
        }
        if (f.Highlight is not null)
        {
            x.WriteStartElement("highlight", W);
            x.WriteAttributeString("val", W, f.Highlight);
            x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    private static void WriteText(XmlWriter x, string text)
    {
        x.WriteStartElement("t", W);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            x.WriteAttributeString("xml", "space", null, "preserve");
        x.WriteString(text);
        x.WriteEndElement();
    }

    private static void WriteTable(XmlWriter x, DocTable t, PartContext ctx)
    {
        x.WriteStartElement("tbl", W);

        // tblPr
        x.WriteStartElement("tblPr", W);
        // Table width + layout:
        //   • Explicit ColumnWidthsPt → fixed layout at the summed width (Word AND LibreOffice
        //     honour the column widths and don't stretch to page width).
        //   • No widths → autofit: tblW w=0 type=auto + tblLayout type=autofit. Word fits to
        //     content; LibreOffice interprets autofit rather than defaulting to full page width.
        if (t.ColumnWidthsPt is not null && t.ColumnWidthsPt.Length > 0)
        {
            double totalTwips = 0;
            foreach (var w in t.ColumnWidthsPt) totalTwips += w * 20;
            x.WriteStartElement("tblW", W);
            x.WriteAttributeString("w", W, ((int)Math.Round(totalTwips)).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("type", W, "dxa");
            x.WriteEndElement();
            x.WriteStartElement("tblLayout", W);
            x.WriteAttributeString("type", W, "fixed");
            x.WriteEndElement();
        }
        else
        {
            x.WriteStartElement("tblW", W);
            x.WriteAttributeString("w", W, "0");
            x.WriteAttributeString("type", W, "auto");
            x.WriteEndElement();
            x.WriteStartElement("tblLayout", W);
            x.WriteAttributeString("type", W, "autofit");
            x.WriteEndElement();
        }
        if (t.Borders)
        {
            x.WriteStartElement("tblBorders", W);
            foreach (var edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
            {
                x.WriteStartElement(edge, W);
                x.WriteAttributeString("val", W, "single");
                x.WriteAttributeString("sz", W, "4");
                x.WriteAttributeString("space", W, "0");
                x.WriteAttributeString("color", W, "auto");
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }
        x.WriteEndElement(); // tblPr

        // tblGrid — one gridCol per column.
        x.WriteStartElement("tblGrid", W);
        for (int c = 0; c < t.Columns; c++)
        {
            x.WriteStartElement("gridCol", W);
            if (t.ColumnWidthsPt is not null && c < t.ColumnWidthsPt.Length && t.ColumnWidthsPt[c] > 0)
                x.WriteAttributeString("w", W, PtToTwips(t.ColumnWidthsPt[c]));
            x.WriteEndElement();
        }
        x.WriteEndElement();

        foreach (var row in t.Rows)
        {
            x.WriteStartElement("tr", W);
            if (row.IsHeader)
            {
                x.WriteStartElement("trPr", W);
                x.WriteStartElement("tblHeader", W);
                x.WriteEndElement();
                x.WriteEndElement();
            }

            int col = 0;
            while (col < t.Columns)
            {
                var cell = row.Cells[col];
                int span = Math.Clamp(cell.ColumnSpan, 1, t.Columns - col);

                x.WriteStartElement("tc", W);
                x.WriteStartElement("tcPr", W);
                x.WriteStartElement("tcW", W);
                if (t.ColumnWidthsPt is not null && col < t.ColumnWidthsPt.Length && t.ColumnWidthsPt[col] > 0)
                {
                    x.WriteAttributeString("w", W, PtToTwips(t.ColumnWidthsPt[col]));
                    x.WriteAttributeString("type", W, "dxa");
                }
                else
                {
                    x.WriteAttributeString("w", W, "0");
                    x.WriteAttributeString("type", W, "auto");
                }
                x.WriteEndElement();
                if (span > 1)
                {
                    x.WriteStartElement("gridSpan", W);
                    x.WriteAttributeString("val", W, span.ToString(CultureInfo.InvariantCulture));
                    x.WriteEndElement();
                }
                if (cell.ShadeHex is not null)
                {
                    x.WriteStartElement("shd", W);
                    x.WriteAttributeString("val", W, "clear");
                    x.WriteAttributeString("color", W, "auto");
                    x.WriteAttributeString("fill", W, cell.ShadeHex.TrimStart('#'));
                    x.WriteEndElement();
                }
                x.WriteEndElement(); // tcPr

                // Every tc must contain at least one paragraph.
                if (cell.Paragraphs.Count == 0) WriteEmptyParagraph(x);
                else foreach (var p in cell.Paragraphs) WriteParagraph(x, p, ctx);

                x.WriteEndElement(); // tc
                col += span;
            }

            x.WriteEndElement(); // tr
        }

        x.WriteEndElement(); // tbl
    }

    // ---- Section properties (page setup + header/footer references) -------------------

    private static void WriteSectionProperties(XmlWriter x, Document doc)
    {
        x.WriteStartElement("sectPr", W);

        int hfIndex = 1;
        void Reference(HeaderFooterContent c, bool isHeader, string type)
        {
            if (!c.HasContent) return;
            x.WriteStartElement(isHeader ? "headerReference" : "footerReference", W);
            x.WriteAttributeString("type", W, type);
            x.WriteAttributeString("id", R, $"rIdHf{hfIndex}");
            x.WriteEndElement();
            hfIndex++;
        }
        Reference(doc.Header, isHeader: true, "default");
        Reference(doc.Footer, isHeader: false, "default");
        Reference(doc.FirstPageHeader, isHeader: true, "first");
        Reference(doc.FirstPageFooter, isHeader: false, "first");

        var (pw, ph) = doc.Page.PortraitTwips;
        bool landscape = doc.Page.Orientation == PageOrientation.Landscape;
        x.WriteStartElement("pgSz", W);
        x.WriteAttributeString("w", W, (landscape ? ph : pw).ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("h", W, (landscape ? pw : ph).ToString(CultureInfo.InvariantCulture));
        if (landscape) x.WriteAttributeString("orient", W, "landscape");
        x.WriteEndElement();

        x.WriteStartElement("pgMar", W);
        x.WriteAttributeString("top", W, PtToTwips(doc.Page.TopMarginPt));
        x.WriteAttributeString("right", W, PtToTwips(doc.Page.RightMarginPt));
        x.WriteAttributeString("bottom", W, PtToTwips(doc.Page.BottomMarginPt));
        x.WriteAttributeString("left", W, PtToTwips(doc.Page.LeftMarginPt));
        x.WriteAttributeString("header", W, "708");
        x.WriteAttributeString("footer", W, "708");
        x.WriteAttributeString("gutter", W, "0");
        x.WriteEndElement();

        if (doc.DifferentFirstPage)
        {
            x.WriteStartElement("titlePg", W);
            x.WriteEndElement();
        }

        x.WriteEndElement(); // sectPr
    }

    // ---- styles.xml --------------------------------------------------------------------

    private static void WriteStyles(XmlWriter x)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "styles", W);

        // Document defaults: Calibri 11pt.
        x.WriteStartElement("docDefaults", W);
        x.WriteStartElement("rPrDefault", W);
        x.WriteStartElement("rPr", W);
        x.WriteStartElement("rFonts", W);
        x.WriteAttributeString("ascii", W, "Calibri");
        x.WriteAttributeString("hAnsi", W, "Calibri");
        x.WriteAttributeString("cs", W, "Calibri");
        x.WriteEndElement();
        x.WriteStartElement("sz", W);
        x.WriteAttributeString("val", W, "22");
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteStartElement("pPrDefault", W);
        x.WriteEndElement();
        x.WriteEndElement();

        void Style(string id, string name, bool isDefault, Action? pPr, Action? rPr, bool character = false)
        {
            x.WriteStartElement("style", W);
            x.WriteAttributeString("type", W, character ? "character" : "paragraph");
            if (isDefault) x.WriteAttributeString("default", W, "1");
            x.WriteAttributeString("styleId", W, id);
            x.WriteStartElement("name", W);
            x.WriteAttributeString("val", W, name);
            x.WriteEndElement();
            x.WriteStartElement("qFormat", W);
            x.WriteEndElement();
            if (pPr is not null) { x.WriteStartElement("pPr", W); pPr(); x.WriteEndElement(); }
            if (rPr is not null) { x.WriteStartElement("rPr", W); rPr(); x.WriteEndElement(); }
            x.WriteEndElement();
        }
        void Sz(int halfPoints)
        {
            x.WriteStartElement("sz", W);
            x.WriteAttributeString("val", W, halfPoints.ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        void Bold() { x.WriteStartElement("b", W); x.WriteEndElement(); }
        void Color(string hex)
        {
            x.WriteStartElement("color", W);
            x.WriteAttributeString("val", W, hex);
            x.WriteEndElement();
        }
        void SpacingBefore(int twips)
        {
            x.WriteStartElement("spacing", W);
            x.WriteAttributeString("before", W, twips.ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("after", W, "120");
            x.WriteEndElement();
        }
        void OutlineLvl(int n)
        {
            x.WriteStartElement("outlineLvl", W);
            x.WriteAttributeString("val", W, n.ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }

        Style("Normal", "Normal", isDefault: true, pPr: null, rPr: null);
        Style("Title", "Title", false,
            pPr: () => SpacingBefore(0),
            rPr: () => { Sz(56); Color("1F3864"); });
        Style("Heading1", "heading 1", false,
            pPr: () => { SpacingBefore(240); OutlineLvl(0); },
            rPr: () => { Bold(); Sz(32); Color("2E74B5"); });
        Style("Heading2", "heading 2", false,
            pPr: () => { SpacingBefore(200); OutlineLvl(1); },
            rPr: () => { Bold(); Sz(26); Color("2E74B5"); });
        Style("Heading3", "heading 3", false,
            pPr: () => { SpacingBefore(160); OutlineLvl(2); },
            rPr: () => { Bold(); Sz(24); Color("1F4D78"); });
        Style("Quote", "Quote", false,
            pPr: () =>
            {
                x.WriteStartElement("ind", W);
                x.WriteAttributeString("left", W, "720");
                x.WriteAttributeString("right", W, "720");
                x.WriteEndElement();
            },
            rPr: () =>
            {
                x.WriteStartElement("i", W);
                x.WriteEndElement();
                Color("404040");
            });
        Style("ListParagraph", "List Paragraph", false,
            pPr: () =>
            {
                x.WriteStartElement("ind", W);
                x.WriteAttributeString("left", W, "720");
                x.WriteEndElement();
                x.WriteStartElement("contextualSpacing", W);
                x.WriteEndElement();
            },
            rPr: null);
        Style("Hyperlink", "Hyperlink", false,
            pPr: null,
            rPr: () =>
            {
                Color("0563C1");
                x.WriteStartElement("u", W);
                x.WriteAttributeString("val", W, "single");
                x.WriteEndElement();
            },
            character: true);

        x.WriteEndElement(); // styles
        x.WriteEndDocument();
    }

    // ---- numbering.xml -------------------------------------------------------------------

    private static void WriteNumbering(XmlWriter x)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "numbering", W);

        // abstractNum 0 — bullets; abstractNum 1 — decimal numbering. 9 levels each.
        //
        // Bullet characters: plain Unicode, NO font override (no Symbol/Wingdings/Courier).
        // Word's own built-in bullet list does the same — these code points are in every
        // modern system font, so they render correctly in Word AND LibreOffice.
        //   U+2022 BULLET            • level 0, 3, 6
        //   U+2013 EN DASH           – level 1, 4, 7
        //   U+25AA BLACK SMALL SQ    ▪ level 2, 5, 8
        string[] bulletChars = { "\u2022", "\u2013", "\u25AA" };
        for (int abs = 0; abs < 2; abs++)
        {
            x.WriteStartElement("abstractNum", W);
            x.WriteAttributeString("abstractNumId", W, abs.ToString(CultureInfo.InvariantCulture));
            for (int lvl = 0; lvl < 9; lvl++)
            {
                x.WriteStartElement("lvl", W);
                x.WriteAttributeString("ilvl", W, lvl.ToString(CultureInfo.InvariantCulture));
                x.WriteStartElement("start", W);
                x.WriteAttributeString("val", W, "1");
                x.WriteEndElement();
                x.WriteStartElement("numFmt", W);
                x.WriteAttributeString("val", W, abs == 0 ? "bullet" : "decimal");
                x.WriteEndElement();
                x.WriteStartElement("lvlText", W);
                x.WriteAttributeString("val", W, abs == 0
                    ? bulletChars[lvl % bulletChars.Length]
                    : $"%{lvl + 1}.");
                x.WriteEndElement();
                x.WriteStartElement("lvlJc", W);
                x.WriteAttributeString("val", W, "left");
                x.WriteEndElement();
                x.WriteStartElement("pPr", W);
                x.WriteStartElement("ind", W);
                x.WriteAttributeString("left", W, ((lvl + 1) * 720).ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("hanging", W, "360");
                x.WriteEndElement();
                x.WriteEndElement();
                // No rPr/rFonts on bullet levels — inherit paragraph font.
                // This matches Word's own built-in bullet list behaviour.
                x.WriteEndElement(); // lvl
            }
            x.WriteEndElement(); // abstractNum
        }

        // num 1 → bullets, num 2 → decimal.
        for (int num = 1; num <= 2; num++)
        {
            x.WriteStartElement("num", W);
            x.WriteAttributeString("numId", W, num.ToString(CultureInfo.InvariantCulture));
            x.WriteStartElement("abstractNumId", W);
            x.WriteAttributeString("val", W, (num - 1).ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
            x.WriteEndElement();
        }

        x.WriteEndElement();
        x.WriteEndDocument();
    }

    // ---- settings.xml (+ documentProtection) ----------------------------------------------

    private static void WriteSettings(XmlWriter x, Document doc)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "settings", W);

        if (doc.IsProtected)
        {
            // Same iterated-SHA-512 hash family as Excel's sheet protection ([MS-OFFCRYPTO] §2.4.2.4).
            var salt = Chuvadi.Internal.Crypto.PasswordHasher.GenerateSalt();
            var hash = Chuvadi.Internal.Crypto.PasswordHasher.ComputeHashBase64(
                doc.ProtectionPassword!, salt, Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount);

            x.WriteStartElement("documentProtection", W);
            x.WriteAttributeString("edit", W, doc.ProtectionMode switch
            {
                DocumentProtectionMode.ReadOnly => "readOnly",
                DocumentProtectionMode.Comments => "comments",
                DocumentProtectionMode.TrackedChanges => "trackedChanges",
                DocumentProtectionMode.Forms => "forms",
                _ => "readOnly",
            });
            x.WriteAttributeString("enforcement", W, "1");
            x.WriteAttributeString("cryptProviderType", W, "rsaAES");
            x.WriteAttributeString("cryptAlgorithmClass", W, "hash");
            x.WriteAttributeString("cryptAlgorithmType", W, "typeAny");
            x.WriteAttributeString("cryptAlgorithmSid", W, "14"); // SHA-512
            x.WriteAttributeString("cryptSpinCount", W,
                Chuvadi.Internal.Crypto.PasswordHasher.DefaultSpinCount.ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("hash", W, hash);
            x.WriteAttributeString("salt", W, Convert.ToBase64String(salt));
            x.WriteEndElement();
        }

        x.WriteEndElement();
        x.WriteEndDocument();
    }

    // ---- docProps ----------------------------------------------------------------------------

    private static void WriteCoreProps(OoxmlPackage pkg)
    {
        const string cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        const string dc = "http://purl.org/dc/elements/1.1/";
        const string dcterms = "http://purl.org/dc/terms/";
        const string xsi = "http://www.w3.org/2001/XMLSchema-instance";

        using var s = pkg.CreatePart("/docProps/core.xml", CtCore);
        using var x = CreateXml(s);
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("cp", "coreProperties", cp);
        x.WriteAttributeString("xmlns", "dc", null, dc);
        x.WriteAttributeString("xmlns", "dcterms", null, dcterms);
        x.WriteAttributeString("xmlns", "xsi", null, xsi);
        x.WriteStartElement("creator", dc);
        x.WriteString("Chuvadi.Docs");
        x.WriteEndElement();
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        x.WriteStartElement("dcterms", "created", dcterms);
        x.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
        x.WriteString(now);
        x.WriteEndElement();
        x.WriteStartElement("dcterms", "modified", dcterms);
        x.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
        x.WriteString(now);
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    private static void WriteAppProps(OoxmlPackage pkg)
    {
        const string ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        using var s = pkg.CreatePart("/docProps/app.xml", CtApp);
        using var x = CreateXml(s);
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("Properties", ep);
        x.WriteStartElement("Application", ep);
        x.WriteString("Chuvadi.Docs");
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    // ---- Helpers --------------------------------------------------------------------------

    internal static string StyleId(ParagraphStyle s) => s switch
    {
        ParagraphStyle.Title => "Title",
        ParagraphStyle.Heading1 => "Heading1",
        ParagraphStyle.Heading2 => "Heading2",
        ParagraphStyle.Heading3 => "Heading3",
        ParagraphStyle.Quote => "Quote",
        ParagraphStyle.ListParagraph => "ListParagraph",
        _ => "Normal",
    };

    private static bool DocumentHasLists(Document doc)
    {
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p && p.List != ListKind.None) return true;
            if (b is DocTable t)
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cp in cell.Paragraphs)
                            if (cp.List != ListKind.None) return true;
        }
        return false;
    }

    private static string PtToTwips(double pt)
        => ((int)Math.Round(pt * 20)).ToString(CultureInfo.InvariantCulture);

    private static XmlWriter CreateXml(Stream s)
        => XmlWriter.Create(s, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
            Indent = false,
        });
}
