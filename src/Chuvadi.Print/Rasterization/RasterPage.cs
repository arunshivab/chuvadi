using System;
using System.Buffers;

namespace Chuvadi.Print
{
    /// <summary>
    /// One rasterised page: a pixel buffer plus its geometry. Pages created with <see cref="Rent"/>
    /// borrow their buffer from the shared array pool and return it on <see cref="Dispose"/>,
    /// so a print job holds at most one page buffer at a time.
    /// </summary>
    public sealed class RasterPage : IDisposable
    {
        private byte[]? _buffer;
        private readonly bool _pooled;

        public int PixelWidth { get; }
        public int PixelHeight { get; }

        /// <summary>Bytes per row, including any padding.</summary>
        public int Stride { get; }

        public RasterPixelFormat PixelFormat { get; }
        public double DpiX { get; }
        public double DpiY { get; }

        /// <summary>True once <see cref="Dispose"/> has been called.</summary>
        public bool IsDisposed => _buffer is null;

        /// <summary>Wraps a caller-owned pixel buffer. The buffer is never returned to any pool.</summary>
        public RasterPage(int pixelWidth, int pixelHeight, int stride, RasterPixelFormat pixelFormat,
                          double dpiX, double dpiY, byte[] pixels)
            : this(pixelWidth, pixelHeight, stride, pixelFormat, dpiX, dpiY,
                   pixels ?? throw new ArgumentNullException(nameof(pixels)), pooled: false)
        {
        }

        private RasterPage(int pixelWidth, int pixelHeight, int stride, RasterPixelFormat pixelFormat,
                           double dpiX, double dpiY, byte[] pixels, bool pooled)
        {
            if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Width must be positive.");
            if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Height must be positive.");
            if (stride < MinimumStride(pixelWidth, pixelFormat))
                throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than one row of pixels.");
            if (!(dpiX > 0)) throw new ArgumentOutOfRangeException(nameof(dpiX));
            if (!(dpiY > 0)) throw new ArgumentOutOfRangeException(nameof(dpiY));
            if ((long)stride * pixelHeight > pixels.LongLength)
                throw new ArgumentException("Pixel buffer is smaller than Stride x PixelHeight.", nameof(pixels));

            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Stride = stride;
            PixelFormat = pixelFormat;
            DpiX = dpiX;
            DpiY = dpiY;
            _buffer = pixels;
            _pooled = pooled;
        }

        /// <summary>
        /// Creates a page whose zero-filled buffer is rented from <see cref="ArrayPool{T}.Shared"/>.
        /// The stride is the minimum for the format (no padding).
        /// </summary>
        public static RasterPage Rent(int pixelWidth, int pixelHeight, RasterPixelFormat pixelFormat, double dpiX, double dpiY)
        {
            if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Width must be positive.");
            if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Height must be positive.");
            int stride = MinimumStride(pixelWidth, pixelFormat);
            long size = (long)stride * pixelHeight;
            if (size > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Raster is too large.");
            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)size);
            Array.Clear(buffer, 0, (int)size);
            return new RasterPage(pixelWidth, pixelHeight, stride, pixelFormat, dpiX, dpiY, buffer, pooled: true);
        }

        /// <summary>The smallest legal stride for a row of <paramref name="pixelWidth"/> pixels.</summary>
        public static int MinimumStride(int pixelWidth, RasterPixelFormat pixelFormat)
        {
            if (pixelWidth < 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
            return pixelFormat switch
            {
                RasterPixelFormat.Bgra32 => checked(pixelWidth * 4),
                RasterPixelFormat.Rgb24 => checked(pixelWidth * 3),
                RasterPixelFormat.Gray8 => pixelWidth,
                RasterPixelFormat.BlackWhite1 => (pixelWidth + 7) / 8,
                _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat))
            };
        }

        /// <summary>The pixel data: <see cref="PixelHeight"/> rows of <see cref="Stride"/> bytes.</summary>
        public Span<byte> Pixels
        {
            get
            {
                byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(RasterPage));
                return new Span<byte>(buffer, 0, Stride * PixelHeight);
            }
        }

        /// <summary>One row of pixel data (<see cref="Stride"/> bytes).</summary>
        public Span<byte> GetRow(int y)
        {
            if ((uint)y >= (uint)PixelHeight) throw new ArgumentOutOfRangeException(nameof(y));
            byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(RasterPage));
            return new Span<byte>(buffer, y * Stride, Stride);
        }

        public void Dispose()
        {
            byte[]? buffer = _buffer;
            if (buffer is null) return;
            _buffer = null;
            if (_pooled) ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
