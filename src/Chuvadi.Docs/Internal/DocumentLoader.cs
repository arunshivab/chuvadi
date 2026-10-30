using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Word;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Builds an editable <see cref="Document"/> from a docx package stream: styles and theme,
/// numbering, settings, body paragraphs (full run and paragraph formatting) and tables,
/// sections with their header/footer parts and watermarks. What the model cannot represent
/// (drawings, most fields, comments, tracked changes, content controls) is dropped — see
/// the <see cref="Document"/> docs.
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
        CheckMainPart(pkg, documentUri);

        var doc = new Document();

        // Theme part (theme fonts/colours), carried through verbatim, within the part-size limit.
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal || !rel.Type.EndsWith("/theme", StringComparison.Ordinal)) continue;
            var themeUri = rel.Target.StartsWith('/') ? rel.Target
                : documentUri[..(documentUri.LastIndexOf('/') + 1)] + rel.Target;
            Stream? themeRaw = null;
            try { themeRaw = pkg.OpenPart(themeUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (themeRaw is null) break;
            using var ts = GuardedPartStream.Wrap(themeRaw, options.MaxPartBytes, ct, themeUri);
            using var tm = new MemoryStream();
            ts.CopyTo(tm);
            doc.ThemeXml = tm.ToArray();
            break;
        }

        // Lists (numbering.xml): definitions and instances; paragraphs and styles are linked after loading.
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal || !rel.Type.EndsWith("/numbering", StringComparison.Ordinal)) continue;
            var numberingUri = rel.Target.StartsWith('/') ? rel.Target
                : documentUri[..(documentUri.LastIndexOf('/') + 1)] + rel.Target;
            Stream? numberingRaw = null;
            try { numberingRaw = pkg.OpenPart(numberingUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (numberingRaw is null) break;
            using var ns = GuardedPartStream.Wrap(numberingRaw, options.MaxPartBytes, ct, numberingUri);
            using var nr = GuardedXmlReader.Create(ns, options.MaxXmlDepth, ct, numberingUri);
            NumberingXml.Read(nr, doc.Numbering);
            EnsureWellFormed(nr, numberingUri);
            // Images that picture bullets refer to (numbering.xml's own relationships).
            if (doc.Numbering.RawPictureBullets.Count > 0)
            {
                var baseDir = numberingUri[..(numberingUri.LastIndexOf('/') + 1)];
                foreach (var pr in pkg.GetRelationships(numberingUri))
                {
                    if (pr.IsExternal || !pr.Type.EndsWith("/image", StringComparison.Ordinal)) continue;
                    if (doc.Numbering.RawPictureParts.Count >= 64) break;
                    var target = pr.Target.StartsWith('/') ? pr.Target : baseDir + pr.Target;
                    Stream? imgRaw = null;
                    try { imgRaw = pkg.OpenPart(target); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
                    if (imgRaw is null) continue;
                    using var gs = GuardedPartStream.Wrap(imgRaw, options.MaxPartBytes, ct, target);
                    using var ms = new MemoryStream();
                    gs.CopyTo(ms);
                    var bytes = ms.ToArray();
                    var ext = Path.GetExtension(target).TrimStart('.');
                    var type = ImageMetadata.DetectContentType(bytes) ?? ImageMetadata.ContentTypeForExtension(ext);
                    if (type is not null) doc.Numbering.RawPictureParts.Add((pr.Id, pr.Type, bytes, type));
                }
            }
            break;
        }

        // Styles and document defaults (styles.xml), when the document has them.
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal || !rel.Type.EndsWith("/styles", StringComparison.Ordinal)) continue;
            var stylesUri = rel.Target.StartsWith('/') ? rel.Target
                : documentUri[..(documentUri.LastIndexOf('/') + 1)] + rel.Target;
            Stream? stylesRaw = null;
            try { stylesRaw = pkg.OpenPart(stylesUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (stylesRaw is null) break;
            using var ss = GuardedPartStream.Wrap(stylesRaw, options.MaxPartBytes, ct, stylesUri);
            using var sr = GuardedXmlReader.Create(ss, options.MaxXmlDepth, ct, stylesUri);
            StylesXml.Read(sr, doc);
            EnsureWellFormed(sr, stylesUri);
            break;
        }

        // Document-wide page settings (settings.xml): even/odd headers, mirror margins, gutter position.
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal || !rel.Type.EndsWith("/settings", StringComparison.Ordinal)) continue;
            var settingsUri = Resolve(documentUri, rel.Target);
            Stream? settingsRaw = null;
            try { settingsRaw = pkg.OpenPart(settingsUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (settingsRaw is null) break;
            using var gs = GuardedPartStream.Wrap(settingsRaw, options.MaxPartBytes, ct, settingsUri);
            using var gr = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, settingsUri);
            try
            {
                ReadSettings(gr, doc);
                EnsureWellFormed(gr, settingsUri);
            }
            catch (XmlException)
            {
                // A malformed settings part only carries page flags; the document still loads.
            }
            break;
        }

        LoadProperties(pkg, doc, options, ct);
        LoadFontTable(pkg, documentUri, doc, options, ct);

        (Section, System.Collections.Generic.List<SectionXml.Reference>)? bodySectPr = null;
        using var raw = pkg.OpenPart(documentUri);
        using var s = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, documentUri);
        using var r = GuardedXmlReader.Create(s, options.MaxXmlDepth, ct, documentUri);
        var mediaCache = new System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?>(StringComparer.Ordinal);
        doc.LoadedMediaCache = mediaCache;
        var bodyImages = new ImageSource(pkg, documentUri, options, ct, mediaCache);
        var loadContext = new LoadContext();
        LoadComments(pkg, documentUri, doc, loadContext, options, ct, mediaCache);
        LoadNotes(pkg, documentUri, doc, loadContext, options, ct, mediaCache);
        var body = new StoryContext(loadContext, bodyImages);

        if (!r.Read()) return doc;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.ReadSubtree()) table = LoadTable(sub, body);
                    r.Read();
                    doc.AddBlock(table);
                    continue;
                }
                if (r.LocalName is "bookmarkStart" or "bookmarkEnd" or "commentRangeStart" or "commentRangeEnd")
                {
                    if (BookmarkMarker(r, body) is { } marker) body.Pending.Add(marker);
                    if (!r.Read()) break;
                    continue;
                }
                if (r.LocalName == "sectPr")
                {
                    using (var sub = r.ReadSubtree()) bodySectPr = SectionXml.Read(sub);
                    r.Read();
                    continue;
                }
                if (r.LocalName == "p" && IsBodyLevel(r))
                {
                    Paragraph p;
                    using (var sub = r.ReadSubtree()) p = LoadParagraph(sub, body);
                    r.Read();
                    doc.AddBlock(p);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        EnsureWellFormed(r, documentUri);
        body.Finish();
        LoadSections(pkg, documentUri, doc, bodySectPr, options, ct, loadContext);
        doc.LoadedMediaCache = null;
        // Word keeps even-page headers after "Different odd & even" is switched off: without the setting they stay hidden.
        doc.DifferentOddAndEvenPages ??= false;
        LinkNumbering(doc);
        return doc;
    }

    private static string Resolve(string sourceUri, string target)
        => target.StartsWith('/') ? target : sourceUri[..(sourceUri.LastIndexOf('/') + 1)] + target;

    private static void ReadSettings(XmlReader r, Document doc)
    {
        if (!r.Read()) return;
        r.MoveToContent();
        if (r.IsEmptyElement) return;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "evenAndOddHeaders": doc.DifferentOddAndEvenPages = RunProperties.OnOff(r) == true; break;
                case "mirrorMargins": doc.MirrorMargins = RunProperties.OnOff(r) == true; break;
                case "gutterAtTop": doc.GutterAtTop = RunProperties.OnOff(r) == true; break;
                case "updateFields": doc.UpdateFieldsOnOpen = RunProperties.OnOff(r) == true; break;
                case "trackRevisions": doc.TrackRevisions = RunProperties.OnOff(r) == true; break;
                case "footnotePr":
                    using (var sub = r.ReadSubtree()) NotesXml.Read(sub, doc.FootnoteProperties);
                    continue;
                case "endnotePr":
                    using (var sub = r.ReadSubtree()) NotesXml.Read(sub, doc.EndnoteProperties);
                    continue;
                case "embedTrueTypeFonts": doc.EmbedTrueTypeFonts = RunProperties.OnOff(r) == true; break;
                case "saveSubsetFonts": doc.SaveSubsetFonts = RunProperties.OnOff(r) == true; break;
                case "defaultTabStop":
                    // Twips; a zero, negative or absurd value is ignored (Word falls back to 720).
                    if (RunProperties.FromTwips(r.GetAttribute("val", W)) is double tab && tab > 0 && tab <= 1584)
                        doc.DefaultTabStopPt = tab;
                    break;
            }
            if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
        }
    }

    /// <summary>
    /// Builds the sections: one per top-level paragraph carrying w:sectPr, then the body's final
    /// w:sectPr, with their header/footer parts (each part read once) and text watermarks.
    /// </summary>
    private static void LoadSections(OoxmlPackage pkg, string documentUri, Document doc,
        (Section Section, System.Collections.Generic.List<SectionXml.Reference> References)? bodySectPr,
        DocxReaderOptions options, CancellationToken ct, LoadContext loadContext)
    {
        var rels = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in pkg.GetRelationships(documentUri))
            if (!rel.IsExternal && (rel.Type.EndsWith("/header", StringComparison.Ordinal) || rel.Type.EndsWith("/footer", StringComparison.Ordinal)))
                rels[rel.Id] = Resolve(documentUri, rel.Target);
        var parts = new System.Collections.Generic.Dictionary<string, (HeaderFooterContent Content, TextWatermark? Watermark)>(StringComparer.Ordinal);

        Section Build((Section Section, System.Collections.Generic.List<SectionXml.Reference> References) loaded)
        {
            var (section, refs) = loaded;
            foreach (var rf in refs)
            {
                if (!rels.TryGetValue(rf.RelId, out var uri)) continue;
                if (!parts.TryGetValue(uri, out var part))
                {
                    try { part = LoadHeaderFooter(pkg, uri, options, ct, doc.LoadedMediaCache ?? new(), loadContext); }
                    catch (XmlException) { part = (new HeaderFooterContent(), null); } // a malformed header/footer loads blank
                    parts[uri] = part;
                }
                // One object per part: sections referencing the same part share it (written once again).
                part.Content.LinkToPrevious = false; // a reference exists: an empty part is a deliberate blank
                section.SetSlot(rf.IsHeader, rf.Type, part.Content);
                if (rf.IsHeader && part.Watermark is { } wm)
                {
                    if (section.Watermark is null || rf.Type == "default")
                    {
                        var slots = section.WatermarkSlots;
                        section.Watermark = wm.Clone();
                        section.WatermarkSlots = slots;
                    }
                    (section.WatermarkSlots ??= new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)).Add(rf.Type);
                }
            }
            if (section.Watermark is not null) section.WatermarkSlots ??= new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            // No w:titlePg: no different first page — unless nothing first-page exists, then stay automatic.
            if (section.TitlePage is null && refs.Exists(r => r.Type == "first")) section.TitlePage = false;
            return section;
        }

        foreach (var b in doc.Blocks)
        {
            if (b is not Paragraph p || p.LoadedSection is not { } loaded) continue;
            p.LoadedSection = null;
            p.SectionProperties = Build(loaded);
        }
        if (bodySectPr is { } body) doc.SetFinalSection(Build(body));
    }

    /// <summary>word/fontTable.xml with embedded fonts (best effort: a malformed font table is ignored).</summary>
    private static void LoadFontTable(OoxmlPackage pkg, string documentUri, Document doc, DocxReaderOptions options, CancellationToken ct)
    {
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal || !rel.Type.EndsWith("/fontTable", StringComparison.Ordinal)) continue;
            var uri = Resolve(documentUri, rel.Target);
            Stream? raw = null;
            try { raw = pkg.OpenPart(uri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (raw is null) return;
            var targets = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fr in pkg.GetRelationships(uri))
                if (!fr.IsExternal && fr.Type.EndsWith("/font", StringComparison.Ordinal)) targets[fr.Id] = Resolve(uri, fr.Target);
            (byte[] Bytes, string ContentType)? Embed(string relId)
            {
                if (!targets.TryGetValue(relId, out var target)) return null;
                Stream? fs = null;
                try { fs = pkg.OpenPart(target); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
                if (fs is null) return null;
                using var gs = GuardedPartStream.Wrap(fs, options.MaxPartBytes, ct, target);
                using var ms = new MemoryStream();
                gs.CopyTo(ms);
                var type = pkg.GetPart(target)?.ContentType ?? "";
                return ms.Length >= 32 ? (ms.ToArray(), type) : null;
            }
            try
            {
                using var gs = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, uri);
                using var r = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, uri);
                FontTableXml.Read(r, doc.Fonts, Embed);
                EnsureWellFormed(r, uri);
            }
            catch (XmlException) { doc.Fonts.Clear(); }
            return;
        }
    }

    /// <summary>docProps core, app and custom properties (best effort: a malformed part is ignored).</summary>
    private static void LoadProperties(OoxmlPackage pkg, Document doc, DocxReaderOptions options, CancellationToken ct)
    {
        var props = doc.Properties;
        foreach (var rel in pkg.GetRelationships("/"))
        {
            if (rel.IsExternal) continue;
            int kind = rel.Type.EndsWith("/core-properties", StringComparison.Ordinal) ? 0
                : rel.Type.EndsWith("/extended-properties", StringComparison.Ordinal) ? 1
                : rel.Type.EndsWith("/custom-properties", StringComparison.Ordinal) ? 2 : -1;
            if (kind < 0) continue;
            var uri = "/" + rel.Target.TrimStart('/');
            Stream? raw = null;
            try { raw = pkg.OpenPart(uri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (raw is null) continue;
            try
            {
                using var gs = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, uri);
                using var r = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, uri);
                var xd = new XmlDocument { XmlResolver = null };
                xd.Load(r);
                if (xd.DocumentElement is null) continue;
                foreach (XmlNode node in xd.DocumentElement.ChildNodes)
                {
                    if (node is not XmlElement e) continue;
                    if (kind == 2)
                    {
                        if (e.LocalName != "property" || props.Custom.Count >= 4096) continue;
                        var name = e.GetAttribute("name");
                        var v = e.ChildNodes.OfType<XmlElement>().FirstOrDefault();
                        if (string.IsNullOrEmpty(name) || v is null) continue;
                        var text = v.InnerText;
                        object value = v.LocalName switch
                        {
                            "bool" => text.Trim() is "true" or "1",
                            "i1" or "i2" or "i4" or "int" or "ui1" or "ui2" when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
                            "ui4" when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) => u,
                            "i8" or "ui8" when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
                            "r4" or "r8" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                            "decimal" or "cy" when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) => m,
                            "filetime" or "date" when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) => dt,
                            _ => text,
                        };
                        props.Custom.Add(new(name, value));
                        continue;
                    }
                    var content = e.InnerText;
                    DateTime? Date() => DateTime.TryParse(content, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dv) ? dv : null;
                    if (kind == 0)
                        switch (e.LocalName)
                        {
                            case "title": props.Title = content; break;
                            case "subject": props.Subject = content; break;
                            case "creator": props.Author = content; break;
                            case "keywords": props.Keywords = content; break;
                            case "description": props.Description = content; break;
                            case "category": props.Category = content; break;
                            case "contentStatus": props.ContentStatus = content; break;
                            case "lastModifiedBy": props.LastModifiedBy = content; break;
                            case "revision": props.Revision = content; break;
                            case "language": props.Language = content; break;
                            case "identifier": props.Identifier = content; break;
                            case "version": props.Version = content; break;
                            case "created": props.Created = Date(); break;
                            case "modified": props.Modified = props.LoadedModified = Date(); break;
                            case "lastPrinted": props.LastPrinted = Date(); break;
                        }
                    else
                        switch (e.LocalName)
                        {
                            case "Company": props.Company = content; break;
                            case "Manager": props.Manager = content; break;
                            case "Template": props.Template = content; break;
                            case "HyperlinkBase": props.HyperlinkBase = content; break;
                        }
                }
            }
            catch (XmlException) { }
        }
    }

    /// <summary>
    /// Footnotes and endnotes (word/footnotes.xml, endnotes.xml): every normal note with its
    /// paragraphs and tables, by file id for the references in the body. Separators are not kept
    /// (they are written fresh).
    /// </summary>
    private static void LoadNotes(OoxmlPackage pkg, string documentUri, Document doc, LoadContext loadContext,
        DocxReaderOptions options, CancellationToken ct,
        System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> mediaCache)
    {
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal) continue;
            NoteKind kind;
            if (rel.Type.EndsWith("/footnotes", StringComparison.Ordinal)) kind = NoteKind.Footnote;
            else if (rel.Type.EndsWith("/endnotes", StringComparison.Ordinal)) kind = NoteKind.Endnote;
            else continue;
            var uri = Resolve(documentUri, rel.Target);
            var byId = kind == NoteKind.Footnote ? loadContext.Footnotes : loadContext.Endnotes;
            if (byId.Count > 0) continue; // one part per kind
            Stream? raw = null;
            try { raw = pkg.OpenPart(uri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
            if (raw is null) continue;
            var collection = kind == NoteKind.Footnote ? doc.Footnotes : doc.Endnotes;
            var images = new ImageSource(pkg, uri, options, ct, mediaCache);
            string element = kind == NoteKind.Footnote ? "footnote" : "endnote";
            try
            {
                ReadNotesPart(raw, uri, element, kind, collection, byId, loadContext, images, options, ct);
            }
            catch (XmlException)
            {
                // A malformed notes part loses its notes (references to them are dropped), not the document.
                foreach (var n in byId.Values) collection.Remove(n);
                byId.Clear();
            }
        }
    }

    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    /// <summary>
    /// Comments (word/comments.xml) with their content, and from commentsExtended.xml (Word 2013+)
    /// which are done and which reply to which. A malformed comments part loses its comments only.
    /// </summary>
    private static void LoadComments(OoxmlPackage pkg, string documentUri, Document doc, LoadContext loadContext,
        DocxReaderOptions options, CancellationToken ct,
        System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> mediaCache)
    {
        string? commentsUri = null, extendedUri = null;
        foreach (var rel in pkg.GetRelationships(documentUri))
        {
            if (rel.IsExternal) continue;
            if (rel.Type.EndsWith("/comments", StringComparison.Ordinal)) commentsUri ??= Resolve(documentUri, rel.Target);
            else if (rel.Type.EndsWith("/commentsExtended", StringComparison.Ordinal)) extendedUri ??= Resolve(documentUri, rel.Target);
        }
        if (commentsUri is null) return;
        Stream? raw = null;
        try { raw = pkg.OpenPart(commentsUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
        if (raw is null) return;
        var byParaId = new System.Collections.Generic.Dictionary<string, Comment>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var images = new ImageSource(pkg, commentsUri, options, ct, mediaCache);
            using var gs = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, commentsUri);
            using var r = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, commentsUri);
            if (!r.Read()) return;
            r.MoveToContent();
            int depth = r.Depth + 1;
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.Depth != depth || r.NamespaceURI != W || r.LocalName != "comment") continue;
                if (!int.TryParse(r.GetAttribute("id", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || loadContext.Comments.ContainsKey(id)
                    || loadContext.Comments.Count >= 65_536)
                {
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    continue;
                }
                var mark = RunProperties.ReadRevisionMark(r, RevisionType.Insert); // author and date
                var c = new Comment(mark.Author) { Date = mark.Date, Initials = r.GetAttribute("initials", W), LoadedId = id };
                loadContext.Comments[id] = c;
                doc.Comments.AddLoaded(c);
                if (r.IsEmptyElement) continue;
                var story = new StoryContext(loadContext, images) { NoNoteReferences = true };
                using (var n = r.ReadSubtree())
                {
                    n.Read();
                    int childDepth = n.Depth + 1;
                    bool more = n.Read();
                    while (more)
                    {
                        if (n.NodeType == XmlNodeType.Element && n.Depth == childDepth && n.NamespaceURI == W)
                        {
                            if (n.LocalName == "p")
                            {
                                var paraId = n.GetAttribute("paraId", W14);
                                if (paraId is not null) c.LoadedParaId = paraId; // the last paragraph's id names the comment
                                using (var sub = n.ReadSubtree()) c.Paragraphs.Add(LoadParagraph(sub, story));
                                more = n.Read();
                                continue;
                            }
                            if (n.LocalName == "tbl")
                            {
                                using (var sub = n.ReadSubtree()) c.AppendTable(LoadTable(sub, story));
                                more = n.Read();
                                continue;
                            }
                        }
                        more = n.Read();
                    }
                }
                story.Finish();
                if (c.LoadedParaId is { } pid) byParaId[pid] = c;
            }
            EnsureWellFormed(r, commentsUri);
        }
        catch (XmlException)
        {
            // A malformed comments part: no comment is kept (their anchors in the text are dropped with them).
            doc.Comments.Clear();
            loadContext.Comments.Clear();
            return;
        }
        if (extendedUri is null || byParaId.Count == 0) return;
        Stream? ext = null;
        try { ext = pkg.OpenPart(extendedUri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
        if (ext is null) return;
        try
        {
            using var gs = GuardedPartStream.Wrap(ext, options.MaxPartBytes, ct, extendedUri);
            using var r = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, extendedUri);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != W15 || r.LocalName != "commentEx") continue;
                if (r.GetAttribute("paraId", W15) is not { } pid || !byParaId.TryGetValue(pid, out var c)) continue;
                c.Done = r.GetAttribute("done", W15) is "1" or "true";
                if (r.GetAttribute("paraIdParent", W15) is { } parent && byParaId.TryGetValue(parent, out var pc) && !ReferenceEquals(pc, c))
                {
                    // Guard against cycles: a parent chain must end.
                    var walk = pc;
                    int guard = 0;
                    while (walk is not null && !ReferenceEquals(walk, c) && guard++ < 1000) walk = walk.Parent;
                    if (walk is null) c.Parent = pc;
                }
            }
        }
        catch (XmlException) { }
    }

    private static void ReadNotesPart(Stream raw, string uri, string element, NoteKind kind, NoteCollection collection,
        System.Collections.Generic.Dictionary<int, Note> byId, LoadContext loadContext, ImageSource images,
        DocxReaderOptions options, CancellationToken ct)
    {
        {
            using var gs = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, uri);
            using var r = GuardedXmlReader.Create(gs, options.MaxXmlDepth, ct, uri);
            if (!r.Read()) return;
            r.MoveToContent();
            int noteDepth = r.Depth + 1;
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.Depth != noteDepth || r.NamespaceURI != W || r.LocalName != element) continue;
                var type = r.GetAttribute("type", W);
                bool normal = type is null or "normal";
                if (!normal || !int.TryParse(r.GetAttribute("id", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || byId.ContainsKey(id)
                    || byId.Count >= 65_536)
                {
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    continue;
                }
                var note = new Note(kind) { LoadedId = id };
                byId[id] = note;
                collection.AddLoaded(note);
                if (r.IsEmptyElement) continue;
                var story = new StoryContext(loadContext, images) { NoNoteReferences = true };
                using (var n = r.ReadSubtree())
                {
                    n.Read();
                    int childDepth = n.Depth + 1;
                    bool more = n.Read();
                    while (more)
                    {
                        if (n.NodeType == XmlNodeType.Element && n.Depth == childDepth && n.NamespaceURI == W)
                        {
                            if (n.LocalName == "p")
                            {
                                using (var sub = n.ReadSubtree()) note.Paragraphs.Add(LoadParagraph(sub, story));
                                more = n.Read();
                                continue;
                            }
                            if (n.LocalName == "tbl")
                            {
                                using (var sub = n.ReadSubtree()) note.AppendTable(LoadTable(sub, story));
                                more = n.Read();
                                continue;
                            }
                            if (n.LocalName is "bookmarkStart" or "bookmarkEnd" or "commentRangeStart" or "commentRangeEnd" && BookmarkMarker(n, story) is { } marker)
                                story.Pending.Add(marker);
                        }
                        more = n.Read();
                    }
                }
                story.Finish();
            }
            EnsureWellFormed(r, uri);
        }
    }

    private static (HeaderFooterContent Content, TextWatermark? Watermark) LoadHeaderFooter(OoxmlPackage pkg, string uri,
        DocxReaderOptions options, CancellationToken ct,
        System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> mediaCache, LoadContext loadContext)
    {
        var content = new HeaderFooterContent();
        Stream? raw = null;
        try { raw = pkg.OpenPart(uri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
        if (raw is null) return (content, null);
        byte[] bytes;
        using (var gs = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, uri))
        using (var ms = new MemoryStream())
        {
            gs.CopyTo(ms);
            bytes = ms.ToArray();
        }
        TextWatermark? watermark;
        using (var wr = GuardedXmlReader.Create(new MemoryStream(bytes, false), options.MaxXmlDepth, ct, uri))
        {
            watermark = FindWatermark(wr);
            EnsureWellFormed(wr, uri);
        }
        var images = new ImageSource(pkg, uri, options, ct, mediaCache);
        var story = new StoryContext(loadContext, images) { NoNoteReferences = true };
        using var r = GuardedXmlReader.Create(new MemoryStream(bytes, false), options.MaxXmlDepth, ct, uri);
        if (!r.Read()) return (content, watermark);
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.ReadSubtree()) table = LoadTable(sub, story);
                    r.Read();
                    content.AppendTable(table);
                    continue;
                }
                if (r.LocalName is "bookmarkStart" or "bookmarkEnd" or "commentRangeStart" or "commentRangeEnd")
                {
                    if (BookmarkMarker(r, story) is { } marker) story.Pending.Add(marker);
                    if (!r.Read()) break;
                    continue;
                }
                if (r.LocalName == "p")
                {
                    Paragraph p;
                    using (var sub = r.ReadSubtree()) p = LoadParagraph(sub, story);
                    r.Read();
                    content.Add(p);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        EnsureWellFormed(r, uri);
        story.Finish();
        return (content, watermark);
    }

    /// <summary>The text watermark Word writes in a header: a VML shape whose id starts with PowerPlusWaterMarkObject.</summary>
    private static TextWatermark? FindWatermark(XmlReader r)
    {
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != DocumentSerializer.Vml || r.LocalName != "shape") continue;
            var id = r.GetAttribute("id");
            if (id is null || !id.StartsWith("PowerPlusWaterMarkObject", StringComparison.Ordinal)) continue;
            var style = r.GetAttribute("style") ?? "";
            var fill = r.GetAttribute("fillcolor");
            string? text = null, font = null;
            double opacity = 1;
            using (var sub = r.ReadSubtree())
            {
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != DocumentSerializer.Vml) continue;
                    if (sub.LocalName == "textpath")
                    {
                        text = sub.GetAttribute("string");
                        font = CssValue(sub.GetAttribute("style") ?? "", "font-family")?.Trim('"', '\'', ' ');
                    }
                    else if (sub.LocalName == "fill" && double.TryParse(sub.GetAttribute("opacity"), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var o) && double.IsFinite(o))
                        opacity = Math.Clamp(o, 0, 1);
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return null;
            var wm = new TextWatermark(text) { Opacity = opacity };
            if (!string.IsNullOrWhiteSpace(font)) wm.Font = font;
            wm.ColorHex = NamedColor(fill) ?? wm.ColorHex;
            var rotation = CssValue(style, "rotation");
            wm.Diagonal = rotation is not null && rotation.Trim() is not ("0" or "");
            wm.WidthPt = CssPoints(CssValue(style, "width"));
            wm.HeightPt = CssPoints(CssValue(style, "height"));
            return wm;
        }
        return null;
    }

    private static string? CssValue(string style, string name)
    {
        foreach (var part in style.Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon > 0 && part[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return part[(colon + 1)..].Trim();
        }
        return null;
    }

    private static double? CssPoints(string? v)
    {
        if (v is null || !v.EndsWith("pt", StringComparison.Ordinal)) return null;
        return double.TryParse(v[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            && double.IsFinite(d) && d > 0 && d < 10_000 ? d : null;
    }

    /// <summary>VML colour: #RRGGBB, #RGB or a few CSS names Word uses (silver, gray, black, red…).</summary>
    private static string? NamedColor(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        var space = v.IndexOf(' ');
        if (space > 0) v = v[..space]; // "#ff0000 [3204]"
        if (v.StartsWith('#'))
        {
            var hex = v[1..];
            if (hex.Length == 3) hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            return hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _)
                ? hex.ToUpperInvariant() : null;
        }
        return v.ToLowerInvariant() switch
        {
            "silver" => "C0C0C0", "gray" or "grey" => "808080", "black" => "000000", "white" => "FFFFFF", "red" => "FF0000",
            "maroon" => "800000", "blue" => "0000FF", "navy" => "000080", "green" => "008000", "lime" => "00FF00",
            "yellow" => "FFFF00", "olive" => "808000", "purple" => "800080", "teal" => "008080", "aqua" => "00FFFF", "fuchsia" => "FF00FF",
            _ => null,
        };
    }

    /// <summary>
    /// A well-formedness error met while a nested subtree reader is disposed leaves the reader in
    /// the Error state without throwing; report it like any other malformed part.
    /// </summary>
    private static void EnsureWellFormed(XmlReader r, string partUri)
    {
        if (r.ReadState == ReadState.Error)
            throw new XmlException($"Part '{partUri}' is not well-formed XML.");
    }

    /// <summary>Links the numIds read from w:numPr (paragraphs and styles) to the loaded list instances.</summary>
    private static void LinkNumbering(Document doc)
    {
        foreach (var p in DocumentSerializer.AllParagraphs(doc))
        {
            if (p.FormatChange is { OldNumbering: (int oldId and not 0, _) } pfc && doc.Numbering.TryGetList(oldId, out var oldList))
                pfc.OldList = oldList;
            if (p.LoadedNumId is not int id)
            {
                // w:numPr with only w:ilvl: the level for numbering that comes from the style.
                if (p.LoadedListLevelExplicit) { p.StyleListLevel = p.ListLevel; p.ListLevel = 0; }
                p.LoadedListLevelExplicit = false;
                continue;
            }
            p.LoadedNumId = null;
            p.LoadedListLevelExplicit = false;
            if (id == 0) p.NumberingSuppressed = true;
            else if (doc.Numbering.TryGetList(id, out var list)) p.ListInstance = list;
            // An unknown numId numbers nothing in Word either; it is dropped.
        }
        foreach (var s in doc.Styles)
        {
            if (s.LoadedNumId is not int id) continue;
            s.LoadedNumId = null;
            if (id == 0) s.NumberingSuppressed = true;
            else if (doc.Numbering.TryGetList(id, out var list)) s.ListInstance = list;
            // An unknown numId is dropped; a stated level is kept (it still selects the level of inherited numbering).
        }
    }

    // Body-level check: with ReadSubtree-based table skipping above, any w:p we reach here
    // is outside tables already; headers/footers are separate parts and never traversed.
    private static bool IsBodyLevel(XmlReader r) => true;

    /// <summary>Document-wide load state: open bookmarks by file id, notes by file id.</summary>
    internal sealed class LoadContext
    {
        public readonly System.Collections.Generic.Dictionary<int, Note> Footnotes = new();
        public readonly System.Collections.Generic.Dictionary<int, Note> Endnotes = new();
        public readonly System.Collections.Generic.Dictionary<int, Comment> Comments = new();
    }

    /// <summary>One story (the body, a header/footer, a note): fields may span its paragraphs.</summary>
    internal sealed class StoryContext
    {
        public StoryContext(LoadContext doc, ImageSource? images) { Doc = doc; Images = images; }
        public LoadContext Doc { get; }
        public ImageSource? Images { get; }
        /// <summary>A note, header or footer: note references inside it are dropped (Word does not allow them).</summary>
        public bool NoNoteReferences { get; init; }
        /// <summary>Open bookmarks of this story by file id (a repeated id pairs with its latest start).</summary>
        public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Bookmark>> OpenBookmarks = new(StringComparer.Ordinal);
        /// <summary>Field begins ignored because the stack is at its cap (their ends are ignored too).</summary>
        public int IgnoredFields;
        /// <summary>The paragraph has only marker runs so far (a page break then becomes PageBreakBefore); runs checked up to <see cref="CheckedRuns"/>.</summary>
        public bool OnlyMarkers;
        public int CheckedRuns;

        public bool OnlyMarkersSoFar(Paragraph p)
        {
            for (; CheckedRuns < p.Runs.Count; CheckedRuns++)
                if (!p.Runs[CheckedRuns].IsMarker) OnlyMarkers = false;
            return OnlyMarkers;
        }
        public readonly System.Collections.Generic.List<FieldFrame> Fields = new();
        /// <summary>Bookmark markers met between paragraphs (body or table level), placed at the next paragraph's start.</summary>
        public readonly System.Collections.Generic.List<Run> Pending = new();
        public Paragraph? Last;

        /// <summary>Markers still pending at the story's end go to its last paragraph.</summary>
        public void Finish()
        {
            if (Pending.Count == 0 || Last is null) return;
            foreach (var m in Pending) Last.AddRun(m);
            Pending.Clear();
        }
    }

    /// <summary>One open field (w:fldChar begin … end).</summary>
    internal sealed class FieldFrame
    {
        public Field Field = new("");
        /// <summary>The instruction so far: strings and nested fields.</summary>
        public readonly System.Collections.Generic.List<object> Parts = new();
        public string InstructionText
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                foreach (var part in Parts) sb.Append(part is Field f ? f.CachedResult ?? "" : (string)part);
                return sb.ToString();
            }
        }
        public void AddInstruction(string text)
        {
            if (Parts.Count > 0 && Parts[^1] is string last) Parts[^1] = last + text;
            else if (Parts.Count < 256) Parts.Add(text);
        }
        public void ApplyInstruction()
        {
            if (Parts.Exists(x => x is Field)) Field.SetInstructionParts(Parts);
            else Field.Instruction = InstructionText;
        }
        public readonly System.Text.StringBuilder Result = new();
        public bool Separated;
        /// <summary>A field inside another field's instruction: kept as a nested part of that instruction.</summary>
        public bool InInstruction;
        /// <summary>A PAGE / NUMPAGES / SECTIONPAGES field: its result is captured into one field run.</summary>
        public bool PageCapture;
        /// <summary>Capture already emitted (the field continued past its paragraph).</summary>
        public bool Done;
        public TextFormat BeginFormat = TextFormat.None;
        public string? BeginStyle;
        public TextFormat? ResultFormat;
        public string? ResultStyle;
    }

    /// <summary>The link a run sits in.</summary>
    private sealed record LinkInfo(string? Url, string? Anchor, string? Tooltip);

    /// <summary>
    /// Loads one <c>w:p</c> subtree: paragraph properties from <c>w:pPr</c>, then every run
    /// (<c>w:r</c>) wherever it sits — directly, or inside hyperlinks, insertions, smart tags,
    /// simple fields or content controls — with its full run formatting, bookmarks, fields and
    /// note references.
    /// </summary>
    internal static Paragraph LoadParagraph(XmlReader sub, StoryContext ctx)
    {
        var p = new Paragraph();
        foreach (var m in ctx.Pending) p.AddRun(m);
        ctx.Pending.Clear();
        ctx.Last = p;
        ctx.OnlyMarkers = true;
        ctx.CheckedRuns = 0;
        if (!sub.Read()) return p; // positioned on <w:p>
        LoadParagraphContent(sub, p, ctx, link: null);
        // A page field still open at the paragraph end (malformed, or ending in a later paragraph):
        // keep what was captured instead of losing it.
        foreach (var f in ctx.Fields)
            if (f.PageCapture && !f.Done)
            {
                if (f.Result.Length > 0) p.AddRun(Run.Field(f.InstructionText, f.Result.ToString(), f.ResultFormat, f.ResultStyle));
                f.Done = true;
            }
        // A field whose instruction never reached its separator cannot run on into the next paragraph
        // (malformed; Word repairs it): drop it, and everything opened inside it, so the text that follows shows.
        int unseparated = ctx.Fields.FindIndex(f => !f.Separated);
        if (unseparated >= 0)
        {
            ctx.Fields.RemoveRange(unseparated, ctx.Fields.Count - unseparated);
            ctx.IgnoredFields = 0;
        }
        return p;
    }

    /// <summary>A bookmark marker from a w:bookmarkStart / w:bookmarkEnd (reader on it), or null.</summary>
    internal static Run? BookmarkMarker(XmlReader r, StoryContext story)
    {
        var open = story.OpenBookmarks;
        var id = r.GetAttribute("id", W);
        if (id is null) return null;
        if (r.LocalName is "commentRangeStart" or "commentRangeEnd")
        {
            // Comment ranges: by the comment's id in comments.xml (unknown comments are dropped).
            return int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid) && story.Doc.Comments.TryGetValue(cid, out var comment)
                ? Run.CommentRangeRun(comment, r.LocalName == "commentRangeStart") : null;
        }
        if (r.LocalName == "bookmarkStart")
        {
            var name = r.GetAttribute("name", W);
            if (string.IsNullOrEmpty(name)) return null;
            var b = Bookmark.Loaded(name);
            if (int.TryParse(r.GetAttribute("colFirst", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cf) && cf >= 0) b.ColumnFirst = cf;
            if (int.TryParse(r.GetAttribute("colLast", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cl) && cl >= 0) b.ColumnLast = cl;
            if (!open.TryGetValue(id, out var list)) open[id] = list = new System.Collections.Generic.List<Bookmark>();
            if (list.Count < 1024) list.Add(b);
            return Run.BookmarkRun(b, true);
        }
        if (!open.TryGetValue(id, out var starts) || starts.Count == 0) return null;
        var started = starts[^1];
        starts.RemoveAt(starts.Count - 1);
        if (starts.Count == 0) open.Remove(id);
        return Run.BookmarkRun(started, false);
    }

    /// <summary>Reads the descendants of the element the reader is on (a w:p, or a w:hyperlink / w:fldSimple inside one).</summary>
    private static void LoadParagraphContent(XmlReader sub, Paragraph p, StoryContext ctx, LinkInfo? link, RevisionMark? revision = null,
        RevisionMark? outerInsertion = null)
    {
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
                case "ins":
                case "moveTo":
                case "del":
                case "moveFrom":
                {
                    // Tracked insertions and deletions (moves are kept as insertions / deletions).
                    var mark = RunProperties.ReadRevisionMark(sub, sub.LocalName is "ins" or "moveTo" ? RevisionType.Insert : RevisionType.Delete);
                    mark.IsMove = sub.LocalName is "moveTo" or "moveFrom";
                    if (sub.IsEmptyElement) break;
                    // A deletion inside an insertion (one reviewer inserted, another deleted) keeps both.
                    var insertion = mark.Type == RevisionType.Delete
                        ? (revision is { Type: RevisionType.Insert } ? revision : outerInsertion)
                        : null;
                    using (var rv = sub.ReadSubtree())
                    {
                        rv.Read();
                        LoadParagraphContent(rv, p, ctx, link, mark, insertion);
                    }
                    break;
                }
                case "pPr":
                    if (link is null)
                        using (var pPr = sub.ReadSubtree()) ParagraphProperties.Read(pPr, p);
                    else using (sub.ReadSubtree()) { }
                    break;
                case "bookmarkStart":
                case "bookmarkEnd":
                case "commentRangeStart":
                case "commentRangeEnd":
                    if (BookmarkMarker(sub, ctx) is { } marker) p.AddRun(marker);
                    break;
                case "hyperlink":
                {
                    // An external link (r:id → the part's hyperlink relationship; a w:anchor with it is
                    // appended as a fragment) or an internal one (w:anchor only: a bookmark name).
                    string? url = link?.Url;
                    string? anchorOnly = null;
                    var anchor = sub.GetAttribute("anchor", W);
                    var relId = sub.GetAttribute("id", DocumentSerializer.R);
                    if (relId is not null && ctx.Images?.GetHyperlink(relId) is { } target)
                        url = string.IsNullOrEmpty(anchor) || target.Contains('#') ? target : target + "#" + anchor;
                    else if (!string.IsNullOrEmpty(anchor) && relId is null)
                    {
                        url = null;
                        anchorOnly = anchor;
                    }
                    var tooltip = sub.GetAttribute("tooltip", W);
                    if (sub.IsEmptyElement) break;
                    var inner = url is null && anchorOnly is null ? link : new LinkInfo(url, anchorOnly, tooltip);
                    using (var h = sub.ReadSubtree())
                    {
                        h.Read();
                        LoadParagraphContent(h, p, ctx, inner, revision, outerInsertion);
                    }
                    break;
                }
                case "r":
                    using (var r = sub.ReadSubtree()) LoadRun(r, p, ctx, link, revision, outerInsertion);
                    break;
                case "fldSimple":
                {
                    var instr = sub.GetAttribute("instr", W);
                    if (IsPageField(instr))
                    {
                        var result = new System.Text.StringBuilder();
                        TextFormat? fieldFormat = null;
                        string? fieldStyle = null;
                        RevisionMark? fieldRevision = revision;
                        using (var fs = sub.ReadSubtree())
                        {
                            bool adv = true;
                            while (true)
                            {
                                if (adv && !fs.Read()) break;
                                adv = true;
                                if (fs.EOF) break;
                                if (fs.NodeType != XmlNodeType.Element || fs.NamespaceURI != W) continue;
                                if (fs.LocalName == "rPr" && fieldFormat is null)
                                    using (var rp = fs.ReadSubtree()) fieldFormat = RunProperties.Read(rp, out fieldStyle);
                                else if (fs.LocalName is "t" or "delText") { result.Append(fs.ReadElementContentAsString()); adv = false; }
                                else if (fs.LocalName is "ins" or "del" or "moveTo" or "moveFrom" && fs.Depth > 0)
                                {
                                    // A tracked change around the field's result run (inside w:fldSimple).
                                    fieldRevision = RunProperties.ReadRevisionMark(fs, fs.LocalName is "ins" or "moveTo" ? RevisionType.Insert : RevisionType.Delete);
                                    fieldRevision.IsMove = fs.LocalName is "moveTo" or "moveFrom";
                                }
                            }
                        }
                        var fieldRun = Run.Field(instr!, result.ToString(), fieldFormat, fieldStyle);
                        p.AddRun(fieldRevision is null ? fieldRun : fieldRun.WithRevision(fieldRevision, null));
                        break;
                    }
                    // Any other simple field: markers around its result runs, written back as w:fldSimple.
                    var field = new Field(instr ?? "")
                    {
                        Simple = true,
                        Dirty = RunProperties.OnOff(sub, "dirty") == true,
                        Locked = RunProperties.OnOff(sub, "fldLock") == true,
                    };
                    p.AddRun(Run.FieldMarkRun(field, FieldMarkType.Begin));
                    if (!sub.IsEmptyElement)
                        using (var fs = sub.ReadSubtree())
                        {
                            fs.Read();
                            LoadParagraphContent(fs, p, ctx, link, revision, outerInsertion);
                        }
                    p.AddRun(Run.FieldMarkRun(field, FieldMarkType.End));
                    break;
                }
            }
        }
    }

    /// <summary>Main-part content types of a Word package: document, template, and their macro-enabled forms.</summary>
    internal static readonly string[] WordMainContentTypes =
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml",
        "application/vnd.ms-word.document.macroEnabled.main+xml",
        "application/vnd.ms-word.template.macroEnabledTemplate.main+xml",
    };

    /// <summary>
    /// Rejects a package whose main part is not a Word document (a workbook or presentation renamed
    /// .docx, or a part with a non-Word content type) with <see cref="DocxFormatException"/>.
    /// </summary>
    internal static void CheckMainPart(OoxmlPackage pkg, string documentUri)
    {
        var part = pkg.GetPart(documentUri)
            ?? throw new DocxFormatException($"The main document part '{documentUri}' is missing from the package.");
        var type = part.ContentType ?? "";
        int semi = type.IndexOf(';');
        if (semi >= 0) type = type[..semi];
        type = type.Trim();
        foreach (var accepted in WordMainContentTypes)
            if (string.Equals(type, accepted, StringComparison.OrdinalIgnoreCase)) return;
        throw new DocxFormatException($"The main part '{documentUri}' has content type '{part.ContentType}', not a Word document.");
    }

    private static void ReadFallbackOffsets(XmlReader fb, FloatingPosition target)
    {
        while (fb.Read())
        {
            if (fb.NodeType != XmlNodeType.Element || fb.NamespaceURI != DocumentSerializer.WP || fb.LocalName != "anchor") continue;
            var abs = DrawingXml.Read(fb).Position;
            if (abs is null) return;
            if (target.HorizontalOffsetPercent is not null) target.HorizontalOffsetPt = abs.HorizontalOffsetPt;
            if (target.VerticalOffsetPercent is not null) target.VerticalOffsetPt = abs.VerticalOffsetPt;
            return;
        }
    }

    /// <summary>Page-number fields the model represents as field runs (PAGE, NUMPAGES, SECTIONPAGES); other fields load as their result text.</summary>
    private static bool IsPageField(string? instruction)
    {
        if (instruction is null) return false;
        var word = instruction.Trim().Split(' ', 2)[0].ToUpperInvariant();
        return word is "PAGE" or "NUMPAGES" or "SECTIONPAGES";
    }

    /// <summary>The paragraphs and tables of a text box (w:txbxContent, reader on it); a story of its own for fields.</summary>
    private static void LoadTextBoxContent(XmlReader r, TextBox box, StoryContext outer)
    {
        if (!r.Read()) return;
        var story = new StoryContext(outer.Doc, outer.Images);
        int childDepth = r.Depth + 1;
        bool more = r.Read();
        while (more)
        {
            if (r.NodeType == XmlNodeType.Element && r.Depth == childDepth && r.NamespaceURI == W)
            {
                if (r.LocalName == "p")
                {
                    using (var sub = r.ReadSubtree()) box.Paragraphs.Add(LoadParagraph(sub, story));
                    more = r.Read();
                    continue;
                }
                if (r.LocalName == "tbl")
                {
                    using (var sub = r.ReadSubtree()) box.AppendTable(LoadTable(sub, story));
                    more = r.Read();
                    continue;
                }
                if (r.LocalName is "bookmarkStart" or "bookmarkEnd" or "commentRangeStart" or "commentRangeEnd" && BookmarkMarker(r, story) is { } marker) story.Pending.Add(marker);
            }
            more = r.Read();
        }
        story.Finish();
    }

    /// <summary>Where run text goes while fields are open.</summary>
    private enum TextSink { Normal, Capture, Instruction, Drop }

    private static TextSink SinkOf(StoryContext ctx, out FieldFrame? frame)
    {
        frame = null;
        if (ctx.Fields.Count == 0) return TextSink.Normal;
        var top = ctx.Fields[^1];
        if (top.InInstruction)
        {
            if (!top.Separated) return TextSink.Drop;
            frame = top; // a nested instruction field's cached result
            return TextSink.Instruction;
        }
        if (!top.Separated) return TextSink.Drop; // between begin and separate: only w:instrText counts
        if (top.PageCapture && !top.Done) { frame = top; return TextSink.Capture; }
        return TextSink.Normal;
    }

    /// <summary>
    /// Loads one <c>w:r</c>. Text, tabs and plain line breaks inside one run become ONE
    /// <see cref="Run"/> whose text carries <c>\t</c> and <c>\n</c> — exactly what the writer
    /// turns back into the same <c>w:t</c>/<c>w:tab</c>/<c>w:br</c> sequence, so a run
    /// round-trips as a single run. Page, column and text-wrapping breaks become explicit
    /// <see cref="BreakType"/> runs; field characters, note references and symbols become their
    /// own runs.
    /// </summary>
    private static void LoadRun(XmlReader sub, Paragraph p, StoryContext ctx, LinkInfo? link, RevisionMark? revision = null,
        RevisionMark? outerInsertion = null)
    {
        int firstRun = p.Runs.Count;
        RunFormatChange? formatChange = null;
        LoadRunCore(sub, p, ctx, link, ref formatChange);
        // A tracked insertion / deletion and a tracked formatting change apply to every run read from this w:r.
        if (revision is not null || formatChange is not null)
            for (int i = firstRun; i < p.Runs.Count; i++)
            {
                var run = p.Runs[i].WithRevision(revision ?? p.Runs[i].Revision, formatChange ?? p.Runs[i].FormatChange);
                if (outerInsertion is not null && revision is { Type: RevisionType.Delete }) run = run.WithInsertedDeletion(revision, outerInsertion);
                p.ReplaceRun(i, run);
            }
    }

    private static void LoadRunCore(XmlReader sub, Paragraph p, StoryContext ctx, LinkInfo? link, ref RunFormatChange? formatChange)
    {
        var images = ctx.Images;
        var format = TextFormat.None;
        string? charStyle = null;
        var text = new System.Text.StringBuilder();
        if (!sub.Read()) return; // positioned on <w:r>

        bool sawText = false; // an empty <w:t/> is still a run (kept for exact round-trip)
        var acTaken = new System.Collections.Generic.Dictionary<int, bool>();
        void Flush()
        {
            if (text.Length == 0 && !sawText) return;
            // A loaded link keeps exactly the run's own style (Word adds no Hyperlink style by itself).
            p.AddRun(link is null
                ? new Run(text.ToString(), format) { CharacterStyleId = charStyle }
                : new Run(text.ToString(), format)
                {
                    CharacterStyleId = charStyle, HyperlinkUrl = link.Url, HyperlinkAnchor = link.Anchor,
                    HyperlinkTooltip = link.Tooltip, HyperlinkStyleImplied = false,
                });
            text.Clear();
            sawText = false;
        }
        bool NormalText() => SinkOf(ctx, out _) == TextSink.Normal;

        // ReadElementContentAsString leaves the cursor on the NEXT node, so the loop only
        // advances when the previous step did not already do so.
        bool advance = true;
        while (true)
        {
            if (advance && !sub.Read()) break;
            advance = true;
            if (sub.EOF) break;
            if (sub.NodeType != XmlNodeType.Element) continue;

            // mc:AlternateContent (markup compatibility): the first Choice whose Requires we understand,
            // else the Fallback — never both (they carry the same content in two forms).
            if (sub.NamespaceURI == Mc && sub.LocalName == "AlternateContent")
            {
                acTaken[sub.Depth] = false;
                continue;
            }
            if (sub.NamespaceURI == Mc && sub.LocalName is "Choice" or "Fallback")
            {
                int parent = sub.Depth - 1;
                bool taken = acTaken.TryGetValue(parent, out var t) && t;
                bool use = !taken && (sub.LocalName == "Fallback" || UnderstoodRequires(sub.GetAttribute("Requires")));
                if (!use)
                {
                    // A skipped Fallback of a percentage-positioned picture still carries the absolute
                    // position older readers use: keep it as the picture's point offsets.
                    if (sub.LocalName == "Fallback" && p.Runs.Count > 0 && p.Runs[^1].Image?.Position is { } pp
                        && (pp.HorizontalOffsetPercent is not null || pp.VerticalOffsetPercent is not null))
                        using (var fb = sub.ReadSubtree()) ReadFallbackOffsets(fb, pp);
                    else using (sub.ReadSubtree()) { }
                    continue;
                }
                acTaken[parent] = true;
                continue;
            }

            if (images is not null && sub.LocalName == "drawing" && sub.NamespaceURI == W && NormalText())
            {
                // A picture becomes an image run in place (inline or floating, with its anchor
                // properties); other drawings keep their text, flattened.
                Flush();
                var loose = new System.Text.StringBuilder(); // text outside wp:inline / wp:anchor (non-standard drawings)
                using (var d = sub.ReadSubtree())
                {
                    d.Read();
                    int depth = d.Depth + 1;
                    bool more = d.Read();
                    while (more)
                    {
                        if (IsAlternateRepresentation(d)) { d.Skip(); more = !d.EOF; continue; }
                        if (d.NodeType == XmlNodeType.Element && d.NamespaceURI == W && d.LocalName == "t")
                        {
                            loose.Append(d.ReadElementContentAsString());
                            more = !d.EOF;
                            continue;
                        }
                        if (d.NodeType == XmlNodeType.Element && d.Depth == depth && d.NamespaceURI == DocumentSerializer.WP
                            && d.LocalName is "inline" or "anchor")
                        {
                            var pic = DrawingXml.Read(d, (tc, box) => LoadTextBoxContent(tc, box, ctx));
                            if (pic.TextBox is { } tb)
                            {
                                p.AddRun(new Run("", TextFormat.None) { TextBox = tb });
                                more = d.Read();
                                continue;
                            }
                            var media = pic.IsPicture && pic.EmbedRelId is { } er ? images.Get(er) : null;
                            var plink = pic.IsPicture && pic.LinkRelId is { } lr ? images.GetLink(lr) : null;
                            if (media is { } m)
                                p.AddRun(new Run("", TextFormat.None) { Image = pic.ToImage(m.Bytes, m.ContentType, plink) });
                            else if (plink is not null)
                                p.AddRun(new Run("", TextFormat.None) { Image = pic.ToImage(Array.Empty<byte>(), ImageMetadata.ContentTypeForExtension(Path.GetExtension(plink)) ?? "application/octet-stream", plink) });
                            else if (pic.Text.Length > 0)
                                p.AddRun(new Run(pic.Text, TextFormat.None));
                            more = d.Read();
                            continue;
                        }
                        more = d.Read();
                    }
                }
                if (loose.Length > 0) p.AddRun(new Run(loose.ToString(), TextFormat.None));
                continue;
            }
            if (sub.LocalName is "drawing" or "pict" or "object")
            {
                // Text boxes / shapes: keep their words, flattened (geometry is outside the model).
                Flush();
                if (NormalText()) AppendEmbeddedText(sub, p);
                else using (sub.ReadSubtree()) { }
                continue;
            }
            if (sub.NamespaceURI != W) continue;

            switch (sub.LocalName)
            {
                case "rPr":
                    // rPr belongs first in a run; if (invalidly) text came before it, that text
                    // keeps the formatting it was read with.
                    Flush();
                    using (var rPr = sub.ReadSubtree()) format = RunProperties.Read(rPr, out charStyle, out formatChange, out _);
                    break;
                case "t":
                case "delText":
                {
                    var value = sub.ReadElementContentAsString();
                    advance = false;
                    switch (SinkOf(ctx, out var frame))
                    {
                        case TextSink.Capture:
                            if (frame!.Result.Length == 0 && frame.ResultFormat is null) { frame.ResultFormat = format; frame.ResultStyle = charStyle; }
                            frame.Result.Append(value);
                            break;
                        case TextSink.Instruction:
                            if (frame!.Result.Length < 1_000_000) frame.Result.Append(value);
                            break;
                        case TextSink.Normal:
                            text.Append(value);
                            sawText = true;
                            break;
                    }
                    break;
                }
                case "instrText":
                case "delInstrText":
                {
                    var instr = sub.ReadElementContentAsString();
                    advance = false;
                    if (ctx.IgnoredFields == 0 && ctx.Fields.Count > 0 && ctx.Fields[^1] is { Separated: false } top)
                        top.AddInstruction(instr);
                    break;
                }
                case "fldChar":
                {
                    var kind = sub.GetAttribute("fldCharType", W);
                    Flush();
                    if (kind == "begin")
                    {
                        if (ctx.Fields.Count >= 64 || ctx.IgnoredFields > 0)
                        {
                            ctx.IgnoredFields++; // absurd nesting: the outer 64 fields are kept
                            if (!sub.IsEmptyElement) using (sub.ReadSubtree()) { }
                            break;
                        }
                        bool inInstruction = ctx.Fields.Count > 0 && (ctx.Fields[^1].InInstruction || !ctx.Fields[^1].Separated);
                        var frame = new FieldFrame
                        {
                            InInstruction = inInstruction,
                            BeginFormat = format,
                            BeginStyle = charStyle,
                            Field = new Field("")
                            {
                                Dirty = RunProperties.OnOff(sub, "dirty") == true,
                                Locked = RunProperties.OnOff(sub, "fldLock") == true,
                            },
                        };
                        ctx.Fields.Add(frame);
                        // Legacy form-field data (w:ffData) and w:fldData: carried verbatim.
                        if (!sub.IsEmptyElement)
                            using (var fc = sub.ReadSubtree())
                            {
                                fc.Read();
                                int dataDepth = fc.Depth + 1;
                                bool more = fc.Read();
                                while (more)
                                {
                                    if (fc.NodeType == XmlNodeType.Element && fc.Depth == dataDepth && frame.Field.RawData.Count < 4)
                                    {
                                        frame.Field.RawData.Add(fc.ReadOuterXml());
                                        more = !fc.EOF;
                                        continue;
                                    }
                                    more = fc.Read();
                                }
                            }
                    }
                    else if (kind == "separate" && ctx.Fields.Count > 0)
                    {
                        if (ctx.IgnoredFields > 0) break;
                        var top = ctx.Fields[^1];
                        if (top.Separated) break;
                        top.Separated = true;
                        if (top.InInstruction) break;
                        var instr = top.InstructionText;
                        if (IsPageField(instr) && !top.Parts.Exists(x => x is Field) && !ctx.Fields.Any(f => f != top && f.PageCapture)) { top.PageCapture = true; break; }
                        top.ApplyInstruction();
                        p.AddRun(Run.FieldMarkRun(top.Field, FieldMarkType.Begin, top.BeginFormat, top.BeginStyle));
                        p.AddRun(Run.FieldMarkRun(top.Field, FieldMarkType.Separate, format, charStyle));
                    }
                    else if (kind == "end" && ctx.Fields.Count > 0)
                    {
                        if (ctx.IgnoredFields > 0) { ctx.IgnoredFields--; break; }
                        var top = ctx.Fields[^1];
                        ctx.Fields.RemoveAt(ctx.Fields.Count - 1);
                        if (top.InInstruction)
                        {
                            // A nested instruction field: becomes a part of the enclosing field's instruction.
                            if (ctx.Fields.Count > 0)
                            {
                                var nested = new Field("") { CachedResult = top.Result.ToString(), Dirty = top.Field.Dirty, Locked = top.Field.Locked };
                                nested.RawData.AddRange(top.Field.RawData);
                                if (top.Parts.Exists(x => x is Field)) nested.SetInstructionParts(top.Parts);
                                else nested.Instruction = top.InstructionText;
                                var parent = ctx.Fields[^1];
                                if (parent.Parts.Count < 256) parent.Parts.Add(nested);
                            }
                            break;
                        }
                        var instr = top.InstructionText;
                        if (top.PageCapture)
                        {
                            if (!top.Done) p.AddRun(Run.Field(instr, top.Result.ToString(), top.ResultFormat, top.ResultStyle));
                        }
                        else if (!top.Separated)
                        {
                            if (IsPageField(instr) && !top.Parts.Exists(x => x is Field)) p.AddRun(Run.Field(instr, "", null, null));
                            else
                            {
                                top.ApplyInstruction();
                                p.AddRun(Run.FieldMarkRun(top.Field, FieldMarkType.Begin, top.BeginFormat, top.BeginStyle));
                                p.AddRun(Run.FieldMarkRun(top.Field, FieldMarkType.End, format, charStyle));
                            }
                        }
                        else p.AddRun(Run.FieldMarkRun(top.Field, FieldMarkType.End, format, charStyle));
                    }
                    break;
                }
                case "footnoteReference":
                case "endnoteReference":
                {
                    Flush();
                    if (!NormalText() || ctx.NoNoteReferences) break;
                    var notes = sub.LocalName == "footnoteReference" ? ctx.Doc.Footnotes : ctx.Doc.Endnotes;
                    if (int.TryParse(sub.GetAttribute("id", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nid)
                        && notes.TryGetValue(nid, out var note))
                        p.AddRun(Run.NoteReferenceRun(note, format, charStyle, RunProperties.OnOff(sub, "customMarkFollows") == true));
                    break;
                }
                case "commentReference":
                {
                    Flush();
                    if (NormalText() && int.TryParse(sub.GetAttribute("id", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
                        && ctx.Doc.Comments.TryGetValue(cid, out var comment))
                        p.AddRun(Run.CommentReferenceRun(comment, format, charStyle));
                    break;
                }
                case "annotationRef":
                    Flush();
                    if (NormalText()) p.AddRun(Run.AnnotationMarkRun(charStyle, format));
                    break;
                case "footnoteRef":
                case "endnoteRef":
                    Flush();
                    if (NormalText()) p.AddRun(Run.NoteMarkRun(charStyle, format));
                    break;
                case "sym":
                {
                    Flush();
                    var font = sub.GetAttribute("font", W);
                    var code = sub.GetAttribute("char", W);
                    if (NormalText() && !string.IsNullOrEmpty(font) && code is { Length: > 0 and <= 4 }
                        && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) && cp > 0)
                        p.AddRun(Run.SymbolRun(new SymbolChar(font, (char)cp), format, charStyle));
                    break;
                }
                case "tab":
                    if (NormalText()) text.Append('\t');
                    break;
                case "ptab":
                    Flush();
                    if (NormalText()) p.AddRun(Run.PositionalTabRun(ReadPositionalTab(sub), format, charStyle));
                    break;
                case "cr":
                    if (NormalText()) text.Append('\n');
                    break;
                case "noBreakHyphen":
                    if (NormalText()) text.Append('\u2011');
                    break;
                case "softHyphen":
                    if (NormalText()) text.Append('\u00AD');
                    break;
                case "br":
                    if (!NormalText()) break;
                    var type = sub.GetAttribute("type", W);
                    var clear = sub.GetAttribute("clear", W);
                    if (type is null && clear is null)
                    {
                        text.Append('\n');
                        break;
                    }
                    Flush();
                    if (type == "page" && !p.PageBreakBefore && ctx.OnlyMarkersSoFar(p))
                        p.PageBreakBefore = true; // the shape Document.AddPageBreak writes
                    else
                        p.AddRun(Run.BreakRun(ParseBreak(type, clear), format, charStyle));
                    break;
            }
        }
        Flush();
    }

    private static PositionalTab ReadPositionalTab(XmlReader r)
    {
        var alignment = r.GetAttribute("alignment", W) switch
        {
            "center" => PositionalTabAlignment.Center,
            "right" => PositionalTabAlignment.Right,
            _ => PositionalTabAlignment.Left,
        };
        var relativeTo = r.GetAttribute("relativeTo", W) == "indent" ? PositionalTabBase.Indent : PositionalTabBase.Margin;
        var leader = r.GetAttribute("leader", W) switch
        {
            "dot" => TabLeader.Dot,
            "hyphen" => TabLeader.Hyphen,
            "underscore" => TabLeader.Underscore,
            "middleDot" => TabLeader.MiddleDot,
            _ => TabLeader.None,
        };
        return new PositionalTab(alignment, relativeTo, leader);
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
    /// Markup-compatibility namespaces this loader understands, by Word's standard prefixes (a
    /// subtree reader cannot resolve prefixes declared on the document root).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> KnownMcPrefixes = new(StringComparer.Ordinal)
    {
        "wps", "wpg", "wpc", "wpi", "wp14", "w14", "w15", "w16", "w16se", "w16cid", "w16cex", "w16sdtdh", "a14", "v", "o", "w10", "wne", "m",
    };

    private static bool UnderstoodRequires(string? requires)
    {
        if (string.IsNullOrWhiteSpace(requires)) return false;
        foreach (var prefix in requires.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!KnownMcPrefixes.Contains(prefix)) return false;
        return true;
    }

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

    private static DocTable LoadTable(XmlReader sub, StoryContext story)
        => TableXml.Read(sub, s => LoadParagraph(s, story), m =>
        {
            if (BookmarkMarker(m, story) is { } marker) story.Pending.Add(marker);
        });

    /// <summary>The media a part's pictures refer to (r:embed → bytes and type), read once each, within the part-size limit.</summary>
    internal sealed class ImageSource
    {
        private readonly OoxmlPackage _pkg;
        private readonly DocxReaderOptions _options;
        private readonly CancellationToken _ct;
        private readonly System.Collections.Generic.Dictionary<string, string> _targets = new(StringComparer.Ordinal);
        private readonly System.Collections.Generic.Dictionary<string, string> _links = new(StringComparer.Ordinal);
        private readonly System.Collections.Generic.Dictionary<string, string> _hyperlinks = new(StringComparer.Ordinal);
        private readonly System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> _cache;

        /// <summary><paramref name="sharedCache"/> (by media part) lets every part reuse one byte array per media file.</summary>
        public ImageSource(OoxmlPackage pkg, string partUri, DocxReaderOptions options, CancellationToken ct,
            System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> sharedCache)
        {
            _cache = sharedCache;
            _pkg = pkg;
            _options = options;
            _ct = ct;
            foreach (var rel in pkg.GetRelationships(partUri))
            {
                if (rel.Type.EndsWith("/hyperlink", StringComparison.Ordinal))
                {
                    if (rel.IsExternal) _hyperlinks[rel.Id] = rel.Target;
                    continue;
                }
                if (!rel.Type.EndsWith("/image", StringComparison.Ordinal)) continue;
                if (rel.IsExternal) _links[rel.Id] = rel.Target;
                else _targets[rel.Id] = Resolve(partUri, rel.Target);
            }
        }

        /// <summary>The external target of a hyperlink relationship (w:hyperlink/@r:id), or null.</summary>
        public string? GetHyperlink(string relId) => _hyperlinks.TryGetValue(relId, out var t) ? t : null;

        /// <summary>The external target of a linked picture (r:link), or null.</summary>
        public string? GetLink(string relId) => _links.TryGetValue(relId, out var t) ? t : null;

        public (byte[] Bytes, string ContentType)? Get(string relId)
        {
            if (!_targets.TryGetValue(relId, out var uri)) return null;
            if (_cache.TryGetValue(uri, out var hit)) return hit;
            (byte[], string)? result = null;
            {
                Stream? raw = null;
                try { raw = _pkg.OpenPart(uri); } catch (FileNotFoundException) { } catch (InvalidDataException) { }
                if (raw is not null)
                {
                    using var gs = GuardedPartStream.Wrap(raw, _options.MaxPartBytes, _ct, uri);
                    using var ms = new MemoryStream();
                    gs.CopyTo(ms);
                    var bytes = ms.ToArray();
                    // An unrecognised format is kept as opaque bytes so the picture and its space survive.
                    var type = ImageMetadata.DetectContentType(bytes) ?? ImageMetadata.ContentTypeForExtension(Path.GetExtension(uri)) ?? "application/octet-stream";
                    if (bytes.Length > 0) result = (bytes, type);
                }
            }
            _cache[uri] = result;
            return result;
        }
    }

}
