using System;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;
using Chuvadi.Sheets.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Step-2 manual verification. Builds an xlsx that exercises BOTH registries:
///   - A row of header strings (which exercises the SharedStringTable)
///   - A row of styled data cells (which exercises the StyleRegistry):
///       * A bold red "Hello" string
///       * A number 42 with currency format
///       * A number 3.14159 with 2-decimal format
///       * A date with date format
///   - A second row of plain data for visual contrast
///
/// If Excel opens the produced "styled.xlsx" without warnings AND shows the styling
/// (colors, bold, formatting) correctly, step 2 is verified.
/// </summary>
internal static class StyledXlsxTest
{
    // ---- OOXML constants -----------------------------------------------------------

    private const string RelOfficeDocument =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelWorksheet =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string RelStyles =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string RelSharedStrings =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";

    private const string CtWorkbook =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string CtWorksheet =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string CtStyles =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private const string CtSharedStrings =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";

    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ---- Entry point ---------------------------------------------------------------

    public static void Run(string outputDir)
    {
        Console.WriteLine("[StyledXlsxTest] Building styled xlsx with shared strings and styles...");
        var outPath = Path.Combine(outputDir, "styled.xlsx");
        if (File.Exists(outPath)) File.Delete(outPath);

        WriteStyled(outPath);
        Console.WriteLine($"[StyledXlsxTest] Wrote: {outPath} ({new FileInfo(outPath).Length} bytes)");
        Console.WriteLine("[StyledXlsxTest] OK — open in Excel. Expected:");
        Console.WriteLine("    Row 1: bold headers Name / Amount / Pi / Date");
        Console.WriteLine("    Row 2: bold red 'Hello' | $42.00 | 3.14 | a date");
        Console.WriteLine("    Row 3: plain 'World' | $99.50 | 2.72 | another date");
    }

    // ---- Build path ----------------------------------------------------------------

    private static void WriteStyled(string path)
    {
        // The two registries accumulate state as we describe cells, then write XML at the end.
        var sst = new SharedStringTable();
        var styles = new StyleRegistry();

        // Pre-register the styles we plan to use. The integers returned become cells' s="N".
        var headerStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Bold()
            .Background("#1A237E")
            .Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center)
            .Build());

        var boldRedStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Bold()
            .Foreground("#C62828")
            .Build());

        var currencyStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Format("$#,##0.00")
            .Build());

        var twoDecimalStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Format("0.00")
            .Build());

        var dateStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Format("yyyy-mm-dd")
            .Build());

        // Pre-register strings. The integers returned become cells' <v> values when t="s".
        var sName   = sst.GetOrAdd("Name");
        var sAmount = sst.GetOrAdd("Amount");
        var sPi     = sst.GetOrAdd("Pi");
        var sDate   = sst.GetOrAdd("Date");
        var sHello  = sst.GetOrAdd("Hello");
        var sWorld  = sst.GetOrAdd("World");

        // Dates in xlsx are stored as serial numbers (days since 1900-01-01, with the
        // famous 1900-leap-year quirk). DateTime.ToOADate() does exactly this conversion.
        var date1 = new DateTime(2026, 1, 15).ToOADate();
        var date2 = new DateTime(2026, 6, 30).ToOADate();

        // Now write the package.
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var pkg = OoxmlPackage.Create(fs);

        // ---- xl/workbook.xml -------------------------------------------------------
        using (var s = pkg.CreatePart("/xl/workbook.xml", CtWorkbook))
        using (var w = XmlWriter.Create(s, MakeSettings()))
        {
            w.WriteStartDocument(standalone: true);
            w.WriteStartElement("workbook", SsNs);
            w.WriteAttributeString("xmlns", "r", null, RelNs);
            w.WriteStartElement("sheets", SsNs);
            w.WriteStartElement("sheet", SsNs);
            w.WriteAttributeString("name", "Demo");
            w.WriteAttributeString("sheetId", "1");
            w.WriteAttributeString("id", RelNs, "rId1");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }

        // ---- xl/worksheets/sheet1.xml ----------------------------------------------
        using (var s = pkg.CreatePart("/xl/worksheets/sheet1.xml", CtWorksheet))
        using (var w = XmlWriter.Create(s, MakeSettings()))
        {
            w.WriteStartDocument(standalone: true);
            w.WriteStartElement("worksheet", SsNs);

            // Column widths so the styled output is visible at first glance.
            w.WriteStartElement("cols", SsNs);
            WriteCol(w, min: 1, max: 4, width: 14.0);
            w.WriteEndElement();

            w.WriteStartElement("sheetData", SsNs);

            // Row 1 — headers (all string cells, all using headerStyleId).
            WriteRow(w, rowNum: 1, cells: new[]
            {
                StringCell("A1", sName,   headerStyleId),
                StringCell("B1", sAmount, headerStyleId),
                StringCell("C1", sPi,     headerStyleId),
                StringCell("D1", sDate,   headerStyleId),
            });

            // Row 2 — styled data.
            WriteRow(w, rowNum: 2, cells: new[]
            {
                StringCell("A2", sHello, boldRedStyleId),
                NumberCell("B2", "42",         currencyStyleId),
                NumberCell("C2", "3.14159",    twoDecimalStyleId),
                NumberCell("D2", date1.ToString("R", System.Globalization.CultureInfo.InvariantCulture), dateStyleId),
            });

            // Row 3 — plain data (no style id → uses default).
            WriteRow(w, rowNum: 3, cells: new[]
            {
                StringCell("A3", sWorld,   styleId: null),
                NumberCell("B3", "99.5",   currencyStyleId),
                NumberCell("C3", "2.71828", twoDecimalStyleId),
                NumberCell("D3", date2.ToString("R", System.Globalization.CultureInfo.InvariantCulture), dateStyleId),
            });

            w.WriteEndElement(); // </sheetData>
            w.WriteEndElement(); // </worksheet>
            w.WriteEndDocument();
        }

        // ---- xl/styles.xml ---------------------------------------------------------
        using (var s = pkg.CreatePart("/xl/styles.xml", CtStyles))
        {
            styles.WriteTo(s);
        }

        // ---- xl/sharedStrings.xml --------------------------------------------------
        using (var s = pkg.CreatePart("/xl/sharedStrings.xml", CtSharedStrings))
        {
            sst.WriteTo(s);
        }

        // ---- Relationships ---------------------------------------------------------
        // Root → workbook
        pkg.AddRelationship("/", "/xl/workbook.xml", RelOfficeDocument, "rId1");

        // Workbook → sheet, styles, sharedStrings (in this order, all unique rIds within
        // this source's relationship set).
        pkg.AddRelationship("/xl/workbook.xml", "/xl/worksheets/sheet1.xml", RelWorksheet,     "rId1");
        pkg.AddRelationship("/xl/workbook.xml", "/xl/styles.xml",            RelStyles,        "rId2");
        pkg.AddRelationship("/xl/workbook.xml", "/xl/sharedStrings.xml",     RelSharedStrings, "rId3");

        pkg.Close();
    }

    // ---- Cell + row helpers --------------------------------------------------------

    /// <summary>Describes one cell to be written. Kept as a small POCO for readability.</summary>
    private sealed record CellSpec(
        string Address,
        string? Type,         // "s" for shared string, null for numeric
        string Value,
        int? StyleId);

    private static CellSpec StringCell(string address, int sharedStringId, int? styleId) =>
        new(address, "s", sharedStringId.ToString(System.Globalization.CultureInfo.InvariantCulture), styleId);

    private static CellSpec NumberCell(string address, string numericValue, int? styleId) =>
        new(address, null, numericValue, styleId);

    private static void WriteRow(XmlWriter w, int rowNum, CellSpec[] cells)
    {
        w.WriteStartElement("row", SsNs);
        w.WriteAttributeString("r", rowNum.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var cell in cells)
        {
            w.WriteStartElement("c", SsNs);
            w.WriteAttributeString("r", cell.Address);
            if (cell.StyleId is int sid)
                w.WriteAttributeString("s", sid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (cell.Type is not null)
                w.WriteAttributeString("t", cell.Type);

            w.WriteStartElement("v", SsNs);
            w.WriteString(cell.Value);
            w.WriteEndElement(); // </v>

            w.WriteEndElement(); // </c>
        }

        w.WriteEndElement(); // </row>
    }

    private static void WriteCol(XmlWriter w, int min, int max, double width)
    {
        w.WriteStartElement("col", SsNs);
        w.WriteAttributeString("min", min.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteAttributeString("max", max.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteAttributeString("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteAttributeString("customWidth", "1");
        w.WriteEndElement();
    }

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
