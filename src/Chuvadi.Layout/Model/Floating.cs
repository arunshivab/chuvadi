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
}
