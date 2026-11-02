using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Drawing
{
    /// <summary>An sRGB colour with alpha.</summary>
    public readonly record struct LayoutColor(byte R, byte G, byte B, byte A = 255)
    {
        public static LayoutColor Black { get; } = new LayoutColor(0, 0, 0);
        public static LayoutColor White { get; } = new LayoutColor(255, 255, 255);
        public static LayoutColor Transparent { get; } = new LayoutColor(0, 0, 0, 0);

        public bool IsTransparent => A == 0;

        /// <summary>Parses "RRGGBB", "#RRGGBB" or "AARRGGBB" (OOXML ARGB order).</summary>
        /// <exception cref="FormatException">Not a hex colour.</exception>
        public static LayoutColor FromHex(string hex)
        {
            ArgumentNullException.ThrowIfNull(hex);
            ReadOnlySpan<char> s = hex.AsSpan().Trim();
            if (s.Length > 0 && s[0] == '#')
            {
                s = s[1..];
            }
            if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return new LayoutColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
            if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return new LayoutColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
            }
            throw new FormatException($"'{hex}' is not a hex colour.");
        }

        public string ToHex() => A == 255 ? $"{R:X2}{G:X2}{B:X2}" : $"{A:X2}{R:X2}{G:X2}{B:X2}";
    }

    /// <summary>A rectangle in points; origin top-left, y down.</summary>
    public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool Contains(LayoutRect other, double tolerance = 0)
            => other.X >= X - tolerance && other.Y >= Y - tolerance && other.Right <= Right + tolerance && other.Bottom <= Bottom + tolerance;

        public LayoutRect Union(LayoutRect o)
        {
            double x = Math.Min(X, o.X);
            double y = Math.Min(Y, o.Y);
            return new LayoutRect(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
        }
    }

    /// <summary>Kinds of <see cref="LayoutOp"/>.</summary>
    public enum LayoutOpKind
    {
        GlyphRun,
        FillRect,
        StrokeLine,
        Path,
        Image,
        PushClip,
        PopClip,
        Link,
    }

    /// <summary>One drawing operation. Ops are immutable and painted in list order.</summary>
    public abstract class LayoutOp
    {
        private protected LayoutOp()
        {
        }

        public abstract LayoutOpKind Kind { get; }
    }

    /// <summary>A positioned glyph; X/Y are offsets in points from the run origin; Y down.</summary>
    public readonly record struct PositionedGlyph(int GlyphId, double X, double Y, double Advance, int Cluster);

    /// <summary>
    /// Glyphs of one face at one size on one baseline. <see cref="Text"/> is the source text the glyphs
    /// came from; <see cref="PositionedGlyph.Cluster"/> indexes into it (for text extraction and search).
    /// </summary>
    public sealed class GlyphRunOp : LayoutOp
    {
        public override LayoutOpKind Kind => LayoutOpKind.GlyphRun;

        public required FontFace Font { get; init; }

        public required double FontSize { get; init; }

        /// <summary>Baseline origin of the run, in points.</summary>
        public required double X { get; init; }

        public required double Y { get; init; }

        public required IReadOnlyList<PositionedGlyph> Glyphs { get; init; }

        public required string Text { get; init; }

        public LayoutColor Color { get; init; } = LayoutColor.Black;

        /// <summary>Draw emboldened (the face lacks the requested bold weight).</summary>
        public bool SyntheticBold { get; init; }

        /// <summary>Draw slanted (the face lacks the requested italic style).</summary>
        public bool SyntheticItalic { get; init; }

        /// <summary>Horizontal glyph scale (Word character scaling); 1 is normal.</summary>
        public double HorizontalScale { get; init; } = 1.0;

        /// <summary>Total advance width of the run in points.</summary>
        public double Width
        {
            get
            {
                if (Glyphs.Count == 0)
                {
                    return 0;
                }
                PositionedGlyph last = Glyphs[^1];
                return last.X + last.Advance;
            }
        }
    }

    /// <summary>A filled rectangle (shading, highlight, cell fill, placeholder).</summary>
    public sealed class FillRectOp : LayoutOp
    {
        public FillRectOp(LayoutRect rect, LayoutColor color)
        {
            Rect = rect;
            Color = color;
        }

        public override LayoutOpKind Kind => LayoutOpKind.FillRect;

        public LayoutRect Rect { get; }

        public LayoutColor Color { get; }
    }

    /// <summary>Dash pattern of a stroked line.</summary>
    public enum LineDash
    {
        Solid,
        Dot,
        Dash,
        DashDot,
        DashDotDot,
        LongDash,
    }

    /// <summary>A straight stroked line (border, rule, underline, gridline).</summary>
    public sealed class StrokeLineOp : LayoutOp
    {
        public StrokeLineOp(double x1, double y1, double x2, double y2, double width, LayoutColor color, LineDash dash = LineDash.Solid)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Width = width;
            Color = color;
            Dash = dash;
        }

        public override LayoutOpKind Kind => LayoutOpKind.StrokeLine;

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Width { get; }
        public LayoutColor Color { get; }
        public LineDash Dash { get; }
    }

    /// <summary>Path segment kinds.</summary>
    public enum PathSegmentKind
    {
        MoveTo,
        LineTo,
        CubicTo,
        Close,
    }

    /// <summary>One path segment; cubic segments use both control points.</summary>
    public readonly record struct PathSegment(PathSegmentKind Kind, double X = 0, double Y = 0, double C1X = 0, double C1Y = 0, double C2X = 0, double C2Y = 0);

    /// <summary>A vector path, filled and/or stroked.</summary>
    public sealed class PathOp : LayoutOp
    {
        public override LayoutOpKind Kind => LayoutOpKind.Path;

        public required IReadOnlyList<PathSegment> Segments { get; init; }

        public LayoutColor? Fill { get; init; }

        public LayoutColor? Stroke { get; init; }

        public double StrokeWidth { get; init; } = 1;
    }

    /// <summary>Encoded image formats a display list may carry.</summary>
    public enum LayoutImageFormat
    {
        Png,
        Jpeg,
        Gif,
        Bmp,
        Tiff,
        Emf,
        Wmf,
        Svg,
        Unknown,
    }

    /// <summary>An encoded image. Bytes are carried unchanged from the source document.</summary>
    public sealed class LayoutImage
    {
        public LayoutImage(ReadOnlyMemory<byte> data, LayoutImageFormat format, int pixelWidth, int pixelHeight)
        {
            Data = data;
            Format = format;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }

        public ReadOnlyMemory<byte> Data { get; }
        public LayoutImageFormat Format { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }

        /// <summary>
        /// Wraps encoded image bytes, detecting the format from the signature (PNG, JPEG, GIF, BMP, TIFF, EMF, WMF,
        /// SVG) and reading the pixel size from the header. Unknown data gives <see cref="LayoutImageFormat.Unknown"/>
        /// and a zero size; EMF, WMF and SVG report the size their header declares (or zero).
        /// </summary>
        public static LayoutImage FromEncoded(ReadOnlyMemory<byte> data)
        {
            (LayoutImageFormat format, int w, int h) = ImageHeader.Probe(data.Span);
            return new LayoutImage(data, format, w, h);
        }
    }

    /// <summary>Reads image formats and pixel sizes from file headers (no decoding).</summary>
    internal static class ImageHeader
    {
        public static (LayoutImageFormat Format, int Width, int Height) Probe(ReadOnlySpan<byte> d)
        {
            try
            {
                if (d.Length >= 24 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
                {
                    return (LayoutImageFormat.Png, BigEndian32(d, 16), BigEndian32(d, 20));
                }
                if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
                {
                    (int jw, int jh) = Jpeg(d);
                    return (LayoutImageFormat.Jpeg, jw, jh);
                }
                if (d.Length >= 10 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F')
                {
                    return (LayoutImageFormat.Gif, d[6] | (d[7] << 8), d[8] | (d[9] << 8));
                }
                if (d.Length >= 26 && d[0] == (byte)'B' && d[1] == (byte)'M')
                {
                    return (LayoutImageFormat.Bmp, Math.Abs(Little32(d, 18)), Math.Abs(Little32(d, 22)));
                }
                if (d.Length >= 8 && ((d[0] == (byte)'I' && d[1] == (byte)'I' && d[2] == 42) || (d[0] == (byte)'M' && d[1] == (byte)'M' && d[3] == 42)))
                {
                    (int tw, int th) = Tiff(d);
                    return (LayoutImageFormat.Tiff, tw, th);
                }
                if (d.Length >= 44 && Little32(d, 0) == 1 && d[40] == 0x20 && d[41] == 0x45 && d[42] == 0x4D && d[43] == 0x46)
                {
                    // EMR_HEADER: rclBounds (device units).
                    return (LayoutImageFormat.Emf, Little32(d, 16) - Little32(d, 8) + 1, Little32(d, 20) - Little32(d, 12) + 1);
                }
                if (d.Length >= 22 && Little32(d, 0) == unchecked((int)0x9AC6CDD7))
                {
                    // Placeable WMF: bounding box in metafile units at 'inch' units per inch; report at 96 dpi.
                    int inch = d[14] | (d[15] << 8);
                    int ww = (short)(d[10] | (d[11] << 8)) - (short)(d[6] | (d[7] << 8));
                    int wh = (short)(d[12] | (d[13] << 8)) - (short)(d[8] | (d[9] << 8));
                    return inch > 0 ? (LayoutImageFormat.Wmf, ww * 96 / inch, wh * 96 / inch) : (LayoutImageFormat.Wmf, 0, 0);
                }
                if (d.Length >= 6 && (d[0] == 1 || d[0] == 2) && d[1] == 0 && d[2] == 9 && d[3] == 0)
                {
                    return (LayoutImageFormat.Wmf, 0, 0);
                }
                int start = d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? 3 : 0;
                ReadOnlySpan<byte> head = d.Slice(start, Math.Min(d.Length - start, 512));
                string text = System.Text.Encoding.UTF8.GetString(head).TrimStart();
                if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (text.StartsWith("<?xml", StringComparison.Ordinal) && text.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
                {
                    return (LayoutImageFormat.Svg, 0, 0);
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Truncated header.
            }
            return (LayoutImageFormat.Unknown, 0, 0);
        }

        private static int BigEndian32(ReadOnlySpan<byte> d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        private static int Little32(ReadOnlySpan<byte> d, int o) => d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);

        private static (int, int) Jpeg(ReadOnlySpan<byte> d)
        {
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF)
                {
                    i++;
                    continue;
                }
                byte marker = d[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }
                if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }
                int len = (d[i + 2] << 8) | d[i + 3];
                bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (sof)
                {
                    return ((d[i + 7] << 8) | d[i + 8], (d[i + 5] << 8) | d[i + 6]);
                }
                i += 2 + len;
            }
            return (0, 0);
        }

        private static int U16(ReadOnlySpan<byte> d, bool le, int o) => le ? d[o] | (d[o + 1] << 8) : (d[o] << 8) | d[o + 1];

        private static int U32(ReadOnlySpan<byte> d, bool le, int o) => le ? Little32(d, o) : BigEndian32(d, o);

        private static (int, int) Tiff(ReadOnlySpan<byte> d)
        {
            bool le = d[0] == (byte)'I';
            int ifd = U32(d, le, 4);
            int n = U16(d, le, ifd);
            int w = 0, h = 0;
            for (int k = 0; k < n; k++)
            {
                int e = ifd + 2 + (k * 12);
                int tag = U16(d, le, e);
                int type = U16(d, le, e + 2);
                int value = type == 3 ? U16(d, le, e + 8) : U32(d, le, e + 8);
                if (tag == 256)
                {
                    w = value;
                }
                else if (tag == 257)
                {
                    h = value;
                }
            }
            return (w, h);
        }
    }

    /// <summary>An image drawn into a destination rectangle, optionally cropped (crop in source pixels).</summary>
    public sealed class ImageOp : LayoutOp
    {
        public ImageOp(LayoutImage image, LayoutRect destination, LayoutRect? sourceCrop = null)
        {
            Image = image;
            Destination = destination;
            SourceCrop = sourceCrop;
        }

        public override LayoutOpKind Kind => LayoutOpKind.Image;

        public LayoutImage Image { get; }
        public LayoutRect Destination { get; }
        public LayoutRect? SourceCrop { get; }
    }

    /// <summary>Starts clipping to a rectangle until the matching <see cref="PopClipOp"/>.</summary>
    public sealed class PushClipOp : LayoutOp
    {
        public PushClipOp(LayoutRect rect) => Rect = rect;

        public override LayoutOpKind Kind => LayoutOpKind.PushClip;

        public LayoutRect Rect { get; }
    }

    /// <summary>Ends the innermost clip.</summary>
    public sealed class PopClipOp : LayoutOp
    {
        public static PopClipOp Instance { get; } = new PopClipOp();

        private PopClipOp()
        {
        }

        public override LayoutOpKind Kind => LayoutOpKind.PopClip;
    }

    /// <summary>A clickable area: an external URI, or an internal bookmark/page target.</summary>
    public sealed class LinkOp : LayoutOp
    {
        public LinkOp(LayoutRect rect, string? uri, string? anchor = null, int? targetPage = null)
        {
            Rect = rect;
            Uri = uri;
            Anchor = anchor;
            TargetPage = targetPage;
        }

        public override LayoutOpKind Kind => LayoutOpKind.Link;

        public LayoutRect Rect { get; }
        public string? Uri { get; }
        public string? Anchor { get; }
        public int? TargetPage { get; }
    }

    /// <summary>The paint-ordered operations of one page. Immutable.</summary>
    public sealed class LayoutDisplayList : IReadOnlyList<LayoutOp>
    {
        private readonly LayoutOp[] _ops;

        public LayoutDisplayList(IEnumerable<LayoutOp> ops, double widthPt, double heightPt)
        {
            ArgumentNullException.ThrowIfNull(ops);
            _ops = new List<LayoutOp>(ops).ToArray();
            foreach (LayoutOp op in _ops)
            {
                if (op is null)
                {
                    throw new ArgumentException("Display list contains a null op.", nameof(ops));
                }
            }
            WidthPt = widthPt;
            HeightPt = heightPt;
        }

        public double WidthPt { get; }

        public double HeightPt { get; }

        public int Count => _ops.Length;

        public LayoutOp this[int index] => _ops[index];

        public IEnumerator<LayoutOp> GetEnumerator() => ((IEnumerable<LayoutOp>)_ops).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _ops.GetEnumerator();

        /// <summary>Every distinct font face used by glyph runs, with the glyph ids used (for subsetting).</summary>
        public IReadOnlyDictionary<FontFace, IReadOnlySet<int>> GetUsedGlyphs()
        {
            Dictionary<FontFace, HashSet<int>> map = new Dictionary<FontFace, HashSet<int>>(ReferenceEqualityComparer.Instance);
            foreach (LayoutOp op in _ops)
            {
                if (op is GlyphRunOp run)
                {
                    if (!map.TryGetValue(run.Font, out HashSet<int>? set))
                    {
                        set = new HashSet<int>();
                        map[run.Font] = set;
                    }
                    foreach (PositionedGlyph g in run.Glyphs)
                    {
                        set.Add(g.GlyphId);
                    }
                }
            }
            Dictionary<FontFace, IReadOnlySet<int>> result = new Dictionary<FontFace, IReadOnlySet<int>>(ReferenceEqualityComparer.Instance);
            foreach (KeyValuePair<FontFace, HashSet<int>> kv in map)
            {
                result[kv.Key] = kv.Value;
            }
            return result;
        }
    }
}
