using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.7.0, the Layout chat's requests: rich-text runs, hyperlinks, repeat-fill position,
/// lenient part names (shared OoxmlPackage), Strict OOXML, cells outside the grid,
/// number-format speed, comments, header/footer pictures, chart sheets, and the shared-crypto
/// spin-count cap and cancellation. Fixtures are built here, shaped like Excel's (and some
/// third-party writers') output.
/// </summary>
internal static class Layout17Test
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private const string StrictR = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[Layout17Test] 1.7.0 Layout requests...");
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

        // ---- B4: lenient part names ---------------------------------------------------------

        Check("B4: lower-case [content_types].xml, '\\' separators, absolute targets and case-mismatched names load", () =>
        {
            var p = Path.Combine(outputDir, "f170_partnames.xlsx");
            WriteZip(p, new()
            {
                ["[content_types].xml"] = ContentTypes(("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                    ("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                    ("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"),
                    ("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                ["_rels\\.rels"] = Rels(("rId1", R + "/officeDocument", "/xl/workbook.xml")),
                ["xl\\workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"FAM\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
                ["xl\\_rels\\workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "/xl/worksheets/sheet1.xml"), ("rId2", R + "/sharedStrings", "/xl/sharedStrings.xml"), ("rId3", R + "/styles", "/xl/styles.xml")),
                ["xl\\sharedstrings.xml"] = $"<sst xmlns=\"{Main}\"><si><t>Cycle</t></si></sst>",
                ["xl\\styles.xml"] = $"<styleSheet xmlns=\"{Main}\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"11\"/><name val=\"Arial\"/></font></fonts><fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills><borders count=\"1\"><border/></borders><cellXfs count=\"2\"><xf fontId=\"0\"/><xf fontId=\"1\" applyFont=\"1\"/></cellXfs></styleSheet>",
                ["xl\\worksheets\\sheet1.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\" s=\"1\"><v>0</v></c><c r=\"B1\"><v>12.5</v></c></row></sheetData></worksheet>",
            });
            var wb = Workbook.Load(p);
            Eq("FAM", wb.Sheets[0].Name, "sheet");
            Eq("Cycle", wb["FAM"].FindCell("A1")!.Value, "shared string through a lower-case entry");
            Eq(12.5, wb["FAM"].FindCell("B1")!.Value, "number");
            True(wb["FAM"].FindCell("A1")!.Style?.Bold == true, "styles part found through '\\' name");
            using var r = XlsxReader.Open(p, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            Eq("Cycle", r.Sheet(1).Rows.First()[0], "streaming");
        });

        Check("B4: entries that differ only in case: the first wins, nothing throws", () =>
        {
            var p = Path.Combine(outputDir, "f170_caseduplicates.xlsx");
            WriteZip(p, new()
            {
                ["[Content_Types].xml"] = ContentTypes(("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                    ("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
                ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
                ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml")),
                ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData></worksheet>",
                ["XL/Worksheets/Sheet1.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\"><v>2</v></c></row></sheetData></worksheet>",
            });
            Eq(1.0, Workbook.Load(p)["S"].FindCell("A1")!.Value, "first entry");
        });

        // ---- B5: Strict OOXML -----------------------------------------------------------------

        var strict = Path.Combine(outputDir, "f170_strict.xlsx");
        WriteStrictFixture(strict);

        Check("B5: Strict workbook: sheets, shared strings, styles, formulas, hyperlinks, drawing r:ids", () =>
        {
            var wb = Workbook.Load(strict);
            True(wb.IsStrict, "IsStrict");
            Eq(2, wb.Sheets.Count, "sheets");
            var s = wb["Data"];
            Eq("Lorem", s.FindCell("A1")!.Value, "shared string");
            Eq(111.0, s.FindCell("B1")!.Value, "number");
            Eq("SUM(B1:B2)", s.FindCell("B3")!.Formula, "formula");
            Eq(333.0, s.FindCell("B3")!.CachedValue, "cached result");
            True(s.FindCell("A1")!.Style?.Bold == true, "strict styles");
            Eq(1, s.Hyperlinks.Count, "hyperlink with strict r:id");
            Eq("https://example.org/strict", s.Hyperlinks[0].Target, "hyperlink target");
            Eq("Other", wb.Sheets[1].Name, "second sheet");
            Eq(1, s.Drawings.Count, "strict drawing part");
            True(s.Drawings[0].Kind == DrawingKind.Shape && s.Drawings[0].Text == "Strict shape", "drawing namespaces mapped");
            using var r = XlsxReader.Open(strict, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            True(r.IsStrict, "XlsxReader.IsStrict");
            Eq("Lorem", r.Sheet("Data").Rows.First()[0], "streaming");
        });

        Check("B5: load → save writes Transitional that reloads with the same values", () =>
        {
            var o = Path.Combine(outputDir, "f170_strict_resaved.xlsx");
            Workbook.Load(strict).SaveTo(o);
            var wb = Workbook.Load(o);
            True(!wb.IsStrict, "saved as Transitional");
            Eq("Lorem", wb["Data"].FindCell("A1")!.Value, "value kept");
            Eq("SUM(B1:B2)", wb["Data"].FindCell("B3")!.Formula, "formula kept");
            Eq("https://example.org/strict", wb["Data"].Hyperlinks[0].Target, "hyperlink kept");
        });

        // ---- B6: cells outside the grid --------------------------------------------------------

        Check("B6: rows and cells outside the grid are left out and reported; nothing throws", () =>
        {
            var p = Path.Combine(outputDir, "f170_outofgrid.xlsx");
            ReadSafetyTest.Package.Write(p, new (string, string, string?)[]
            {
                ("G", "<sheetData>" +
                      "<row r=\"0\"><c r=\"A0\" t=\"inlineStr\"><is><t>zero</t></is></c><c r=\"B0\"><v>1</v></c></row>" +
                      "<row r=\"1\"><c r=\"A1\"><v>10</v></c><c r=\"XFD1\"><v>11</v></c><c r=\"XFE1\"><v>12</v></c><c r=\"1\"><v>13</v></c></row>" +
                      "<row r=\"2\"><c r=\"ZZZZZZZZZZZZZ2\"><v>14</v></c><c r=\"B2\"><v>15</v></c></row>" +
                      "<row r=\"1048576\"><c r=\"C1048576\"><v>16</v></c></row>" +
                      "<row r=\"1048577\"><c r=\"A1048577\"><v>17</v></c></row>" +
                      "</sheetData>", null),
            });
            var wb = Workbook.Load(p);
            var s = wb["G"];
            Eq(4, s.PopulatedCells.Count(), "cells kept");
            Eq(11.0, s.FindCell(1, 16384)!.Value, "XFD kept");
            Eq(16.0, s.FindCell(1048576, 3)!.Value, "last row kept");
            foreach (var c in s.PopulatedCells) s.GetEffectiveStyle(c.Row, c.Column);
            Eq(1, wb.LoadWarnings.Count, "one warning");
            var w = wb.LoadWarnings[0];
            True(w.Kind == LoadWarningKind.CellsOutsideGrid && w.SheetName == "G" && w.Count == 6 && w.Reference == "A0", $"warning: {w}");

            using var r = XlsxReader.Open(p, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var sr = r.Sheet(1);
            var rows = sr.Rows.Select(x => x.RowNumber).ToList();
            True(rows.SequenceEqual(new[] { 1, 2, 1048576 }), "streamed rows: " + string.Join(",", rows));
            Eq(2L, sr.OutOfGridRows, "rows dropped");
            Eq(6L, sr.OutOfGridCells, "cells dropped");
            Eq("A0", sr.FirstOutOfGridReference, "first reference");
            Eq(0, Workbook.Load(Path.Combine(outputDir, "f170_strict.xlsx")).LoadWarnings.Count, "clean file: no warnings");
        });

        // ---- B3: fill position ---------------------------------------------------------------

        Check("B3: FillIndex for accounting, leading and trailing fills, dates, text and negatives", () =>
        {
            const string acct = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";
            var f = NumberFormatter.FormatDetailed(1234.5, acct);
            True(f.Text == " $1,234.50 " && f.FillCharacter == ' ' && f.FillIndex == 2, $"positive: [{f.Text}] {f.FillIndex}");
            f = NumberFormatter.FormatDetailed(-1234.5, acct);
            True(f.Text == " $(1,234.50)" && f.FillIndex == 2, $"negative: [{f.Text}] {f.FillIndex}");
            f = NumberFormatter.FormatDetailed(0, acct);
            True(f.Text == " $-   " && f.FillIndex == 2, $"zero: [{f.Text}] {f.FillIndex}");
            f = NumberFormatter.FormatDetailed("abc", acct);
            True(f.Text == " abc " && f.FillCharacter is null && f.FillIndex is null, "text section without fill");
            f = NumberFormatter.FormatDetailed(-5, "* #,##0");
            True(f.Text == "-5" && f.FillIndex == 1, "minus stays left of the fill");
            f = NumberFormatter.FormatDetailed(5, "0*-");
            True(f.Text == "5" && f.FillCharacter == '-' && f.FillIndex == 1, "trailing fill");
            f = NumberFormatter.FormatDetailed(45000, "dd/mm/yyyy* ");
            True(f.Text == "15/03/2023" && f.FillIndex == 10, "date fill");
            f = NumberFormatter.FormatDetailed("t", "@*.");
            True(f.Text == "t" && f.FillCharacter == '.' && f.FillIndex == 1, "text fill");
            f = NumberFormatter.FormatDetailed(0.5, "# ?/?*x");
            True(f.Text == " 1/2" && f.FillIndex == 4, "fraction fill");
            f = NumberFormatter.FormatDetailed(5, "#,##0");
            True(f.FillIndex is null && f.FillCharacter is null, "no fill");
            var (text, color, fill, isDate) = NumberFormatter.FormatDetailed(1, "0*x");
            True(text == "1" && fill == 'x' && color is null && !isDate, "positional deconstruction unchanged");
        });

        // ---- B7: speed ---------------------------------------------------------------------

        Check("B7: number formatting: identical text, few allocations, timing printed", () =>
        {
            var inr = new NumberFormatOptions { Culture = CultureInfo.GetCultureInfo("en-IN") };
            var de = new NumberFormatOptions { Culture = CultureInfo.GetCultureInfo("de-DE") };
            Eq("1,234,567.89", NumberFormatter.Format(1234567.891, "#,##0.00"), "en-US");
            Eq("12,34,567.89", NumberFormatter.Format(1234567.891, "#,##0.00", inr), "en-IN groups 3;2");
            Eq("1.234.567,89", NumberFormatter.Format(1234567.891, "#,##0.00", de), "de-DE");
            Eq("-0.01", NumberFormatter.Format(-0.005, "0.00"), "half away from zero");
            Eq("0.00", NumberFormatter.Format(-0.004, "0.00"), "rounded zero: no minus");
            Eq("1.23E+05", NumberFormatter.Format(123456, "0.00E+00"), "scientific");
            Eq("12,345.7", NumberFormatter.Format(12345.67, "#,##0.0"), "grouping one decimal");
            Eq("1,235K", NumberFormatter.Format(1234567, "#,##0,\"K\""), "scaling comma");
            Eq("1.5 x", NumberFormatter.Format(1.5, "General \"x\""), "General with literal");

            var rnd = new Random(3);
            var vals = new double[100_000];
            for (int i = 0; i < vals.Length; i++) vals[i] = Math.Round((rnd.NextDouble() - 0.3) * Math.Pow(10, rnd.Next(0, 8)), 3);
            foreach (var v in vals) NumberFormatter.Format(v, "#,##0.00");
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            foreach (var v in vals) NumberFormatter.Format(v, "#,##0.00");
            sw.Stop();
            long perValue = (GC.GetAllocatedBytesForCurrentThread() - before) / vals.Length;
            Console.WriteLine($"          (100k values with \"#,##0.00\": {sw.Elapsed.TotalMilliseconds:0} ms, {perValue} bytes allocated per value; 1.6.1: ~150 ms, ~1,650 bytes)");
            True(perValue < 200, $"allocations per value: {perValue}");
        });

        // ---- B1: rich text -------------------------------------------------------------------

        var rich = Path.Combine(outputDir, "f170_richtext.xlsx");
        WriteRichFixture(rich);

        Check("B1: rich shared and inline strings: runs, every font property, theme colour resolved, phonetic runs skipped", () =>
        {
            var wb = Workbook.Load(rich);
            var s = wb["R"];
            var a1 = s.FindCell("A1")!;
            Eq("Bold red then plain", a1.Value, "plain text unchanged");
            var rt = a1.RichText!;
            Eq(3, rt.Runs.Count, "runs");
            var f0 = rt.Runs[0].Font!;
            True(rt.Runs[0].Text == "Bold red" && f0.Bold == true && f0.Italic is null && f0.Size == 14 && f0.Name == "Arial"
                 && f0.Color!.Kind == ColorKind.Rgb && rt.Runs[0].ResolvedColor == "FF0000", "run 0");
            True(rt.Runs[1].Text == " then " && rt.Runs[1].Font is null && rt.Runs[1].ResolvedColor is null, "run 1: no rPr");
            var f2 = rt.Runs[2].Font!;
            True(f2.Underline == UnderlineStyle.Double && f2.Strikethrough == true && f2.VerticalAlign == FontVerticalAlign.Superscript
                 && f2.Family == 2 && f2.Charset == 0 && f2.Scheme == FontScheme.Minor && f2.Bold == false
                 && f2.Color!.Kind == ColorKind.Theme && f2.Color.Theme == 4 && rt.Runs[2].ResolvedColor == "4472C4", $"run 2: {f2}");
            Eq("Plain", s.FindCell("A2")!.Value, "plain shared string");
            True(s.FindCell("A2")!.RichText is null, "plain has no runs");
            var a3 = s.FindCell("A3")!;
            True(a3.Value as string == "漢字です" && a3.RichText!.Runs.Count == 2, $"phonetic runs skipped: {a3.Value}");
            var b1 = s.FindCell("B1")!;
            True(b1.Value as string == "inline rich" && b1.RichText!.Runs[1].Font!.Italic == true, "inline rich string");

            using var r = XlsxReader.Open(rich, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var row = r.Sheet(1).Rows.First();
            True(row.GetRichText(0)!.Equals(rt) && row.GetRichText(1) is not null && row.GetRichText(5) is null, "streaming GetRichText");
            Eq("Bold red then plain", row[0], "streaming plain value");
        });

        Check("B1: rich text round-trips (shared and inline strings) and can be built in code", () =>
        {
            var wb = Workbook.Load(rich);
            var o = Path.Combine(outputDir, "f170_richtext_resaved.xlsx");
            wb.SaveTo(o);
            var back = Workbook.Load(o)["R"];
            True(back.FindCell("A1")!.RichText!.Equals(wb["R"].FindCell("A1")!.RichText), "A1 runs kept");
            True(back.FindCell("B1")!.RichText!.Equals(wb["R"].FindCell("B1")!.RichText), "inline runs kept");
            Eq("Plain", back.FindCell("A2")!.Value, "plain kept");

            var built = new RichText(new RichTextRun("Total: ", new RichTextFont { Bold = true }),
                                     new RichTextRun("42", new RichTextFont { Color = ColorRef.FromRgb("FF00B050"), Size = 12 }));
            foreach (bool inline in new[] { false, true })
            {
                var p = Path.Combine(outputDir, inline ? "f170_rich_inline.xlsx" : "f170_rich_shared.xlsx");
                using (var w = XlsxWriter.Create(p, new XlsxWriterOptions { UseInlineStrings = inline }))
                {
                    w.AddSheet("W").WriteRow(built, "plain", built);
                    w.Save();
                }
                var c = Workbook.Load(p)["W"];
                True(c.FindCell("A1")!.RichText!.Equals(built) && c.FindCell("C1")!.RichText!.Equals(built), $"built runs ({(inline ? "inline" : "shared")})");
                Eq("Total: 42", c.FindCell("A1")!.Value, "value is the text");
                Eq("00B050", c.FindCell("A1")!.RichText!.Runs[1].ResolvedColor, "colour resolved on read");
            }

            var m = new Workbook();
            var cell = m.AddSheet("M").Cell("A1");
            cell.Value = built;
            True(cell.RichText == built && (string)cell.Value! == "Total: 42", "Value = RichText sets the runs");
            cell.Value = "x";
            True(cell.RichText is null, "a plain value clears the runs");
            Throws<ArgumentException>(() => new RichText(Array.Empty<RichTextRun>()), "empty runs");
        });

        // ---- B2: hyperlinks --------------------------------------------------------------------

        var links = Path.Combine(outputDir, "f170_hyperlinks.xlsx");
        WriteHyperlinkFixture(links);

        Check("B2: hyperlinks: external, internal, both, ranges, tooltip, display, missing relationship", () =>
        {
            var s = Workbook.Load(links)["L"];
            Eq(5, s.Hyperlinks.Count, "links");
            var h = s.Hyperlinks;
            True(h[0].Reference == "A1" && h[0].Target == "https://example.com/a?b=1&c=2" && h[0].Tooltip == "Tip" && !h[0].IsInternal, "external");
            True(h[1].Target is null && h[1].Location == "'Other Sheet'!B5" && h[1].Display == "jump" && h[1].IsInternal, "internal");
            True(h[2].Target == "report.xlsx" && h[2].Location == "Summary!A1", "file plus location");
            True(h[3].Reference == "B3:D4" && h[3].FirstRow == 3 && h[3].LastColumn == 4 && h[3].Contains(4, 3) && !h[3].Contains(5, 3), "range");
            True(h[4].Target is null && h[4].Location is null, "missing relationship kept as stored");
            Eq(h[3], s.FindHyperlink(3, 2), "FindHyperlink");
            using var r = XlsxReader.Open(links);
            var lay = r.Sheet(1).ReadLayout();
            Eq(5, lay.Hyperlinks.Count, "streaming layout");
            Eq("mailto:x@y.org", lay.Hyperlinks[3].Target, "streaming target");
        });

        Check("B2: hyperlinks round-trip through the model and can be added on the streaming writer", () =>
        {
            var wb = Workbook.Load(links);
            wb["L"].AddHyperlink("E9", location: "L!A1", tooltip: "back");
            var o = Path.Combine(outputDir, "f170_hyperlinks_resaved.xlsx");
            wb.SaveTo(o);
            var h = Workbook.Load(o)["L"].Hyperlinks;
            Eq(5, h.Count, "links after save (the one without a target or location is not written)");
            True(h[0].Target == "https://example.com/a?b=1&c=2" && h[0].Tooltip == "Tip", "external kept");
            True(h[1].Location == "'Other Sheet'!B5" && h[1].Target is null && h[1].Display == "jump", "internal kept");
            True(h[2].Target == "report.xlsx" && h[2].Location == "Summary!A1", "both kept");
            True(h[3].Reference == "B3:D4" && h[3].Target == "mailto:x@y.org", "range kept");
            True(h[4].Reference == "E9" && h[4].Location == "L!A1", "added");

            var p = Path.Combine(outputDir, "f170_hyperlinks_streamed.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var sh = w.AddSheet("S");
                sh.WriteRow("a", "b");
                sh.AddHyperlink(new Hyperlink("A1:B1", "https://example.net/x"));
                sh.AddHyperlink(new Hyperlink("A2", location: "S!B1"));
                w.Save();
            }
            var sl = Workbook.Load(p)["S"].Hyperlinks;
            True(sl.Count == 2 && sl[0].Reference == "A1:B1" && sl[0].Target == "https://example.net/x" && sl[1].Location == "S!B1", "streamed links");
            Throws<ArgumentException>(() => new Hyperlink("A1"), "a link needs a target or location");
        });

        // ---- B8: comments ----------------------------------------------------------------------

        var notes = Path.Combine(outputDir, "f170_comments.xlsx");
        WriteCommentFixture(notes);

        Check("B8: comments: author, runs, anchors and visibility from Excel-style VML", () =>
        {
            var s = Workbook.Load(notes)["N"];
            Eq(2, s.Comments.Count, "comments");
            var c0 = s.Comments[0];
            True(c0.Reference == "B1" && c0.Row == 1 && c0.Column == 2 && c0.Author == "Arun" && c0.Text == "Arun:\nfirst note", $"c0: {c0}");
            True(c0.RichText!.Runs.Count == 2 && c0.RichText.Runs[0].Font!.Bold == true, "runs");
            Eq(new CommentAnchor(3, 15, 1, 2, 5, 15, 5, 8), c0.Anchor!.Value, "anchor (1-based)");
            True(!c0.Visible, "hidden");
            var c1 = s.Comments[1];
            True(c1.Reference == "C4" && c1.Author == "Bob" && c1.Visible && c1.Text == "plain", "c1 visible");
            using var r = XlsxReader.Open(notes);
            var sc = r.Sheet(1).ReadComments();
            True(sc.Count == 2 && sc[1].Visible && sc[0].Anchor == c0.Anchor, "streaming");
        });

        Check("B8: comments round-trip with runs, anchors and visibility; AddComment", () =>
        {
            var wb = Workbook.Load(notes);
            wb["N"].AddComment("A9", "Me", "added");
            var o = Path.Combine(outputDir, "f170_comments_resaved.xlsx");
            wb.SaveTo(o);
            var c = Workbook.Load(o)["N"].Comments;
            Eq(3, c.Count, "comments");
            True(c[0].Text == "Arun:\nfirst note" && c[0].RichText!.Runs[0].Font!.Bold == true, "runs kept");
            Eq(new CommentAnchor(3, 15, 1, 2, 5, 15, 5, 8), c[0].Anchor!.Value, "anchor kept");
            True(c[1].Visible && !c[0].Visible, "visibility kept");
            True(c[2].Reference == "A9" && c[2].Author == "Me" && c[2].Text == "added", "added");
        });

        // ---- B8: header/footer pictures -----------------------------------------------------

        var hf = Path.Combine(outputDir, "f170_hfpictures.xlsx");
        WriteHeaderFooterFixture(hf);

        Check("B8: header/footer pictures (&G): position, size, image bytes; model and streaming", () =>
        {
            var s = Workbook.Load(hf).Sheets[0];
            Eq("&C&G", s.PageSetup.HeaderFooter.OddHeader, "header text");
            var p = s.HeaderFooterPictures;
            Eq(3, p.Count, "pictures");
            True(p[0].Id == "CH" && p[0].Section == HeaderFooterSection.Center && p[0].Part == HeaderFooterPart.Header && p[0].Kind == HeaderFooterPageKind.Odd, "CH");
            True(p[0].WidthPoints == 192.75 && p[0].HeightPoints == 63 && p[0].Title == "logo", "size and title");
            True(p[0].ImagePartName == "/xl/media/image1.png" && p[0].ContentType == "image/png" && p[0].GetImageBytes()!.Length == Png.Length, "bytes");
            True(p[1].Id == "LF" && p[1].WidthPoints == 36 && p[1].HeightPoints == 18, "px converted to points");
            True(p[2].Id == "RHEVEN" && p[2].Kind == HeaderFooterPageKind.Even && p[2].Section == HeaderFooterSection.Right, "even-page picture");
            using var r = XlsxReader.Open(hf);
            var sp = r.Sheet(1).ReadHeaderFooterPictures();
            True(sp.Count == 3 && sp[0].GetImageBytes()!.SequenceEqual(Png), "streaming bytes on demand");
        });

        // ---- B8: chart sheets ----------------------------------------------------------------

        var cs = Path.Combine(outputDir, "f170_chartsheet.xlsx");
        WriteChartSheetFixture(cs);

        Check("B8: chart sheets: name, tab position, page setup, view, chart metadata; model and streaming", () =>
        {
            var wb = Workbook.Load(cs);
            Eq(2, wb.Sheets.Count, "worksheets");
            Eq(1, wb.ChartSheets.Count, "chart sheets");
            var c = wb.ChartSheets[0];
            True(c.Name == "Chart1" && c.WorkbookIndex == 1 && c.Visibility == SheetVisibility.Visible, "name and position");
            True(c.PageSetup.Orientation == PageOrientation.Landscape && c.PageSetup.Margins.Left == 0.7 && c.View.ZoomScale == 84, "page setup and view");
            True(c.Drawings.Count == 1 && c.Drawings[0].Anchor.Type == DrawingAnchorType.Absolute && c.Drawings[0].Anchor.WidthEmu == 8697232, "absolute anchor");
            True(c.Chart is { Type: ChartType.Column } && c.Chart.Plots[0].Series.Count == 1 && c.Chart.Title == "Sales", "chart metadata");
            using var r = XlsxReader.Open(cs);
            Eq(1, r.ChartSheets.Count, "streaming list");
            var rc = r.ReadChartSheet(r.ChartSheets[0]);
            True(rc.Chart?.Title == "Sales" && rc.WorkbookIndex == 1, "streaming read");
            Throws<ArgumentException>(() => r.ReadChartSheet(r.Sheets[0]), "a worksheet is not a chart sheet");
            var o = Path.Combine(outputDir, "f170_chartsheet_resaved.xlsx");
            wb.SaveTo(o);
            var back = Workbook.Load(o);
            True(back.Sheets.Count == 2 && back.ChartSheets.Count == 0, "save writes worksheets only (documented)");
        });

        // ---- Model save: rows whose settings make no row format -------------------------------

        Check("fix: load → save of a sheet whose last row has customFormat but no style (57828-like)", () =>
        {
            var p = Path.Combine(outputDir, "f170_customformat_rows.xlsx");
            ReadSafetyTest.Package.Write(p, new (string, string, string?)[]
            {
                ("S", "<sheetData><row r=\"1\" customFormat=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"2\" customFormat=\"1\"/><row r=\"5\" customFormat=\"1\"/></sheetData>", null),
            });
            var o = Path.Combine(outputDir, "f170_customformat_rows_resaved.xlsx");
            Workbook.Load(p).SaveTo(o);
            Eq(1.0, Workbook.Load(o)["S"].FindCell("A1")!.Value, "value");
        });

        // ---- D-25: shared crypto ----------------------------------------------------------------

        // The encrypted fixtures for the timed checks are written to and read from memory, never
        // a file. On Windows, real-time antivirus (Microsoft Defender) scans an encrypted Office
        // file when it is opened by trying the default "VelvetSweatshop" password, which runs the
        // whole key derivation at the file's declared spin count inside the OS file-open call,
        // before any Chuvadi code runs. Measured: 12.8 s for a 3,000,000-spin file on first open,
        // 9 ms once cached. A file-based timing check would measure the scanner, not this library.
        static byte[] EncryptedBytes(int spinCount, params object?[] row)
        {
            using var ms = new MemoryStream();
            using (var w = XlsxWriter.Create(ms, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = "pw", SpinCount = spinCount } }))
            {
                w.AddSheet("E").WriteRow(row);
                w.Save();
            }
            return ms.ToArray();
        }
        static MemoryStream Mem(byte[] b) => new MemoryStream(b, writable: false);

        var enc = EncryptedBytes(600_000, "secret", 1);
        File.WriteAllBytes(Path.Combine(outputDir, "f170_encrypted_spin.xlsx"), enc);   // for inspection only; not read back

        Check("D-25: MaxSpinCount (default 500,000) rejects a higher declared spin count before key derivation", () =>
        {
            Eq(500_000, new XlsxReaderOptions().MaxSpinCount, "default");
            Eq(500_000, XlsxReaderOptions.ForUntrustedInput().MaxSpinCount, "untrusted");
            var sw = Stopwatch.StartNew();
            var ex = Throws<XlsxLimitExceededException>(() => Workbook.Load(Mem(enc), "pw", null), "default cap");
            sw.Stop();
            True(ex.LimitName == "MaxSpinCount" && ex.Limit == 500_000 && ex.Actual == 600_000, ex.Message);
            True(sw.ElapsedMilliseconds < 2000, $"rejected in {sw.ElapsedMilliseconds} ms");
            Throws<XlsxLimitExceededException>(() => XlsxReader.Open(Mem(enc), "pw", new XlsxReaderOptions { MaxSpinCount = 100_000 }).Dispose(), "streaming");
            Eq("secret", Workbook.Load(Mem(enc), "pw", new XlsxReaderOptions { MaxSpinCount = null })["E"].FindCell("A1")!.Value, "unlimited reads it");
            Eq("secret", Workbook.Load(Mem(enc), "pw", new XlsxReaderOptions { MaxSpinCount = 600_000 })["E"].FindCell("A1")!.Value, "equal to the cap reads it");
        });

        Check("D-25: cancellation lands inside key derivation (Workbook.Load and XlsxReader.Open)", () =>
        {
            var big = EncryptedBytes(3_000_000, "x");
            foreach (var model in new[] { true, false })
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                var opts = new XlsxReaderOptions { MaxSpinCount = null, CancellationToken = cts.Token };
                var sw = Stopwatch.StartNew();
                Throws<OperationCanceledException>(() =>
                {
                    if (model) Workbook.Load(Mem(big), "pw", opts);
                    else XlsxReader.Open(Mem(big), "pw", opts).Dispose();
                }, model ? "model" : "streaming");
                sw.Stop();
                True(sw.ElapsedMilliseconds < 1500, $"{(model ? "model" : "streaming")}: stopped in {sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"          ({(model ? "Workbook.Load" : "XlsxReader.Open")}: cancel at 100 ms landed after {sw.ElapsedMilliseconds} ms of a 3,000,000-spin derivation)");
            }
        });

        Check("D-25: SaveAsync's token cancels encryption key derivation", () =>
        {
            var p = Path.Combine(outputDir, "f170_encrypt_cancel.xlsx");
            using var cts = new CancellationTokenSource();
            var w = XlsxWriter.Create(p, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = "pw", SpinCount = 3_000_000 } });
            w.AddSheet("E").WriteRow("x");
            cts.CancelAfter(100);
            var sw = Stopwatch.StartNew();
            Throws<OperationCanceledException>(() => w.SaveAsync(cts.Token).GetAwaiter().GetResult(), "SaveAsync");
            sw.Stop();
            try { w.Dispose(); } catch { /* the writer is unusable after a cancelled save */ }
            True(sw.ElapsedMilliseconds < 1500, $"stopped in {sw.ElapsedMilliseconds} ms");
        });

        Console.WriteLine();
        Console.WriteLine($"  Layout17Test: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"Layout17Test: {fail} check(s) failed.");
    }

    // ---- Fixtures ---------------------------------------------------------------------------

    internal static readonly byte[] Png = MakePng();

    private static byte[] MakePng()
    {
        // A valid 2x1 red PNG.
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAEElEQVR4nGP4z8DAwMDAAAAN/gEBKYY+nAAAAABJRU5ErkJggg==");
    }

    private static string ContentTypes(params (string Part, string Type)[] overrides)
        => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
           "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
           "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"vml\" ContentType=\"application/vnd.openxmlformats-officedocument.vmlDrawing\"/>" +
           string.Concat(overrides.Select(o => $"<Override PartName=\"{o.Part}\" ContentType=\"{o.Type}\"/>")) + "</Types>";

    private static string Rels(params (string Id, string Type, string Target)[] rels)
        => $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"{Pkg}\">" +
           string.Concat(rels.Select(r => $"<Relationship Id=\"{r.Id}\" Type=\"{r.Type}\" Target=\"{System.Security.SecurityElement.Escape(r.Target)}\"" +
                                          (r.Target.Contains(':') || r.Target.EndsWith(".xlsx") ? " TargetMode=\"External\"" : "") + "/>")) +
           "</Relationships>";

    private static void WriteZip(string path, Dictionary<string, object> entries)
    {
        if (File.Exists(path)) File.Delete(path);
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            var bytes = content as byte[] ?? new UTF8Encoding(false).GetBytes((string)content);
            s.Write(bytes, 0, bytes.Length);
        }
    }

    private static readonly (string, string) CtWorkbook = ("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
    private static (string, string) CtSheet(int n) => ($"/xl/worksheets/sheet{n}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
    private static readonly (string, string) CtSst = ("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");
    private static readonly (string, string) CtStyles = ("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

    private static void WriteStrictFixture(string path)
    {
        const string sr = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1), CtSheet(2), CtSst, CtStyles,
                ("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml")),
            ["_rels/.rels"] = Rels(("rId1", sr + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{StrictMain}\" xmlns:r=\"{StrictR}\" conformance=\"strict\"><workbookPr dateCompatibility=\"0\"/><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Other\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", sr + "/worksheet", "worksheets/sheet1.xml"), ("rId2", sr + "/worksheet", "worksheets/sheet2.xml"),
                ("rId3", sr + "/sharedStrings", "sharedStrings.xml"), ("rId4", sr + "/styles", "styles.xml")),
            ["xl/sharedStrings.xml"] = $"<sst xmlns=\"{StrictMain}\"><si><t>Lorem</t></si><si><t>ipsum</t></si></sst>",
            ["xl/styles.xml"] = $"<styleSheet xmlns=\"{StrictMain}\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills><borders count=\"1\"><border/></borders><cellXfs count=\"2\"><xf fontId=\"0\"/><xf fontId=\"1\" applyFont=\"1\"/></cellXfs></styleSheet>",
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{StrictMain}\" xmlns:r=\"{StrictR}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\" s=\"1\"><v>0</v></c><c r=\"B1\"><v>111</v></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"B2\"><v>222</v></c></row><row r=\"3\"><c r=\"B3\"><f>SUM(B1:B2)</f><v>333</v></c></row></sheetData>" +
                "<hyperlinks><hyperlink ref=\"A1\" r:id=\"rIdH\"/></hyperlinks><drawing r:id=\"rIdD\"/></worksheet>",
            ["xl/worksheets/_rels/sheet1.xml.rels"] = Rels(("rIdH", sr + "/hyperlink", "https://example.org/strict"), ("rIdD", sr + "/drawing", "../drawings/drawing1.xml")),
            ["xl/worksheets/sheet2.xml"] = $"<worksheet xmlns=\"{StrictMain}\"><sheetData/></worksheet>",
            ["xl/drawings/drawing1.xml"] = "<xdr:wsDr xmlns:xdr=\"http://purl.oclc.org/ooxml/drawingml/spreadsheetDrawing\" xmlns:a=\"http://purl.oclc.org/ooxml/drawingml/main\">" +
                "<xdr:twoCellAnchor><xdr:from><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>6</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>5</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
                "<xdr:sp><xdr:nvSpPr><xdr:cNvPr id=\"2\" name=\"Shape 1\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst=\"rect\"/></xdr:spPr><xdr:txBody><a:bodyPr/><a:p><a:r><a:t>Strict shape</a:t></a:r></a:p></xdr:txBody></xdr:sp><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>",
        });
    }

    private static void WriteRichFixture(string path)
    {
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1), CtSst, ("/xl/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml")),
            ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"R\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml"), ("rId2", R + "/sharedStrings", "sharedStrings.xml"), ("rId3", R + "/theme", "theme/theme1.xml")),
            ["xl/theme/theme1.xml"] = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\"><a:themeElements><a:clrScheme name=\"Office\">" +
                "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
                "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2><a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
                "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6><a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>" +
                "</a:clrScheme></a:themeElements></a:theme>",
            ["xl/sharedStrings.xml"] = $"<sst xmlns=\"{Main}\">" +
                "<si><r><rPr><b/><sz val=\"14\"/><color rgb=\"FFFF0000\"/><rFont val=\"Arial\"/></rPr><t>Bold red</t></r><r><t xml:space=\"preserve\"> then </t></r>" +
                "<r><rPr><b val=\"0\"/><strike/><u val=\"double\"/><vertAlign val=\"superscript\"/><sz val=\"11\"/><color theme=\"4\"/><rFont val=\"Calibri\"/><family val=\"2\"/><charset val=\"0\"/><scheme val=\"minor\"/></rPr><t>plain</t></r></si>" +
                "<si><t>Plain</t></si>" +
                "<si><r><t>漢字</t></r><r><rPr><sz val=\"11\"/></rPr><t>です</t></r><rPh sb=\"0\" eb=\"2\"><t>カンジ</t></rPh><phoneticPr fontId=\"1\"/></si>" +
                "</sst>",
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><r><t xml:space=\"preserve\">inline </t></r><r><rPr><i/></rPr><t>rich</t></r></is></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c></row><row r=\"3\"><c r=\"A3\" t=\"s\"><v>2</v></c></row></sheetData></worksheet>",
        });
    }

    private static void WriteHyperlinkFixture(string path)
    {
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1), CtSheet(2)),
            ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"L\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Other Sheet\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml"), ("rId2", R + "/worksheet", "worksheets/sheet2.xml")),
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>site</t></is></c></row></sheetData>" +
                "<hyperlinks><hyperlink ref=\"A1\" r:id=\"rId1\" tooltip=\"Tip\"/><hyperlink ref=\"A2\" location=\"'Other Sheet'!B5\" display=\"jump\"/>" +
                "<hyperlink ref=\"A3\" r:id=\"rId3\" location=\"Summary!A1\"/><hyperlink ref=\"B3:D4\" r:id=\"rId2\"/><hyperlink ref=\"E5\" r:id=\"rIdMissing\"/></hyperlinks></worksheet>",
            ["xl/worksheets/_rels/sheet1.xml.rels"] = Rels(("rId1", R + "/hyperlink", "https://example.com/a?b=1&c=2"), ("rId2", R + "/hyperlink", "mailto:x@y.org"), ("rId3", R + "/hyperlink", "report.xlsx")),
            ["xl/worksheets/sheet2.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData/></worksheet>",
        });
    }

    private static void WriteCommentFixture(string path)
    {
        const string vml = "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\"\n xmlns:o=\"urn:schemas-microsoft-com:office:office\"\n xmlns:x=\"urn:schemas-microsoft-com:office:excel\">\n" +
            " <o:shapelayout v:ext=\"edit\">\n  <o:idmap v:ext=\"edit\" data=\"1\"/>\n </o:shapelayout><v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\"\n  path=\"m,l,21600r21600,l21600,xe\">\n  <v:stroke joinstyle=\"miter\"/>\n </v:shapetype>" +
            "<v:shape id=\"_x0000_s1025\" type=\"#_x0000_t202\" style='position:absolute;\n  margin-left:107.25pt;margin-top:1.5pt;width:96pt;height:55.5pt;z-index:1;\n  visibility:hidden' fillcolor=\"infoBackground [80]\" o:insetmode=\"auto\">\n" +
            "  <v:textbox style='mso-direction-alt:auto'>\n   <div style='text-align:left'></div>\n  </v:textbox>\n  <x:ClientData ObjectType=\"Note\">\n   <x:MoveWithCells/>\n   <x:SizeWithCells/>\n   <x:Anchor>\n    2, 15, 0, 2, 4, 15, 4, 8</x:Anchor>\n   <x:AutoFill>False</x:AutoFill>\n   <x:Row>0</x:Row>\n   <x:Column>1</x:Column>\n  </x:ClientData>\n </v:shape>" +
            "<v:shape id=\"_x0000_s1026\" type=\"#_x0000_t202\" style='position:absolute;margin-left:200pt;margin-top:30pt;width:96pt;height:55.5pt;z-index:2;visibility:visible' fillcolor=\"infoBackground [80]\">" +
            "<x:ClientData ObjectType=\"Note\"><x:Anchor>3, 15, 2, 10, 5, 15, 6, 4</x:Anchor><x:Row>3</x:Row><x:Column>2</x:Column><x:Visible/></x:ClientData></v:shape></xml>";
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1), ("/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml")),
            ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"N\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml")),
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheetData><row r=\"1\"><c r=\"B1\"><v>1</v></c></row></sheetData><legacyDrawing r:id=\"rId2\"/></worksheet>",
            ["xl/worksheets/_rels/sheet1.xml.rels"] = Rels(("rId1", R + "/comments", "../comments1.xml"), ("rId2", R + "/vmlDrawing", "../drawings/vmlDrawing1.vml")),
            ["xl/comments1.xml"] = $"<comments xmlns=\"{Main}\"><authors><author>Arun</author><author>Bob</author></authors><commentList>" +
                "<comment ref=\"B1\" authorId=\"0\"><text><r><rPr><b/><sz val=\"9\"/><rFont val=\"Tahoma\"/></rPr><t>Arun:</t></r><r><rPr><sz val=\"9\"/><rFont val=\"Tahoma\"/></rPr><t xml:space=\"preserve\">\nfirst note</t></r></text></comment>" +
                "<comment ref=\"C4\" authorId=\"1\"><text><t>plain</t></text></comment></commentList></comments>",
            ["xl/drawings/vmlDrawing1.vml"] = vml,
        });
    }

    private static void WriteHeaderFooterFixture(string path)
    {
        const string vml = "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\"\n xmlns:o=\"urn:schemas-microsoft-com:office:office\"\n xmlns:x=\"urn:schemas-microsoft-com:office:excel\">\n" +
            " <o:shapelayout v:ext=\"edit\">\n  <o:idmap v:ext=\"edit\" data=\"1\"/>\n </o:shapelayout><v:shapetype id=\"_x0000_t75\" coordsize=\"21600,21600\" o:spt=\"75\"\n  o:preferrelative=\"t\" path=\"m@4@5l@4@11@9@11@9@5xe\" filled=\"f\" stroked=\"f\">\n  <v:stroke joinstyle=\"miter\"/>\n </v:shapetype>" +
            "<v:shape id=\"CH\" o:spid=\"_x0000_s1025\" type=\"#_x0000_t75\"\n  style='position:absolute;margin-left:0;margin-top:0;width:192.75pt;height:63pt;\n  z-index:1'>\n  <v:imagedata o:relid=\"rId1\" o:title=\"logo\"/>\n  <o:lock v:ext=\"edit\" rotation=\"t\"/>\n </v:shape>" +
            "<v:shape id=\"LF\" o:spid=\"_x0000_s1026\" type=\"#_x0000_t75\" style='position:absolute;margin-left:0;margin-top:0;width:48px;height:24px;z-index:2'><v:imagedata o:relid=\"rId1\" o:title=\"small\"/></v:shape>" +
            "<v:shape id=\"RHEVEN\" o:spid=\"_x0000_s1027\" type=\"#_x0000_t75\" style='position:absolute;width:1in;height:0.5in'><v:imagedata o:relid=\"rId1\"/></v:shape></xml>";
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1)),
            ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"H\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml")),
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>" +
                "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/><headerFooter differentOddEven=\"1\"><oddHeader>&amp;C&amp;G</oddHeader><oddFooter>&amp;L&amp;G</oddFooter><evenHeader>&amp;R&amp;G</evenHeader></headerFooter><legacyDrawingHF r:id=\"rId1\"/></worksheet>",
            ["xl/worksheets/_rels/sheet1.xml.rels"] = Rels(("rId1", R + "/vmlDrawing", "../drawings/vmlDrawing1.vml")),
            ["xl/drawings/vmlDrawing1.vml"] = vml,
            ["xl/drawings/_rels/vmlDrawing1.vml.rels"] = Rels(("rId1", R + "/image", "../media/image1.png")),
            ["xl/media/image1.png"] = Png,
        });
    }

    private static void WriteChartSheetFixture(string path)
    {
        const string c = "http://schemas.openxmlformats.org/drawingml/2006/chart", a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        WriteZip(path, new()
        {
            ["[Content_Types].xml"] = ContentTypes(CtWorkbook, CtSheet(1), CtSheet(2),
                ("/xl/chartsheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml"),
                ("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml"),
                ("/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")),
            ["_rels/.rels"] = Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")),
            ["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><bookViews><workbookView activeTab=\"1\"/></bookViews><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Chart1\" sheetId=\"3\" r:id=\"rId3\"/><sheet name=\"More\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml"), ("rId2", R + "/worksheet", "worksheets/sheet2.xml"), ("rId3", R + "/chartsheet", "chartsheets/sheet1.xml")),
            ["xl/worksheets/sheet1.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Jan</t></is></c><c r=\"B1\"><v>5</v></c></row></sheetData></worksheet>",
            ["xl/worksheets/sheet2.xml"] = $"<worksheet xmlns=\"{Main}\"><sheetData/></worksheet>",
            ["xl/chartsheets/sheet1.xml"] = $"<chartsheet xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheetPr/><sheetViews><sheetView zoomScale=\"84\" workbookViewId=\"0\" zoomToFit=\"1\"/></sheetViews><pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/><pageSetup orientation=\"landscape\"/><drawing r:id=\"rId1\"/></chartsheet>",
            ["xl/chartsheets/_rels/sheet1.xml.rels"] = Rels(("rId1", R + "/drawing", "../drawings/drawing1.xml")),
            ["xl/drawings/drawing1.xml"] = $"<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"{a}\"><xdr:absoluteAnchor><xdr:pos x=\"34018\" y=\"34018\"/><xdr:ext cx=\"8697232\" cy=\"6281964\"/>" +
                $"<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>" +
                $"<a:graphic><a:graphicData uri=\"{c}\"><c:chart xmlns:c=\"{c}\" xmlns:r=\"{R}\" r:id=\"rId1\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:absoluteAnchor></xdr:wsDr>",
            ["xl/drawings/_rels/drawing1.xml.rels"] = Rels(("rId1", R + "/chart", "../charts/chart1.xml")),
            ["xl/charts/chart1.xml"] = $"<c:chartSpace xmlns:c=\"{c}\" xmlns:a=\"{a}\"><c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title><c:plotArea>" +
                "<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:cat><c:strRef><c:f>Data!$A$1</c:f></c:strRef></c:cat><c:val><c:numRef><c:f>Data!$B$1</c:f></c:numRef></c:val></c:ser></c:barChart></c:plotArea></c:chart></c:chartSpace>",
        });
    }
}
