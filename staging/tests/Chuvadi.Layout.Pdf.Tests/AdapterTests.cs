using System.Buffers.Binary;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Text;
using PdfShaping = Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Layout.Pdf.Tests;

/// <summary>Fonts from the OFL packs (LiPi from Chuvadi.Fonts, the rest from Chuvadi.Layout.Fonts), and layout helpers.</summary>
internal static class Fixture
{
    public const string Tamil = "LiPi-Sans-Tamil-Regular.ttf";

    public static byte[] FontBytes(string file) => file.StartsWith("LiPi-", StringComparison.Ordinal) ? Chuvadi.Fonts.FontPack.ReadAllBytes(file) : BundledFontSource.ReadResource(file);

    public static FontFace Face(string file) => FontFace.Load(FontBytes(file), 0, FontOrigin.Supplied, file);

    public static LayoutOptions Options(IShaper? shaper = null)
    {
        LayoutOptions o = new LayoutOptions
        {
            UseEmbeddedFonts = false,
            AllowSystemFonts = false,
            Shaper = shaper,
            Now = new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.FromHours(5.5)),
        };
        MemoryFontSource fonts = new MemoryFontSource("Test fonts");
        foreach (string f in new[] { "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSerif-Regular.ttf", Tamil })
        {
            fonts.Add(FontBytes(f), f);
        }
        o.FontSources.Add(fonts);
        return o;
    }

    public static CharacterFormat Sans12 { get; } = new CharacterFormat { FontFamily = "Liberation Sans", FontSize = 12 };

    public static FlowDocument Doc(params FlowBlock[] blocks)
    {
        FlowDocument d = new FlowDocument();
        FlowSection s = new FlowSection();
        foreach (FlowBlock b in blocks)
        {
            s.Blocks.Add(b);
        }
        d.Sections.Add(s);
        return d;
    }

    public static LaidOutDocument Layout(params string[] paragraphs)
        => DocumentLayout.Open(Doc(paragraphs.Select(p => (FlowBlock)FlowParagraph.Of(p, Sans12, new ParagraphFormat())).ToArray()), Options());

    public static PdfDocument OpenPdf(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf, writable: false));

    public static string Latin1(byte[] data) => Encoding.Latin1.GetString(data);

    /// <summary>The hex-string form chuvadi-pdf writes for ASCII text strings.</summary>
    public static string Hex(string ascii) => "<" + Convert.ToHexString(Encoding.ASCII.GetBytes(ascii)) + ">";

    /// <summary>A display list of one glyph run, for driving the translator directly.</summary>
    public static GlyphRunOp Run(FontFace face, string text, double x, double y, double size = 12, bool italic = false)
    {
        List<PositionedGlyph> glyphs = new List<PositionedGlyph>();
        double pen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int gid = face.GetGlyphId(text[i]);
            double adv = face.ToPoints(face.GetAdvance(gid), size);
            glyphs.Add(new PositionedGlyph(gid, pen, 0, adv, i));
            pen += adv;
        }
        return new GlyphRunOp { Font = face, FontSize = size, X = x, Y = y, Glyphs = glyphs, Text = text, SyntheticItalic = italic };
    }

    /// <summary>Renders the first page of a PDF at 72 dpi (one pixel per point).</summary>
    public static PixelBuffer Render(byte[] pdf)
    {
        using PdfDocument doc = OpenPdf(pdf);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AntiAlias = false }).Rasterize(doc.Pages[0]);
    }

    /// <summary>True when the pixel is darker than mid-grey.</summary>
    public static bool Dark(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte a) = p.GetPixelBgra(x, y);
        return a > 0 && r + g + b < 3 * 128;
    }

    /// <summary>The number of dark pixels in a rectangle.</summary>
    public static int DarkCount(PixelBuffer p, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                n += Dark(p, x, y) ? 1 : 0;
            }
        }
        return n;
    }

    public static byte[] Translate(LayoutDisplayList list, Action<LayoutWarning>? warn = null)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageTranslator t = new PageTranslator(builder, new Dictionary<FontFace, string?>(ReferenceEqualityComparer.Instance), new HashSet<string>(), warn);
        t.Draw(list, builder.AddPage(new PageSize(list.WidthPt, list.HeightPt)));
        using MemoryStream ms = new MemoryStream();
        builder.Save(ms);
        return ms.ToArray();
    }
}

public class PdfOutputTests
{
    [Fact]
    public void OnePagePdf_OpensWithPageSizeAndEmbeddedSubset()
    {
        using LaidOutDocument doc = Fixture.Layout("Hello, world.");
        LaidOutPage page = doc.GetPage(0);
        byte[] pdf = page.ToPdf(new LayoutPdfOptions { Title = "Adapter test" });

        Assert.StartsWith("%PDF-", Fixture.Latin1(pdf[..8]));
        using PdfDocument read = Fixture.OpenPdf(pdf);
        Assert.Single(read.Pages);
        string raw = Fixture.Latin1(pdf);
        Assert.Contains("/FontFile2", raw);
        Assert.Contains("/Title " + Fixture.Hex("Adapter test"), raw);
        Assert.Equal(page.WidthPt, read.Pages[0].Width, 0.1);
        Assert.Equal(page.HeightPt, read.Pages[0].Height, 0.1);
    }

    [Fact]
    public void WritePdf_WritesEveryPage()
    {
        string para = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 40));
        using LaidOutDocument doc = Fixture.Layout(Enumerable.Repeat(para, 12).ToArray());
        int pages = doc.ResolvePageCount();
        Assert.True(pages >= 3, $"expected several pages, got {pages}");

        using MemoryStream ms = new MemoryStream();
        doc.ToPdf(ms);
        using PdfDocument read = Fixture.OpenPdf(ms.ToArray());
        Assert.Equal(pages, read.Pages.Count);
    }

    [Fact]
    public void WritePdf_RejectsNoPages()
    {
        Assert.Throws<ArgumentException>(() => LayoutPdf.WritePdf(Array.Empty<LaidOutPage>(), new MemoryStream()));
    }

    [Fact]
    public void Svg_IsProduced()
    {
        using LaidOutDocument doc = Fixture.Layout("SVG output");
        string svg = doc.GetPage(0).ToSvg(new LayoutPdfOptions { Title = "T" });
        System.Xml.Linq.XDocument d = System.Xml.Linq.XDocument.Parse(svg);
        System.Xml.Linq.XNamespace ns = "http://www.w3.org/2000/svg";
        // Layout's own writer: glyph outlines plus the source text in the (invisible) text layer.
        Assert.NotEmpty(d.Descendants(ns + "use"));
        Assert.Equal("SVG output", string.Join(" ", d.Descendants(ns + "text").Select(t => t.Value.Trim()).Where(v => v.Length > 0)));
        Assert.Equal("T", d.Descendants(ns + "title").Single().Value);
    }

    [Fact]
    public void Svg_ViaPdf_IsStillAvailable()
    {
        using LaidOutDocument doc = Fixture.Layout("SVG output");
        Assert.Contains("<svg", LayoutPdf.ToSvgViaPdf(doc.GetPage(0)));
    }

    [Theory]
    [InlineData(72)]
    [InlineData(96)]
    public void Png_HasSignatureAndDpiScaledWidth(double dpi)
    {
        using LaidOutDocument doc = Fixture.Layout("PNG output");
        LaidOutPage page = doc.GetPage(0);
        byte[] png = page.ToPng(dpi);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        Assert.InRange(width, (int)Math.Floor(page.WidthPt / 72 * dpi) - 1, (int)Math.Ceiling(page.WidthPt / 72 * dpi) + 1);
    }

    [Fact]
    public void Png_RejectsBadDpi()
    {
        using LaidOutDocument doc = Fixture.Layout("x");
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPage(0).ToPng(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPage(0).ToPng(5000));
    }

    [Fact]
    public void Hyperlink_WritesUriAnnotation()
    {
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[] { new LinkOp(new LayoutRect(72, 72, 100, 14), "https://example.com/") }, 300, 300);
        string raw = Fixture.Latin1(Fixture.Translate(list));
        Assert.Contains("/URI", raw);
        Assert.Contains("/URI " + Fixture.Hex("https://example.com/"), raw);
    }

    [Fact]
    public void InternalPageLink_WritesGoTo()
    {
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[] { new LinkOp(new LayoutRect(72, 72, 100, 14), null, "intro", 0) }, 300, 300);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        string raw = Fixture.Latin1(Fixture.Translate(list, warnings.Add));
        Assert.Contains("/Link", raw);
        Assert.DoesNotContain("/URI", raw);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SyntheticItalic_AndScaling_AreDrawn_WithoutWarnings()
    {
        FontFace face = Fixture.Face("LiberationSans-Regular.ttf");
        GlyphRunOp upright = Fixture.Run(face, "IIII", 10, 40, 30);
        GlyphRunOp slanted = Fixture.Run(face, "IIII", 10, 40, 30, italic: true);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        PixelBuffer a = Fixture.Render(Fixture.Translate(new LayoutDisplayList(new LayoutOp[] { upright }, 200, 60), warnings.Add));
        PixelBuffer b = Fixture.Render(Fixture.Translate(new LayoutDisplayList(new LayoutOp[] { slanted }, 200, 60), warnings.Add));
        Assert.Empty(warnings);
        // The slant moves the tops of the stems right: the upper band of the italic run starts further right.
        static int FirstDarkX(PixelBuffer p, int y)
        {
            for (int x = 0; x < p.Width; x++)
            {
                if (Fixture.Dark(p, x, y))
                {
                    return x;
                }
            }
            return -1;
        }
        Assert.True(FirstDarkX(b, 20) > FirstDarkX(a, 20));
    }

    [Fact]
    public void Clip_CutsGlyphsAndFills_AtTheClipEdge()
    {
        FontFace face = Fixture.Face("LiberationSans-Regular.ttf");
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[]
        {
            new PushClipOp(new LayoutRect(0, 0, 100, 100)),
            new FillRectOp(new LayoutRect(50, 50, 100, 20), LayoutColor.Black),
            Fixture.Run(face, "MMMMMMMM", 60, 95, 24),
            PopClipOp.Instance,
            new FillRectOp(new LayoutRect(150, 150, 20, 20), LayoutColor.Black),
        }, 200, 200);
        PixelBuffer p = Fixture.Render(Fixture.Translate(list));
        Assert.True(Fixture.DarkCount(p, 55, 55, 95, 65) > 300, "inside the clip the fill is drawn");
        Assert.Equal(0, Fixture.DarkCount(p, 102, 50, 150, 70)); // the rest of the fill is cut off
        Assert.Equal(0, Fixture.DarkCount(p, 102, 72, 200, 100)); // and so are the glyphs past the edge
        Assert.True(Fixture.DarkCount(p, 60, 75, 99, 99) > 0, "the glyphs inside are drawn, cut at the edge");
        Assert.True(Fixture.DarkCount(p, 152, 152, 168, 168) > 200, "after PopClip drawing is unclipped");
    }

    [Fact]
    public void Translucency_IsDrawnWithOpacity()
    {
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[] { new FillRectOp(new LayoutRect(10, 10, 50, 50), new LayoutColor(0, 0, 0, 128)) }, 100, 100);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        PixelBuffer p = Fixture.Render(Fixture.Translate(list, warnings.Add));
        Assert.Empty(warnings);
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(30, 30);
        // Alpha 128 black over white, blended in sRGB (Chuvadi.Pdf 3.26, like Acrobat and pdf.js): 255 x (1 - 128/255) = 127.
        Assert.InRange(r, 126, 128);
        Assert.True(Fixture.Dark(p, 5, 5) is false);
    }

    [Fact]
    public void DashedLines_UseALineStyle()
    {
        LayoutDisplayList dashed = new LayoutDisplayList(new LayoutOp[] { new StrokeLineOp(10, 50, 190, 50, 2, LayoutColor.Black, LineDash.Dash) }, 200, 100);
        PixelBuffer p = Fixture.Render(Fixture.Translate(dashed));
        int dark = Fixture.DarkCount(p, 10, 48, 190, 52);
        int solid = 180 * 2;
        Assert.InRange(dark, solid / 3, solid * 3 / 4); // 4-on 3-off, in units of the width
    }

    [Fact]
    public void Images_AreCropped()
    {
        // 2x1 PNG: left pixel black, right pixel white; the crop keeps the left pixel only.
        byte[] png = TwoPixelPng();
        LayoutImage image = LayoutImage.FromEncoded(png);
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[] { new ImageOp(image, new LayoutRect(10, 10, 80, 80), new LayoutRect(0, 0, 1, 1)) }, 100, 100);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        PixelBuffer p = Fixture.Render(Fixture.Translate(list, warnings.Add));
        Assert.Empty(warnings);
        Assert.True(Fixture.Dark(p, 80, 50), "the cropped (black) half fills the whole box");
    }

    private static byte[] TwoPixelPng(bool alpha = false)
    {
        static uint Crc(byte[] d)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte x in d)
            {
                c ^= x;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
                }
            }
            return ~c;
        }
        static byte[] Chunk(string type, byte[] data)
        {
            byte[] td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            byte[] len = new byte[4], crc = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(td));
            return len.Concat(td).Concat(crc).ToArray();
        }
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, 2);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), 1);
        ihdr[8] = 8;
        ihdr[9] = (byte)(alpha ? 6 : 2);
        using MemoryStream z = new MemoryStream();
        using (System.IO.Compression.ZLibStream zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            zs.Write(alpha ? new byte[] { 0, 0, 0, 0, 255, 255, 255, 255, 0 } : new byte[] { 0, 0, 0, 0, 255, 255, 255 });
        }
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Chunk("IHDR", ihdr)).Concat(Chunk("IDAT", z.ToArray())).Concat(Chunk("IEND", Array.Empty<byte>())).ToArray();
    }

    [Fact]
    public void AlphaPng_IsEmbeddedWithASoftMask()
    {
        LayoutImage image = LayoutImage.FromEncoded(TwoPixelPng(alpha: true));
        LayoutDisplayList list = new LayoutDisplayList(new LayoutOp[] { new ImageOp(image, new LayoutRect(10, 10, 80, 40)) }, 100, 60);
        byte[] pdf = Fixture.Translate(list);
        Assert.Contains("/SMask", Fixture.Latin1(pdf));
        PixelBuffer p = Fixture.Render(pdf);
        Assert.True(Fixture.Dark(p, 30, 30), "the opaque black pixel is drawn");
        Assert.False(Fixture.Dark(p, 70, 30), "the transparent pixel shows the page");
    }

    [Fact]
    public void CollectionAndCffFaces_AreEmbedded()
    {
        foreach (string file in new[] { "ChuvadiTest.ttc", "ChuvadiTest-Cff.otf" })
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fonts", file);
            FontFace face = FontFace.Load(File.ReadAllBytes(path), file.EndsWith(".ttc") ? 1 : 0, FontOrigin.Supplied, file);
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            string text = "Hello";
            byte[] pdf = Fixture.Translate(new LayoutDisplayList(new LayoutOp[] { Fixture.Run(face, text, 10, 40, 20) }, 200, 60), warnings.Add);
            Assert.Empty(warnings);
            using PdfDocument read = Fixture.OpenPdf(pdf);
            Assert.Contains(text, new TextExtractor(read.Objects).ExtractText(read.Pages[0]), StringComparison.Ordinal);
            Assert.True(Fixture.DarkCount(Fixture.Render(pdf), 0, 0, 200, 60) > 20, file + " draws");
        }
    }
}

public class PdfShaperTests
{
    // KA + O-sign: the O sign splits into a pre-base E part and a post-base AA part.
    private const string Ko = "\u0B95\u0BCA";

    // KA + VIRAMA + SSA + I-sign, then a space and TA.
    private const string Conjunct = "\u0B95\u0BCD\u0BB7\u0BBF \u0BA4";

    [Fact]
    public void Supports_IndicFacesWithGsubOnly()
    {
        PdfShaper shaper = new PdfShaper();
        FontFace tamil = Fixture.Face(Fixture.Tamil);
        Assert.True(shaper.Supports(tamil, UnicodeScript.Tamil));
        Assert.False(shaper.Supports(tamil, UnicodeScript.Latin));
    }

    [Fact]
    public void Shape_ShapesTheWholeRun_AndReportsClustersInLayoutText()
    {
        // The run starts at index 3 of Layout's text; Chuvadi.Pdf's clusters are UTF-16 indices into the run.
        string text = "ab " + Conjunct;
        FontFace face = Fixture.Face(Fixture.Tamil);
        IReadOnlyList<ShapedGlyph> ours = new PdfShaper().Shape(face, text, 3, Conjunct.Length, new ShapingOptions { Script = UnicodeScript.Tamil });
        IReadOnlyList<PdfShaping.ShapedGlyph> direct = PdfShaping.TextShaper.Shape(Fixture.FontBytes(Fixture.Tamil), Conjunct, PdfShaping.LipiScript.Tamil);
        Assert.Equal(direct.Select(g => g.GlyphId), ours.Select(g => g.GlyphId));
        Assert.Equal(direct.Select(g => 3 + g.Cluster), ours.Select(g => g.Cluster));
        Assert.All(ours.Take(direct.Count(g => g.Cluster == 0)), g => Assert.Equal(3, g.Cluster)); // the conjunct is one cluster
    }

    [Fact]
    public void Shape_AppliesContextualFormsAcrossSyllables()
    {
        // Bengali \u09AA\u09CD\u09B0\u09A4\u09BF\u09AC\u09C7\u09A6\u09A8: shaped as a whole word, as HarfBuzz does (syllable-by-syllable shaping chose another glyph).
        const string word = "\u09AA\u09CD\u09B0\u09A4\u09BF\u09AC\u09C7\u09A6\u09A8";
        const string file = "LiPi-Sans-Bengali-Regular.ttf";
        IReadOnlyList<ShapedGlyph> ours = new PdfShaper().Shape(Fixture.Face(file), word, 0, word.Length, new ShapingOptions { Script = UnicodeScript.Bengali });
        IReadOnlyList<PdfShaping.ShapedGlyph> direct = PdfShaping.TextShaper.Shape(Fixture.FontBytes(file), word, PdfShaping.LipiScript.Bengali);
        Assert.Equal(direct.Select(g => g.GlyphId), ours.Select(g => g.GlyphId));
    }

    [Fact]
    public void Shape_MatchesTextShaperGlyphs_WithMergedClusters()
    {
        FontFace face = Fixture.Face(Fixture.Tamil);
        IReadOnlyList<ShapedGlyph> ours = new PdfShaper().Shape(face, Ko, 0, Ko.Length, new ShapingOptions { Script = UnicodeScript.Tamil });
        IReadOnlyList<PdfShaping.ShapedGlyph> direct = PdfShaping.TextShaper.Shape(Fixture.FontBytes(Fixture.Tamil), Ko, PdfShaping.LipiScript.Tamil);

        Assert.Equal(direct.Select(g => g.GlyphId), ours.Select(g => g.GlyphId));
        Assert.All(ours, g => Assert.Equal(0, g.Cluster));
        Assert.True(ours.Count >= 2, "the O sign should decompose into two parts");
        double toUnits = face.UnitsPerEm / 1000.0;
        Assert.Equal(direct.Sum(g => g.XAdvance) * toUnits, ours.Sum(g => g.XAdvance), 0.5 * ours.Count + 1);
    }

    [Fact]
    public void Layout_WithPdfShaper_ProducesTamilPdf()
    {
        CharacterFormat cf = new CharacterFormat { FontFamily = "LiPi Sans Tamil", ComplexScriptFontFamily = "LiPi Sans Tamil", FontSize = 14 };
        FlowDocument d = Fixture.Doc(FlowParagraph.Of(Conjunct + " " + Ko, cf, new ParagraphFormat()));
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        LayoutOptions o = Fixture.Options(new PdfShaper());
        using LaidOutDocument doc = DocumentLayout.Open(d, o);
        Assert.DoesNotContain(doc.Warnings, w => w.Code == "ComplexScriptNotShaped");
        byte[] pdf = doc.GetPage(0).ToPdf(new LayoutPdfOptions { Warning = warnings.Add });
        using PdfDocument read = Fixture.OpenPdf(pdf);
        Assert.Single(read.Pages);
    }
}

/// <summary>
/// Text extraction from Layout PDFs. Since Chuvadi.Pdf 3.25, DrawShapedRun records each glyph's source text
/// (ToUnicode, or /ActualText for conjuncts and split vowels), so PDFs extract, copy and search as written.
/// </summary>
public class PdfTextExtractionTests
{
    [Fact]
    public void GlyphsAreDrawn()
    {
        using LaidOutDocument doc = Fixture.Layout("Searchable text");
        using PdfDocument read = Fixture.OpenPdf(doc.GetPage(0).ToPdf());
        string text = new TextExtractor(read.Objects).ExtractText(read.Pages[0]);
        Assert.Equal("Searchable text".Replace(" ", string.Empty, StringComparison.Ordinal).Length, text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Length);
    }

    [Fact]
    public void ExtractedText_IsTheSourceText()
    {
        using LaidOutDocument doc = Fixture.Layout("Searchable text");
        using PdfDocument read = Fixture.OpenPdf(doc.GetPage(0).ToPdf());
        string text = new TextExtractor(read.Objects).ExtractText(read.Pages[0]);
        Assert.Contains("Searchable text", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShapedTamil_ExtractsAsWritten()
    {
        const string tamil = "\u0B95\u0BCA\u0B9F\u0BC1"; // KO and DU: a split vowel and a ligature
        CharacterFormat cf = new CharacterFormat { FontFamily = "LiPi Sans Tamil", ComplexScriptFontFamily = "LiPi Sans Tamil", FontSize = 14 };
        using LaidOutDocument doc = DocumentLayout.Open(Fixture.Doc(FlowParagraph.Of(tamil, cf, new ParagraphFormat())), Fixture.Options(new PdfShaper()));
        using PdfDocument read = Fixture.OpenPdf(doc.GetPage(0).ToPdf());
        string text = new TextExtractor(read.Objects).ExtractText(read.Pages[0]);
        // Every character comes back in order, with no space inside the word (Chuvadi.Pdf 3.26 measures /ActualText spans
        // by the glyphs they replace).
        Assert.Equal(tamil, text.Trim());
    }
}

public class PdfFaceTests
{
    /// <summary>Chuvadi.Pdf's FontFace and Chuvadi.Layout's own font core read the same metrics from the same file.</summary>
    [Theory]
    [InlineData("LiberationSans-Regular.ttf")]
    [InlineData("LiberationSerif-Regular.ttf")]
    [InlineData(Fixture.Tamil)]
    public void PdfFaceMetrics_MatchLayoutsFontCore(string file)
    {
        FontFace layout = Fixture.Face(file);
        Chuvadi.Pdf.Fonts.Rendering.FontFace pdf = PdfFaces.Get(layout);
        Assert.Equal(layout.UnitsPerEm, pdf.UnitsPerEm);
        Assert.Equal(layout.GlyphCount, pdf.GlyphCount);
        foreach (char c in "AaWg0 ")
        {
            int gid = layout.GetGlyphId(c);
            Assert.Equal(gid, pdf.GetGlyphIndex(c));
            Assert.Equal(layout.GetAdvance(gid), pdf.GetAdvanceWidth(gid));
        }
        Assert.Same(pdf, PdfFaces.Get(layout)); // loaded once per face
    }
}
