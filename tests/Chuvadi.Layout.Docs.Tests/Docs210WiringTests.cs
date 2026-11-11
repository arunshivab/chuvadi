using System.IO.Compression;
using Chuvadi.Docs.Model;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Tests;
using Xunit;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>
/// The Chuvadi.Docs 2.1.0 wiring of Layout 0.2.0 (D-317): skipped parts as warnings (W1), noColumnBalance (W2), theme
/// lookups through Docs (W3), the element cap from MaxBlocks (W4) and Docs' untrusted preset (W5).
/// </summary>
public sealed class Docs210WiringTests
{
    private static string Fixture(string folder, string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, file);

    /// <summary>A copy of a .docx with some parts replaced (null removes the part, keeping the relationship to it).</summary>
    private static byte[] Rewrite(string path, Dictionary<string, string?> parts)
    {
        MemoryStream ms = new MemoryStream();
        using (ZipArchive src = ZipFile.OpenRead(path))
        using (ZipArchive dst = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry e in src.Entries)
            {
                string? replacement = null;
                if (parts.TryGetValue(e.FullName, out replacement) && replacement is null)
                {
                    continue;
                }
                ZipArchiveEntry n = dst.CreateEntry(e.FullName);
                using Stream w = n.Open();
                if (replacement is not null)
                {
                    byte[] b = System.Text.Encoding.UTF8.GetBytes(replacement);
                    w.Write(b, 0, b.Length);
                }
                else
                {
                    using Stream r = e.Open();
                    r.CopyTo(w);
                }
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void W1_a_broken_header_and_missing_styles_are_reported_and_the_document_still_lays_out()
    {
        byte[] file = Rewrite(Fixture("docx", "ThreeColHeadFoot.docx"), new Dictionary<string, string?>
        {
            ["word/header1.xml"] = "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p>",
            ["word/styles.xml"] = null,
        });
        using LaidOutDocument doc = DocsLayout.Open(file, null, DocsImportTests.Options());
        Assert.True(doc.ResolvePageCount() >= 1);
        List<LayoutWarning> warnings = doc.Warnings.ToList();

        LayoutWarning header = Assert.Single(warnings, w => w.Code == LayoutWarningCodes.DocxPartSkipped && w.Location == "/word/header1.xml");
        Assert.Equal(LayoutWarningSeverity.ContentLoss, header.Severity);
        Assert.Contains("NotWellFormed", header.Message, StringComparison.Ordinal);
        LayoutWarning styles = Assert.Single(warnings, w => w.Code == LayoutWarningCodes.DocxPartSkipped && w.Location == "/word/styles.xml");
        Assert.Equal(LayoutWarningSeverity.Fidelity, styles.Severity);
        Assert.Contains("Missing", styles.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void W1_a_sound_document_has_no_skipped_part_warnings()
    {
        using FileStream fs = File.OpenRead(Fixture("docx", "ThreeColHeadFoot.docx"));
        using LaidOutDocument doc = DocsLayout.Open(fs, null, DocsImportTests.Options());
        doc.ResolvePageCount();
        Assert.DoesNotContain(doc.Warnings, w => w.Code == LayoutWarningCodes.DocxPartSkipped);
    }

    private static int LinesInFirstColumns(Document d)
    {
        using LaidOutDocument doc = DocsLayout.Open(d, null, DocsImportTests.Options());
        int n = doc.ResolvePageCount(), lines = 0;
        for (int i = 0; i < n; i++)
        {
            List<LayoutBox> page = Flow.Lines(doc.GetPage(i)).ToList();
            double left = page.Min(b => b.Rect.X);
            lines += page.Count(b => b.Rect.X < left + 1);
        }
        return lines;
    }

    [Fact]
    public void W2_noColumnBalance_turns_balancing_off_in_every_section()
    {
        Document balanced, unbalanced;
        using (FileStream fs = File.OpenRead(Fixture("columns", "calib-columns.docx")))
        {
            balanced = Document.Load(fs);
        }
        using (FileStream fs = File.OpenRead(Fixture("columns", "calib-columns.docx")))
        {
            unbalanced = Document.Load(fs);
        }
        Assert.False(balanced.Compatibility.NoColumnBalance);
        unbalanced.Compatibility.NoColumnBalance = true;

        Assert.All(DocsImporter.Import(balanced).Sections, s => Assert.True(s.BalanceColumns));
        Assert.All(DocsImporter.Import(unbalanced).Sections, s => Assert.False(s.BalanceColumns));

        // Not balanced, the first column of a section before a continuous break fills before the next one starts.
        Assert.True(LinesInFirstColumns(unbalanced) > LinesInFirstColumns(balanced));
    }

    [Fact]
    public void W3_theme_fonts_come_from_the_theme_scheme_without_the_per_language_lookup()
    {
        Document doc = new Document();
        doc.ThemeFontLanguages.ComplexScript = "ta-IN";
        doc.AddParagraph().Text("x", new TextFormat { AsciiThemeFont = ThemeFont.MinorHAnsi, ComplexScriptThemeFont = ThemeFont.MinorBidi });
        FlowDocument flow = DocsImportTests.Import(doc);
        CharacterFormat f = flow.Sections[0].Blocks.OfType<FlowParagraph>().First().Inlines.OfType<TextRun>().Single().Format;
        Assert.Equal("Calibri", f.FontFamily);
        // No theme part: the scheme's cs font is empty, so the run has no theme cs font (D-191 R1), whatever themeFontLang says.
        Assert.Null(doc.Theme.MinorFonts.ComplexScript);
    }

    [Fact]
    public void W3_theme_colours_follow_the_documents_colour_scheme_mapping()
    {
        Document doc = new Document();
        doc.ColorSchemeMapping.Text1 = ColorSchemeIndex.Accent1;
        doc.AddParagraph().Text("mapped", new TextFormat { ThemeColor = ThemeColor.Text1 });
        doc.AddParagraph().Text("tinted", new TextFormat { ThemeColor = ThemeColor.Accent2, ThemeColorTint = 0x99 });
        List<TextRun> runs = DocsImportTests.Import(doc).Sections[0].Blocks.OfType<FlowParagraph>().SelectMany(p => p.Inlines.OfType<TextRun>()).ToList();
        Assert.Equal(LayoutColor.FromHex(doc.GetThemeColorHex(ThemeColor.Text1).TrimStart('#')), runs[0].Format.Color);
        Assert.Equal(LayoutColor.FromHex("4472C4"), runs[0].Format.Color); // accent1 of the default theme, not dk1 (black)
        Assert.Equal(LayoutColor.FromHex(doc.GetThemeColorHex(ThemeColor.Accent2, 0x99, null).TrimStart('#')), runs[1].Format.Color);
    }

    [Fact]
    public void W4_the_element_cap_is_five_per_block()
    {
        Assert.Equal(10_000_000, DocsImporter.ReaderOptions(new LayoutLimits(), true).MaxElements);
        Assert.Equal(1_000_000, DocsImporter.ReaderOptions(LayoutLimits.ForUntrustedInput(), true).MaxElements);
        Assert.Equal(int.MaxValue, DocsImporter.ElementsFor(long.MaxValue / 2));

        // A document of many empty paragraphs is refused by the element cap while it is read.
        Document many = new Document();
        for (int i = 0; i < 400; i++)
        {
            many.AddParagraph();
        }
        MemoryStream ms = new MemoryStream();
        many.SaveTo(ms);
        LayoutOptions lo = DocsImportTests.Options();
        lo.Limits.MaxBlocks = 100;
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => DocsLayout.Open(ms.ToArray(), null, lo));
        Assert.Equal("DocxElements", ex.LimitName);
        Assert.Equal(500, ex.Limit);
    }

    [Fact]
    public void W5_untrusted_limits_start_from_the_Docs_preset_and_keep_it_when_cloned_or_changed()
    {
        DocxReaderOptions plain = DocsImporter.ReaderOptions(new LayoutLimits(), true);
        Assert.Equal(DocxReaderOptions.DefaultMaxCompressionRatio, plain.MaxCompressionRatio);

        LayoutLimits untrusted = LayoutLimits.ForUntrustedInput();
        untrusted.MaxPages = 50;
        DocxReaderOptions r = DocsImporter.ReaderOptions(untrusted.Clone(), true);
        DocxReaderOptions docs = DocxReaderOptions.ForUntrustedInput();
        Assert.Equal(docs.MaxCompressionRatio, r.MaxCompressionRatio); // 200:1
        Assert.Equal(docs.MaxEntries, r.MaxEntries);
        Assert.Equal(docs.MaxSpinCount, r.MaxSpinCount);
        Assert.Equal(docs.MaxElements, r.MaxElements);
        Assert.Equal(docs.MaxXmlDepth, r.MaxXmlDepth);
        Assert.Equal(untrusted.MaxSourceBytes, r.MaxPackageBytes); // Layout's own limits on top
        Assert.Equal(untrusted.MaxSourceBytes, r.MaxPartBytes);
        Assert.Equal(untrusted.MaxLayoutBytes, r.MaxTotalUncompressedBytes);
    }
}
