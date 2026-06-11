using System;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Internal;
using Chuvadi.Internal;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Step-1 manual verification. Builds the absolute minimum viable xlsx using only OoxmlPackage
/// and XmlWriter: one sheet, one cell containing the number 42, no styles, no shared strings.
///
/// Definition of done for step 1:
///   1. This program runs without exceptions.
///   2. The produced "minimal.xlsx" opens in Excel without "we found a problem" warnings.
///   3. The reopened package enumerates the parts and relationships we wrote.
/// </summary>
internal static class MinimalXlsxTest
{
    // ---- OOXML constants (relationship type URIs and content types) ----------------
    //
    // These strings are part of the OOXML standard (ECMA-376 / ISO/IEC 29500) and
    // MUST match exactly — Excel rejects files whose relationship type strings deviate.

    private const string RelOfficeDocument =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string RelWorksheet =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

    private const string CtWorkbook =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string CtWorksheet =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";

    // SpreadsheetML namespace used inside workbook.xml and sheet XML files.
    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // The relationships namespace used for the r:id attribute on <sheet>.
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ---- Entry point ---------------------------------------------------------------

    public static void Run(string outputDir)
    {
        Console.WriteLine("[MinimalXlsxTest] Building minimal xlsx...");
        var outPath = Path.Combine(outputDir, "minimal.xlsx");
        if (File.Exists(outPath)) File.Delete(outPath);

        WriteMinimal(outPath);
        Console.WriteLine($"[MinimalXlsxTest] Wrote: {outPath} ({new FileInfo(outPath).Length} bytes)");

        Console.WriteLine("[MinimalXlsxTest] Reopening and inspecting package...");
        ReopenAndInspect(outPath);

        Console.WriteLine("[MinimalXlsxTest] OK — open the file in Excel to verify it loads without warnings.");
    }

    // ---- Write path ----------------------------------------------------------------

    private static void WriteMinimal(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var pkg = OoxmlPackage.Create(fs);

        // 1. Workbook part (xl/workbook.xml) — lists one sheet, linked by relationship rId1.
        using (var s = pkg.CreatePart("/xl/workbook.xml", CtWorkbook))
        using (var w = XmlWriter.Create(s, MakeSettings()))
        {
            w.WriteStartDocument(standalone: true);
            w.WriteStartElement("workbook", SsNs);
            w.WriteAttributeString("xmlns", "r", null, RelNs);

            w.WriteStartElement("sheets", SsNs);
            w.WriteStartElement("sheet", SsNs);
            w.WriteAttributeString("name", "Sheet1");
            w.WriteAttributeString("sheetId", "1");
            w.WriteAttributeString("id", RelNs, "rId1");
            w.WriteEndElement(); // </sheet>
            w.WriteEndElement(); // </sheets>

            w.WriteEndElement(); // </workbook>
            w.WriteEndDocument();
        }

        // 2. The sheet itself (xl/worksheets/sheet1.xml) — one row, one cell, value 42.
        using (var s = pkg.CreatePart("/xl/worksheets/sheet1.xml", CtWorksheet))
        using (var w = XmlWriter.Create(s, MakeSettings()))
        {
            w.WriteStartDocument(standalone: true);
            w.WriteStartElement("worksheet", SsNs);

            w.WriteStartElement("sheetData", SsNs);
            w.WriteStartElement("row", SsNs);
            w.WriteAttributeString("r", "1");

            w.WriteStartElement("c", SsNs);
            w.WriteAttributeString("r", "A1");
            // No t="..." attribute → defaults to numeric.
            w.WriteStartElement("v", SsNs);
            w.WriteString("42");
            w.WriteEndElement(); // </v>
            w.WriteEndElement(); // </c>

            w.WriteEndElement(); // </row>
            w.WriteEndElement(); // </sheetData>

            w.WriteEndElement(); // </worksheet>
            w.WriteEndDocument();
        }

        // 3. Relationships: package root → workbook, workbook → sheet.
        pkg.AddRelationship(
            sourceUri: "/",
            targetUri: "/xl/workbook.xml",
            relationshipType: RelOfficeDocument,
            id: "rId1");

        pkg.AddRelationship(
            sourceUri: "/xl/workbook.xml",
            targetUri: "/xl/worksheets/sheet1.xml",
            relationshipType: RelWorksheet,
            id: "rId1");

        pkg.Close();
    }

    // ---- Read path -----------------------------------------------------------------

    private static void ReopenAndInspect(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pkg = OoxmlPackage.Open(fs);

        Console.WriteLine();
        Console.WriteLine("  Parts:");
        foreach (var part in pkg.Parts)
            Console.WriteLine($"    {part.Uri}  ({ShortenContentType(part.ContentType)})");

        Console.WriteLine();
        Console.WriteLine("  Root relationships:");
        foreach (var rel in pkg.GetRelationships("/"))
            Console.WriteLine($"    [{rel.Id}]  {Shorten(rel.Type)}  →  {rel.Target}");

        Console.WriteLine();
        Console.WriteLine("  Workbook relationships:");
        foreach (var rel in pkg.GetRelationships("/xl/workbook.xml"))
            Console.WriteLine($"    [{rel.Id}]  {Shorten(rel.Type)}  →  {rel.Target}");
        Console.WriteLine();
    }

    // ---- Small helpers -------------------------------------------------------------

    private static string Shorten(string typeUri)
    {
        // Display the trailing segment of the relationship type URI for readability,
        // e.g. ".../relationships/worksheet"  →  "worksheet".
        var lastSlash = typeUri.LastIndexOf('/');
        return lastSlash >= 0 ? typeUri.Substring(lastSlash + 1) : typeUri;
    }

    private static string ShortenContentType(string ct)
    {
        // application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml
        //                                        → "spreadsheetml.sheet.main+xml"
        const string prefix = "application/vnd.openxmlformats-officedocument.";
        return ct.StartsWith(prefix, StringComparison.Ordinal) ? ct.Substring(prefix.Length) : ct;
    }

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
