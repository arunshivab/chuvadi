using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>Whether an image flows inline with text or floats with absolute positioning.</summary>
public enum ImagePlacement
{
    /// <summary>Image sits in the text flow like a large character.</summary>
    Inline,
    /// <summary>Image is positioned absolutely; text wraps per <see cref="FloatingPosition.Wrap"/>.</summary>
    Floating,
}

/// <summary>Horizontal reference frame for a floating image's position.</summary>
public enum HorizontalAnchor
{
    Page,
    Margin,
    Column,
    Character,
    LeftMargin,
    RightMargin,
    InsideMargin,
    OutsideMargin,
}

/// <summary>Vertical reference frame for a floating image's position.</summary>
public enum VerticalAnchor
{
    Page,
    Margin,
    Line,
    Paragraph,
    TopMargin,
    BottomMargin,
    InsideMargin,
    OutsideMargin,
}

/// <summary>Named horizontal alignment for a floating image (overrides the offset when set).</summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Inside,
    Outside,
}

/// <summary>Named vertical alignment for a floating image (overrides the offset when set).</summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Inside,
    Outside,
}

/// <summary>How text wraps around a floating image.</summary>
public enum TextWrap
{
    /// <summary>No wrapping; combine with <see cref="FloatingPosition.BehindText"/> to place
    /// behind or in front of text.</summary>
    None,
    /// <summary>Text wraps in a rectangle around the image.</summary>
    Square,
    /// <summary>Text wraps tightly to the image's contours.</summary>
    Tight,
    /// <summary>Like Tight, but text can also flow through open areas.</summary>
    Through,
    /// <summary>Text appears only above and below the image.</summary>
    TopAndBottom,
}

/// <summary>
/// Absolute placement for a floating image. Either an offset (in points) from an anchor,
/// or a named alignment relative to that anchor. When an alignment is set it takes
/// precedence over the corresponding offset.
///
/// <code>
/// // Logo pinned 10pt from the top-left of the page:
/// new FloatingPosition {
///     HorizontalAnchor = HorizontalAnchor.Page, HorizontalOffsetPt = 10,
///     VerticalAnchor   = VerticalAnchor.Page,   VerticalOffsetPt   = 10,
///     Wrap = TextWrap.Square };
///
/// // Watermark centred on the page, behind the text:
/// new FloatingPosition {
///     HorizontalAnchor = HorizontalAnchor.Page, HAlign = HorizontalAlignment.Center,
///     VerticalAnchor   = VerticalAnchor.Page,   VAlign = VerticalAlignment.Center,
///     Wrap = TextWrap.None, BehindText = true };
/// </code>
/// </summary>
public sealed class FloatingPosition
{
    public HorizontalAnchor HorizontalAnchor { get; set; } = HorizontalAnchor.Column;
    public VerticalAnchor VerticalAnchor { get; set; } = VerticalAnchor.Paragraph;

    /// <summary>Horizontal offset from the anchor, in points. Ignored when <see cref="HAlign"/> is set.</summary>
    public double HorizontalOffsetPt { get; set; }

    /// <summary>Vertical offset from the anchor, in points. Ignored when <see cref="VAlign"/> is set.</summary>
    public double VerticalOffsetPt { get; set; }

    /// <summary>Named horizontal alignment; overrides <see cref="HorizontalOffsetPt"/> when set.</summary>
    public HorizontalAlignment? HAlign { get; set; }

    /// <summary>Named vertical alignment; overrides <see cref="VerticalOffsetPt"/> when set.</summary>
    public VerticalAlignment? VAlign { get; set; }

    /// <summary>Text-wrap mode around the image.</summary>
    public TextWrap Wrap { get; set; } = TextWrap.Square;

    /// <summary>Behind the text (behindDoc) rather than in front — Word's "Behind text" (with <see cref="Wrap"/> None); kept for any wrap.</summary>
    public bool BehindText { get; set; }

    /// <summary>Allow this image to overlap other floating objects.</summary>
    public bool AllowOverlap { get; set; } = true;

    /// <summary>Lock the anchor so the image doesn't move when its paragraph moves.</summary>
    public bool LockAnchor { get; set; }

    /// <summary>Z-order; higher is closer to the front. Default auto-assigns.</summary>
    public long? RelativeHeight { get; set; }

    /// <summary>Distance from text on each side, in points (wrap padding); the per-side values below override it.</summary>
    public double DistanceFromTextPt { get; set; } = 0;

    /// <summary>Distance from text above the image (wp:anchor/@distT); null = <see cref="DistanceFromTextPt"/>.</summary>
    public double? DistanceTopPt { get; set; }

    /// <summary>Distance from text below the image (@distB); null = <see cref="DistanceFromTextPt"/>.</summary>
    public double? DistanceBottomPt { get; set; }

    /// <summary>Distance from text left of the image (@distL); null = <see cref="DistanceFromTextPt"/>.</summary>
    public double? DistanceLeftPt { get; set; }

    /// <summary>Distance from text right of the image (@distR); null = <see cref="DistanceFromTextPt"/>.</summary>
    public double? DistanceRightPt { get; set; }

    /// <summary>Which sides text wraps on for Square/Tight/Through (wrapText). Default both sides.</summary>
    public WrapSide WrapSide { get; set; } = WrapSide.BothSides;

    /// <summary>The wrap outline for Tight/Through (wp:wrapPolygon); null = the image rectangle.</summary>
    public WrapPolygon? WrapPolygon { get; set; }

    /// <summary>Position relative to the table cell when the anchor is in a table (@layoutInCell). Default true.</summary>
    public bool LayoutInCell { get; set; } = true;

    /// <summary>
    /// Horizontal position as a percentage of the <see cref="HorizontalAnchor"/> frame's width
    /// (wp14:pctPosHOffset, Word 2010+), e.g. 50 = halfway. Takes precedence over
    /// <see cref="HorizontalOffsetPt"/>, which is kept as the fallback for older readers; <see cref="HAlign"/> still wins.
    /// </summary>
    public double? HorizontalOffsetPercent { get; set; }

    /// <summary>Vertical position as a percentage of the <see cref="VerticalAnchor"/> frame's height (wp14:pctPosVOffset).</summary>
    public double? VerticalOffsetPercent { get; set; }

    /// <summary>Use the simple absolute position (@simplePos, wp:simplePos) instead of positionH/positionV.</summary>
    public bool UseSimplePosition { get; set; }

    /// <summary>Simple position X from the page's top-left, in points (with <see cref="UseSimplePosition"/>).</summary>
    public double SimplePositionXPt { get; set; }

    /// <summary>Simple position Y from the page's top-left, in points (with <see cref="UseSimplePosition"/>).</summary>
    public double SimplePositionYPt { get; set; }

    /// <summary>Effective distances (per side, falling back to <see cref="DistanceFromTextPt"/>).</summary>
    public (double Top, double Bottom, double Left, double Right) EffectiveDistances
        => (DistanceTopPt ?? DistanceFromTextPt, DistanceBottomPt ?? DistanceFromTextPt, DistanceLeftPt ?? DistanceFromTextPt, DistanceRightPt ?? DistanceFromTextPt);
}

/// <summary>What a relative (percentage) picture size is measured against (wp14:sizeRelH/V/@relativeFrom).</summary>
public enum RelativeSizeFrame
{
    Page,
    Margin,
    LeftMargin,
    RightMargin,
    InsideMargin,
    OutsideMargin,
    TopMargin,
    BottomMargin,
}

/// <summary>A picture dimension as a percentage of a page frame (Word 2010+ "relative size").</summary>
/// <param name="Percent">e.g. 50 = half the frame.</param>
/// <param name="RelativeTo">The frame.</param>
public sealed record RelativeSize(double Percent, RelativeSizeFrame RelativeTo);

/// <summary>Which sides of a floating image text wraps on (ST_WrapText).</summary>
public enum WrapSide
{
    BothSides,
    Left,
    Right,
    /// <summary>Only the side with more room.</summary>
    Largest,
}

/// <summary>
/// The outline text wraps around (wp:wrapPolygon), in the image's own coordinate space of
/// 21600 × 21600 units (0,0 top-left; 21600,21600 bottom-right of the extent).
/// </summary>
public sealed class WrapPolygon
{
    /// <summary>The outline: the first point is the start, each following point a line to it.</summary>
    public List<(long X, long Y)> Points { get; } = new();

    /// <summary>The outline was edited by hand in Word (@edited).</summary>
    public bool Edited { get; set; }

    /// <summary>The image rectangle (Word's default outline).</summary>
    public static WrapPolygon Rectangle()
    {
        var p = new WrapPolygon();
        p.Points.AddRange(new (long, long)[] { (0, 0), (0, 21600), (21600, 21600), (21600, 0), (0, 0) });
        return p;
    }
}

/// <summary>Cropping as fractions (0–1, may be negative to pad) of the picture removed from each side (a:srcRect).</summary>
public sealed record ImageCrop(double Left, double Top, double Right, double Bottom);

/// <summary>Four distances in points (left, top, right, bottom).</summary>
public sealed record ImageEffectExtent(double LeftPt, double TopPt, double RightPt, double BottomPt);

/// <summary>
/// A read-only description of an image found in a document. Carries the raw bytes, format,
/// display size, placement, and — when inside a table — the table/row/column location.
/// This is the data the Chuvadi.Pdf converter consumes to re-emit images on a PDF page.
/// </summary>
public sealed class ImageInfo
{
    /// <summary>The image relationship id within its hosting part (e.g. "rId7").</summary>
    public required string RelationshipId { get; init; }

    /// <summary>MIME type, e.g. "image/png".</summary>
    public required string ContentType { get; init; }

    /// <summary>The media part name inside the package, e.g. "image1.png".</summary>
    public required string FileName { get; init; }

    /// <summary>Raw image bytes.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>Display width in points as set in the document (not the pixel width).</summary>
    public double WidthPt { get; init; }

    /// <summary>Display height in points as set in the document (not the pixel height).</summary>
    public double HeightPt { get; init; }

    /// <summary>Alt text / description, if any.</summary>
    public string? AltText { get; init; }

    /// <summary>Inline or floating.</summary>
    public ImagePlacement Placement { get; init; }

    /// <summary>Position details for floating images; null for inline.</summary>
    public FloatingPosition? Position { get; init; }

    /// <summary>Which host part the image came from.</summary>
    public ImageHost Host { get; init; }

    /// <summary>0-based index of the table the image sits in, or null if not in a table.</summary>
    public int? TableIndex { get; init; }

    /// <summary>0-based row index within the table, or null.</summary>
    public int? TableRow { get; init; }

    /// <summary>0-based column index within the table, or null.</summary>
    public int? TableColumn { get; init; }

    /// <summary>Text of the paragraph the image is anchored to (for identification), if available.</summary>
    public string? AnchorParagraphText { get; init; }

    /// <summary>Cropping (a:srcRect), or null.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Clockwise rotation in degrees.</summary>
    public double RotationDegrees { get; init; }

    /// <summary>Mirrored left-to-right.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Mirrored top-to-bottom.</summary>
    public bool FlipVertical { get; init; }

    /// <summary>Space taken by effects outside the frame, or null.</summary>
    public ImageEffectExtent? EffectExtent { get; init; }
}

/// <summary>Which part of the document an image was found in.</summary>
public enum ImageHost
{
    Body,
    Header,
    Footer,
    /// <summary>Inside a footnote (<see cref="PlacedImage.Note"/>).</summary>
    Footnote,
    /// <summary>Inside an endnote (<see cref="PlacedImage.Note"/>).</summary>
    Endnote,
}

/// <summary>
/// One picture in the document model with everything a layout engine needs to place it: the
/// paragraph it sits in (inline) or is anchored to (floating), the run, its index among the
/// paragraph's runs, the story and section, and the table cell when inside a table.
/// </summary>
/// <param name="Image">The picture: size, crop, rotation, placement and <see cref="ImageSpec.Position"/>.</param>
/// <param name="Paragraph">The paragraph holding the image run — the anchor paragraph of a floating image.</param>
/// <param name="Run">The run carrying the image.</param>
/// <param name="RunIndex">Index of <paramref name="Run"/> in <see cref="Paragraph.Runs"/>.</param>
/// <param name="Host">Body, header or footer.</param>
/// <param name="Section">For headers/footers: the (first) section using that header/footer; for the body: the section containing the paragraph.</param>
/// <param name="HeaderFooter">For headers/footers: the header/footer content object; null in the body.</param>
/// <param name="Cell">The innermost table cell holding the paragraph, or null.</param>
public sealed record PlacedImage(ImageSpec Image, Paragraph Paragraph, Run Run, int RunIndex, ImageHost Host, Section Section,
    HeaderFooterContent? HeaderFooter, TableCellRef? Cell)
{
    /// <summary>The footnote or endnote holding the picture (hosts <see cref="ImageHost.Footnote"/> / <see cref="ImageHost.Endnote"/>), else null.</summary>
    public Note? Note { get; init; }

    /// <summary>The text box holding the picture (inside a text box's paragraphs), else null.</summary>
    public TextBox? TextBox { get; init; }
}
