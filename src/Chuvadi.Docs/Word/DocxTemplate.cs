using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    {
        if (string.IsNullOrEmpty(inputPath)) throw new ArgumentException("Input path required.", nameof(inputPath));
        if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Output path required.", nameof(outputPath));
        textValues ??= new Dictionary<string, string>();
        imageValues ??= new Dictionary<string, ImageSpec>();

        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Obtain the plaintext package bytes (decrypting when needed).
        byte[] packageBytes;
        if (Chuvadi.Internal.Crypto.EncryptedPackageReader.IsEncryptedPackage(input))
        {
            try
            {
                packageBytes = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(input, password);
            }
            catch (Chuvadi.Internal.Crypto.PackagePasswordException ex)
            {
                throw new DocxPasswordRequiredException(ex.Message, ex);
            }
        }
        else
        {
            using var ms = new MemoryStream();
            input.CopyTo(ms);
            packageBytes = ms.ToArray();
        }

        // Transform into a new package in memory.
        byte[] filled;
        using (var inPkg = new MemoryStream(packageBytes, writable: false))
        using (var outPkg = new MemoryStream())
        {
            FillPackage(inPkg, outPkg, textValues, imageValues);
            filled = outPkg.ToArray();
        }

        // Write output (optionally encrypted).
        if (outputEncryption is null)
        {
            File.WriteAllBytes(outputPath, filled);
        }
        else
        {
            if (string.IsNullOrEmpty(outputEncryption.Password))
                throw new ArgumentException("Encryption password cannot be empty.", nameof(outputEncryption));
            var tempPath = Path.Combine(Path.GetTempPath(), $"chuvadi_docs_tpl_{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(tempPath, filled);
                using var spoolRead = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                Chuvadi.Internal.Crypto.EncryptedPackageWriter.WriteEncrypted(
                    output, spoolRead, spoolRead.Length, outputEncryption.Password, outputEncryption.SpinCount);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }

    // ---- Package transform -----------------------------------------------------------

    private sealed class ImageInsertion
    {
        public required string RelId { get; init; }
        public required string MediaName { get; init; } // e.g. "image5.png" (no path)
        public required ImageSpec Image { get; init; }
        public required int DrawingId { get; init; }
    }

    private static void FillPackage(
        Stream packageIn, Stream packageOut,
        IReadOnlyDictionary<string, string> textValues,
        IReadOnlyDictionary<string, ImageSpec> imageValues)
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
        int globalDrawingId = 1000; // high base to avoid clashing with template docPr ids

        var rewrittenParts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var partInsertions = new Dictionary<string, List<ImageInsertion>>(StringComparer.Ordinal);
        var newMedia = new List<(string MediaName, byte[] Bytes)>();
        var replaceMediaBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal); // full media path -> new bytes
        var neededExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Map each text-bearing part to its .rels (for resolving blip targets, alt-text replace).
        foreach (var entry in zipIn.Entries.ToList())
        {
            if (!IsTextBearingPart(entry.FullName)) continue;

            var relsPath = RelsPathFor(entry.FullName);
            var relTargets = ReadRelationshipTargets(zipIn, relsPath); // relId -> target media path (full)

            var xml = LoadXml(entry);

            // Text replacement (existing behaviour).
            ReplaceText(xml, textValues);

            // Image: replace-by-alt-text (swap bytes of an existing drawing's media).
            if (imageValues.Count > 0)
                ApplyAltTextImageReplacement(xml, imageValues, entry.FullName, relTargets, replaceMediaBytes);

            // Image: text-to-image insertion ({{key}} -> inline/floating drawing).
            if (imageValues.Count > 0)
            {
                var insertions = ApplyImagePlaceholders(
                    xml, imageValues,
                    allocMedia: (img) =>
                    {
                        mediaSeq++;
                        var ext = ImageMetadata.ExtensionFor(img.ContentType);
                        neededExtensions.Add(ext);
                        var mediaName = $"image{mediaSeq}.{ext}";
                        newMedia.Add((mediaName, img.Bytes));
                        return mediaName;
                    },
                    nextDrawingId: () => ++globalDrawingId);

                if (insertions.Count > 0)
                    partInsertions[entry.FullName] = insertions;
            }

            rewrittenParts[entry.FullName] = SaveXml(xml);
        }

        // ---- PASS 2: write the output package ----

        using var zipOut = new ZipArchive(packageOut, ZipArchiveMode.Create, leaveOpen: true);

        var relsHandled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in zipIn.Entries)
        {
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
                var injected = InjectImageRelationships(entry, inserts);
                WriteEntry(zipOut, name, injected);
                relsHandled.Add(ownerPart);
                continue;
            }

            // [Content_Types].xml: ensure image extensions are declared.
            if (name == "[Content_Types].xml")
            {
                var ct = EnsureContentTypeDefaults(entry, neededExtensions);
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

    // ---- Text replacement (unchanged behaviour) --------------------------------------

    private static void ReplaceText(XmlDocument xml, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0) return;
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("w", W);

        var paragraphs = xml.SelectNodes("//w:p", ns);
        if (paragraphs is null) return;

        foreach (XmlNode p in paragraphs)
        {
            var textNodes = p.SelectNodes(".//w:t", ns);
            if (textNodes is null || textNodes.Count == 0) continue;

            foreach (XmlNode t in textNodes)
                t.InnerText = ReplaceAll(t.InnerText, values);

            var combined = ConcatText(textNodes);
            if (ContainsCompletePlaceholder(combined))
            {
                var replaced = ReplaceAll(combined, values);
                if (replaced != combined)
                {
                    var first = (XmlElement)textNodes[0]!;
                    first.InnerText = replaced;
                    first.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    for (int i = 1; i < textNodes.Count; i++)
                        textNodes[i]!.InnerText = string.Empty;
                }
            }
        }
    }

    // ---- Image: text-to-image insertion ----------------------------------------------

    private static List<ImageInsertion> ApplyImagePlaceholders(
        XmlDocument xml,
        IReadOnlyDictionary<string, ImageSpec> imageValues,
        Func<ImageSpec, string> allocMedia,
        Func<int> nextDrawingId)
    {
        var result = new List<ImageInsertion>();
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("w", W);

        var paragraphs = xml.SelectNodes("//w:p", ns);
        if (paragraphs is null) return result;

        foreach (XmlNode p in paragraphs.Cast<XmlNode>().ToList())
        {
            var textNodes = p.SelectNodes(".//w:t", ns)?.Cast<XmlNode>().ToList();
            if (textNodes is null || textNodes.Count == 0) continue;

            var combined = ConcatText(textNodes);
            // Does this paragraph contain any image placeholder?
            string? hitKey = null;
            foreach (var key in imageValues.Keys)
                if (combined.Contains("{{" + key + "}}", StringComparison.Ordinal)) { hitKey = key; break; }
            if (hitKey is null) continue;

            // Consolidate paragraph text into the first run's w:t, blank the rest.
            var firstT = (XmlElement)textNodes[0];
            firstT.InnerText = combined;
            firstT.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
            for (int i = 1; i < textNodes.Count; i++) textNodes[i].InnerText = string.Empty;

            // The run that owns firstT is where we splice the image(s).
            var ownerRun = AncestorRun(firstT);
            if (ownerRun?.ParentNode is null) continue;

            // Repeatedly split out each image placeholder in this paragraph's consolidated text.
            var parentOfRun = ownerRun.ParentNode;

            // Re-emit the paragraph's consolidated text as alternating text/image runs.
            var pieces = SplitByImageKeys(firstT.InnerText, imageValues.Keys);
            // Remove original owner run; rebuild.
            var template = ownerRun;
            var newNodes = new List<XmlNode>();
            foreach (var piece in pieces)
            {
                if (piece.IsImage)
                {
                    var img = imageValues[piece.Key!];
                    var mediaName = allocMedia(img);
                    var relId = "rIdTplImg" + (result.Count + 1);
                    int drawingId = nextDrawingId();
                    result.Add(new ImageInsertion { RelId = relId, MediaName = mediaName, Image = img, DrawingId = drawingId });

                    var drawingRun = BuildDrawingRun(xml, img, relId, drawingId);
                    newNodes.Add(drawingRun);
                }
                else if (piece.Text.Length > 0)
                {
                    var textRun = (XmlElement)template.CloneNode(deep: true);
                    SetRunText(textRun, piece.Text);
                    newNodes.Add(textRun);
                }
            }

            foreach (var n in newNodes) parentOfRun.InsertBefore(n, template);
            parentOfRun.RemoveChild(template);
        }
        return result;
    }


    private readonly record struct Piece(bool IsImage, string Text, string? Key);

    private static List<Piece> SplitByImageKeys(string text, IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        var pieces = new List<Piece>();
        int i = 0;
        while (i < text.Length)
        {
            int bestPos = -1; string? bestKey = null;
            foreach (var key in keyList)
            {
                var token = "{{" + key + "}}";
                int pos = text.IndexOf(token, i, StringComparison.Ordinal);
                if (pos >= 0 && (bestPos < 0 || pos < bestPos)) { bestPos = pos; bestKey = key; }
            }
            if (bestPos < 0)
            {
                pieces.Add(new Piece(false, text[i..], null));
                break;
            }
            if (bestPos > i) pieces.Add(new Piece(false, text[i..bestPos], null));
            pieces.Add(new Piece(true, string.Empty, bestKey));
            i = bestPos + ("{{" + bestKey + "}}").Length;
        }
        return pieces;
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
        long cx = (long)Math.Round(image.WidthPt * DocumentSerializer.EmuPerPoint);
        long cy = (long)Math.Round(image.HeightPt * DocumentSerializer.EmuPerPoint);
        string name = $"Picture {drawingId}";
        string altAttr = image.AltText is null ? "" : $" descr=\"{Escape(image.AltText)}\"";

        string innerPlacement;
        if (image.Placement == ImagePlacement.Floating && image.Position is not null)
            innerPlacement = BuildAnchorXml(image.Position, cx, cy, drawingId, name, altAttr, relId);
        else
            innerPlacement = BuildInlineXml(cx, cy, drawingId, name, altAttr, relId);

        string runXml =
            $"<w:r xmlns:w=\"{W}\"><w:drawing xmlns:wp=\"{WP}\" xmlns:a=\"{A}\" xmlns:pic=\"{PIC}\" xmlns:r=\"{R}\">{innerPlacement}</w:drawing></w:r>";

        var frag = new XmlDocument();
        frag.LoadXml(runXml);
        return (XmlElement)doc.ImportNode(frag.DocumentElement!, deep: true);
    }

    private static string BuildInlineXml(long cx, long cy, int id, string name, string altAttr, string relId)
        => $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
           $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>" +
           $"<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
           $"<wp:docPr id=\"{id}\" name=\"{Escape(name)}\"{altAttr}/>" +
           GraphicFrameLocksXml() +
           GraphicXml(cx, cy, name, relId) +
           $"</wp:inline>";

    private static string BuildAnchorXml(FloatingPosition pos, long cx, long cy, int id, string name, string altAttr, string relId)
    {
        long dist = (long)Math.Round(pos.DistanceFromTextPt * DocumentSerializer.EmuPerPoint);
        long relHeight = pos.RelativeHeight ?? (251658240L + id);
        string behind = pos.Wrap == TextWrap.None && pos.BehindText ? "1" : "0";

        string hPos = pos.HAlign is HorizontalAlignment ha
            ? $"<wp:align>{HAlignValue(ha)}</wp:align>"
            : $"<wp:posOffset>{(long)Math.Round(pos.HorizontalOffsetPt * DocumentSerializer.EmuPerPoint)}</wp:posOffset>";
        string vPos = pos.VAlign is VerticalAlignment va
            ? $"<wp:align>{VAlignValue(va)}</wp:align>"
            : $"<wp:posOffset>{(long)Math.Round(pos.VerticalOffsetPt * DocumentSerializer.EmuPerPoint)}</wp:posOffset>";

        return $"<wp:anchor distT=\"{dist}\" distB=\"{dist}\" distL=\"{dist}\" distR=\"{dist}\" " +
               $"simplePos=\"0\" relativeHeight=\"{relHeight}\" behindDoc=\"{behind}\" " +
               $"locked=\"{(pos.LockAnchor ? "1" : "0")}\" layoutInCell=\"1\" allowOverlap=\"{(pos.AllowOverlap ? "1" : "0")}\">" +
               $"<wp:simplePos x=\"0\" y=\"0\"/>" +
               $"<wp:positionH relativeFrom=\"{HAnchorValue(pos.HorizontalAnchor)}\">{hPos}</wp:positionH>" +
               $"<wp:positionV relativeFrom=\"{VAnchorValue(pos.VerticalAnchor)}\">{vPos}</wp:positionV>" +
               $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>" +
               $"<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
               WrapXml(pos.Wrap) +
               $"<wp:docPr id=\"{id}\" name=\"{Escape(name)}\"{altAttr}/>" +
               GraphicFrameLocksXml() +
               GraphicXml(cx, cy, name, relId) +
               $"</wp:anchor>";
    }

    private static string WrapXml(TextWrap wrap) => wrap switch
    {
        TextWrap.None => "<wp:wrapNone/>",
        TextWrap.Square => "<wp:wrapSquare wrapText=\"bothSides\"/>",
        TextWrap.Tight => "<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapTight>",
        TextWrap.Through => "<wp:wrapThrough wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapThrough>",
        TextWrap.TopAndBottom => "<wp:wrapTopAndBottom/>",
        _ => "<wp:wrapSquare wrapText=\"bothSides\"/>",
    };

    private static string GraphicFrameLocksXml()
        => "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>";

    private static string GraphicXml(long cx, long cy, string name, string relId)
        => $"<a:graphic><a:graphicData uri=\"{PIC}\">" +
           $"<pic:pic>" +
           $"<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"{Escape(name)}\"/><pic:cNvPicPr/></pic:nvPicPr>" +
           $"<pic:blipFill><a:blip r:embed=\"{relId}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
           $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
           $"<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
           $"</pic:pic></a:graphicData></a:graphic>";

    private static string HAnchorValue(HorizontalAnchor a) => a switch
    {
        HorizontalAnchor.Page => "page", HorizontalAnchor.Margin => "margin", HorizontalAnchor.Column => "column",
        HorizontalAnchor.Character => "character", HorizontalAnchor.LeftMargin => "leftMargin",
        HorizontalAnchor.RightMargin => "rightMargin", HorizontalAnchor.InsideMargin => "insideMargin",
        HorizontalAnchor.OutsideMargin => "outsideMargin", _ => "column",
    };
    private static string VAnchorValue(VerticalAnchor a) => a switch
    {
        VerticalAnchor.Page => "page", VerticalAnchor.Margin => "margin", VerticalAnchor.Line => "line",
        VerticalAnchor.Paragraph => "paragraph", VerticalAnchor.TopMargin => "topMargin",
        VerticalAnchor.BottomMargin => "bottomMargin", VerticalAnchor.InsideMargin => "insideMargin",
        VerticalAnchor.OutsideMargin => "outsideMargin", _ => "paragraph",
    };
    private static string HAlignValue(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Left => "left", HorizontalAlignment.Center => "center", HorizontalAlignment.Right => "right",
        HorizontalAlignment.Inside => "inside", HorizontalAlignment.Outside => "outside", _ => "left",
    };
    private static string VAlignValue(VerticalAlignment a) => a switch
    {
        VerticalAlignment.Top => "top", VerticalAlignment.Center => "center", VerticalAlignment.Bottom => "bottom",
        VerticalAlignment.Inside => "inside", VerticalAlignment.Outside => "outside", _ => "top",
    };

    // ---- Relationship + content-type injection ---------------------------------------

    private static Dictionary<string, string> ReadRelationshipTargets(ZipArchive zip, string relsPath)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = zip.GetEntry(relsPath);
        if (entry is null) return map;
        var xml = LoadXml(entry);
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

    private static byte[] InjectImageRelationships(ZipArchiveEntry relsEntry, List<ImageInsertion> inserts)
    {
        var xml = LoadXml(relsEntry);
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

    private static byte[] EnsureContentTypeDefaults(ZipArchiveEntry ctEntry, HashSet<string> extensions)
    {
        var xml = LoadXml(ctEntry);
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

    private static XmlDocument LoadXml(ZipArchiveEntry entry)
    {
        var xml = new XmlDocument { PreserveWhitespace = true };
        using var s = entry.Open();
        using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
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

    private static XmlElement? AncestorRun(XmlNode node)
    {
        var cur = node.ParentNode;
        while (cur is not null)
        {
            if (cur.LocalName == "r" && cur.NamespaceURI == W) return (XmlElement)cur;
            cur = cur.ParentNode;
        }
        return null;
    }

    private static void SetRunText(XmlElement run, string text)
    {
        var ns = new XmlNamespaceManager(run.OwnerDocument.NameTable);
        ns.AddNamespace("w", W);
        var t = run.SelectSingleNode(".//w:t", ns) as XmlElement;
        if (t is null)
        {
            t = run.OwnerDocument.CreateElement("w", "t", W);
            run.AppendChild(t);
        }
        t.InnerText = text;
        t.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
    }

    private static string ConcatText(System.Collections.IEnumerable textNodes)
    {
        var sb = new StringBuilder();
        foreach (XmlNode t in textNodes) sb.Append(t.InnerText);
        return sb.ToString();
    }

    private static string ConcatText(XmlNodeList textNodes)
    {
        var sb = new StringBuilder();
        foreach (XmlNode t in textNodes) sb.Append(t.InnerText);
        return sb.ToString();
    }

    private static bool ContainsCompletePlaceholder(string s)
    {
        int open = s.IndexOf("{{", StringComparison.Ordinal);
        if (open < 0) return false;
        return s.IndexOf("}}", open + 2, StringComparison.Ordinal) >= 0;
    }

    private static string ReplaceAll(string text, IReadOnlyDictionary<string, string> values)
    {
        if (text.IndexOf("{{", StringComparison.Ordinal) < 0) return text;
        foreach (var (key, value) in values)
            text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        return text;
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
