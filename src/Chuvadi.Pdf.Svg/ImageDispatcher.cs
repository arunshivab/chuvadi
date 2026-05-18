// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 (Images), §8.10 (Form XObjects)
// PHASE: Phase 2.0 — SVG export

using System;
using System.IO;
using System.IO.Compression;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

internal static class ImageDispatcher
{
    /// <summary>
    /// Resolves a named XObject from /Resources and emits SVG.
    /// For image XObjects: emits an <c>&lt;image&gt;</c> element.
    /// For Form XObjects: not supported in v1 (would require recursive content stream walking).
    /// </summary>
    internal static void DrawXObject(string name, SvgGraphicsState s, SvgWriter w,
        PdfDictionary? resources, PdfDocument doc)
    {
        if (resources is null) { return; }
        if (!resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xobjVal))
        {
            return;
        }
        PdfDictionary? xobjects = doc.Objects.ResolveAs<PdfDictionary>(xobjVal);
        if (xobjects is null) { return; }
        if (!xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? imgRef))
        {
            return;
        }
        if (doc.Objects.Resolve(imgRef) is not PdfStream imgStream) { return; }

        if (!imgStream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? subtype)
            || subtype is not PdfName subtypeName)
        {
            return;
        }
        if (subtypeName.Value != "Image")
        {
            // Form XObject etc. — skip for v1.
            return;
        }

        DrawImageStream(imgStream, s, w);
    }

    private static void DrawImageStream(PdfStream imgStream, SvgGraphicsState s, SvgWriter w)
    {
        // Image's CTM defines its placement: a unit square is mapped to the image's
        // destination rectangle. CTM in PDF for images: [w 0 0 h x y].
        Mat2x3 m = s.Ctm;

        // SVG <image> accepts JPEG / PNG. We need a data URL or external URL.
        string? dataUrl = TryBuildDataUrl(imgStream);
        if (dataUrl is null) { return; }

        // The unit-square-to-destination CTM goes straight into a <g transform>.
        string transform = m.ToSvgMatrix("0.######");

        // Emit at (0,0) with width=1, height=1 (the CTM scales it).
        w.OpenGroup(transform);
        w.EmitImage(dataUrl, 0, 0, 1, 1);
        w.CloseGroup();
    }

    private static string? TryBuildDataUrl(PdfStream imgStream)
    {
        PdfDictionary dict = imgStream.Dictionary;
        string? filter = ExtractFilterName(dict);
        byte[] rawBytes = imgStream.RawBytes;

        // JPEG (DCT-compressed): pass through bytes as image/jpeg.
        if (filter == "DCTDecode")
        {
            return "data:image/jpeg;base64," + Convert.ToBase64String(rawBytes);
        }

        // FlateDecode: pixel data, must repack as PNG for the browser.
        if (filter == "FlateDecode")
        {
            return TryBuildPngFromFlateImage(imgStream);
        }

        // Other filters (CCITTFaxDecode, JBIG2Decode) not supported in v1.
        return null;
    }

    private static string? ExtractFilterName(PdfDictionary dict)
    {
        if (!dict.TryGetValue(PdfName.Intern("Filter"), out PdfPrimitive? f)) { return null; }
        return f switch
        {
            PdfName n => n.Value,
            PdfArray arr when arr.Count > 0 && arr[0] is PdfName n2 => n2.Value,
            _ => null,
        };
    }

    private static string? TryBuildPngFromFlateImage(PdfStream imgStream)
    {
        PdfDictionary dict = imgStream.Dictionary;
        int width = IntOf(dict, "Width", -1);
        int height = IntOf(dict, "Height", -1);
        int bpc = IntOf(dict, "BitsPerComponent", 8);
        if (width <= 0 || height <= 0 || bpc != 8) { return null; }

        int channels = 3;
        if (dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs))
        {
            if (cs is PdfName csn)
            {
                channels = csn.Value switch
                {
                    "DeviceGray" => 1,
                    "DeviceRGB"  => 3,
                    "DeviceCMYK" => 4,
                    _ => 3,
                };
            }
        }

        byte[] decoded;
        try { decoded = StreamDecoder.Decode(imgStream); }
        catch { return null; }

        // CMYK -> RGB conversion for SVG output.
        byte[] rgbData;
        if (channels == 4)
        {
            rgbData = CmykToRgbBytes(decoded, width, height);
            channels = 3;
        }
        else
        {
            rgbData = decoded;
        }

        byte[] png = EncodePng(rgbData, width, height, channels);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static int IntOf(PdfDictionary d, string key, int fallback)
    {
        if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is PdfInteger i)
        {
            return i.Value;
        }
        return fallback;
    }

    private static byte[] CmykToRgbBytes(byte[] cmyk, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int n = width * height;
        for (int i = 0; i < n; i++)
        {
            double c = cmyk[i * 4] / 255.0;
            double m = cmyk[i * 4 + 1] / 255.0;
            double y = cmyk[i * 4 + 2] / 255.0;
            double k = cmyk[i * 4 + 3] / 255.0;
            rgb[i * 3]     = (byte)((1 - c) * (1 - k) * 255);
            rgb[i * 3 + 1] = (byte)((1 - m) * (1 - k) * 255);
            rgb[i * 3 + 2] = (byte)((1 - y) * (1 - k) * 255);
        }
        return rgb;
    }

    // ── Minimal PNG encoder for embedding pixel data into SVG. ───────────

    private static byte[] EncodePng(byte[] pixels, int width, int height, int channels)
    {
        using MemoryStream ms = new();
        // Signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        // IHDR
        byte[] ihdr = new byte[13];
        WriteInt32BigEndian(ihdr, 0, width);
        WriteInt32BigEndian(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = (byte)(channels switch { 1 => 0, 3 => 2, _ => 2 });
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);
        // IDAT: filter each scanline with byte 0 (none), then deflate.
        using MemoryStream filtered = new();
        int stride = width * channels;
        for (int row = 0; row < height; row++)
        {
            filtered.WriteByte(0);
            filtered.Write(pixels, row * stride, stride);
        }
        byte[] deflated = Deflate(filtered.ToArray());
        WriteChunk(ms, "IDAT", deflated);
        // IEND
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        byte[] lenBytes = new byte[4];
        WriteInt32BigEndian(lenBytes, 0, data.Length);
        s.Write(lenBytes);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteInt32BigEndian(crcBytes, 0, (int)crc);
        s.Write(crcBytes);
    }

    private static void WriteInt32BigEndian(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static byte[] Deflate(byte[] data)
    {
        // ZLIB wrapper: 2 header bytes + deflate stream + 4-byte Adler-32.
        using MemoryStream ms = new();
        ms.WriteByte(0x78);   // CMF: deflate, 32K window
        ms.WriteByte(0x9C);   // FLG: default compression, no dict, fcheck
        using (DeflateStream ds = new(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        const uint Mod = 65521;
        foreach (byte by in data)
        {
            a = (a + by) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        uint[] t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in type) { c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8); }
        foreach (byte b in data) { c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8); }
        return c ^ 0xFFFFFFFF;
    }
}
