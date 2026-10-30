using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Print;

namespace ChuvadiPdfPrinter
{
    /// <summary>
    /// Renders a printer test page without any PDF engine: the "document" is plain UTF-8 text, one line per
    /// row of large block letters, on an A4 page with a border, corner marks and a gray ramp. Used by the
    /// self-test to print through the virtual printer.
    /// </summary>
    public sealed class TestPageRasterizer : IPageRasterizer
    {
        public static readonly PageDimensions PageSize = PageDimensions.FromPaper(PaperSize.A4);

        public IRasterDocument Open(ReadOnlyMemory<byte> document)
        {
            string text = Encoding.UTF8.GetString(document.Span);
            return new Doc(text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        private sealed class Doc : IRasterDocument
        {
            private readonly string[] _lines;

            public Doc(string[] lines) => _lines = lines;

            public int PageCount => 1;

            public PageDimensions GetPageSize(int pageNumber)
            {
                if (pageNumber != 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
                return PageSize;
            }

            public RasterPage Render(int pageNumber, RasterRequest request, CancellationToken cancellationToken)
            {
                if (pageNumber != 1) throw new ArgumentOutOfRangeException(nameof(pageNumber));
                if (request.Rotation != PageRotation.None)
                    throw new NotSupportedException("The test page is portrait only.");
                int w = Math.Max(1, (int)Math.Round(PageSize.WidthPoints / 72.0 * request.DpiX));
                int h = Math.Max(1, (int)Math.Round(PageSize.HeightPoints / 72.0 * request.DpiY));
                RasterPage page = RasterPage.Rent(w, h, RasterPixelFormat.Gray8, request.DpiX, request.DpiY);
                try
                {
                    page.Pixels.Fill(255);
                    double px = request.DpiX / 72.0, py = request.DpiY / 72.0;
                    // Border 3pt, 18pt in from the edge; corner squares 24pt.
                    Rect(page, 18 * px, 18 * py, (PageSize.WidthPoints - 36) * px, 3 * py, 0);
                    Rect(page, 18 * px, (PageSize.HeightPoints - 21) * py, (PageSize.WidthPoints - 36) * px, 3 * py, 0);
                    Rect(page, 18 * px, 18 * py, 3 * px, (PageSize.HeightPoints - 36) * py, 0);
                    Rect(page, (PageSize.WidthPoints - 21) * px, 18 * py, 3 * px, (PageSize.HeightPoints - 36) * py, 0);
                    foreach ((double cx, double cy) in new[] { (30.0, 30.0), (PageSize.WidthPoints - 54, 30.0), (30.0, PageSize.HeightPoints - 54), (PageSize.WidthPoints - 54, PageSize.HeightPoints - 54) })
                        Rect(page, cx * px, cy * py, 24 * px, 24 * py, 0);
                    // Gray ramp near the bottom.
                    double rampLeft = 60, rampWidth = PageSize.WidthPoints - 120, rampTop = PageSize.HeightPoints - 120;
                    for (int i = 0; i < 16; i++)
                        Rect(page, (rampLeft + rampWidth * i / 16) * px, rampTop * py, rampWidth / 16 * px + 1, 30 * py, (byte)(i * 17));
                    // Text rows: first line 18pt cells, the rest 9pt cells.
                    double y = 80;
                    for (int line = 0; line < _lines.Length; line++)
                    {
                        double cell = line == 0 ? 3.6 : 2.0;
                        DrawText(page, _lines[line], 60, y, cell, px, py);
                        y += cell * 7 + (line == 0 ? 24 : 12);
                        if (y > PageSize.HeightPoints - 160) break;
                    }
                    return page;
                }
                catch
                {
                    page.Dispose();
                    throw;
                }
            }

            public void Dispose() { }
        }

        private static void Rect(RasterPage page, double x, double y, double w, double h, byte value)
        {
            int x0 = Math.Max(0, (int)x), y0 = Math.Max(0, (int)y);
            int x1 = Math.Min(page.PixelWidth, (int)Math.Ceiling(x + w)), y1 = Math.Min(page.PixelHeight, (int)Math.Ceiling(y + h));
            for (int row = y0; row < y1; row++) page.GetRow(row).Slice(x0, Math.Max(0, x1 - x0)).Fill(value);
        }

        /// <summary>Draws text in a 5x7 block font; <paramref name="cell"/> is the size of one font pixel in points.</summary>
        private static void DrawText(RasterPage page, string text, double left, double top, double cell, double px, double py)
        {
            double x = left;
            double maxX = PageSize.WidthPoints - 60;
            foreach (char raw in text)
            {
                if (x + cell * 5 > maxX) break;
                string glyph = Glyph(char.ToUpperInvariant(raw));
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                        if (glyph[row * 5 + col] == '1')
                            Rect(page, (x + col * cell) * px, (top + row * cell) * py, cell * px + 0.5, cell * py + 0.5, 0);
                x += cell * 6;
            }
        }

        private static readonly Dictionary<char, string> Font = new()
        {
            [' '] = "00000000000000000000000000000000000",
            ['A'] = "01110100011000111111100011000110001", ['B'] = "11110100011000111110100011000111110",
            ['C'] = "01110100011000010000100001000101110", ['D'] = "11110100011000110001100011000111110",
            ['E'] = "11111100001000011110100001000011111", ['F'] = "11111100001000011110100001000010000",
            ['G'] = "01110100011000010111100011000101111", ['H'] = "10001100011000111111100011000110001",
            ['I'] = "01110001000010000100001000010001110", ['J'] = "00111000100001000010000101001001100",
            ['K'] = "10001100101010011000101001001010001", ['L'] = "10000100001000010000100001000011111",
            ['M'] = "10001110111010110101100011000110001", ['N'] = "10001100011100110101100111000110001",
            ['O'] = "01110100011000110001100011000101110", ['P'] = "11110100011000111110100001000010000",
            ['Q'] = "01110100011000110001101011001001101", ['R'] = "11110100011000111110101001001010001",
            ['S'] = "01111100001000001110000010000111110", ['T'] = "11111001000010000100001000010000100",
            ['U'] = "10001100011000110001100011000101110", ['V'] = "10001100011000110001100010101000100",
            ['W'] = "10001100011000110101101011010101010", ['X'] = "10001100010101000100010101000110001",
            ['Y'] = "10001100010101000100001000010000100", ['Z'] = "11111000010001000100010001000011111",
            ['0'] = "01110100011001110101110011000101110", ['1'] = "00100011000010000100001000010001110",
            ['2'] = "01110100010000100010001000100011111", ['3'] = "11111000100010000010000011000101110",
            ['4'] = "00010001100101010010111110001000010", ['5'] = "11111100001111000001000011000101110",
            ['6'] = "00110010001000011110100011000101110", ['7'] = "11111000010001000100010000100001000",
            ['8'] = "01110100011000101110100011000101110", ['9'] = "01110100011000101111000010001001100",
            ['-'] = "00000000000000011111000000000000000", [':'] = "00000011000110000000011000110000000",
            ['.'] = "00000000000000000000000000110001100", ['/'] = "00001000100001000100010000100010000",
            ['('] = "00010001000100001000010000010000010", [')'] = "01000001000001000010000100010001000",
            ['_'] = "00000000000000000000000000000011111", ['?'] = "01110100010000100010001000000000100"
        };

        private static string Glyph(char c) => Font.TryGetValue(c, out string? g) ? g : Font['?'];
    }
}
