using System;

namespace Chuvadi.Print
{
    /// <summary>Pixel layout of a <see cref="RasterPage"/>.</summary>
    public enum RasterPixelFormat
    {
        /// <summary>4 bytes per pixel: blue, green, red, alpha (straight, not premultiplied).</summary>
        Bgra32,
        /// <summary>3 bytes per pixel: red, green, blue.</summary>
        Rgb24,
        /// <summary>1 byte per pixel: 0 = black, 255 = white.</summary>
        Gray8,
        /// <summary>
        /// 1 bit per pixel, most-significant bit first within each byte. A set bit (1) is black (ink);
        /// a clear bit (0) is white (paper). Unused trailing bits of each row are 0.
        /// </summary>
        BlackWhite1
    }

    /// <summary>A clockwise rotation applied to page content. All four quarter turns are represented.</summary>
    public enum PageRotation
    {
        None,
        Clockwise90,
        Rotate180,
        Clockwise270
    }

    /// <summary>
    /// What an <see cref="IRasterDocument"/> is asked to produce for one page.
    /// <see cref="DpiX"/> and <see cref="DpiY"/> apply to the axes of the OUTPUT raster
    /// (after <see cref="Rotation"/>), so the output is
    /// round(rotatedWidthPoints / 72 * DpiX) by round(rotatedHeightPoints / 72 * DpiY) pixels.
    /// </summary>
    public sealed class RasterRequest
    {
        private double _dpiX = 300;
        private double _dpiY = 300;

        /// <summary>Horizontal resolution of the output raster, in pixels per inch. Must be positive.</summary>
        public double DpiX
        {
            get => _dpiX;
            init => _dpiX = Positive(value, nameof(DpiX));
        }

        /// <summary>Vertical resolution of the output raster, in pixels per inch. Must be positive.</summary>
        public double DpiY
        {
            get => _dpiY;
            init => _dpiY = Positive(value, nameof(DpiY));
        }

        /// <summary>
        /// Preferred output format. A rasteriser should honour it when it can; otherwise it may return
        /// <see cref="RasterPixelFormat.Bgra32"/>, <see cref="RasterPixelFormat.Rgb24"/> or
        /// <see cref="RasterPixelFormat.Gray8"/> and the print job converts.
        /// </summary>
        public RasterPixelFormat PixelFormat { get; init; } = RasterPixelFormat.Bgra32;

        /// <summary>Clockwise rotation applied to the page before rasterising.</summary>
        public PageRotation Rotation { get; init; } = PageRotation.None;

        /// <summary>Render annotations (comments, stamps, markup).</summary>
        public bool IncludeAnnotations { get; init; } = true;

        /// <summary>Render interactive form-field appearances.</summary>
        public bool IncludeFormFields { get; init; } = true;

        /// <summary>Smooth edges of text and vector graphics.</summary>
        public bool AntiAlias { get; init; } = true;

        private static double Positive(double value, string name)
        {
            if (!(value > 0) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "DPI must be a positive finite number.");
            return value;
        }
    }
}
