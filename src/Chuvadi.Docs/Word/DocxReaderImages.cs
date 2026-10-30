using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Image-reading members of <see cref="DocxReader"/>. Parses w:drawing elements (inline and
/// floating), resolves blip relationship ids to media parts, reads the raw bytes, and records
/// placement plus table location for each image.
/// </summary>
public sealed partial class DocxReader
{
    /// <summary>
    /// All images in the document — body, headers, and footers (subject to
    /// <see cref="DocxReaderOptions.IncludeHeadersAndFooters"/>) — with bytes, display size,
    /// placement, and table location. Returned in document order per part.
    /// </summary>
    public IReadOnlyList<ImageInfo> Images() => Images(CancellationToken.None);

    /// <summary>As <see cref="Images()"/>, observing <paramref name="cancellationToken"/>.</summary>
    public IReadOnlyList<ImageInfo> Images(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        var result = new List<ImageInfo>();

        CollectImagesFromPart(_documentUri, ImageHost.Body, result, cancellationToken);
        if (_options.IncludeHeadersAndFooters)
        {
            foreach (var uri in _headerUris) CollectImagesFromPart(uri, ImageHost.Header, result, cancellationToken);
            foreach (var uri in _footerUris) CollectImagesFromPart(uri, ImageHost.Footer, result, cancellationToken);
        }
        return result;
    }

    /// <summary>
    /// Saves every image to <paramref name="folderPath"/> (created if needed), using each
    /// image's package file name (image1.png, image2.jpeg, ...). Returns the full paths
    /// written, in order. Duplicate file names are de-collided with a numeric suffix.
    /// </summary>
    public IReadOnlyList<string> SaveImages(string folderPath) => SaveImages(folderPath, CancellationToken.None);

    /// <summary>As <see cref="SaveImages(string)"/>, observing <paramref name="cancellationToken"/>.</summary>
    public IReadOnlyList<string> SaveImages(string folderPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folderPath)) throw new ArgumentException("Folder path required.", nameof(folderPath));
        Directory.CreateDirectory(folderPath);

        var written = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in Images(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = img.FileName;
            var target = Path.Combine(folderPath, name);
            int n = 1;
            while (used.Contains(target) || File.Exists(target))
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                target = Path.Combine(folderPath, $"{stem}_{n}{ext}");
                n++;
            }
            File.WriteAllBytes(target, img.Bytes);
            used.Add(target);
            written.Add(target);
        }
        return written;
    }

    // ---- Internal: scan one part for drawings ----------------------------------------

    private void CollectImagesFromPart(string partUri, ImageHost host, List<ImageInfo> sink, CancellationToken ct)
    {
        // Resolve this part's image relationships: relId -> media part uri.
        var relToMedia = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in _package.GetRelationships(partUri))
        {
            if (rel.Type.EndsWith("/image", StringComparison.Ordinal))
                relToMedia[rel.Id] = ResolveUri(partUri, rel.Target);
        }
        if (relToMedia.Count == 0) return;

        Stream? s = TryOpenPart(partUri, ct);
        if (s is null) return;
        using var _ = s;
        using var r = CreateXml(s, partUri, ct);

        // Track table position as we scan, so an image inside a cell records its location.
        int tableIndex = -1;
        int rowIndex = -1;
        int colIndex = -1;
        int tblDepth = 0;
        string? lastParagraphText = null;
        var paraText = new System.Text.StringBuilder();

        if (!r.Read()) return;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.W)
            {
                switch (r.LocalName)
                {
                    case "tbl":
                        tblDepth++;
                        if (tblDepth == 1) { tableIndex++; rowIndex = -1; }
                        break;
                    case "tr" when tblDepth == 1:
                        rowIndex++; colIndex = -1;
                        break;
                    case "tc" when tblDepth == 1:
                        colIndex++;
                        break;
                    case "p":
                        paraText.Clear();
                        break;
                    case "t":
                        paraText.Append(r.ReadElementContentAsString());
                        continue;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.NamespaceURI == DocumentSerializer.W)
            {
                if (r.LocalName == "tbl") { tblDepth--; if (tblDepth == 0) { rowIndex = -1; colIndex = -1; } }
                else if (r.LocalName == "p") lastParagraphText = paraText.ToString();
            }
            else if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == DocumentSerializer.WP
                     && (r.LocalName == "inline" || r.LocalName == "anchor"))
            {
                // An empty <wp:inline/> leaves the cursor on itself after ReadSubtree; step past
                // it explicitly or the loop would revisit it forever.
                bool emptyDrawing = r.IsEmptyElement;
                var info = ParseDrawing(r, relToMedia, host, ct,
                    tblDepth >= 1 ? tableIndex : (int?)null,
                    tblDepth >= 1 ? rowIndex : (int?)null,
                    tblDepth >= 1 ? colIndex : (int?)null,
                    paraText.Length > 0 ? paraText.ToString() : lastParagraphText);
                if (info is not null) sink.Add(info);
                if (emptyDrawing && !r.Read()) break;
                continue; // ParseDrawing consumed the subtree
            }
            if (!r.Read()) break;
        }
    }

    private readonly Dictionary<string, byte[]> _mediaBytes = new(StringComparer.Ordinal);

    /// <summary>Parses a wp:inline or wp:anchor element into an <see cref="ImageInfo"/>.</summary>
    private ImageInfo? ParseDrawing(XmlReader r, Dictionary<string, string> relToMedia, ImageHost host, CancellationToken ct,
        int? tableIndex, int? rowIndex, int? colIndex, string? anchorText)
    {
        // Same parser as Document.Load: full anchor geometry (distances, wrap side and polygon,
        // simple position, layout in cell), crop, rotation and flips.
        var pic = DrawingXml.Read(r);
        // Pictures; for groups and text boxes holding pictures, their first picture (as before 1.5).
        var embedRelId = pic.IsPicture ? pic.EmbedRelId : pic.FirstBlipRelId;
        if (embedRelId is null) return null;
        bool floating = pic.Placement == ImagePlacement.Floating;
        var pos = pic.Position;
        long cx = (long)Math.Round(pic.WidthPt * DocumentSerializer.EmuPerPoint), cy = (long)Math.Round(pic.HeightPt * DocumentSerializer.EmuPerPoint);
        var altText = pic.AltText;

        if (embedRelId is null || !relToMedia.TryGetValue(embedRelId, out var mediaUri))
            return null;

        // Media bytes go through the same per-part cap and cancellation check as XML parts.
        // A missing or unreadable media part skips the image; limit breaches and
        // cancellation propagate.
        // One byte array per media part, however many drawings show it.
        if (!_mediaBytes.TryGetValue(mediaUri, out var bytes))
        {
            Stream? ps = TryOpenPart(mediaUri, ct);
            if (ps is null) return null;
            using (ps)
            {
                using var ms = new MemoryStream();
                ps.CopyTo(ms);
                bytes = ms.ToArray();
            }
            _mediaBytes[mediaUri] = bytes;
        }

        var contentType = ImageMetadata.DetectContentType(bytes)
            ?? ImageMetadata.ContentTypeForExtension(Path.GetExtension(mediaUri))
            ?? "application/octet-stream";

        return new ImageInfo
        {
            RelationshipId = embedRelId,
            ContentType = contentType,
            FileName = mediaUri.Contains('/') ? mediaUri[(mediaUri.LastIndexOf('/') + 1)..] : mediaUri,
            Bytes = bytes,
            WidthPt = cx / (double)DocumentSerializer.EmuPerPoint,
            HeightPt = cy / (double)DocumentSerializer.EmuPerPoint,
            AltText = altText,
            Placement = floating ? ImagePlacement.Floating : ImagePlacement.Inline,
            Position = pos,
            Host = host,
            TableIndex = tableIndex,
            TableRow = rowIndex,
            TableColumn = colIndex,
            AnchorParagraphText = anchorText,
            Crop = pic.Crop,
            RotationDegrees = pic.RotationDegrees,
            FlipHorizontal = pic.FlipH,
            FlipVertical = pic.FlipV,
            EffectExtent = pic.EffectExtent,
        };
    }

}
