using System.IO.Compression;
using System.Text;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>
/// Hostile workbooks of Audit B, built in memory (D-406, D-413): a conditional format with deeply nested brackets
/// (SEC-08) and nested shape groups (SEC-S1). Shared with the Layout.Documents tests for AutoDetectLayout.
/// </summary>
internal static class CraftedXlsx
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>One cell, and a conditional format on it whose formula is 1 inside <paramref name="depth"/> pairs of brackets.</summary>
    internal static byte[] NestedConditionalFormat(int depth)
    {
        string formula = new string('(', depth) + "1" + new string(')', depth);
        return Workbook("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
            "<conditionalFormatting sqref=\"A1\"><cfRule type=\"expression\" dxfId=\"0\" priority=\"1\"><formula>" + formula
            + "</formula></cfRule></conditionalFormatting>", null);
    }

    /// <summary>One cell, and a drawing whose shape sits inside <paramref name="groups"/> nested shape groups.</summary>
    internal static byte[] NestedShapeGroups(int groups)
    {
        StringBuilder d = new StringBuilder();
        d.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"").Append(Xdr).Append("\" xmlns:a=\"").Append(A).Append("\">");
        d.Append("<xdr:twoCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
        d.Append("<xdr:to><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>");
        for (int i = 0; i < groups; i++)
        {
            d.Append("<xdr:grpSp><xdr:nvGrpSpPr><xdr:cNvPr id=\"").Append(i + 2).Append("\" name=\"G\"/><xdr:cNvGrpSpPr/></xdr:nvGrpSpPr><xdr:grpSpPr/>");
        }
        d.Append("<xdr:sp><xdr:nvSpPr><xdr:cNvPr id=\"1\" name=\"S\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:sp>");
        for (int i = 0; i < groups; i++)
        {
            d.Append("</xdr:grpSp>");
        }
        d.Append("<xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>");
        return Workbook("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", "<drawing r:id=\"rId1\"/>", d.ToString());
    }

    private static byte[] Workbook(string sheetData, string extra, string? drawing)
    {
        string sheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + Rel + "\"><sheetData>"
            + sheetData + "</sheetData>" + extra + "</worksheet>";
        string types = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
            + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
            + (drawing is null ? "" : "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>")
            + "</Types>";
        string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"" + Main + "\">"
            + "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
            + "<borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"1\"><xf xfId=\"0\"/></cellXfs>"
            + "<dxfs count=\"1\"><dxf><fill><patternFill><bgColor rgb=\"FFFFFF00\"/></patternFill></fill></dxf></dxfs></styleSheet>";
        List<(string, string)> parts = new List<(string, string)>
        {
            ("[Content_Types].xml", types),
            ("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"),
            ("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"" + Main + "\" xmlns:r=\"" + Rel + "\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            ("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"" + Rel + "/styles\" Target=\"styles.xml\"/></Relationships>"),
            ("xl/styles.xml", styles),
            ("xl/worksheets/sheet1.xml", sheet),
        };
        if (drawing is not null)
        {
            parts.Add(("xl/worksheets/_rels/sheet1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + Rel + "/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>"));
            parts.Add(("xl/drawings/drawing1.xml", drawing));
        }
        using MemoryStream ms = new MemoryStream();
        using (ZipArchive z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach ((string name, string text) in parts)
            {
                using StreamWriter w = new StreamWriter(z.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }
        return ms.ToArray();
    }
}
