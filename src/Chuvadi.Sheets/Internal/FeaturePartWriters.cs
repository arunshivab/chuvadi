using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

using Chuvadi.Internal;

/// <summary>
/// Writers for the feature parts that step 4 introduces — tables, comments, and VML drawings.
/// Each method takes a Stream and writes a complete, standalone XML/VML document into it.
/// </summary>
internal static class FeaturePartWriters
{
    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---- Tables --------------------------------------------------------------------

    /// <summary>
    /// Writes a complete xl/tables/tableN.xml document for one table. Tables describe a
    /// structured region: name, range, columns, and a reference to a built-in style.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="tableId">The workbook-unique table id (used as the 'id' attribute and part name).</param>
    /// <param name="table">The table definition recorded by the sheet writer.</param>
    public static void WriteTable(Stream output, int tableId, SheetTable table)
    {
        using var w = XmlWriter.Create(output, MakeSettings());
        w.WriteStartDocument(standalone: true);

        w.WriteStartElement("table", SsNs);
        w.WriteAttributeString("id", tableId.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("name", table.Name);
        w.WriteAttributeString("displayName", table.DisplayName);
        w.WriteAttributeString("ref", table.Range);
        w.WriteAttributeString("totalsRowShown", "0");

        // <autoFilter ref="..."/> — gives the dropdown filters on header.
        w.WriteStartElement("autoFilter", SsNs);
        w.WriteAttributeString("ref", table.Range);
        w.WriteEndElement();

        // <tableColumns> — one entry per header.
        w.WriteStartElement("tableColumns", SsNs);
        w.WriteAttributeString("count", table.ColumnHeaders.Length.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < table.ColumnHeaders.Length; i++)
        {
            w.WriteStartElement("tableColumn", SsNs);
            // Column id within the table — 1-based.
            w.WriteAttributeString("id", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("name", table.ColumnHeaders[i]);
            w.WriteEndElement();
        }
        w.WriteEndElement(); // </tableColumns>

        // <tableStyleInfo> — chooses the built-in style by name.
        var styleName = TableStyleNames.ToOoxmlName(table.Style);
        if (styleName is not null)
        {
            w.WriteStartElement("tableStyleInfo", SsNs);
            w.WriteAttributeString("name", styleName);
            w.WriteAttributeString("showFirstColumn", "0");
            w.WriteAttributeString("showLastColumn", "0");
            w.WriteAttributeString("showRowStripes", "1");
            w.WriteAttributeString("showColumnStripes", "0");
            w.WriteEndElement();
        }

        w.WriteEndElement(); // </table>
        w.WriteEndDocument();
    }

    // ---- Comments ------------------------------------------------------------------

    /// <summary>
    /// Writes xl/comments&lt;N&gt;.xml. Each comment lists the author by index into an
    /// authors table at the top of the document.
    /// </summary>
    public static void WriteComments(Stream output, IReadOnlyList<SheetComment> comments)
    {
        // Build the authors table — deduplicated, ordinal comparison.
        var authorIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderedAuthors = new List<string>();
        foreach (var c in comments)
        {
            if (!authorIndex.ContainsKey(c.Author))
            {
                authorIndex[c.Author] = orderedAuthors.Count;
                orderedAuthors.Add(c.Author);
            }
        }

        using var w = XmlWriter.Create(output, MakeSettings());
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("comments", SsNs);

        w.WriteStartElement("authors", SsNs);
        foreach (var a in orderedAuthors)
        {
            w.WriteStartElement("author", SsNs);
            w.WriteString(a);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteStartElement("commentList", SsNs);
        foreach (var c in comments)
        {
            w.WriteStartElement("comment", SsNs);
            w.WriteAttributeString("ref", c.CellAddress);
            w.WriteAttributeString("authorId", authorIndex[c.Author].ToString(CultureInfo.InvariantCulture));
            w.WriteStartElement("text", SsNs);
            w.WriteStartElement("r", SsNs);
            w.WriteStartElement("rPr", SsNs);
            w.WriteStartElement("sz", SsNs); w.WriteAttributeString("val", "9"); w.WriteEndElement();
            w.WriteStartElement("rFont", SsNs); w.WriteAttributeString("val", "Tahoma"); w.WriteEndElement();
            w.WriteEndElement(); // </rPr>
            w.WriteStartElement("t", SsNs);
            w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(c.Text);
            w.WriteEndElement(); // </t>
            w.WriteEndElement(); // </r>
            w.WriteEndElement(); // </text>
            w.WriteEndElement(); // </comment>
        }
        w.WriteEndElement(); // </commentList>

        w.WriteEndElement(); // </comments>
        w.WriteEndDocument();
    }

    // ---- VML drawing ---------------------------------------------------------------

    /// <summary>
    /// Writes the VML drawing that defines comment shapes/positions. VML (Vector Markup
    /// Language) is a defunct 1990s Microsoft format that Excel still requires for comment
    /// rendering. Most of the markup is boilerplate; the per-comment part is the row/column
    /// anchor inside the &lt;ClientData&gt; element.
    ///
    /// We use a fixed comment box geometry (offsets in cell-units) — three columns wide,
    /// six rows tall, positioned to the right of the anchor cell. Users who need custom
    /// positioning will need to wait for a future drawing API.
    /// </summary>
    public static void WriteVmlDrawing(Stream output, IReadOnlyList<SheetComment> comments)
    {
        // VML uses different namespaces and a non-XML-compliant set of conventions. We
        // write it as a string template rather than via XmlWriter to avoid namespace pain
        // with XmlWriter's strict prefix handling.
        var sb = new StringBuilder();
        sb.Append("<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
        sb.Append("<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout>");
        sb.Append("<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\">");
        sb.Append("<v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/>");
        sb.Append("</v:shapetype>");

        int shapeId = 1024; // Arbitrary base id; just needs to be unique within the drawing.
        foreach (var c in comments)
        {
            var (row, col) = CellAddress.ParseA1(c.CellAddress);
            // VML is 0-based for row/column anchors.
            var anchorRow = row - 1;
            var anchorCol = col - 1;
            // Default geometry: comment box anchored at (col+1, row), spanning 2 cols × 4 rows.
            // The Anchor attribute is "leftCol, leftColOffset, leftRow, leftRowOffset, rightCol, rightColOffset, rightRow, rightRowOffset".
            sb.Append("<v:shape id=\"_x0000_s").Append(shapeId).Append("\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:80pt;margin-top:5pt;width:108pt;height:60pt;z-index:");
            sb.Append(shapeId - 1023);
            sb.Append(";visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\"><v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/><v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"/></v:textbox>");
            sb.Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/>");
            sb.Append("<x:Anchor>");
            sb.Append(anchorCol + 1).Append(", 15, ");
            sb.Append(anchorRow).Append(", 10, ");
            sb.Append(anchorCol + 3).Append(", 15, ");
            sb.Append(anchorRow + 4).Append(", 10");
            sb.Append("</x:Anchor>");
            sb.Append("<x:AutoFill>False</x:AutoFill>");
            sb.Append("<x:Row>").Append(anchorRow).Append("</x:Row>");
            sb.Append("<x:Column>").Append(anchorCol).Append("</x:Column>");
            sb.Append("</x:ClientData></v:shape>");
            shapeId++;
        }

        sb.Append("</xml>");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    // ---- Helpers -------------------------------------------------------------------

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };
}
