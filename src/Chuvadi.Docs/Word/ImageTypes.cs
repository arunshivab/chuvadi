using System;

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

    /// <summary>When <see cref="Wrap"/> is None: true = behind text, false = in front of text.</summary>
    public bool BehindText { get; set; }

    /// <summary>Allow this image to overlap other floating objects.</summary>
    public bool AllowOverlap { get; set; } = true;

    /// <summary>Lock the anchor so the image doesn't move when its paragraph moves.</summary>
    public bool LockAnchor { get; set; }

    /// <summary>Z-order; higher is closer to the front. Default auto-assigns.</summary>
    public long? RelativeHeight { get; set; }

    /// <summary>Distance from text on each side, in points (wrap padding).</summary>
    public double DistanceFromTextPt { get; set; } = 0;
}

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
}

/// <summary>Which part of the document an image was found in.</summary>
public enum ImageHost
{
    Body,
    Header,
    Footer,
}
