using System;
using System.Buffers.Binary;
using System.IO;

namespace Chuvadi.Print
{
    public enum VectorImageFormat
    {
        Png = 0,
        Jpeg = 1
    }

    /// <summary>Layouts for <see cref="VectorImage.FromPixels"/>.</summary>
    public enum VectorPixelFormat
    {
        /// <summary>One byte per pixel, 0 black to 255 white.</summary>
        Gray8 = 0,

        /// <summary>Three bytes per pixel: red, green, blue (sRGB).</summary>
        Rgb24 = 1,

        /// <summary>Two bytes per pixel: grey, then alpha (0 transparent, 255 opaque; not premultiplied).</summary>
        GrayAlpha16 = 2,

        /// <summary>Four bytes per pixel: red, green, blue (sRGB), then alpha (not premultiplied).</summary>
        Rgba32 = 3
    }

    /// <summary>
    /// An encoded image (PNG or JPEG) for <see cref="VectorImageElement"/>. Use the same instance wherever the
    /// image repeats so it is stored once. The pixel size and resolution are read from the image data.
    /// </summary>
    public sealed class VectorImage
    {
        private VectorImage(VectorImageFormat format, byte[] data, int pixelWidth, int pixelHeight, double dpiX, double dpiY, int components, bool hasAlpha)
        {
            HasAlpha = hasAlpha;
            Format = format;
            Data = data;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiX = dpiX;
            DpiY = dpiY;
            Components = components;
        }

        public VectorImageFormat Format { get; }

        /// <summary>The encoded image.</summary>
        public byte[] Data { get; }

        public int PixelWidth { get; }
        public int PixelHeight { get; }

        /// <summary>The resolution the image data states, or 96 when it states none.</summary>
        public double DpiX { get; }

        public double DpiY { get; }

        /// <summary>Colour components: 1 grey, 3 colour, 4 CMYK (JPEG only). For PNG, alpha is not counted.</summary>
        public int Components { get; }

        /// <summary>Whether the image has transparency: a PNG with an alpha channel or a tRNS chunk. JPEG never has.</summary>
        public bool HasAlpha { get; }

        /// <summary>A PNG image.</summary>
        /// <exception cref="InvalidDataException">The data is not a PNG.</exception>
        public static VectorImage FromPng(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            ReadOnlySpan<byte> sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(sig)) throw new InvalidDataException("Not a PNG image.");
            if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
                throw new InvalidDataException("PNG image without IHDR.");
            int width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16));
            int height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
            if (width <= 0 || height <= 0) throw new InvalidDataException("PNG image with no pixels.");
            byte colorType = data[25];
            int components = colorType is 0 or 4 ? 1 : 3;
            bool hasAlpha = colorType is 4 or 6;
            double dpiX = 96, dpiY = 96;
            int pos = 8;
            while (pos + 12 <= data.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
                if (length < 0 || pos + 12L + length > data.Length) break;
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                if (type == "pHYs" && length >= 9 && data[pos + 16] == 1)
                {
                    uint px = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8));
                    uint py = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 12));
                    if (px > 0 && py > 0)
                    {
                        dpiX = px * 0.0254;
                        dpiY = py * 0.0254;
                    }
                }
                if (type == "tRNS") hasAlpha = true;
                if (type == "IEND") break;
                pos += 12 + length;
            }
            return new VectorImage(VectorImageFormat.Png, data, width, height, dpiX, dpiY, components, hasAlpha);
        }

        /// <summary>A baseline or progressive JPEG image.</summary>
        /// <exception cref="InvalidDataException">The data is not a JPEG, or has no frame header.</exception>
        public static VectorImage FromJpeg(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) throw new InvalidDataException("Not a JPEG image.");
            double dpiX = 96, dpiY = 96;
            int pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xFF)
                {
                    pos++;
                    continue;
                }
                byte marker = data[pos + 1];
                if (marker == 0xFF)
                {
                    pos++;
                    continue;
                }
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    pos += 2;
                    continue;
                }
                if (marker == 0xD9 || marker == 0xDA) break;
                int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2));
                if (length < 2 || pos + 2 + length > data.Length) break;
                int seg = pos + 4;
                if (marker == 0xE0 && length >= 16 && data[seg] == (byte)'J' && data[seg + 1] == (byte)'F'
                    && data[seg + 2] == (byte)'I' && data[seg + 3] == (byte)'F' && data[seg + 4] == 0)
                {
                    byte units = data[seg + 7];
                    int dx = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(seg + 8));
                    int dy = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(seg + 10));
                    if (dx > 0 && dy > 0 && (units == 1 || units == 2))
                    {
                        dpiX = units == 1 ? dx : dx * 2.54;
                        dpiY = units == 1 ? dy : dy * 2.54;
                    }
                }
                bool isFrame = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrame && length >= 8)
                {
                    int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(seg + 1));
                    int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(seg + 3));
                    int components = data[seg + 5];
                    if (width <= 0 || height <= 0) throw new InvalidDataException("JPEG image with no pixels.");
                    return new VectorImage(VectorImageFormat.Jpeg, data, width, height, dpiX, dpiY, components, hasAlpha: false);
                }
                pos += 2 + length;
            }
            throw new InvalidDataException("JPEG image without a frame header.");
        }

        /// <summary>
        /// Encodes pixels as a PNG. <paramref name="pixels"/> holds the rows top to bottom with no padding
        /// (width × 1, 2, 3 or 4 bytes each, as <paramref name="format"/> says).
        /// </summary>
        public static VectorImage FromPixels(int width, int height, VectorPixelFormat format, byte[] pixels, double dpi = 96)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (pixels is null) throw new ArgumentNullException(nameof(pixels));
            if (!(dpi > 0) || double.IsInfinity(dpi)) throw new ArgumentOutOfRangeException(nameof(dpi));
            byte colorType = format switch
            {
                VectorPixelFormat.Gray8 => PngEncoder.ColorTypeGray,
                VectorPixelFormat.Rgb24 => PngEncoder.ColorTypeRgb,
                VectorPixelFormat.GrayAlpha16 => PngEncoder.ColorTypeGrayAlpha,
                VectorPixelFormat.Rgba32 => PngEncoder.ColorTypeRgba,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            var encoder = new PngEncoder(width, height, colorType, dpi, dpi);
            int rowBytes = encoder.RowBytes;
            if ((long)rowBytes * height != pixels.Length)
                throw new ArgumentException("Expected " + ((long)rowBytes * height) + " bytes of pixels, got " + pixels.Length + ".", nameof(pixels));
            using var ms = new MemoryStream();
            encoder.Write(ms, (y, row) => pixels.AsSpan(y * rowBytes, rowBytes).CopyTo(row));
            return FromPng(ms.ToArray());
        }
    }
}
