using System;
using System.Globalization;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// NumberFormatter (Sheets 1.3.0): display text for Excel number format codes. Expected values
/// are what Excel shows; the renderer was also diffed against LibreOffice over a 26 × 60
/// value/format grid (differences are only where LibreOffice itself departs from Excel).
/// </summary>
internal static class NumberFormatTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[NumberFormatTest] Excel number format rendering...");
        Console.WriteLine();
        int pass = 0, fail = 0;

        void Eq(string label, string expected, string actual)
        {
            if (expected == actual) { pass++; return; }
            fail++;
            Console.WriteLine($"    FAIL: {label}: expected <{expected}> got <{actual}>");
        }

        var inv = NumberFormatOptions.Default;
        var enIN = new NumberFormatOptions { Culture = CultureInfo.GetCultureInfo("en-IN") };
        var de = new NumberFormatOptions { Culture = CultureInfo.GetCultureInfo("de-DE") };
        string F(object? v, string? f, NumberFormatOptions? o = null) => NumberFormatter.Format(v, f, o ?? inv);

        // ---- General ----
        Eq("General int", "1234", F(1234, null));
        Eq("General decimal", "1234.5", F(1234.5, "General"));
        Eq("General pi (11 chars)", "3.141592654", F(Math.PI, "General"));
        Eq("General small", "0.000123", F(0.000123, "General"));
        Eq("General 1/3", "0.333333333", F(1.0 / 3, "General"));
        Eq("General large → sci", "1.23457E+14", F(123456789012345d, "General"));
        Eq("General tiny → sci", "1E-10", F(1e-10, "General"));
        Eq("General negative", "-1234.5", F(-1234.5, "General"));
        Eq("General zero", "0", F(0, "General"));

        // ---- Numbers ----
        Eq("0.00", "1234.50", F(1234.5, "0.00"));
        Eq("#,##0.00", "1,234.50", F(1234.5, "#,##0.00"));
        Eq("#,##0 rounding half away", "1,235", F(1234.5, "#,##0"));
        Eq("#,##0 negative", "-1,235", F(-1234.5, "#,##0"));
        Eq("#,##0 en-IN lakh grouping", "1,00,00,000", F(10000000, "#,##0", enIN));
        Eq("#,##0.00 de-DE separators", "1.234,50", F(1234.5, "#,##0.00", de));
        Eq("thousands scaling", "1,235", F(1234567, "#,##0,"));
        Eq("millions with literal", "1.2M", F(1234567.891, "#,##0.0,,\"M\""));
        Eq("percent", "25.6%", F(0.256, "0.0%"));
        Eq("escaped percent is literal", "25.6%", F(25.6, "0.0\\%"));
        Eq("scientific", "1.23E+04", F(12345, "0.00E+00"));
        Eq("scientific negative exponent", "1.23E-04", F(0.000123, "0.00E+00"));
        Eq("engineering", "12.3E+3", F(12345, "##0.0E+0"));
        Eq("optional decimals", "1.5", F(1.5, "0.0##"));
        Eq("? padding", "  1.5  ", F(1.5, "???.???"));
        Eq("leading zeros", "00042", F(42, "00000"));
        Eq("literal digits pattern", "123-45-6789", F(123456789, "000-00-0000"));
        Eq("quoted units", "7 units", F(7, "0 \"units\""));
        Eq("currency tag", "€1,234.50", F(1234.5, "[$€-407]#,##0.00"));
        Eq("backslash literal", "$0.50", F(0.5, "\\$0.00"));
        Eq("negative zero suppressed", "0.00", F(-0.001, "0.00"));

        // ---- Sections, conditions, colours ----
        Eq("2 sections negative", "(1,234.50)", F(-1234.5, "#,##0.00;(#,##0.00)"));
        Eq("3 sections zero", "zero", F(0, "0;-0;\"zero\""));
        Eq("hide negatives", "", F(-5, "0;;"));
        Eq("conditional large", "1,235", F(1234.5, "[>=1000]#,##0;[<0]-0.0;0.000"));
        Eq("conditional negative uses own sign", "-1.0", F(-1, "[>=1000]#,##0;[<0]-0.0;0.000"));
        Eq("conditional else", "0.500", F(0.5, "[>=1000]#,##0;[<0]-0.0;0.000"));
        var red = NumberFormatter.FormatDetailed(-3, "0;[Red]0");
        Eq("colour text", "3", red.Text);
        Eq("colour value", "FF0000", red.Color ?? "null");
        Eq("[Color10] = palette 17", "008000", NumberFormatter.FormatDetailed(1, "[Color10]0").Color ?? "null");
        var acct = NumberFormatter.FormatDetailed(1234.5, "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)");
        Eq("accounting text", " 1,234.50 ", acct.Text);
        Eq("accounting fill", " ", acct.FillCharacter?.ToString() ?? "null");
        Eq("accounting zero", " -   ", F(0, "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)"));

        // ---- Fractions ----
        Eq("mixed fraction", "1 1/2", F(1.5, "# ?/?"));
        Eq("proper fraction under 1", " 3/4", F(0.75, "# ?/?"));
        Eq("two-digit denominator", "3 14/99", F(3.1414, "# ??/??"));
        Eq("fixed denominator", " 25/100", F(0.25, "# ??/100"));
        Eq("improper fraction", "3/2", F(1.5, "?/?"));

        // ---- Text ----
        Eq("text section", "Text: abc", F("abc", "0;-0;0;\"Text: \"@"));
        Eq("text without text section", "abc", F("abc", "0.00"));
        Eq("@ with number → General", "12.5", F(12.5, "@"));
        Eq("0;@ text", "x!", F("x", "0;@\"!\""));
        Eq("bool", "TRUE", F(true, "0.00"));
        Eq("null", "", F(null, "0.00"));

        // ---- Dates and times ----
        Eq("dd/mm/yyyy", "15/03/2023", F(45000, "dd/mm/yyyy"));
        // Culture data differs by OS (Windows en-IN uses '-', ICU on Linux uses '/'), so pin it.
        var dash = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        dash.DateTimeFormat.DateSeparator = "-";
        Eq("'/' follows culture date separator", "15-03-2023", F(45000, "dd/mm/yyyy", new NumberFormatOptions { Culture = dash }));
        Eq("DateTime value", "2023-03-15", F(new DateTime(2023, 3, 15), "yyyy-mm-dd"));
        Eq("long date", "Wednesday, March 15, 2023", F(45000.75, "dddd, mmmm d, yyyy"));
        Eq("ddd mmm", "Wed Mar", F(45000, "ddd mmm"));
        Eq("mmmmm initial", "M", F(45000, "mmmmm"));
        Eq("d-mmm-yy", "15-Mar-23", F(45000, "d-mmm-yy"));
        Eq("h:mm AM/PM", "6:00 PM", F(45000.75, "h:mm AM/PM"));
        Eq("noon", "12:00 PM", F(0.5, "h:mm AM/PM"));
        Eq("am/pm lower", "6:00 pm", F(0.75, "h:mm am/pm"));
        Eq("A/P", "6:00P", F(0.75, "h:mmA/P"));
        Eq("m after h = minutes", "18:30", F(45000.7708333333, "h:mm"));
        Eq("mm before ss = minutes", "30:00", F(0.5 / 24, "mm:ss"));
        Eq("seconds rounding to next day", "2023-03-17 00:00:00", F(45001.999999, "yyyy-mm-dd hh:mm:ss"));
        Eq("fractional seconds", "00:00:01.500", F(1.5 / 86400, "hh:mm:ss.000"));
        Eq("elapsed hours", "30:00:00", F(1.25, "[h]:mm:ss"));
        Eq("elapsed minutes", "1800:00", F(1.25, "[mm]:ss"));
        Eq("TimeSpan value", "36:15", F(new TimeSpan(1, 12, 15, 0), "[h]:mm"));
        Eq("1900 leap bug: serial 60", "29-02-1900", F(60, "dd-mm-yyyy"));
        Eq("serial 61", "01-03-1900", F(61, "dd-mm-yyyy"));
        Eq("serial 1", "01-01-1900", F(1, "dd-mm-yyyy"));
        Eq("serial 0", "00-01-1900", F(0, "dd-mm-yyyy"));
        Eq("1904 system", "1904-01-01", F(0, "yyyy-mm-dd", new NumberFormatOptions { Date1904 = true }));
        Eq("negative date", "########", F(-1, "yyyy-mm-dd"));
        Eq("ToSerial 1900-03-01", "61", NumberFormatter.ToSerial(new DateTime(1900, 3, 1)).ToString(CultureInfo.InvariantCulture));
        Eq("ToSerial 1900-01-01", "1", NumberFormatter.ToSerial(new DateTime(1900, 1, 1)).ToString(CultureInfo.InvariantCulture));
        Eq("ToSerial 2023-03-15", "45000", NumberFormatter.ToSerial(new DateTime(2023, 3, 15)).ToString(CultureInfo.InvariantCulture));
        Eq("IsDateTime flag", "True", NumberFormatter.FormatDetailed(1, "yyyy").IsDateTime.ToString());

        // ---- Built-in format codes from CellStyleTable ----
        foreach (var kv in CellStyleTable.BuiltInNumberFormats)
        {
            try { NumberFormatter.Format(45000.75, kv.Value); pass++; }
            catch (Exception ex) { fail++; Console.WriteLine($"    FAIL: built-in {kv.Key} '{kv.Value}' threw {ex.GetType().Name}"); }
        }

        Console.WriteLine($"[NumberFormatTest] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"NumberFormatTest: {fail} check(s) failed.");
    }
}
