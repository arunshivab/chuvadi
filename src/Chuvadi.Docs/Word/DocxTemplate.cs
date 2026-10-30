using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Chuvadi.Docs.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Fills <c>{{Placeholder}}</c> values into a .docx template designed in Word, preserving
/// EVERYTHING in the template — styles, images, tables, headers/footers, themes — because
/// only text nodes are rewritten and image parts are added surgically; all other package
/// parts are copied byte-for-byte.
///
/// <code>
/// // Text only:
/// DocxTemplate.Fill("invoice-template.docx", "invoice-0042.docx", new Dictionary&lt;string,string&gt;
/// {
///     ["CustomerName"] = "Acme Pvt Ltd",
///     ["InvoiceNo"]    = "0042",
/// });
///
/// // Text + images:
/// DocxTemplate.Fill("template.docx", "out.docx",
///     textValues:  new Dictionary&lt;string,string&gt; { ["Name"] = "Arun" },
///     imageValues: new Dictionary&lt;string,ImageSpec&gt;
///     {
///         ["Logo"] = ImageSpec.FromFile("logo.png", widthPt: 80, heightPt: 40),
///     });
/// </code>
///
/// TEXT placeholders are replaced in the body, headers, footers, footnotes and endnotes.
/// Word often splits typed text across runs, so a placeholder may span runs; this is handled
/// by consolidating the paragraph's text into its first run. Placeholders fully inside one run
/// keep surrounding formatting. A placeholder must not span paragraphs. Unmatched placeholders
/// are left as-is.
///
/// IMAGE placeholders work two ways:
/// <list type="bullet">
/// <item><b>Text-to-image:</b> a <c>{{Key}}</c> whose key is in <c>imageValues</c> is replaced
/// in place with an inline image (or floating, per the <see cref="ImageSpec"/>).</item>
/// <item><b>Replace-by-alt-text:</b> an existing image in the template whose alt text equals a
/// key in <c>imageValues</c> has its bytes swapped for the new image, keeping the template's
/// position and size. For best results the replacement should be the same format as the
/// original.</item>
/// </list>
/// </summary>
public static class DocxTemplate
{
    private const string W = DocumentSerializer.W;
    private const string WP = DocumentSerializer.WP;
    private const string A = DocumentSerializer.A;
    private const string PIC = DocumentSerializer.PIC;
    private const string R = DocumentSerializer.R;
    private const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string RelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string CtNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Fills a template with text values only (backwards-compatible overload).</summary>
    /// <remarks>
    /// Preferred by overload resolution, so a bare <c>null</c> third argument binds here as it did
    /// before the <see cref="TemplateData"/> overloads existed (and throws <see cref="ArgumentNullException"/>).
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static void Fill(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> values,
        string? password = null,
        EncryptionOptions? outputEncryption = null)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        Fill(inputPath, outputPath, values, imageValues: null, password, outputEncryption);
    }

    /// <summary>Fills a template with text and/or image values.</summary>
    /// <param name="inputPath">The template .docx (may be password-encrypted; pass <paramref name="password"/>).</param>
    /// <param name="outputPath">The filled output (created or overwritten).</param>
    /// <param name="textValues">Placeholder name → replacement text, matched as <c>{{Name}}</c>. May be null.</param>
    /// <param name="imageValues">Placeholder/alt-text name → image. May be null.</param>
    /// <param name="password">Password for an encrypted template; null for unencrypted.</param>
    /// <param name="outputEncryption">When set, the filled output is saved encrypted.</param>
    public static void Fill(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string>? textValues,
        IReadOnlyDictionary<string, ImageSpec>? imageValues,
        string? password = null,
        EncryptionOptions? outputEncryption = null)
        => Fill(inputPath, outputPath, textValues, imageValues, password, outputEncryption,
            readOptions: null, System.Threading.CancellationToken.None);

    /// <summary>
    /// Fills a template with explicit resource limits for reading the template (see
    /// <see cref="DocxReaderOptions"/>; null = defaults) and cancellation. Use
    /// <see cref="DocxReaderOptions.CreateUnrestricted"/> for trusted templates that exceed the
    /// defaults (e.g. very deep nesting). Limit breaches throw <see cref="DocxSecurityLimitException"/>;
    /// cancellation throws <see cref="OperationCanceledException"/> and writes no output.
    /// </summary>
    public static void Fill(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string>? textValues,
        IReadOnlyDictionary<string, ImageSpec>? imageValues,
        string? password,
        EncryptionOptions? outputEncryption,
        DocxReaderOptions? readOptions,
        System.Threading.CancellationToken cancellationToken = default)
        => Fill(inputPath, outputPath, TemplateData.From(textValues, imageValues),
            new TemplateOptions { Password = password, OutputEncryption = outputEncryption, ReadOptions = readOptions }, cancellationToken);

    /// <summary>
    /// Fills a template file with <paramref name="data"/>: values, formatted values, images,
    /// repeating blocks (<c>{{#Items}}</c> … <c>{{/Items}}</c>) and conditions (<c>{{?Flag}}</c>,
    /// <c>{{^Flag}}</c>). See <see cref="TemplateData"/> for the syntax.
    /// </summary>
    public static void Fill(string inputPath, string outputPath, TemplateData data, TemplateOptions? options = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(inputPath)) throw new ArgumentException("Input path required.", nameof(inputPath));
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path required.", nameof(outputPath));
        byte[] filled;
        using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            filled = FillToBytes(input, data, options, cancellationToken);
        File.WriteAllBytes(outputPath, filled);
    }

    /// <summary>
    /// Fills a template read from <paramref name="template"/> (any stream; left open) and writes the
    /// result to <paramref name="output"/> (left open). Nothing is written when filling fails or is cancelled.
    /// </summary>
    public static void Fill(Stream template, Stream output, TemplateData data, TemplateOptions? options = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (output is null) throw new ArgumentNullException(nameof(output));
        var filled = FillToBytes(template, data, options, cancellationToken);
        output.Write(filled, 0, filled.Length);
        output.Flush();
    }

    /// <summary>Every placeholder in a template, in document order per part, with its kind and enclosing blocks.</summary>
    [OverloadResolutionPriority(1)] // GetPlaceholders(null) binds to the path overload (ArgumentException), not ambiguous
    public static IReadOnlyList<TemplatePlaceholder> GetPlaceholders(string path, string? password = null, DocxReaderOptions? readOptions = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return GetPlaceholders(input, password, readOptions);
    }

    /// <summary>Every placeholder in a template read from a stream (left open).</summary>
    public static IReadOnlyList<TemplatePlaceholder> GetPlaceholders(Stream template, string? password = null, DocxReaderOptions? readOptions = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        var limits = readOptions ?? new DocxReaderOptions();
        var lim = new FillLimits(limits, cancellationToken);
        var packageBytes = ReadPackage(template, password, limits, cancellationToken);
        var result = new List<TemplatePlaceholder>();
        using var zipIn = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (var entry in OrderedTextParts(zipIn))
        {
            var xml = LoadXml(entry, lim);
            result.AddRange(new TemplateEngine(xml, entry.FullName, new TemplateOptions(), null, cancellationToken: cancellationToken).List());
        }
        return result;
    }

    /// <summary>
    /// Checks a template without filling it and returns every problem a fill would meet whatever
    /// the data: block markers without a partner, and blocks whose markers cannot form a block
    /// (inline within one paragraph, marker paragraphs, or table rows). Empty = no problems.
    /// </summary>
    [OverloadResolutionPriority(1)] // CheckTemplate(null) binds to the path overload (ArgumentException), not ambiguous
    public static IReadOnlyList<string> CheckTemplate(string path, string? password = null, DocxReaderOptions? readOptions = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return CheckTemplate(input, password, readOptions);
    }

    /// <summary>Checks a template read from a stream (left open); see <see cref="CheckTemplate(string, string?, DocxReaderOptions?)"/>.</summary>
    public static IReadOnlyList<string> CheckTemplate(Stream template, string? password = null, DocxReaderOptions? readOptions = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        var limits = readOptions ?? new DocxReaderOptions();
        var lim = new FillLimits(limits, cancellationToken);
        var packageBytes = ReadPackage(template, password, limits, cancellationToken);
        var result = new List<string>();
        using var zipIn = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (var entry in OrderedTextParts(zipIn))
        {
            var xml = LoadXml(entry, lim);
            result.AddRange(new TemplateEngine(xml, entry.FullName, new TemplateOptions(), null, cancellationToken: cancellationToken).Check());
        }
        return result;
    }

    private static byte[] ReadPackage(Stream input, string? password, DocxReaderOptions limits, System.Threading.CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var seekable = PackageGuard.EnsureSeekable(input, limits, ct, out var buffered);
        using var _ = buffered;
        byte[] packageBytes;
        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(seekable))
            packageBytes = PackageGuard.Decrypt(seekable, password, limits, ct);
        else
        {
            using var ms = PackageGuard.CopyBounded(seekable, limits, ct);
            packageBytes = ms.ToArray();
        }
        using (var scan = new MemoryStream(packageBytes, writable: false))
            PackageGuard.Scan(scan, limits, ct);
        return packageBytes;
    }

    private static byte[] FillToBytes(Stream input, TemplateData data, TemplateOptions? options, System.Threading.CancellationToken ct)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        options ??= new TemplateOptions();
        options.Validate();
        // Templates get the same resource limits as DocxReader (zip bomb, nesting depth, spin
        // count, input size) — a template can arrive from an upload or a database too.
        var limits = options.ReadOptions ?? new DocxReaderOptions();
        var packageBytes = ReadPackage(input, options.Password, limits, ct);
        var fillLimits = new FillLimits(limits, ct);

        byte[] filled;
        using (var inPkg = new MemoryStream(packageBytes, writable: false))
        using (var outPkg = new MemoryStream())
        {
            FillPackage(inPkg, outPkg, data, options, fillLimits);
            filled = outPkg.ToArray();
        }

        if (options.OutputEncryption is not { } enc) return filled;
        if (string.IsNullOrEmpty(enc.Password))
            throw new ArgumentException("Encryption password cannot be empty.", nameof(options));
        using var plain = new MemoryStream(filled, writable: false);
        using var encrypted = new MemoryStream();
        Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(encrypted, plain, plain.Length, enc.Password, enc.SpinCount, ct);
        return encrypted.ToArray();
    }

    // ---- Package transform -----------------------------------------------------------

    private sealed class ImageInsertion
    {
        public required string RelId { get; init; }
        public required string MediaName { get; init; } // e.g. "image5.png" (no path)
        public required ImageSpec Image { get; init; }
        public required int DrawingId { get; init; }
    }

    /// <summary>Read limits and cancellation for one Fill call, threaded to every part read.</summary>
    private sealed record FillLimits(DocxReaderOptions Options, System.Threading.CancellationToken Ct);

    private static void FillPackage(Stream packageIn, Stream packageOut, TemplateData data, TemplateOptions options, FillLimits lim)
    {
        // ---- PASS 1: read text-bearing parts + their .rels, compute all edits ----

        using var zipIn = new ZipArchive(packageIn, ZipArchiveMode.Read, leaveOpen: true);

        // Existing media file numbering: continue past the highest imageN already present.
        int mediaSeq = 0;
        foreach (var e in zipIn.Entries)
        {
            if (e.FullName.StartsWith("word/media/image", StringComparison.OrdinalIgnoreCase))
            {
                var stem = Path.GetFileNameWithoutExtension(e.FullName);
                if (stem.Length > 5 && int.TryParse(stem.AsSpan(5), out var n)) mediaSeq = Math.Max(mediaSeq, n);
            }
        }
        // Parts are loaded first so ids already in use anywhere are known: drawing ids (wp:docPr)
        // and bookmark ids must stay unique across the document, also when an output is used as
        // a template again.
        var parts = new List<(ZipArchiveEntry Entry, XmlDocument Xml)>();
        foreach (var entry in OrderedTextParts(zipIn))
        {
            lim.Ct.ThrowIfCancellationRequested();
            parts.Add((entry, LoadXml(entry, lim)));
        }
        int maxDrawingId = 0, maxBookmarkId = -1;
        foreach (var (_, xml) in parts) ScanIds(xml, ref maxDrawingId, ref maxBookmarkId);
        int globalDrawingId = Math.Max(1000, maxDrawingId); // high base to avoid clashing with template docPr ids
        int bookmarkId = maxBookmarkId;

        var rewrittenParts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var partInsertions = new Dictionary<string, List<ImageInsertion>>(StringComparer.Ordinal);
        var newMedia = new List<(string MediaName, byte[] Bytes)>();
        var mediaByBytes = new Dictionary<byte[], string>(ReferenceEqualityComparer.Instance); // same image → one media part
        var replaceMediaBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal); // full media path -> new bytes
        var neededExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        // Top-level images also replace template pictures by alt text.
        var rootImages = new Dictionary<string, ImageSpec>(StringComparer.Ordinal);
        foreach (var key in data.Keys)
            if (data[key] is ImageSpec img) rootImages[key] = img;

        foreach (var (entry, xml) in parts)
        {
            var relsPath = RelsPathFor(entry.FullName);
            lim.Ct.ThrowIfCancellationRequested();
            var relTargets = ReadRelationshipTargets(zipIn, relsPath, lim); // relId -> target media path (full)

            var insertions = new List<ImageInsertion>();
            var relByMedia = new Dictionary<string, string>(StringComparer.Ordinal);
            int relSeq = 0;
            XmlElement MakeImageRun(ImageSpec img)
            {
                if (!mediaByBytes.TryGetValue(img.Bytes, out var mediaName))
                {
                    mediaSeq++;
                    var ext = ImageMetadata.ExtensionFor(img.ContentType);
                    neededExtensions.Add(ext);
                    mediaName = $"image{mediaSeq}.{ext}";
                    newMedia.Add((mediaName, img.Bytes));
                    mediaByBytes[img.Bytes] = mediaName;
                }
                if (!relByMedia.TryGetValue(mediaName, out var relId))
                {
                    // A relationship id the part does not use yet.
                    do relId = "rIdTplImg" + (++relSeq); while (relTargets.ContainsKey(relId));
                    relByMedia[mediaName] = relId;
                    insertions.Add(new ImageInsertion { RelId = relId, MediaName = mediaName, Image = img, DrawingId = 0 });
                }
                return BuildDrawingRun(xml, img, relId, ++globalDrawingId);
            }

            // Blocks, values (text, formatted, images). Only the touched runs change; untouched
            // template text is never altered.
            var engine = new TemplateEngine(xml, entry.FullName, options, MakeImageRun,
                () => ++globalDrawingId, () => ++bookmarkId, lim.Ct);
            engine.Run(data);
            problems.AddRange(engine.Problems);

            // Image: replace-by-alt-text (swap bytes of an existing drawing's media).
            if (rootImages.Count > 0)
                ApplyAltTextImageReplacement(xml, rootImages, entry.FullName, relTargets, replaceMediaBytes);

            if (insertions.Count > 0) partInsertions[entry.FullName] = insertions;

            // Line feeds / tabs inside FILLED values become w:br / w:tab.
            ExpandControlCharacters(engine.ChangedText);

            rewrittenParts[entry.FullName] = SaveXml(xml);
        }
        if (problems.Count > 0) throw new DocxTemplateException(problems);

        // ---- PASS 2: write the output package ----

        using var zipOut = new ZipArchive(packageOut, ZipArchiveMode.Create, leaveOpen: true);

        var relsHandled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in zipIn.Entries)
        {
            lim.Ct.ThrowIfCancellationRequested();
            var name = entry.FullName;

            // Replaced media bytes (alt-text path).
            if (replaceMediaBytes.TryGetValue(name, out var newBytes))
            {
                WriteEntry(zipOut, name, newBytes);
                continue;
            }

            // Rewritten text part.
            if (rewrittenParts.TryGetValue(name, out var partBytes))
            {
                WriteEntry(zipOut, name, partBytes);
                continue;
            }

            // A .rels file whose owner part received image insertions: inject relationships.
            var ownerPart = OwnerPartForRels(name);
            if (ownerPart is not null && partInsertions.TryGetValue(ownerPart, out var inserts))
            {
                var injected = InjectImageRelationships(entry, inserts, lim);
                WriteEntry(zipOut, name, injected);
                relsHandled.Add(ownerPart);
                continue;
            }

            // [Content_Types].xml: ensure image extensions are declared.
            if (name == "[Content_Types].xml")
            {
                var ct = EnsureContentTypeDefaults(entry, neededExtensions, lim);
                WriteEntry(zipOut, name, ct);
                continue;
            }

            // Everything else: copy byte-for-byte.
            CopyEntry(entry, zipOut);
        }

        // Parts that needed image rels but had NO existing .rels file: create new .rels.
        foreach (var (ownerPart, inserts) in partInsertions)
        {
            if (relsHandled.Contains(ownerPart)) continue;
            var relsPath = RelsPathFor(ownerPart);
            var created = CreateImageRelationships(inserts);
            WriteEntry(zipOut, relsPath, created);
        }

        // Add new media parts.
        foreach (var (mediaName, bytes) in newMedia)
            WriteEntry(zipOut, "word/media/" + mediaName, bytes);
    }

    /// <summary>The largest drawing id (wp:docPr/@id) and bookmark id (w:bookmarkStart/@w:id) in a part.</summary>
    private static void ScanIds(XmlDocument xml, ref int maxDrawingId, ref int maxBookmarkId)
    {
        if (xml.DocumentElement is null) return;
        var stack = new Stack<XmlNode>();
        stack.Push(xml.DocumentElement);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is XmlElement e)
            {
                if (e.LocalName == "docPr" && e.NamespaceURI == WP
                    && int.TryParse(e.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
                    maxDrawingId = Math.Max(maxDrawingId, d);
                else if (e.LocalName is "bookmarkStart" or "bookmarkEnd" && e.NamespaceURI == W
                    && int.TryParse(e.GetAttribute("id", W), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                    maxBookmarkId = Math.Max(maxBookmarkId, b);
            }
            for (var c = n.FirstChild; c is not null; c = c.NextSibling) stack.Push(c);
        }
    }

    /// <summary>
    /// In each FILLED text node, turns line feeds (\n, \r\n, \r) into <c>w:br</c> and tabs
    /// into <c>w:tab</c> within the same run, so multi-line values (addresses, notes) render
    /// on separate lines with the run's formatting. Nodes detached by image insertion are skipped.
    /// </summary>
    private static void ExpandControlCharacters(HashSet<XmlElement> textNodes)
    {
        const string XmlNs = "http://www.w3.org/XML/1998/namespace";
        foreach (var t in textNodes)
        {
            var parent = t.ParentNode;
            if (parent is null || t.OwnerDocument is null) continue;
            var text = t.InnerText;
            if (text.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0) continue;

            var doc = t.OwnerDocument;
            var prefix = t.Prefix;
            void AddText(string segment)
            {
                var el = doc.CreateElement(prefix, "t", W);
                el.InnerText = segment;
                if (segment.Length > 0 && (char.IsWhiteSpace(segment[0]) || char.IsWhiteSpace(segment[^1])))
                    el.SetAttribute("space", XmlNs, "preserve");
                parent.InsertBefore(el, t);
            }
            void AddEmpty(string name) => parent.InsertBefore(doc.CreateElement(prefix, name, W), t);

            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\n' && c != '\r' && c != '\t') continue;
                if (i > start) AddText(text.Substring(start, i - start));
                if (c == '\t') AddEmpty("tab");
                else
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    AddEmpty("br");
                }
                start = i + 1;
            }
            if (start < text.Length) AddText(text.Substring(start));
            parent.RemoveChild(t);
        }
    }

    // ---- Image: replace-by-alt-text --------------------------------------------------

    private static void ApplyAltTextImageReplacement(
        XmlDocument xml,
        IReadOnlyDictionary<string, ImageSpec> imageValues,
        string partName,
        Dictionary<string, string> relTargets,
        Dictionary<string, byte[]> replaceMediaBytes)
    {
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("wp", WP);
        ns.AddNamespace("a", A);
        ns.AddNamespace("r", R);

        var docPrs = xml.SelectNodes("//wp:docPr", ns);
        if (docPrs is null) return;

        foreach (XmlNode docPr in docPrs)
        {
            var descr = (docPr as XmlElement)?.GetAttribute("descr");
            if (string.IsNullOrEmpty(descr) || !imageValues.TryGetValue(descr, out var img)) continue;

            // Find the blip's r:embed within the same drawing (docPr's parent is inline/anchor).
            var blip = FindDescendantBlip(docPr.ParentNode);
            if (blip is null) continue;
            var relId = ((XmlElement)blip).GetAttribute("embed", R);
            if (string.IsNullOrEmpty(relId) || !relTargets.TryGetValue(relId, out var mediaPath)) continue;

            // Swap the media bytes (keeps size/position from the template).
            replaceMediaBytes[mediaPath] = img.Bytes;
        }
    }

    private static XmlNode? FindDescendantBlip(XmlNode? container)
    {
        if (container is null) return null;
        foreach (XmlNode child in container.ChildNodes)
        {
            if (child.LocalName == "blip" && child.NamespaceURI == A) return child;
            var nested = FindDescendantBlip(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    // ---- Drawing XML construction ----------------------------------------------------

    private static XmlElement BuildDrawingRun(XmlDocument doc, ImageSpec image, string relId, int drawingId)
    {
        // The same DrawingML writer as Document.SaveTo (crop, rotation, wrap polygon… included).
        var sb = new StringBuilder();
        using (var x = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            x.WriteStartElement("w", "root", W);
            x.WriteAttributeString("xmlns", "wp", null, WP);
            x.WriteAttributeString("xmlns", "r", null, R);
            DrawingXml.WriteRun(x, image, relId, drawingId);
            x.WriteEndElement();
        }
        var frag = new XmlDocument();
        frag.LoadXml(sb.ToString());
        return (XmlElement)doc.ImportNode(frag.DocumentElement!.FirstChild!, deep: true);
    }

    private static Dictionary<string, string> ReadRelationshipTargets(ZipArchive zip, string relsPath, FillLimits lim)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = zip.GetEntry(relsPath);
        if (entry is null) return map;
        var xml = LoadXml(entry, lim);
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("r", RelsNs);
        var rels = xml.SelectNodes("//r:Relationship", ns);
        if (rels is null) return map;
        // Targets are relative to the part's folder (word/). Media path becomes word/media/imageN.ext
        foreach (XmlNode rel in rels)
        {
            var el = (XmlElement)rel;
            var id = el.GetAttribute("Id");
            var target = el.GetAttribute("Target");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) continue;
            // Resolve relative to "word/" (the part dir for document.xml/header/footer).
            var full = target.StartsWith("/")
                ? target.TrimStart('/')
                : "word/" + target.Replace("../", "");
            map[id] = full;
        }
        return map;
    }

    private static byte[] InjectImageRelationships(ZipArchiveEntry relsEntry, List<ImageInsertion> inserts, FillLimits lim)
    {
        var xml = LoadXml(relsEntry, lim);
        var root = xml.DocumentElement!;
        foreach (var ins in inserts)
        {
            var rel = xml.CreateElement("Relationship", RelsNs);
            rel.SetAttribute("Id", ins.RelId);
            rel.SetAttribute("Type", RelImage);
            rel.SetAttribute("Target", "media/" + ins.MediaName);
            root.AppendChild(rel);
        }
        return SaveXml(xml);
    }

    private static byte[] CreateImageRelationships(List<ImageInsertion> inserts)
    {
        var xml = new XmlDocument();
        var root = xml.CreateElement("Relationships", RelsNs);
        xml.AppendChild(root);
        foreach (var ins in inserts)
        {
            var rel = xml.CreateElement("Relationship", RelsNs);
            rel.SetAttribute("Id", ins.RelId);
            rel.SetAttribute("Type", RelImage);
            rel.SetAttribute("Target", "media/" + ins.MediaName);
            root.AppendChild(rel);
        }
        return SaveXml(xml);
    }

    private static byte[] EnsureContentTypeDefaults(ZipArchiveEntry ctEntry, HashSet<string> extensions, FillLimits lim)
    {
        var xml = LoadXml(ctEntry, lim);
        var root = xml.DocumentElement!;
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("ct", CtNs);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaults = xml.SelectNodes("//ct:Default", ns);
        if (defaults is not null)
            foreach (XmlNode d in defaults)
                existing.Add(((XmlElement)d).GetAttribute("Extension"));

        foreach (var ext in extensions)
        {
            if (existing.Contains(ext)) continue;
            var contentType = ext switch
            {
                "png" => "image/png",
                "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "tiff" => "image/tiff",
                "emf" => "image/x-emf",
                "wmf" => "image/x-wmf",
                _ => "application/octet-stream",
            };
            var def = xml.CreateElement("Default", CtNs);
            def.SetAttribute("Extension", ext);
            def.SetAttribute("ContentType", contentType);
            root.PrependChild(def);
        }
        return SaveXml(xml);
    }

    // ---- Zip + XML helpers -----------------------------------------------------------

    /// <summary>Text-bearing parts in a stable order (document first, then headers, footers, notes).</summary>
    private static IEnumerable<ZipArchiveEntry> OrderedTextParts(ZipArchive zip)
        => zip.Entries.Where(e => IsTextBearingPart(e.FullName)).OrderBy(e => e.FullName == "word/document.xml" ? 0 : 1)
            .ThenBy(e => e.FullName, StringComparer.Ordinal).ToList();

    private static bool IsTextBearingPart(string entryName)
        => entryName is "word/document.xml" or "word/footnotes.xml" or "word/endnotes.xml"
           || (entryName.StartsWith("word/header", StringComparison.Ordinal) && entryName.EndsWith(".xml", StringComparison.Ordinal))
           || (entryName.StartsWith("word/footer", StringComparison.Ordinal) && entryName.EndsWith(".xml", StringComparison.Ordinal));

    private static string RelsPathFor(string partName)
    {
        var slash = partName.LastIndexOf('/');
        var dir = slash >= 0 ? partName[..(slash + 1)] : "";
        var file = slash >= 0 ? partName[(slash + 1)..] : partName;
        return $"{dir}_rels/{file}.rels";
    }

    private static string? OwnerPartForRels(string relsName)
    {
        // word/_rels/document.xml.rels -> word/document.xml
        if (!relsName.EndsWith(".rels", StringComparison.Ordinal)) return null;
        var idx = relsName.IndexOf("_rels/", StringComparison.Ordinal);
        if (idx < 0) return null;
        var dir = relsName[..idx];
        var file = relsName[(idx + "_rels/".Length)..^".rels".Length];
        return dir + file;
    }

    private static XmlDocument LoadXml(ZipArchiveEntry entry, FillLimits lim)
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        using var s = GuardedPartStream.Wrap(entry.Open(), lim.Options.MaxPartBytes, lim.Ct, entry.FullName);
        // Whitespace and comments are preserved: rewritten parts must keep everything else intact.
        using var reader = GuardedXmlReader.Create(s, lim.Options.MaxXmlDepth, lim.Ct, entry.FullName,
            ignoreWhitespace: false, ignoreComments: false);
        xml.Load(reader);
        return xml;
    }

    private static byte[] SaveXml(XmlDocument xml)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
        }))
        {
            xml.Save(writer);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static void CopyEntry(ZipArchiveEntry src, ZipArchive zipOut)
    {
        var outEntry = zipOut.CreateEntry(src.FullName, CompressionLevel.Optimal);
        using var inS = src.Open();
        using var outS = outEntry.Open();
        inS.CopyTo(outS);
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
