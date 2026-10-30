using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private const string RelTheme = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string CtTheme = "application/vnd.openxmlformats-officedocument.theme+xml";
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
        /// <summary>The numbering written with this save (ids for list paragraphs).</summary>
        public NumberingXml.Plan? Numbering { get; init; }
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

        public List<(string RelId, string Url)> ImageLinks { get; } = new();

        /// <summary>An external relationship for a linked picture.</summary>
        public string AddImageLink(string url)
        {
            var id = $"rIdImgLink{ImageLinks.Count + 1}";
            ImageLinks.Add((id, url));
            return id;
        }
    }

    public static void Write(Stream output, Document doc)
    {
        using var pkg = OoxmlPackage.Create(output);
        var numbering = new NumberingXml.Plan(doc);
        bool hasLists = numbering.HasAny;

        // Package-wide media registry: each image becomes a unique /word/media/imageN.ext
        // part, written after all the XML parts. A global drawing-id counter keeps every
        // <wp:docPr id="..."> unique across the whole document, as Word expects.
        var mediaParts = new List<(string Uri, ImageSpec Image)>();
        int mediaCounter = 0;
        int drawingIdCounter = 0;
        var mediaByBytes = new Dictionary<byte[], string>(ReferenceEqualityComparer.Instance);
        string RegisterMedia(ImageSpec img)
        {
            // The same picture bytes (e.g. a loaded image used in several places) become one media part.
            if (mediaByBytes.TryGetValue(img.Bytes, out var existing)) return existing;
            mediaCounter++;
            var ext = ImageMetadata.ExtensionFor(img.ContentType);
            var uri = $"/word/media/image{mediaCounter}.{ext}";
            mediaParts.Add((uri, img));
            mediaByBytes[img.Bytes] = uri;
            return uri;
        }
        int NextDrawingId() => ++drawingIdCounter;

        var bodyCtx = new PartContext("/word/document.xml", RegisterMedia, NextDrawingId) { Numbering = numbering };

        // ---- header/footer plan: one part per non-empty (or explicitly blank, or watermarked) slot of each section ----
        bool oddEven = doc.EffectiveOddAndEven;
        int hfIndex = 1;
        var hfParts = new List<(string Uri, string RelId, string RelType, HeaderFooterContent Content, bool IsHeader, TextWatermark? Watermark, Section Section)>();
        var sectionRefs = new Dictionary<Section, List<SectionXml.Reference>>(ReferenceEqualityComparer.Instance);
        var sections = doc.Sections;
        var written = new Dictionary<(HeaderFooterContent, TextWatermark?, HeaderFooterContent?), string>();
        for (int si = 0; si < sections.Count; si++)
        {
            var section = sections[si];
            if (sectionRefs.ContainsKey(section)) continue;
            var refs = new List<SectionXml.Reference>();
            foreach (var (content, isHeader, type) in section.Slots())
            {
                bool watermarkHere = isHeader && section.Watermark is not null
                    && (section.WatermarkSlots is { } only ? only.Contains(type)
                        : type == "default" || (type == "first" && section.DifferentFirstPage) || (type == "even" && oddEven));
                if (!content.HasContent && !watermarkHere && content.LinkToPrevious) continue;
                // A watermark in a slot that would inherit: carry the inherited content along (as Word does).
                HeaderFooterContent? inherited = null;
                if (watermarkHere && !content.HasContent && content.LinkToPrevious)
                    for (int pj = si - 1; pj >= 0 && inherited is null; pj--)
                    {
                        var prev = sections[pj].Slot(isHeader, type);
                        if (prev.HasContent || !prev.LinkToPrevious) inherited = prev;
                    }
                var key = (content, watermarkHere ? section.Watermark : null, inherited);
                if (!written.TryGetValue(key, out var relId))
                {
                    var uri = $"/word/{(isHeader ? "header" : "footer")}{hfIndex}.xml";
                    relId = $"rIdHf{hfIndex}";
                    hfIndex++;
                    hfParts.Add((uri, relId, isHeader ? RelHeader : RelFooter, inherited ?? content, isHeader, watermarkHere ? section.Watermark : null, section));
                    written[key] = relId;
                }
                refs.Add(new SectionXml.Reference(isHeader, type, relId));
            }
            sectionRefs[section] = refs;
        }

        // ---- /word/document.xml ----
        using (var s = pkg.CreatePart("/word/document.xml", CtDocument))
        using (var x = CreateXml(s))
        {
            x.WriteStartDocument(standalone: true);
            x.WriteStartElement("w", "document", W);
            x.WriteAttributeString("xmlns", "r", null, R);
            if (doc.GetImagePlacements().Any(i => i.Host == ImageHost.Body && DrawingXml.NeedsWp14(i.Image))) DeclareWp14(x);
            x.WriteStartElement("body", W);

            object? previous = null;
            foreach (var block in doc.Blocks)
            {
                // Two adjacent tables merge into one in Word; separate them with an
                // empty paragraph so authoring intent is preserved.
                if (block is DocTable && previous is DocTable)
                    WriteEmptyParagraph(x);

                if (block is Paragraph p)
                {
                    Action<XmlWriter>? sectPr = p.SectionProperties is { } ps && !ReferenceEquals(ps, doc.Section)
                        ? w => SectionXml.Write(w, ps, sectionRefs[ps]) : null;
                    WriteParagraph(x, p, bodyCtx, sectPr: sectPr);
                }
                else if (block is DocTable t) WriteTable(x, t, bodyCtx);
                previous = block;
            }

            // A table as the last body child (directly before sectPr) trips some Word
            // versions; always close with a paragraph in that case.
            if (previous is DocTable) WriteEmptyParagraph(x);

            SectionXml.Write(x, doc.Section, sectionRefs[doc.Section]);

            x.WriteEndElement(); // body
            x.WriteEndElement(); // document
            x.WriteEndDocument();
        }

        // ---- /word/styles.xml ----
        using (var s = pkg.CreatePart("/word/styles.xml", CtStyles))
        using (var x = CreateXml(s))
        {
            StylesXml.Write(x, doc, ReferencedStyleIds(doc), numbering);
        }

        // ---- /word/theme/theme1.xml (carried through from a loaded document) ----
        if (doc.ThemeXml is { Length: > 0 } theme)
        {
            using var s = pkg.CreatePart("/word/theme/theme1.xml", CtTheme);
            s.Write(theme, 0, theme.Length);
        }

        // ---- /word/numbering.xml (only when lists are used) ----
        if (hasLists)
        {
            using var s = pkg.CreatePart("/word/numbering.xml", CtNumbering);
            using var x = CreateXml(s);
            NumberingXml.Write(x, numbering);
        }
        if (hasLists)
        {
            // Images behind picture bullets, with numbering.xml's relationship ids unchanged.
            int pic = 0;
            foreach (var (relId, relType, bytes, contentType) in doc.Numbering.RawPictureParts)
            {
                var uri = $"/word/media/numbering-picture{++pic}.{ImageMetadata.ExtensionFor(contentType)}";
                using (var ps = pkg.CreatePart(uri, contentType)) ps.Write(bytes, 0, bytes.Length);
                pkg.AddRelationship("/word/numbering.xml", uri, relType, relId);
            }
        }

        // ---- /word/settings.xml (always present; carries documentProtection when set) ----
        using (var s = pkg.CreatePart("/word/settings.xml", CtSettings))
        using (var x = CreateXml(s))
        {
            WriteSettings(x, doc);
        }

        // ---- header/footer parts ----
        var hfContexts = new List<PartContext>();
        int watermarkCount = 0;
        foreach (var (uri, _, _, content, isHeader, watermark, section) in hfParts)
        {
            var hfCtx = new PartContext(uri, RegisterMedia, NextDrawingId) { Numbering = numbering };
            hfContexts.Add(hfCtx);
            using var s = pkg.CreatePart(uri, isHeader ? CtHeader : CtFooter);
            using var x = CreateXml(s);
            x.WriteStartDocument(standalone: true);
            x.WriteStartElement("w", isHeader ? "hdr" : "ftr", W);
            x.WriteAttributeString("xmlns", "r", null, R);
            if (AllParagraphsOf(content).Any(p => p.Runs.Any(r => r.Image is { } im && DrawingXml.NeedsWp14(im)))) DeclareWp14(x);
            if (watermark is not null)
            {
                x.WriteAttributeString("xmlns", "v", null, Vml);
                x.WriteAttributeString("xmlns", "o", null, Office);
                x.WriteAttributeString("xmlns", "w10", null, Word10);
            }
            Action<XmlWriter>? leading = null;
            if (watermark is not null)
            {
                int n = ++watermarkCount;
                leading = w => WriteWatermark(w, watermark, section, n);
            }
            object? previous = null;
            foreach (var block in content.Blocks)
            {
                if (block is Paragraph p)
                {
                    WriteParagraph(x, p, hfCtx, leading);
                    leading = null;
                }
                else if (block is DocTable t)
                {
                    if (previous is DocTable) WriteEmptyParagraph(x);
                    WriteTable(x, t, hfCtx);
                }
                previous = block;
            }
            if (leading is not null || previous is DocTable || previous is null)
            {
                // A header/footer must end with a paragraph; the watermark needs one to live in.
                x.WriteStartElement("p", W);
                leading?.Invoke(x);
                x.WriteEndElement();
            }
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
        if (doc.ThemeXml is { Length: > 0 })
            pkg.AddRelationship("/word/document.xml", "/word/theme/theme1.xml", RelTheme, "rIdTheme");
        if (hasLists)
            pkg.AddRelationship("/word/document.xml", "/word/numbering.xml", RelNumbering, "rIdNumbering");
        foreach (var part in hfParts)
            pkg.AddRelationship("/word/document.xml", part.Uri, part.RelType, part.RelId);

        // Per-part hyperlink + image relationships (body and each header/footer).
        void WriteContextRels(PartContext ctx)
        {
            foreach (var (relId, url) in ctx.Hyperlinks)
                pkg.AddExternalRelationship(ctx.PartUri, url, RelHyperlink, relId);
            foreach (var (relId, mediaUri) in ctx.Images)
                pkg.AddRelationship(ctx.PartUri, mediaUri, RelImage, relId);
            foreach (var (relId, url) in ctx.ImageLinks)
                pkg.AddExternalRelationship(ctx.PartUri, url, RelImage, relId);
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

    internal static void WriteParagraph(XmlWriter x, Paragraph p, PartContext? ctx, Action<XmlWriter>? leadingRuns = null,
        Action<XmlWriter>? sectPr = null)
    {
        x.WriteStartElement("p", W);

        // pPr — paragraph properties in schema order (see ParagraphProperties).
        ParagraphProperties.Write(x, p, ctx?.Numbering, sectPr);
        leadingRuns?.Invoke(x);

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
                RunProperties.Write(x, run.Format, run.CharacterStyleId);
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
                RunProperties.Write(x, run.Format, characterStyleId: run.EffectiveCharacterStyleId);
                WriteText(x, run.TextContent);
                x.WriteEndElement();
                x.WriteEndElement();
                continue;
            }

            // Skip empty non-image runs (e.g. an image run reached without a context).
            if (run.Image is not null) continue;

            x.WriteStartElement("r", W);
            RunProperties.Write(x, run.Format, run.CharacterStyleId);
            if (run.Break is BreakType bt) WriteBreak(x, bt);
            else if (run.PositionalTab is { } ptab) WritePositionalTab(x, ptab);
            else if (run.IsTab) { x.WriteStartElement("tab", W); x.WriteEndElement(); }
            else WriteText(x, run.TextContent);
            x.WriteEndElement();
        }

        x.WriteEndElement(); // p
    }

    private static void WritePositionalTab(XmlWriter x, PositionalTab tab)
    {
        // CT_PTab: alignment, relativeTo and leader are all required.
        x.WriteStartElement("ptab", W);
        x.WriteAttributeString("relativeTo", W, tab.RelativeTo == PositionalTabBase.Indent ? "indent" : "margin");
        x.WriteAttributeString("alignment", W, tab.Alignment switch
        {
            PositionalTabAlignment.Center => "center",
            PositionalTabAlignment.Right => "right",
            _ => "left",
        });
        x.WriteAttributeString("leader", W, tab.Leader switch
        {
            TabLeader.Dot => "dot",
            TabLeader.Hyphen => "hyphen",
            TabLeader.Underscore => "underscore",
            TabLeader.MiddleDot => "middleDot",
            _ => "none",
        });
        x.WriteEndElement();
    }

    // ---- Drawing (image) emission ----------------------------------------------------

    /// <summary>Writes a run containing a w:drawing: inline or floating (wp:anchor).</summary>
    private static void WriteDrawingRun(XmlWriter x, ImageSpec image, PartContext ctx)
    {
        string? embed = image.Bytes.Length > 0 || image.LinkUrl is null ? ctx.AddImage(image) : null;
        string? link = image.LinkUrl is { } url ? ctx.AddImageLink(url) : null;
        DrawingXml.WriteRun(x, image, embed, ctx.NextDrawingId(), link);
    }

    /// <summary>
    /// Writes run content as <c>w:t</c> segments, turning line feeds (<c>\n</c>, <c>\r\n</c>,
    /// <c>\r</c>) into <c>w:br</c> and tab characters into <c>w:tab</c> — the elements Word
    /// itself uses. A literal control character inside <c>w:t</c> would render as a space.
    /// </summary>
    internal static void WriteText(XmlWriter x, string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r' && c != '\t') continue;
            if (i > start) WriteTextSegment(x, text.Substring(start, i - start));
            if (c == '\t')
            {
                x.WriteStartElement("tab", W);
                x.WriteEndElement();
            }
            else
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; // CRLF = one break
                x.WriteStartElement("br", W);
                x.WriteEndElement();
            }
            start = i + 1;
        }
        if (start < text.Length || text.Length == 0)
            WriteTextSegment(x, start == 0 ? text : text.Substring(start));
    }

    private static void WriteTextSegment(XmlWriter x, string text)
    {
        x.WriteStartElement("t", W);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            x.WriteAttributeString("xml", "space", null, "preserve");
        x.WriteString(text);
        x.WriteEndElement();
    }

    private static void WriteBreak(XmlWriter x, BreakType type)
    {
        x.WriteStartElement("br", W);
        switch (type)
        {
            case BreakType.Page: x.WriteAttributeString("type", W, "page"); break;
            case BreakType.Column: x.WriteAttributeString("type", W, "column"); break;
            case BreakType.TextWrappingClearLeft:
                x.WriteAttributeString("type", W, "textWrapping");
                x.WriteAttributeString("clear", W, "left");
                break;
            case BreakType.TextWrappingClearRight:
                x.WriteAttributeString("type", W, "textWrapping");
                x.WriteAttributeString("clear", W, "right");
                break;
            case BreakType.TextWrappingClearAll:
                x.WriteAttributeString("type", W, "textWrapping");
                x.WriteAttributeString("clear", W, "all");
                break;
        }
        x.WriteEndElement();
    }

    private static void WriteTable(XmlWriter x, DocTable t, PartContext ctx)
        => TableXml.Write(x, t, (w, p) => WriteParagraph(w, p, ctx));

    /// <summary>
    /// Declares wp14 and mc on a part's root: readers resolve mc:Choice/@Requires prefixes there
    /// (as Word writes them), and mark wp14 ignorable.
    /// </summary>
    private static void DeclareWp14(XmlWriter x)
    {
        x.WriteAttributeString("xmlns", "mc", null, "http://schemas.openxmlformats.org/markup-compatibility/2006");
        x.WriteAttributeString("xmlns", "wp14", null, DrawingXml.Wp14);
        x.WriteAttributeString("mc", "Ignorable", "http://schemas.openxmlformats.org/markup-compatibility/2006", "wp14");
    }

    private static IEnumerable<Paragraph> AllParagraphsOf(HeaderFooterContent content)
    {
        foreach (var b in content.Blocks)
        {
            if (b is Paragraph p) yield return p;
            else if (b is DocTable t) foreach (var tp in TableParagraphs(t, 0)) yield return tp;
        }
    }

    // ---- Watermark ---------------------------------------------------------------------

    internal const string Vml = "urn:schemas-microsoft-com:vml";
    internal const string Office = "urn:schemas-microsoft-com:office:office";
    internal const string Word10 = "urn:schemas-microsoft-com:office:word";

    /// <summary>
    /// A text watermark as Word writes it: a VML WordArt shape (shapetype 136, the "PowerPlus"
    /// watermark object) behind the text, centred on the margins.
    /// </summary>
    private static void WriteWatermark(XmlWriter x, TextWatermark wm, Section section, int n)
    {
        if (string.IsNullOrWhiteSpace(wm.Text)) throw new ArgumentException("Watermark text required.", nameof(wm));
        if (!double.IsFinite(wm.Opacity)) throw new ArgumentOutOfRangeException(nameof(wm), "Watermark opacity must be finite.");
        var page = section.Page;
        double contentWidth = Math.Max(36, page.WidthPt - page.LeftMarginPt - page.RightMarginPt);
        double contentHeight = Math.Max(36, page.HeightPt - Math.Abs(page.TopMarginPt) - Math.Abs(page.BottomMarginPt));
        double width = wm.WidthPt ?? contentWidth;
        // WordArt stretches the text to the box: about three characters per box height reads like Word's "Auto".
        double height = wm.HeightPt ?? Math.Min(width * 3 / Math.Max(1, wm.Text.Length), contentHeight / 2);
        if (!(width > 0) || !(height > 0) || !double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(wm), "Watermark size must be positive and finite.");
        var color = (wm.ColorHex ?? "C0C0C0").TrimStart('#');
        if (color.Length != 6 || !int.TryParse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Watermark ColorHex must be RRGGBB.", nameof(wm));
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        x.WriteStartElement("r", W);
        x.WriteStartElement("rPr", W);
        x.WriteStartElement("noProof", W);
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteStartElement("pict", W);

        x.WriteStartElement("v", "shapetype", Vml);
        x.WriteAttributeString("id", "_x0000_t136");
        x.WriteAttributeString("coordsize", "21600,21600");
        x.WriteAttributeString("o", "spt", Office, "136");
        x.WriteAttributeString("adj", "10800");
        x.WriteAttributeString("path", "m@7,l@8,m@5,21600l@6,21600e");
        x.WriteStartElement("v", "formulas", Vml);
        foreach (var eqn in new[]
        {
            "sum #0 0 10800", "prod #0 2 1", "sum 21600 0 @1", "sum 0 0 @2", "sum 21600 0 @3", "if @0 @3 0", "if @0 21600 @1",
            "if @0 0 @2", "if @0 @4 21600", "mid @5 @6", "mid @8 @5", "mid @7 @8", "mid @6 @7", "sum @6 0 @5",
        })
        {
            x.WriteStartElement("v", "f", Vml);
            x.WriteAttributeString("eqn", eqn);
            x.WriteEndElement();
        }
        x.WriteEndElement(); // formulas
        x.WriteStartElement("v", "path", Vml);
        x.WriteAttributeString("textpathok", "t");
        x.WriteAttributeString("o", "connecttype", Office, "custom");
        x.WriteAttributeString("o", "connectlocs", Office, "@9,0;@10,10800;@11,21600;@12,10800");
        x.WriteAttributeString("o", "connectangles", Office, "270,180,90,0");
        x.WriteEndElement();
        x.WriteStartElement("v", "textpath", Vml);
        x.WriteAttributeString("on", "t");
        x.WriteAttributeString("fitshape", "t");
        x.WriteEndElement();
        x.WriteStartElement("v", "handles", Vml);
        x.WriteStartElement("v", "h", Vml);
        x.WriteAttributeString("position", "#0,bottomRight");
        x.WriteAttributeString("xrange", "6629,14971");
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteStartElement("o", "lock", Office);
        x.WriteAttributeString("v", "ext", Vml, "edit");
        x.WriteAttributeString("text", "t");
        x.WriteAttributeString("shapetype", "t");
        x.WriteEndElement();
        x.WriteEndElement(); // shapetype

        x.WriteStartElement("v", "shape", Vml);
        x.WriteAttributeString("id", "PowerPlusWaterMarkObject" + (357476640 + n).ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("o", "spid", Office, "_x0000_s" + (2048 + n).ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("type", "#_x0000_t136");
        x.WriteAttributeString("style",
            $"position:absolute;margin-left:0;margin-top:0;width:{F(width)}pt;height:{F(height)}pt;" +
            (wm.Diagonal ? "rotation:315;" : "") +
            "z-index:-251657216;mso-position-horizontal:center;mso-position-horizontal-relative:margin;" +
            "mso-position-vertical:center;mso-position-vertical-relative:margin");
        x.WriteAttributeString("o", "allowincell", Office, "f");
        x.WriteAttributeString("fillcolor", "#" + color.ToUpperInvariant());
        x.WriteAttributeString("stroked", "f");
        double opacity = Math.Clamp(wm.Opacity, 0, 1);
        if (opacity < 1)
        {
            x.WriteStartElement("v", "fill", Vml);
            x.WriteAttributeString("opacity", F(opacity));
            x.WriteEndElement();
        }
        x.WriteStartElement("v", "textpath", Vml);
        x.WriteAttributeString("style", $"font-family:\"{wm.Font ?? "Calibri"}\";font-size:1pt");
        x.WriteAttributeString("string", wm.Text);
        x.WriteEndElement();
        x.WriteStartElement("w10", "wrap", Word10);
        x.WriteAttributeString("anchorx", "margin");
        x.WriteAttributeString("anchory", "margin");
        x.WriteEndElement();
        x.WriteEndElement(); // shape

        x.WriteEndElement(); // pict
        x.WriteEndElement(); // r
    }

    // ---- settings.xml (+ documentProtection) ----------------------------------------------

    private static void WriteSettings(XmlWriter x, Document doc)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "settings", W);

        // CT_Settings order: … mirrorMargins, …, gutterAtTop, …, documentProtection, …, defaultTabStop, …, evenAndOddHeaders …
        if (doc.MirrorMargins) { x.WriteStartElement("mirrorMargins", W); x.WriteEndElement(); }
        if (doc.GutterAtTop) { x.WriteStartElement("gutterAtTop", W); x.WriteEndElement(); }

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

        if (doc.DefaultTabStopPt is double tab)
        {
            x.WriteStartElement("defaultTabStop", W);
            x.WriteAttributeString("val", W, RunProperties.Twips(tab));
            x.WriteEndElement();
        }

        if (doc.EffectiveOddAndEven) { x.WriteStartElement("evenAndOddHeaders", W); x.WriteEndElement(); }

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

    /// <summary>Every paragraph in the document: body, table cells and headers/footers.</summary>
    /// <summary>Every paragraph: body (tables and nested tables included, in order), then headers and footers.</summary>
    internal static IEnumerable<Paragraph> AllParagraphs(Document doc)
    {
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p) yield return p;
            else if (block is DocTable t)
                foreach (var tp in TableParagraphs(t, 0)) yield return tp;
        }
        foreach (var hf in HeaderFooterStories(doc))
            foreach (var b in hf.Blocks)
            {
                if (b is Paragraph hp) yield return hp;
                else if (b is DocTable ht)
                    foreach (var tp in TableParagraphs(ht, 0)) yield return tp;
            }
    }

    /// <summary>Every distinct header/footer of every section.</summary>
    internal static IEnumerable<HeaderFooterContent> HeaderFooterStories(Document doc)
    {
        var seen = new HashSet<HeaderFooterContent>(ReferenceEqualityComparer.Instance);
        foreach (var section in doc.Sections)
            foreach (var (content, _, _) in section.Slots())
                if (seen.Add(content)) yield return content;
    }

    /// <summary>Paragraphs of a table in content order, nested tables included (depth-guarded).</summary>
    internal static IEnumerable<Paragraph> TableParagraphs(DocTable t, int depth)
    {
        if (depth > 512) yield break; // far beyond what MaxXmlDepth admits on load
        foreach (var row in t.Rows)
            foreach (var cell in row.Cells)
                foreach (var b in cell.Blocks)
                {
                    if (b is Paragraph cp) yield return cp;
                    else if (b is DocTable nested)
                        foreach (var np in TableParagraphs(nested, depth + 1)) yield return np;
                }
    }

    /// <summary>Paragraph and character style ids the content refers to (incl. the implicit Hyperlink style).</summary>
    internal static HashSet<string> ReferencedStyleIds(Document doc)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "Normal" };
        foreach (var p in AllParagraphs(doc))
        {
            ids.Add(p.StyleId);
            foreach (var r in p.Runs)
            {
                if (r.EffectiveCharacterStyleId is { } sid) ids.Add(sid);
            }
        }
        return ids;
    }

    /// <summary>Maps a built-in style id back to its <see cref="ParagraphStyle"/>.</summary>
    internal static bool TryBuiltInStyle(string id, out ParagraphStyle style)
    {
        foreach (ParagraphStyle s in Enum.GetValues<ParagraphStyle>())
        {
            if (StyleId(s) == id) { style = s; return true; }
        }
        style = ParagraphStyle.Normal;
        return false;
    }

    internal static string StyleId(ParagraphStyle s) => s switch
    {
        ParagraphStyle.Title => "Title",
        ParagraphStyle.Heading1 => "Heading1",
        ParagraphStyle.Heading2 => "Heading2",
        ParagraphStyle.Heading3 => "Heading3",
        ParagraphStyle.Quote => "Quote",
        ParagraphStyle.ListParagraph => "ListParagraph",
        ParagraphStyle.Heading4 => "Heading4",
        ParagraphStyle.Heading5 => "Heading5",
        ParagraphStyle.Heading6 => "Heading6",
        ParagraphStyle.Heading7 => "Heading7",
        ParagraphStyle.Heading8 => "Heading8",
        ParagraphStyle.Heading9 => "Heading9",
        _ => "Normal",
    };

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
