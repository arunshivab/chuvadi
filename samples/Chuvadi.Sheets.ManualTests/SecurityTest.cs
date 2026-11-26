using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Chuvadi.Sheets.Internal;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 2.2.1, the security patch (D-410): every item of the approved Rule 5 list and the Audit B addendum (P1 to P10),
/// and the audit's reproductions (SEC-08, SEC-10, SEC-S1, S-01, S-02, S-13, S-14, S-15, S-16, SEC-01, SEC-05).
/// All fixtures are built here, in memory, from fictional data (D-44, D-60).
/// </summary>
internal static class SecurityTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void Run(string outputDir)
    {
        var g = new TestGroup("security", "2.2.1 security patch: nesting, wildcards, budgets, safe defaults, writer and error contract");
        Nesting(g);
        Wildcards(g);
        Budget(g);
        Defaults(g);
        Writer(g);
        ErrorContract(g);
        Fuzz(g, iterations: 3000);
        g.Finish();
    }

    // ---- P1: nesting (SEC-08, SEC-S1) ----------------------------------------------------------------------------

    private static void Nesting(TestGroup g)
    {
        static string Brackets(int n) => new string('(', n) + "1" + new string(')', n);
        static string Minus(int n) => new string('-', n) + "1";
        static string Nots(int n) => string.Concat(Enumerable.Repeat("NOT(", n)) + "FALSE" + new string(')', n);

        g.Check("P1: brackets, minus signs and NOT( chains: 256 levels evaluate, 257 are refused", () =>
        {
            TestGroup.Eq(1.0, Eval(Brackets(MiniFormula.MaxNesting - 1)), "255 brackets (+ the expression level)");
            TestGroup.Eq(1.0, Eval(Minus(MiniFormula.MaxNesting - 2)), "254 minus signs (an even count)");
            TestGroup.Eq(true, Eval(Nots(MiniFormula.MaxNesting - 1)) is bool, "255 NOT( calls");
            foreach (var f in new[] { Brackets(MiniFormula.MaxNesting), Minus(MiniFormula.MaxNesting), Nots(MiniFormula.MaxNesting) })
                TestGroup.Throws<FormulaNotSupportedException>(() => MiniFormula.Parse(f), "one level more");
        });

        g.Check("P1: the deepest allowed formulas evaluate on a 256 KB thread stack (thread pool, Android)", () =>
        {
            var formulas = new[]
            {
                Brackets(MiniFormula.MaxNesting - 1), Minus(MiniFormula.MaxNesting - 2), Nots(MiniFormula.MaxNesting - 1),
                string.Concat(Enumerable.Repeat("-(NOT(", 85)) + "FALSE" + string.Concat(Enumerable.Repeat("))", 85)),
                string.Join("+", Enumerable.Repeat("1", 100_000)),            // long chains do not nest
                "1" + new string('%', 250),
            };
            Exception? failure = null;
            var results = new object?[formulas.Length];
            var t = new Thread(() =>
            {
                try { for (int i = 0; i < formulas.Length; i++) results[i] = Eval(formulas[i]); }
                catch (Exception ex) { failure = ex; }
            }, 256 * 1024);
            t.Start();
            t.Join();
            if (failure is not null) throw new Exception("failed on the small stack: " + failure.GetType().Name + ": " + failure.Message);
            TestGroup.Eq(1.0, results[0], "brackets");
            TestGroup.Eq(1.0, results[1], "minus");
            TestGroup.Eq(100_000.0, results[4], "100,000-term sum");
        });

        g.Check("SEC-08: 100,000-deep brackets, minus signs and NOT( in a conditional format come back as \"not evaluated\"", () =>
        {
            foreach (var f in new[] { Brackets(100_000), Minus(100_000), Nots(100_000) })
            {
                var res = Rule(f, new ConditionalFormatOptions());
                TestGroup.True(res is { NotEvaluated.Count: 1 }, "not evaluated");
            }
            // From a file, through Workbook.Load and Sheet.EvaluateConditionalFormats (the audit's path).
            var bytes = Package(Sheet("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
                $"<conditionalFormatting sqref=\"A1\"><cfRule type=\"expression\" priority=\"1\" dxfId=\"0\"><formula>{Brackets(10_000)}</formula></cfRule></conditionalFormatting>"));
            var wb = Workbook.Load(new MemoryStream(bytes), null, null);
            var r = wb.Sheets[0].EvaluateConditionalFormats().Evaluate(1, 1);
            TestGroup.True(r is { NotEvaluated.Count: 1 }, "file: not evaluated");
        });

        g.Check("SEC-S1: shape groups 256 deep load; 257 and 20,000 deep are XlsxFormatException, never a crash", () =>
        {
            var ok = Workbook.Load(new MemoryStream(GroupsPackage(DrawingReader.MaxGroupNesting)), null, null);
            int depth = 0;
            for (var o = ok.Sheets[0].Drawings[0]; ; o = o.Children[0]) { depth++; if (o.Children.Count == 0) break; }
            TestGroup.Eq(DrawingReader.MaxGroupNesting + 1, depth, "256 groups and the shape inside");
            foreach (var n in new[] { DrawingReader.MaxGroupNesting + 1, 20_000 })
            {
                var ex = TestGroup.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(GroupsPackage(n)), null, null), $"{n} groups");
                // 257 groups: the group limit; 20,000: the XML depth limit (1,000) stops the part first.
                TestGroup.True(ex.Message.Contains(n == 20_000 ? "nested deeper than 1,000" : "nested deeper than 256"), "names the limit: " + ex.Message);
                using var rd = XlsxReader.Open(new MemoryStream(GroupsPackage(n)));
                TestGroup.Throws<XlsxFormatException>(() => rd.Sheet("S").ReadDrawings(), "streaming ReadDrawings");
            }
        });

        g.Check("P1: 100,000-deep XML in every part type loads or is XlsxFormatException within seconds, never a crash", () =>
        {
            string Deep(string open, string close, int n = 100_000) => string.Concat(Enumerable.Repeat(open, n)) + string.Concat(Enumerable.Repeat(close, n));
            var cases = new (string What, Dictionary<string, string> Entries)[]
            {
                ("sheet", BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>") .Replace("</worksheet>", "<extLst><ext uri=\"x\">" + Deep("<q>", "</q>") + "</ext></extLst></worksheet>"))),
                ("sheet cell", BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\"><is>" + Deep("<r>", "</r>") + "</is></c></row>"))),
                ("styles", With(BaseEntries(Sheet("")), "xl/styles.xml", $"<styleSheet xmlns=\"{Main}\">" + Deep("<extLst>", "</extLst>") + "</styleSheet>", "styles", "styles.xml")),
                ("shared strings", With(BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>")), "xl/sharedStrings.xml",
                    $"<sst xmlns=\"{Main}\"><si><r><t>a</t></r>" + Deep("<r>", "</r>") + "</si></sst>", "sharedStrings", "sharedStrings.xml")),
                ("drawing", DrawingEntries($"<xdr:wsDr xmlns:xdr=\"{Xdr}\" xmlns:a=\"{A}\"><xdr:twoCellAnchor>{Anchor()}<xdr:sp>" + Deep("<xdr:txBody>", "</xdr:txBody>") + "</xdr:sp><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>")),
                ("theme", With(BaseEntries(Sheet("")), "xl/theme/theme1.xml", "<a:theme xmlns:a=\"" + A + "\" name=\"T\">" + Deep("<a:extLst>", "</a:extLst>") + "</a:theme>", "theme", "theme/theme1.xml")),
                ("chart", ChartEntries("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart>" + Deep("<c:extLst>", "</c:extLst>") + "</c:chart></c:chartSpace>")),
                ("comments", CommentEntries($"<comments xmlns=\"{Main}\"><authors><author>Ana</author></authors><commentList><comment ref=\"A1\" authorId=\"0\"><text>" + Deep("<r>", "</r>") + "</text></comment></commentList></comments>",
                    "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\">" + Deep("<v:shape>", "</v:shape>", 20_000) + "</xml>")),
            };
            foreach (var (what, entries) in cases)
            {
                var timer = Stopwatch.StartNew();
                var bytes = Zip(entries);
                try { var wb = Workbook.Load(new MemoryStream(bytes), null, null); foreach (var s in wb.Sheets) _ = s.PopulatedCells.Count(); }
                catch (XlsxFormatException) { }
                try
                {
                    using var rd = XlsxReader.Open(new MemoryStream(bytes));
                    foreach (var si in rd.Sheets) { var sr = rd.Sheet(si.Name); foreach (var row in sr.Rows) _ = row.Count; _ = sr.ReadDrawings(); _ = sr.ReadComments(); }
                }
                catch (XlsxFormatException) { }
                Console.WriteLine($"          {what}: no crash ({timer.Elapsed.TotalSeconds:F1} s)");
                // XLinq builds a tree in depth-squared time (100,000 levels: about two minutes before 2.2.1).
                TestGroup.True(timer.Elapsed.TotalSeconds < 10, $"{what}: took {timer.Elapsed.TotalSeconds:F1} s");
            }
        });
    }

    private static object? Eval(string formula)
    {
        var src = new DictSource(new Dictionary<(int, int), object?> { [(1, 1)] = 1.0 });
        return MiniFormula.Parse(formula).Evaluate(new MiniFormula.EvalContext
        {
            Source = src, Row = 1, Column = 1, AnchorRow = 1, AnchorColumn = 1, Today = new DateTime(2026, 11, 25),
        });
    }

    private static ConditionalFormatResult? Rule(string formula, ConditionalFormatOptions options, ICellValueSource? source = null, string range = "A1", int row = 1, int col = 1)
    {
        var rule = new ConditionalRule(ConditionalRuleType.Expression) { Style = new DifferentialStyle { Bold = true } };
        rule.Formulas.Add(formula);
        source ??= new DictSource(new Dictionary<(int, int), object?> { [(1, 1)] = 1.0 });
        return new ConditionalFormatEvaluator(new[] { new ConditionalFormatting(new[] { range }, new[] { rule }) }, null, source, options).Evaluate(row, col);
    }

    // ---- P2: wildcards (SEC-10) ------------------------------------------------------------------------------------

    private static void Wildcards(TestGroup g)
    {
        g.Check("P2: the wildcard matcher gives the old regex's results on 10,000 random patterns and texts", () =>
        {
            var rnd = new Random(2211);
            const string alphabet = "aAbBkK_x0~*?\n\r .ſµΣσς";
            string Rand(int max)
            {
                var sb = new StringBuilder();
                int n = rnd.Next(max + 1);
                for (int i = 0; i < n; i++)
                    sb.Append(rnd.Next(20) == 0 ? (char)rnd.Next(0x20, 0xD7FF) : alphabet[rnd.Next(alphabet.Length)]);
                return sb.ToString();
            }
            int compared = 0;
            for (int i = 0; i < 10_000; i++)
            {
                var pattern = Rand(8);
                if (pattern.Length == 0) continue;
                var text = Rand(24);
                var wc = Wildcard.Compile(pattern);
                bool oldWhole = new Regex(OldWildcardToRegex(pattern, true), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch(text);
                if (wc.IsMatch(text) != oldWhole) throw new Exception($"IsMatch differs: pattern {Show(pattern)} text {Show(text)}: regex {oldWhole}");
                int start = rnd.Next(text.Length + 1);
                var m = new Regex(OldWildcardToRegex(pattern, false), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Match(text, start);
                int oldIndex = m.Success ? m.Index : -1;
                int newIndex = wc.IndexOf(text, start);
                if (newIndex != oldIndex) throw new Exception($"IndexOf differs: pattern {Show(pattern)} text {Show(text)} from {start}: regex {oldIndex}, new {newIndex}");
                compared++;
            }
            Console.WriteLine($"          {compared:N0} pattern/text pairs, all identical");
        });

        g.Check("SEC-10: SEARCH(\"*a*a*a*a*b\") over 300 letters and a 25-star COUNTIF finish in milliseconds", () =>
        {
            var text = new string('a', 300);
            var src = new DictSource(new Dictionary<(int, int), object?> { [(1, 1)] = text });
            var sw = Stopwatch.StartNew();
            var r1 = Rule("ISNUMBER(SEARCH(\"*a*a*a*a*b\",A1))", new ConditionalFormatOptions(), src);
            var stars = "\"" + string.Concat(Enumerable.Repeat("*a", 25)) + "b\"";
            var r2 = Rule($"COUNTIF(A1:A1,{stars})>0", new ConditionalFormatOptions(), src);
            sw.Stop();
            TestGroup.True(r1 is { NotEvaluated.Count: 0, Bold: null } && r2 is { NotEvaluated.Count: 0, Bold: null }, "both evaluated, no match");
            TestGroup.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"          {sw.ElapsedMilliseconds} ms (was over 120 s)");
            TestGroup.True(Rule("ISNUMBER(SEARCH(\"*a*a*a\",A1))", new ConditionalFormatOptions(), src) is { Bold: true }, "a real match still matches");
        });
    }

    private static string Show(string s) => "\"" + string.Concat(s.Select(c => c is < ' ' or > '~' ? $"\\u{(int)c:X4}" : c.ToString())) + "\"";

    /// <summary>MiniFormula.WildcardToRegex as it was in 2.2.0, kept here to compare the new matcher against.</summary>
    private static string OldWildcardToRegex(string pattern, bool anchored)
    {
        var sb = new StringBuilder(anchored ? "^" : "");
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '~' && i + 1 < pattern.Length) { sb.Append(Regex.Escape(pattern[++i].ToString())); continue; }
            if (ch == '*') sb.Append(".*");
            else if (ch == '?') sb.Append('.');
            else sb.Append(Regex.Escape(ch.ToString()));
        }
        if (anchored) sb.Append('$');
        return sb.ToString();
    }

    // ---- P3: cancellation and the work budget ------------------------------------------------------------------------

    private static void Budget(TestGroup g)
    {
        var cells = new Dictionary<(int, int), object?>();
        for (int r = 1; r <= 5000; r++) cells[(r, 1)] = (double)r;
        var src = new DictSource(cells);

        g.Check("P3: a cancelled token stops evaluation with OperationCanceledException", () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            TestGroup.Throws<OperationCanceledException>(() => Rule("A1>0", new ConditionalFormatOptions { CancellationToken = cts.Token }, src), "cancelled");
        });

        g.Check("P3: past MaxCellsVisited the rule is \"not evaluated\"; the default 100 M and null evaluate it", () =>
        {
            const string f = "COUNTIF($A$1:$A$5000,A1)>0";
            TestGroup.Eq(100_000_000L, new ConditionalFormatOptions().MaxCellsVisited, "default");
            TestGroup.True(Rule(f, new ConditionalFormatOptions { MaxCellsVisited = 1000 }, src, "A1:A5000") is { NotEvaluated.Count: 1 }, "budget 1,000");
            TestGroup.True(Rule(f, new ConditionalFormatOptions(), src, "A1:A5000") is { NotEvaluated.Count: 0, Bold: true }, "default budget");
            TestGroup.True(Rule(f, new ConditionalFormatOptions { MaxCellsVisited = null }, src, "A1:A5000") is { NotEvaluated.Count: 0, Bold: true }, "no budget");
            TestGroup.Throws<ArgumentOutOfRangeException>(() => new ConditionalFormatOptions { MaxCellsVisited = 0 }, "0 is refused");
            // Once used up, every later cell is "not evaluated" too, and Evaluate itself never throws for it.
            var rule = new ConditionalRule(ConditionalRuleType.Expression) { Style = new DifferentialStyle { Bold = true } };
            rule.Formulas.Add(f);
            var ev = new ConditionalFormatEvaluator(new[] { new ConditionalFormatting(new[] { "A1:A5000" }, new[] { rule }) }, null, src, new ConditionalFormatOptions { MaxCellsVisited = 12_000 });
            TestGroup.True(ev.Evaluate(1, 1) is { NotEvaluated.Count: 0 } && ev.Evaluate(2, 1) is { NotEvaluated.Count: 0 }, "the first two fit");
            TestGroup.True(ev.Evaluate(3, 1) is { NotEvaluated.Count: 1 } && ev.Evaluate(4, 1) is { NotEvaluated.Count: 1 }, "then not evaluated");
        });

        g.Check("P3: 1,000 x COUNTIF over a 1,000,000-row column stops at the budget in seconds, not hours", () =>
        {
            var big = new RangeSource(1_000_000);
            var rule = new ConditionalRule(ConditionalRuleType.Expression) { Style = new DifferentialStyle { Bold = true } };
            rule.Formulas.Add("COUNTIF($A:$A,A1)>1");
            var ev = new ConditionalFormatEvaluator(new[] { new ConditionalFormatting(new[] { "A1:A1000000" }, new[] { rule }) }, null, big, new ConditionalFormatOptions());
            var sw = Stopwatch.StartNew();
            int notEvaluated = 0;
            for (int r = 1; r <= 1000; r++) if (ev.Evaluate(r, 1) is { NotEvaluated.Count: 1 }) notEvaluated++;
            sw.Stop();
            TestGroup.True(notEvaluated > 800, $"{notEvaluated} of 1,000 not evaluated");
            TestGroup.True(sw.Elapsed.TotalSeconds < 60, $"took {sw.Elapsed.TotalSeconds:F1} s");
            Console.WriteLine($"          {1000 - notEvaluated} cells evaluated, {notEvaluated} not evaluated, {ev.WorkUsed:N0} work units, {sw.Elapsed.TotalSeconds:F1} s");
        });
    }

    // ---- P4, P5: safe defaults, MaxCells (S-02, SEC-01) ------------------------------------------------------------------

    private static void Defaults(TestGroup g)
    {
        g.Check("P4: the defaults are the untrusted limits; ForTrustedInput() turns them all off", () =>
        {
            var d = new XlsxReaderOptions();
            TestGroup.Eq(256L * 1024 * 1024, d.MaxPartBytes, "MaxPartBytes");
            TestGroup.Eq(1024L * 1024 * 1024, d.MaxTotalUncompressedBytes, "MaxTotalUncompressedBytes");
            TestGroup.Eq(500.0, d.MaxCompressionRatio, "MaxCompressionRatio");
            TestGroup.Eq(20_000, d.MaxPackageEntries, "MaxPackageEntries");
            TestGroup.Eq(1_048_576, d.MaxRowsPerSheet, "MaxRowsPerSheet");
            TestGroup.Eq(500_000, d.MaxSpinCount, "MaxSpinCount");
            TestGroup.Eq(4_000_000L, d.MaxCells, "MaxCells");
            var u = XlsxReaderOptions.ForUntrustedInput();
            TestGroup.True(u.MaxPartBytes == d.MaxPartBytes && u.MaxTotalUncompressedBytes == d.MaxTotalUncompressedBytes && u.MaxCompressionRatio == d.MaxCompressionRatio
                && u.MaxPackageEntries == d.MaxPackageEntries && u.MaxRowsPerSheet == d.MaxRowsPerSheet && u.MaxSpinCount == d.MaxSpinCount && u.MaxCells == d.MaxCells, "ForUntrustedInput() = the defaults");
            var t = XlsxReaderOptions.ForTrustedInput();
            TestGroup.True(t.MaxPartBytes is null && t.MaxTotalUncompressedBytes is null && t.MaxCompressionRatio is null && t.MaxPackageEntries is null
                && t.MaxRowsPerSheet is null && t.MaxSpinCount is null && t.MaxCells is null, "ForTrustedInput(): all null");
            TestGroup.Throws<ArgumentOutOfRangeException>(() => new XlsxReaderOptions { MaxCells = 0 }, "MaxCells 0 is refused");
        });

        g.Check("S-02: a 400 MB-of-spaces sheet (a few hundred KB of zip) is refused by the defaults, with little memory", () =>
        {
            var bomb = SpacesBomb(400L * 1024 * 1024);
            Console.WriteLine($"          bomb: {bomb.Length:N0} bytes");
            long before = GC.GetAllocatedBytesForCurrentThread();
            Exception? ex = null;
            try { Workbook.Load(new MemoryStream(bomb), null, null); }
            catch (Exception e) { ex = e; }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            TestGroup.True(ex is XlsxLimitExceededException || ex is InvalidDataException, "refused: " + (ex is null ? "loaded" : ex.GetType().Name + ": " + ex.Message));
            TestGroup.True(used < 64L * 1024 * 1024, $"allocated {used / 1048576.0:F0} MB (was 1,604 MB)");
            Console.WriteLine($"          {ex!.GetType().Name}, {used / 1048576.0:F1} MB allocated");
            TestGroup.True(Workbook.Load(new MemoryStream(bomb), null, XlsxReaderOptions.ForTrustedInput()).Sheets.Count == 1, "loads when trusted");
        });

        g.Check("P5: MaxCells counts the whole workbook, stops early, and does not limit the streaming reader", () =>
        {
            var ms = new MemoryStream();
            using (var w = XlsxWriter.Create(ms))
            {
                foreach (var name in new[] { "One", "Two", "Three" })
                {
                    var sh = w.AddSheet(name);
                    for (int r = 0; r < 20; r++) sh.WriteRow(Enumerable.Range(0, 10).Select(c => (object?)(r * 10 + c)).ToArray());
                }
                w.Save();
            }
            var bytes = ms.ToArray();
            TestGroup.Eq(600, Workbook.Load(new MemoryStream(bytes), null, new XlsxReaderOptions { MaxCells = 600 }).Sheets.Sum(s => s.PopulatedCells.Count()), "600 cells, limit 600");
            var ex = TestGroup.Throws<XlsxLimitExceededException>(() => Workbook.Load(new MemoryStream(bytes), null, new XlsxReaderOptions { MaxCells = 450 }), "450 over three sheets");
            TestGroup.Eq("MaxCells", ex.LimitName, "LimitName");
            TestGroup.True(ex.Message.Contains("'Three'") && ex.Message.Contains("streaming"), "names the sheet and the way out: " + ex.Message);
            using var rd = XlsxReader.Open(new MemoryStream(bytes), null, new XlsxReaderOptions { MaxCells = 1, TreatFirstRowAsHeaders = false });
            TestGroup.Eq(60, rd.Sheets.Sum(si => rd.Sheet(si.Name).Rows.Count()), "streaming reads every row");
        });

        g.Check("SEC-01: 300,000 rows x 20 cells (6 M cells) stop at MaxCells with defaults", () =>
        {
            var ms = new MemoryStream();
            using (var w = XlsxWriter.Create(ms))
            {
                var sh = w.AddSheet("Big");
                var row = new object?[20];
                for (int r = 0; r < 300_000; r++) { for (int c = 0; c < 20; c++) row[c] = (double)(c + 1); sh.WriteRow(row); }
                w.Save();
            }
            var bytes = ms.ToArray();
            ms = null;
            GC.Collect();
            long before = GC.GetTotalMemory(forceFullCollection: true);
            var sw = Stopwatch.StartNew();
            var ex = TestGroup.Throws<XlsxLimitExceededException>(() => Workbook.Load(new MemoryStream(bytes), null, null), "default load");
            sw.Stop();
            TestGroup.Eq("MaxCells", ex.LimitName, "LimitName");
            TestGroup.Eq(4_000_001.0, ex.Actual, "stops at the first cell over the limit");
            Console.WriteLine($"          {bytes.Length:N0}-byte file refused after {sw.Elapsed.TotalSeconds:F1} s: {ex.Message.Split(':')[0]}");
            GC.KeepAlive(before);
        });
    }

    // ---- P6, P7, P8: the writer (S-01, S-15, S-13) ------------------------------------------------------------------------

    private static void Writer(TestGroup g)
    {
        g.Check("S-01 / P6: a writer disposed without Save writes nothing and says so (IsSaved)", () =>
        {
            var ms = new MemoryStream();
            XlsxWriter w;
            using (w = XlsxWriter.Create(ms)) { w.AddSheet("S").WriteRow("x"); TestGroup.True(!w.IsSaved, "not saved yet"); }
            TestGroup.True(!w.IsSaved && ms.Length == 0, $"nothing written ({ms.Length} bytes)");
            var ms2 = new MemoryStream();
            using (w = XlsxWriter.Create(ms2)) { w.AddSheet("S").WriteRow("x"); w.Save(); }
            TestGroup.True(w.IsSaved && ms2.Length > 0, "saved");
        });

        g.Check("S-15 / P7: SaveTo with an empty password throws ArgumentException (null: ArgumentNullException) before writing", () =>
        {
            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = "x";
            var ms = new MemoryStream();
            TestGroup.Throws<ArgumentException>(() => wb.SaveTo(ms, new EncryptionOptions { Password = "" }), "stream, empty");
            TestGroup.Throws<ArgumentNullException>(() => wb.SaveTo(ms, new EncryptionOptions { Password = null! }), "stream, null (as before)");
            TestGroup.Eq(0L, ms.Length, "nothing written to the stream");
            var path = Path.Combine(Path.GetTempPath(), $"chuvadi_s15_{Guid.NewGuid():N}.xlsx");
            TestGroup.Throws<ArgumentException>(() => wb.SaveTo(path, new EncryptionOptions { Password = "" }), "path, empty");
            TestGroup.True(!File.Exists(path), "no file");
        });

        // Every free-text field, with control characters, a lone surrogate and literal escapes.
        const string Nasty = "a\u0001b\u000Bc\u001Fd _x0041_ _X0041_ _x005F_ _x12_ \uD800 e￿";

        g.Check("S-13 / P8: control characters save as _xHHHH_ and every field reads back unchanged", () =>
        {
            foreach (bool inline in new[] { false, true })
            {
                var ms = new MemoryStream();
                using (var w = XlsxWriter.Create(ms, new XlsxWriterOptions { UseInlineStrings = inline }))
                {
                    var sh = w.AddSheet("S");
                    sh.WriteRow("a\u0001b", Nasty, new RichText(new RichTextRun(Nasty), new RichTextRun("\u0002", new RichTextFont { Bold = true })));
                    w.Save();
                }
                var xml = PartText(ms.ToArray(), inline ? "xl/worksheets/sheet1.xml" : "xl/sharedStrings.xml");
                TestGroup.True(xml.Contains("a_x0001_b") && xml.Contains("_x005F_x0041_") && xml.Contains(" _X0041_ ") /* a capital X is not an escape: written as is */ && xml.Contains("_x005F_x005F_")
                    && xml.Contains("_x12_") && xml.Contains("_xD800_") && xml.Contains("_xFFFF_"), "escaped as Excel does");
                using var rd = XlsxReader.Open(new MemoryStream(ms.ToArray()), null, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
                var row = rd.Sheet("S").Rows.First();
                TestGroup.Eq("a\u0001b", row.GetString(0), "streaming: cell 1");
                TestGroup.Eq(Nasty, row.GetString(1), "streaming: cell 2");
                TestGroup.Eq(Nasty + "\u0002", row.GetString(2), "streaming: rich text");
            }

            var wb = new Workbook();
            var s = wb.AddSheet("Model");
            s.Cell("A1").Value = Nasty;
            s.Cell("A2").RichText = new RichText(new RichTextRun(Nasty), new RichTextRun("\u0003"));
            s.Cell("A3").Formula = "\"x\"";
            s.AddComment("B1", "Au\u0001thor _x0041_", Nasty);
            s.SetHeaderFooter("&C" + Nasty, "&R\u0004");
            s.AddHyperlink("C1", "https://example.org/", null, Nasty, "Show\u0005 _x0041_");
            var dv = DataValidation.Custom("TRUE");
            dv.PromptTitle = "T\u0006"; dv.PromptMessage = Nasty; dv.ErrorTitle = "_x0041_"; dv.ErrorMessage = "E\u0007";
            s.AddDataValidation("D1", dv);
            var path = Path.Combine(Path.GetTempPath(), $"chuvadi_s13_{Guid.NewGuid():N}.xlsx");
            try
            {
                wb.SaveTo(path);
                var back = Workbook.Load(path)["Model"];
                TestGroup.Eq(Nasty, back.Cell("A1").Value, "cell");
                TestGroup.Eq(Nasty + "\u0003", back.Cell("A2").Value, "rich text cell");
                TestGroup.Eq(Nasty, back.Comments[0].Text, "comment text");
                TestGroup.Eq("Au\u0001thor _x0041_", back.Comments[0].Author, "comment author");
                TestGroup.Eq("&C" + Nasty, back.PageSetup.HeaderFooter.OddHeader, "header");
                TestGroup.Eq("&R\u0004", back.PageSetup.HeaderFooter.OddFooter, "footer");
                TestGroup.Eq(Nasty, back.Hyperlinks[0].Tooltip, "hyperlink tooltip");
                TestGroup.Eq("Show\u0005 _x0041_", back.Hyperlinks[0].Display, "hyperlink display");
                var v = back.DataValidations[0].Validation;
                TestGroup.True(v.PromptTitle == "T\u0006" && v.PromptMessage == Nasty && v.ErrorTitle == "_x0041_" && v.ErrorMessage == "E\u0007", "validation texts");
            }
            finally { try { File.Delete(path); } catch { } }
        });

        g.Check("P8: a cached formula text result round-trips, and Excel's own _x000B_ reads as the character", () =>
        {
            var bytes = Package(Sheet("<row r=\"1\"><c r=\"A1\" t=\"str\"><f>A2</f><v>x_x000B_y _x005F_x0041_</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>p_x000D_q</t></is></c></row>"));
            var wb = Workbook.Load(new MemoryStream(bytes), null, null);
            TestGroup.Eq("x\u000By _x0041_", wb.Sheets[0].Cell("A1").Value, "cached formula string");
            TestGroup.Eq("p\rq", wb.Sheets[0].Cell("B1").Value, "inline string");
            var ms = new MemoryStream();
            wb.SaveTo(ms);
            var again = Workbook.Load(new MemoryStream(ms.ToArray()), null, null);
            TestGroup.Eq("x\u000By _x0041_", again.Sheets[0].Cell("A1").Value, "saved again, read again");
        });

        g.Check("P8: text without control characters or escapes is written byte for byte as before", () =>
        {
            var ms = new MemoryStream();
            using (var w = XlsxWriter.Create(ms))
            {
                w.AddSheet("S").WriteRow("plain", " lead", "tab\there", "line\nbreak", "_x", "_x00", "x_0041_", "naïve ✓ 😀");
                w.Save();
            }
            var xml = PartText(ms.ToArray(), "xl/sharedStrings.xml");
            // A line feed in a text node is written as Environment.NewLine ("\r\n" on Windows), as every version has
            // done: .NET's XmlWriter default (NewLineHandling.Replace). It reads back as "\n".
            foreach (var t in new[] { "<t>plain</t>", "<t xml:space=\"preserve\"> lead</t>", "<t>tab\there</t>", "<t>line" + Environment.NewLine + "break</t>", "<t>_x</t>", "<t>_x00</t>", "<t>x_0041_</t>", "<t>naïve ✓ 😀</t>" })
                TestGroup.True(xml.Contains(t), "unchanged: " + t);
        });
    }

    // ---- P9, P10: the error contract (SEC-05, S-14, S-16) ----------------------------------------------------------------

    private static void ErrorContract(TestGroup g)
    {
        g.Check("S-14 / P10: a Word package is XlsxFormatException, not a workbook with no sheets", () =>
        {
            var docx = Zip(new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>",
                ["_rels/.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"word/document.xml\"/></Relationships>",
                ["word/document.xml"] = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>",
            });
            var ex = TestGroup.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(docx), null, null), "Workbook.Load");
            TestGroup.True(ex.Message.Contains("not a spreadsheet"), ex.Message);
            TestGroup.Throws<XlsxFormatException>(() => XlsxReader.Open(new MemoryStream(docx)), "XlsxReader.Open");
        });

        g.Check("S-16 / P9: a corrupt salt in an encrypted file is XlsxFormatException (was FormatException)", () =>
        {
            var wb = new Workbook();
            wb.AddSheet("S").Cell("A1").Value = 1;
            var ms = new MemoryStream();
            wb.SaveTo(ms, new EncryptionOptions { Password = "pw" });
            var bytes = ms.ToArray();
            var marker = Encoding.ASCII.GetBytes("saltValue=\"");
            int at = IndexOf(bytes, marker);
            TestGroup.True(at > 0, "salt found");
            bytes[at + marker.Length] = (byte)'!';
            var ex = TestGroup.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(bytes), "pw", null), "corrupt salt");
            TestGroup.True(ex.InnerException is FormatException, "the original is the inner exception: " + ex.InnerException?.GetType().Name);
        });

        g.Check("SEC-05 / P9: two sheets with the same name are XlsxFormatException (was InvalidOperationException)", () =>
        {
            var e = BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>"));
            e["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"s\" sheetId=\"2\" r:id=\"rId1\"/></sheets></workbook>";
            var ex = TestGroup.Throws<XlsxFormatException>(() => Workbook.Load(new MemoryStream(Zip(e)), null, null), "duplicate sheet name");
            TestGroup.True(ex.Message.Contains("'s'"), ex.Message);
        });
    }

    // ---- P9: the fuzz test ---------------------------------------------------------------------------------------------

    private static void Fuzz(TestGroup g, int iterations)
    {
        g.Check($"SEC-05 / P9: {iterations:N0} mutated workbooks raise only the documented exception types", () =>
        {
            var seeds = new List<Dictionary<string, byte[]>> { Unzip(RichSeed()), Unzip(Zip(DrawingEntries(GroupsDrawing(3)))), Unzip(InlineSeed()) };
            var rnd = new Random(221);
            var stray = new Dictionary<string, (int Count, string Example)>();
            var outcomes = new Dictionary<string, int>();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var entries = seeds[rnd.Next(seeds.Count)].ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone());
                var bytes = Mutate(entries, rnd);
                string outcome;
                try { ReadEverything(bytes); outcome = "read"; }
                catch (Exception ex) when (Allowed(ex)) { outcome = ex.GetType().Name; }
                catch (Exception ex)
                {
                    var key = ex.GetType().FullName + " at " + (ex.StackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains("Chuvadi"))?.Trim();
                    stray[key] = stray.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, $"#{i}: {ex.Message}");
                    outcome = "STRAY";
                }
                outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            }
            sw.Stop();
            Console.WriteLine($"          {string.Join(", ", outcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"))} ({sw.Elapsed.TotalSeconds:F1} s)");
            if (stray.Count > 0)
                throw new Exception("stray exception types:\n" + string.Join("\n", stray.Select(kv => $"            {kv.Value.Count} x {kv.Key}\n              {kv.Value.Example}")));
        });
    }

    private static bool Allowed(Exception ex) => ex switch
    {
        XlsxFormatException or XlsxLimitExceededException or XlsxPasswordRequiredException => true,
        InvalidDataException ide => ide.Data.Contains(MalformedInput.LimitMarker),   // MaxPartBytes, until 3.0
        _ => false,
    };

    /// <summary>Every public read entry point: the model (with conditional formats) and the streaming reader.</summary>
    private static void ReadEverything(byte[] bytes)
    {
        var wb = Workbook.Load(new MemoryStream(bytes), null, null);
        foreach (var s in wb.Sheets)
        {
            var ev = s.EvaluateConditionalFormats();
            foreach (var c in s.PopulatedCells.Take(200)) { _ = ev.Evaluate(c.Row, c.Column); _ = c.DisplayText; }
        }
        using var rd = XlsxReader.Open(new MemoryStream(bytes));
        foreach (var si in rd.Sheets)
        {
            var sr = rd.Sheet(si.Name);
            foreach (var row in sr.Rows)
                for (int c = 0; c < row.Count; c++) { _ = row[c]; _ = row.GetDisplayString(c); _ = row.GetFormulaInfo(c); }
            _ = sr.ReadLayout();
            _ = sr.ReadComments();
            foreach (var d in sr.ReadDrawings()) _ = d.GetImageBytes();
        }
    }

    private static readonly string[] Junk = { "", "-1", "0", "999999999999", "1048577", "16385", "A0", "XFE1", "abc", "1e309", "NaN", "-0", "2147483648", "A1:XFD1048576", "&amp;", "_x0001_", "#REF!", "1.5", "é" };

    private static byte[] Mutate(Dictionary<string, byte[]> entries, Random rnd)
    {
        var names = entries.Keys.ToList();
        var name = names[rnd.Next(names.Count)];
        var data = entries[name];
        switch (rnd.Next(7))
        {
            case 0:   // flip a few bytes
                for (int k = rnd.Next(1, 9); k > 0 && data.Length > 0; k--) data[rnd.Next(data.Length)] = (byte)rnd.Next(256);
                break;
            case 1:   // truncate
                entries[name] = data.AsSpan(0, rnd.Next(data.Length + 1)).ToArray();
                break;
            case 2:   // replace attribute values
            {
                var text = Encoding.UTF8.GetString(data);
                var attrs = Regex.Matches(text, "=\"[^\"]*\"");
                for (int k = rnd.Next(1, 4); k > 0 && attrs.Count > 0; k--)
                {
                    var m = attrs[rnd.Next(attrs.Count)];
                    text = text.Substring(0, m.Index) + "=\"" + Junk[rnd.Next(Junk.Length)] + "\"" + text.Substring(m.Index + m.Length);
                    attrs = Regex.Matches(text, "=\"[^\"]*\"");
                }
                entries[name] = Encoding.UTF8.GetBytes(text);
                break;
            }
            case 3:   // duplicate a run of markup at another tag boundary
            {
                var text = Encoding.UTF8.GetString(data);
                var tags = Enumerable.Range(0, text.Length).Where(i => text[i] == '<').ToList();
                if (tags.Count >= 2)
                {
                    int a = tags[rnd.Next(tags.Count - 1)], b = tags.First(t => t > a), at = tags[rnd.Next(tags.Count)];
                    var piece = text.Substring(a, Math.Min(b - a + rnd.Next(200), text.Length - a));
                    text = text.Insert(at, piece);
                }
                entries[name] = Encoding.UTF8.GetBytes(text);
                break;
            }
            case 4:   // remove a part
                entries.Remove(name);
                break;
            case 5:   // remove a run of markup
            {
                var text = Encoding.UTF8.GetString(data);
                if (text.Length > 2) { int a = rnd.Next(text.Length - 1); text = text.Remove(a, rnd.Next(1, Math.Min(64, text.Length - a))); }
                entries[name] = Encoding.UTF8.GetBytes(text);
                break;
            }
            default:  // damage the zip itself
            {
                var zip = ZipBytes(entries);
                for (int k = rnd.Next(1, 5); k > 0; k--) zip[rnd.Next(zip.Length)] = (byte)rnd.Next(256);
                return zip;
            }
        }
        return ZipBytes(entries);
    }

    private static byte[] RichSeed()
    {
        var wb = new Workbook();
        var s = wb.AddSheet("Data");
        s.Cell("A1").Value = "Name"; s.Cell("B1").Value = "Amount"; s.Cell("C1").Value = "Date"; s.Cell("D1").Value = "Done";
        for (int r = 2; r <= 12; r++)
        {
            s.Cell(r, 1).Value = "Item " + r;
            s.Cell(r, 2).Value = r * 12.5;
            s.Cell(r, 3).Value = new DateTime(2026, 1, r);
            s.Cell(r, 4).Value = r % 2 == 0;
        }
        s.Cell("B13").Formula = "SUM(B2:B12)";
        s.Cell("A14").RichText = new RichText(new RichTextRun("Bold ", new RichTextFont { Bold = true }), new RichTextRun("plain"));
        s.Range("A15:C15").Merge();
        s.AddComment("A2", "Ana", "A note");
        s.AddHyperlink("A3", "https://example.org/", null, "Tip", "Shown");
        s.AddDataValidation("D2:D12", DataValidation.List("TRUE", "FALSE"));
        var rule = new ConditionalRule(ConditionalRuleType.CellIs) { Operator = ComparisonOp.GreaterThan, Style = new DifferentialStyle { FillColor = "FFC7CE" } };
        rule.Formulas.Add("50");
        var text = new ConditionalRule(ConditionalRuleType.Expression) { Style = new DifferentialStyle { Bold = true } };
        text.Formulas.Add("ISNUMBER(SEARCH(\"it*m\",A2))");
        s.AddConditionalFormatting(new ConditionalFormatting(new[] { "B2:B12" }, new[] { rule }));
        s.AddConditionalFormatting(new ConditionalFormatting(new[] { "A2:A12" }, new[] { text }));
        s.SetHeaderFooter("&CReport", "&RPage &P");
        s.Cell("B2").Style = new CellStyle { Bold = true, NumberFormat = "#,##0.00", FillColor = "DDEBF7" };
        var t = wb.AddSheet("Other");
        t.Cell("A1").Value = 42;
        wb.DefinedNames.Add("Total", "Data!$B$13");
        var ms = new MemoryStream();
        wb.SaveTo(ms);
        return ms.ToArray();
    }

    private static byte[] InlineSeed()
    {
        var ms = new MemoryStream();
        using (var w = XlsxWriter.Create(ms, new XlsxWriterOptions { UseInlineStrings = true }))
        {
            var sh = w.AddSheet("Inline");
            sh.WriteRow("Text", 1.5, true, new DateTime(2026, 11, 25), null, "x\u0001y");
            sh.WriteRow(new RichText(new RichTextRun("r1"), new RichTextRun("r2", new RichTextFont { Italic = true })));
            w.Save();
        }
        return ms.ToArray();
    }

    // ---- fixtures ------------------------------------------------------------------------------------------------------

    private static string Sheet(string rows, string after = "") => $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheetData>{rows}</sheetData>{after}</worksheet>";

    private static Dictionary<string, string> BaseEntries(string sheetXml) => new()
    {
        ["[Content_Types].xml"] = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"vml\" ContentType=\"application/vnd.openxmlformats-officedocument.vmlDrawing\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
        ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
        ["xl/_rels/workbook.xml.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>",
        ["xl/worksheets/sheet1.xml"] = sheetXml,
    };

    private static Dictionary<string, string> With(Dictionary<string, string> e, string part, string xml, string relType, string target)
    {
        e[part] = xml;
        e["xl/_rels/workbook.xml.rels"] = e["xl/_rels/workbook.xml.rels"].Replace("</Relationships>", $"<Relationship Id=\"rId9\" Type=\"{R}/{relType}\" Target=\"{target}\"/></Relationships>");
        return e;
    }

    private static Dictionary<string, string> DrawingEntries(string drawingXml)
    {
        var e = BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", "<drawing r:id=\"rIdD\"/>"));
        e["xl/worksheets/_rels/sheet1.xml.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rIdD\" Type=\"{R}/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>";
        e["xl/drawings/drawing1.xml"] = drawingXml;
        return e;
    }

    private static Dictionary<string, string> ChartEntries(string chartXml)
    {
        var e = DrawingEntries($"<xdr:wsDr xmlns:xdr=\"{Xdr}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\"><xdr:twoCellAnchor>{Anchor()}<xdr:graphicFrame><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>"
            + "<xdr:xfrm/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rIdC\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>");
        e["xl/drawings/_rels/drawing1.xml.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rIdC\" Type=\"{R}/chart\" Target=\"../charts/chart1.xml\"/></Relationships>";
        e["xl/charts/chart1.xml"] = chartXml;
        return e;
    }

    private static Dictionary<string, string> CommentEntries(string commentsXml, string vml)
    {
        var e = BaseEntries(Sheet("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", "<legacyDrawing r:id=\"rIdV\"/>"));
        e["xl/worksheets/_rels/sheet1.xml.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rIdC\" Type=\"{R}/comments\" Target=\"../comments1.xml\"/>"
            + $"<Relationship Id=\"rIdV\" Type=\"{R}/vmlDrawing\" Target=\"../drawings/vmlDrawing1.vml\"/></Relationships>";
        e["xl/comments1.xml"] = commentsXml;
        e["xl/drawings/vmlDrawing1.vml"] = vml;
        return e;
    }

    private static string Anchor() => "<xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
        + "<xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>6</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>";

    private static string GroupsDrawing(int depth)
    {
        var sb = new StringBuilder($"<xdr:wsDr xmlns:xdr=\"{Xdr}\" xmlns:a=\"{A}\"><xdr:twoCellAnchor>{Anchor()}");
        for (int i = 0; i < depth; i++) sb.Append($"<xdr:grpSp><xdr:nvGrpSpPr><xdr:cNvPr id=\"{i + 2}\" name=\"Group {i + 1}\"/><xdr:cNvGrpSpPr/></xdr:nvGrpSpPr><xdr:grpSpPr/>");
        sb.Append("<xdr:sp><xdr:nvSpPr><xdr:cNvPr id=\"1\" name=\"Box\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst=\"rect\"/></xdr:spPr></xdr:sp>");
        for (int i = 0; i < depth; i++) sb.Append("</xdr:grpSp>");
        sb.Append("<xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>");
        return sb.ToString();
    }

    private static byte[] GroupsPackage(int depth) => Zip(DrawingEntries(GroupsDrawing(depth)));

    private static byte[] Package(string sheetXml) => Zip(BaseEntries(sheetXml));

    /// <summary>One sheet whose sheetData holds <paramref name="spaces"/> spaces, deflated (the S-02 shape).</summary>
    private static byte[] SpacesBomb(long spaces)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in BaseEntries("PLACEHOLDER"))
            {
                using var s = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                if (content != "PLACEHOLDER") { var b = Encoding.UTF8.GetBytes(content); s.Write(b, 0, b.Length); continue; }
                var head = Encoding.UTF8.GetBytes($"<worksheet xmlns=\"{Main}\"><sheetData>");
                s.Write(head, 0, head.Length);
                var block = Encoding.ASCII.GetBytes(new string(' ', 1 << 20));
                for (long left = spaces; left > 0; left -= block.Length) s.Write(block, 0, (int)Math.Min(block.Length, left));
                var tail = Encoding.UTF8.GetBytes("</sheetData></worksheet>");
                s.Write(tail, 0, tail.Length);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Zip(Dictionary<string, string> entries)
        => ZipBytes(entries.ToDictionary(kv => kv.Key, kv => new UTF8Encoding(false).GetBytes(kv.Value)));

    private static byte[] ZipBytes(Dictionary<string, byte[]> entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content, 0, content.Length);
            }
        return ms.ToArray();
    }

    private static Dictionary<string, byte[]> Unzip(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var d = new Dictionary<string, byte[]>();
        foreach (var e in z.Entries)
        {
            using var s = e.Open();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            d[e.FullName] = ms.ToArray();
        }
        return d;
    }

    private static string PartText(byte[] zip, string part)
    {
        using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var s = z.GetEntry(part)!.Open();
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            int k = 0;
            while (k < needle.Length && hay[i + k] == needle[k]) k++;
            if (k == needle.Length) return i;
        }
        return -1;
    }

    private sealed class DictSource : ICellValueSource
    {
        private readonly Dictionary<(int, int), object?> _v;
        public DictSource(Dictionary<(int, int), object?> v) { _v = v; }
        public object? GetValue(int row, int column) => _v.TryGetValue((row, column), out var x) ? x : null;
        public IEnumerable<(int Row, int Column, object? Value)> GetValues(int r1, int c1, int r2, int c2)
        {
            foreach (var ((r, c), v) in _v)
                if (r >= r1 && r <= r2 && c >= c1 && c <= c2 && v is not null) yield return (r, c, v);
        }
    }

    /// <summary>A column A of <c>rows</c> numbers, without storing them.</summary>
    private sealed class RangeSource : ICellValueSource
    {
        private readonly int _rows;
        public RangeSource(int rows) { _rows = rows; }
        public object? GetValue(int row, int column) => column == 1 && row <= _rows ? (double)row : null;
        public IEnumerable<(int Row, int Column, object? Value)> GetValues(int r1, int c1, int r2, int c2)
        {
            if (c1 > 1 || c2 < 1) yield break;
            for (int r = Math.Max(1, r1); r <= Math.Min(_rows, r2); r++) yield return (r, 1, (double)r);
        }
    }
}
