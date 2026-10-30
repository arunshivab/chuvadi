using System;
using System.IO;
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

        (Section, System.Collections.Generic.List<SectionXml.Reference>)? bodySectPr = null;
        using var raw = pkg.OpenPart(documentUri);
        using var s = GuardedPartStream.Wrap(raw, options.MaxPartBytes, ct, documentUri);
        using var r = GuardedXmlReader.Create(s, options.MaxXmlDepth, ct, documentUri);
        var mediaCache = new System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?>(StringComparer.Ordinal);
        doc.LoadedMediaCache = mediaCache;
        var bodyImages = new ImageSource(pkg, documentUri, options, ct, mediaCache);

        if (!r.Read()) return doc;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.ReadSubtree()) table = LoadTable(sub, bodyImages);
                    r.Read();
                    doc.AddBlock(table);
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
                    using (var sub = r.ReadSubtree()) p = LoadParagraph(sub, bodyImages);
                    r.Read();
                    doc.AddBlock(p);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        EnsureWellFormed(r, documentUri);
        LoadSections(pkg, documentUri, doc, bodySectPr, options, ct);
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
        DocxReaderOptions options, CancellationToken ct)
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
                    try { part = LoadHeaderFooter(pkg, uri, options, ct, doc.LoadedMediaCache ?? new()); }
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

    private static (HeaderFooterContent Content, TextWatermark? Watermark) LoadHeaderFooter(OoxmlPackage pkg, string uri,
        DocxReaderOptions options, CancellationToken ct,
        System.Collections.Generic.Dictionary<string, (byte[] Bytes, string ContentType)?> mediaCache)
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
        using var r = GuardedXmlReader.Create(new MemoryStream(bytes, false), options.MaxXmlDepth, ct, uri);
        if (!r.Read()) return (content, watermark);
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "tbl")
                {
                    DocTable table;
                    using (var sub = r.ReadSubtree()) table = LoadTable(sub, images);
                    r.Read();
                    content.AppendTable(table);
                    continue;
                }
                if (r.LocalName == "p")
                {
                    Paragraph p;
                    using (var sub = r.ReadSubtree()) p = LoadParagraph(sub, images);
                    r.Read();
                    content.Add(p);
                    continue;
                }
            }
            if (!r.Read()) break;
        }
        EnsureWellFormed(r, uri);
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

    /// <summary>
    /// Loads one <c>w:p</c> subtree: paragraph properties from <c>w:pPr</c>, then every run
    /// (<c>w:r</c>) wherever it sits — directly, or inside hyperlinks, insertions, smart tags,
    /// simple fields or content controls — with its full run formatting.
    /// </summary>
    internal static Paragraph LoadParagraph(XmlReader sub) => LoadParagraph(sub, null);

    internal static Paragraph LoadParagraph(XmlReader sub, ImageSource? images)
    {
        var p = new Paragraph();
        if (!sub.Read()) return p; // positioned on <w:p>
        var fields = new FieldState();
        LoadParagraphContent(sub, p, fields, images, hyperlink: null);
        // A page field still open at the paragraph end (malformed, or ending in a later paragraph):
        // keep what was captured instead of losing it.
        if (fields.Capturing && fields.Result.Length > 0)
            p.AddRun(Run.Field(fields.Instruction.ToString(), fields.Result.ToString(), fields.Format, fields.StyleId));
        return p;
    }

    /// <summary>Reads the descendants of the element the reader is on (a w:p, or a w:hyperlink inside one).</summary>
    private static void LoadParagraphContent(XmlReader sub, Paragraph p, FieldState fields, ImageSource? images, string? hyperlink)
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
                case "moveFrom":
                case "del":
                    // Moved-away / deleted content: not part of the current text. (Revision
                    // reading is a later phase; skipping avoids showing moved text twice.)
                    using (sub.ReadSubtree()) { }
                    break;
                case "pPr":
                    if (hyperlink is null)
                        using (var pPr = sub.ReadSubtree()) ParagraphProperties.Read(pPr, p);
                    else using (sub.ReadSubtree()) { }
                    break;
                case "hyperlink":
                {
                    // An external link (r:id → the part's hyperlink relationship; w:anchor appended
                    // as a fragment). Internal links (w:anchor only) come with bookmarks.
                    string? url = hyperlink;
                    var relId = sub.GetAttribute("id", DocumentSerializer.R);
                    if (relId is not null && images?.GetHyperlink(relId) is { } target)
                    {
                        var anchor = sub.GetAttribute("anchor", W);
                        url = string.IsNullOrEmpty(anchor) || target.Contains('#') ? target : target + "#" + anchor;
                    }
                    if (sub.IsEmptyElement) break;
                    using (var h = sub.ReadSubtree())
                    {
                        h.Read();
                        LoadParagraphContent(h, p, fields, images, url);
                    }
                    break;
                }
                case "r":
                    using (var r = sub.ReadSubtree()) LoadRun(r, p, fields, images, hyperlink);
                    break;
                case "fldSimple":
                {
                    var instr = sub.GetAttribute("instr", W);
                    if (!IsPageField(instr)) break; // other fields: their result runs load as text
                    var result = new System.Text.StringBuilder();
                    TextFormat? fieldFormat = null;
                    string? fieldStyle = null;
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
                            else if (fs.LocalName == "t") { result.Append(fs.ReadElementContentAsString()); adv = false; }
                        }
                    }
                    p.AddRun(Run.Field(instr!, result.ToString(), fieldFormat, fieldStyle));
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

    /// <summary>Complex field (w:fldChar begin/separate/end + w:instrText) state across the runs of one paragraph.</summary>
    private sealed class FieldState
    {
        public int Depth;
        public bool Collecting;
        public bool Capturing;
        public readonly System.Text.StringBuilder Instruction = new();
        public readonly System.Text.StringBuilder Result = new();
        public TextFormat? Format;
        public string? StyleId;
    }

    /// <summary>
    /// Loads one <c>w:r</c>. Text, tabs and plain line breaks inside one run become ONE
    /// <see cref="Run"/> whose text carries <c>\t</c> and <c>\n</c> — exactly what the writer
    /// turns back into the same <c>w:t</c>/<c>w:tab</c>/<c>w:br</c> sequence, so a run
    /// round-trips as a single run. Page, column and text-wrapping breaks become explicit
    /// <see cref="BreakType"/> runs.
    /// </summary>
    private static void LoadRun(XmlReader sub, Paragraph p, FieldState fields, ImageSource? images, string? hyperlink)
    {
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
            p.AddRun(hyperlink is null
                ? new Run(text.ToString(), format) { CharacterStyleId = charStyle }
                : new Run(text.ToString(), format) { CharacterStyleId = charStyle, HyperlinkUrl = hyperlink, HyperlinkStyleImplied = false });
            text.Clear();
            sawText = false;
        }

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

            if (images is not null && sub.LocalName == "drawing" && sub.NamespaceURI == W)
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
                            var pic = DrawingXml.Read(d);
                            var media = pic.IsPicture && pic.EmbedRelId is { } er ? images.Get(er) : null;
                            var link = pic.IsPicture && pic.LinkRelId is { } lr ? images.GetLink(lr) : null;
                            if (media is { } m)
                                p.AddRun(new Run("", TextFormat.None) { Image = pic.ToImage(m.Bytes, m.ContentType, link) });
                            else if (link is not null)
                                p.AddRun(new Run("", TextFormat.None) { Image = pic.ToImage(Array.Empty<byte>(), ImageMetadata.ContentTypeForExtension(Path.GetExtension(link)) ?? "application/octet-stream", link) });
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
                AppendEmbeddedText(sub, p);
                continue;
            }
            if (sub.NamespaceURI != W) continue;

            switch (sub.LocalName)
            {
                case "rPr":
                    // rPr belongs first in a run; if (invalidly) text came before it, that text
                    // keeps the formatting it was read with.
                    Flush();
                    using (var rPr = sub.ReadSubtree()) format = RunProperties.Read(rPr, out charStyle);
                    break;
                case "t":
                    if (fields.Capturing)
                    {
                        if (fields.Result.Length == 0 && fields.Format is null) { fields.Format = format; fields.StyleId = charStyle; }
                        fields.Result.Append(sub.ReadElementContentAsString());
                    }
                    else
                    {
                        text.Append(sub.ReadElementContentAsString());
                        sawText = true;
                    }
                    advance = false;
                    break;
                case "instrText":
                {
                    var instr = sub.ReadElementContentAsString();
                    if (fields.Collecting && fields.Depth == 1) fields.Instruction.Append(instr);
                    advance = false;
                    break;
                }
                case "fldChar":
                {
                    var kind = sub.GetAttribute("fldCharType", W);
                    Flush();
                    if (kind == "begin")
                    {
                        fields.Depth++;
                        if (fields.Depth == 1) { fields.Collecting = true; fields.Instruction.Clear(); }
                    }
                    else if (kind == "separate" && fields.Depth == 1)
                    {
                        fields.Collecting = false;
                        if (IsPageField(fields.Instruction.ToString())) { fields.Capturing = true; fields.Result.Clear(); fields.Format = null; fields.StyleId = null; }
                    }
                    else if (kind == "end" && fields.Depth > 0)
                    {
                        if (fields.Depth == 1)
                        {
                            var instr = fields.Instruction.ToString();
                            if (fields.Capturing || (fields.Collecting && IsPageField(instr)))
                                p.AddRun(Run.Field(instr, fields.Result.ToString(), fields.Format, fields.StyleId));
                            fields.Capturing = false;
                            fields.Collecting = false;
                        }
                        fields.Depth--;
                    }
                    break;
                }
                case "tab":
                    text.Append('\t');
                    break;
                case "ptab":
                    Flush();
                    p.AddRun(Run.PositionalTabRun(ReadPositionalTab(sub), format, charStyle));
                    break;
                case "cr":
                    text.Append('\n');
                    break;
                case "noBreakHyphen":
                    text.Append('\u2011');
                    break;
                case "softHyphen":
                    text.Append('\u00AD');
                    break;
                case "br":
                    var type = sub.GetAttribute("type", W);
                    var clear = sub.GetAttribute("clear", W);
                    if (type is null && clear is null)
                    {
                        text.Append('\n');
                        break;
                    }
                    Flush();
                    if (type == "page" && p.Runs.Count == 0)
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

    private static DocTable LoadTable(XmlReader sub, ImageSource? images) => TableXml.Read(sub, s => LoadParagraph(s, images));

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
