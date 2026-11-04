using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.Model
{
    /// <summary>What a floating object's horizontal position is measured from (wp:positionH/@relativeFrom).</summary>
    public enum HorizontalAnchor
    {
        Page,
        Margin,
        Column,
        Character,
    }

    /// <summary>What a floating object's vertical position is measured from (wp:positionV/@relativeFrom).</summary>
    public enum VerticalAnchor
    {
        Page,
        Margin,
        Paragraph,
        Line,
    }

    /// <summary>Alignment of a floating object within its anchor (wp:align); null uses the offset.</summary>
    public enum FloatAlignment
    {
        Start,
        Center,
        End,
    }

    /// <summary>How text flows around a floating object (OOXML wrap types).</summary>
    public enum FloatWrap
    {
        /// <summary>wrapNone, in front of text: drawn over text, no effect on flow.</summary>
        InFrontOfText,

        /// <summary>wrapNone with behindDoc: drawn under text, no effect on flow.</summary>
        BehindText,

        /// <summary>wrapTopAndBottom: text continues below the object.</summary>
        TopAndBottom,

        /// <summary>wrapSquare: text flows beside the object's rectangle (plus the wrap distances).</summary>
        Square,

        /// <summary>
        /// wrapTight: text flows beside the object's wrap polygon (<see cref="AnchoredObjectInline.WrapPolygon"/>),
        /// line by line; without a polygon, beside its rectangle.
        /// </summary>
        Tight,

        /// <summary>
        /// wrapThrough: as <see cref="Tight"/>. Text is not placed in gaps inside the polygon; a TextWrapApproximated
        /// warning says so.
        /// </summary>
        Through,
    }

    /// <summary>Which sides of a square, tight or through object text may use (OOXML wrapText).</summary>
    public enum FloatWrapSide
    {
        /// <summary>bothSides: text fills the space on the left, then on the right, of each row.</summary>
        BothSides,

        /// <summary>left: text only on the left of the object.</summary>
        Left,

        /// <summary>right: text only on the right of the object.</summary>
        Right,

        /// <summary>largest: text only on the wider side.</summary>
        Largest,
    }

    /// <summary>Fractions of an image cut away on each side before it is drawn (OOXML a:srcRect), 0 to 1.</summary>
    public readonly record struct ImageCrop(double Left, double Top, double Right, double Bottom)
    {
        /// <summary>True when something is cut away.</summary>
        public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

        /// <summary>The kept part in source pixels, or null when the pixel size is unknown or nothing is cut.</summary>
        public LayoutRect? ToSourceRect(LayoutImage image)
        {
            if (IsEmpty || image.PixelWidth <= 0 || image.PixelHeight <= 0)
            {
                return null;
            }
            double w = Math.Max(1, (1 - Left - Right) * image.PixelWidth);
            double h = Math.Max(1, (1 - Top - Bottom) * image.PixelHeight);
            return new LayoutRect(Left * image.PixelWidth, Top * image.PixelHeight, w, h);
        }
    }

    /// <summary>A vertex of a wrap polygon, in points from the object's top-left corner.</summary>
    public readonly record struct WrapPoint(double X, double Y);

    /// <summary>
    /// A floating (anchored) object: a picture or a text box. It appears on the page where its anchor paragraph's first
    /// line is placed, positioned relative to the page, margins or paragraph.
    /// </summary>
    public abstract class AnchoredObjectInline : FlowInline
    {
        private protected AnchoredObjectInline(double widthPt, double heightPt, CharacterFormat format) : base(format)
        {
            Width = widthPt;
            Height = heightPt;
        }

        public double Width { get; }

        public double Height { get; }

        public HorizontalAnchor HorizontalRelativeTo { get; init; } = HorizontalAnchor.Column;

        public VerticalAnchor VerticalRelativeTo { get; init; } = VerticalAnchor.Paragraph;

        /// <summary>Offset from the anchor's left edge, used when <see cref="HorizontalAlignment"/> is null.</summary>
        public double OffsetX { get; init; }

        /// <summary>Offset from the anchor's top edge, used when <see cref="VerticalAlignment"/> is null.</summary>
        public double OffsetY { get; init; }

        public FloatAlignment? HorizontalAlignment { get; init; }

        public FloatAlignment? VerticalAlignment { get; init; }

        public FloatWrap Wrap { get; init; } = FloatWrap.Square;

        /// <summary>Sides text may use for <see cref="FloatWrap.Square"/>, <see cref="FloatWrap.Tight"/> and <see cref="FloatWrap.Through"/>.</summary>
        public FloatWrapSide WrapSide { get; init; } = FloatWrapSide.BothSides;

        /// <summary>
        /// Wrap contour for <see cref="FloatWrap.Tight"/> and <see cref="FloatWrap.Through"/> (wp:wrapPolygon), in points
        /// from the object's top-left corner. OOXML stores it in 21600ths of the extent: X = x / 21600 * Width,
        /// Y = y / 21600 * Height. Null uses the rectangle.
        /// </summary>
        public IReadOnlyList<WrapPoint>? WrapPolygon { get; init; }

        /// <summary>Distance kept between the object and text (wp:anchor distT/distB/distL/distR), in points.</summary>
        public double DistanceTop { get; init; }

        public double DistanceBottom { get; init; }

        public double DistanceLeft { get; init; } = 9;

        public double DistanceRight { get; init; } = 9;

        /// <summary>Alternative text.</summary>
        public string? Description { get; init; }
    }

    /// <summary>A floating (anchored) image.</summary>
    public sealed class AnchoredImageInline : AnchoredObjectInline
    {
        public AnchoredImageInline(LayoutImage image, double widthPt, double heightPt, CharacterFormat format) : base(widthPt, heightPt, format)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }

        public LayoutImage Image { get; }

        /// <summary>Part of the image cut away before drawing; null = none.</summary>
        public ImageCrop? Crop { get; init; }
    }

    /// <summary>
    /// A floating text box. With <see cref="FlowTextBox.AutoFit"/> its height grows to fit the text; text around it
    /// wraps as for a picture.
    /// </summary>
    public sealed class AnchoredTextBoxInline : AnchoredObjectInline
    {
        public AnchoredTextBoxInline(FlowTextBox box, double widthPt, double heightPt, CharacterFormat format) : base(widthPt, heightPt, format)
        {
            Box = box ?? throw new ArgumentNullException(nameof(box));
        }

        public FlowTextBox Box { get; }
    }

    /// <summary>What a floating table's horizontal position is measured from (w:tblpPr/@w:horzAnchor).</summary>
    public enum TableHorizontalAnchor
    {
        /// <summary>The page edge (the standard's default when the file does not say).</summary>
        Page,

        /// <summary>The text margins.</summary>
        Margin,

        /// <summary>The text column the table is in (w:horzAnchor="text").</summary>
        Column,
    }

    /// <summary>What a floating table's vertical position is measured from (w:tblpPr/@w:vertAnchor).</summary>
    public enum TableVerticalAnchor
    {
        /// <summary>The page edge (the standard's default when the file does not say).</summary>
        Page,

        /// <summary>The text margins.</summary>
        Margin,

        /// <summary>The top of the paragraph that follows the table (w:vertAnchor="text").</summary>
        Paragraph,
    }

    /// <summary>Relative horizontal position of a floating table (w:tblpXSpec).</summary>
    public enum TableHorizontalAlignment
    {
        Left,
        Center,
        Right,

        /// <summary>The inside edge: left on odd pages, right on even pages.</summary>
        Inside,

        /// <summary>The outside edge: right on odd pages, left on even pages.</summary>
        Outside,
    }

    /// <summary>Relative vertical position of a floating table (w:tblpYSpec).</summary>
    public enum TableVerticalAlignment
    {
        /// <summary>In line with the text: laid out at the anchor's top.</summary>
        Inline,
        Top,
        Center,
        Bottom,

        /// <summary>The inside edge (the top).</summary>
        Inside,

        /// <summary>The outside edge (the bottom).</summary>
        Outside,
    }

    /// <summary>
    /// Where a floating table sits (w:tblpPr) and how text keeps away from it. ECMA-376 17.4.57: omitted anchors are the
    /// page, omitted offsets and distances 0; a relative position wins over the offset, except that a vertical position
    /// relative to the paragraph uses the offset only. Text flows on both sides of the table, at least the distances away.
    /// </summary>
    public sealed record FloatingTablePosition
    {
        public TableHorizontalAnchor HorizontalAnchor { get; init; } = TableHorizontalAnchor.Page;

        public TableVerticalAnchor VerticalAnchor { get; init; } = TableVerticalAnchor.Page;

        /// <summary>Relative horizontal position; null uses <see cref="OffsetX"/>.</summary>
        public TableHorizontalAlignment? HorizontalAlignment { get; init; }

        /// <summary>Distance of the table's left edge from the anchor (w:tblpX), in points; negative = before it.</summary>
        public double OffsetX { get; init; }

        /// <summary>Relative vertical position; null uses <see cref="OffsetY"/>. Ignored for <see cref="TableVerticalAnchor.Paragraph"/>.</summary>
        public TableVerticalAlignment? VerticalAlignment { get; init; }

        /// <summary>Distance of the table's top from the anchor (w:tblpY), in points; negative = above it.</summary>
        public double OffsetY { get; init; }

        /// <summary>Least distance between the table and the text on its left (w:leftFromText).</summary>
        public double DistanceLeft { get; init; }

        /// <summary>Least distance between the table and the text on its right (w:rightFromText).</summary>
        public double DistanceRight { get; init; }

        /// <summary>Least distance between the table and the text above it (w:topFromText).</summary>
        public double DistanceTop { get; init; }

        /// <summary>Least distance between the table and the text below it (w:bottomFromText).</summary>
        public double DistanceBottom { get; init; }

        /// <summary>
        /// Other floating tables may overlap this one (w:tblOverlap; true when the file does not say). When either of two
        /// tables does not allow it, the later one is moved down until they no longer overlap.
        /// </summary>
        public bool AllowOverlap { get; init; } = true;
    }
}
