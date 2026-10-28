using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.4.0 read side: display text (C1), password on the streaming reader (C3), hostile-
/// input limits and cancellation (C4), macro detection (C5), page setup, 1904 date system.
/// Fixtures are raw SpreadsheetML packages shaped like Excel's output.
/// </summary>
internal static class ReadSafetyTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[ReadSafetyTest] 1.4.0 read side...");
        Console.WriteLine();
        int pass = 0, fail = 0;

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; }
        }

        static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}: expected <{expected}> got <{actual}>");
        }

        static void True(bool c, string what) { if (!c) throw new Exception(what); }

        static T Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
            throw new Exception($"{what}: expected {typeof(T).Name}, nothing thrown");
        }

        // ---- C1: display text ----------------------------------------------------------------

        var displayPath = Path.Combine(outputDir, "read140_display.xlsx");
        Package.Write(displayPath, new[]
        {
            ("Data",
             "<sheetData><row r=\"1\">" +
             "<c r=\"A1\" s=\"1\"><v>1234567.891</v></c>" +
             "<c r=\"B1\" s=\"2\"><v>45000</v></c>" +
             "<c r=\"C1\" s=\"3\"><v>0.256</v></c>" +
             "<c r=\"D1\" t=\"s\"><v>0</v></c>" +
             "<c r=\"E1\" t=\"b\"><v>1</v></c>" +
             "<c r=\"F1\" t=\"str\"><f>\"a\"&amp;\"b\"</f><v>ab</v></c>" +
             "<c r=\"G1\" t=\"e\"><v>#N/A</v></c>" +
             "<c r=\"H1\" s=\"4\"><v>1234.5</v></c>" +
             "<c r=\"I1\"><v>3.14159265358979</v></c>" +
             "<c r=\"J1\" s=\"5\"><f>1+1</f></c>" +
             "</row></sheetData>", (string?)null),
        },
        styles:
            "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/><numFmt numFmtId=\"165\" formatCode=\"0.0%\"/></numFmts>" +
            "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"6\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"44\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>",
        sharedStrings: new[] { "plain text" });

        var expected = new[] { "1,234,567.89", "15/03/2023", "25.6%", "plain text", "TRUE", "ab", "#N/A", " $1,234.50 ", "3.141592654", "" };

        Check("C1: RowReader.GetDisplayString matches Excel", () =>
        {
            using var r = XlsxReader.Open(displayPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var row = r.Sheet(1).Rows.First();
            for (int i = 0; i < expected.Length; i++)
                Eq(expected[i], row.GetDisplayString(i), $"col {i + 1}");
            Eq("", row.GetDisplayString(50), "missing cell");
        });

        Check("C1: Cell.DisplayText matches the streaming reader", () =>
        {
            var s = Workbook.Load(displayPath).Sheets[0];
            for (int i = 0; i < expected.Length; i++)
                Eq(expected[i], s.Cell(1, i + 1).DisplayText, $"col {i + 1}");
        });

        Check("C1: DisplayCulture (en-IN grouping, pinned date separator)", () =>
        {
            var c = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
            c.DateTimeFormat.DateSeparator = "-";   // Windows en-IN; ICU uses '/'
            using var r = XlsxReader.Open(displayPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false, DisplayCulture = c });
            var row = r.Sheet(1).Rows.First();
            Eq("12,34,567.89", row.GetDisplayString(0), "lakh grouping");
            Eq("15-03-2023", row.GetDisplayString(1), "date separator");
            var wb = Workbook.Load(displayPath, null, new XlsxReaderOptions { DisplayCulture = c });
            Eq("12,34,567.89", wb.Sheets[0].Cell("A1").DisplayText, "model honours culture");
        });

        Check("C1: in-memory cells use the writer's default date/time formats", () =>
        {
            var s = new Workbook().AddSheet("M");
            s.Cell("A1").Value = new DateTime(2026, 10, 28);
            s.Cell("A2").Value = new DateTime(2026, 10, 28, 6, 41, 5);
            s.Cell("A3").Value = new TimeSpan(1, 2, 3, 4);
            s.Cell("A4").Value = 1234.5;
            s.Cell("A4").Style = new CellStyleBuilder().Format("#,##0.00").Build();
            s.Cell("A5").Value = 0.5;
            Eq("2026-10-28", s.Cell("A1").DisplayText, "date");
            Eq("2026-10-28 06:41:05", s.Cell("A2").DisplayText, "date-time");
            Eq("26:03:04", s.Cell("A3").DisplayText, "timespan");
            Eq("1,234.50", s.Cell("A4").DisplayText, "styled number");
            Eq("0.5", s.Cell("A5").DisplayText, "General");
            Eq("", s.Cell("A9").DisplayText, "empty");
        });

        // ---- C3: password on the streaming reader --------------------------------------------

        var encPath = Path.Combine(outputDir, "read140_encrypted.xlsx");
        var plainPath = Path.Combine(outputDir, "read140_plain.xlsx");
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Secret");
            s.Cell("A1").Value = "Name"; s.Cell("A2").Value = "Arun";
            wb.SaveTo(encPath, new EncryptionOptions { Password = "pw" });
            wb.SaveTo(plainPath);
        }

        Check("C3: Open(path, password, options) reads an encrypted file", () =>
        {
            using var r = XlsxReader.Open(encPath, "pw", null);
            Eq("Arun", r.Sheet(1).Rows.First().GetString("Name"), "value");
        });

        Check("C3: missing or wrong password → XlsxPasswordRequiredException", () =>
        {
            Throws<XlsxPasswordRequiredException>(() => XlsxReader.Open(encPath).Dispose(), "no password");
            Throws<XlsxPasswordRequiredException>(() => XlsxReader.Open(encPath, "nope", null).Dispose(), "wrong password");
        });

        Check("C3: stream overload decrypts and leaves the caller's stream open", () =>
        {
            using var fs = File.OpenRead(encPath);
            using (var r = XlsxReader.Open(fs, "pw", new XlsxReaderOptions()))
                Eq("Arun", r.Sheet(1).Rows.First().GetString("Name"), "value");
            True(fs.CanRead, "caller stream still open");
            using var ps = File.OpenRead(plainPath);
            using (var r = XlsxReader.Open(ps, "ignored", null))
                Eq("Arun", r.Sheet(1).Rows.First().GetString("Name"), "unencrypted ignores password");
            True(ps.CanRead, "unencrypted: caller stream still open");
        });

        // ---- C4: limits, bombs, cancellation -----------------------------------------------

        var rowsPath = Path.Combine(outputDir, "read140_rows.xlsx");
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Rows");
            for (int i = 1; i <= 10; i++) s.Cell(i, 1).Value = i;
            wb.SaveTo(rowsPath);
        }

        Check("C4: MaxRowsPerSheet (streaming and model)", () =>
        {
            var o = new XlsxReaderOptions { TreatFirstRowAsHeaders = false, MaxRowsPerSheet = 5 };
            using (var r = XlsxReader.Open(rowsPath, o))
            {
                var ex = Throws<XlsxLimitExceededException>(() => r.Sheet(1).Rows.Count(), "streaming");
                Eq("MaxRowsPerSheet", ex.LimitName, "limit name");
                Eq(5.0, ex.Limit, "limit");
                Eq(6.0, ex.Actual, "actual");
            }
            Throws<XlsxLimitExceededException>(() => Workbook.Load(rowsPath, null, o), "model");
            using (var r = XlsxReader.Open(rowsPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false, MaxRowsPerSheet = 10 }))
                Eq(10, r.Sheet(1).Rows.Count(), "exactly at the limit is fine");
        });

        var bombPath = Path.Combine(outputDir, "read140_bomb.xlsx");
        Package.Write(bombPath, new[] { ("Bomb", "<sheetData/>" + new string(' ', 40 * 1024 * 1024), (string?)null) });

        Check("C4: zip-bomb guards (ratio, declared total, entries) before reading", () =>
        {
            var ratio = Throws<XlsxLimitExceededException>(() => XlsxReader.Open(bombPath, new XlsxReaderOptions { MaxCompressionRatio = 500 }).Dispose(), "ratio");
            Eq("MaxCompressionRatio", ratio.LimitName, "ratio name");
            var total = Throws<XlsxLimitExceededException>(() => XlsxReader.Open(bombPath, new XlsxReaderOptions { MaxTotalUncompressedBytes = 10_000_000 }).Dispose(), "total");
            Eq("MaxTotalUncompressedBytes", total.LimitName, "total name");
            var entries = Throws<XlsxLimitExceededException>(() => XlsxReader.Open(bombPath, new XlsxReaderOptions { MaxPackageEntries = 3 }).Dispose(), "entries");
            Eq("MaxPackageEntries", entries.LimitName, "entries name");
            Throws<InvalidDataException>(() => XlsxReader.Open(bombPath, new XlsxReaderOptions { MaxPartBytes = 1_000_000 }).Dispose(), "MaxPartBytes keeps InvalidDataException");
        });

        Check("C4: ForUntrustedInput reads normal files; non-seekable input works", () =>
        {
            var untrusted = XlsxReaderOptions.ForUntrustedInput();
            untrusted.TreatFirstRowAsHeaders = false;
            using (var r = XlsxReader.Open(displayPath, untrusted))
                Eq(1, r.Sheet(1).Rows.Count(), "normal file");
            var bytes = File.ReadAllBytes(plainPath);
            using var ns = new NonSeekable(new MemoryStream(bytes));
            var o = XlsxReaderOptions.ForUntrustedInput();
            using var r2 = XlsxReader.Open(ns, o);
            Eq("Arun", r2.Sheet(1).Rows.First().GetString("Name"), "non-seekable with limits");
        });

        Check("C4: cancellation stops a large read within a second", () =>
        {
            var big = Path.Combine(outputDir, "read140_big.xlsx");
            using (var w = XlsxWriter.Create(big))
            {
                using var sh = w.AddSheet("Big");
                for (int i = 0; i < 300_000; i++) sh.WriteRow(i, "row " + i, i * 1.5);
                w.Save();
            }
            using var cts = new CancellationTokenSource();
            var o = new XlsxReaderOptions { TreatFirstRowAsHeaders = false, CancellationToken = cts.Token };
            var sw = Stopwatch.StartNew();
            long latency = -1;
            Throws<OperationCanceledException>(() =>
            {
                using var r = XlsxReader.Open(big, o);
                int n = 0;
                foreach (var _ in r.Sheet(1).Rows)
                    if (++n == 1000) { cts.Cancel(); sw.Restart(); }
            }, "streaming");
            latency = sw.ElapsedMilliseconds;
            True(latency < 1000, $"cancel latency {latency} ms");

            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            sw.Restart();
            Throws<OperationCanceledException>(() => Workbook.Load(big, null, new XlsxReaderOptions { CancellationToken = cts2.Token }), "model");
            True(sw.ElapsedMilliseconds < 1150, $"model cancel took {sw.ElapsedMilliseconds} ms");

            using var pre = new CancellationTokenSource();
            pre.Cancel();
            Throws<OperationCanceledException>(() => XlsxReader.Open(big, new XlsxReaderOptions { CancellationToken = pre.Token }).Dispose(), "pre-cancelled");
            Console.WriteLine($"          (cancel latency {latency} ms)");
        });

        // ---- C5: macros ----------------------------------------------------------------------

        var xlsmPath = Path.Combine(outputDir, "read140_macros.xlsm");
        Package.Write(xlsmPath, new[] { ("Sheet1", "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>", (string?)null) },
            mainContentType: "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
            extraParts: new[] { ("xl/vbaProject.bin", "application/vnd.ms-office.vbaProject",
                "http://schemas.microsoft.com/office/2006/relationships/vbaProject", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3, 4 }) });

        Check("C5: HasMacros detects a VBA project, never runs it, and save drops it", () =>
        {
            using (var r = XlsxReader.Open(xlsmPath))
                True(r.HasMacros, "reader flag");
            using (var r = XlsxReader.Open(displayPath))
                True(!r.HasMacros, "plain file");
            var wb = Workbook.Load(xlsmPath);
            True(wb.HasMacros, "model flag");
            Eq(1.0, wb.Sheets[0].Cell("A1").Value, "data readable");
            var saved = Path.Combine(outputDir, "read140_macros_resaved.xlsx");
            wb.SaveTo(saved);
            using var z = ZipFile.OpenRead(saved);
            True(!z.Entries.Any(e => e.FullName.Contains("vbaProject")), "no VBA in saved file");
        });

        // ---- Page setup ----------------------------------------------------------------------

        var pagePath = Path.Combine(outputDir, "read140_pagesetup.xlsx");
        Package.Write(pagePath, new[]
        {
            ("Report",
             "<sheetPr><outlinePr summaryBelow=\"0\"/><pageSetUpPr fitToPage=\"1\"/></sheetPr>" +
             "<sheetViews><sheetView showGridLines=\"0\" rightToLeft=\"1\" view=\"pageLayout\" zoomScale=\"85\" workbookViewId=\"0\"/></sheetViews>" +
             "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"5\" hidden=\"1\" outlineLevel=\"1\"><c r=\"A5\"><v>5</v></c></row></sheetData>" +
             "<printOptions gridLines=\"1\" headings=\"1\" horizontalCentered=\"1\"/>" +
             "<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/>" +
             "<pageSetup paperSize=\"9\" scale=\"80\" fitToWidth=\"1\" fitToHeight=\"0\" firstPageNumber=\"3\" useFirstPageNumber=\"1\" " +
             "pageOrder=\"overThenDown\" orientation=\"landscape\" blackAndWhite=\"1\" cellComments=\"atEnd\" errors=\"dash\" horizontalDpi=\"600\" verticalDpi=\"600\" copies=\"2\"/>" +
             "<headerFooter differentFirst=\"1\" scaleWithDoc=\"0\">" +
             "<oddHeader>&amp;L&amp;\"Arial,Bold\"Apulki  Medical&amp;CPage &amp;P of &amp;N</oddHeader>" +
             "<oddFooter>&amp;RPrinted &amp;D</oddFooter><firstHeader>&amp;CCover</firstHeader></headerFooter>" +
             "<rowBreaks count=\"2\" manualBreakCount=\"2\"><brk id=\"20\" max=\"16383\" man=\"1\"/><brk id=\"45\" max=\"16383\" man=\"1\"/></rowBreaks>" +
             "<colBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"6\" max=\"1048575\" man=\"1\"/></colBreaks>",
             (string?)null),
            ("Hidden", "<sheetData/><pageSetup orientation=\"portrait\"/>", "hidden"),
        },
        workbookExtra:
            "<definedNames>" +
            "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">Report!$A$1:$H$40,Report!$J$1:$K$5</definedName>" +
            "<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">Report!$A:$B,Report!$1:$2</definedName>" +
            "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"1\">Hidden!$C$3:$D$4</definedName>" +
            "<definedName name=\"TaxRate\">0.18</definedName>" +
            "</definedNames>");

        Check("page setup: paper, orientation, scale, fit, numbering, flags", () =>
        {
            var p = Workbook.Load(pagePath).Sheets[0].PageSetup;
            True(p.IsDefined, "defined");
            Eq(9, p.PaperSizeCode ?? -1, "paper code");
            Eq("A4", p.Paper?.Name ?? "?", "paper name");
            Eq(210.0, p.Paper!.WidthMm, "A4 width");
            Eq(PageOrientation.Landscape, p.Orientation, "orientation");
            Eq(80, p.Scale, "scale");
            True(p.FitToPage, "fitToPage");
            Eq(1, p.FitToWidth, "fit width"); Eq(0, p.FitToHeight, "fit height auto");
            Eq(3, p.FirstPageNumber, "first page"); True(p.UseFirstPageNumber, "use first page");
            Eq(PageOrder.OverThenDown, p.PageOrder, "page order");
            True(p.BlackAndWhite && !p.Draft, "b/w, draft");
            Eq(PrintCellComments.AtEnd, p.CellComments, "comments");
            Eq(PrintErrors.Dash, p.Errors, "errors");
            Eq(600, p.HorizontalDpi ?? 0, "dpi"); Eq(2, p.Copies, "copies");
            True(p.PrintGridlines && p.PrintHeadings && p.CenterHorizontally && !p.CenterVertically, "print options");
        });

        Check("page setup: margins and header/footer text (incl. double space and codes)", () =>
        {
            var p = Workbook.Load(pagePath).Sheets[0].PageSetup;
            var m = p.Margins;
            True(m.IsDefined, "margins defined");
            Eq(0.25, m.Left, "left"); Eq(0.5, m.Top, "top"); Eq(0.2, m.Header, "header"); Eq(0.2, m.Footer, "footer");
            var h = p.HeaderFooter;
            Eq("&L&\"Arial,Bold\"Apulki  Medical&CPage &P of &N", h.OddHeader ?? "null", "odd header verbatim");
            Eq("&RPrinted &D", h.OddFooter ?? "null", "odd footer");
            Eq("&CCover", h.FirstHeader ?? "null", "first header");
            True(h.EvenHeader is null, "no even header");
            True(h.DifferentFirst && !h.DifferentOddEven && !h.ScaleWithDocument && h.AlignWithMargins, "flags");
        });

        Check("page setup: breaks, print area, print titles", () =>
        {
            var p = Workbook.Load(pagePath).Sheets[0].PageSetup;
            Eq(2, p.RowBreaks.Count, "row breaks");
            Eq(new PageBreak(20, 0, 16383, true), p.RowBreaks[0], "row break 1");
            Eq(45, p.RowBreaks[1].Id, "row break 2");
            Eq(new PageBreak(6, 0, 1048575, true), p.ColumnBreaks.Single(), "column break");
            Eq("Report!$A$1:$H$40,Report!$J$1:$K$5", p.PrintAreaFormula ?? "null", "print area formula");
            True(p.PrintAreas.SequenceEqual(new[] { "A1:H40", "J1:K5" }), "print areas: " + string.Join(";", p.PrintAreas));
            Eq((1, 2), p.PrintTitleRows ?? (0, 0), "title rows");
            Eq((1, 2), p.PrintTitleColumns ?? (0, 0), "title columns");
        });

        Check("page setup: view flags, outline, hidden rows, sheet visibility, scoped names", () =>
        {
            var wb = Workbook.Load(pagePath);
            var s = wb.Sheets[0];
            True(!s.ShowGridLines && s.RightToLeft, "gridlines off, RTL");
            Eq(SheetViewMode.PageLayout, s.ViewMode, "view mode");
            Eq(85, s.ZoomScale, "zoom");
            True(!s.Format.OutlineSummaryBelow && s.Format.OutlineSummaryRight, "outline summary flags");
            True(s.Rows[5].Hidden && s.Rows[5].OutlineLevel == 1, "hidden grouped row");
            Eq(SheetVisibility.Visible, s.Visibility, "visible");
            var hidden = wb.Sheets[1];
            Eq(SheetVisibility.Hidden, hidden.Visibility, "hidden sheet");
            True(hidden.PageSetup.PrintAreas.SequenceEqual(new[] { "C3:D4" }), "second sheet's own print area");
            True(hidden.PageSetup.PrintTitleRows is null, "no titles on second sheet");
            Eq(PageOrientation.Portrait, hidden.PageSetup.Orientation, "portrait");
            Eq(4, wb.LoadedDefinedNames.Count, "all names listed");
            // Since 1.5.0 names load into the model (print names stay in PageSetup).
            True(wb.DefinedNames.Count == 1 && wb.DefinedNames.TryGet("TaxRate", out var tax) && tax == "0.18", "TaxRate loaded; print names not duplicated");
        });

        Check("page setup: in-memory sheet reports Excel defaults", () =>
        {
            var p = new Workbook().AddSheet("N").PageSetup;
            True(!p.IsDefined && p.PaperSizeCode is null && p.Orientation == PageOrientation.Default, "unset");
            Eq(100, p.Scale, "scale"); Eq(0.7, p.Margins.Left, "left margin"); Eq(0.75, p.Margins.Top, "top margin");
            True(p.RowBreaks.Count == 0 && p.PrintAreas.Count == 0, "no breaks / areas");
        });

        Check("page setup: odd header/footer round-trip through save", () =>
        {
            var saved = Path.Combine(outputDir, "read140_pagesetup_resaved.xlsx");
            Workbook.Load(pagePath).SaveTo(saved);
            var back = Workbook.Load(saved).Sheets[0];
            Eq("&L&\"Arial,Bold\"Apulki  Medical&CPage &P of &N", back.PageSetup.HeaderFooter.OddHeader ?? "null", "header survives");
            Eq("&RPrinted &D", back.PageHeader is null ? "null" : back.PageFooter ?? "null", "footer survives");
        });

        // ---- 1904 date system ---------------------------------------------------------------

        var d1904 = Path.Combine(outputDir, "read140_date1904.xlsx");
        Package.Write(d1904, new[]
        {
            ("Mac", "<sheetData><row r=\"1\"><c r=\"A1\" s=\"1\"><v>0</v></c><c r=\"B1\" s=\"1\"><v>43538</v></c></row></sheetData>", (string?)null),
        },
        styles:
            "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>",
        workbookPr: "<workbookPr date1904=\"1\"/>");

        Check("1904 date system: values and display text", () =>
        {
            using (var r = XlsxReader.Open(d1904, new XlsxReaderOptions { TreatFirstRowAsHeaders = false }))
            {
                True(r.Date1904, "flag");
                var row = r.Sheet(1).Rows.First();
                Eq(new DateTime(1904, 1, 1), (DateTime)row[0]!, "serial 0");
                Eq(new DateTime(2023, 3, 15), (DateTime)row[1]!, "serial 43538 (1904) = 2023-03-15");
                Eq("03-15-23", row.GetDisplayString(1), "display (built-in 14)");
            }
            var wb = Workbook.Load(d1904);
            True(wb.Date1904, "model flag");
            Eq("03-15-23", wb.Sheets[0].Cell("B1").DisplayText, "model display");
        });

        Check("1900 system: serials before March 1900 convert exactly", () =>
        {
            var p = Path.Combine(outputDir, "read140_early.xlsx");
            Package.Write(p, new[] { ("Early", "<sheetData><row r=\"1\"><c r=\"A1\" s=\"1\"><v>1</v></c><c r=\"B1\" s=\"1\"><v>59</v></c><c r=\"C1\" s=\"1\"><v>60</v></c></row></sheetData>", (string?)null) },
                styles: "<fonts count=\"1\"><font/></fonts><fills count=\"1\"><fill/></fills><borders count=\"1\"><border/></borders>" +
                        "<cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"2\"><xf xfId=\"0\"/><xf numFmtId=\"14\" xfId=\"0\"/></cellXfs>");
            using var r = XlsxReader.Open(p, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var row = r.Sheet(1).Rows.First();
            Eq(new DateTime(1900, 1, 1), (DateTime)row[0]!, "serial 1");
            Eq(new DateTime(1900, 2, 28), (DateTime)row[1]!, "serial 59");
            Eq(60.0, (double)row[2]!, "serial 60 (29-Feb-1900) stays a number");
            Eq("02-29-00", row.GetDisplayString(2), "…but displays as Excel does");
        });

        Console.WriteLine();
        Console.WriteLine($"[ReadSafetyTest] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"ReadSafetyTest: {fail} check(s) failed.");
    }

    /// <summary>Read-only, non-seekable wrapper (simulates a network stream).</summary>
    private sealed class NonSeekable : Stream
    {
        private readonly Stream _s;
        public NonSeekable(Stream s) { _s = s; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _s.Read(buffer, offset, count);
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool d) { if (d) _s.Dispose(); base.Dispose(d); }
    }

    /// <summary>Writes a raw SpreadsheetML package from part XML, the way Excel lays it out.</summary>
    internal static class Package
    {
        public static void Write(string path,
            (string Name, string SheetXml, string? State)[] sheets,
            string? styles = null,
            string[]? sharedStrings = null,
            string? workbookPr = null,
            string? workbookExtra = null,
            string mainContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            (string Path, string ContentType, string RelType, byte[] Data)[]? extraParts = null)
        {
            if (File.Exists(path)) File.Delete(path);
            using var fs = new FileStream(path, FileMode.CreateNew);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            var ct = new StringBuilder();
            ct.Append($"<Override PartName=\"/xl/workbook.xml\" ContentType=\"{mainContentType}\"/>");
            var wbRels = new StringBuilder();
            var sheetList = new StringBuilder();
            for (int i = 0; i < sheets.Length; i++)
            {
                int n = i + 1;
                ct.Append($"<Override PartName=\"/xl/worksheets/sheet{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                wbRels.Append($"<Relationship Id=\"rId{n}\" Type=\"{R}/worksheet\" Target=\"worksheets/sheet{n}.xml\"/>");
                var state = sheets[i].State is { } st ? $" state=\"{st}\"" : "";
                sheetList.Append($"<sheet name=\"{sheets[i].Name}\" sheetId=\"{n}\"{state} r:id=\"rId{n}\"/>");
                Add(zip, $"xl/worksheets/sheet{n}.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\">{sheets[i].SheetXml}</worksheet>");
            }
            if (styles is not null)
            {
                ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                wbRels.Append($"<Relationship Id=\"rIdS\" Type=\"{R}/styles\" Target=\"styles.xml\"/>");
                Add(zip, "xl/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"{Main}\">{styles}</styleSheet>");
            }
            if (sharedStrings is not null)
            {
                ct.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
                wbRels.Append($"<Relationship Id=\"rIdSS\" Type=\"{R}/sharedStrings\" Target=\"sharedStrings.xml\"/>");
                Add(zip, "xl/sharedStrings.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"{Main}\">" +
                    string.Concat(sharedStrings.Select(x => $"<si><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(x)}</t></si>")) + "</sst>");
            }
            foreach (var (p, ctype, relType, data) in extraParts ?? Array.Empty<(string, string, string, byte[])>())
            {
                ct.Append($"<Override PartName=\"/{p}\" ContentType=\"{ctype}\"/>");
                wbRels.Append($"<Relationship Id=\"rIdX{p.GetHashCode() & 0xFFFF}\" Type=\"{relType}\" Target=\"{p.Substring(3)}\"/>");
                var e = zip.CreateEntry(p);
                using var es = e.Open();
                es.Write(data, 0, data.Length);
            }

            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" + ct + "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"{Main}\" xmlns:r=\"{R}\">{workbookPr}<sheets>{sheetList}</sheets>{workbookExtra}</workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + wbRels + "</Relationships>");
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            var bytes = new UTF8Encoding(false).GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }
}
