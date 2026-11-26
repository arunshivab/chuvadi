using System.Buffers.Binary;
using System.Diagnostics;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Import;
using Chuvadi.Layout.Model;
using Chuvadi.Fonts;

namespace Chuvadi.Layout.Tests;

/// <summary>
/// D-406, the 0.3.1 security patch: safe default limits, SEC-04 (a long run of spaces), SEC-11 (composite glyphs that
/// refer to themselves) and the S5 recursion and cancellation sweep. The crafted fonts are built here, in memory, from
/// Gelasio (OFL, no reserved font name); no modified font is stored in the repository.
/// </summary>
public class SecurityTests
{
    // ---------- S2a: safe defaults ----------

    [Fact]
    public void Default_limits_are_the_safe_limits()
    {
        LayoutLimits d = new LayoutLimits();
        LayoutLimits u = LayoutLimits.ForUntrustedInput();
        foreach (LayoutLimits l in new[] { d, u, new LayoutOptions().Limits })
        {
            Assert.Equal(2_000, l.MaxPages);
            Assert.Equal(50L * 1024 * 1024, l.MaxSourceBytes);
            Assert.Equal(1_000_000, l.MaxCellsPerSheet);
            Assert.Equal(200_000, l.MaxBlocks);
            Assert.Equal(8, l.MaxNestingDepth);
            Assert.Equal(16L * 1024 * 1024, l.MaxResourceBytes);
            Assert.Equal(256L * 1024 * 1024, l.MaxLayoutBytes);
            Assert.Equal(200_000, l.MaxCharactersPerBlock);
            Assert.False(l.Trusted);
        }
    }

    [Fact]
    public void Trusted_limits_are_the_0_3_0_defaults_and_survive_clone_and_edits()
    {
        LayoutLimits t = LayoutLimits.ForTrustedInput();
        Assert.Equal(10_000, t.MaxPages);
        Assert.Equal(256L * 1024 * 1024, t.MaxSourceBytes);
        Assert.Equal(5_000_000, t.MaxCellsPerSheet);
        Assert.Equal(2_000_000, t.MaxBlocks);
        Assert.Equal(32, t.MaxNestingDepth);
        Assert.Equal(64L * 1024 * 1024, t.MaxResourceBytes);
        Assert.Equal(512L * 1024 * 1024, t.MaxLayoutBytes);
        Assert.Equal(1_000_000, t.MaxCharactersPerBlock);
        Assert.True(t.Trusted);
        LayoutLimits c = t.Clone();
        c.MaxPages = 5;
        Assert.True(c.Trusted);
        Assert.False(new LayoutLimits { MaxPages = 10_000 }.Trusted);
    }

    // ---------- S3: SEC-04, a long run of spaces ----------

    [Fact]
    public void A_paragraph_of_spaces_is_measured_in_linear_time()
    {
        // 0.3.0 took n²/2 steps: 40,000 spaces 1.3 s, 900,000 spaces about 11 minutes. Now 900,000 take well under a second;
        // the budget here is generous for slow CI machines.
        LayoutOptions o = Flow.Options();
        o.Limits = LayoutLimits.ForTrustedInput();
        Stopwatch sw = Stopwatch.StartNew();
        using LaidOutDocument doc = DocumentLayout.OpenText(new string(' ', 900_000), null, o);
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Default_limits_refuse_a_900k_character_paragraph()
    {
        LimitIs("MaxCharactersPerBlock", () => DocumentLayout.OpenText(new string(' ', 900_000), null, Flow.Options()).ResolvePageCount());
    }

    [Fact]
    public void Spaces_between_complex_script_words_still_take_the_complex_script_font()
    {
        // The run cache of D-406 S3 must give the same answer as the old per-space scan: spaces between two Tamil words
        // use the Tamil font; spaces with Latin on one side do not.
        LayoutOptions o = Flow.Options();
        o.FontSources.Add(FontPackSource.For(Chuvadi.Fonts.FontPack.Instance));
        CharacterFormat cf = new CharacterFormat { FontFamily = "Calibri", FontSize = 11, ComplexScriptFontFamily = "Gelasio" };
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("அ   ஆ x   அ  y", null, cf)), o);
        string[] runs = Flow.Runs(doc.GetPage(0)).Select(r => $"[{r.Text}] {r.Font.FamilyName}").ToArray();
        // The same runs as 0.3.0 gives: only the three spaces between the two Tamil words take the complex-script font.
        Assert.Equal(new[] { "[அ] LiPi Sans Tamil", "[   ] Gelasio", "[ஆ] LiPi Sans Tamil", "[ ] Carlito", "[x   ] Carlito", "[அ] LiPi Sans Tamil", "[  ] Carlito", "[y] Carlito" }, runs);
    }

    [Fact]
    public void Measuring_one_huge_paragraph_honours_the_callers_token()
    {
        LayoutOptions o = Flow.Options();
        LayoutContext ctx = new LayoutContext(o, o.CreateFontResolver(new MemoryFontSource()));
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        ctx.Cancellation = cts.Token;
        FlowParagraph p = Flow.P(new string('a', 50_000));
        Assert.Throws<OperationCanceledException>(() => ParagraphMeasurer.Measure(p, ctx, NoFields.Instance));
    }

    [Fact]
    public void Fitting_many_lines_honours_the_measured_token()
    {
        LayoutOptions o = Flow.Options();
        LayoutContext ctx = new LayoutContext(o, o.CreateFontResolver(new MemoryFontSource()));
        FlowParagraph p = Flow.P(string.Join(" ", Enumerable.Repeat("word", 2_000)));
        Assert.True(LineFitter.Fit(ParagraphMeasurer.Measure(p, ctx, NoFields.Instance), 20, 36).Count > 64);
        using CancellationTokenSource cts = new CancellationTokenSource();
        ctx.Cancellation = cts.Token;
        MeasuredParagraph m = ParagraphMeasurer.Measure(p, ctx, NoFields.Instance);
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => LineFitter.Fit(m, 20, 36));
    }

    [Fact]
    public void A_cancelled_page_call_stops_and_the_document_still_works()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText(string.Join("\n", Enumerable.Repeat("line", 100)), null, Flow.Options());
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => doc.GetPage(0, cts.Token));
        Assert.True(doc.ResolvePageCount() >= 1);
        Assert.NotNull(doc.GetPage(0));
    }

    // ---------- S4: SEC-11, composite glyphs ----------

    [Fact]
    public void A_glyph_made_of_copies_of_itself_is_cut_quickly_and_drawn_empty()
    {
        foreach (int copies in new[] { 12, 30 })
        {
            (byte[] font, int gid) = CompositeFont(self: true, copies);
            Stopwatch sw = Stopwatch.StartNew();
            using LaidOutDocument doc = DocumentLayout.OpenText("AAAA", new TextImportOptions { FontFamily = "Gelasio" }, Options(font));
            string svg = LayoutSvg.ToSvg(doc.GetPage(0));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"{copies} copies took {sw.Elapsed}");
            FontFace face = FontFace.Load(font, 0, FontOrigin.Supplied, "test");
            GlyphOutlines outlines = GlyphOutlines.For(face);
            Assert.Equal(string.Empty, outlines.PathData(gid));
            Assert.True(outlines.IsInvalid(gid));
            Assert.Contains("<svg", svg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Too_many_components_make_a_glyph_invalid_and_a_few_do_not()
    {
        (byte[] many, int gid) = CompositeFont(self: false, GlyphOutlines.MaxComponents + 1);
        GlyphOutlines o1 = GlyphOutlines.For(FontFace.Load(many, 0, FontOrigin.Supplied, "many"));
        Assert.Equal(string.Empty, o1.PathData(gid));
        Assert.True(o1.IsInvalid(gid));

        (byte[] few, int gid2) = CompositeFont(self: false, 3);
        GlyphOutlines o2 = GlyphOutlines.For(FontFace.Load(few, 0, FontOrigin.Supplied, "few"));
        Assert.NotEqual(string.Empty, o2.PathData(gid2));
        Assert.False(o2.IsInvalid(gid2));
    }

    [Fact]
    public void Walking_a_composite_glyph_honours_the_token_and_caches_nothing()
    {
        (byte[] font, int gid) = CompositeFont(self: false, 3);
        GlyphOutlines o = GlyphOutlines.For(FontFace.Load(font, 0, FontOrigin.Supplied, "cancel"));
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => o.PathData(gid, cts.Token));
        Assert.NotEqual(string.Empty, o.PathData(gid));
    }

    [Fact]
    public void A_watermark_in_such_a_font_warns_and_is_not_drawn()
    {
        (byte[] font, _) = CompositeFont(self: true, 12);
        FlowDocument d = Flow.Doc(Flow.P("x"));
        d.Sections[0].Watermark = new FlowWatermark("AAAA") { FontFamily = "Gelasio" };
        Stopwatch sw = Stopwatch.StartNew();
        using LaidOutDocument doc = DocumentLayout.Open(d, Options(font));
        doc.GetPage(0);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
        Assert.Contains(doc.Warnings, w => w.Code == LayoutWarningCodes.FontGlyphOutlineInvalid);
    }

    [Fact]
    public void Real_fonts_stay_within_the_composite_limits()
    {
        // Every bundled TrueType face: no glyph is invalid (the limits change no output for real fonts).
        foreach (FontFileInfo f in SubstituteFontPack.Files.Where(f => f.FileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            FontFace face = FontFace.Load(SubstituteFontPack.ReadAllBytes(f.FileName), 0, FontOrigin.Bundled, f.FileName);
            GlyphOutlines o = GlyphOutlines.For(face);
            if (!o.HasOutlines)
            {
                continue;
            }
            for (int g = 0; g < face.GlyphCount; g++)
            {
                o.PathData(g);
                Assert.False(o.IsInvalid(g), $"{f.FileName} glyph {g}");
            }
        }
    }

    // ---------- S5: recursion on input ----------

    [Fact]
    public void Tables_nested_far_beyond_the_limit_are_refused_not_a_stack_overflow()
    {
        // 0.3.0 counted blocks recursively and overflowed the stack at about 25,000 levels, before the nesting limit.
        foreach (LayoutLimits limits in new[] { new LayoutLimits(), LayoutLimits.ForTrustedInput() })
        {
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            IList<FlowBlock> into = s.Blocks;
            for (int i = 0; i < 100_000; i++)
            {
                FlowTable t = new FlowTable();
                FlowTableRow r = new FlowTableRow();
                FlowTableCell c = new FlowTableCell();
                t.Rows.Add(r);
                r.Cells.Add(c);
                into.Add(t);
                into = c.Blocks;
            }
            into.Add(Flow.P("x"));
            LayoutOptions o = Flow.Options();
            o.Limits = limits;
            Assert.Equal(200_001, d.CountBlocks());
            Assert.Throws<LayoutLimitExceededException>(() => DocumentLayout.Open(d, o).GetPage(0));
        }
    }

    // ---------- helpers ----------

    private sealed class NoFields : IFieldValues
    {
        public static readonly NoFields Instance = new NoFields();

        public string Evaluate(FieldInline field) => string.Empty;
    }

    private static void LimitIs(string name, Action a)
    {
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(a);
        Assert.Equal(name, ex.LimitName);
    }

    private static LayoutOptions Options(byte[] font)
    {
        LayoutOptions o = new LayoutOptions { UseBuiltInFonts = false };
        MemoryFontSource src = new MemoryFontSource();
        src.Add(font);
        o.FontSources.Add(src);
        return o;
    }

    /// <summary>
    /// Gelasio Regular with its glyph "A" replaced by a composite of <paramref name="copies"/> components: of "A"
    /// itself when <paramref name="self"/>, otherwise of "B" (a real outline). Returns the font and the glyph id of "A".
    /// The glyph and location tables are rebuilt in long format; nothing else changes.
    /// </summary>
    internal static (byte[] Font, int Glyph) CompositeFont(bool self, int copies)
    {
        byte[] src = SubstituteFontPack.ReadAllBytes("Gelasio-Regular.ttf");
        FontFace face = FontFace.Load(src, 0, FontOrigin.Bundled, "Gelasio-Regular.ttf");
        int a = face.GetGlyphId('A');
        int b = face.GetGlyphId('B');
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(src.AsSpan(4));
        var tables = new List<(string Tag, byte[] Data)>();
        for (int t = 0; t < numTables; t++)
        {
            int rec = 12 + (16 * t);
            string tag = System.Text.Encoding.ASCII.GetString(src, rec, 4);
            int off = (int)BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(rec + 8));
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(src.AsSpan(rec + 12));
            tables.Add((tag, src.AsSpan(off, len).ToArray()));
        }
        byte[] head = tables.First(t => t.Tag == "head").Data;
        byte[] glyf = tables.First(t => t.Tag == "glyf").Data;
        byte[] loca = tables.First(t => t.Tag == "loca").Data;
        int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(tables.First(t => t.Tag == "maxp").Data.AsSpan(4));
        bool longLoca = BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50)) != 0;
        long Loc(int i) => longLoca ? BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * i)) : BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(2 * i)) * 2L;

        byte[] composite = new byte[10 + (6 * copies)];
        BinaryPrimitives.WriteInt16BigEndian(composite.AsSpan(0), -1);
        BinaryPrimitives.WriteInt16BigEndian(composite.AsSpan(6), 1000);
        BinaryPrimitives.WriteInt16BigEndian(composite.AsSpan(8), 1000);
        for (int i = 0; i < copies; i++)
        {
            int p = 10 + (6 * i);
            ushort flags = (ushort)(0x0002 | (i + 1 < copies ? 0x0020 : 0)); // ARGS_ARE_XY_VALUES, MORE_COMPONENTS
            BinaryPrimitives.WriteUInt16BigEndian(composite.AsSpan(p), flags);
            BinaryPrimitives.WriteUInt16BigEndian(composite.AsSpan(p + 2), (ushort)(self ? a : b));
        }

        List<byte> newGlyf = new List<byte>();
        byte[] newLoca = new byte[4 * (numGlyphs + 1)];
        for (int g = 0; g < numGlyphs; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * g), (uint)newGlyf.Count);
            newGlyf.AddRange(g == a ? composite : glyf.AsSpan((int)Loc(g), (int)(Loc(g + 1) - Loc(g))).ToArray());
            while (newGlyf.Count % 4 != 0)
            {
                newGlyf.Add(0);
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(4 * numGlyphs), (uint)newGlyf.Count);
        byte[] newHead = (byte[])head.Clone();
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(50), 1);

        for (int t = 0; t < tables.Count; t++)
        {
            tables[t] = tables[t].Tag switch
            {
                "glyf" => ("glyf", newGlyf.ToArray()),
                "loca" => ("loca", newLoca),
                "head" => ("head", newHead),
                _ => tables[t],
            };
        }
        List<byte> output = new List<byte>(src.AsSpan(0, 12 + (16 * numTables)).ToArray());
        byte[] dir = output.ToArray();
        for (int t = 0; t < tables.Count; t++)
        {
            int rec = 12 + (16 * t);
            BinaryPrimitives.WriteUInt32BigEndian(dir.AsSpan(rec + 8), (uint)output.Count);
            BinaryPrimitives.WriteUInt32BigEndian(dir.AsSpan(rec + 12), (uint)tables[t].Data.Length);
            output.AddRange(tables[t].Data);
            while (output.Count % 4 != 0)
            {
                output.Add(0);
            }
        }
        byte[] result = output.ToArray();
        dir.AsSpan().CopyTo(result);
        return (result, a);
    }
}
