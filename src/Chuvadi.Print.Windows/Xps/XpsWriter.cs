using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Writes <see cref="VectorPage"/>s as an XPS document (ECMA-388, the XML Paper Specification): one fixed
    /// document, one fixed page per page, TrueType fonts embedded obfuscated (ECMA-388 9.1.7.3) and images as PNG or
    /// JPEG parts. Pure managed code; runs on any OS. The package is built with
    /// <see cref="System.IO.Compression.ZipArchive"/>.
    /// </summary>
    /// <remarks>
    /// <para>Some content has no XPS equivalent and is refused rather than approximated: blend modes other than
    /// Normal, knockout groups, soft masks, gradients that do not extend, radial gradients whose start circle is not
    /// a point inside the end circle, CFF fonts, fonts whose licence forbids embedding, CMYK images, strokes of zero
    /// width and values that are not finite. <see cref="CanWrite(VectorPage, out string?)"/> gives the first such
    /// item; route those pages to raster.</para>
    /// <para>Colours are sRGB. Opacity and gradient colours are written as given.</para>
    /// <para>Output is the same bytes for the same pages: part names come from content hashes, and every entry has a
    /// fixed time stamp.</para>
    /// </remarks>
    public static class XpsWriter
    {
        /// <summary>The XPS 1.0 (ECMA-388 "XPS 2005/06") namespace.</summary>
        public const string Namespace = "http://schemas.microsoft.com/xps/2005/06";

        internal const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        internal const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        internal const string FixedRepresentationRelationship = "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation";
        internal const string RequiredResourceRelationship = "http://schemas.microsoft.com/xps/2005/06/required-resource";

        internal const string FixedDocumentSequencePart = "/FixedDocSeq.fdseq";
        internal const string FixedDocumentPart = "/Documents/1/FixedDoc.fdoc";

        private const double PixelsPerPoint = 96.0 / 72.0;
        private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        // ------------------------------------------------------------------ checks

        /// <summary>Whether every page can be written; <paramref name="reason"/> names the first page and item that cannot.</summary>
        public static bool CanWrite(IEnumerable<VectorPage> pages, out string? reason)
        {
            if (pages is null) throw new ArgumentNullException(nameof(pages));
            var checker = new Checker();
            int n = 0;
            foreach (VectorPage page in pages)
            {
                n++;
                if (page is null) throw new ArgumentException("A page is null.", nameof(pages));
                if (!checker.Check(page, out string? r))
                {
                    reason = "Page " + n + ": " + r;
                    return false;
                }
            }
            if (n == 0)
            {
                reason = "There are no pages.";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>Whether the page can be written; <paramref name="reason"/> names the first item that cannot.</summary>
        public static bool CanWrite(VectorPage page, out string? reason)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            return new Checker().Check(page, out reason);
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Writes the pages as an XPS package to <paramref name="output"/>, which is left open. Pages are read and
        /// written one at a time, so a long document is never held in memory; fonts and images are written once.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// A page holds content XPS cannot express (see <see cref="CanWrite(IEnumerable{VectorPage}, out string?)"/>).
        /// Pages are checked as they are reached, so the output is incomplete; check first when that matters.
        /// </exception>
        /// <exception cref="ArgumentException">There are no pages, or a page is null.</exception>
        public static void Write(IEnumerable<VectorPage> pages, Stream output)
        {
            if (pages is null) throw new ArgumentNullException(nameof(pages));
            if (output is null) throw new ArgumentNullException(nameof(output));
            if (!output.CanWrite) throw new ArgumentException("The output stream cannot be written.", nameof(output));

            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var package = new PackageState(zip);
            WriteXmlEntry(zip, "[Content_Types].xml", WriteContentTypes);
            WriteXmlEntry(zip, "_rels/.rels", w => WriteRelationships(w, new[] { (FixedRepresentationRelationship, FixedDocumentSequencePart) }));
            WriteXmlEntry(zip, FixedDocumentSequencePart.Substring(1), w =>
            {
                w.WriteStartElement("FixedDocumentSequence", Namespace);
                w.WriteStartElement("DocumentReference", Namespace);
                w.WriteAttributeString("Source", FixedDocumentPart);
                w.WriteEndElement();
                w.WriteEndElement();
            });

            var checker = new Checker();
            var pageSizes = new List<(double Width, double Height)>();
            foreach (VectorPage page in pages)
            {
                if (page is null) throw new ArgumentException("A page is null.", nameof(pages));
                int number = pageSizes.Count + 1;
                if (!checker.Check(page, out string? reason))
                    throw new NotSupportedException("Page " + number + " cannot be written as XPS: " + reason);
                WritePage(package, page, number);
                pageSizes.Add((page.Width * PixelsPerPoint, page.Height * PixelsPerPoint));
            }
            if (pageSizes.Count == 0) throw new ArgumentException("There are no pages.", nameof(pages));

            WriteXmlEntry(zip, FixedDocumentPart.Substring(1), w =>
            {
                w.WriteStartElement("FixedDocument", Namespace);
                for (int i = 0; i < pageSizes.Count; i++)
                {
                    w.WriteStartElement("PageContent", Namespace);
                    w.WriteAttributeString("Source", PagePart(i + 1));
                    w.WriteAttributeString("Width", Num(pageSizes[i].Width));
                    w.WriteAttributeString("Height", Num(pageSizes[i].Height));
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            });
        }

        /// <summary>Writes the pages as an XPS file (created or replaced).</summary>
        public static void Write(IEnumerable<VectorPage> pages, string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Write(pages, stream);
        }

        internal static string PagePart(int number) => "/Documents/1/Pages/" + number.ToString(CultureInfo.InvariantCulture) + ".fpage";

        internal static string PageRelsPart(int number) => "/Documents/1/Pages/_rels/" + number.ToString(CultureInfo.InvariantCulture) + ".fpage.rels";

        private static void WriteContentTypes(XmlWriter w)
        {
            w.WriteStartElement("Types", ContentTypesNamespace);
            void Default(string ext, string type)
            {
                w.WriteStartElement("Default", ContentTypesNamespace);
                w.WriteAttributeString("Extension", ext);
                w.WriteAttributeString("ContentType", type);
                w.WriteEndElement();
            }
            Default("rels", "application/vnd.openxmlformats-package.relationships+xml");
            Default("fdseq", "application/vnd.ms-package.xps-fixeddocumentsequence+xml");
            Default("fdoc", "application/vnd.ms-package.xps-fixeddocument+xml");
            Default("fpage", "application/vnd.ms-package.xps-fixedpage+xml");
            Default("odttf", "application/vnd.ms-package.obfuscated-opentype");
            Default("png", "image/png");
            Default("jpg", "image/jpeg");
            w.WriteEndElement();
        }

        private static void WriteRelationships(XmlWriter w, IEnumerable<(string Type, string Target)> rels)
        {
            w.WriteStartElement("Relationships", RelationshipsNamespace);
            int id = 0;
            foreach ((string type, string target) in rels)
            {
                w.WriteStartElement("Relationship", RelationshipsNamespace);
                w.WriteAttributeString("Id", "R" + (++id).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("Type", type);
                w.WriteAttributeString("Target", target);
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        private static XmlWriterSettings XmlSettings => new()
        {
            Encoding = Utf8,
            Indent = false,
            CloseOutput = false,
            CheckCharacters = true
        };

        private static void WriteXmlEntry(ZipArchive zip, string name, Action<XmlWriter> body)
        {
            using var buffer = new MemoryStream();
            using (XmlWriter w = XmlWriter.Create(buffer, XmlSettings))
            {
                w.WriteStartDocument();
                body(w);
                w.WriteEndDocument();
            }
            WriteEntry(zip, name, buffer.GetBuffer().AsSpan(0, (int)buffer.Length), CompressionLevel.Optimal);
        }

        private static void WriteEntry(ZipArchive zip, string name, ReadOnlySpan<byte> data, CompressionLevel level)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, level);
            entry.LastWriteTime = EntryTime;
            using Stream s = entry.Open();
            s.Write(data);
        }

        private static void WritePage(PackageState package, VectorPage page, int number)
        {
            var resources = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            using var buffer = new MemoryStream();
            using (XmlWriter w = XmlWriter.Create(buffer, XmlSettings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("FixedPage", Namespace);
                w.WriteAttributeString("Width", Num(page.Width * PixelsPerPoint));
                w.WriteAttributeString("Height", Num(page.Height * PixelsPerPoint));
                w.WriteAttributeString("xml", "lang", "http://www.w3.org/XML/1998/namespace", "und");
                w.WriteStartElement("Canvas", Namespace);
                // Page content is in points; one transform takes it to XPS units (1/96 inch).
                w.WriteAttributeString("RenderTransform", "1.333333333333,0,0,1.333333333333,0,0");
                var pageWriter = new PageWriter(package, w, part =>
                {
                    if (seen.Add(part)) resources.Add(part);
                });
                foreach (VectorElement e in page.Elements) pageWriter.WriteElement(e);
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            WriteEntry(package.Zip, PagePart(number).Substring(1), buffer.GetBuffer().AsSpan(0, (int)buffer.Length), CompressionLevel.Optimal);
            if (resources.Count > 0)
            {
                var rels = new List<(string, string)>();
                foreach (string r in resources) rels.Add((RequiredResourceRelationship, r));
                WriteXmlEntry(package.Zip, PageRelsPart(number).Substring(1), w => WriteRelationships(w, rels));
            }
        }

        // ------------------------------------------------------------------ numbers and colours

        /// <summary>A number as XPS writes it: invariant culture, no exponent, at most 6 decimals, no "-0".</summary>
        internal static string Num(double v)
        {
            double r = Math.Round(v, 6, MidpointRounding.AwayFromZero);
            if (r == 0) return "0";
            return r.ToString("0.######", CultureInfo.InvariantCulture);
        }

        internal static string Color(VectorColor c)
            => "#" + c.A.ToString("X2", CultureInfo.InvariantCulture) + c.R.ToString("X2", CultureInfo.InvariantCulture)
               + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);

        internal static string Matrix(VectorMatrix m)
            => Num(m.M11) + "," + Num(m.M12) + "," + Num(m.M21) + "," + Num(m.M22) + "," + Num(m.OffsetX) + "," + Num(m.OffsetY);

        internal static string Point(VectorPoint p) => Num(p.X) + "," + Num(p.Y);

        /// <summary>Path geometry in the XPS abbreviated syntax.</summary>
        internal static string PathData(VectorGeometry g)
        {
            var sb = new StringBuilder(g.Segments.Count * 16);
            sb.Append(g.FillRule == VectorFillRule.EvenOdd ? "F0" : "F1");
            foreach (VectorSegment s in g.Segments)
            {
                switch (s.Kind)
                {
                    case VectorSegmentKind.MoveTo: sb.Append(" M ").Append(Point(s.Point3)); break;
                    case VectorSegmentKind.LineTo: sb.Append(" L ").Append(Point(s.Point3)); break;
                    case VectorSegmentKind.CubicTo:
                        sb.Append(" C ").Append(Point(s.Point1)).Append(' ').Append(Point(s.Point2)).Append(' ').Append(Point(s.Point3));
                        break;
                    case VectorSegmentKind.Close: sb.Append(" Z"); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Obfuscates (or, applied again, restores) a font for an XPS package (ECMA-388 9.1.7.3): the first 32 bytes
        /// are XORed with the GUID in the part name, its bytes taken from the hex digits in reverse order.
        /// </summary>
        internal static byte[] ObfuscateFont(byte[] data, Guid key)
        {
            string hex = key.ToString("N");
            var g = new byte[16];
            for (int i = 0; i < 16; i++) g[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var result = (byte[])data.Clone();
            for (int i = 0; i < 32 && i < result.Length; i++) result[i] ^= g[15 - (i % 16)];
            return result;
        }

        /// <summary>A GUID made from a content hash (RFC 9562 version 8 layout), so the same font gives the same part.</summary>
        internal static Guid ContentGuid(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            var b = new byte[16];
            Array.Copy(hash, b, 16);
            b[6] = (byte)((b[6] & 0x0F) | 0x80);
            b[8] = (byte)((b[8] & 0x3F) | 0x80);
            return new Guid(b, bigEndian: true);
        }

        // ------------------------------------------------------------------ package state

        private sealed class PackageState
        {
            public PackageState(ZipArchive zip) => Zip = zip;

            public ZipArchive Zip { get; }
            public Dictionary<VectorFont, string> FontsByInstance { get; } = new(ReferenceEqualityComparer.Instance);
            public Dictionary<Guid, string> FontsByContent { get; } = new();
            public Dictionary<VectorImage, string> ImagesByInstance { get; } = new(ReferenceEqualityComparer.Instance);
            public Dictionary<string, string> ImagesByHash { get; } = new(StringComparer.Ordinal);

            public string FontPart(VectorFont font)
            {
                if (FontsByInstance.TryGetValue(font, out string? part)) return part;
                Guid key = ContentGuid(font.Data);
                if (!FontsByContent.TryGetValue(key, out part))
                {
                    part = "/Resources/Fonts/" + key.ToString("D").ToUpperInvariant() + ".odttf";
                    WriteEntry(Zip, part.Substring(1), ObfuscateFont(font.Data, key), CompressionLevel.Optimal);
                    FontsByContent[key] = part;
                }
                FontsByInstance[font] = part;
                return part;
            }

            public string ImagePart(VectorImage image)
            {
                if (ImagesByInstance.TryGetValue(image, out string? part)) return part;
                string hash = Convert.ToHexString(SHA256.HashData(image.Data));
                if (!ImagesByHash.TryGetValue(hash, out part))
                {
                    string ext = image.Format == VectorImageFormat.Jpeg ? ".jpg" : ".png";
                    part = "/Resources/Images/" + (ImagesByHash.Count + 1).ToString(CultureInfo.InvariantCulture) + ext;
                    WriteEntry(Zip, part.Substring(1), image.Data, CompressionLevel.NoCompression);
                    ImagesByHash[hash] = part;
                }
                ImagesByInstance[image] = part;
                return part;
            }
        }

        // ------------------------------------------------------------------ element writer

        private sealed class PageWriter
        {
            private readonly PackageState _package;
            private readonly XmlWriter _w;
            private readonly Action<string> _usesResource;

            public PageWriter(PackageState package, XmlWriter w, Action<string> usesResource)
            {
                _package = package;
                _w = w;
                _usesResource = usesResource;
            }

            public void WriteElement(VectorElement e)
            {
                switch (e)
                {
                    case VectorPath p: WritePath(p); break;
                    case VectorGlyphRun r: WriteGlyphRun(r); break;
                    case VectorImageElement i: WriteImage(i); break;
                    case VectorGroup g: WriteGroup(g); break;
                }
            }

            private void Common(VectorElement e)
            {
                if (!e.Transform.IsIdentity) _w.WriteAttributeString("RenderTransform", Matrix(e.Transform));
                if (e.Clip is not null) _w.WriteAttributeString("Clip", PathData(e.Clip));
                if (e.Opacity != 1) _w.WriteAttributeString("Opacity", Num(e.Opacity));
            }

            private void WriteGroup(VectorGroup g)
            {
                if (g.Children.Count == 0) return;
                _w.WriteStartElement("Canvas", Namespace);
                Common(g);
                foreach (VectorElement child in g.Children) WriteElement(child);
                _w.WriteEndElement();
            }

            private void WritePath(VectorPath p)
            {
                if (p.Geometry.IsEmpty || (p.Fill is null && p.Stroke is null)) return;
                _w.WriteStartElement("Path", Namespace);
                Common(p);
                bool fillAsAttribute = p.Fill is VectorSolidBrush { Opacity: 1 };
                bool strokeAsAttribute = p.Stroke?.Brush is VectorSolidBrush { Opacity: 1 };
                if (fillAsAttribute) _w.WriteAttributeString("Fill", Color(((VectorSolidBrush)p.Fill!).Color));
                if (p.Stroke is VectorStroke s)
                {
                    if (strokeAsAttribute) _w.WriteAttributeString("Stroke", Color(((VectorSolidBrush)s.Brush).Color));
                    _w.WriteAttributeString("StrokeThickness", Num(s.Width));
                    string cap = s.LineCap switch { VectorLineCap.Round => "Round", VectorLineCap.Square => "Square", _ => "Flat" };
                    _w.WriteAttributeString("StrokeStartLineCap", cap);
                    _w.WriteAttributeString("StrokeEndLineCap", cap);
                    _w.WriteAttributeString("StrokeLineJoin", s.LineJoin switch { VectorLineJoin.Round => "Round", VectorLineJoin.Bevel => "Bevel", _ => "Miter" });
                    if (s.LineJoin == VectorLineJoin.Miter) _w.WriteAttributeString("StrokeMiterLimit", Num(Math.Max(1, s.MiterLimit)));
                    if (s.DashArray.Count > 0)
                    {
                        // XPS dash lengths and offset are in multiples of the stroke thickness; an odd PDF array repeats.
                        int count = s.DashArray.Count % 2 == 1 ? s.DashArray.Count * 2 : s.DashArray.Count;
                        var sb = new StringBuilder();
                        for (int i = 0; i < count; i++)
                        {
                            if (i > 0) sb.Append(' ');
                            sb.Append(Num(s.DashArray[i % s.DashArray.Count] / s.Width));
                        }
                        _w.WriteAttributeString("StrokeDashArray", sb.ToString());
                        _w.WriteAttributeString("StrokeDashCap", cap);
                        if (s.DashOffset != 0) _w.WriteAttributeString("StrokeDashOffset", Num(s.DashOffset / s.Width));
                    }
                }
                _w.WriteAttributeString("Data", PathData(p.Geometry));
                if (p.Fill is not null && !fillAsAttribute)
                {
                    _w.WriteStartElement("Path.Fill", Namespace);
                    WriteBrush(p.Fill);
                    _w.WriteEndElement();
                }
                if (p.Stroke is not null && !strokeAsAttribute)
                {
                    _w.WriteStartElement("Path.Stroke", Namespace);
                    WriteBrush(p.Stroke.Brush);
                    _w.WriteEndElement();
                }
                _w.WriteEndElement();
            }

            private void WriteBrush(VectorBrush brush)
            {
                switch (brush)
                {
                    case VectorSolidBrush s:
                        _w.WriteStartElement("SolidColorBrush", Namespace);
                        _w.WriteAttributeString("Color", Color(s.Color));
                        if (s.Opacity != 1) _w.WriteAttributeString("Opacity", Num(s.Opacity));
                        _w.WriteEndElement();
                        break;
                    case VectorLinearGradientBrush l:
                        _w.WriteStartElement("LinearGradientBrush", Namespace);
                        GradientAttributes(l);
                        _w.WriteAttributeString("StartPoint", Point(l.Start));
                        _w.WriteAttributeString("EndPoint", Point(l.End));
                        GradientStops(l, "LinearGradientBrush.GradientStops");
                        _w.WriteEndElement();
                        break;
                    case VectorRadialGradientBrush r:
                        _w.WriteStartElement("RadialGradientBrush", Namespace);
                        GradientAttributes(r);
                        _w.WriteAttributeString("Center", Point(r.EndCenter));
                        _w.WriteAttributeString("GradientOrigin", Point(r.StartCenter));
                        _w.WriteAttributeString("RadiusX", Num(r.EndRadius));
                        _w.WriteAttributeString("RadiusY", Num(r.EndRadius));
                        GradientStops(r, "RadialGradientBrush.GradientStops");
                        _w.WriteEndElement();
                        break;
                }
            }

            private void GradientAttributes(VectorGradientBrush g)
            {
                if (g.Opacity != 1) _w.WriteAttributeString("Opacity", Num(g.Opacity));
                if (!g.Transform.IsIdentity) _w.WriteAttributeString("Transform", Matrix(g.Transform));
                _w.WriteAttributeString("ColorInterpolationMode", "SRgbLinearInterpolation");
                _w.WriteAttributeString("SpreadMethod", "Pad");
                _w.WriteAttributeString("MappingMode", "Absolute");
            }

            private void GradientStops(VectorGradientBrush g, string element)
            {
                _w.WriteStartElement(element, Namespace);
                // XPS needs at least two stops; a single stop is a flat colour.
                int repeat = g.Stops.Count == 1 ? 2 : 1;
                foreach (VectorGradientStop stop in g.Stops)
                {
                    for (int i = 0; i < repeat; i++)
                    {
                        _w.WriteStartElement("GradientStop", Namespace);
                        _w.WriteAttributeString("Color", Color(stop.Color));
                        _w.WriteAttributeString("Offset", Num(repeat == 2 ? i : stop.Offset));
                        _w.WriteEndElement();
                    }
                }
                _w.WriteEndElement();
            }

            private void WriteImage(VectorImageElement e)
            {
                string part = _package.ImagePart(e.Image);
                _usesResource(part);
                VectorImage img = e.Image;
                _w.WriteStartElement("Path", Namespace);
                Common(e);
                _w.WriteAttributeString("Data", "M 0,0 L 1,0 1,1 0,1 Z");
                _w.WriteStartElement("Path.Fill", Namespace);
                _w.WriteStartElement("ImageBrush", Namespace);
                _w.WriteAttributeString("ImageSource", part);
                _w.WriteAttributeString("Viewbox", "0,0," + Num(img.PixelWidth * 96.0 / img.DpiX) + "," + Num(img.PixelHeight * 96.0 / img.DpiY));
                _w.WriteAttributeString("ViewboxUnits", "Absolute");
                _w.WriteAttributeString("Viewport", "0,0,1,1");
                _w.WriteAttributeString("ViewportUnits", "Absolute");
                _w.WriteAttributeString("TileMode", "None");
                _w.WriteEndElement();
                _w.WriteEndElement();
                _w.WriteEndElement();
            }

            private void WriteGlyphRun(VectorGlyphRun run)
            {
                if (run.Glyphs.Count == 0) return;
                string part = _package.FontPart(run.Font);
                _usesResource(part);

                // One Glyphs element per baseline: XPS places glyphs by advances along one line.
                var lines = new List<(int Start, int Count)>();
                int start = 0;
                for (int i = 1; i <= run.Glyphs.Count; i++)
                {
                    if (i == run.Glyphs.Count || run.Glyphs[i].Y != run.Glyphs[start].Y)
                    {
                        lines.Add((start, i - start));
                        start = i;
                    }
                }

                bool wrap = lines.Count > 1;
                if (wrap)
                {
                    _w.WriteStartElement("Canvas", Namespace);
                    Common(run);
                }
                foreach ((int s, int c) in lines) WriteGlyphLine(run, part, s, c, applyCommon: !wrap);
                if (wrap) _w.WriteEndElement();
            }

            private void WriteGlyphLine(VectorGlyphRun run, string fontPart, int start, int count, bool applyCommon)
            {
                VectorGlyph first = run.Glyphs[start];
                _w.WriteStartElement("Glyphs", Namespace);
                if (applyCommon) Common(run);
                bool fillAsAttribute = run.Fill is VectorSolidBrush { Opacity: 1 };
                if (fillAsAttribute) _w.WriteAttributeString("Fill", Color(((VectorSolidBrush)run.Fill).Color));
                _w.WriteAttributeString("FontUri", fontPart);
                _w.WriteAttributeString("FontRenderingEmSize", Num(run.EmSize));
                _w.WriteAttributeString("OriginX", Num(first.X));
                _w.WriteAttributeString("OriginY", Num(first.Y));

                bool mapped = true;
                var text = new StringBuilder();
                for (int i = start; i < start + count; i++)
                {
                    if (!UsableText(run.Glyphs[i].Text))
                    {
                        mapped = false;
                        break;
                    }
                    text.Append(run.Glyphs[i].Text);
                }

                var indices = new StringBuilder();
                for (int i = start; i < start + count; i++)
                {
                    VectorGlyph g = run.Glyphs[i];
                    double advance = i + 1 < start + count ? run.Glyphs[i + 1].X - g.X : g.Advance;
                    if (i > start) indices.Append(';');
                    if (mapped && g.Text!.Length > 1) indices.Append('(').Append(g.Text.Length.ToString(CultureInfo.InvariantCulture)).Append(":1)");
                    indices.Append(g.GlyphId.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Num(advance / run.EmSize * 100));
                }
                _w.WriteAttributeString("Indices", indices.ToString());
                if (mapped)
                {
                    string s = text.ToString();
                    _w.WriteAttributeString("UnicodeString", s.StartsWith('{') ? "{}" + s : s);
                }
                if (!fillAsAttribute)
                {
                    _w.WriteStartElement("Glyphs.Fill", Namespace);
                    WriteBrush(run.Fill);
                    _w.WriteEndElement();
                }
                _w.WriteEndElement();
            }

            /// <summary>Text XPS can carry for one glyph: not empty, no control characters, no unpaired surrogates.</summary>
            internal static bool UsableText(string? s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch < 0x20 || ch == 0xFFFE || ch == 0xFFFF) return false;
                    if (char.IsHighSurrogate(ch))
                    {
                        if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false;
                        i++;
                    }
                    else if (char.IsLowSurrogate(ch)) return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------ checker

        private sealed class Checker
        {
            private readonly Dictionary<VectorFont, (bool Ok, string? Reason, XpsFontInfo Info)> _fonts = new(ReferenceEqualityComparer.Instance);
            private string? _reason;

            public bool Check(VectorPage page, out string? reason)
            {
                _reason = null;
                foreach (VectorElement e in page.Elements)
                {
                    if (e is null) return Fail(out reason, "an element is null");
                    if (!Element(e)) break;
                }
                reason = _reason;
                return reason is null;
            }

            private static bool Fail(out string? reason, string message)
            {
                reason = message;
                return false;
            }

            private bool No(string message)
            {
                _reason = message;
                return false;
            }

            private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

            private static bool Finite(VectorMatrix m)
                => Finite(m.M11) && Finite(m.M12) && Finite(m.M21) && Finite(m.M22) && Finite(m.OffsetX) && Finite(m.OffsetY);

            private static bool Finite(VectorPoint p) => Finite(p.X) && Finite(p.Y);

            private bool Geometry(VectorGeometry g, string what)
            {
                foreach (VectorSegment s in g.Segments)
                    if (!Finite(s.Point1) || !Finite(s.Point2) || !Finite(s.Point3)) return No(what + " has a coordinate that is not finite");
                return true;
            }

            private bool Element(VectorElement e)
            {
                string kind = e switch
                {
                    VectorPath => "a path",
                    VectorGlyphRun => "a glyph run",
                    VectorImageElement => "an image",
                    VectorGroup => "a group",
                    _ => "an element"
                };
                if (e is not (VectorPath or VectorGlyphRun or VectorImageElement or VectorGroup)) return No("an element of an unknown type");
                if (e.BlendMode != VectorBlendMode.Normal) return No(kind + " uses blend mode " + e.BlendMode + " (XPS has Normal only)");
                if (!Finite(e.Transform)) return No(kind + " has a transform that is not finite");
                if (!Finite(e.Opacity) || e.Opacity < 0 || e.Opacity > 1) return No(kind + " has an opacity outside 0..1");
                if (e.Clip is not null && !Geometry(e.Clip, "the clip of " + kind)) return false;

                switch (e)
                {
                    case VectorGroup g:
                        if (g.Knockout) return No("a knockout group (XPS has no knockout)");
                        if (g.SoftMask is not null) return No("a group with a soft mask (not written as XPS yet)");
                        foreach (VectorElement c in g.Children)
                        {
                            if (c is null) return No("a group holds a null element");
                            if (!Element(c)) return false;
                        }
                        return true;

                    case VectorPath p:
                        if (!Geometry(p.Geometry, kind)) return false;
                        if (p.Fill is not null && !Brush(p.Fill, "the fill of a path")) return false;
                        if (p.Stroke is VectorStroke s)
                        {
                            if (!Finite(s.Width) || s.Width <= 0) return No("a stroke of zero or negative width (give the thinnest width wanted)");
                            if (!Finite(s.MiterLimit) || !Finite(s.DashOffset)) return No("a stroke with a value that is not finite");
                            double total = 0;
                            foreach (double d in s.DashArray)
                            {
                                if (!Finite(d) || d < 0) return No("a dash length that is negative or not finite");
                                total += d;
                            }
                            if (s.DashArray.Count > 0 && total == 0) return No("a dash pattern whose lengths are all zero");
                            if (!Brush(s.Brush, "the stroke of a path")) return false;
                        }
                        return true;

                    case VectorGlyphRun r:
                        if (!Finite(r.EmSize) || r.EmSize <= 0) return No("a glyph run with an em size that is not positive");
                        if (!Brush(r.Fill, "the fill of a glyph run")) return false;
                        if (!_fonts.TryGetValue(r.Font, out var font))
                        {
                            bool ok = XpsFontCheck.TryRead(r.Font.Data, out XpsFontInfo info, out string? why);
                            font = (ok, why, info);
                            _fonts[r.Font] = font;
                        }
                        if (!font.Ok) return No("font " + (r.Font.Name ?? "(unnamed)") + " " + font.Reason);
                        foreach (VectorGlyph g in r.Glyphs)
                        {
                            if (!Finite(g.X) || !Finite(g.Y) || !Finite(g.Advance)) return No("a glyph position that is not finite");
                            if (font.Info.GlyphCount > 0 && g.GlyphId >= font.Info.GlyphCount)
                                return No("glyph " + g.GlyphId + " is not in font " + (r.Font.Name ?? "(unnamed)") + " (" + font.Info.GlyphCount + " glyphs)");
                        }
                        return true;

                    case VectorImageElement i:
                        if (i.Image.Components != 1 && i.Image.Components != 3)
                            return No("an image with " + i.Image.Components + " colour components (only grey and RGB are written)");
                        if (!Finite(i.Image.DpiX) || !Finite(i.Image.DpiY) || i.Image.DpiX <= 0 || i.Image.DpiY <= 0)
                            return No("an image with an unusable resolution");
                        return true;
                }
                return true;
            }

            private bool Brush(VectorBrush brush, string what)
            {
                if (!Finite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return No(what + " has an opacity outside 0..1");
                if (brush is not VectorGradientBrush g) return true;
                if (!Finite(g.Transform)) return No(what + " has a gradient transform that is not finite");
                if (g.Stops.Count == 0) return No(what + " is a gradient with no stops");
                double last = 0;
                foreach (VectorGradientStop s in g.Stops)
                {
                    if (!Finite(s.Offset) || s.Offset < 0 || s.Offset > 1 || s.Offset < last)
                        return No(what + " is a gradient whose stop offsets are not increasing within 0..1");
                    last = s.Offset;
                }
                if (!g.ExtendStart || !g.ExtendEnd) return No(what + " is a gradient that does not extend at both ends (XPS always extends)");
                switch (g)
                {
                    case VectorLinearGradientBrush l:
                        if (!Finite(l.Start) || !Finite(l.End)) return No(what + " has a gradient point that is not finite");
                        if (l.Start == l.End) return No(what + " is a linear gradient with no length");
                        return true;
                    case VectorRadialGradientBrush r:
                        if (!Finite(r.StartCenter) || !Finite(r.EndCenter) || !Finite(r.StartRadius) || !Finite(r.EndRadius))
                            return No(what + " has a gradient value that is not finite");
                        if (r.StartRadius != 0) return No(what + " is a radial gradient whose start circle is not a point");
                        if (r.EndRadius <= 0) return No(what + " is a radial gradient with no radius");
                        double dx = r.StartCenter.X - r.EndCenter.X, dy = r.StartCenter.Y - r.EndCenter.Y;
                        if (Math.Sqrt(dx * dx + dy * dy) > r.EndRadius) return No(what + " is a radial gradient whose start point is outside the end circle");
                        return true;
                }
                return true;
            }
        }
    }
}
