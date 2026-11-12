using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.2.0, D-336/D-340: <see cref="DocxReader.FormattedParagraphs()"/> streams paragraphs with the effective run
/// and paragraph formatting. The main proof is equivalence with <see cref="Document.Load(Stream, string?)"/> plus
/// <see cref="Document.GetEffectiveRunFormat(Paragraph, Run, TableCellRef)"/>: the same paragraphs in the same
/// order, the same runs and the same effective formats, on a hand-built document that exercises every resolution
/// level and on every .docx the earlier groups of this run wrote.
/// </summary>
public static class FormattedParagraphsTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Run(string outDir)
    {
        var rich = Rich();
        File.WriteAllBytes(Path.Combine(outDir, "formatted-paragraphs-rich.docx"), rich);

        Check("the hand-built document: the same paragraphs, runs and effective formats as Load + GetEffectiveRunFormat", () =>
        {
            int n = Compare(rich, "rich");
            AssertTrue(n == 18, "body paragraphs compared: " + n);
        });

        Check("styles resolve: paragraph style bold, character style italic (toggle combined), direct size, table style first row and banding, list indent", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich));
            var items = r.FormattedParagraphs().ToList();
            var heading = items.Single(i => i.Text == "Heading text");
            AssertTrue(heading.RunFormats[0].Bold && heading.RunFormats[0].SizePt == 16, Describe(heading.RunFormats[0]));
            var both = items.Single(i => i.Text == "Bold style, italic char");
            AssertTrue(both.RunFormats[1].Italic && both.RunFormats[1].Bold, "italic char style over bold para style");
            var toggled = items.Single(i => i.Text.StartsWith("Toggle", StringComparison.Ordinal));
            AssertTrue(!toggled.RunFormats[1].Bold, "bold para style XOR bold char style = not bold");
            var header = items.Single(i => i.Text == "H1");
            AssertTrue(header.Cell is { Row: 0, Column: 0 } && header.RunFormats[0].Bold && header.RunFormats[0].ColorHex == "C00000", "table style first row: " + Describe(header.RunFormats[0]));
            var body = items.Single(i => i.Text == "B1");
            AssertTrue(body.Cell is { Row: 1 } && !body.RunFormats[0].Bold, "body row not bold");
            var nested = items.Single(i => i.Text == "nested cell");
            AssertTrue(nested.Cell is { } nc && nc.Table.Rows.Count == 1 && nested.Cell.Value.Table != header.Cell!.Value.Table, "nested table cell");
            var listed = items.Single(i => i.Text == "List item");
            AssertTrue(listed.ParagraphFormat.IndentLeftPt == 36, "list level indent: " + listed.ParagraphFormat.IndentLeftPt);
            var link = items.Single(i => i.Text == "a link");
            AssertTrue(link.Paragraph.Runs[0].HyperlinkUrl == "https://example.com/", "hyperlink kept");
        });

        Check("order: body (table cells in place), then headers, then footers; Part names the part", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich));
            var items = r.FormattedParagraphs().ToList();
            var texts = items.Select(i => i.Text).ToList();
            AssertTrue(texts.IndexOf("Before table") < texts.IndexOf("H1") && texts.IndexOf("nested cell") < texts.IndexOf("After table"), string.Join("|", texts));
            AssertTrue(items[^2].Text == "Header text" && items[^2].Part == "/word/header1.xml" && items[^1].Text == "Footer text" && items[^1].Part == "/word/footer1.xml", items[^2].Part + " " + items[^1].Part);
            AssertTrue(items[^2].RunFormats[0].Italic, "header style resolved");
            using var noHf = DocxReader.Open(new MemoryStream(rich), null, new DocxReaderOptions { IncludeHeadersAndFooters = false });
            AssertTrue(noHf.FormattedParagraphs().All(i => i.Part == "/word/document.xml"), "no headers or footers when switched off");
        });

        Check("bookmarks after the last paragraph go to it before it is yielded (items are final when seen)", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich));
            var last = r.FormattedParagraphs().Last(i => i.Part == "/word/document.xml");
            AssertTrue(last.RunFormats.Count == last.Paragraph.Runs.Count, "one format per run");
            AssertTrue(last.Paragraph.Runs.Any(x => x.Bookmark is not null), "the trailing bookmark marks are on the last paragraph");
        });

        Check("theme helpers: GetThemeFontName and GetThemeColorHex match Document's", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich));
            var d = Document.Load(new MemoryStream(rich));
            foreach (ThemeFont f in Enum.GetValues<ThemeFont>())
                AssertTrue(r.GetThemeFontName(f) == d.GetThemeFontName(f), f + ": " + r.GetThemeFontName(f));
            foreach (ThemeColor c in Enum.GetValues<ThemeColor>())
                AssertTrue(r.GetThemeColorHex(c, 153, null) == d.GetThemeColorHex(c, 153, null), c.ToString());
            AssertTrue(r.GetThemeFontName(ThemeFont.MinorHAnsi) == "Calibri", "the theme's minor font");
            AssertThrows<ArgumentOutOfRangeException>(() => r.GetThemeFontName((ThemeFont)999), "undefined");
        });

        Check("missing or broken styles, numbering or settings are listed in SkippedParts and the defaults are used", () =>
        {
            var bad = ErrorContractMatrix.Replace(rich, "word/styles.xml", $"<w:styles xmlns:w=\"{W}\"><w:style>");
            bad = ErrorContractMatrix.Remove(bad, "word/numbering.xml");
            bad = ErrorContractMatrix.Replace(bad, "word/settings.xml", $"<w:settings xmlns:w=\"{W}\"><w:compat>");
            using var r = DocxReader.Open(new MemoryStream(bad));
            var items = r.FormattedParagraphs().ToList();
            AssertTrue(items.Any(i => i.Text == "Heading text" && !i.RunFormats[0].Bold), "no styles: defaults");
            var got = string.Join(",", r.SkippedParts.Select(s => s.PartName + ":" + s.Reason));
            AssertTrue(got == "/word/numbering.xml:Missing,/word/styles.xml:NotWellFormed,/word/settings.xml:NotWellFormed", got);
        });

        Check("limits, cancellation and the error contract: MaxElements per enumeration, the Open token, a broken body", () =>
        {
            int elements = CountElements(rich);
            var limited = new DocxReaderOptions { MaxElements = 40 };
            using (var r = DocxReader.Open(new MemoryStream(rich), null, limited))
                AssertThrows<DocxSecurityLimitException>(() => r.FormattedParagraphs().ToList(), "40 elements");
            using (var r = DocxReader.Open(new MemoryStream(rich), null, new DocxReaderOptions { MaxElements = elements }))
            {
                AssertTrue(r.FormattedParagraphs().Count() > 0, "within the limit (styles counted in the first call)");
                AssertTrue(r.FormattedParagraphs().Count() > 0, "a second call starts from zero");
            }
            using (var cts = new CancellationTokenSource())
            using (var r = DocxReader.Open(new MemoryStream(rich), null, null, cts.Token))
            {
                cts.Cancel();
                AssertThrows<OperationCanceledException>(() => r.FormattedParagraphs().ToList(), "Open token");
            }
            using (var cts = new CancellationTokenSource())
            using (var r = DocxReader.Open(new MemoryStream(rich)))
            {
                var e = r.FormattedParagraphs(cts.Token).GetEnumerator();
                AssertTrue(e.MoveNext(), "first");
                cts.Cancel();
                AssertThrows<OperationCanceledException>(() => e.MoveNext(), "own token, mid-stream");
            }
            var broken = ErrorContractMatrix.Replace(rich, "word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>x</w:t></w:r></w:p><w:p>");
            using (var r = DocxReader.Open(new MemoryStream(broken)))
                AssertThrows<DocxFormatException>(() => r.FormattedParagraphs().ToList(), "broken body");
            var r2 = DocxReader.Open(new MemoryStream(rich));
            r2.Dispose();
            AssertThrows<ObjectDisposedException>(() => r2.FormattedParagraphs(), "disposed");
        });

        Check("every .docx written by the earlier groups: the same paragraphs, runs and effective formats as Load", () =>
        {
            int files = 0, paragraphs = 0;
            bool alone = Directory.EnumerateFiles(outDir, "*.docx", SearchOption.AllDirectories).Count() < 20;
            if (alone)
            {
                // Run on its own (the CI fact): make a corpus with the main writing groups first.
                var corpus = Path.Combine(outDir, "corpus");
                Directory.CreateDirectory(corpus);
                MinimalDocxTest.Run(corpus);
                FormattingTest.Run(corpus);
                TablesTest.Run(corpus);
                HeaderFooterPageTest.Run(corpus);
                ReaderRoundTripTest.Run(corpus);
                ImageTests.Run(corpus);
            }
            foreach (var path in Directory.EnumerateFiles(outDir, "*.docx", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(path);
                try { Document.Load(new MemoryStream(bytes)); }
                catch (Exception e) when (e is DocxFormatException or DocxPasswordRequiredException) { continue; }
                paragraphs += Compare(bytes, Path.GetFileName(path));
                files++;
            }
            AssertTrue(alone ? files >= 10 && paragraphs >= 50 : files >= 20 && paragraphs >= 200, $"{files} files, {paragraphs} paragraphs");
            Console.WriteLine($"        ({files} files, {paragraphs} paragraphs compared)");
        });

        Check("memory: one paragraph or one top-level table at a time (a 2,000-paragraph document)", () =>
        {
            var big = Big(2000);
            long before = GC.GetTotalMemory(true);
            using var r = DocxReader.Open(new MemoryStream(big));
            long peak = 0;
            int n = 0;
            foreach (var item in r.FormattedParagraphs())
            {
                if (++n % 250 == 0) peak = Math.Max(peak, GC.GetTotalMemory(true) - before);
            }
            AssertTrue(n == 2000, "paragraphs: " + n);
            long loaded = Measure(() => Document.Load(new MemoryStream(big)));
            AssertTrue(peak < loaded / 2, $"streaming held {peak / 1024:N0} KB against Load's {loaded / 1024:N0} KB");
        });
    }

    // ---- equivalence ------------------------------------------------------------------------

    /// <summary>Compares the reader's body items with Load's body; returns the number of paragraphs compared.</summary>
    private static int Compare(byte[] package, string name)
    {
        var doc = Document.Load(new MemoryStream(package));
        var expected = new List<(Paragraph P, TableCellRef? Cell)>();
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p) expected.Add((p, null));
            else if (b is DocTable t) Collect(t, expected);
        }
        using var reader = DocxReader.Open(new MemoryStream(package));
        string main = null!;
        var got = new List<DocxFormattedParagraph>();
        foreach (var item in reader.FormattedParagraphs())
        {
            main ??= item.Part;
            if (item.Part == main) got.Add(item);
        }
        if (got.Count != expected.Count) throw new InvalidOperationException($"{name}: {got.Count} paragraphs against Load's {expected.Count}");
        for (int i = 0; i < got.Count; i++)
        {
            var (p, cell) = expected[i];
            var item = got[i];
            string where = $"{name}, paragraph {i} \"{Short(p.GetText())}\"";
            if ((cell is null) != (item.Cell is null) || (cell is { } c && (c.Row != item.Cell!.Value.Row || c.Column != item.Cell.Value.Column)))
                throw new InvalidOperationException(where + ": cell " + cell + " against " + item.Cell);
            var runs = p.Runs.Where(x => x.NoteReference is null && x.CommentReference is null && x.CommentRange is null).ToList();
            if (runs.Count != item.Paragraph.Runs.Count)
                throw new InvalidOperationException($"{where}: {item.Paragraph.Runs.Count} runs against Load's {runs.Count}");
            for (int k = 0; k < runs.Count; k++)
            {
                var want = cell is { } cr ? doc.GetEffectiveRunFormat(p, runs[k], cr) : doc.GetEffectiveRunFormat(p, runs[k]);
                if (runs[k].TextContent != item.Paragraph.Runs[k].TextContent)
                    throw new InvalidOperationException($"{where}, run {k}: text \"{item.Paragraph.Runs[k].TextContent}\" against \"{runs[k].TextContent}\"");
                string a = Describe(want), b2 = Describe(item.RunFormats[k]);
                if (a != b2) throw new InvalidOperationException($"{where}, run {k}: format\n  reader: {b2}\n  Load:   {a}");
                if (Describe(runs[k].Format) != Describe(item.Paragraph.Runs[k].Format))
                    throw new InvalidOperationException($"{where}, run {k}: direct format differs");
            }
            var wantP = cell is { } cp ? doc.GetEffectiveParagraphFormat(p, cp) : doc.GetEffectiveParagraphFormat(p);
            if (Describe(wantP) != Describe(item.ParagraphFormat))
                throw new InvalidOperationException($"{where}: paragraph format\n  reader: {Describe(item.ParagraphFormat)}\n  Load:   {Describe(wantP)}");
        }
        return got.Count;
    }

    private static void Collect(DocTable t, List<(Paragraph, TableCellRef?)> into)
    {
        for (int r = 0; r < t.Rows.Count; r++)
        {
            var row = t.Rows[r];
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                foreach (var b in cell.Blocks)
                {
                    if (b is Paragraph p) into.Add((p, new TableCellRef(t, r, c)));
                    else if (b is DocTable nested) Collect(nested, into);
                }
            }
        }
    }

    private static string Short(string s) => s.Length <= 30 ? s : s[..30] + "…";

    /// <summary>Every public readable property, recursively (formats are plain data classes).</summary>
    internal static string Describe(object? o)
    {
        var sb = new StringBuilder();
        Describe(o, sb, 0);
        return sb.ToString();
    }

    private static void Describe(object? o, StringBuilder sb, int depth)
    {
        if (o is null) { sb.Append("null"); return; }
        var t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is string or decimal or DateTime or Guid) { sb.Append(o); return; }
        if (depth > 6) { sb.Append('…'); return; }
        if (o is IEnumerable e)
        {
            sb.Append('[');
            foreach (var x in e) { Describe(x, sb, depth + 1); sb.Append(','); }
            sb.Append(']');
            return;
        }
        sb.Append('{');
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            sb.Append(p.Name).Append('=');
            Describe(p.GetValue(o), sb, depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }

    private static long Measure(Func<object> make)
    {
        long before = GC.GetTotalMemory(true);
        var keep = make();
        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(keep);
        return after - before;
    }

    private static int CountElements(byte[] package)
    {
        int n = 0;
        using var z = new ZipArchive(new MemoryStream(package));
        foreach (var e in z.Entries)
        {
            if (!e.FullName.EndsWith(".xml", StringComparison.Ordinal)) continue;
            using var x = System.Xml.XmlReader.Create(e.Open());
            while (x.Read()) if (x.NodeType == System.Xml.XmlNodeType.Element) n++;
        }
        return n;
    }

    // ---- inputs ------------------------------------------------------------------------------

    private static byte[] Rich()
    {
        string body =
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Heading text</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"BoldPara\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Bold style, </w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"Emph\"/></w:rPr><w:t>italic char</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"BoldPara\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Toggle </w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"StrongChar\"/></w:rPr><w:t>off</w:t></w:r></w:p>" +
            "<w:p><w:r><w:rPr><w:sz w:val=\"30\"/><w:color w:val=\"0070C0\"/></w:rPr><w:t>Direct size</w:t></w:r><w:r><w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\" w:hAnsiTheme=\"minorHAnsi\"/></w:rPr><w:t xml:space=\"preserve\"> theme font</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"ListPara\"/></w:pPr><w:r><w:t>List item</w:t></w:r></w:p>" +
            "<w:p><w:hyperlink r:id=\"rIdLink\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr><w:t>a link</w:t></w:r></w:hyperlink></w:p>" +
            "<w:p><w:r><w:t>Before table</w:t></w:r></w:p>" +
            "<w:tbl><w:tblPr><w:tblStyle w:val=\"Grid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:p><w:r><w:t>H1</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>H2 spans</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr><w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B3a</w:t></w:r></w:p><w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>nested cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/></w:tc></w:tr>" +
            "<w:tr><w:tc><w:p><w:r><w:t>C1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>C2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>C3</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:p><w:r><w:t>After table</w:t></w:r></w:p>" +
            "<w:bookmarkStart w:id=\"7\" w:name=\"tail\"/><w:bookmarkEnd w:id=\"7\"/>" +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:footerReference w:type=\"default\" r:id=\"rIdF\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr>";
        string styles =
            $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\" w:hAnsiTheme=\"minorHAnsi\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"160\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"BoldPara\"><w:name w:val=\"Bold Para\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:b/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"HeaderStyle\"><w:name w:val=\"header\"/><w:rPr><w:i/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"ListPara\"><w:name w:val=\"List Para\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Emph\"><w:name w:val=\"Emph\"/><w:rPr><w:i/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"StrongChar\"><w:name w:val=\"Strong Char\"/><w:rPr><w:b/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"0563C1\"/><w:u w:val=\"single\"/></w:rPr></w:style>" +
            "<w:style w:type=\"table\" w:styleId=\"Grid\"><w:name w:val=\"Grid\"/><w:tblPr><w:tblStyleRowBandSize w:val=\"1\"/></w:tblPr>" +
            "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/><w:color w:val=\"C00000\"/></w:rPr></w:tblStylePr>" +
            "<w:tblStylePr w:type=\"band1Horz\"><w:rPr><w:i/></w:rPr><w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEEEEE\"/></w:tcPr></w:tblStylePr>" +
            "<w:tblStylePr w:type=\"firstCol\"><w:rPr><w:caps/></w:rPr></w:tblStylePr></w:style>" +
            "</w:styles>";
        string numbering =
            $"<w:numbering xmlns:w=\"{W}\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
        string theme =
            "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"T\"><a:themeElements>" +
            "<a:clrScheme name=\"C\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
            "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2><a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4><a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>" +
            "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
            "<a:fontScheme name=\"F\"><a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/><a:font script=\"Taml\" typeface=\"Latha\"/></a:majorFont><a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/><a:font script=\"Taml\" typeface=\"Latha\"/></a:minorFont></a:fontScheme>" +
            "<a:fmtScheme name=\"S\"><a:fillStyleLst/><a:lnStyleLst/><a:effectStyleLst/><a:bgFillStyleLst/></a:fmtScheme></a:themeElements></a:theme>";
        string settings = $"<w:settings xmlns:w=\"{W}\"><w:themeFontLang w:val=\"en-IN\" w:bidi=\"ta-IN\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";
        string header = $"<w:hdr xmlns:w=\"{W}\"><w:p><w:pPr><w:pStyle w:val=\"HeaderStyle\"/></w:pPr><w:r><w:t>Header text</w:t></w:r></w:p></w:hdr>";
        string footer = $"<w:ftr xmlns:w=\"{W}\"><w:p><w:r><w:t>Footer text</w:t></w:r></w:p></w:ftr>";
        string docRels =
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rIdS\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rIdN\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>" +
            "<Relationship Id=\"rIdT\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>" +
            "<Relationship Id=\"rIdG\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
            "<Relationship Id=\"rIdH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>" +
            "<Relationship Id=\"rIdF\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
            "<Relationship Id=\"rIdLink\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.com/\" TargetMode=\"External\"/>" +
            "</Relationships>";
        var parts = new (string, string)[]
        {
            ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>" +
                "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
                "<Override PartName=\"/word/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>" +
                "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/></Types>"),
            ("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"),
            ("word/document.xml", $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body>{body}</w:body></w:document>"),
            ("word/_rels/document.xml.rels", docRels),
            ("word/styles.xml", styles),
            ("word/numbering.xml", numbering),
            ("word/theme/theme1.xml", theme),
            ("word/settings.xml", settings),
            ("word/header1.xml", header),
            ("word/footer1.xml", footer),
        };
        return Zip(parts);
    }

    private static byte[] Big(int paragraphs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paragraphs; i++)
            sb.Append("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">Paragraph ").Append(i)
              .Append(" with some text to make it a realistic length for the measurement </w:t></w:r><w:r><w:t>and a second run.</w:t></w:r></w:p>");
        return Zip(new[]
        {
            ("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"),
            ("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"),
            ("word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body>{sb}</w:body></w:document>"),
        });
    }

    private static byte[] Zip((string Name, string Text)[] parts)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in parts)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                w.Write(text);
            }
        return ms.ToArray();
    }
}
