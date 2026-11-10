using System;
using System.Collections.Generic;

namespace Chuvadi.Print
{
    /// <summary>
    /// A 2-D affine transform, in the same form as PDF and XPS: a point (x, y) maps to
    /// (<see cref="M11"/>·x + <see cref="M21"/>·y + <see cref="OffsetX"/>, <see cref="M12"/>·x + <see cref="M22"/>·y + <see cref="OffsetY"/>).
    /// </summary>
    public readonly struct VectorMatrix : IEquatable<VectorMatrix>
    {
        public VectorMatrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public static VectorMatrix Identity => new(1, 0, 0, 1, 0, 0);

        public double M11 { get; }
        public double M12 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }

        public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

        public static VectorMatrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

        public static VectorMatrix Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

        /// <summary>The transform that applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
        public static VectorMatrix Multiply(VectorMatrix first, VectorMatrix second) => new(
            first.M11 * second.M11 + first.M12 * second.M21,
            first.M11 * second.M12 + first.M12 * second.M22,
            first.M21 * second.M11 + first.M22 * second.M21,
            first.M21 * second.M12 + first.M22 * second.M22,
            first.OffsetX * second.M11 + first.OffsetY * second.M21 + second.OffsetX,
            first.OffsetX * second.M12 + first.OffsetY * second.M22 + second.OffsetY);

        public VectorPoint Transform(VectorPoint p)
            => new(M11 * p.X + M21 * p.Y + OffsetX, M12 * p.X + M22 * p.Y + OffsetY);

        public bool Equals(VectorMatrix other)
            => M11 == other.M11 && M12 == other.M12 && M21 == other.M21 && M22 == other.M22
               && OffsetX == other.OffsetX && OffsetY == other.OffsetY;

        public override bool Equals(object? obj) => obj is VectorMatrix m && Equals(m);

        public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);

        public static bool operator ==(VectorMatrix a, VectorMatrix b) => a.Equals(b);

        public static bool operator !=(VectorMatrix a, VectorMatrix b) => !a.Equals(b);
    }

    public readonly struct VectorPoint : IEquatable<VectorPoint>
    {
        public VectorPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public bool Equals(VectorPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is VectorPoint p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(VectorPoint a, VectorPoint b) => a.Equals(b);
        public static bool operator !=(VectorPoint a, VectorPoint b) => !a.Equals(b);
    }

    /// <summary>An sRGB colour with alpha, 8 bits per channel.</summary>
    public readonly struct VectorColor : IEquatable<VectorColor>
    {
        public VectorColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        /// <summary>Alpha: 0 transparent, 255 opaque.</summary>
        public byte A { get; }

        public static VectorColor Black => new(0, 0, 0);
        public static VectorColor White => new(255, 255, 255);

        /// <summary>From channels 0..1 (clamped, rounded to the nearest 8-bit value).</summary>
        public static VectorColor FromUnit(double r, double g, double b, double a = 1)
            => new(ToByte(r), ToByte(g), ToByte(b), ToByte(a));

        private static byte ToByte(double v)
        {
            if (double.IsNaN(v) || v <= 0) return 0;
            if (v >= 1) return 255;
            return (byte)Math.Round(v * 255, MidpointRounding.AwayFromZero);
        }

        public bool Equals(VectorColor other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object? obj) => obj is VectorColor c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        public static bool operator ==(VectorColor a, VectorColor b) => a.Equals(b);
        public static bool operator !=(VectorColor a, VectorColor b) => !a.Equals(b);
    }

    public enum VectorFillRule
    {
        /// <summary>Non-zero winding (PDF <c>f</c>, XPS <c>F1</c>).</summary>
        NonZero = 0,

        /// <summary>Even-odd (PDF <c>f*</c>, XPS <c>F0</c>).</summary>
        EvenOdd = 1
    }

    public enum VectorSegmentKind
    {
        /// <summary>Starts a new figure at <see cref="VectorSegment.Point3"/>.</summary>
        MoveTo = 0,

        /// <summary>A straight line to <see cref="VectorSegment.Point3"/>.</summary>
        LineTo = 1,

        /// <summary>A cubic Bézier through control points <see cref="VectorSegment.Point1"/> and <see cref="VectorSegment.Point2"/> to <see cref="VectorSegment.Point3"/>.</summary>
        CubicTo = 2,

        /// <summary>Closes the current figure.</summary>
        Close = 3
    }

    public readonly struct VectorSegment
    {
        public VectorSegment(VectorSegmentKind kind, VectorPoint point1, VectorPoint point2, VectorPoint point3)
        {
            Kind = kind;
            Point1 = point1;
            Point2 = point2;
            Point3 = point3;
        }

        public VectorSegmentKind Kind { get; }

        /// <summary>The first control point (<see cref="VectorSegmentKind.CubicTo"/> only).</summary>
        public VectorPoint Point1 { get; }

        /// <summary>The second control point (<see cref="VectorSegmentKind.CubicTo"/> only).</summary>
        public VectorPoint Point2 { get; }

        /// <summary>The end point (not used by <see cref="VectorSegmentKind.Close"/>).</summary>
        public VectorPoint Point3 { get; }
    }

    /// <summary>
    /// A path outline: figures of straight lines and cubic Béziers, and the fill rule. Coordinates are in the
    /// coordinate space of the element that uses it.
    /// </summary>
    public sealed class VectorGeometry
    {
        private readonly List<VectorSegment> _segments = new();

        public VectorFillRule FillRule { get; set; }

        public IReadOnlyList<VectorSegment> Segments => _segments;

        public bool IsEmpty => _segments.Count == 0;

        public VectorGeometry MoveTo(double x, double y)
        {
            _segments.Add(new VectorSegment(VectorSegmentKind.MoveTo, default, default, new VectorPoint(x, y)));
            return this;
        }

        /// <exception cref="InvalidOperationException">No figure has been started with <see cref="MoveTo"/>.</exception>
        public VectorGeometry LineTo(double x, double y)
        {
            RequireFigure();
            _segments.Add(new VectorSegment(VectorSegmentKind.LineTo, default, default, new VectorPoint(x, y)));
            return this;
        }

        /// <exception cref="InvalidOperationException">No figure has been started with <see cref="MoveTo"/>.</exception>
        public VectorGeometry CubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            RequireFigure();
            _segments.Add(new VectorSegment(VectorSegmentKind.CubicTo, new VectorPoint(x1, y1), new VectorPoint(x2, y2), new VectorPoint(x3, y3)));
            return this;
        }

        /// <exception cref="InvalidOperationException">No figure has been started with <see cref="MoveTo"/>.</exception>
        public VectorGeometry Close()
        {
            RequireFigure();
            _segments.Add(new VectorSegment(VectorSegmentKind.Close, default, default, default));
            return this;
        }

        /// <summary>Adds a closed rectangle figure.</summary>
        public VectorGeometry Rectangle(double x, double y, double width, double height)
            => MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close();

        /// <summary>A geometry holding one closed rectangle.</summary>
        public static VectorGeometry FromRectangle(double x, double y, double width, double height, VectorFillRule fillRule = VectorFillRule.NonZero)
            => new VectorGeometry { FillRule = fillRule }.Rectangle(x, y, width, height);

        private void RequireFigure()
        {
            if (_segments.Count == 0) throw new InvalidOperationException("A path must start with MoveTo.");
        }
    }

    /// <summary>The 16 blend modes of PDF (ISO 32000-2, 11.3.5).</summary>
    public enum VectorBlendMode
    {
        Normal = 0,
        Multiply = 1,
        Screen = 2,
        Overlay = 3,
        Darken = 4,
        Lighten = 5,
        ColorDodge = 6,
        ColorBurn = 7,
        HardLight = 8,
        SoftLight = 9,
        Difference = 10,
        Exclusion = 11,
        Hue = 12,
        Saturation = 13,
        Color = 14,
        Luminosity = 15
    }

    // ---------------------------------------------------------------- brushes

    /// <summary>What fills an area or a stroke.</summary>
    public abstract class VectorBrush
    {
        private protected VectorBrush() { }

        /// <summary>The brush's own opacity, 0..1 (multiplied with any colour alpha).</summary>
        public double Opacity { get; set; } = 1;
    }

    public sealed class VectorSolidBrush : VectorBrush
    {
        public VectorSolidBrush(VectorColor color) => Color = color;

        public VectorColor Color { get; set; }
    }

    public readonly struct VectorGradientStop
    {
        public VectorGradientStop(double offset, VectorColor color)
        {
            Offset = offset;
            Color = color;
        }

        /// <summary>Position along the gradient, 0..1.</summary>
        public double Offset { get; }

        public VectorColor Color { get; }
    }

    /// <summary>Shared parts of the gradient brushes. Colours between stops are interpolated in sRGB.</summary>
    public abstract class VectorGradientBrush : VectorBrush
    {
        private protected VectorGradientBrush() { }

        /// <summary>The stops, in increasing offset order.</summary>
        public IList<VectorGradientStop> Stops { get; } = new List<VectorGradientStop>();

        /// <summary>Whether the first colour continues before the start (PDF Extend[0]).</summary>
        public bool ExtendStart { get; set; } = true;

        /// <summary>Whether the last colour continues past the end (PDF Extend[1]).</summary>
        public bool ExtendEnd { get; set; } = true;

        /// <summary>
        /// Maps the gradient's own coordinates into the coordinate space of the element it fills (for a PDF
        /// shading pattern, its pattern matrix relative to that space).
        /// </summary>
        public VectorMatrix Transform { get; set; } = VectorMatrix.Identity;
    }

    /// <summary>An axial gradient from <see cref="Start"/> (offset 0) to <see cref="End"/> (offset 1).</summary>
    public sealed class VectorLinearGradientBrush : VectorGradientBrush
    {
        public VectorLinearGradientBrush(VectorPoint start, VectorPoint end)
        {
            Start = start;
            End = end;
        }

        public VectorPoint Start { get; set; }
        public VectorPoint End { get; set; }
    }

    /// <summary>
    /// A radial gradient between two circles, as PDF type 3 shading: offset 0 is the circle at
    /// <see cref="StartCenter"/> with <see cref="StartRadius"/>, offset 1 the circle at <see cref="EndCenter"/> with
    /// <see cref="EndRadius"/>.
    /// </summary>
    public sealed class VectorRadialGradientBrush : VectorGradientBrush
    {
        public VectorRadialGradientBrush(VectorPoint startCenter, double startRadius, VectorPoint endCenter, double endRadius)
        {
            StartCenter = startCenter;
            StartRadius = startRadius;
            EndCenter = endCenter;
            EndRadius = endRadius;
        }

        public VectorPoint StartCenter { get; set; }
        public double StartRadius { get; set; }
        public VectorPoint EndCenter { get; set; }
        public double EndRadius { get; set; }
    }

    // ---------------------------------------------------------------- elements

    /// <summary>
    /// Something drawn on a vector page. Each element has its own coordinate space: <see cref="Transform"/> maps it
    /// into the space of the parent (the page, or a <see cref="VectorGroup"/>).
    /// </summary>
    public abstract class VectorElement
    {
        private protected VectorElement() { }

        /// <summary>Maps the element's coordinates into its parent's.</summary>
        public VectorMatrix Transform { get; set; } = VectorMatrix.Identity;

        /// <summary>A clip in the element's own coordinates (after <see cref="Transform"/>), or null.</summary>
        public VectorGeometry? Clip { get; set; }

        /// <summary>Opacity of the whole element, 0..1.</summary>
        public double Opacity { get; set; } = 1;

        /// <summary>How the element is composited with what is under it.</summary>
        public VectorBlendMode BlendMode { get; set; }
    }

    public enum VectorLineCap
    {
        Butt = 0,
        Round = 1,
        Square = 2
    }

    public enum VectorLineJoin
    {
        Miter = 0,
        Round = 1,
        Bevel = 2
    }

    /// <summary>How a path is stroked. Widths and dashes are in the path's coordinate space.</summary>
    public sealed class VectorStroke
    {
        public VectorStroke(VectorBrush brush, double width)
        {
            Brush = brush ?? throw new ArgumentNullException(nameof(brush));
            Width = width;
        }

        public VectorBrush Brush { get; set; }

        /// <summary>The line width; must be greater than 0.</summary>
        public double Width { get; set; }

        public VectorLineCap LineCap { get; set; }
        public VectorLineJoin LineJoin { get; set; }

        /// <summary>The miter limit (PDF default 10).</summary>
        public double MiterLimit { get; set; } = 10;

        /// <summary>Dash and gap lengths, alternating; empty for a solid line. An odd count repeats once, as in PDF.</summary>
        public IList<double> DashArray { get; } = new List<double>();

        public double DashOffset { get; set; }
    }

    /// <summary>A filled and/or stroked path.</summary>
    public sealed class VectorPath : VectorElement
    {
        public VectorPath(VectorGeometry geometry) => Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));

        public VectorGeometry Geometry { get; set; }

        /// <summary>The fill, or null for none.</summary>
        public VectorBrush? Fill { get; set; }

        /// <summary>The stroke, or null for none.</summary>
        public VectorStroke? Stroke { get; set; }
    }

    /// <summary>
    /// A font for glyph runs: the font file as bytes. Use the same instance for every run with that font so it is
    /// stored once.
    /// </summary>
    public sealed class VectorFont
    {
        public VectorFont(byte[] data, string? name = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Name = name;
        }

        /// <summary>The font file (an sfnt: TrueType or OpenType).</summary>
        public byte[] Data { get; }

        /// <summary>A name for messages (for example the PDF BaseFont), or null.</summary>
        public string? Name { get; }
    }

    /// <summary>One glyph of a run.</summary>
    public readonly struct VectorGlyph
    {
        public VectorGlyph(ushort glyphId, double x, double y, double advance, string? text = null)
        {
            GlyphId = glyphId;
            X = x;
            Y = y;
            Advance = advance;
            Text = text;
        }

        /// <summary>The glyph index in the font.</summary>
        public ushort GlyphId { get; }

        /// <summary>The glyph origin on the baseline, in the run's coordinates.</summary>
        public double X { get; }

        public double Y { get; }

        /// <summary>The horizontal advance in the run's coordinates (used for the last glyph of a line).</summary>
        public double Advance { get; }

        /// <summary>The characters this glyph stands for (for search and copy), or null when unknown.</summary>
        public string? Text { get; }
    }

    /// <summary>
    /// Glyphs from one font at one size. Coordinates run right and down; glyphs are drawn upright, with the
    /// baseline at each glyph's Y. Use <see cref="VectorElement.Transform"/> for rotated, mirrored or skewed text.
    /// </summary>
    public sealed class VectorGlyphRun : VectorElement
    {
        public VectorGlyphRun(VectorFont font, double emSize, VectorBrush fill)
        {
            Font = font ?? throw new ArgumentNullException(nameof(font));
            EmSize = emSize;
            Fill = fill ?? throw new ArgumentNullException(nameof(fill));
        }

        public VectorFont Font { get; set; }

        /// <summary>The em size in the run's coordinates.</summary>
        public double EmSize { get; set; }

        public VectorBrush Fill { get; set; }

        public IList<VectorGlyph> Glyphs { get; } = new List<VectorGlyph>();
    }

    /// <summary>
    /// An image. Its pixels fill the unit square (0,0)–(1,1) of its coordinate space, top row at y = 0;
    /// <see cref="VectorElement.Transform"/> places that square on the page.
    /// </summary>
    public sealed class VectorImageElement : VectorElement
    {
        public VectorImageElement(VectorImage image) => Image = image ?? throw new ArgumentNullException(nameof(image));

        public VectorImageElement(VectorImage image, double x, double y, double width, double height) : this(image)
            => Transform = new VectorMatrix(width, 0, 0, height, x, y);

        public VectorImage Image { get; set; }
    }

    /// <summary>The kind of a soft mask.</summary>
    public enum VectorSoftMaskKind
    {
        /// <summary>The mask's alpha is the coverage.</summary>
        Alpha = 0,

        /// <summary>The mask's luminosity is the coverage.</summary>
        Luminosity = 1
    }

    /// <summary>A soft mask: the drawing in <see cref="Content"/>, in the masked element's coordinate space.</summary>
    public sealed class VectorSoftMask
    {
        public VectorSoftMask(VectorSoftMaskKind kind, VectorGroup content)
        {
            Kind = kind;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public VectorSoftMaskKind Kind { get; set; }

        public VectorGroup Content { get; set; }

        /// <summary>For <see cref="VectorSoftMaskKind.Luminosity"/>: the colour outside the mask content (PDF BC).</summary>
        public VectorColor Backdrop { get; set; } = VectorColor.Black;
    }

    /// <summary>
    /// Elements drawn together: a transparency group when <see cref="VectorElement.Opacity"/> is below 1, a soft
    /// mask is set, or the blend mode is not Normal. Children are drawn in order, later on top.
    /// </summary>
    public sealed class VectorGroup : VectorElement
    {
        public IList<VectorElement> Children { get; } = new List<VectorElement>();

        /// <summary>Whether the group is isolated (PDF /I).</summary>
        public bool Isolated { get; set; }

        /// <summary>Whether the group is a knockout group (PDF /K).</summary>
        public bool Knockout { get; set; }

        /// <summary>A soft mask over the group, or null.</summary>
        public VectorSoftMask? SoftMask { get; set; }
    }

    /// <summary>
    /// One page of vector content. Sizes and coordinates are in points (1/72 inch); the origin is the top-left
    /// corner and y runs down.
    /// </summary>
    public sealed class VectorPage
    {
        public VectorPage(double width, double height)
        {
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(height > 0) || double.IsInfinity(height)) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }

        /// <summary>The elements, drawn in order, later on top.</summary>
        public IList<VectorElement> Elements { get; } = new List<VectorElement>();
    }
}
