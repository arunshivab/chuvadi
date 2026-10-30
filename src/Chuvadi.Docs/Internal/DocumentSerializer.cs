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
    internal const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    private const string CtDocument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string CtStyles = "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
    private const string CtNumbering = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    private const string CtSettings = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    private const string CtHeader = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string CtFooter = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
    private const string CtCore = "application/vnd.openxmlformats-package.core-properties+xml";
    private const string CtApp = "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    private const string CtCustom = "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    private const string CtFootnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
    private const string CtEndnotes = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";
    private const string RelFootnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
    private const string RelEndnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
    private const string RelCustom = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string CtComments = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
    private const string CtCommentsExtended = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml";
    private const string RelComments = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string RelCommentsExtended = "http://schemas.microsoft.com/office/2011/relationships/commentsExtended";
    private const string CtFontTable = "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";
    private const string RelFontTable = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    private const string RelFont = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/font";

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

        /// <summary>Package-wide ids (bookmarks, notes).</summary>
        public SaveState State { get; init; } = new();

        /// <summary>The note part being written (note marks become footnoteRef / endnoteRef), or null.</summary>
        public NoteKind? NoteKind { get; init; }

        /// <summary>Only the body may hold note references (Word rejects them in notes, headers and footers).</summary>
        public bool AllowNoteReferences { get; init; }

        /// <summary>Comment anchors may be written (body and notes; not comments, headers or footers).</summary>
        public bool AllowCommentReferences { get; init; }

        /// <summary>w14:paraId to write on paragraphs (the comments part names comments by their last paragraph).</summary>
        public Dictionary<Paragraph, string>? ParaIds { get; init; }
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

        /// <summary>Text boxes being written around the current paragraph (Word does not nest them; guards cycles).</summary>
        public int TextBoxDepth { get; set; }

        public List<(string RelId, string Url)> ImageLinks { get; } = new();

        /// <summary>An external relationship for a linked picture.</summary>
        public string AddImageLink(string url)
        {
            var id = $"rIdImgLink{ImageLinks.Count + 1}";
            ImageLinks.Add((id, url));
            return id;
        }
    }

    /// <summary>Ids shared by every part of one save: bookmarks (w:id) and notes.</summary>
    internal sealed class SaveState
    {
        private int _nextBookmarkId;
        private readonly Dictionary<Bookmark, Stack<int>> _open = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, Bookmark> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Bookmark, string> _written = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<Note, int> NoteIds = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<Comment, int> CommentIds = new(ReferenceEqualityComparer.Instance);
        /// <summary>Replies with no markers of their own: written next to their comment's markers.</summary>
        public readonly Dictionary<Comment, List<Comment>> RepliesWithoutMarkers = new(ReferenceEqualityComparer.Instance);
        private int _revisionId;
        public string NextRevisionId() => (_revisionId++).ToString(CultureInfo.InvariantCulture);

        /// <summary>A new id for each start (a bookmark object used twice gets two ids), with a name unique in the file.</summary>
        public (int Id, string Name) StartBookmark(Bookmark b)
        {
            int id = _nextBookmarkId++;
            if (!_open.TryGetValue(b, out var stack)) _open[b] = stack = new Stack<int>();
            stack.Push(id);
            if (!_written.TryGetValue(b, out var name))
            {
                name = b.Name;
                if (_names.TryGetValue(name, out var other) && !ReferenceEquals(other, b))
                {
                    // Two bookmarks with one name: Word keeps one; the later is written under a free name.
                    int n = 2;
                    string candidate;
                    do
                    {
                        var suffix = "_" + n++;
                        candidate = (name.Length + suffix.Length > 40 ? name[..(40 - suffix.Length)] : name) + suffix;
                    }
                    while (_names.ContainsKey(candidate));
                    name = candidate;
                }
                _names[name] = b;
                _written[b] = name;
            }
            return (id, name);
        }

        /// <summary>The id of the latest open start of <paramref name="b"/>, or null for an end with no start before it.</summary>
        public int? EndBookmark(Bookmark b)
            => _open.TryGetValue(b, out var stack) && stack.Count > 0 ? stack.Pop() : null;
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

        var state = new SaveState();
        int noteId = 0;
        foreach (var n in doc.Footnotes) state.NoteIds[n] = ++noteId;
        noteId = 0;
        foreach (var n in doc.Endnotes) state.NoteIds[n] = ++noteId;
        int commentId = 0;
        foreach (var c in doc.Comments) state.CommentIds[c] = commentId++;
        if (doc.Comments.Any(c => c.Parent is not null))
        {
            var marked = new HashSet<Comment>(ReferenceEqualityComparer.Instance);
            foreach (var para in AllParagraphs(doc))
                foreach (var r in para.Runs)
                {
                    if (r.CommentRange is { } cr) marked.Add(cr.Comment);
                    if (r.CommentReference is { } cref) marked.Add(cref);
                }
            foreach (var c in doc.Comments)
                if (c.Parent is { } parent && !marked.Contains(c) && state.CommentIds.ContainsKey(parent))
                {
                    if (!state.RepliesWithoutMarkers.TryGetValue(parent, out var l)) state.RepliesWithoutMarkers[parent] = l = new List<Comment>();
                    l.Add(c);
                }
        }
        var bodyCtx = new PartContext("/word/document.xml", RegisterMedia, NextDrawingId) { Numbering = numbering, State = state, AllowNoteReferences = true, AllowCommentReferences = true };

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
            var hfCtx = new PartContext(uri, RegisterMedia, NextDrawingId) { Numbering = numbering, State = state };
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

        // ---- font table with embedded fonts (only when the document has one) ----
        if (doc.Fonts.Count > 0)
        {
            var fontParts = new List<(string Uri, string RelId, byte[] Bytes)>();
            using (var s = pkg.CreatePart("/word/fontTable.xml", CtFontTable))
            using (var x = CreateXml(s))
                FontTableXml.Write(x, doc.Fonts, bytes =>
                {
                    int n = fontParts.Count + 1;
                    fontParts.Add(($"/word/fonts/font{n}.odttf", $"rIdFont{n}", bytes));
                    return $"rIdFont{n}";
                });
            foreach (var (uri, relId, bytes) in fontParts)
            {
                using (var s = pkg.CreatePart(uri, FontTableXml.ObfuscatedFontType)) s.Write(bytes, 0, bytes.Length);
                pkg.AddRelationship("/word/fontTable.xml", uri, RelFont, relId);
            }
            pkg.AddRelationship("/word/document.xml", "/word/fontTable.xml", RelFontTable, "rIdFontTable");
        }

        // ---- footnotes / endnotes ----
        var noteContexts = new List<PartContext>();
        foreach (var notes in new[] { doc.Footnotes, doc.Endnotes })
        {
            if (notes.Count == 0) continue;
            bool foot = notes.Kind == NoteKind.Footnote;
            var uri = foot ? "/word/footnotes.xml" : "/word/endnotes.xml";
            var nctx = new PartContext(uri, RegisterMedia, NextDrawingId) { Numbering = numbering, State = state, NoteKind = notes.Kind, AllowCommentReferences = true };
            noteContexts.Add(nctx);
            using var s = pkg.CreatePart(uri, foot ? CtFootnotes : CtEndnotes);
            using var x = CreateXml(s);
            WriteNotes(x, notes, nctx);
        }

        // ---- comments (+ commentsExtended for done flags and replies) ----
        PartContext? commentsCtx = null;
        bool commentsExtended = false;
        if (doc.Comments.Count > 0)
        {
            var paraIds = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
            var commentParaId = new Dictionary<Comment, string>(ReferenceEqualityComparer.Instance);
            int seq = 0;
            foreach (var c in doc.Comments)
            {
                var id = (0x10000000 + seq++).ToString("X8", CultureInfo.InvariantCulture);
                // Word names a comment by its last paragraph: a trailing table gets an empty paragraph after it, which carries the id.
                if (c.Blocks.Count > 0 && c.Blocks[^1] is Paragraph last) paraIds[last] = id;
                commentParaId[c] = id;
            }
            commentsCtx = new PartContext("/word/comments.xml", RegisterMedia, NextDrawingId) { Numbering = numbering, State = state, ParaIds = paraIds };
            using (var s = pkg.CreatePart("/word/comments.xml", CtComments))
            using (var x = CreateXml(s))
            {
                x.WriteStartDocument(standalone: true);
                x.WriteStartElement("w", "comments", W);
                x.WriteAttributeString("xmlns", "r", null, R);
                x.WriteAttributeString("xmlns", "mc", null, "http://schemas.openxmlformats.org/markup-compatibility/2006");
                x.WriteAttributeString("xmlns", "w14", null, W14);
                if (doc.Comments.SelectMany(c => c.Blocks).OfType<Paragraph>().Any(p => p.Runs.Any(r => r.Image is { } im && DrawingXml.NeedsWp14(im))))
                    x.WriteAttributeString("xmlns", "wp14", null, DrawingXml.Wp14);
                x.WriteAttributeString("mc", "Ignorable", "http://schemas.openxmlformats.org/markup-compatibility/2006",
                    doc.Comments.SelectMany(c => c.Blocks).OfType<Paragraph>().Any(p => p.Runs.Any(r => r.Image is { } im && DrawingXml.NeedsWp14(im))) ? "w14 wp14" : "w14");
                foreach (var c in doc.Comments)
                {
                    x.WriteStartElement("comment", W);
                    x.WriteAttributeString("id", W, state.CommentIds[c].ToString(CultureInfo.InvariantCulture));
                    x.WriteAttributeString("author", W, c.Author);
                    if (c.Date is DateTime cd)
                        x.WriteAttributeString("date", W, (cd.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(cd, DateTimeKind.Utc) : cd.ToUniversalTime())
                            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                    if (!string.IsNullOrEmpty(c.Initials)) x.WriteAttributeString("initials", W, c.Initials);
                    object? previous = null;
                    foreach (var block in c.Blocks)
                    {
                        if (block is Paragraph cp) WriteParagraph(x, cp, commentsCtx);
                        else if (block is DocTable ct)
                        {
                            if (previous is DocTable) WriteEmptyParagraph(x);
                            WriteTable(x, ct, commentsCtx);
                        }
                        previous = block;
                    }
                    if (previous is null or DocTable)
                    {
                        x.WriteStartElement("p", W);
                        x.WriteAttributeString("w14", "paraId", W14, commentParaId[c]);
                        x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
                x.WriteEndElement();
                x.WriteEndDocument();
            }
            commentsExtended = doc.Comments.Any(c => c.Done || c.Parent is not null);
            if (commentsExtended)
            {
                using var s = pkg.CreatePart("/word/commentsExtended.xml", CtCommentsExtended);
                using var x = CreateXml(s);
                x.WriteStartDocument(standalone: true);
                x.WriteStartElement("w15", "commentsEx", W15);
                x.WriteAttributeString("xmlns", "mc", null, "http://schemas.openxmlformats.org/markup-compatibility/2006");
                x.WriteAttributeString("mc", "Ignorable", "http://schemas.openxmlformats.org/markup-compatibility/2006", "w15");
                foreach (var c in doc.Comments)
                {
                    x.WriteStartElement("w15", "commentEx", W15);
                    x.WriteAttributeString("w15", "paraId", W15, commentParaId[c]);
                    if (c.Parent is { } parent && commentParaId.TryGetValue(parent, out var pp)) x.WriteAttributeString("w15", "paraIdParent", W15, pp);
                    x.WriteAttributeString("w15", "done", W15, c.Done ? "1" : "0");
                    x.WriteEndElement();
                }
                x.WriteEndElement();
                x.WriteEndDocument();
            }
        }

        // ---- media parts (binary) ----
        foreach (var (uri, img) in mediaParts)
        {
            using var s = pkg.CreatePart(uri, img.ContentType);
            s.Write(img.Bytes, 0, img.Bytes.Length);
        }

        // ---- docProps ----
        WriteCoreProps(pkg, doc.Properties);
        WriteAppProps(pkg, doc.Properties);
        bool custom = doc.Properties.Custom.Count > 0;
        if (custom) WriteCustomProps(pkg, doc.Properties);

        // ---- relationships ----
        pkg.AddRelationship("/", "/word/document.xml", RelOfficeDocument, "rId1");
        pkg.AddRelationship("/", "/docProps/core.xml", RelCore, "rId2");
        pkg.AddRelationship("/", "/docProps/app.xml", RelApp, "rId3");
        if (custom) pkg.AddRelationship("/", "/docProps/custom.xml", RelCustom, "rId4");

        pkg.AddRelationship("/word/document.xml", "/word/styles.xml", RelStyles, "rIdStyles");
        pkg.AddRelationship("/word/document.xml", "/word/settings.xml", RelSettings, "rIdSettings");
        if (doc.ThemeXml is { Length: > 0 })
            pkg.AddRelationship("/word/document.xml", "/word/theme/theme1.xml", RelTheme, "rIdTheme");
        if (hasLists)
            pkg.AddRelationship("/word/document.xml", "/word/numbering.xml", RelNumbering, "rIdNumbering");
        foreach (var part in hfParts)
            pkg.AddRelationship("/word/document.xml", part.Uri, part.RelType, part.RelId);
        if (doc.Footnotes.Count > 0) pkg.AddRelationship("/word/document.xml", "/word/footnotes.xml", RelFootnotes, "rIdFootnotes");
        if (doc.Endnotes.Count > 0) pkg.AddRelationship("/word/document.xml", "/word/endnotes.xml", RelEndnotes, "rIdEndnotes");
        if (doc.Comments.Count > 0) pkg.AddRelationship("/word/document.xml", "/word/comments.xml", RelComments, "rIdComments");
        if (commentsExtended) pkg.AddRelationship("/word/document.xml", "/word/commentsExtended.xml", RelCommentsExtended, "rIdCommentsExtended");

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
        foreach (var ctx in noteContexts) WriteContextRels(ctx);
        if (commentsCtx is not null) WriteContextRels(commentsCtx);

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
        if (ctx?.ParaIds is { } ids && ids.TryGetValue(p, out var paraId)) x.WriteAttributeString("w14", "paraId", W14, paraId);

        // pPr — paragraph properties in schema order (see ParagraphProperties).
        ParagraphProperties.Write(x, p, ctx?.Numbering, sectPr, ctx is null ? null : _ => ctx.State.NextRevisionId());
        leadingRuns?.Invoke(x);

        if (p.PageBreakBefore)
        {
            x.WriteStartElement("r", W);
            x.WriteStartElement("br", W);
            x.WriteAttributeString("type", W, "page");
            x.WriteEndElement();
            x.WriteEndElement();
        }

        // Simple fields (w:fldSimple) need their end in the same paragraph and no separate marker.
        // Simple fields (w:fldSimple) need their end in the same paragraph, no separate marker, and
        // proper nesting with the other simple fields of the paragraph (they become XML elements).
        HashSet<Field>? simpleOpen = null;
        HashSet<Field>? simpleHere = null;
        {
            List<Field>? stack = null;
            for (int i = 0; i < p.Runs.Count; i++)
            {
                if (p.Runs[i].FieldMark is not { Field.Simple: true } fm) continue;
                stack ??= new List<Field>();
                if (fm.Type == FieldMarkType.Begin) { if (!stack.Contains(fm.Field)) stack.Add(fm.Field); }
                else if (fm.Type == FieldMarkType.Separate) { int k = stack.IndexOf(fm.Field); if (k >= 0) stack.RemoveRange(k, stack.Count - k); }
                else
                {
                    int k = stack.IndexOf(fm.Field);
                    if (k < 0) continue;
                    if (k == stack.Count - 1) (simpleHere ??= new(ReferenceEqualityComparer.Instance)).Add(fm.Field);
                    stack.RemoveRange(k, stack.Count - k); // fields opened inside but not closed are not simple
                }
            }
        }

        bool deleting = false;
        void OpenRevision(RevisionMark mark)
            => ParagraphProperties.WriteRevisionStart(x, mark.Type == RevisionType.Delete ? "del" : "ins", mark, ctx!.State.NextRevisionId(), close: false);

        // Opens a run's tracked change (an insertion around a deletion when both are recorded); returns how many elements to close.
        int OpenRunRevision(Run run)
        {
            if (run.Revision is null || ctx is null) return 0;
            int n = 0;
            if (run.Revision.Type == RevisionType.Delete && run.InsertedRevision is not null) { OpenRevision(run.InsertedRevision); n++; }
            OpenRevision(run.Revision);
            return n + 1;
        }

        void Close(int n) { for (int k = 0; k < n; k++) x.WriteEndElement(); }

        for (int index = 0; index < p.Runs.Count; index++)
        {
            var current = p.Runs[index];
            // Tracked insertions / deletions wrap their run (links and simple fields place it themselves or keep it out).
            bool wrap = ctx is not null && current.Revision is not null && current.Bookmark is null && current.CommentRange is null
                        && !current.IsLink && current.FieldInstruction is null && current.FieldMark is not { Field.Simple: true };
            deleting = current.Revision is { Type: RevisionType.Delete } && ctx is not null;
            // Text one reviewer inserted and another deleted: w:ins around w:del, as Word writes it.
            int opened = wrap ? OpenRunRevision(current) : 0;
            WriteOneRun(current);
            Close(opened);
        }

        void WriteOneRun(Run run)
        {
            if (run.FieldInstruction is not null)
            {
                // <w:fldSimple w:instr=" PAGE "><w:r><w:t>1</w:t></w:r></w:fldSimple>
                x.WriteStartElement("fldSimple", W);
                x.WriteAttributeString("instr", W, run.FieldInstruction);
                // A tracked change goes inside the simple field, around its result run.
                int fieldOpened = OpenRunRevision(run);
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                WriteText(x, run.TextContent, deleting);
                x.WriteEndElement();
                Close(fieldOpened);
                x.WriteEndElement();
                return;
            }

            if (run.Image is not null && ctx is not null)
            {
                WriteDrawingRun(x, run.Image, ctx);
                return;
            }

            if (run.TextBox is { } box)
            {
                // Word does not allow a text box inside a text box: nested boxes are not written.
                if (ctx is not null && ctx.TextBoxDepth == 0) WriteTextBoxRun(x, box, ctx);
                return;
            }

            if (run.Bookmark is { } bm)
            {
                if (ctx is null) return;
                if (bm.IsStart)
                {
                    var (bid, bname) = ctx.State.StartBookmark(bm.Bookmark);
                    x.WriteStartElement("bookmarkStart", W);
                    x.WriteAttributeString("id", W, bid.ToString(CultureInfo.InvariantCulture));
                    x.WriteAttributeString("name", W, bname);
                    if (bm.Bookmark.ColumnFirst is int cf) x.WriteAttributeString("colFirst", W, cf.ToString(CultureInfo.InvariantCulture));
                    if (bm.Bookmark.ColumnLast is int cl) x.WriteAttributeString("colLast", W, cl.ToString(CultureInfo.InvariantCulture));
                    x.WriteEndElement();
                }
                else if (ctx.State.EndBookmark(bm.Bookmark) is int eid)
                {
                    x.WriteStartElement("bookmarkEnd", W);
                    x.WriteAttributeString("id", W, eid.ToString(CultureInfo.InvariantCulture));
                    x.WriteEndElement();
                }
                return;
            }

            if (run.CommentRange is { } range)
            {
                if (ctx is null) return;
                void Range(Comment c)
                {
                    if (!ctx.State.CommentIds.TryGetValue(c, out var cid)) return;
                    x.WriteStartElement(range.IsStart ? "commentRangeStart" : "commentRangeEnd", W);
                    x.WriteAttributeString("id", W, cid.ToString(CultureInfo.InvariantCulture));
                    x.WriteEndElement();
                }
                Range(range.Comment);
                if (ctx.State.RepliesWithoutMarkers.TryGetValue(range.Comment, out var replies)) foreach (var reply in replies) Range(reply);
                return;
            }

            if (run.CommentReference is { } commented)
            {
                if (ctx is null || !ctx.AllowCommentReferences) return; // body and notes only (not comments, headers or footers)
                void Reference(Comment c)
                {
                    if (!ctx.State.CommentIds.TryGetValue(c, out var cid)) return;
                    x.WriteStartElement("r", W);
                    WriteRunPr(x, run, run.CharacterStyleId, ctx);
                    x.WriteStartElement("commentReference", W);
                    x.WriteAttributeString("id", W, cid.ToString(CultureInfo.InvariantCulture));
                    x.WriteEndElement();
                    x.WriteEndElement();
                }
                Reference(commented);
                if (ctx.State.RepliesWithoutMarkers.TryGetValue(commented, out var replies)) foreach (var reply in replies) Reference(reply);
                return;
            }

            if (run.IsAnnotationMark)
            {
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                x.WriteStartElement("annotationRef", W);
                x.WriteEndElement();
                x.WriteEndElement();
                return;
            }

            if (run.FieldMark is { } mark)
            {
                var field = mark.Field;
                if (simpleHere is not null && simpleHere.Contains(field))
                {
                    if (mark.Type == FieldMarkType.Begin)
                    {
                        x.WriteStartElement("fldSimple", W);
                        x.WriteAttributeString("instr", W, field.Instruction);
                        if (field.Locked) x.WriteAttributeString("fldLock", W, "1");
                        if (field.Dirty) x.WriteAttributeString("dirty", W, "1");
                        (simpleOpen ??= new(ReferenceEqualityComparer.Instance)).Add(field);
                    }
                    else if (mark.Type == FieldMarkType.End && simpleOpen is not null && simpleOpen.Remove(field))
                        x.WriteEndElement();
                    return;
                }
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                x.WriteStartElement("fldChar", W);
                x.WriteAttributeString("fldCharType", W, mark.Type switch
                {
                    FieldMarkType.Begin => "begin",
                    FieldMarkType.Separate => "separate",
                    _ => "end",
                });
                if (mark.Type == FieldMarkType.Begin)
                {
                    if (field.Locked) x.WriteAttributeString("fldLock", W, "1");
                    if (field.Dirty) x.WriteAttributeString("dirty", W, "1");
                    foreach (var raw in field.RawData) StylesXml.WriteFragmentClean(x, raw);
                }
                x.WriteEndElement();
                x.WriteEndElement();
                if (mark.Type == FieldMarkType.Begin)
                {
                    WriteInstruction(x, field, run, 0, deleting);
                    // A simple field written in complex form (its markers did not nest): its result needs a separator.
                    if (field.Simple)
                    {
                        x.WriteStartElement("r", W);
                        WriteRunPr(x, run, run.CharacterStyleId, ctx);
                        x.WriteStartElement("fldChar", W);
                        x.WriteAttributeString("fldCharType", W, "separate");
                        x.WriteEndElement();
                        x.WriteEndElement();
                    }
                }
                return;
            }

            if (run.NoteReference is { } note)
            {
                // A removed note, or a reference outside the body: nothing is written (Word rejects both).
                if (ctx is null || !ctx.AllowNoteReferences || !ctx.State.NoteIds.TryGetValue(note, out var nid)) return;
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                x.WriteStartElement(note.Kind == NoteKind.Footnote ? "footnoteReference" : "endnoteReference", W);
                if (run.CustomMarkFollows) x.WriteAttributeString("customMarkFollows", W, "1");
                x.WriteAttributeString("id", W, nid.ToString(CultureInfo.InvariantCulture));
                x.WriteEndElement();
                x.WriteEndElement();
                return;
            }

            if (run.IsNoteMark)
            {
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                x.WriteStartElement(ctx?.NoteKind == NoteKind.Endnote ? "endnoteRef" : "footnoteRef", W);
                x.WriteEndElement();
                x.WriteEndElement();
                return;
            }

            if (run.Symbol is { } sym && !run.IsLink)
            {
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.CharacterStyleId, ctx);
                x.WriteStartElement("sym", W);
                x.WriteAttributeString("font", W, sym.Font);
                x.WriteAttributeString("char", W, ((int)sym.Code).ToString("X4", CultureInfo.InvariantCulture));
                x.WriteEndElement();
                x.WriteEndElement();
                return;
            }

            if (run.IsLink && ctx is not null)
            {
                x.WriteStartElement("hyperlink", W);
                if (run.HyperlinkUrl is not null) x.WriteAttributeString("id", R, ctx.AddHyperlink(run.HyperlinkUrl));
                if (run.HyperlinkAnchor is not null) x.WriteAttributeString("anchor", W, run.HyperlinkAnchor);
                if (run.HyperlinkTooltip is not null) x.WriteAttributeString("tooltip", W, run.HyperlinkTooltip);
                x.WriteAttributeString("history", W, "1");
                // A tracked change goes inside the link (w:ins / w:del cannot hold a w:hyperlink).
                int linkOpened = OpenRunRevision(run);
                x.WriteStartElement("r", W);
                WriteRunPr(x, run, run.EffectiveCharacterStyleId, ctx);
                WriteText(x, run.TextContent, deleting);
                x.WriteEndElement();
                Close(linkOpened);
                x.WriteEndElement();
                return;
            }

            // Skip empty non-image runs (e.g. an image run reached without a context).
            if (run.Image is not null) return;

            x.WriteStartElement("r", W);
            WriteRunPr(x, run, run.CharacterStyleId, ctx);
            if (run.Break is BreakType bt) WriteBreak(x, bt);
            else if (run.PositionalTab is { } ptab) WritePositionalTab(x, ptab);
            else if (run.IsTab) { x.WriteStartElement("tab", W); x.WriteEndElement(); }
            else WriteText(x, run.TextContent, deleting);
            x.WriteEndElement();
        
        }

        // A simple field left open (its end was elsewhere): close it so the XML stays well formed.
        if (simpleOpen is not null) foreach (var _ in simpleOpen) x.WriteEndElement();

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
    /// A text box as Word 2010+ writes it: a wps:wsp shape inside mc:AlternateContent (Choice
    /// Requires="wps"; readers that do not understand wps show nothing in its place).
    /// </summary>
    private static void WriteTextBoxRun(XmlWriter x, TextBox box, PartContext ctx)
    {
        const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        if (!double.IsFinite(box.WidthPt) || !double.IsFinite(box.HeightPt) || box.WidthPt < 0 || box.HeightPt < 0)
            throw new ArgumentOutOfRangeException(nameof(box), "Text box size must be finite and not negative.");
        var spec = new ImageSpec
        {
            Bytes = Array.Empty<byte>(), ContentType = "image/png", WidthPt = box.WidthPt, HeightPt = box.HeightPt,
            Placement = box.Placement, Position = box.Placement == ImagePlacement.Floating ? box.Position ?? new FloatingPosition() : null,
            Name = box.Name, AltText = box.AltText, RelativeWidth = box.RelativeWidth, RelativeHeight = box.RelativeHeight,
        };
        int id = ctx.NextDrawingId();
        bool wp14 = DrawingXml.NeedsWp14(spec);
        x.WriteStartElement("r", W);
        x.WriteStartElement("mc", "AlternateContent", Mc);
        x.WriteStartElement("mc", "Choice", Mc);
        x.WriteAttributeString("xmlns", "wps", null, DrawingXml.Wps);
        if (wp14) x.WriteAttributeString("xmlns", "wp14", null, DrawingXml.Wp14);
        x.WriteAttributeString("Requires", wp14 ? "wps wp14" : "wps");
        x.WriteStartElement("drawing", W);
        DrawingXml.WriteDrawing(x, spec, null, id, null, wp14, (w, cx, cy, name) =>
        {
            w.WriteStartElement("a", "graphic", A);
            w.WriteAttributeString("xmlns", "a", null, A);
            w.WriteStartElement("a", "graphicData", A);
            w.WriteAttributeString("uri", DrawingXml.Wps);
            w.WriteStartElement("wps", "wsp", DrawingXml.Wps);
            w.WriteStartElement("wps", "cNvSpPr", DrawingXml.Wps);
            w.WriteAttributeString("txBox", "1");
            w.WriteEndElement();
            if (box.RawSpPrValid) StylesXml.WriteFragmentClean(w, box.RawSpPr!);
            else
            {
            w.WriteStartElement("wps", "spPr", DrawingXml.Wps);
            w.WriteStartElement("a", "xfrm", A);
            if (!double.IsFinite(box.RotationDegrees)) throw new ArgumentOutOfRangeException(nameof(box), "Rotation must be finite.");
            long rot = (long)Math.Round(((box.RotationDegrees % 360) + 360) % 360 * 60000);
            if (rot != 0) w.WriteAttributeString("rot", rot.ToString(CultureInfo.InvariantCulture));
            w.WriteStartElement("a", "off", A); w.WriteAttributeString("x", "0"); w.WriteAttributeString("y", "0"); w.WriteEndElement();
            w.WriteStartElement("a", "ext", A);
            w.WriteAttributeString("cx", cx.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("cy", cy.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteEndElement(); // xfrm
            w.WriteStartElement("a", "prstGeom", A);
            w.WriteAttributeString("prst", "rect");
            w.WriteStartElement("a", "avLst", A); w.WriteEndElement();
            w.WriteEndElement();
            void Solid(string hex)
            {
                var h = hex.TrimStart('#');
                if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException("Text box colours are RRGGBB.", nameof(box));
                w.WriteStartElement("a", "solidFill", A);
                w.WriteStartElement("a", "srgbClr", A);
                w.WriteAttributeString("val", h.ToUpperInvariant());
                w.WriteEndElement();
                w.WriteEndElement();
            }
            if (box.FillHex is { } fill) Solid(fill);
            else { w.WriteStartElement("a", "noFill", A); w.WriteEndElement(); }
            w.WriteStartElement("a", "ln", A);
            if (box.LineWidthPt is double lw)
            {
                if (!double.IsFinite(lw) || lw < 0 || lw > 1584) throw new ArgumentOutOfRangeException(nameof(box), "Outline width must be 0–1584pt.");
                w.WriteAttributeString("w", ((long)Math.Round(lw * EmuPerPoint)).ToString(CultureInfo.InvariantCulture));
            }
            if (box.LineHex is { } line) Solid(line);
            else { w.WriteStartElement("a", "noFill", A); w.WriteEndElement(); }
            w.WriteEndElement(); // ln
            w.WriteEndElement(); // spPr
            }
            if (box.RawStyle is { } style) StylesXml.WriteFragmentClean(w, style);
            w.WriteStartElement("wps", "txbx", DrawingXml.Wps);
            w.WriteStartElement("txbxContent", W);
            ctx.TextBoxDepth++;
            try
            {
                object? previous = null;
                foreach (var block in box.Blocks)
                {
                    if (block is Paragraph bp) WriteParagraph(w, bp, ctx);
                    else if (block is DocTable bt)
                    {
                        if (previous is DocTable) WriteEmptyParagraph(w);
                        WriteTable(w, bt, ctx);
                    }
                    previous = block;
                }
                if (previous is null or DocTable) WriteEmptyParagraph(w);
            }
            finally { ctx.TextBoxDepth--; }
            w.WriteEndElement(); // txbxContent
            w.WriteEndElement(); // txbx
            if (box.RawBodyPrValid) StylesXml.WriteFragmentClean(w, box.RawBodyPr!);
            else
            {
            w.WriteStartElement("wps", "bodyPr", DrawingXml.Wps);
            w.WriteAttributeString("rot", "0");
            w.WriteAttributeString("vert", "horz");
            w.WriteAttributeString("wrap", "square");
            var ins = box.Insets ?? new ImageEffectExtent(7.2, 3.6, 7.2, 3.6);
            string E(double pt)
            {
                if (!double.IsFinite(pt) || pt < 0 || pt > 1584) throw new ArgumentOutOfRangeException(nameof(box), "Insets must be 0–1584pt.");
                return ((long)Math.Round(pt * EmuPerPoint)).ToString(CultureInfo.InvariantCulture);
            }
            w.WriteAttributeString("lIns", E(ins.LeftPt));
            w.WriteAttributeString("tIns", E(ins.TopPt));
            w.WriteAttributeString("rIns", E(ins.RightPt));
            w.WriteAttributeString("bIns", E(ins.BottomPt));
            w.WriteAttributeString("anchor", box.VerticalAnchor switch { TextBoxVerticalAnchor.Center => "ctr", TextBoxVerticalAnchor.Bottom => "b", _ => "t" });
            w.WriteAttributeString("anchorCtr", "0");
            w.WriteStartElement("a", box.AutoFit ? "spAutoFit" : "noAutofit", A);
            w.WriteEndElement();
            w.WriteEndElement(); // bodyPr
            }
            w.WriteEndElement(); // wsp
            w.WriteEndElement(); // graphicData
            w.WriteEndElement(); // graphic
        });
        x.WriteEndElement(); // drawing
        x.WriteEndElement(); // Choice
        x.WriteEndElement(); // AlternateContent
        x.WriteEndElement(); // r
    }

    /// <summary>
    /// Writes run content as <c>w:t</c> segments, turning line feeds (<c>\n</c>, <c>\r\n</c>,
    /// <c>\r</c>) into <c>w:br</c> and tab characters into <c>w:tab</c> — the elements Word
    /// itself uses. A literal control character inside <c>w:t</c> would render as a space.
    /// </summary>
    internal static void WriteText(XmlWriter x, string text) => WriteText(x, text, false);

    /// <summary>Writes run content; in a tracked deletion the text goes in w:delText.</summary>
    internal static void WriteText(XmlWriter x, string text, bool deleted)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r' && c != '\t') continue;
            if (i > start) WriteTextSegment(x, text.Substring(start, i - start), deleted);
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
            WriteTextSegment(x, start == 0 ? text : text.Substring(start), deleted);
    }

    private static void WriteTextSegment(XmlWriter x, string text, bool deleted = false)
    {
        x.WriteStartElement(deleted ? "delText" : "t", W);
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
        => TableXml.Write(x, t, (w, p) => WriteParagraph(w, p, ctx), _ => ctx.State.NextRevisionId());

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

    /// <summary>A run's rPr, with its tracked formatting change (w:rPrChange) when it has one.</summary>
    private static void WriteRunPr(XmlWriter x, Run run, string? characterStyleId, PartContext? ctx)
    {
        if (run.FormatChange is { } change && ctx is not null)
            RunProperties.Write(x, run.Format, characterStyleId, null,
                w => ParagraphProperties.WriteRunFormatChange(w, change, ctx.State.NextRevisionId()));
        else RunProperties.Write(x, run.Format, characterStyleId);
    }

    /// <summary>The instruction runs of a field: text as w:instrText, nested fields as complete fields with their cached result.</summary>
    private static void WriteInstruction(XmlWriter x, Field field, Run run, int depth, bool deleted = false)
    {
        foreach (var part in field.InstructionParts)
        {
            if (part is string text)
            {
                if (text.Length == 0) continue;
                x.WriteStartElement("r", W);
                RunProperties.Write(x, run.Format, run.CharacterStyleId);
                x.WriteStartElement(deleted ? "delInstrText" : "instrText", W);
                x.WriteAttributeString("xml", "space", null, "preserve");
                x.WriteString(text);
                x.WriteEndElement();
                x.WriteEndElement();
            }
            else if (part is Field nested && depth < 16)
            {
                void Char(string type, bool begin)
                {
                    x.WriteStartElement("r", W);
                    RunProperties.Write(x, run.Format, run.CharacterStyleId);
                    x.WriteStartElement("fldChar", W);
                    x.WriteAttributeString("fldCharType", W, type);
                    if (begin)
                    {
                        if (nested.Locked) x.WriteAttributeString("fldLock", W, "1");
                        if (nested.Dirty) x.WriteAttributeString("dirty", W, "1");
                        foreach (var raw in nested.RawData) StylesXml.WriteFragmentClean(x, raw);
                    }
                    x.WriteEndElement();
                    x.WriteEndElement();
                }
                Char("begin", true);
                WriteInstruction(x, nested, run, depth + 1, deleted);
                Char("separate", false);
                if (!string.IsNullOrEmpty(nested.CachedResult))
                {
                    x.WriteStartElement("r", W);
                    RunProperties.Write(x, run.Format, run.CharacterStyleId);
                    WriteText(x, nested.CachedResult, deleted);
                    x.WriteEndElement();
                }
                Char("end", false);
            }
        }
    }

    // ---- Notes -------------------------------------------------------------------------

    private static void WriteNotes(XmlWriter x, NoteCollection notes, PartContext ctx)
    {
        bool foot = notes.Kind == NoteKind.Footnote;
        string element = foot ? "footnote" : "endnote";
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", foot ? "footnotes" : "endnotes", W);
        x.WriteAttributeString("xmlns", "r", null, R);
        if (notes.SelectMany(n => n.Blocks).OfType<Paragraph>().Any(p => p.Runs.Any(r => r.Image is { } im && DrawingXml.NeedsWp14(im)))) DeclareWp14(x);
        // Word's separators: the line above notes, and the line when notes continue on the next page.
        foreach (var (type, id, mark) in new[] { ("separator", "-1", "separator"), ("continuationSeparator", "0", "continuationSeparator") })
        {
            x.WriteStartElement(element, W);
            x.WriteAttributeString("type", W, type);
            x.WriteAttributeString("id", W, id);
            x.WriteStartElement("p", W);
            x.WriteStartElement("pPr", W);
            x.WriteStartElement("spacing", W);
            x.WriteAttributeString("after", W, "0");
            x.WriteAttributeString("line", W, "240");
            x.WriteAttributeString("lineRule", W, "auto");
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteStartElement("r", W);
            x.WriteStartElement(mark, W);
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndElement();
        }
        foreach (var note in notes)
        {
            x.WriteStartElement(element, W);
            x.WriteAttributeString("id", W, ctx.State.NoteIds[note].ToString(CultureInfo.InvariantCulture));
            object? previous = null;
            foreach (var block in note.Blocks)
            {
                if (block is Paragraph np) WriteParagraph(x, np, ctx);
                else if (block is DocTable nt)
                {
                    if (previous is DocTable) WriteEmptyParagraph(x);
                    WriteTable(x, nt, ctx);
                }
                previous = block;
            }
            if (previous is null or DocTable) WriteEmptyParagraph(x); // a note ends with a paragraph
            x.WriteEndElement();
        }
        x.WriteEndElement();
        x.WriteEndDocument();
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

        // CT_Settings order: … embedTrueTypeFonts, saveSubsetFonts, mirrorMargins, …, gutterAtTop, …, documentProtection, …,
        // defaultTabStop, …, evenAndOddHeaders, …, updateFields, footnotePr, endnotePr …
        if (doc.EmbedTrueTypeFonts) { x.WriteStartElement("embedTrueTypeFonts", W); x.WriteEndElement(); }
        if (doc.SaveSubsetFonts) { x.WriteStartElement("saveSubsetFonts", W); x.WriteEndElement(); }
        if (doc.MirrorMargins) { x.WriteStartElement("mirrorMargins", W); x.WriteEndElement(); }
        if (doc.GutterAtTop) { x.WriteStartElement("gutterAtTop", W); x.WriteEndElement(); }
        if (doc.TrackRevisions) { x.WriteStartElement("trackRevisions", W); x.WriteEndElement(); }

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

        if (doc.UpdateFieldsOnOpen) { x.WriteStartElement("updateFields", W); x.WriteAttributeString("val", W, "true"); x.WriteEndElement(); }

        // Document-wide note properties; with notes, the separator references Word expects.
        foreach (var (kind, props, count) in new[] { (NoteKind.Footnote, doc.FootnoteProperties, doc.Footnotes.Count), (NoteKind.Endnote, doc.EndnoteProperties, doc.Endnotes.Count) })
        {
            string element = kind == NoteKind.Footnote ? "footnote" : "endnote";
            Action<XmlWriter>? separators = count == 0 ? null : w =>
            {
                foreach (var id in new[] { "-1", "0" })
                {
                    w.WriteStartElement(element, W);
                    w.WriteAttributeString("id", W, id);
                    w.WriteEndElement();
                }
            };
            NotesXml.Write(x, kind, props, separators);
        }

        x.WriteEndElement();
        x.WriteEndDocument();
    }

    // ---- docProps ----------------------------------------------------------------------------

    private static void WriteCoreProps(OoxmlPackage pkg, DocumentProperties props)
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
        void Text(string prefix, string ns, string name, string? value)
        {
            if (value is null) return;
            x.WriteStartElement(prefix, name, ns);
            x.WriteString(value);
            x.WriteEndElement();
        }
        void Date(string prefix, string ns, string name, DateTime value, bool typed)
        {
            x.WriteStartElement(prefix, name, ns);
            if (typed) x.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
            x.WriteString(Utc(value).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }
        Text("dc", dc, "title", props.Title);
        Text("dc", dc, "subject", props.Subject);
        Text("dc", dc, "creator", props.Author ?? "Chuvadi.Docs");
        Text("cp", cp, "keywords", props.Keywords);
        Text("dc", dc, "description", props.Description);
        Text("cp", cp, "lastModifiedBy", props.LastModifiedBy);
        Text("cp", cp, "revision", props.Revision);
        if (props.LastPrinted is DateTime printed) Date("cp", cp, "lastPrinted", printed, false);
        var now = DateTime.UtcNow;
        Date("dcterms", dcterms, "created", props.Created ?? now, true);
        // A loaded document's modified date, unchanged since loading, becomes the time of this save (as Word does).
        Date("dcterms", dcterms, "modified", props.Modified is { } mod && mod != props.LoadedModified ? mod : now, true);
        Text("cp", cp, "category", props.Category);
        Text("cp", cp, "contentStatus", props.ContentStatus);
        Text("dc", dc, "language", props.Language);
        Text("dc", dc, "identifier", props.Identifier);
        Text("cp", cp, "version", props.Version);
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    private static void WriteAppProps(OoxmlPackage pkg, DocumentProperties props)
    {
        const string ep = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        using var s = pkg.CreatePart("/docProps/app.xml", CtApp);
        using var x = CreateXml(s);
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("Properties", ep);
        void Text(string name, string? value)
        {
            if (value is null) return;
            x.WriteStartElement(name, ep);
            x.WriteString(value);
            x.WriteEndElement();
        }
        Text("Template", props.Template);
        Text("Application", "Chuvadi.Docs");
        Text("Manager", props.Manager);
        Text("Company", props.Company);
        Text("HyperlinkBase", props.HyperlinkBase);
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    /// <summary>UTC for writing: an Unspecified date is taken as already UTC (no shift by the local offset).</summary>
    private static DateTime Utc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    private static void WriteCustomProps(OoxmlPackage pkg, DocumentProperties props)
    {
        const string cust = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        const string vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        using var s = pkg.CreatePart("/docProps/custom.xml", CtCustom);
        using var x = CreateXml(s);
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("Properties", cust);
        x.WriteAttributeString("xmlns", "vt", null, vt);
        int pid = 2; // 0 and 1 are reserved
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in props.Custom)
        {
            if (string.IsNullOrWhiteSpace(name) || value is null || !seen.Add(name)) continue; // first of duplicate names wins
            x.WriteStartElement("property", cust);
            x.WriteAttributeString("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");
            x.WriteAttributeString("pid", (pid++).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("name", name);
            var (type, text) = value switch
            {
                bool b => ("bool", b ? "true" : "false"),
                int i => ("i4", i.ToString(CultureInfo.InvariantCulture)),
                short or byte or sbyte or ushort => ("i4", Convert.ToString(value, CultureInfo.InvariantCulture)!),
                uint u => ("ui4", u.ToString(CultureInfo.InvariantCulture)),
                long l => ("i8", l.ToString(CultureInfo.InvariantCulture)),
                ulong ul => ("ui8", ul.ToString(CultureInfo.InvariantCulture)),
                double d => ("r8", d.ToString("R", CultureInfo.InvariantCulture)),
                float f => ("r8", ((double)f).ToString("R", CultureInfo.InvariantCulture)),
                decimal m => ("decimal", m.ToString(CultureInfo.InvariantCulture)),
                DateTime dt => ("filetime", Utc(dt).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                _ => ("lpwstr", Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
            };
            x.WriteStartElement("vt", type, vt);
            x.WriteString(text);
            x.WriteEndElement();
            x.WriteEndElement();
        }
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    // ---- Helpers --------------------------------------------------------------------------

    /// <summary>Every paragraph in the document: body, table cells and headers/footers.</summary>
    /// <summary>Every paragraph: body (tables and nested tables included, in order), then headers and footers.</summary>
    internal static IEnumerable<Paragraph> AllParagraphs(Document doc)
    {
        var seenBoxes = new HashSet<TextBox>(ReferenceEqualityComparer.Instance);
        foreach (var p in AllParagraphsCore(doc))
        {
            yield return p;
            foreach (var inner in TextBoxParagraphs(p, 0, seenBoxes)) yield return inner;
        }
    }

    /// <summary>Paragraphs inside the text boxes of <paramref name="p"/> (and of tables in them), each box once, depth-guarded.</summary>
    internal static IEnumerable<Paragraph> TextBoxParagraphs(Paragraph p, int depth, HashSet<TextBox> seen)
    {
        if (depth > 8) yield break;
        foreach (var r in p.Runs)
        {
            if (r.TextBox is not { } box || !seen.Add(box)) continue;
            foreach (var b in box.Blocks)
            {
                if (b is Paragraph bp)
                {
                    yield return bp;
                    foreach (var inner in TextBoxParagraphs(bp, depth + 1, seen)) yield return inner;
                }
                else if (b is DocTable bt)
                    foreach (var tp in TableParagraphs(bt, 0))
                    {
                        yield return tp;
                        foreach (var inner in TextBoxParagraphs(tp, depth + 1, seen)) yield return inner;
                    }
            }
        }
    }

    private static IEnumerable<Paragraph> AllParagraphsCore(Document doc)
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
        foreach (var notes in new[] { doc.Footnotes, doc.Endnotes })
            foreach (var note in notes)
                foreach (var b in note.Blocks)
                {
                    if (b is Paragraph np) yield return np;
                    else if (b is DocTable nt)
                        foreach (var tp in TableParagraphs(nt, 0)) yield return tp;
                }
        foreach (var c in doc.Comments)
            foreach (var b in c.Blocks)
            {
                if (b is Paragraph cp) yield return cp;
                else if (b is DocTable ct)
                    foreach (var tp in TableParagraphs(ct, 0)) yield return tp;
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
