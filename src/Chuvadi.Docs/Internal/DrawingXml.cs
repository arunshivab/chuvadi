using System;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// DrawingML pictures ⇄ <see cref="ImageSpec"/>: wp:inline / wp:anchor with a pic:pic graphic —
/// size, effect extent, distances, position (offsets, alignments, simple position), wrap (type,
/// side, polygon), behind/in front, z-order, lock, overlap, layout in cell, docPr name/title/
/// description/hidden, crop, rotation and flips. Shared by the document writer, the loader,
/// the template engine and DocxReader.
/// </summary>
internal static class DrawingXml
{
    private const string W = DocumentSerializer.W;
    private const string WP = DocumentSerializer.WP;
    private const string A = DocumentSerializer.A;
    private const string PIC = DocumentSerializer.PIC;
    private const string R = DocumentSerializer.R;
    private const long Emu = DocumentSerializer.EmuPerPoint;

    internal const string Wp14 = "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing";
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    // =====================================================================================
    // Writer
    // =====================================================================================

    /// <summary>Writes <c>w:r/w:drawing</c> for an image whose media has relationship <paramref name="relId"/>.</summary>

    /// <summary>
    /// Writes <c>w:r/w:drawing</c>. <paramref name="relId"/> is the embedded media relationship (null for a
    /// picture that is only linked), <paramref name="linkRelId"/> the external link relationship (or null).
    /// Percentage positions/sizes (Word 2010+) are written as Word does: mc:AlternateContent with a wp14
    /// Choice and an absolute Fallback.
    /// </summary>
    public static void WriteRun(XmlWriter x, ImageSpec image, string? relId, int docPrId, string? linkRelId = null)
    {
        x.WriteStartElement("r", W);
        if (NeedsWp14(image))
        {
            x.WriteStartElement("mc", "AlternateContent", Mc);
            x.WriteStartElement("mc", "Choice", Mc);
            x.WriteAttributeString("xmlns", "wp14", null, Wp14);
            x.WriteAttributeString("Requires", "wp14");
            x.WriteStartElement("drawing", W);
            WriteDrawing(x, image, relId, docPrId, linkRelId, wp14: true);
            x.WriteEndElement();
            x.WriteEndElement(); // Choice
            x.WriteStartElement("mc", "Fallback", Mc);
            x.WriteStartElement("drawing", W);
            WriteDrawing(x, image, relId, docPrId, linkRelId, wp14: false);
            x.WriteEndElement();
            x.WriteEndElement(); // Fallback
            x.WriteEndElement(); // AlternateContent
        }
        else
        {
            x.WriteStartElement("drawing", W);
            WriteDrawing(x, image, relId, docPrId, linkRelId, wp14: false);
            x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    internal static bool NeedsWp14(ImageSpec image)
        => image.Placement == ImagePlacement.Floating && image.Position is { } p
           && (p.HorizontalOffsetPercent is not null || p.VerticalOffsetPercent is not null || image.RelativeWidth is not null || image.RelativeHeight is not null);

    public static void WriteDrawing(XmlWriter x, ImageSpec image, string? relId, int docPrId, string? linkRelId = null, bool wp14 = false)
    {
        if (!double.IsFinite(image.WidthPt) || !double.IsFinite(image.HeightPt) || image.WidthPt < 0 || image.HeightPt < 0)
            throw new ArgumentOutOfRangeException(nameof(image), "Image size must be finite and not negative.");
        long cx = E(image.WidthPt), cy = E(image.HeightPt);
        string name = image.Name ?? $"Picture {docPrId}";
        if (image.Placement == ImagePlacement.Floating && image.Position is { } pos)
        {
            var (t, b, l, r) = pos.EffectiveDistances;
            x.WriteStartElement("wp", "anchor", WP);
            x.WriteAttributeString("distT", Str(E(t)));
            x.WriteAttributeString("distB", Str(E(b)));
            x.WriteAttributeString("distL", Str(E(l)));
            x.WriteAttributeString("distR", Str(E(r)));
            x.WriteAttributeString("simplePos", pos.UseSimplePosition ? "1" : "0");
            x.WriteAttributeString("relativeHeight", Str(pos.RelativeHeight ?? (251658240L + docPrId)));
            x.WriteAttributeString("behindDoc", pos.BehindText ? "1" : "0");
            x.WriteAttributeString("locked", pos.LockAnchor ? "1" : "0");
            x.WriteAttributeString("layoutInCell", pos.LayoutInCell ? "1" : "0");
            x.WriteAttributeString("allowOverlap", pos.AllowOverlap ? "1" : "0");
            if (image.Hidden) x.WriteAttributeString("hidden", "1");

            x.WriteStartElement("wp", "simplePos", WP);
            x.WriteAttributeString("x", Str(pos.UseSimplePosition ? E(pos.SimplePositionXPt) : 0));
            x.WriteAttributeString("y", Str(pos.UseSimplePosition ? E(pos.SimplePositionYPt) : 0));
            x.WriteEndElement();

            x.WriteStartElement("wp", "positionH", WP);
            x.WriteAttributeString("relativeFrom", HAnchor(pos.HorizontalAnchor));
            if (pos.HAlign is HorizontalAlignment ha) x.WriteElementString("wp", "align", WP, HAlign(ha));
            else if (wp14 && pos.HorizontalOffsetPercent is double hp) x.WriteElementString("wp14", "pctPosHOffset", Wp14, Str(Pct1000(hp)));
            else x.WriteElementString("wp", "posOffset", WP, Str(E(pos.HorizontalOffsetPt)));
            x.WriteEndElement();
            x.WriteStartElement("wp", "positionV", WP);
            x.WriteAttributeString("relativeFrom", VAnchor(pos.VerticalAnchor));
            if (pos.VAlign is VerticalAlignment va) x.WriteElementString("wp", "align", WP, VAlign(va));
            else if (wp14 && pos.VerticalOffsetPercent is double vp) x.WriteElementString("wp14", "pctPosVOffset", Wp14, Str(Pct1000(vp)));
            else x.WriteElementString("wp", "posOffset", WP, Str(E(pos.VerticalOffsetPt)));
            x.WriteEndElement();

            Extent(x, cx, cy);
            EffectExtent(x, image.EffectExtent);
            WriteWrap(x, pos);
            DocPr(x, image, docPrId, name);
            FrameLocks(x);
            Graphic(x, image, cx, cy, name, relId, linkRelId);
            if (wp14)
            {
                if (image.RelativeWidth is { } rw) SizeRel(x, "sizeRelH", "pctWidth", rw);
                if (image.RelativeHeight is { } rh) SizeRel(x, "sizeRelV", "pctHeight", rh);
            }
            x.WriteEndElement(); // anchor
        }
        else
        {
            var d = image.InlineDistance;
            x.WriteStartElement("wp", "inline", WP);
            x.WriteAttributeString("distT", Str(E(d?.TopPt ?? 0)));
            x.WriteAttributeString("distB", Str(E(d?.BottomPt ?? 0)));
            x.WriteAttributeString("distL", Str(E(d?.LeftPt ?? 0)));
            x.WriteAttributeString("distR", Str(E(d?.RightPt ?? 0)));
            Extent(x, cx, cy);
            EffectExtent(x, image.EffectExtent);
            DocPr(x, image, docPrId, name);
            FrameLocks(x);
            Graphic(x, image, cx, cy, name, relId, linkRelId);
            x.WriteEndElement(); // inline
        }
    }

    private static void SizeRel(XmlWriter x, string element, string inner, RelativeSize size)
    {
        x.WriteStartElement("wp14", element, Wp14);
        x.WriteAttributeString("relativeFrom", size.RelativeTo switch
        {
            RelativeSizeFrame.Page => "page", RelativeSizeFrame.Margin => "margin", RelativeSizeFrame.LeftMargin => "leftMargin",
            RelativeSizeFrame.RightMargin => "rightMargin", RelativeSizeFrame.InsideMargin => "insideMargin",
            RelativeSizeFrame.OutsideMargin => "outsideMargin", RelativeSizeFrame.TopMargin => "topMargin",
            RelativeSizeFrame.BottomMargin => "bottomMargin",
            _ => throw new ArgumentOutOfRangeException(nameof(size), size.RelativeTo, "Undefined RelativeSizeFrame value."),
        });
        x.WriteElementString("wp14", inner, Wp14, Str(Pct1000(size.Percent)));
        x.WriteEndElement();
    }

    private static void WriteWrap(XmlWriter x, FloatingPosition pos)
    {
        string side = pos.WrapSide switch
        {
            WrapSide.BothSides => "bothSides",
            WrapSide.Left => "left",
            WrapSide.Right => "right",
            WrapSide.Largest => "largest",
            _ => throw new ArgumentOutOfRangeException(nameof(pos), pos.WrapSide, "Undefined WrapSide value."),
        };
        switch (pos.Wrap)
        {
            case TextWrap.None:
                x.WriteStartElement("wp", "wrapNone", WP);
                x.WriteEndElement();
                break;
            case TextWrap.Square:
                x.WriteStartElement("wp", "wrapSquare", WP);
                x.WriteAttributeString("wrapText", side);
                x.WriteEndElement();
                break;
            case TextWrap.Tight:
            case TextWrap.Through:
                x.WriteStartElement("wp", pos.Wrap == TextWrap.Tight ? "wrapTight" : "wrapThrough", WP);
                x.WriteAttributeString("wrapText", side);
                Polygon(x, pos.WrapPolygon ?? WrapPolygon.Rectangle());
                x.WriteEndElement();
                break;
            case TextWrap.TopAndBottom:
                x.WriteStartElement("wp", "wrapTopAndBottom", WP);
                x.WriteEndElement();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pos), pos.Wrap, "Undefined TextWrap value.");
        }
    }

    private static void Polygon(XmlWriter x, WrapPolygon polygon)
    {
        if (polygon.Points.Count < 3) polygon = WrapPolygon.Rectangle(); // the schema needs a start and two lines
        x.WriteStartElement("wp", "wrapPolygon", WP);
        x.WriteAttributeString("edited", polygon.Edited ? "1" : "0");
        for (int i = 0; i < polygon.Points.Count; i++)
        {
            x.WriteStartElement("wp", i == 0 ? "start" : "lineTo", WP);
            x.WriteAttributeString("x", Str(polygon.Points[i].X));
            x.WriteAttributeString("y", Str(polygon.Points[i].Y));
            x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    private static void Extent(XmlWriter x, long cx, long cy)
    {
        x.WriteStartElement("wp", "extent", WP);
        x.WriteAttributeString("cx", Str(cx));
        x.WriteAttributeString("cy", Str(cy));
        x.WriteEndElement();
    }

    private static void EffectExtent(XmlWriter x, ImageEffectExtent? e)
    {
        x.WriteStartElement("wp", "effectExtent", WP);
        x.WriteAttributeString("l", Str(E(e?.LeftPt ?? 0)));
        x.WriteAttributeString("t", Str(E(e?.TopPt ?? 0)));
        x.WriteAttributeString("r", Str(E(e?.RightPt ?? 0)));
        x.WriteAttributeString("b", Str(E(e?.BottomPt ?? 0)));
        x.WriteEndElement();
    }

    private static void DocPr(XmlWriter x, ImageSpec image, int id, string name)
    {
        x.WriteStartElement("wp", "docPr", WP);
        x.WriteAttributeString("id", Str(id));
        x.WriteAttributeString("name", name);
        if (!string.IsNullOrEmpty(image.AltText)) x.WriteAttributeString("descr", image.AltText);
        if (image.Hidden && image.Placement != ImagePlacement.Floating) x.WriteAttributeString("hidden", "1");
        if (!string.IsNullOrEmpty(image.Title)) x.WriteAttributeString("title", image.Title);
        x.WriteEndElement();
    }

    private static void FrameLocks(XmlWriter x)
    {
        x.WriteStartElement("wp", "cNvGraphicFramePr", WP);
        x.WriteStartElement("a", "graphicFrameLocks", A);
        x.WriteAttributeString("xmlns", "a", null, A);
        x.WriteAttributeString("noChangeAspect", "1");
        x.WriteEndElement();
        x.WriteEndElement();
    }

    private static void Graphic(XmlWriter x, ImageSpec image, long cx, long cy, string name, string? relId, string? linkRelId)
    {
        x.WriteStartElement("a", "graphic", A);
        x.WriteAttributeString("xmlns", "a", null, A);
        x.WriteStartElement("a", "graphicData", A);
        x.WriteAttributeString("uri", PIC);
        x.WriteStartElement("pic", "pic", PIC);
        x.WriteAttributeString("xmlns", "pic", null, PIC);

        x.WriteStartElement("pic", "nvPicPr", PIC);
        x.WriteStartElement("pic", "cNvPr", PIC);
        x.WriteAttributeString("id", "0");
        x.WriteAttributeString("name", name);
        x.WriteEndElement();
        x.WriteStartElement("pic", "cNvPicPr", PIC);
        x.WriteEndElement();
        x.WriteEndElement(); // nvPicPr

        x.WriteStartElement("pic", "blipFill", PIC);
        x.WriteStartElement("a", "blip", A);
        if (relId is not null) x.WriteAttributeString("r", "embed", R, relId);
        if (linkRelId is not null) x.WriteAttributeString("r", "link", R, linkRelId);
        x.WriteEndElement();
        if (image.Crop is { } c)
        {
            // a:srcRect in thousandths of a percent.
            x.WriteStartElement("a", "srcRect", A);
            if (c.Left != 0) x.WriteAttributeString("l", Str(Pct(c.Left)));
            if (c.Top != 0) x.WriteAttributeString("t", Str(Pct(c.Top)));
            if (c.Right != 0) x.WriteAttributeString("r", Str(Pct(c.Right)));
            if (c.Bottom != 0) x.WriteAttributeString("b", Str(Pct(c.Bottom)));
            x.WriteEndElement();
        }
        x.WriteStartElement("a", "stretch", A);
        x.WriteStartElement("a", "fillRect", A);
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndElement(); // blipFill

        x.WriteStartElement("pic", "spPr", PIC);
        x.WriteStartElement("a", "xfrm", A);
        if (!double.IsFinite(image.RotationDegrees)) throw new ArgumentOutOfRangeException(nameof(image), "Rotation must be finite.");
        long rot = (long)Math.Round(((image.RotationDegrees % 360) + 360) % 360 * 60000);
        if (rot != 0) x.WriteAttributeString("rot", Str(rot));
        if (image.FlipHorizontal) x.WriteAttributeString("flipH", "1");
        if (image.FlipVertical) x.WriteAttributeString("flipV", "1");
        x.WriteStartElement("a", "off", A);
        x.WriteAttributeString("x", "0");
        x.WriteAttributeString("y", "0");
        x.WriteEndElement();
        x.WriteStartElement("a", "ext", A);
        x.WriteAttributeString("cx", Str(cx));
        x.WriteAttributeString("cy", Str(cy));
        x.WriteEndElement();
        x.WriteEndElement(); // xfrm
        x.WriteStartElement("a", "prstGeom", A);
        x.WriteAttributeString("prst", "rect");
        x.WriteStartElement("a", "avLst", A);
        x.WriteEndElement();
        x.WriteEndElement();
        x.WriteEndElement(); // spPr

        x.WriteEndElement(); // pic
        x.WriteEndElement(); // graphicData
        x.WriteEndElement(); // graphic
    }

    // =====================================================================================
    // Reader
    // =====================================================================================

    /// <summary>A picture read from a drawing, before its media bytes are resolved.</summary>
    internal sealed class Picture
    {
        public string? EmbedRelId, LinkRelId;
        /// <summary>The first blip anywhere in the drawing (groups, text boxes) — for DocxReader's image listing.</summary>
        public string? FirstBlipRelId;
        public double WidthPt, HeightPt;
        public string? AltText, Name, Title;
        public bool Hidden;
        public ImagePlacement Placement;
        public FloatingPosition? Position;
        public ImageCrop? Crop;
        public double RotationDegrees;
        public bool FlipH, FlipV;
        public ImageEffectExtent? EffectExtent;
        public ImageEffectExtent? InlineDistance;
        public RelativeSize? RelativeWidth, RelativeHeight;
        /// <summary>True for a top-level pic:pic graphic with an embedded or linked blip.</summary>
        public bool IsPicture;
        /// <summary>Text inside the drawing (text boxes in shapes), for drawings that are not pictures.</summary>
        public string Text = "";

        public ImageSpec ToImage(byte[] bytes, string contentType, string? linkUrl = null) => new()
        {
            Bytes = bytes, ContentType = contentType, WidthPt = WidthPt, HeightPt = HeightPt, AltText = AltText,
            Name = Name, Title = Title, Hidden = Hidden, Placement = Placement, Position = Position, Crop = Crop,
            RotationDegrees = RotationDegrees, FlipHorizontal = FlipH, FlipVertical = FlipV, EffectExtent = EffectExtent,
            InlineDistance = InlineDistance, RelativeWidth = RelativeWidth, RelativeHeight = RelativeHeight, LinkUrl = linkUrl,
        };
    }

    /// <summary>
    /// Reads a wp:inline or wp:anchor subtree (reader on it). Only the drawing's own properties are
    /// read (direct children of the anchor, the top-level graphic's picture): a picture inside a text
    /// box or group does not make the drawing a picture. <see cref="Picture.IsPicture"/> is false for
    /// shapes, charts, SmartArt, text boxes and groups; their text is in <see cref="Picture.Text"/>.
    /// Malformed content inside property elements is tolerated (the property is ignored).
    /// </summary>
    public static Picture Read(XmlReader r)
    {
        var p = new Picture { Placement = r.LocalName == "anchor" ? ImagePlacement.Floating : ImagePlacement.Inline };
        FloatingPosition? pos = null;
        double? dT = NonNeg(Dist(r.GetAttribute("distT"))), dB = NonNeg(Dist(r.GetAttribute("distB"))),
                dL = NonNeg(Dist(r.GetAttribute("distL"))), dR = NonNeg(Dist(r.GetAttribute("distR")));
        if (p.Placement == ImagePlacement.Floating)
        {
            pos = new FloatingPosition
            {
                BehindText = r.GetAttribute("behindDoc") is "1" or "true",
                AllowOverlap = r.GetAttribute("allowOverlap") is not ("0" or "false"),
                LockAnchor = r.GetAttribute("locked") is "1" or "true",
                LayoutInCell = r.GetAttribute("layoutInCell") is not ("0" or "false"),
                UseSimplePosition = r.GetAttribute("simplePos") is "1" or "true",
                RelativeHeight = long.TryParse(r.GetAttribute("relativeHeight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rh) && rh >= 0 ? rh : null,
                DistanceTopPt = dT, DistanceBottomPt = dB, DistanceLeftPt = dL, DistanceRightPt = dR,
                Wrap = TextWrap.None,
            };
            p.Hidden = r.GetAttribute("hidden") is "1" or "true";
            p.Position = pos;
        }
        else if ((dT ?? 0) != 0 || (dB ?? 0) != 0 || (dL ?? 0) != 0 || (dR ?? 0) != 0)
        {
            p.InlineDistance = new ImageEffectExtent(dL ?? 0, dT ?? 0, dR ?? 0, dB ?? 0);
        }

        // Depths inside the subtree: 0 = anchor/inline, 1 = its children (extent, docPr, wrap…),
        // 2 = a:graphicData, 3 = pic:pic, 4 = blipFill / spPr, 5 = blip, srcRect, xfrm.
        bool isPicture = false, inTopPic = false;
        var text = new System.Text.StringBuilder();
        using var sub = r.ReadSubtree();
        sub.Read();
        bool advance = true;
        while (true)
        {
            if (advance && !sub.Read()) break;
            advance = true;
            if (sub.EOF) break;
            if (sub.NodeType != XmlNodeType.Element) continue;
            int depth = sub.Depth;
            if (depth <= 3) inTopPic = inTopPic && depth > 3; // leaving pic:pic
            if (sub.LocalName == "Fallback" && sub.NamespaceURI == Mc)
            {
                sub.Skip(); // duplicates the Choice
                advance = false;
                continue;
            }
            if (sub.NamespaceURI == W && sub.LocalName == "t")
            {
                text.Append(sub.ReadElementContentAsString());
                advance = false;
                continue;
            }
            if (sub.NamespaceURI == A && sub.LocalName == "blip")
            {
                p.FirstBlipRelId ??= sub.GetAttribute("embed", R) ?? sub.GetAttribute("link", R);
            }
            if (depth == 1 && sub.NamespaceURI == WP)
            {
                switch (sub.LocalName)
                {
                    case "simplePos" when pos is not null:
                        pos.SimplePositionXPt = Dist(sub.GetAttribute("x")) ?? 0;
                        pos.SimplePositionYPt = Dist(sub.GetAttribute("y")) ?? 0;
                        break;
                    case "positionH" when pos is not null:
                        pos.HorizontalAnchor = ParseHAnchor(sub.GetAttribute("relativeFrom"));
                        using (var ph = sub.ReadSubtree()) ReadPosition(ph, true, pos);
                        break;
                    case "positionV" when pos is not null:
                        pos.VerticalAnchor = ParseVAnchor(sub.GetAttribute("relativeFrom"));
                        using (var pv = sub.ReadSubtree()) ReadPosition(pv, false, pos);
                        break;
                    case "extent":
                        p.WidthPt = NonNeg(Dist(sub.GetAttribute("cx"))) ?? 0;
                        p.HeightPt = NonNeg(Dist(sub.GetAttribute("cy"))) ?? 0;
                        break;
                    case "effectExtent":
                    {
                        var e = new ImageEffectExtent(Dist(sub.GetAttribute("l")) ?? 0, Dist(sub.GetAttribute("t")) ?? 0,
                            Dist(sub.GetAttribute("r")) ?? 0, Dist(sub.GetAttribute("b")) ?? 0);
                        if (e.LeftPt != 0 || e.TopPt != 0 || e.RightPt != 0 || e.BottomPt != 0) p.EffectExtent = e;
                        break;
                    }
                    case "wrapNone" when pos is not null: pos.Wrap = TextWrap.None; break;
                    case "wrapSquare" when pos is not null: pos.Wrap = TextWrap.Square; pos.WrapSide = Side(sub.GetAttribute("wrapText")); break;
                    case "wrapTight" when pos is not null:
                    case "wrapThrough" when pos is not null:
                    {
                        pos.Wrap = sub.LocalName == "wrapTight" ? TextWrap.Tight : TextWrap.Through;
                        pos.WrapSide = Side(sub.GetAttribute("wrapText"));
                        using (var wr = sub.ReadSubtree()) ReadPolygon(wr, pos);
                        break;
                    }
                    case "wrapTopAndBottom" when pos is not null: pos.Wrap = TextWrap.TopAndBottom; break;
                    case "docPr":
                        p.AltText = sub.GetAttribute("descr");
                        p.Name = sub.GetAttribute("name");
                        p.Title = sub.GetAttribute("title");
                        if (sub.GetAttribute("hidden") is "1" or "true") p.Hidden = true;
                        break;
                }
            }
            else if (depth == 1 && sub.NamespaceURI == Wp14 && sub.LocalName is "sizeRelH" or "sizeRelV")
            {
                var frame = ParseSizeFrame(sub.GetAttribute("relativeFrom"));
                bool horizontal = sub.LocalName == "sizeRelH";
                using var sr = sub.ReadSubtree();
                while (sr.Read())
                    if (sr.NodeType == XmlNodeType.Element && sr.NamespaceURI == Wp14 && sr.LocalName is "pctWidth" or "pctHeight"
                        && long.TryParse(TextOf(sr), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct) && Math.Abs(pct) <= 1_000_000_000)
                    {
                        var size = new RelativeSize(pct / 1000.0, frame);
                        if (horizontal) p.RelativeWidth = size; else p.RelativeHeight = size;
                    }
            }
            else if (sub.NamespaceURI == A && depth == 2 && sub.LocalName == "graphicData")
            {
                isPicture = sub.GetAttribute("uri") == PIC;
            }
            else if (isPicture && depth == 3 && sub.NamespaceURI == PIC && sub.LocalName == "pic")
            {
                inTopPic = true;
            }
            else if (inTopPic && depth == 5 && sub.NamespaceURI == A)
            {
                switch (sub.LocalName)
                {
                    case "blip":
                        p.EmbedRelId ??= sub.GetAttribute("embed", R);
                        p.LinkRelId ??= sub.GetAttribute("link", R);
                        break;
                    case "srcRect":
                    {
                        double F(string n) => int.TryParse(sub.GetAttribute(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, -1_000_000, 100_000) / 100_000.0 : 0;
                        var (l, t, rr, b) = (F("l"), F("t"), F("r"), F("b"));
                        // Never leave nothing visible: opposite sides together stay below 100%.
                        if (l + rr >= 1) { var k = 0.99 / (l + rr); l *= k; rr *= k; }
                        if (t + b >= 1) { var k = 0.99 / (t + b); t *= k; b *= k; }
                        var c = new ImageCrop(l, t, rr, b);
                        if (c.Left != 0 || c.Top != 0 || c.Right != 0 || c.Bottom != 0) p.Crop = c;
                        break;
                    }
                    case "xfrm":
                        if (long.TryParse(sub.GetAttribute("rot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rot)) p.RotationDegrees = (rot % 21_600_000) / 60000.0;
                        p.FlipH = sub.GetAttribute("flipH") is "1" or "true";
                        p.FlipV = sub.GetAttribute("flipV") is "1" or "true";
                        break;
                }
            }
        }
        p.Text = text.ToString();
        p.IsPicture = isPicture && (!string.IsNullOrEmpty(p.EmbedRelId) || !string.IsNullOrEmpty(p.LinkRelId));
        // "Picture N" is the default name (Word's and the writer's): keep it automatic so ids renumber cleanly.
        if (p.Name is not null && p.Name.StartsWith("Picture ", StringComparison.Ordinal) && int.TryParse(p.Name.AsSpan(8), out _)) p.Name = null;
        return p;
    }

    private static void ReadPolygon(XmlReader wr, FloatingPosition pos)
    {
        while (wr.Read())
        {
            if (wr.NodeType != XmlNodeType.Element || wr.NamespaceURI != WP || wr.LocalName != "wrapPolygon") continue;
            var poly = new WrapPolygon { Edited = wr.GetAttribute("edited") is "1" or "true" };
            using (var pg = wr.ReadSubtree())
                while (pg.Read())
                    if (pg.NodeType == XmlNodeType.Element && pg.NamespaceURI == WP && pg.LocalName is "start" or "lineTo" && poly.Points.Count < 10_000
                        && long.TryParse(pg.GetAttribute("x"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var px)
                        && long.TryParse(pg.GetAttribute("y"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var py))
                        poly.Points.Add((px, py));
            if (poly.Points.Count >= 3) pos.WrapPolygon = poly;
        }
    }

    private static void ReadPosition(XmlReader sub, bool horizontal, FloatingPosition pos)
    {
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element) continue;
            if (sub.NamespaceURI == WP && sub.LocalName == "posOffset")
            {
                var pt = Dist(TextOf(sub)) ?? 0;
                if (horizontal) pos.HorizontalOffsetPt = pt; else pos.VerticalOffsetPt = pt;
            }
            else if (sub.NamespaceURI == Wp14 && sub.LocalName is "pctPosHOffset" or "pctPosVOffset")
            {
                if (long.TryParse(TextOf(sub).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct) && Math.Abs(pct) <= 1_000_000_000)
                {
                    if (horizontal) pos.HorizontalOffsetPercent = pct / 1000.0; else pos.VerticalOffsetPercent = pct / 1000.0;
                }
            }
            else if (sub.NamespaceURI == WP && sub.LocalName == "align")
            {
                var text = TextOf(sub).Trim();
                if (horizontal)
                    pos.HAlign = text switch
                    {
                        "left" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center, "right" => HorizontalAlignment.Right,
                        "inside" => HorizontalAlignment.Inside, "outside" => HorizontalAlignment.Outside, _ => null,
                    };
                else
                    pos.VAlign = text switch
                    {
                        "top" => VerticalAlignment.Top, "center" => VerticalAlignment.Center, "bottom" => VerticalAlignment.Bottom,
                        "inside" => VerticalAlignment.Inside, "outside" => VerticalAlignment.Outside, _ => null,
                    };
            }
        }
    }

    /// <summary>The text content of the current element, tolerating (skipping) child elements; leaves the reader inside it.</summary>
    private static string TextOf(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var sb = new System.Text.StringBuilder();
        using var inner = r.ReadSubtree();
        inner.Read();
        while (inner.Read())
            if (inner.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                sb.Append(inner.Value);
        return sb.ToString();
    }

    private static RelativeSizeFrame ParseSizeFrame(string? v) => v switch
    {
        "margin" => RelativeSizeFrame.Margin, "leftMargin" => RelativeSizeFrame.LeftMargin, "rightMargin" => RelativeSizeFrame.RightMargin,
        "insideMargin" => RelativeSizeFrame.InsideMargin, "outsideMargin" => RelativeSizeFrame.OutsideMargin,
        "topMargin" => RelativeSizeFrame.TopMargin, "bottomMargin" => RelativeSizeFrame.BottomMargin, _ => RelativeSizeFrame.Page,
    };

    private static double? NonNeg(double? v) => v is double d && d >= 0 ? d : null;

    // =====================================================================================
    // Tokens and units
    // =====================================================================================

    internal static HorizontalAnchor ParseHAnchor(string? v) => v switch
    {
        "page" => HorizontalAnchor.Page, "margin" => HorizontalAnchor.Margin, "column" => HorizontalAnchor.Column,
        "character" => HorizontalAnchor.Character, "leftMargin" => HorizontalAnchor.LeftMargin, "rightMargin" => HorizontalAnchor.RightMargin,
        "insideMargin" => HorizontalAnchor.InsideMargin, "outsideMargin" => HorizontalAnchor.OutsideMargin, _ => HorizontalAnchor.Column,
    };

    internal static VerticalAnchor ParseVAnchor(string? v) => v switch
    {
        "page" => VerticalAnchor.Page, "margin" => VerticalAnchor.Margin, "line" => VerticalAnchor.Line, "paragraph" => VerticalAnchor.Paragraph,
        "topMargin" => VerticalAnchor.TopMargin, "bottomMargin" => VerticalAnchor.BottomMargin, "insideMargin" => VerticalAnchor.InsideMargin,
        "outsideMargin" => VerticalAnchor.OutsideMargin, _ => VerticalAnchor.Paragraph,
    };

    private static string HAnchor(HorizontalAnchor a) => a switch
    {
        HorizontalAnchor.Page => "page", HorizontalAnchor.Margin => "margin", HorizontalAnchor.Column => "column",
        HorizontalAnchor.Character => "character", HorizontalAnchor.LeftMargin => "leftMargin", HorizontalAnchor.RightMargin => "rightMargin",
        HorizontalAnchor.InsideMargin => "insideMargin", HorizontalAnchor.OutsideMargin => "outsideMargin",
        _ => throw new ArgumentOutOfRangeException(nameof(a), a, "Undefined HorizontalAnchor value."),
    };

    private static string VAnchor(VerticalAnchor a) => a switch
    {
        VerticalAnchor.Page => "page", VerticalAnchor.Margin => "margin", VerticalAnchor.Line => "line", VerticalAnchor.Paragraph => "paragraph",
        VerticalAnchor.TopMargin => "topMargin", VerticalAnchor.BottomMargin => "bottomMargin", VerticalAnchor.InsideMargin => "insideMargin",
        VerticalAnchor.OutsideMargin => "outsideMargin",
        _ => throw new ArgumentOutOfRangeException(nameof(a), a, "Undefined VerticalAnchor value."),
    };

    private static string HAlign(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Left => "left", HorizontalAlignment.Center => "center", HorizontalAlignment.Right => "right",
        HorizontalAlignment.Inside => "inside", HorizontalAlignment.Outside => "outside",
        _ => throw new ArgumentOutOfRangeException(nameof(a), a, "Undefined HorizontalAlignment value."),
    };

    private static string VAlign(VerticalAlignment a) => a switch
    {
        VerticalAlignment.Top => "top", VerticalAlignment.Center => "center", VerticalAlignment.Bottom => "bottom",
        VerticalAlignment.Inside => "inside", VerticalAlignment.Outside => "outside",
        _ => throw new ArgumentOutOfRangeException(nameof(a), a, "Undefined VerticalAlignment value."),
    };

    private static WrapSide Side(string? v) => v switch
    {
        "left" => WrapSide.Left, "right" => WrapSide.Right, "largest" => WrapSide.Largest, _ => WrapSide.BothSides,
    };

    /// <summary>Points → EMU; non-finite or absurd values are a caller error.</summary>
    private static long E(double pt)
    {
        double emu = Math.Round(pt * Emu);
        if (!double.IsFinite(emu) || Math.Abs(emu) > 27_273_042_316_900)
            throw new ArgumentOutOfRangeException(nameof(pt), pt, "Drawing measurement out of range.");
        return (long)emu;
    }

    /// <summary>EMU text → points; null for missing, non-numeric or absurd values (hostile input).</summary>
    private static double? Dist(string? v)
        => long.TryParse(v?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu) && Math.Abs(emu) <= 27_273_042_316_900
            ? emu / (double)Emu : null;

    /// <summary>Percent → thousandths of a percent (wp14 percentages).</summary>
    private static long Pct1000(double percent)
    {
        if (!double.IsFinite(percent) || Math.Abs(percent) > 1_000_000) throw new ArgumentOutOfRangeException(nameof(percent), "Percentage out of range.");
        return (long)Math.Round(percent * 1000);
    }

    private static long Pct(double fraction)
    {
        if (!double.IsFinite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction), "Crop must be finite.");
        return (long)Math.Round(fraction * 100_000);
    }

    private static string Str(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}
