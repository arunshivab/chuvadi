using System;
using System.Globalization;
using System.IO;
using Chuvadi.Sheets.Model;
using static Chuvadi.Sheets.ManualTests.TestGroup;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.1.0 items H1 (widths), H2 (cellref), SH-9 C1 and C2 (displaytext) and N1 (limits), one group each.
/// Data is fictional (D-60).
/// </summary>
internal static class Items210Test
{
    // ---- widths (H1, D-212) ---------------------------------------------------------------------------------

    public static void RunWidths(string outputDir)
    {
        var g = new TestGroup("WidthsTest", "2.1.0 H1: column widths in pixels and points...");
        string dir = Path.Combine(outputDir, "widths");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        // Chuvadi.Layout's GridGeometry formula (ECMA-376 §18.3.1.13), written out here independently.
        static int LayoutPixels(double width, double mdw) => (int)Math.Truncate(((256 * width) + Math.Truncate(128.0 / mdw)) / 256 * mdw);

        g.Check("Excel's known widths at MDW 7: 9.140625 → 64 px (Excel's default), 13.7109375 → 96 px, 0 → 0", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Fictional");
            s.Columns[1].Width = 9.140625;
            s.Columns[2].Width = 13.7109375;
            Eq(64, s.Columns[1].WidthInPixels(), "9.140625");
            Eq(48.0, s.Columns[1].WidthInPoints(), "9.140625 in points");
            Eq(96, s.Columns[2].WidthInPixels(), "13.7109375");
            Eq(72.0, s.Columns[2].WidthInPoints(), "13.7109375 in points");
            Eq(64, s.Columns[3].WidthInPixels(), "no width: the sheet default (base 8 → 9.140625)");
            Eq(64, s.Format.DefaultWidthInPixels(), "SheetFormat default");
            Eq(48.0, s.Format.DefaultWidthInPoints(), "SheetFormat default in points");
            s.Format.DefaultColumnWidth = 20;
            Eq(LayoutPixels(20, 7), s.Columns[3].WidthInPixels(), "a stated default width");
        });

        g.Check("every width and MDW on a grid gives Layout's pixels; points = pixels × 0.75", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Fictional");
            int n = 0;
            foreach (double w in new[] { 0.5, 1, 1.43, 2.7109375, 8.43, 9.140625, 10.7109375, 13, 13.7109375, 50.33, 100, 255 })
                foreach (double mdw in new[] { 5.0, 6, 7, 8, 9, 11, 12.5 })
                {
                    s.Columns[1].Width = w;
                    Eq(LayoutPixels(w, mdw), s.Columns[1].WidthInPixels(mdw), $"width {w} at MDW {mdw}");
                    Eq(LayoutPixels(w, mdw) * 0.75, s.Columns[1].WidthInPoints(mdw), $"points, width {w} at MDW {mdw}");
                    n++;
                }
            Console.WriteLine($"          {n} width × MDW pairs");
        });

        g.Check("a loaded span (ColumnRange): its stored width; 0 when the file gives none", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Fictional");
            s.Cell("A1").Value = "Fictional";
            s.Columns[2].Width = 13.7109375;
            s.Columns[3].Hidden = true;
            var p = Path.Combine(dir, "spans.xlsx");
            wb.SaveTo(p);
            var back = Workbook.Load(p).Sheets[0];
            var range = back.Columns[2].Range!;
            Eq(96, range.WidthInPixels(), "span width");
            Eq(72.0, range.WidthInPoints(), "span points");
            Eq(96, back.Columns[2].WidthInPixels(), "the column");
            if (back.Columns[3].Range is { } hidden)
                Eq(hidden.Width > 0 ? LayoutPixels(hidden.Width, 7) : 0, hidden.WidthInPixels(), "hidden span: its own width, or 0");
        });

        g.Check("an MDW that is not a positive finite number throws ArgumentOutOfRangeException", () =>
        {
            var s = new Workbook().AddSheet("Fictional");
            foreach (double bad in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
            {
                Throws<ArgumentOutOfRangeException>(() => s.Columns[1].WidthInPixels(bad), $"column, {bad}");
                Throws<ArgumentOutOfRangeException>(() => s.Format.DefaultWidthInPoints(bad), $"default, {bad}");
            }
        });

        g.Finish();
    }

    // ---- cellref (H2, D-212) --------------------------------------------------------------------------------

    public static void RunCellRef(string outputDir)
    {
        var g = new TestGroup("CellRefTest", "2.1.0 H2: the public CellReference helper...");

        g.Check("ColumnLetters and ColumnIndex round-trip every column 1..16,384", () =>
        {
            Eq("A", CellReference.ColumnLetters(1), "1");
            Eq("Z", CellReference.ColumnLetters(26), "26");
            Eq("AA", CellReference.ColumnLetters(27), "27");
            Eq("ZZ", CellReference.ColumnLetters(702), "702");
            Eq("AAA", CellReference.ColumnLetters(703), "703");
            Eq("XFD", CellReference.ColumnLetters(16384), "16384");
            for (int c = 1; c <= 16384; c++)
                if (CellReference.ColumnIndex(CellReference.ColumnLetters(c)) != c) throw new Exception($"column {c} does not round-trip");
        });

        g.Check("ToA1 and ParseA1 round-trip, the same results as the internal CellAddress", () =>
        {
            foreach (var (r, c) in new[] { (1, 1), (10, 28), (1048576, 16384), (65536, 256), (3, 2) })
            {
                string a1 = CellReference.ToA1(r, c);
                Eq(Internal.CellAddress.ToA1(r, c), a1, $"ToA1({r},{c})");
                Eq((r, c), CellReference.ParseA1(a1), $"ParseA1({a1})");
            }
            Eq("AB10", CellReference.ToA1(10, 28), "AB10");
            Eq((1, 16385), CellReference.ParseA1("XFE1"), "ParseA1 checks the shape, not the range (as documented)");
        });

        g.Check("errors: the same exceptions as the internal helper; \"$\", lower case and ranges are rejected", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => CellReference.ColumnLetters(0), "ColumnLetters(0)");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ColumnLetters(16385), "ColumnLetters(16385)");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ToA1(0, 1), "ToA1 row 0");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ToA1(1048577, 1), "ToA1 row 1048577");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ToA1(1, 0), "ToA1 column 0");
            Throws<ArgumentException>(() => CellReference.ColumnIndex(""), "ColumnIndex(\"\")");
            Throws<ArgumentException>(() => CellReference.ColumnIndex(null!), "ColumnIndex(null)");
            Throws<FormatException>(() => CellReference.ColumnIndex("a"), "ColumnIndex(\"a\")");
            Throws<FormatException>(() => CellReference.ColumnIndex("$A"), "ColumnIndex(\"$A\")");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ColumnIndex("XFE"), "ColumnIndex(\"XFE\")");
            Throws<ArgumentOutOfRangeException>(() => CellReference.ColumnIndex("AAAAAAAA"), "ColumnIndex(8 letters)");
            Throws<ArgumentException>(() => CellReference.ParseA1(""), "ParseA1(\"\")");
            foreach (var bad in new[] { "$A$1", "a1", "A0", "A", "1", "A1:B2", " A1", "A1 " })
            {
                var pub = Catch(() => CellReference.ParseA1(bad));
                var inner = Catch(() => Internal.CellAddress.ParseA1(bad));
                True(pub is FormatException, $"ParseA1(\"{bad}\") gives {pub.GetType().Name}");
                Eq(inner.Message, pub.Message, $"ParseA1(\"{bad}\"): same message");
            }
        });

        g.Finish();
    }

    // ---- displaytext (SH-9 C1, C2) --------------------------------------------------------------------------

    public static void RunDisplayText(string outputDir)
    {
        var g = new TestGroup("DisplayTextTest", "2.1.0 SH-9 C1 and C2: serials below 1, built-in 14 and 22...");
        string dir = Path.Combine(outputDir, "displaytext");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        // C1: serials and formats from A7 x01, as Excel shows them.
        var c1 = new (double Serial, string Format, string Excel)[]
        {
            (0, "dd-mm-yyyy", "00-01-1900"),
            (0, "d-mmm-yy", "0-Jan-00"),
            (0.375, "h:mm:ss", "9:00:00"),
            (0.375, "hh:mm:ss", "09:00:00"),
            (0.375, "dd-mm-yyyy hh:mm", "00-01-1900 09:00"),
            (-1, "dd-mm-yyyy", "########"),
            (1, "dd-mm-yyyy", "01-01-1900"),
            (61, "dd-mm-yyyy", "01-03-1900"),
        };
        string c1Path = Path.Combine(dir, "below-one.xlsx");
        using (var w = XlsxWriter.Create(c1Path))
        {
            using (var s = w.AddSheet("Fictional"))
                foreach (var (serial, format, _) in c1)
                    s.WriteRow(r => r.Cell(serial, new CellStyleBuilder().Format(format).Build()));
            w.Save();
        }

        g.Check("C1: serials 0 and 0.375 display as Excel shows them (DisplayText and GetDisplayString), not ########", () =>
        {
            var sheet = Workbook.Load(c1Path).Sheets[0];
            for (int i = 0; i < c1.Length; i++)
                Eq(c1[i].Excel, sheet.Cell($"A{i + 1}").DisplayText, $"DisplayText {c1[i].Serial} as {c1[i].Format}");
            using var r = XlsxReader.Open(c1Path, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            int n = 0;
            foreach (var row in r.Sheet("Fictional").Rows)
            {
                Eq(c1[n].Excel, row.GetDisplayString(0), $"GetDisplayString {c1[n].Serial} as {c1[n].Format}");
                n++;
            }
            Eq(c1.Length, n, "rows");
        });

        g.Check("C1: NumberFormatter.ToSerial maps 1899-12-30 + time to the time of day; every other date unchanged", () =>
        {
            Eq(0.375, NumberFormatter.ToSerial(new DateTime(1899, 12, 30, 9, 0, 0)), "1899-12-30 09:00");
            Eq(0.0, NumberFormatter.ToSerial(new DateTime(1899, 12, 30)), "1899-12-30 00:00");
            Eq(0.0, NumberFormatter.ToSerial(new DateTime(1899, 12, 31)), "1899-12-31");
            Eq(1.0, NumberFormatter.ToSerial(new DateTime(1900, 1, 1)), "1900-01-01");
            Eq(59.5, NumberFormatter.ToSerial(new DateTime(1900, 2, 28, 12, 0, 0)), "1900-02-28 12:00");
            Eq(61.0, NumberFormatter.ToSerial(new DateTime(1900, 3, 1)), "1900-03-01");
            Eq((new DateTime(1899, 12, 29) - new DateTime(1899, 12, 31)).TotalDays, NumberFormatter.ToSerial(new DateTime(1899, 12, 29)), "1899-12-29, as before");
            Eq(new DateTime(2024, 10, 2).ToOADate(), NumberFormatter.ToSerial(new DateTime(2024, 10, 2)), "2024-10-02");
            Eq((new DateTime(1899, 12, 30, 9, 0, 0) - new DateTime(1904, 1, 1)).TotalDays, NumberFormatter.ToSerial(new DateTime(1899, 12, 30, 9, 0, 0), date1904: true), "1904 system unchanged");
        });

        // C2: one cell with built-in 14 and one with 22, written through the writer's built-in mapping.
        string c2Path = Path.Combine(dir, "builtin-14-22.xlsx");
        var when = new DateTime(1903, 5, 18, 14, 5, 0);
        using (var w = XlsxWriter.Create(c2Path))
        {
            using (var s = w.AddSheet("Fictional"))
                s.WriteRow(r => r.Cell(when.Date, new CellStyleBuilder().Format("mm-dd-yy").Build())
                                 .Cell(when, new CellStyleBuilder().Format("m/d/yy h:mm").Build()));
            w.Save();
        }

        g.Check("C2: the file stores ids 14 and 22; the resolved style still reports their ECMA codes", () =>
        {
            var wb = Workbook.Load(c2Path);
            var styles = wb.StyleTable.CellFormats;
            var a = wb.Sheets[0].Cell("A1");
            var b = wb.Sheets[0].Cell("B1");
            Eq(14, styles[a.StyleIndex].NumberFormatId, "A1 id");
            Eq(22, styles[b.StyleIndex].NumberFormatId, "B1 id");
            Eq("mm-dd-yy", a.Style!.Value.NumberFormat, "A1 code (data unchanged)");
            Eq("m/d/yy h:mm", b.Style!.Value.NumberFormat, "B1 code (data unchanged)");
        });

        g.Check("C2: ids 14 and 22 follow the culture's short date (model and streaming reader agree)", () =>
        {
            var custom = (CultureInfo)CultureInfo.GetCultureInfo("en-IN").Clone();
            custom.DateTimeFormat.ShortDatePattern = "dd-MM-yyyy";   // as Windows regional settings set it (A7)
            var cases = new (string Name, CultureInfo Culture)[]
            {
                ("invariant", CultureInfo.InvariantCulture),
                ("en-US", CultureInfo.GetCultureInfo("en-US")),
                ("en-IN", CultureInfo.GetCultureInfo("en-IN")),
                ("de-DE", CultureInfo.GetCultureInfo("de-DE")),
                ("en-GB", CultureInfo.GetCultureInfo("en-GB")),
                ("dd-MM-yyyy (custom)", custom),
            };
            foreach (var (name, culture) in cases)
            {
                string date = when.Date.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
                var wb = Workbook.Load(c2Path, null, new XlsxReaderOptions { DisplayCulture = culture });
                Eq(date, wb.Sheets[0].Cell("A1").DisplayText, $"{name}: built-in 14");
                Eq(date + " 14:05", wb.Sheets[0].Cell("B1").DisplayText, $"{name}: built-in 22");
                using var r = XlsxReader.Open(c2Path, new XlsxReaderOptions { DisplayCulture = culture, TreatFirstRowAsHeaders = false });
                foreach (var row in r.Sheet("Fictional").Rows)
                {
                    Eq(date, row.GetDisplayString(0), $"{name}: streaming 14");
                    Eq(date + " 14:05", row.GetDisplayString(1), $"{name}: streaming 22");
                }
                Console.WriteLine($"          {name,-20} 14: {date,-12} 22: {date} 14:05");
            }
            Eq("05/18/1903", Workbook.Load(c2Path).Sheets[0].Cell("A1").DisplayText, "default (invariant)");
        });

        g.Check("C2: a code the file declares for id 14 wins; an explicit \"mm-dd-yy\" code is formatted as written", () =>
        {
            Eq("05-18-03", NumberFormatter.Format(when.Date, "mm-dd-yy"), "NumberFormatter with the code");
            var wb = new Workbook();
            var s = wb.AddSheet("Fictional");
            s.Cell("A1").Value = when.Date;
            s.Cell("A1").Style = new CellStyleBuilder().Format("mm-dd-yy").Build();
            Eq("05-18-03", s.Cell("A1").DisplayText, "a style set in code shows its own code");

            // A file that declares numFmtId 14 itself (some writers do): its code wins.
            string p = Path.Combine(dir, "declared-14.xlsx");
            File.Copy(c2Path, p, overwrite: true);
            using (var z = System.IO.Compression.ZipFile.Open(p, System.IO.Compression.ZipArchiveMode.Update))
            {
                var e = z.GetEntry("xl/styles.xml")!;
                string xml;
                using (var rd = new StreamReader(e.Open())) xml = rd.ReadToEnd();
                int at = xml.IndexOf("<numFmts", StringComparison.Ordinal);
                xml = at >= 0
                    ? xml.Insert(xml.IndexOf('>', at) + 1, "<numFmt numFmtId=\"14\" formatCode=\"yyyy/mm/dd\"/>")
                    : xml.Insert(xml.IndexOf('>', xml.IndexOf("<styleSheet", StringComparison.Ordinal)) + 1, "<numFmts count=\"1\"><numFmt numFmtId=\"14\" formatCode=\"yyyy/mm/dd\"/></numFmts>");
                e.Delete();
                using var wr = new StreamWriter(z.CreateEntry("xl/styles.xml").Open());
                wr.Write(xml);
            }
            Eq("1903/05/18", Workbook.Load(p).Sheets[0].Cell("A1").DisplayText, "the file's own code for 14");
        });

        g.Finish();
    }

    // ---- limits (N1, D-329) ---------------------------------------------------------------------------------

    public static void RunLimits(string outputDir)
    {
        var g = new TestGroup("LimitsTest", "2.1.0 N1: XlsxLimitExceededException with an inner exception...");

        g.Check("the five-argument constructor keeps the inner exception, the values and the four-argument message", () =>
        {
            var inner = new InvalidDataException("fictional inner cause");
            var four = new XlsxLimitExceededException("MaxRowsPerSheet", 10, 11, "fictional detail");
            var five = new XlsxLimitExceededException("MaxRowsPerSheet", 10, 11, "fictional detail", inner);
            Eq(four.Message, five.Message, "message");
            True(ReferenceEquals(inner, five.InnerException), "inner exception");
            True(four.InnerException is null, "four-argument: no inner exception");
            Eq("MaxRowsPerSheet", five.LimitName, "LimitName");
            Eq(10.0, five.Limit, "Limit");
            Eq(11.0, five.Actual, "Actual");
            True(five is IOException, "still an IOException");
        });

        g.Finish();
    }

    private static Exception Catch(Action a)
    {
        try { a(); } catch (Exception ex) { return ex; }
        throw new Exception("nothing was thrown");
    }
}
