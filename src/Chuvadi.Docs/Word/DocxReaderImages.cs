using System;
using System.Collections.Generic;
using System.IO;
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
    public IReadOnlyList<ImageInfo> Images()
    {
        EnsureNotDisposed();
        var result = new List<ImageInfo>();

        CollectImagesFromPart(_documentUri, ImageHost.Body, result);
        if (_options.IncludeHeadersAndFooters)
        {
            foreach (var uri in _headerUris) CollectImagesFromPart(uri, ImageHost.Header, result);
            foreach (var uri in _footerUris) CollectImagesFromPart(uri, ImageHost.Footer, result);
        }
        return result;
    }

    /// <summary>
    /// Saves every image to <paramref name="folderPath"/> (created if needed), using each
    /// image's package file name (image1.png, image2.jpeg, ...). Returns the full paths
    /// written, in order. Duplicate file names are de-collided with a numeric suffix.
    /// </summary>
    public IReadOnlyList<string> SaveImages(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) throw new ArgumentException("Folder path required.", nameof(folderPath));
        Directory.CreateDirectory(folderPath);

        var written = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in Images())
        {
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

    private void CollectImagesFromPart(string partUri, ImageHost host, List<ImageInfo> sink)
    {
        // Resolve this part's image relationships: relId -> media part uri.
        var relToMedia = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in _package.GetRelationships(partUri))
        {
            if (rel.Type.EndsWith("/image", StringComparison.Ordinal))
                relToMedia[rel.Id] = ResolveUri(partUri, rel.Target);
        }
        if (relToMedia.Count == 0) return;

        Stream? s = TryOpenPart(partUri);
        if (s is null) return;
        using var _ = s;
        using var r = XmlReader.Create(s, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
            DtdProcessing = DtdProcessing.Prohibit,
        });

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
                var info = ParseDrawing(r, relToMedia, host,
                    tblDepth >= 1 ? tableIndex : (int?)null,
                    tblDepth >= 1 ? rowIndex : (int?)null,
                    tblDepth >= 1 ? colIndex : (int?)null,
                    paraText.Length > 0 ? paraText.ToString() : lastParagraphText);
                if (info is not null) sink.Add(info);
                continue; // ParseDrawing consumed the subtree
            }
            if (!r.Read()) break;
        }
    }

    /// <summary>Parses a wp:inline or wp:anchor element into an <see cref="ImageInfo"/>.</summary>
    private ImageInfo? ParseDrawing(XmlReader r, Dictionary<string, string> relToMedia, ImageHost host,
        int? tableIndex, int? rowIndex, int? colIndex, string? anchorText)
    {
        bool floating = r.LocalName == "anchor";
        long cx = 0, cy = 0;
        string? embedRelId = null;
        string? altText = null;

        // Floating attributes captured for FloatingPosition.
        var pos = floating ? new FloatingPosition() : null;
        if (floating)
        {
            var behind = r.GetAttribute("behindDoc");
            var allowOverlap = r.GetAttribute("allowOverlap");
            var locked = r.GetAttribute("locked");
            if (behind == "1") pos!.BehindText = true;
            pos!.AllowOverlap = allowOverlap != "0";
            pos.LockAnchor = locked == "1";
        }

        using (var sub = r.ReadSubtree())
        {
            sub.Read();
            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element) continue;

                if (sub.NamespaceURI == DocumentSerializer.WP)
                {
                    switch (sub.LocalName)
                    {
                        case "extent":
                            long.TryParse(sub.GetAttribute("cx"), out cx);
                            long.TryParse(sub.GetAttribute("cy"), out cy);
                            break;
                        case "docPr":
                            altText = sub.GetAttribute("descr");
                            break;
                        case "positionH" when pos is not null:
                            pos.HorizontalAnchor = ParseHAnchor(sub.GetAttribute("relativeFrom"));
                            ReadPosition(sub, isHorizontal: true, pos);
                            continue;
                        case "positionV" when pos is not null:
                            pos.VerticalAnchor = ParseVAnchor(sub.GetAttribute("relativeFrom"));
                            ReadPosition(sub, isHorizontal: false, pos);
                            continue;
                        case "wrapNone" when pos is not null:
                            pos.Wrap = TextWrap.None;
                            break;
                        case "wrapSquare" when pos is not null:
                            pos.Wrap = TextWrap.Square;
                            break;
                        case "wrapTight" when pos is not null:
                            pos.Wrap = TextWrap.Tight;
                            break;
                        case "wrapThrough" when pos is not null:
                            pos.Wrap = TextWrap.Through;
                            break;
                        case "wrapTopAndBottom" when pos is not null:
                            pos.Wrap = TextWrap.TopAndBottom;
                            break;
                    }
                }
                else if (sub.NamespaceURI == DocumentSerializer.A && sub.LocalName == "blip")
                {
                    embedRelId = sub.GetAttribute("embed", DocumentSerializer.R);
                }
            }
        }

        if (embedRelId is null || !relToMedia.TryGetValue(embedRelId, out var mediaUri))
            return null;

        byte[] bytes;
        try
        {
            using var ms = new MemoryStream();
            using (var ps = _package.OpenPart(mediaUri)) ps.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch
        {
            return null;
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
        };
    }

    private static void ReadPosition(XmlReader positionElem, bool isHorizontal, FloatingPosition pos)
    {
        using var sub = positionElem.ReadSubtree();
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != DocumentSerializer.WP) continue;
            if (sub.LocalName == "posOffset")
            {
                var text = sub.ReadElementContentAsString();
                if (long.TryParse(text, out var emu))
                {
                    double pt = emu / (double)DocumentSerializer.EmuPerPoint;
                    if (isHorizontal) pos.HorizontalOffsetPt = pt; else pos.VerticalOffsetPt = pt;
                }
            }
            else if (sub.LocalName == "align")
            {
                var text = sub.ReadElementContentAsString();
                if (isHorizontal)
                    pos.HAlign = text switch
                    {
                        "left" => HorizontalAlignment.Left,
                        "center" => HorizontalAlignment.Center,
                        "right" => HorizontalAlignment.Right,
                        "inside" => HorizontalAlignment.Inside,
                        "outside" => HorizontalAlignment.Outside,
                        _ => null,
                    };
                else
                    pos.VAlign = text switch
                    {
                        "top" => VerticalAlignment.Top,
                        "center" => VerticalAlignment.Center,
                        "bottom" => VerticalAlignment.Bottom,
                        "inside" => VerticalAlignment.Inside,
                        "outside" => VerticalAlignment.Outside,
                        _ => null,
                    };
            }
        }
    }

    private static HorizontalAnchor ParseHAnchor(string? v) => v switch
    {
        "page" => HorizontalAnchor.Page,
        "margin" => HorizontalAnchor.Margin,
        "column" => HorizontalAnchor.Column,
        "character" => HorizontalAnchor.Character,
        "leftMargin" => HorizontalAnchor.LeftMargin,
        "rightMargin" => HorizontalAnchor.RightMargin,
        "insideMargin" => HorizontalAnchor.InsideMargin,
        "outsideMargin" => HorizontalAnchor.OutsideMargin,
        _ => HorizontalAnchor.Column,
    };

    private static VerticalAnchor ParseVAnchor(string? v) => v switch
    {
        "page" => VerticalAnchor.Page,
        "margin" => VerticalAnchor.Margin,
        "line" => VerticalAnchor.Line,
        "paragraph" => VerticalAnchor.Paragraph,
        "topMargin" => VerticalAnchor.TopMargin,
        "bottomMargin" => VerticalAnchor.BottomMargin,
        "insideMargin" => VerticalAnchor.InsideMargin,
        "outsideMargin" => VerticalAnchor.OutsideMargin,
        _ => VerticalAnchor.Paragraph,
    };
}
