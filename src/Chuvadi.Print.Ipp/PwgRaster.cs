using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chuvadi.Print.Ipp
{
    /// <summary>PWG Raster colour spaces used by IPP Everywhere printers (pwg-raster-document-type-supported).</summary>
    public enum PwgColorSpace
    {
        /// <summary>"black_1": 1 bit per pixel, 1 = black ink, 0 = white paper.</summary>
        Black1,
        /// <summary>"sgray_8": 8-bit gray, 0 = black, 255 = white.</summary>
        SGray8,
        /// <summary>"srgb_8": 8 bits per channel R, G, B.</summary>
        SRgb8
    }

    /// <summary>The fields of a PWG Raster page header (PWG 5102.4) that a producer sets.</summary>
    public sealed class PwgPageHeader
    {
        public const int Size = 1796;

        public PwgColorSpace ColorSpace { get; init; } = PwgColorSpace.SGray8;
        public int Width { get; init; }
        public int Height { get; init; }
        public int DpiX { get; init; } = 300;
        public int DpiY { get; init; } = 300;

        /// <summary>Media size in points (PageSize).</summary>
        public int PageWidthPoints { get; init; }
        public int PageHeightPoints { get; init; }

        /// <summary>PWG self-describing media name, e.g. "iso_a4_210x297mm" (cupsPageSizeName).</summary>
        public string PageSizeName { get; init; } = string.Empty;

        public bool Duplex { get; init; }

        /// <summary>Short-edge binding (Tumble) when <see cref="Duplex"/> is set.</summary>
        public bool Tumble { get; init; }

        public int NumCopies { get; init; } = 1;

        /// <summary>Total pages in the stream (0 = unknown).</summary>
        public int TotalPageCount { get; init; }

        /// <summary>1 or -1: whether the producer mirrored the page across the feed (cross-feed) or feed direction.</summary>
        public int CrossFeedTransform { get; init; } = 1;
        public int FeedTransform { get; init; } = 1;

        /// <summary>PrintQuality: 0 = default, 3 draft, 4 normal, 5 high.</summary>
        public int PrintQuality { get; init; }

        public string MediaType { get; init; } = string.Empty;

        public int BitsPerPixel => ColorSpace switch { PwgColorSpace.Black1 => 1, PwgColorSpace.SGray8 => 8, _ => 24 };
        public int BytesPerLine => (Width * BitsPerPixel + 7) / 8;

        /// <summary>Bytes per compression unit: whole pixels for 8/24-bit, single bytes for 1-bit.</summary>
        internal int UnitBytes => ColorSpace switch { PwgColorSpace.Black1 => 1, PwgColorSpace.SGray8 => 1, _ => 3 };

        /// <summary>The value of a unit that is "white" (used for fill-to-end-of-line).</summary>
        internal byte WhiteByte => ColorSpace == PwgColorSpace.Black1 ? (byte)0x00 : (byte)0xFF;

        internal static int ColorSpaceCode(PwgColorSpace space) => space switch
        {
            PwgColorSpace.Black1 => 3,
            PwgColorSpace.SGray8 => 18,
            _ => 19
        };

        internal byte[] Encode()
        {
            var h = new byte[Size];
            WriteString(h, 128, 64, MediaType);                    // MediaType (after MediaClass[64], MediaColor[64])
            WriteUInt(h, 272, Duplex ? 1u : 0u);                   // Duplex
            WriteUInt(h, 276, (uint)DpiX);                         // HWResolution[0]
            WriteUInt(h, 280, (uint)DpiY);                         // HWResolution[1]
            WriteUInt(h, 340, (uint)NumCopies);                    // NumCopies
            WriteUInt(h, 352, (uint)PageWidthPoints);              // PageSize[0]
            WriteUInt(h, 356, (uint)PageHeightPoints);             // PageSize[1]
            WriteUInt(h, 368, Tumble ? 1u : 0u);                   // Tumble
            WriteUInt(h, 372, (uint)Width);                        // cupsWidth
            WriteUInt(h, 376, (uint)Height);                       // cupsHeight
            WriteUInt(h, 384, ColorSpace == PwgColorSpace.Black1 ? 1u : 8u); // cupsBitsPerColor
            WriteUInt(h, 388, (uint)BitsPerPixel);                 // cupsBitsPerPixel
            WriteUInt(h, 392, (uint)BytesPerLine);                 // cupsBytesPerLine
            WriteUInt(h, 396, 0);                                  // cupsColorOrder: chunky
            WriteUInt(h, 400, (uint)ColorSpaceCode(ColorSpace));   // cupsColorSpace
            WriteUInt(h, 420, ColorSpace == PwgColorSpace.SRgb8 ? 3u : 1u); // cupsNumColors
            WriteUInt(h, 452, (uint)TotalPageCount);               // cupsInteger[0] TotalPageCount
            WriteInt(h, 456, CrossFeedTransform);                  // cupsInteger[1] CrossFeedTransform
            WriteInt(h, 460, FeedTransform);                       // cupsInteger[2] FeedTransform
            WriteUInt(h, 484, (uint)PrintQuality);                 // cupsInteger[8] PrintQuality
            WriteString(h, 1668, 64, string.Empty);                // cupsRenderingIntent (default)
            WriteString(h, 1732, 64, PageSizeName);                // cupsPageSizeName
            return h;
        }

        internal static PwgPageHeader Decode(ReadOnlySpan<byte> h)
        {
            if (h.Length < Size) throw new InvalidDataException("Truncated PWG Raster page header.");
            uint colorSpace = ReadUInt(h, 400);
            uint bitsPerPixel = ReadUInt(h, 388);
            PwgColorSpace space = (colorSpace, bitsPerPixel) switch
            {
                (3, 1) => PwgColorSpace.Black1,
                (18, 8) => PwgColorSpace.SGray8,
                (19, 24) => PwgColorSpace.SRgb8,
                _ => throw new InvalidDataException("Unsupported PWG Raster colour space " + colorSpace + " at " + bitsPerPixel + " bits.")
            };
            var header = new PwgPageHeader
            {
                ColorSpace = space,
                MediaType = ReadString(h, 128, 64),
                Duplex = ReadUInt(h, 272) != 0,
                DpiX = (int)ReadUInt(h, 276),
                DpiY = (int)ReadUInt(h, 280),
                NumCopies = (int)ReadUInt(h, 340),
                PageWidthPoints = (int)ReadUInt(h, 352),
                PageHeightPoints = (int)ReadUInt(h, 356),
                Tumble = ReadUInt(h, 368) != 0,
                Width = (int)ReadUInt(h, 372),
                Height = (int)ReadUInt(h, 376),
                TotalPageCount = (int)ReadUInt(h, 452),
                CrossFeedTransform = BinaryPrimitives.ReadInt32BigEndian(h.Slice(456)),
                FeedTransform = BinaryPrimitives.ReadInt32BigEndian(h.Slice(460)),
                PrintQuality = (int)ReadUInt(h, 484),
                PageSizeName = ReadString(h, 1732, 64)
            };
            if (ReadUInt(h, 392) != header.BytesPerLine)
                throw new InvalidDataException("PWG Raster cupsBytesPerLine does not match width and colour space.");
            return header;
        }

        private static void WriteUInt(byte[] h, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(offset), value);
        private static void WriteInt(byte[] h, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(h.AsSpan(offset), value);
        private static uint ReadUInt(ReadOnlySpan<byte> h, int offset) => BinaryPrimitives.ReadUInt32BigEndian(h.Slice(offset));

        private static void WriteString(byte[] h, int offset, int size, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length >= size) throw new ArgumentException("PWG Raster header string too long: " + value);
            bytes.CopyTo(h, offset);
        }

        private static string ReadString(ReadOnlySpan<byte> h, int offset, int size)
        {
            ReadOnlySpan<byte> s = h.Slice(offset, size);
            int end = s.IndexOf((byte)0);
            return Encoding.ASCII.GetString(end < 0 ? s : s.Slice(0, end));
        }
    }

    /// <summary>
    /// Writes a PWG Raster stream ("image/pwg-raster", PWG 5102.4): the "RaS2" sync word, then per page a
    /// 1796-byte header and the PackBits-style compressed lines. Lines are written one at a time.
    /// </summary>
    public sealed class PwgRasterWriter
    {
        private readonly Stream _output;
        private PwgPageHeader? _page;
        private int _linesWritten;
        private byte[]? _pending;
        private int _pendingRepeat;
        private byte[] _encodeBuffer = Array.Empty<byte>();

        public PwgRasterWriter(Stream output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _output.Write("RaS2"u8);
        }

        public int PagesWritten { get; private set; }

        public void BeginPage(PwgPageHeader header)
        {
            if (header is null) throw new ArgumentNullException(nameof(header));
            if (_page is not null) throw new InvalidOperationException("EndPage was not called.");
            if (header.Width <= 0 || header.Height <= 0) throw new ArgumentException("Page must have positive pixel dimensions.", nameof(header));
            _output.Write(header.Encode());
            _page = header;
            _linesWritten = 0;
            _pending = null;
            int worst = header.BytesPerLine + header.BytesPerLine / header.UnitBytes + 16;
            if (_encodeBuffer.Length < worst) _encodeBuffer = new byte[worst];
        }

        /// <summary>Writes the next line (exactly <see cref="PwgPageHeader.BytesPerLine"/> bytes). Identical consecutive lines are merged.</summary>
        public void WriteLine(ReadOnlySpan<byte> line)
        {
            PwgPageHeader page = _page ?? throw new InvalidOperationException("BeginPage was not called.");
            if (line.Length != page.BytesPerLine) throw new ArgumentException("Line must be " + page.BytesPerLine + " bytes.", nameof(line));
            if (_linesWritten >= page.Height) throw new InvalidOperationException("More lines than the page height.");
            _linesWritten++;

            if (_pending is not null && _pendingRepeat < 255 && line.SequenceEqual(_pending))
            {
                _pendingRepeat++;
                return;
            }
            FlushPending();
            _pending = line.ToArray();
            _pendingRepeat = 0;
        }

        public void EndPage()
        {
            PwgPageHeader page = _page ?? throw new InvalidOperationException("BeginPage was not called.");
            if (_linesWritten != page.Height)
                throw new InvalidOperationException("Page has " + _linesWritten + " lines; header says " + page.Height + ".");
            FlushPending();
            _page = null;
            PagesWritten++;
        }

        private void FlushPending()
        {
            if (_pending is null) return;
            _output.WriteByte((byte)_pendingRepeat);
            int length = EncodeLine(_pending, _page!.UnitBytes, _page.WhiteByte, _encodeBuffer);
            _output.Write(_encodeBuffer, 0, length);
            _pending = null;
        }

        /// <summary>
        /// PackBits-style line encoding: 0..127 = repeat the next unit n+1 times; 129..255 = 257-n literal
        /// units follow; 128 = fill the rest of the line with white.
        /// </summary>
        internal static int EncodeLine(ReadOnlySpan<byte> line, int unit, byte white, Span<byte> output)
        {
            int units = line.Length / unit;
            int o = 0, i = 0;

            // Trailing white run: one 128 code instead of repeats.
            int lastNonWhite = units - 1;
            while (lastNonWhite >= 0 && IsWhite(line.Slice(lastNonWhite * unit, unit), white)) lastNonWhite--;
            int end = lastNonWhite + 1;

            while (i < end)
            {
                int run = 1;
                while (i + run < end && run < 128 && line.Slice((i + run) * unit, unit).SequenceEqual(line.Slice(i * unit, unit))) run++;
                if (run > 1)
                {
                    output[o++] = (byte)(run - 1);
                    line.Slice(i * unit, unit).CopyTo(output.Slice(o));
                    o += unit;
                    i += run;
                    continue;
                }

                int start = i, count = 0;
                while (i < end && count < 128)
                {
                    if (i + 1 < end && line.Slice((i + 1) * unit, unit).SequenceEqual(line.Slice(i * unit, unit))) break;
                    i++;
                    count++;
                }
                // Literal codes cover 2..128 units; a single unit is written as a 1-unit repeat.
                output[o++] = count == 1 ? (byte)0 : (byte)(257 - count);
                line.Slice(start * unit, count * unit).CopyTo(output.Slice(o));
                o += count * unit;
            }

            if (end < units) output[o++] = 128;
            return o;
        }

        private static bool IsWhite(ReadOnlySpan<byte> unit, byte white)
        {
            foreach (byte b in unit) if (b != white) return false;
            return true;
        }
    }

    /// <summary>Reads a PWG Raster stream page by page (for verification and for IPP servers).</summary>
    public sealed class PwgRasterReader
    {
        private readonly Stream _input;

        public PwgRasterReader(Stream input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            Span<byte> sync = stackalloc byte[4];
            if (ReadFully(sync) != 4 || !sync.SequenceEqual("RaS2"u8))
                throw new InvalidDataException("Not a PWG Raster stream (missing RaS2 sync word).");
        }

        /// <summary>Reads the next page, or returns null at the end of the stream.</summary>
        public (PwgPageHeader Header, byte[] Pixels)? ReadPage()
        {
            var headerBytes = new byte[PwgPageHeader.Size];
            int got = ReadFully(headerBytes);
            if (got == 0) return null;
            if (got != headerBytes.Length) throw new InvalidDataException("Truncated PWG Raster page header.");
            PwgPageHeader header = PwgPageHeader.Decode(headerBytes);

            int bpl = header.BytesPerLine, unit = header.UnitBytes;
            var pixels = new byte[(long)bpl * header.Height];
            var line = new byte[bpl];
            int y = 0;
            while (y < header.Height)
            {
                int repeat = ReadByte() + 1;
                int x = 0;
                while (x < bpl)
                {
                    int code = ReadByte();
                    if (code == 128)
                    {
                        line.AsSpan(x).Fill(header.WhiteByte);
                        x = bpl;
                    }
                    else if (code < 128)
                    {
                        int count = (code + 1) * unit;
                        if (x + count > bpl) throw new InvalidDataException("PWG Raster run overflows the line.");
                        var value = new byte[unit];
                        if (ReadFully(value) != unit) throw new InvalidDataException("Truncated PWG Raster data.");
                        for (int k = 0; k < count; k += unit) value.CopyTo(line, x + k);
                        x += count;
                    }
                    else
                    {
                        int count = (257 - code) * unit;
                        if (x + count > bpl) throw new InvalidDataException("PWG Raster literal overflows the line.");
                        if (ReadFully(line.AsSpan(x, count)) != count) throw new InvalidDataException("Truncated PWG Raster data.");
                        x += count;
                    }
                }
                for (int r = 0; r < repeat && y < header.Height; r++, y++)
                    line.CopyTo(pixels, (long)y * bpl);
            }
            return (header, pixels);
        }

        /// <summary>Reads every remaining page.</summary>
        public IReadOnlyList<(PwgPageHeader Header, byte[] Pixels)> ReadAll()
        {
            var pages = new List<(PwgPageHeader, byte[])>();
            while (ReadPage() is { } page) pages.Add(page);
            return pages;
        }

        private int ReadByte()
        {
            int b = _input.ReadByte();
            if (b < 0) throw new InvalidDataException("Truncated PWG Raster data.");
            return b;
        }

        private int ReadFully(Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = _input.Read(buffer.Slice(total));
                if (n == 0) break;
                total += n;
            }
            return total;
        }
    }
}
