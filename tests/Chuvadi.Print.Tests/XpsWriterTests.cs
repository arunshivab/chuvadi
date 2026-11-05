using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Tests
{
    public class XpsWriterTests
    {
        private static readonly XNamespace X = XpsWriter.Namespace;
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

        // ---------------------------------------------------------------- helpers

        /// <summary>A minimal sfnt with the tables the writer reads (glyf, loca, maxp, optional OS/2).</summary>
        internal static byte[] MakeFont(int glyphCount = 10, ushort? fsType = 0, uint version = 0x00010000, bool outlines = true)
        {
            var tables = new List<(string Tag, byte[] Data)>();
            if (outlines)
            {
                tables.Add(("glyf", new byte[4]));
                tables.Add(("loca", new byte[4]));
            }
            var maxp = new byte[6];
            BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphCount);
            tables.Add(("maxp", maxp));
            if (fsType.HasValue)
            {
                var os2 = new byte[78];
                BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(8), fsType.Value);
                tables.Add(("OS/2", os2));
            }
            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
            int offset = 12 + tables.Count * 16;
            var output = new MemoryStream();
            var header = new byte[12 + tables.Count * 16];
            BinaryPrimitives.WriteUInt32BigEndian(header, version);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)tables.Count);
            for (int i = 0; i < tables.Count; i++)
            {
                int rec = 12 + i * 16;
                for (int k = 0; k < 4; k++) header[rec + k] = (byte)tables[i].Tag[k];
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 8), (uint)offset);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 12), (uint)tables[i].Data.Length);
                offset += (tables[i].Data.Length + 3) & ~3;
            }
            output.Write(header);
            foreach ((_, byte[] data) in tables)
            {
                output.Write(data);
                output.Write(new byte[((data.Length + 3) & ~3) - data.Length]);
            }
            return output.ToArray();
        }

        /// <summary>A JPEG header with a JFIF segment and a frame header; enough for the parser.</summary>
        internal static byte[] MakeJpeg(int width, int height, int components = 3, int dpi = 300, bool jfif = true)
        {
            var b = new List<byte> { 0xFF, 0xD8 };
            if (jfif)
            {
                b.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 1 });
                b.Add((byte)(dpi >> 8)); b.Add((byte)dpi); b.Add((byte)(dpi >> 8)); b.Add((byte)dpi);
                b.Add(0); b.Add(0);
            }
            int len = 8 + components * 3;
            b.AddRange(new byte[] { 0xFF, 0xC0, (byte)(len >> 8), (byte)len, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components });
            for (int i = 0; i < components; i++) b.AddRange(new byte[] { (byte)(i + 1), 0x11, 0 });
            b.AddRange(new byte[] { 0xFF, 0xD9 });
            return b.ToArray();
        }

        private sealed class Package
        {
            public Package(byte[] bytes)
            {
                Bytes = bytes;
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    using var s = e.Open();
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    Parts["/" + e.FullName] = ms.ToArray();
                    Times.Add(e.LastWriteTime);
                }
            }

            public byte[] Bytes { get; }
            public Dictionary<string, byte[]> Parts { get; } = new(StringComparer.Ordinal);
            public List<DateTimeOffset> Times { get; } = new();

            public XDocument Xml(string part) => XDocument.Load(new MemoryStream(Parts[part]));

            public XElement Page(int n) => Xml(XpsWriter.PagePart(n)).Root!;

            /// <summary>The page content under the root canvas that converts points to XPS units.</summary>
            public List<XElement> Content(int n) => Page(n).Element(X + "Canvas")!.Elements().ToList();
        }

        private static Package Write(params VectorPage[] pages)
        {
            var ms = new MemoryStream();
            XpsWriter.Write(pages, ms);
            return new Package(ms.ToArray());
        }

        private static VectorPage A4() => new(595.28, 841.89);

        private static VectorSolidBrush Solid(byte r, byte g, byte b, byte a = 255) => new(new VectorColor(r, g, b, a));

        // ---------------------------------------------------------------- package

        [Fact]
        public void PackageHasTheEcma388Parts()
        {
            Package p = Write(A4(), new VectorPage(200, 100));

            XElement types = p.Xml("/[Content_Types].xml").Root!;
            var defaults = types.Elements(Ct + "Default").ToDictionary(e => (string)e.Attribute("Extension")!, e => (string)e.Attribute("ContentType")!);
            Assert.Equal("application/vnd.ms-package.xps-fixeddocumentsequence+xml", defaults["fdseq"]);
            Assert.Equal("application/vnd.ms-package.xps-fixeddocument+xml", defaults["fdoc"]);
            Assert.Equal("application/vnd.ms-package.xps-fixedpage+xml", defaults["fpage"]);
            Assert.Equal("application/vnd.ms-package.obfuscated-opentype", defaults["odttf"]);
            Assert.Equal("application/vnd.openxmlformats-package.relationships+xml", defaults["rels"]);

            XElement root = p.Xml("/_rels/.rels").Root!.Element(Rel + "Relationship")!;
            Assert.Equal("http://schemas.microsoft.com/xps/2005/06/fixedrepresentation", (string)root.Attribute("Type")!);
            Assert.Equal("/FixedDocSeq.fdseq", (string)root.Attribute("Target")!);

            XElement seq = p.Xml("/FixedDocSeq.fdseq").Root!;
            Assert.Equal(X + "FixedDocumentSequence", seq.Name);
            Assert.Equal("/Documents/1/FixedDoc.fdoc", (string)seq.Element(X + "DocumentReference")!.Attribute("Source")!);

            var contents = p.Xml("/Documents/1/FixedDoc.fdoc").Root!.Elements(X + "PageContent").ToList();
            Assert.Equal(2, contents.Count);
            Assert.Equal("/Documents/1/Pages/1.fpage", (string)contents[0].Attribute("Source")!);
            Assert.Equal("/Documents/1/Pages/2.fpage", (string)contents[1].Attribute("Source")!);
            Assert.Equal("266.666667", (string)contents[1].Attribute("Width")!);
            Assert.Equal("133.333333", (string)contents[1].Attribute("Height")!);

            XElement page = p.Page(2);
            Assert.Equal(X + "FixedPage", page.Name);
            Assert.Equal("266.666667", (string)page.Attribute("Width")!);
            Assert.Equal("und", (string)page.Attribute(XNamespace.Xml + "lang")!);
            Assert.Equal("1.333333333333,0,0,1.333333333333,0,0", (string)page.Element(X + "Canvas")!.Attribute("RenderTransform")!);
            Assert.False(p.Parts.ContainsKey(XpsWriter.PageRelsPart(2)));
        }

        [Fact]
        public void OutputIsTheSameBytesEachTime()
        {
            VectorPage Make()
            {
                var page = A4();
                var font = new VectorFont(MakeFont());
                var run = new VectorGlyphRun(font, 12, Solid(0, 0, 0));
                run.Glyphs.Add(new VectorGlyph(3, 10, 20, 6, "A"));
                page.Elements.Add(run);
                page.Elements.Add(new VectorImageElement(VectorImage.FromPixels(2, 1, VectorPixelFormat.Gray8, new byte[] { 0, 255 }), 0, 0, 10, 5));
                return page;
            }
            byte[] a = Write(Make()).Bytes;
            Thread.Sleep(2100); // zip times have 2-second steps
            byte[] b = Write(Make()).Bytes;
            Assert.Equal(a, b);
            List<DateTimeOffset> times = new Package(a).Times;
            Assert.All(times, t => Assert.Equal(times[0], t));
        }

        [Fact]
        public void NoPagesIsRefused()
        {
            Assert.Throws<ArgumentException>(() => XpsWriter.Write(Array.Empty<VectorPage>(), new MemoryStream()));
            Assert.False(XpsWriter.CanWrite(Array.Empty<VectorPage>(), out string? reason));
            Assert.Equal("There are no pages.", reason);
        }

        [Fact]
        public void WriteToFileCreatesAReadablePackage()
        {
            string path = Path.Combine(Path.GetTempPath(), "chuvadi-xps-" + Guid.NewGuid().ToString("N") + ".xps");
            try
            {
                XpsWriter.Write(new[] { A4() }, path);
                var p = new Package(File.ReadAllBytes(path));
                Assert.True(p.Parts.ContainsKey("/Documents/1/Pages/1.fpage"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------------------------------------------------------------- paths

        [Fact]
        public void PathIsWrittenWithFillStrokeAndData()
        {
            var page = A4();
            var geometry = new VectorGeometry { FillRule = VectorFillRule.EvenOdd }
                .MoveTo(10, 20).LineTo(30, 20).CubicTo(35, 25, 35, 35, 30, 40).Close();
            var stroke = new VectorStroke(Solid(255, 0, 0), 2) { LineCap = VectorLineCap.Round, LineJoin = VectorLineJoin.Bevel };
            stroke.DashArray.Add(3);
            stroke.DashOffset = 1;
            page.Elements.Add(new VectorPath(geometry)
            {
                Fill = Solid(0, 128, 255, 200),
                Stroke = stroke,
                Opacity = 0.5,
                Transform = VectorMatrix.Translation(5, -2.5)
            });

            XElement path = Assert.Single(Write(page).Content(1));
            Assert.Equal(X + "Path", path.Name);
            Assert.Equal("F0 M 10,20 L 30,20 C 35,25 35,35 30,40 Z", (string)path.Attribute("Data")!);
            Assert.Equal("#C80080FF", (string)path.Attribute("Fill")!);
            Assert.Equal("#FFFF0000", (string)path.Attribute("Stroke")!);
            Assert.Equal("2", (string)path.Attribute("StrokeThickness")!);
            Assert.Equal("Round", (string)path.Attribute("StrokeStartLineCap")!);
            Assert.Equal("Round", (string)path.Attribute("StrokeEndLineCap")!);
            Assert.Equal("Round", (string)path.Attribute("StrokeDashCap")!);
            Assert.Equal("Bevel", (string)path.Attribute("StrokeLineJoin")!);
            Assert.Null(path.Attribute("StrokeMiterLimit"));
            Assert.Equal("1.5 1.5", (string)path.Attribute("StrokeDashArray")!); // odd array repeats; in stroke widths
            Assert.Equal("0.5", (string)path.Attribute("StrokeDashOffset")!);
            Assert.Equal("0.5", (string)path.Attribute("Opacity")!);
            Assert.Equal("1,0,0,1,5,-2.5", (string)path.Attribute("RenderTransform")!);
        }

        [Fact]
        public void MiterJoinWritesTheLimitAndButtCapsAreFlat()
        {
            var page = A4();
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 10, 10)) { Stroke = new VectorStroke(Solid(0, 0, 0), 0.5) { MiterLimit = 4 } });
            XElement path = Assert.Single(Write(page).Content(1));
            Assert.Equal("Flat", (string)path.Attribute("StrokeStartLineCap")!);
            Assert.Equal("Miter", (string)path.Attribute("StrokeLineJoin")!);
            Assert.Equal("4", (string)path.Attribute("StrokeMiterLimit")!);
            Assert.Null(path.Attribute("Fill"));
            Assert.Null(path.Attribute("StrokeDashArray"));
            Assert.Equal("F1 M 0,0 L 10,0 L 10,10 L 0,10 Z", (string)path.Attribute("Data")!);
        }

        [Fact]
        public void EmptyAndUnpaintedPathsAreSkipped()
        {
            var page = A4();
            page.Elements.Add(new VectorPath(new VectorGeometry()) { Fill = Solid(0, 0, 0) });
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 1, 1)));
            page.Elements.Add(new VectorGroup());
            Assert.Empty(Write(page).Content(1));
        }

        [Fact]
        public void GeometryNeedsMoveToFirst()
        {
            Assert.Throws<InvalidOperationException>(() => new VectorGeometry().LineTo(1, 1));
            Assert.Throws<InvalidOperationException>(() => new VectorGeometry().Close());
        }

        [Fact]
        public void BrushWithOpacityIsAPropertyElement()
        {
            var page = A4();
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 1, 1))
            {
                Fill = new VectorSolidBrush(new VectorColor(1, 2, 3)) { Opacity = 0.25 }
            });
            XElement path = Assert.Single(Write(page).Content(1));
            Assert.Null(path.Attribute("Fill"));
            XElement brush = path.Element(X + "Path.Fill")!.Element(X + "SolidColorBrush")!;
            Assert.Equal("#FF010203", (string)brush.Attribute("Color")!);
            Assert.Equal("0.25", (string)brush.Attribute("Opacity")!);
        }

        // ---------------------------------------------------------------- gradients

        [Fact]
        public void LinearGradientIsAbsoluteWithSrgbStops()
        {
            var page = A4();
            var brush = new VectorLinearGradientBrush(new VectorPoint(0, 0), new VectorPoint(100, 0)) { Transform = VectorMatrix.Scaling(2, 1) };
            brush.Stops.Add(new VectorGradientStop(0, VectorColor.Black));
            brush.Stops.Add(new VectorGradientStop(0.4, new VectorColor(255, 0, 0)));
            brush.Stops.Add(new VectorGradientStop(1, VectorColor.White));
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 100, 50)) { Fill = brush });

            XElement g = Assert.Single(Write(page).Content(1)).Element(X + "Path.Fill")!.Element(X + "LinearGradientBrush")!;
            Assert.Equal("Absolute", (string)g.Attribute("MappingMode")!);
            Assert.Equal("Pad", (string)g.Attribute("SpreadMethod")!);
            Assert.Equal("SRgbLinearInterpolation", (string)g.Attribute("ColorInterpolationMode")!);
            Assert.Equal("0,0", (string)g.Attribute("StartPoint")!);
            Assert.Equal("100,0", (string)g.Attribute("EndPoint")!);
            Assert.Equal("2,0,0,1,0,0", (string)g.Attribute("Transform")!);
            var stops = g.Element(X + "LinearGradientBrush.GradientStops")!.Elements(X + "GradientStop").ToList();
            Assert.Equal(new[] { "0", "0.4", "1" }, stops.Select(s => (string)s.Attribute("Offset")!));
            Assert.Equal("#FFFF0000", (string)stops[1].Attribute("Color")!);
        }

        [Fact]
        public void RadialGradientFromAFocalPoint()
        {
            var page = A4();
            var brush = new VectorRadialGradientBrush(new VectorPoint(45, 50), 0, new VectorPoint(50, 50), 20);
            brush.Stops.Add(new VectorGradientStop(0.5, new VectorColor(0, 255, 0)));
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 100, 100)) { Fill = brush });

            XElement g = Assert.Single(Write(page).Content(1)).Element(X + "Path.Fill")!.Element(X + "RadialGradientBrush")!;
            Assert.Equal("50,50", (string)g.Attribute("Center")!);
            Assert.Equal("45,50", (string)g.Attribute("GradientOrigin")!);
            Assert.Equal("20", (string)g.Attribute("RadiusX")!);
            Assert.Equal("20", (string)g.Attribute("RadiusY")!);
            // One stop becomes a flat colour from 0 to 1.
            var stops = g.Element(X + "RadialGradientBrush.GradientStops")!.Elements(X + "GradientStop").ToList();
            Assert.Equal(new[] { "0", "1" }, stops.Select(s => (string)s.Attribute("Offset")!));
            Assert.All(stops, s => Assert.Equal("#FF00FF00", (string)s.Attribute("Color")!));
        }

        // ---------------------------------------------------------------- glyphs and fonts

        [Fact]
        public void GlyphRunEmbedsTheFontObfuscatedAndWritesIndices()
        {
            byte[] fontData = MakeFont();
            var font = new VectorFont(fontData, "TestSans");
            var page = A4();
            var run = new VectorGlyphRun(font, 10, Solid(0, 0, 0));
            run.Glyphs.Add(new VectorGlyph(1, 100, 200, 6, "H"));
            run.Glyphs.Add(new VectorGlyph(2, 106, 200, 5, "i"));
            run.Glyphs.Add(new VectorGlyph(3, 112.5, 200, 4.25, "!"));
            page.Elements.Add(run);
            Package p = Write(page);

            XElement glyphs = Assert.Single(p.Content(1));
            Assert.Equal(X + "Glyphs", glyphs.Name);
            Assert.Equal("10", (string)glyphs.Attribute("FontRenderingEmSize")!);
            Assert.Equal("100", (string)glyphs.Attribute("OriginX")!);
            Assert.Equal("200", (string)glyphs.Attribute("OriginY")!);
            Assert.Equal("1,60;2,65;3,42.5", (string)glyphs.Attribute("Indices")!);
            Assert.Equal("Hi!", (string)glyphs.Attribute("UnicodeString")!);
            Assert.Equal("#FF000000", (string)glyphs.Attribute("Fill")!);

            string fontUri = (string)glyphs.Attribute("FontUri")!;
            Assert.Matches("^/Resources/Fonts/[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\.odttf$", fontUri);
            Guid key = Guid.Parse(fontUri.Substring("/Resources/Fonts/".Length, 36));
            byte[] stored = p.Parts[fontUri];
            Assert.NotEqual(fontData, stored);
            Assert.Equal(fontData.AsSpan(32).ToArray(), stored.AsSpan(32).ToArray());
            Assert.Equal(fontData, XpsWriter.ObfuscateFont(stored, key));

            XElement rel = Assert.Single(p.Xml(XpsWriter.PageRelsPart(1)).Root!.Elements(Rel + "Relationship"));
            Assert.Equal("http://schemas.microsoft.com/xps/2005/06/required-resource", (string)rel.Attribute("Type")!);
            Assert.Equal(fontUri, (string)rel.Attribute("Target")!);
        }

        [Fact]
        public void ObfuscationMatchesTheEcma388Algorithm()
        {
            // ECMA-388 9.1.7.3: key bytes are the GUID's hex digit pairs read in order, applied in reverse.
            var key = new Guid("0102030405060708090A0B0C0D0E0F10");
            byte[] data = new byte[40];
            byte[] o = XpsWriter.ObfuscateFont(data, key);
            Assert.Equal(0x10, o[0]);
            Assert.Equal(0x0F, o[1]);
            Assert.Equal(0x01, o[15]);
            Assert.Equal(0x10, o[16]);
            Assert.Equal(0x01, o[31]);
            Assert.Equal(0x00, o[32]);
        }

        [Fact]
        public void SameFontIsStoredOnceAcrossRunsInstancesAndPages()
        {
            byte[] data = MakeFont();
            var a = new VectorFont(data);
            var b = new VectorFont((byte[])data.Clone());
            VectorPage Page(VectorFont f1, VectorFont f2)
            {
                var page = A4();
                foreach (VectorFont f in new[] { f1, f2 })
                {
                    var run = new VectorGlyphRun(f, 12, Solid(0, 0, 0));
                    run.Glyphs.Add(new VectorGlyph(1, 0, 10, 5));
                    page.Elements.Add(run);
                }
                return page;
            }
            Package p = Write(Page(a, b), Page(b, a));
            Assert.Single(p.Parts.Keys, k => k.StartsWith("/Resources/Fonts/", StringComparison.Ordinal));
            Assert.Single(p.Xml(XpsWriter.PageRelsPart(2)).Root!.Elements(Rel + "Relationship"));
        }

        [Fact]
        public void GlyphsOnSeveralBaselinesAreSplitInsideOneCanvas()
        {
            var page = A4();
            var run = new VectorGlyphRun(new VectorFont(MakeFont()), 12, Solid(0, 0, 0))
            {
                Opacity = 0.5,
                Transform = new VectorMatrix(0, 1, -1, 0, 300, 0),
                Clip = VectorGeometry.FromRectangle(0, 0, 50, 50)
            };
            run.Glyphs.Add(new VectorGlyph(1, 0, 10, 6, "a"));
            run.Glyphs.Add(new VectorGlyph(2, 6, 10, 6, "b"));
            run.Glyphs.Add(new VectorGlyph(3, 0, 24, 6, "c"));
            page.Elements.Add(run);

            XElement canvas = Assert.Single(Write(page).Content(1));
            Assert.Equal(X + "Canvas", canvas.Name);
            Assert.Equal("0.5", (string)canvas.Attribute("Opacity")!);
            Assert.Equal("0,1,-1,0,300,0", (string)canvas.Attribute("RenderTransform")!);
            Assert.NotNull(canvas.Attribute("Clip"));
            var lines = canvas.Elements(X + "Glyphs").ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal("ab", (string)lines[0].Attribute("UnicodeString")!);
            Assert.Equal("1,50;2,50", (string)lines[0].Attribute("Indices")!);
            Assert.Equal("24", (string)lines[1].Attribute("OriginY")!);
            Assert.All(lines, l => Assert.Null(l.Attribute("Opacity")));
            Assert.All(lines, l => Assert.Null(l.Attribute("RenderTransform")));
        }

        [Fact]
        public void LigaturesGetClusterMapsAndUnknownTextIsLeftOut()
        {
            var font = new VectorFont(MakeFont());
            var page = A4();
            var lig = new VectorGlyphRun(font, 10, Solid(0, 0, 0));
            lig.Glyphs.Add(new VectorGlyph(5, 0, 10, 5, "fi"));
            lig.Glyphs.Add(new VectorGlyph(6, 5, 10, 5, "x"));
            page.Elements.Add(lig);
            var unknown = new VectorGlyphRun(font, 10, Solid(0, 0, 0));
            unknown.Glyphs.Add(new VectorGlyph(5, 0, 30, 5, "a"));
            unknown.Glyphs.Add(new VectorGlyph(6, 5, 30, 5, null));
            page.Elements.Add(unknown);
            var brace = new VectorGlyphRun(font, 10, Solid(0, 0, 0));
            brace.Glyphs.Add(new VectorGlyph(7, 0, 50, 5, "{"));
            page.Elements.Add(brace);
            var control = new VectorGlyphRun(font, 10, Solid(0, 0, 0));
            control.Glyphs.Add(new VectorGlyph(7, 0, 70, 5, "\u0001"));
            page.Elements.Add(control);

            var e = Write(page).Content(1);
            Assert.Equal("(2:1)5,50;6,50", (string)e[0].Attribute("Indices")!);
            Assert.Equal("fix", (string)e[0].Attribute("UnicodeString")!);
            Assert.Equal("5,50;6,50", (string)e[1].Attribute("Indices")!);
            Assert.Null(e[1].Attribute("UnicodeString"));
            Assert.Equal("{}{", (string)e[2].Attribute("UnicodeString")!);
            Assert.Null(e[3].Attribute("UnicodeString"));
        }

        [Fact]
        public void GlyphFillWithAGradientIsAPropertyElement()
        {
            var page = A4();
            var brush = new VectorLinearGradientBrush(new VectorPoint(0, 0), new VectorPoint(10, 0));
            brush.Stops.Add(new VectorGradientStop(0, VectorColor.Black));
            brush.Stops.Add(new VectorGradientStop(1, VectorColor.White));
            var run = new VectorGlyphRun(new VectorFont(MakeFont()), 10, brush);
            run.Glyphs.Add(new VectorGlyph(1, 0, 10, 5));
            page.Elements.Add(run);
            XElement glyphs = Assert.Single(Write(page).Content(1));
            Assert.Null(glyphs.Attribute("Fill"));
            Assert.NotNull(glyphs.Element(X + "Glyphs.Fill")!.Element(X + "LinearGradientBrush"));
        }

        // ---------------------------------------------------------------- images and groups

        [Fact]
        public void PngImageFillsItsPlacedUnitSquare()
        {
            VectorImage img = VectorImage.FromPixels(3, 2, VectorPixelFormat.Rgb24, new byte[18], dpi: 192);
            Assert.Equal(3, img.PixelWidth);
            Assert.Equal(2, img.PixelHeight);
            Assert.Equal(192, img.DpiX, 1);
            var page = A4();
            page.Elements.Add(new VectorImageElement(img, 10, 20, 30, 40));
            page.Elements.Add(new VectorImageElement(img, 50, 20, 30, 40));
            Package p = Write(page);

            var paths = p.Content(1);
            Assert.Equal(2, paths.Count);
            Assert.Equal("30,0,0,40,10,20", (string)paths[0].Attribute("RenderTransform")!);
            Assert.Equal("M 0,0 L 1,0 1,1 0,1 Z", (string)paths[0].Attribute("Data")!);
            XElement brush = paths[0].Element(X + "Path.Fill")!.Element(X + "ImageBrush")!;
            Assert.Equal("/Resources/Images/1.png", (string)brush.Attribute("ImageSource")!);
            // The image's own resolution sets its size in 1/96 inch; PNG stores whole pixels per metre (192 dpi = 7559/m).
            Assert.Equal("0,0,1.500011,1.000007", (string)brush.Attribute("Viewbox")!);
            Assert.Equal("Absolute", (string)brush.Attribute("ViewboxUnits")!);
            Assert.Equal("0,0,1,1", (string)brush.Attribute("Viewport")!);
            Assert.Equal("None", (string)brush.Attribute("TileMode")!);
            Assert.Equal(img.Data, p.Parts["/Resources/Images/1.png"]);
            Assert.Single(p.Parts.Keys, k => k.StartsWith("/Resources/Images/", StringComparison.Ordinal));
            Assert.Single(p.Xml(XpsWriter.PageRelsPart(1)).Root!.Elements(Rel + "Relationship"));
        }

        [Fact]
        public void JpegSizeAndResolutionAreReadFromTheFile()
        {
            VectorImage img = VectorImage.FromJpeg(MakeJpeg(640, 480, dpi: 300));
            Assert.Equal(VectorImageFormat.Jpeg, img.Format);
            Assert.Equal(640, img.PixelWidth);
            Assert.Equal(480, img.PixelHeight);
            Assert.Equal(300, img.DpiX);
            Assert.Equal(3, img.Components);
            Assert.Equal(96, VectorImage.FromJpeg(MakeJpeg(8, 8, jfif: false)).DpiX);

            var page = A4();
            page.Elements.Add(new VectorImageElement(img, 0, 0, 100, 75));
            Package p = Write(page);
            XElement brush = Assert.Single(p.Content(1)).Element(X + "Path.Fill")!.Element(X + "ImageBrush")!;
            Assert.Equal("/Resources/Images/1.jpg", (string)brush.Attribute("ImageSource")!);
            Assert.Equal("0,0,204.8,153.6", (string)brush.Attribute("Viewbox")!);
            Assert.Equal("image/jpeg", p.Xml("/[Content_Types].xml").Root!.Elements(Ct + "Default").Single(e => (string)e.Attribute("Extension")! == "jpg").Attribute("ContentType")!.Value);
        }

        [Fact]
        public void ImagesThatAreNotPngOrJpegAreRejected()
        {
            Assert.Throws<InvalidDataException>(() => VectorImage.FromPng(new byte[40]));
            Assert.Throws<InvalidDataException>(() => VectorImage.FromJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
            Assert.Throws<ArgumentException>(() => VectorImage.FromPixels(2, 2, VectorPixelFormat.Gray8, new byte[3]));
        }

        [Fact]
        public void GroupsAreCanvasesWithOpacityClipAndTransform()
        {
            var page = A4();
            var group = new VectorGroup { Opacity = 0.75, Transform = VectorMatrix.Scaling(2, 2), Clip = VectorGeometry.FromRectangle(0, 0, 5, 5) };
            var inner = new VectorGroup();
            inner.Children.Add(new VectorPath(VectorGeometry.FromRectangle(0, 0, 1, 1)) { Fill = Solid(1, 1, 1), Opacity = 0.5 });
            group.Children.Add(inner);
            page.Elements.Add(group);

            XElement canvas = Assert.Single(Write(page).Content(1));
            Assert.Equal(X + "Canvas", canvas.Name);
            Assert.Equal("0.75", (string)canvas.Attribute("Opacity")!);
            Assert.Equal("2,0,0,2,0,0", (string)canvas.Attribute("RenderTransform")!);
            Assert.Equal("F1 M 0,0 L 5,0 L 5,5 L 0,5 Z", (string)canvas.Attribute("Clip")!);
            XElement path = canvas.Element(X + "Canvas")!.Element(X + "Path")!;
            Assert.Equal("0.5", (string)path.Attribute("Opacity")!);
        }

        // ---------------------------------------------------------------- refusals

        public static IEnumerable<object[]> Unwritable()
        {
            VectorPath Rect() => new(VectorGeometry.FromRectangle(0, 0, 1, 1)) { Fill = Solid(0, 0, 0) };
            VectorGlyphRun Run(byte[] font, ushort gid = 1)
            {
                var r = new VectorGlyphRun(new VectorFont(font, "F"), 10, Solid(0, 0, 0));
                r.Glyphs.Add(new VectorGlyph(gid, 0, 0, 1));
                return r;
            }
            VectorLinearGradientBrush Linear(bool extend = true)
            {
                var b = new VectorLinearGradientBrush(new VectorPoint(0, 0), new VectorPoint(1, 0)) { ExtendEnd = extend };
                b.Stops.Add(new VectorGradientStop(0, VectorColor.Black));
                return b;
            }

            yield return new object[] { "blend", (Func<VectorElement>)(() => { var p = Rect(); p.BlendMode = VectorBlendMode.Multiply; return p; }), "blend mode Multiply" };
            yield return new object[] { "knockout", (Func<VectorElement>)(() => new VectorGroup { Knockout = true }), "knockout" };
            yield return new object[] { "softmask", (Func<VectorElement>)(() => new VectorGroup { SoftMask = new VectorSoftMask(VectorSoftMaskKind.Alpha, new VectorGroup()) }), "soft mask" };
            yield return new object[] { "cff", (Func<VectorElement>)(() => Run(MakeFont(version: 0x4F54544F))), "CFF" };
            yield return new object[] { "collection", (Func<VectorElement>)(() => Run(MakeFont(version: 0x74746366))), "collection" };
            yield return new object[] { "restricted", (Func<VectorElement>)(() => Run(MakeFont(fsType: 0x0002))), "Restricted License" };
            yield return new object[] { "bitmap", (Func<VectorElement>)(() => Run(MakeFont(fsType: 0x0200))), "bitmap embedding only" };
            yield return new object[] { "nooutlines", (Func<VectorElement>)(() => Run(MakeFont(outlines: false))), "no TrueType outlines" };
            yield return new object[] { "glyphid", (Func<VectorElement>)(() => Run(MakeFont(glyphCount: 5), gid: 5)), "glyph 5 is not in font F" };
            yield return new object[] { "extend", (Func<VectorElement>)(() => { var p = Rect(); p.Fill = Linear(extend: false); return p; }), "does not extend" };
            yield return new object[] { "radial", (Func<VectorElement>)(() =>
            {
                var b = new VectorRadialGradientBrush(new VectorPoint(0, 0), 2, new VectorPoint(0, 0), 10);
                b.Stops.Add(new VectorGradientStop(0, VectorColor.Black));
                var p = Rect(); p.Fill = b; return p;
            }), "start circle is not a point" };
            yield return new object[] { "outside", (Func<VectorElement>)(() =>
            {
                var b = new VectorRadialGradientBrush(new VectorPoint(20, 0), 0, new VectorPoint(0, 0), 10);
                b.Stops.Add(new VectorGradientStop(0, VectorColor.Black));
                var p = Rect(); p.Fill = b; return p;
            }), "outside the end circle" };
            yield return new object[] { "nostops", (Func<VectorElement>)(() => { var p = Rect(); p.Fill = new VectorLinearGradientBrush(new VectorPoint(0, 0), new VectorPoint(1, 0)); return p; }), "no stops" };
            yield return new object[] { "zerostroke", (Func<VectorElement>)(() => new VectorPath(VectorGeometry.FromRectangle(0, 0, 1, 1)) { Stroke = new VectorStroke(Solid(0, 0, 0), 0) }), "zero or negative width" };
            yield return new object[] { "zerodash", (Func<VectorElement>)(() => { var s = new VectorStroke(Solid(0, 0, 0), 1); s.DashArray.Add(0); return new VectorPath(VectorGeometry.FromRectangle(0, 0, 1, 1)) { Stroke = s }; }), "all zero" };
            yield return new object[] { "cmyk", (Func<VectorElement>)(() => new VectorImageElement(VectorImage.FromJpeg(MakeJpeg(4, 4, components: 4)), 0, 0, 1, 1)), "4 colour components" };
            yield return new object[] { "nan", (Func<VectorElement>)(() => { var p = Rect(); p.Transform = new VectorMatrix(double.NaN, 0, 0, 1, 0, 0); return p; }), "not finite" };
            yield return new object[] { "opacity", (Func<VectorElement>)(() => { var p = Rect(); p.Opacity = 1.5; return p; }), "outside 0..1" };
            yield return new object[] { "nested", (Func<VectorElement>)(() => { var g = new VectorGroup(); var inner = new VectorGroup(); var p = Rect(); p.BlendMode = VectorBlendMode.Screen; inner.Children.Add(p); g.Children.Add(inner); return g; }), "blend mode Screen" };
        }

        [Theory]
        [MemberData(nameof(Unwritable))]
        public void ContentXpsCannotExpressIsRefusedWithAReason(string name, Func<VectorElement> make, string expected)
        {
            var page = A4();
            page.Elements.Add(make());
            Assert.False(XpsWriter.CanWrite(page, out string? reason), name);
            Assert.Contains(expected, reason, StringComparison.Ordinal);

            Assert.False(XpsWriter.CanWrite(new[] { A4(), page }, out string? listReason));
            Assert.StartsWith("Page 2: ", listReason, StringComparison.Ordinal);

            var ex = Assert.Throws<NotSupportedException>(() => XpsWriter.Write(new[] { page }, new MemoryStream()));
            Assert.Contains("Page 1 cannot be written as XPS", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void PrintAndEditableFontsCanBeEmbedded()
        {
            foreach (ushort fs in new ushort[] { 0, 0x0004, 0x0008, 0x0100 })
            {
                var page = A4();
                var run = new VectorGlyphRun(new VectorFont(MakeFont(fsType: fs)), 10, Solid(0, 0, 0));
                run.Glyphs.Add(new VectorGlyph(1, 0, 0, 1));
                page.Elements.Add(run);
                Assert.True(XpsWriter.CanWrite(page, out string? reason), reason);
            }
            var noOs2 = A4();
            var r2 = new VectorGlyphRun(new VectorFont(MakeFont(fsType: null)), 10, Solid(0, 0, 0));
            r2.Glyphs.Add(new VectorGlyph(1, 0, 0, 1));
            noOs2.Elements.Add(r2);
            Assert.True(XpsWriter.CanWrite(noOs2, out _));
        }

        // ---------------------------------------------------------------- numbers

        [Fact]
        public void NumbersAreInvariantWithoutExponentsOrNegativeZero()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("1.5", XpsWriter.Num(1.5));
                Assert.Equal("0", XpsWriter.Num(-0.0));
                Assert.Equal("0", XpsWriter.Num(-1e-9));
                Assert.Equal("0.000001", XpsWriter.Num(1e-6));
                Assert.Equal("12345678901234", XpsWriter.Num(12345678901234));
                Assert.Equal("-2.333333", XpsWriter.Num(-7.0 / 3));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public void MatrixMultiplyAppliesFirstThenSecond()
        {
            VectorMatrix m = VectorMatrix.Multiply(VectorMatrix.Scaling(2, 3), VectorMatrix.Translation(10, 20));
            VectorPoint p = m.Transform(new VectorPoint(1, 1));
            Assert.Equal(12, p.X);
            Assert.Equal(23, p.Y);
            Assert.True(VectorMatrix.Identity.IsIdentity);
        }
    }
}
