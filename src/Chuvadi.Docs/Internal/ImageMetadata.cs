using System;
using System.Buffers.Binary;
using System.IO;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Pure-BCL inspection of raw image bytes: detects the format from magic bytes, and reads
/// pixel dimensions and DPI for PNG, JPEG, BMP, GIF, and TIFF. Used to auto-size images when
/// the caller doesn't supply explicit display dimensions. No System.Drawing dependency, so it
/// runs identically on every platform.
/// </summary>
internal static class ImageMetadata
{
    /// <summary>Result of inspecting image bytes.</summary>
    internal readonly record struct Info(
        string ContentType,
        string Extension,
        int PixelWidth,
        int PixelHeight,
        double DpiX,
        double DpiY)
    {
        /// <summary>Display width in points using the image's DPI (falls back to 96 DPI).</summary>
        public double WidthPt => PixelWidth / (DpiX > 0 ? DpiX : 96.0) * 72.0;

        /// <summary>Display height in points using the image's DPI (falls back to 96 DPI).</summary>
        public double HeightPt => PixelHeight / (DpiY > 0 ? DpiY : 96.0) * 72.0;
    }

    /// <summary>
    /// Detects content type from magic bytes. Returns null if unrecognized. Recognizes
    /// PNG, JPEG, BMP, GIF, TIFF (both endiannesses), and WMF/EMF.
    /// </summary>
    public static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length < 4) return null;

        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        // GIF: "GIF8"
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return "image/gif";
        // BMP: "BM"
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";
        // TIFF: "II*\0" (little-endian) or "MM\0*" (big-endian)
        if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
            (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))
            return "image/tiff";
        // EMF: a bit further in, but check the common signature " EMF" at offset 40
        if (bytes.Length >= 44 && bytes[40] == 0x20 && bytes[41] == 0x45 && bytes[42] == 0x4D && bytes[43] == 0x46)
            return "image/x-emf";
        // WMF: D7 CD C6 9A (placeable) or 01 00 09 00
        if (bytes[0] == 0xD7 && bytes[1] == 0xCD && bytes[2] == 0xC6 && bytes[3] == 0x9A)
            return "image/x-wmf";

        return null;
    }

    /// <summary>Maps a content type to the file extension used for the package media part.</summary>
    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpeg",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        "image/tiff" => "tiff",
        "image/x-emf" => "emf",
        "image/x-wmf" => "wmf",
        _ => "bin",
    };

    /// <summary>Maps a common file extension (with or without dot) to a content type, or null.</summary>
    public static string? ContentTypeForExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" or "jpe" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" or "dib" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "emf" => "image/x-emf",
            "wmf" => "image/x-wmf",
            _ => null,
        };
    }

    /// <summary>
    /// Inspects image bytes and returns format, pixel dimensions, and DPI. Throws
    /// <see cref="InvalidDataException"/> if the format isn't recognized or the header
    /// can't be parsed. DPI defaults to 96 when the format carries no resolution data.
    /// </summary>
    public static Info Inspect(byte[] bytes)
    {
        var contentType = DetectContentType(bytes)
            ?? throw new InvalidDataException("Unrecognized image format (PNG, JPEG, BMP, GIF, TIFF supported for auto-sizing).");
        var ext = ExtensionFor(contentType);

        return contentType switch
        {
            "image/png" => InspectPng(bytes, ext),
            "image/jpeg" => InspectJpeg(bytes, ext),
            "image/bmp" => InspectBmp(bytes, ext),
            "image/gif" => InspectGif(bytes, ext),
            "image/tiff" => InspectTiff(bytes, ext),
            // Vector formats (EMF/WMF) have no pixel grid; report zero so callers must
            // supply explicit dimensions.
            _ => new Info(contentType, ext, 0, 0, 96, 96),
        };
    }

    // ---- PNG --------------------------------------------------------------------------

    private static Info InspectPng(byte[] b, string ext)
    {
        // IHDR starts at offset 16: width (4 BE), height (4 BE).
        if (b.Length < 24) throw new InvalidDataException("Truncated PNG header.");
        int width = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20, 4));

        // Scan for a pHYs chunk for DPI (pixels per metre). Chunks: length(4 BE) type(4) data crc(4).
        double dpiX = 96, dpiY = 96;
        int pos = 8;
        while (pos + 12 <= b.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
            int dataStart = pos + 8;
            if (type == "pHYs" && dataStart + 9 <= b.Length)
            {
                int ppuX = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(dataStart, 4));
                int ppuY = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(dataStart + 4, 4));
                byte unit = b[dataStart + 8];
                if (unit == 1 && ppuX > 0 && ppuY > 0) // unit = metres
                {
                    dpiX = ppuX * 0.0254;
                    dpiY = ppuY * 0.0254;
                }
                break;
            }
            if (type == "IDAT" || type == "IEND") break; // past the header chunks
            pos = dataStart + len + 4; // skip data + crc
            if (len < 0) break;
        }
        return new Info("image/png", ext, width, height, dpiX, dpiY);
    }

    // ---- JPEG -------------------------------------------------------------------------

    private static Info InspectJpeg(byte[] b, string ext)
    {
        double dpiX = 96, dpiY = 96;
        int width = 0, height = 0;
        int pos = 2; // skip SOI (FF D8)

        while (pos + 4 < b.Length)
        {
            if (b[pos] != 0xFF) { pos++; continue; }
            byte marker = b[pos + 1];
            int segLen = (b[pos + 2] << 8) | b[pos + 3];

            // APP0 JFIF density.
            if (marker == 0xE0 && pos + 18 < b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
                if (id == "JFIF")
                {
                    byte units = b[pos + 11];
                    int dx = (b[pos + 12] << 8) | b[pos + 13];
                    int dy = (b[pos + 14] << 8) | b[pos + 15];
                    if (dx > 0 && dy > 0)
                    {
                        if (units == 1) { dpiX = dx; dpiY = dy; }          // dots per inch
                        else if (units == 2) { dpiX = dx * 2.54; dpiY = dy * 2.54; } // dots per cm
                    }
                }
            }
            // SOF0..SOF15 (except DHT=C4, DNL=C8, DAC=CC) carry dimensions.
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (pos + 9 < b.Length)
                {
                    height = (b[pos + 5] << 8) | b[pos + 6];
                    width = (b[pos + 7] << 8) | b[pos + 8];
                }
                break;
            }
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2; // markers without length
                continue;
            }
            pos += 2 + segLen;
        }
        if (width == 0 || height == 0)
            throw new InvalidDataException("Could not read JPEG dimensions.");
        return new Info("image/jpeg", ext, width, height, dpiX, dpiY);
    }

    // ---- BMP --------------------------------------------------------------------------

    private static Info InspectBmp(byte[] b, string ext)
    {
        if (b.Length < 42) throw new InvalidDataException("Truncated BMP header.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18, 4));
        int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22, 4)));
        int ppmX = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(38, 4));
        int ppmY = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(42 <= b.Length ? 42 : 38, 4));
        double dpiX = ppmX > 0 ? ppmX * 0.0254 : 96;
        double dpiY = ppmY > 0 ? ppmY * 0.0254 : 96;
        return new Info("image/bmp", ext, width, height, dpiX, dpiY);
    }

    // ---- GIF --------------------------------------------------------------------------

    private static Info InspectGif(byte[] b, string ext)
    {
        if (b.Length < 10) throw new InvalidDataException("Truncated GIF header.");
        int width = b[6] | (b[7] << 8);
        int height = b[8] | (b[9] << 8);
        return new Info("image/gif", ext, width, height, 96, 96); // GIF carries no DPI
    }

    // ---- TIFF -------------------------------------------------------------------------

    private static Info InspectTiff(byte[] b, string ext)
    {
        bool le = b[0] == 0x49;
        uint ReadU32(int o) => le
            ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
        ushort ReadU16(int o) => le
            ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o, 2));

        uint ifdOffset = ReadU32(4);
        if (ifdOffset + 2 > b.Length) throw new InvalidDataException("Bad TIFF IFD offset.");
        int entryCount = ReadU16((int)ifdOffset);

        long width = 0, height = 0;
        double xRes = 0, yRes = 0;
        int resUnit = 2; // default inch

        long GetValue(int entryOff, ushort type)
        {
            // For SHORT/LONG values that fit in the 4-byte value field.
            return type == 3 ? ReadU16(entryOff + 8) : ReadU32(entryOff + 8);
        }
        double GetRational(int entryOff)
        {
            uint off = ReadU32(entryOff + 8);
            if (off + 8 > b.Length) return 0;
            uint num = ReadU32((int)off);
            uint den = ReadU32((int)off + 4);
            return den == 0 ? 0 : (double)num / den;
        }

        for (int i = 0; i < entryCount; i++)
        {
            int entryOff = (int)ifdOffset + 2 + i * 12;
            if (entryOff + 12 > b.Length) break;
            ushort tag = ReadU16(entryOff);
            ushort type = ReadU16(entryOff + 2);
            switch (tag)
            {
                case 256: width = GetValue(entryOff, type); break;   // ImageWidth
                case 257: height = GetValue(entryOff, type); break;  // ImageLength
                case 282: xRes = GetRational(entryOff); break;       // XResolution
                case 283: yRes = GetRational(entryOff); break;       // YResolution
                case 296: resUnit = (int)GetValue(entryOff, type); break; // ResolutionUnit
            }
        }
        double dpiX = 96, dpiY = 96;
        if (xRes > 0) dpiX = resUnit == 3 ? xRes * 2.54 : xRes; // 3 = cm
        if (yRes > 0) dpiY = resUnit == 3 ? yRes * 2.54 : yRes;
        if (width == 0 || height == 0)
            throw new InvalidDataException("Could not read TIFF dimensions.");
        return new Info("image/tiff", ext, (int)width, (int)height, dpiX, dpiY);
    }
}
