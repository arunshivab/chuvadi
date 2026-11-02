using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Tests;

public class FontFaceTests
{
    [Fact]
    public void Carlito_identity_and_metrics_match_the_font_tables()
    {
        FontFace f = TestFonts.Face("Carlito-Regular.ttf");
        Assert.Equal("Carlito", f.FamilyName);
        Assert.Equal("Regular", f.SubfamilyName);
        Assert.Equal(400, f.Weight);
        Assert.False(f.IsItalic);
        Assert.Equal(FontOutlineFormat.TrueType, f.OutlineFormat);
        Assert.Equal(2048, f.UnitsPerEm);
        Assert.Equal(new FontLineMetrics(1950, -550, 0), f.WindowsMetrics);
        Assert.Equal(new FontLineMetrics(1950, -550, 0), f.HheaMetrics);
        Assert.Equal(new FontLineMetrics(1536, -512, 452), f.TypoMetrics);
        Assert.Equal(1038, f.GetAdvance(f.GetGlyphId('0')));
        Assert.Equal(3, f.GetGlyphId('A'));
        Assert.Equal(1185, f.GetAdvance(3));
        Assert.Equal(463, f.GetAdvance(f.GetGlyphId(' ')));
    }

    [Fact]
    public void Carlito_single_line_height_at_11pt_matches_Calibri_in_Word()
    {
        // Word uses usWinAscent + usWinDescent: (1950 + 550) / 2048 * 11 = 13.4277 pt.
        FontFace f = TestFonts.Face("Carlito-Regular.ttf");
        double h = f.ToPoints(f.WindowsMetrics.LineHeight, 11);
        Assert.Equal(13.4277, h, 4);
    }

    [Fact]
    public void Style_bits_are_read_for_bold_and_italic_faces()
    {
        FontFace bi = TestFonts.Face("LiberationSerif-BoldItalic.ttf");
        Assert.Equal("Liberation Serif", bi.FamilyName);
        Assert.True(bi.IsBold);
        Assert.True(bi.IsItalic);
        Assert.True(bi.ItalicAngle < 0);
        FontFace mono = TestFonts.Face("LiberationMono-Regular.ttf");
        Assert.True(mono.IsFixedPitch);
    }

    [Fact]
    public void Simple_measure_sums_advances()
    {
        FontFace f = TestFonts.Face("LiberationSans-Regular.ttf");
        // Sum of advances for "Hello" is 4667 units at 2048 upem.
        Assert.Equal(4667 * 12.0 / 2048, f.MeasureSimple("Hello", 12), 6);
    }

    [Theory]
    [InlineData("LiberationSans-Regular.ttf", 'A', 'V', -152)]
    [InlineData("LiberationSans-Regular.ttf", 'T', 'o', -227)]
    [InlineData("LiberationSerif-Regular.ttf", 'A', 'V', -264)]
    [InlineData("Carlito-Regular.ttf", 'A', 'V', -89)]
    [InlineData("Carlito-Regular.ttf", 'T', 'o', -182)]
    [InlineData("Carlito-Regular.ttf", 'L', 'T', -150)]
    public void Gpos_pair_kerning_matches_reference_values(string file, char left, char right, int expected)
    {
        FontFace f = TestFonts.Face(file);
        Assert.True(f.HasKerning);
        Assert.Equal(expected, f.GetKerning(f.GetGlyphId(left), f.GetGlyphId(right)));
    }

    [Fact]
    public void Unkerned_pair_is_zero()
    {
        FontFace f = TestFonts.Face("Carlito-Regular.ttf");
        Assert.Equal(0, f.GetKerning(f.GetGlyphId('o'), f.GetGlyphId('o')));
    }

    [Theory]
    [InlineData("Carlito-Regular.ttf", 11.0, 7)]          // Excel reference: Calibri 11 \u2192 MDW 7 px (default column = 64 px)
    [InlineData("LiberationSans-Regular.ttf", 10.0, 7)]   // Excel reference: Arial 10 \u2192 MDW 7 px
    public void Max_digit_width_reproduces_Excel_reference_points(string file, double size, int expected)
    {
        Assert.Equal(expected, TestFonts.Face(file).MaxDigitWidthPixels(size));
    }

    [Fact]
    public void Indic_face_maps_its_script_and_not_latin_specific_glyphs()
    {
        FontFace ta = TestFonts.Face("LiPi-Sans-Tamil-Regular.ttf");
        Assert.Equal(25, ta.GetGlyphId(0x0BA4));
        Assert.Equal(825, ta.GetAdvance(25));
        Assert.True(ta.HasGsub);
        FontFace carlito = TestFonts.Face("Carlito-Regular.ttf");
        Assert.False(carlito.HasGlyph(0x0BA4));
    }

    [Fact]
    public void Collection_faces_are_read_by_index()
    {
        byte[] ttc = TestFonts.BuildCollection(TestFonts.Bytes("Carlito-Regular.ttf"), TestFonts.Bytes("Caladea-Bold.ttf"));
        Assert.Equal(2, FontFace.CountFaces(ttc));
        FontFace a = FontFace.Load(ttc, 0);
        FontFace b = FontFace.Load(ttc, 1);
        Assert.Equal("Carlito", a.FamilyName);
        Assert.Equal("Caladea", b.FamilyName);
        Assert.True(b.IsBold);
        Assert.Equal(1038, a.GetAdvance(a.GetGlyphId('0')));
        Assert.Throws<FontFormatException>(() => FontFace.Load(ttc, 2));
    }

    [Fact]
    public void Non_font_and_truncated_data_throw_FontFormatException()
    {
        Assert.Throws<FontFormatException>(() => FontFace.Load(new byte[] { 1, 2, 3 }));
        Assert.Throws<FontFormatException>(() => FontFace.Load(System.Text.Encoding.ASCII.GetBytes("wOF2 not supported here")));
        byte[] good = TestFonts.Bytes("Carlito-Regular.ttf");
        Assert.Throws<FontFormatException>(() => FontFace.Load(good.AsMemory(0, 200)));
        Assert.Throws<FontFormatException>(() => FontFace.Load(good, 1));
    }

    [Fact]
    public void Mutated_fonts_never_throw_anything_but_FontFormatException()
    {
        byte[] original = TestFonts.Bytes("LiberationSans-Regular.ttf");
        Random rng = new Random(12345);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            byte[] data = (byte[])original.Clone();
            int flips = rng.Next(1, 16);
            for (int i = 0; i < flips; i++)
            {
                // Bias towards the table directory and the small tables at the front.
                int pos = rng.Next(0, iteration % 2 == 0 ? Math.Min(4096, data.Length) : data.Length);
                data[pos] = (byte)rng.Next(256);
            }
            try
            {
                FontFace f = FontFace.Load(data);
                for (int cp = 0x20; cp < 0x180; cp++)
                {
                    int g = f.GetGlyphId(cp);
                    _ = f.GetAdvance(g);
                    _ = f.GetKerning(g, f.GetGlyphId('V'));
                }
                _ = f.MaxDigitWidthPixels(11);
            }
            catch (FontFormatException)
            {
            }
        }
    }
}
