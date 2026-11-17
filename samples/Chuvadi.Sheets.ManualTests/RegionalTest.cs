using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Sheets.Model;
using static Chuvadi.Sheets.ManualTests.TestGroup;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.2.0 C3 and C5 (D-388, D-404): built-in 14 to 17 and 22, and the month names of "mmm", as Excel shows them on a
/// PC with each region (Excel check, 16 Nov 2026). The expected text is the approved Rule 5 list
/// (claude/sheets-2.2.0-rule5-list.md), every "after" cell. The cultures are pinned to the patterns and month names
/// .NET had when the list was made, so the checks test Chuvadi's rules, not the ICU version of the machine.
/// The last check prints the text under this PC's CultureInfo.CurrentCulture, for Arun to compare with Excel.
/// Data is fictional dates only (D-60).
/// </summary>
internal static class RegionalTest
{
    private static readonly (string Label, string Code, bool Time)[] Formats =
    {
        ("14", "mm-dd-yy", false), ("15", "d-mmm-yy", false), ("16", "d-mmm", false), ("17", "mmm-yy", false),
        ("22", "m/d/yy h:mm", true), ("dd-mmm-yyyy", "dd-mmm-yyyy", false), ("mmmm", "mmmm", false),
    };

    private static readonly DateTime[] Values =
        { new(2023, 3, 5, 9, 5, 0), new(2023, 9, 5, 9, 5, 0), new(2023, 5, 18, 14, 5, 0) };

    // Culture, format, the text for 5 Mar 2023 09:05, 5 Sep 2023 09:05 and 18 May 2023 14:05 (the approved "after").
    private static readonly (string Culture, string Format, string[] Expected)[] Expected =
    {
        ("Invariant", "14", new[] { "03/05/2023", "09/05/2023", "05/18/2023" }),
        ("Invariant", "15", new[] { "5-Mar-23", "5-Sep-23", "18-May-23" }),
        ("Invariant", "16", new[] { "5-Mar", "5-Sep", "18-May" }),
        ("Invariant", "17", new[] { "Mar-23", "Sep-23", "May-23" }),
        ("Invariant", "22", new[] { "03/05/2023 9:05", "09/05/2023 9:05", "05/18/2023 14:05" }),
        ("Invariant", "dd-mmm-yyyy", new[] { "05-Mar-2023", "05-Sep-2023", "18-May-2023" }),
        ("Invariant", "mmmm", new[] { "March", "September", "May" }),
        ("EnUs", "14", new[] { "3/5/2023", "9/5/2023", "5/18/2023" }),
        ("EnUs", "15", new[] { "5-Mar-23", "5-Sep-23", "18-May-23" }),
        ("EnUs", "16", new[] { "5-Mar", "5-Sep", "18-May" }),
        ("EnUs", "17", new[] { "Mar-23", "Sep-23", "May-23" }),
        ("EnUs", "22", new[] { "3/5/2023 9:05", "9/5/2023 9:05", "5/18/2023 14:05" }),
        ("EnUs", "dd-mmm-yyyy", new[] { "05-Mar-2023", "05-Sep-2023", "18-May-2023" }),
        ("EnUs", "mmmm", new[] { "March", "September", "May" }),
        ("EnInWindows", "14", new[] { "05-03-2023", "05-09-2023", "18-05-2023" }),
        ("EnInWindows", "15", new[] { "05-Mar-23", "05-Sep-23", "18-May-23" }),
        ("EnInWindows", "16", new[] { "05-Mar", "05-Sep", "18-May" }),
        ("EnInWindows", "17", new[] { "Mar-23", "Sep-23", "May-23" }),
        ("EnInWindows", "22", new[] { "05-03-2023 09:05", "05-09-2023 09:05", "18-05-2023 14:05" }),
        ("EnInWindows", "dd-mmm-yyyy", new[] { "05-Mar-2023", "05-Sep-2023", "18-May-2023" }),
        ("EnInWindows", "mmmm", new[] { "March", "September", "May" }),
        ("EnIn", "14", new[] { "05/03/2023", "05/09/2023", "18/05/2023" }),
        ("EnIn", "15", new[] { "05-Mar-23", "05-Sep-23", "18-May-23" }),
        ("EnIn", "16", new[] { "05-Mar", "05-Sep", "18-May" }),
        ("EnIn", "17", new[] { "Mar-23", "Sep-23", "May-23" }),
        ("EnIn", "22", new[] { "05/03/2023 9:05", "05/09/2023 9:05", "18/05/2023 14:05" }),
        ("EnIn", "dd-mmm-yyyy", new[] { "05-Mar-2023", "05-Sep-2023", "18-May-2023" }),
        ("EnIn", "mmmm", new[] { "March", "September", "May" }),
        ("EnGb", "14", new[] { "05/03/2023", "05/09/2023", "18/05/2023" }),
        ("EnGb", "15", new[] { "05-Mar-23", "05-Sept-23", "18-May-23" }),
        ("EnGb", "16", new[] { "05-Mar", "05-Sept", "18-May" }),
        ("EnGb", "17", new[] { "Mar-23", "Sept-23", "May-23" }),
        ("EnGb", "22", new[] { "05/03/2023 09:05", "05/09/2023 09:05", "18/05/2023 14:05" }),
        ("EnGb", "dd-mmm-yyyy", new[] { "05-Mar-2023", "05-Sept-2023", "18-May-2023" }),
        ("EnGb", "mmmm", new[] { "March", "September", "May" }),
        ("DeDe", "14", new[] { "05.03.2023", "05.09.2023", "18.05.2023" }),
        ("DeDe", "15", new[] { "05. Mrz 23", "05. Sep 23", "18. Mai 23" }),
        ("DeDe", "16", new[] { "05. Mrz", "05. Sep", "18. Mai" }),
        ("DeDe", "17", new[] { "Mrz 23", "Sep 23", "Mai 23" }),
        ("DeDe", "22", new[] { "05.03.2023 09:05", "05.09.2023 09:05", "18.05.2023 14:05" }),
        ("DeDe", "dd-mmm-yyyy", new[] { "05-Mrz-2023", "05-Sep-2023", "18-Mai-2023" }),
        ("DeDe", "mmmm", new[] { "März", "September", "Mai" }),
        ("TaIn", "14", new[] { "5/3/2023", "5/9/2023", "18/5/2023" }),
        ("TaIn", "15", new[] { "5-மார்ச்-23", "5-செப்டம்பர்-23", "18-மே-23" }),
        ("TaIn", "16", new[] { "5-மார்ச்", "5-செப்டம்பர்", "18-மே" }),
        ("TaIn", "17", new[] { "மார்ச்-23", "செப்டம்பர்-23", "மே-23" }),
        ("TaIn", "22", new[] { "5/3/2023 9:05", "5/9/2023 9:05", "18/5/2023 14:05" }),
        ("TaIn", "dd-mmm-yyyy", new[] { "05-மார்ச்-2023", "05-செப்டம்பர்-2023", "18-மே-2023" }),
        ("TaIn", "mmmm", new[] { "மார்ச்", "செப்டம்பர்", "மே" }),
    };

    private static CultureInfo Pinned(string name, string shortDate, string shortTime, string dateSeparator, string[] months, string[] abbreviated)
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
        var d = c.DateTimeFormat;
        d.ShortDatePattern = shortDate;
        d.ShortTimePattern = shortTime;
        d.DateSeparator = dateSeparator;
        d.MonthNames = months.Append(string.Empty).ToArray();
        d.AbbreviatedMonthNames = abbreviated.Append(string.Empty).ToArray();
        d.MonthGenitiveNames = d.MonthNames;
        d.AbbreviatedMonthGenitiveNames = d.AbbreviatedMonthNames;
        return c;
    }

    private static Dictionary<string, CultureInfo> Cultures()
    {
        var c = new Dictionary<string, CultureInfo>
        {
            ["Invariant"] = CultureInfo.InvariantCulture,
        ["EnUs"] = Pinned("en-US", "M/d/yyyy", "h:mm tt", "/",
            new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" },
            new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" }),
        ["EnIn"] = Pinned("en-IN", "dd/MM/yyyy", "h:mm tt", "/",
            new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" },
            new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec" }),
        ["EnGb"] = Pinned("en-GB", "dd/MM/yyyy", "HH:mm", "/",
            new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" },
            new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec" }),
        ["DeDe"] = Pinned("de-DE", "dd.MM.yyyy", "HH:mm", ".",
            new[] { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" },
            new[] { "Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez" }),
        ["TaIn"] = Pinned("ta-IN", "d/M/yyyy", "tt h:mm", "/",
            new[] { "ஜனவரி", "பிப்ரவரி", "மார்ச்", "ஏப்ரல்", "மே", "ஜூன்", "ஜூலை", "ஆகஸ்ட்", "செப்டம்பர்", "அக்டோபர்", "நவம்பர்", "டிசம்பர்" },
            new[] { "ஜன.", "பிப்.", "மார்.", "ஏப்.", "மே", "ஜூன்", "ஜூலை", "ஆக.", "செப்.", "அக்.", "நவ.", "டிச." }),
        };
        // Arun's Windows (regional format English (India), short date dd-MM-yyyy, short time HH:mm).
        var arun = (CultureInfo)c["EnIn"].Clone();
        arun.DateTimeFormat.ShortDatePattern = "dd-MM-yyyy";
        arun.DateTimeFormat.ShortTimePattern = "HH:mm";
        arun.DateTimeFormat.DateSeparator = "-";
        c["EnInWindows"] = arun;
        return c;
    }

    public static void Run(string outputDir)
    {
        var g = new TestGroup("RegionalTest", "2.2.0 C3, C5: built-in 14 to 17 and 22, and \"mmm\" month names, as Excel shows them per region...");
        string dir = Path.Combine(outputDir, "regional");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "regional-dates.xlsx");
        using (var w = XlsxWriter.Create(path))
        {
            using (var s = w.AddSheet("S"))
                foreach (var v in Values)
                    s.WriteRow(r => { foreach (var f in Formats) r.Cell(f.Time ? v : v.Date, new CellStyleBuilder().Format(f.Code).Build()); });
            w.Save();
        }
        var cultures = Cultures();

        g.Check("the built-in ids are the ones Excel stores (14, 15, 16, 17, 22), with no code declared in the file", () =>
        {
            var wb = Workbook.Load(path);
            var ids = Enumerable.Range(1, 5).Select(c => wb.StyleTable.CellFormats[wb.Sheets[0].Cell(1, c).StyleIndex].NumberFormatId).ToArray();
            Eq("14,15,16,17,22", string.Join(",", ids), "ids");
            True(ids.All(i => !wb.StyleTable.CustomNumberFormats.ContainsKey(i)), "a built-in id has a declared code");
        });

        foreach (var name in new[] { "Invariant", "EnUs", "EnInWindows", "EnIn", "EnGb", "DeDe", "TaIn" })
        {
            var culture = cultures[name];
            g.Check($"{name}: every approved cell, from Workbook.Load, XlsxReader and NumberFormatter", () =>
            {
                var wb = Workbook.Load(path, null, new XlsxReaderOptions { DisplayCulture = culture });
                using var reader = XlsxReader.Open(path, new XlsxReaderOptions { DisplayCulture = culture, TreatFirstRowAsHeaders = false });
                var streamed = new List<string[]>();
                foreach (var row in reader.Sheet("S").Rows)
                    streamed.Add(Enumerable.Range(0, Formats.Length).Select(c => row.GetDisplayString(c) ?? "").ToArray());
                var errors = new List<string>();
                foreach (var e in Expected.Where(e => e.Culture == name))
                {
                    int k = Array.FindIndex(Formats, f => f.Label == e.Format);
                    for (int r = 0; r < Values.Length; r++)
                    {
                        string model = wb.Sheets[0].Cell(r + 1, k + 1).DisplayText;
                        string stream = streamed[r][k];
                        if (model != e.Expected[r]) errors.Add($"{e.Format} row {r + 1}: Workbook.Load '{model}', expected '{e.Expected[r]}'");
                        if (stream != e.Expected[r]) errors.Add($"{e.Format} row {r + 1}: XlsxReader '{stream}', expected '{e.Expected[r]}'");
                        if (!Formats[k].Label.All(char.IsDigit))
                        {
                            string direct = NumberFormatter.Format(Formats[k].Time ? Values[r] : Values[r].Date, Formats[k].Code, new NumberFormatOptions { Culture = culture });
                            if (direct != e.Expected[r]) errors.Add($"{e.Format} row {r + 1}: NumberFormatter '{direct}', expected '{e.Expected[r]}'");
                        }
                    }
                }
                True(errors.Count == 0, string.Join("; ", errors));
            });
        }

        g.Check("invariant culture (the default): every built-in date and time id 14 to 22 shows its 2.1.0 code", () =>
        {
            string p = Path.Combine(dir, "invariant-14-22.xlsx");
            var codes = new[] { "mm-dd-yy", "d-mmm-yy", "d-mmm", "mmm-yy", "h:mm AM/PM", "h:mm:ss AM/PM", "h:mm", "h:mm:ss", "m/d/yy h:mm" };
            using (var w = XlsxWriter.Create(p))
            {
                using (var s = w.AddSheet("S")) s.WriteRow(r => { foreach (var c in codes) r.Cell(Values[0], new CellStyleBuilder().Format(c).Build()); });
                w.Save();
            }
            var wb = Workbook.Load(p);
            var expected = new[] { "03/05/2023", "5-Mar-23", "5-Mar", "Mar-23", "9:05 AM", "9:05:00 AM", "9:05", "9:05:00", "03/05/2023 9:05" };
            for (int c = 0; c < codes.Length; c++)
            {
                Eq(expected[c], wb.Sheets[0].Cell(1, c + 1).DisplayText, $"id {wb.StyleTable.CellFormats[wb.Sheets[0].Cell(1, c + 1).StyleIndex].NumberFormatId}");
            }
        });

        g.Check("a code the file itself declares for id 15 still wins over the culture", () =>
        {
            string p = Path.Combine(dir, "declared-15.xlsx");
            DeclareCode(path, p, 15, "yyyy\\-mm\\-dd");
            foreach (var (name, id14) in new[] { ("Invariant", "03/05/2023"), ("EnInWindows", "05-03-2023"), ("DeDe", "05.03.2023") })
            {
                var wb = Workbook.Load(p, null, new XlsxReaderOptions { DisplayCulture = cultures[name] });
                Eq("2023-03-05", wb.Sheets[0].Cell(1, 2).DisplayText, name + ": id 15 with its declared code");
                Eq(id14, wb.Sheets[0].Cell(1, 1).DisplayText, name + ": id 14, not declared, still follows the culture");
            }
        });

        g.Check("Style.NumberFormat still reports the stored code (only the display follows the culture)", () =>
        {
            var wb = Workbook.Load(path, null, new XlsxReaderOptions { DisplayCulture = cultures["DeDe"] });
            Eq("d-mmm-yy", wb.Sheets[0].Cell(1, 2).Style!.Value.NumberFormat, "id 15");
            Eq("05. Mrz 23", wb.Sheets[0].Cell(1, 2).DisplayText, "id 15 display");
        });

        g.Check("information: this PC's CurrentCulture, and the c3/c5 check sheets in it (compare with Excel on the same PC)", () =>
        {
            var cur = CultureInfo.CurrentCulture;
            var d = cur.DateTimeFormat;
            Console.WriteLine($"          CurrentCulture '{cur.Name}': short date '{d.ShortDatePattern}', short time '{d.ShortTimePattern}', date separator '{d.DateSeparator}'");
            var sb = new StringBuilder();
            var wb = Workbook.Load(path, null, new XlsxReaderOptions { DisplayCulture = cur });
            Console.WriteLine("          c3-builtin-dates-v2 (C: 18 May 2023 14:05, D: 5 Mar 2023 09:05):");
            foreach (var (label, col) in new[] { ("14", 1), ("15", 2), ("16", 3), ("17", 4), ("22", 5) })
                Console.WriteLine($"            id {label}: C {wb.Sheets[0].Cell(3, col).DisplayText}   D {wb.Sheets[0].Cell(1, col).DisplayText}");
            Console.WriteLine("          c5-month-names (the 5th of each month, 2023): mmm | mmmm | dd-mmm-yyyy | id 15");
            for (int m = 1; m <= 12; m++)
            {
                var day = new DateTime(2023, m, 5);
                var o = new NumberFormatOptions { Culture = cur };
                Console.WriteLine($"            {m,2}: {NumberFormatter.Format(day, "mmm", o)} | {NumberFormatter.Format(day, "mmmm", o)} | {NumberFormatter.Format(day, "dd-mmm-yyyy", o)} | {Id15(cur, day)}");
            }
        });

        g.Finish();
    }

    private static string Id15(CultureInfo culture, DateTime day)
    {
        var ms = new MemoryStream();
        using (var w = XlsxWriter.Create(ms))
        {
            using (var s = w.AddSheet("S")) s.WriteRow(r => r.Cell(day, new CellStyleBuilder().Format("d-mmm-yy").Build()));
            w.Save();
        }
        ms.Position = 0;
        return Workbook.Load(ms, null, new XlsxReaderOptions { DisplayCulture = culture }).Sheets[0].Cell(1, 1).DisplayText;
    }

    /// <summary>Copies an xlsx and declares <paramref name="code"/> for <paramref name="id"/> in its styles part.</summary>
    private static void DeclareCode(string source, string target, int id, string code)
    {
        File.Copy(source, target, overwrite: true);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Update);
        var entry = zip.GetEntry("xl/styles.xml")!;
        string xml;
        using (var r = new StreamReader(entry.Open())) xml = r.ReadToEnd();
        string numFmt = $"<numFmt numFmtId=\"{id}\" formatCode=\"{System.Security.SecurityElement.Escape(code)}\"/>";
        int numFmts = xml.IndexOf("<numFmts", StringComparison.Ordinal);
        if (numFmts >= 0)
        {
            int close = xml.IndexOf('>', numFmts);
            string open = xml.Substring(numFmts, close - numFmts + 1);
            var count = System.Text.RegularExpressions.Regex.Match(open, "count=\"(\\d+)\"");
            string newOpen = count.Success
                ? open.Replace(count.Value, $"count=\"{int.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture) + 1}\"")
                : open;
            xml = xml.Substring(0, numFmts) + newOpen + numFmt + xml.Substring(close + 1);
        }
        else
        {
            int styleSheet = xml.IndexOf('>', xml.IndexOf("<styleSheet", StringComparison.Ordinal));
            xml = xml.Insert(styleSheet + 1, "<numFmts count=\"1\">" + numFmt + "</numFmts>");
        }
        entry.Delete();
        var created = zip.CreateEntry("xl/styles.xml");
        using var wtr = new StreamWriter(created.Open(), new UTF8Encoding(false));
        wtr.Write(xml);
    }
}
