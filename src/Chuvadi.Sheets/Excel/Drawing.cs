using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>What a drawing object on a sheet is.</summary>
public enum DrawingKind
{
    /// <summary>A picture (&lt;xdr:pic&gt;) with image bytes.</summary>
    Picture = 0,
    /// <summary>An autoshape or text box (&lt;xdr:sp&gt;).</summary>
    Shape,
    /// <summary>A connector line (&lt;xdr:cxnSp&gt;).</summary>
    Connector,
    /// <summary>A group of objects (&lt;xdr:grpSp&gt;); see <see cref="DrawingObject.Children"/>.</summary>
    Group,
    /// <summary>A chart (graphic frame); its metadata is in <see cref="DrawingObject.Chart"/>.</summary>
    Chart,
    /// <summary>Another graphic frame (SmartArt, slicer, ...) or an unrecognised object.</summary>
    Other,
}

/// <summary>How an object is anchored (the anchor element).</summary>
public enum DrawingAnchorType
{
    /// <summary>&lt;xdr:twoCellAnchor&gt;: from one cell corner to another.</summary>
    TwoCell = 0,
    /// <summary>&lt;xdr:oneCellAnchor&gt;: from one cell corner, with a fixed size.</summary>
    OneCell,
    /// <summary>&lt;xdr:absoluteAnchor&gt;: a fixed position and size on the sheet.</summary>
    Absolute,
}

/// <summary>How a two-cell-anchored object moves and resizes with its cells (&lt;xdr:twoCellAnchor editAs&gt;).</summary>
public enum DrawingEditAs
{
    /// <summary>Move and size with cells (Excel's default).</summary>
    TwoCell = 0,
    /// <summary>Move but don't size with cells.</summary>
    OneCell,
    /// <summary>Don't move or size with cells.</summary>
    Absolute,
}

/// <summary>
/// A point on the sheet grid: a cell corner plus an offset into the cell, in EMU (see
/// <see cref="Emu"/>). Column and row are 1-based (the file stores 0-based values).
/// </summary>
/// <param name="Column">1-based column whose left edge the offset is measured from.</param>
/// <param name="ColumnOffsetEmu">Offset right of that edge, in EMU.</param>
/// <param name="Row">1-based row whose top edge the offset is measured from.</param>
/// <param name="RowOffsetEmu">Offset below that edge, in EMU.</param>
public readonly record struct DrawingPoint(int Column, long ColumnOffsetEmu, int Row, long RowOffsetEmu);

/// <summary>Where an object sits on the sheet.</summary>
public sealed class DrawingAnchor
{
    internal DrawingAnchor() { }

    public DrawingAnchorType Type { get; internal set; }

    /// <summary>For two-cell anchors: how the object follows its cells.</summary>
    public DrawingEditAs EditAs { get; internal set; }

    /// <summary>Top-left corner (two-cell and one-cell anchors), or null.</summary>
    public DrawingPoint? From { get; internal set; }

    /// <summary>Bottom-right corner (two-cell anchors), or null.</summary>
    public DrawingPoint? To { get; internal set; }

    /// <summary>Position from the sheet's top-left in EMU (absolute anchors), or null.</summary>
    public long? XEmu { get; internal set; }
    public long? YEmu { get; internal set; }

    /// <summary>Size in EMU (one-cell and absolute anchors; two-cell anchors take it from From/To), or null.</summary>
    public long? WidthEmu { get; internal set; }
    public long? HeightEmu { get; internal set; }
}

/// <summary>A picture's crop, as fractions (0–1) of the image cut from each side.</summary>
public readonly record struct PictureCrop(double Left, double Top, double Right, double Bottom);

/// <summary>
/// A picture, shape, connector, group or chart anchored on a sheet (from the sheet's drawing
/// part). Read-only. Which properties are set depends on <see cref="Kind"/>.
/// </summary>
public sealed class DrawingObject
{
    internal DrawingObject() { }

    public DrawingKind Kind { get; internal set; }

    /// <summary>The anchor of the top-level object (shared by a group's children), never null.</summary>
    public DrawingAnchor Anchor { get; internal set; } = new();

    /// <summary>Object id (&lt;cNvPr id&gt;).</summary>
    public int Id { get; internal set; }

    /// <summary>Name, e.g. "Picture 1" (&lt;cNvPr name&gt;).</summary>
    public string? Name { get; internal set; }

    /// <summary>Alternative text (&lt;cNvPr descr&gt;), or null.</summary>
    public string? Description { get; internal set; }

    /// <summary>Alternative-text title (&lt;cNvPr title&gt;), or null.</summary>
    public string? Title { get; internal set; }

    public bool Hidden { get; internal set; }

    /// <summary>Clockwise rotation in degrees (&lt;a:xfrm rot&gt;).</summary>
    public double Rotation { get; internal set; }

    public bool FlipHorizontal { get; internal set; }
    public bool FlipVertical { get; internal set; }

    /// <summary>The object's own transform (&lt;a:xfrm&gt; offset and size, EMU), or null. For a group's children it is in the group's coordinate space.</summary>
    public long? OffsetXEmu { get; internal set; }
    public long? OffsetYEmu { get; internal set; }
    public long? ExtentWidthEmu { get; internal set; }
    public long? ExtentHeightEmu { get; internal set; }

    /// <summary>For a group: its child coordinate space (&lt;a:chOff&gt;, &lt;a:chExt&gt;), or null.</summary>
    public long? ChildOffsetXEmu { get; internal set; }
    public long? ChildOffsetYEmu { get; internal set; }
    public long? ChildExtentWidthEmu { get; internal set; }
    public long? ChildExtentHeightEmu { get; internal set; }

    /// <summary>A group's members, in file order; empty for other kinds.</summary>
    public IReadOnlyList<DrawingObject> Children { get; internal set; } = Array.Empty<DrawingObject>();

    // ---- Pictures -------------------------------------------------------------------

    /// <summary>The image part's name in the package, e.g. "/xl/media/image1.png", or null.</summary>
    public string? ImagePartName { get; internal set; }

    /// <summary>The image's content type, e.g. "image/png", "image/jpeg", "image/x-emf", or null.</summary>
    public string? ContentType { get; internal set; }

    /// <summary>The picture's crop, or null when not cropped.</summary>
    public PictureCrop? Crop { get; internal set; }

    internal Func<byte[]?>? ImageLoader { get; set; }
    internal byte[]? ImageData { get; set; }

    /// <summary>
    /// The image file's bytes (PNG, JPEG, GIF, BMP, TIFF, EMF, WMF or SVG as stored), or null
    /// when the object is not a picture, the image is linked (not embedded), or the Workbook was
    /// loaded with <see cref="XlsxReaderOptions.LoadImageData"/> false. For the streaming reader
    /// the bytes are read when asked, while the <see cref="XlsxReader"/> is open (read limits
    /// apply). Treat the array as read-only.
    /// </summary>
    public byte[]? GetImageBytes() => ImageData ?? ImageLoader?.Invoke();

    /// <summary>For a linked picture: the external target (file path or URL), or null.</summary>
    public string? LinkedImageTarget { get; internal set; }

    // ---- Shapes -----------------------------------------------------------------------

    /// <summary>Preset geometry, e.g. "rect", "roundRect", "ellipse", "line", "rightArrow"; null for custom geometry.</summary>
    public string? Geometry { get; internal set; }

    /// <summary>The shape's text as plain text (paragraphs joined with '\n'), or null.</summary>
    public string? Text { get; internal set; }

    /// <summary>Solid fill colour "RRGGBB" (explicit or from the shape style; theme colours resolved), or null.</summary>
    public string? FillColor { get; internal set; }

    /// <summary>True if the shape explicitly has no fill.</summary>
    public bool NoFill { get; internal set; }

    /// <summary>Outline colour "RRGGBB", or null.</summary>
    public string? LineColor { get; internal set; }

    /// <summary>Outline width in EMU, or null.</summary>
    public long? LineWidthEmu { get; internal set; }

    /// <summary>True if the shape explicitly has no outline.</summary>
    public bool NoLine { get; internal set; }

    // ---- Charts ----------------------------------------------------------------------

    /// <summary>The chart part's name, e.g. "/xl/charts/chart1.xml" (or "/xl/charts/chartEx1.xml" for Excel 2016 charts), or null.</summary>
    public string? ChartPartName { get; internal set; }

    /// <summary>
    /// The chart's metadata (type, title, series references and cached values, axis titles,
    /// legend), or null when the object is not a chart or its part is missing. Metadata only.
    /// </summary>
    public ChartInfo? Chart { get; internal set; }

    public override string ToString() => $"{Kind} '{Name}'";
}

/// <summary>English Metric Units, DrawingML's length unit, and conversions.</summary>
public static class Emu
{
    public const long PerInch = 914400;
    public const long PerPoint = 12700;
    public const long PerCentimeter = 360000;
    public const long PerMillimeter = 36000;

    /// <summary>EMU per pixel at 96 DPI (Excel's screen pixel).</summary>
    public const long PerPixel = 9525;

    public static double ToPoints(long emu) => emu / (double)PerPoint;
    public static double ToInches(long emu) => emu / (double)PerInch;
    public static double ToPixels(long emu, double dpi = 96) => emu / (double)PerInch * dpi;
    public static long FromPoints(double points) => (long)Math.Round(points * PerPoint);
}
