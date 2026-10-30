using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// w:tbl ⇄ <see cref="DocTable"/>: table properties (CT_TblPr order), grid, rows (trPr), cells
/// (CT_TcPr order) and cell content including nested tables. Shared by the writer and the loader.
/// </summary>
internal static class TableXml
{
    private const string W = DocumentSerializer.W;

    // =====================================================================================
    // Write
    // =====================================================================================

    /// <summary>Word's maximum number of grid columns in a table.</summary>
    internal const int MaxColumns = 63;

    public static void Write(XmlWriter x, DocTable t, Action<XmlWriter, Paragraph> writeParagraph)
        => Write(x, t, writeParagraph, new HashSet<DocTable>(ReferenceEqualityComparer.Instance));

    private static void Write(XmlWriter x, DocTable t, Action<XmlWriter, Paragraph> writeParagraph, HashSet<DocTable> open)
    {
        // A table reachable from inside itself would recurse forever (stack overflow kills the
        // process); refuse it with a catchable exception instead.
        if (!open.Add(t)) throw new InvalidOperationException("A table is nested inside itself; it cannot be written.");
        x.WriteStartElement("tbl", W);
        WriteTablePr(x, t);

        x.WriteStartElement("tblGrid", W);
        for (int c = 0; c < t.Columns; c++)
        {
            x.WriteStartElement("gridCol", W);
            if (t.ColumnWidthsPt is not null && c < t.ColumnWidthsPt.Length && t.ColumnWidthsPt[c] > 0)
                x.WriteAttributeString("w", W, RunProperties.Twips(t.ColumnWidthsPt[c]));
            x.WriteEndElement();
        }
        x.WriteEndElement();

        foreach (var row in t.Rows)
        {
            x.WriteStartElement("tr", W);
            WriteRowPr(x, row);

            int col = Math.Clamp(row.GridBefore, 0, t.Columns);
            int end = t.Columns - Math.Clamp(row.GridAfter, 0, t.Columns - col);
            // Placeholder cells for a ragged row's missing trailing cells stay unwritten.
            for (int k = 0; k < row.OmittedTrailingCells && end > col && row.Cells[end - 1].IsPristine; k++) end--;
            while (col < end)
            {
                var cell = row.Cells[col];
                int span = Math.Clamp(cell.ColumnSpan, 1, end - col);
                x.WriteStartElement("tc", W);
                WriteCellPr(x, t, cell, col, span);

                // Every tc must END with a paragraph (also after a nested table).
                bool lastWasParagraph = false;
                foreach (var block in cell.Blocks)
                {
                    if (block is Paragraph p) { writeParagraph(x, p); lastWasParagraph = true; }
                    else if (block is DocTable nested) { Write(x, nested, writeParagraph, open); lastWasParagraph = false; }
                }
                if (!lastWasParagraph) { x.WriteStartElement("p", W); x.WriteEndElement(); }

                x.WriteEndElement(); // tc
                col += span;
            }
            x.WriteEndElement(); // tr
        }
        x.WriteEndElement(); // tbl
        open.Remove(t);
    }

    /// <summary>tblPr of a table: explicit Format values win; otherwise the pre-1.3 behaviour (column widths → fixed layout at their sum; no widths → autofit at auto width; legacy default grid).</summary>
    private static void WriteTablePr(XmlWriter x, DocTable t)
    {
        var f = t.Format;
        bool hasColumnWidths = t.ColumnWidthsPt is { Length: > 0 };
        var width = f.Width ?? (hasColumnWidths ? TableWidth.Points(Sum(t.ColumnWidthsPt!)) : TableWidth.Auto);
        var borders = f.Borders ?? (t.Borders ? TableBorders.Grid(BorderSpec.Single(0.5)) : null);
        var layout = f.Layout ?? (f.Width is null && hasColumnWidths ? TableLayout.Fixed : f.Width is null ? TableLayout.Autofit : (TableLayout?)null);
        WriteTablePrCore(x, f, f.StyleId, width, layout, borders, alwaysWrite: true);
    }

    /// <summary>
    /// CT_TblPr in schema order: tblStyle, tblpPr, tblOverlap, bidiVisual, tblStyleRowBandSize,
    /// tblStyleColBandSize, tblW, jc, tblCellSpacing, tblInd, tblBorders, shd, tblLayout,
    /// tblCellMar, tblLook, tblCaption, tblDescription. Shared by tables and table styles.
    /// </summary>
    private static void WriteTablePrCore(XmlWriter x, TableFormat f, string? styleId, TableWidth? width, TableLayout? layout,
        TableBorders? borders, bool alwaysWrite)
    {
        bool any = alwaysWrite || styleId is not null || f.FloatingPosition is not null || f.AllowOverlap is not null || f.RightToLeft is not null
            || f.RowBandSize is not null || f.ColumnBandSize is not null || width is not null || f.Alignment is not null
            || f.CellSpacingPt is not null || f.IndentPt is not null || borders is { IsEmpty: false } || f.Shading is not null
            || layout is not null || f.CellMargins is { IsEmpty: false } || f.Look is not null || f.Caption is not null
            || f.Description is not null;
        if (!any) return;

        x.WriteStartElement("tblPr", W);
        if (styleId is not null) Val(x, "tblStyle", styleId);
        if (f.FloatingPosition is { } fp) WriteFloating(x, fp);
        if (f.AllowOverlap is bool ov) Val(x, "tblOverlap", ov ? "overlap" : "never");
        Toggle(x, "bidiVisual", f.RightToLeft);
        if (f.RowBandSize is int rb) Val(x, "tblStyleRowBandSize", rb.ToString(CultureInfo.InvariantCulture));
        if (f.ColumnBandSize is int cb) Val(x, "tblStyleColBandSize", cb.ToString(CultureInfo.InvariantCulture));
        if (width is TableWidth w) WriteWidth(x, "tblW", w);
        if (f.Alignment is TableAlignment a) Val(x, "jc", AlignToken(a));
        if (f.CellSpacingPt is double cs) WriteWidth(x, "tblCellSpacing", TableWidth.Points(cs));
        if (f.IndentPt is double ind) WriteWidth(x, "tblInd", TableWidth.Points(ind));
        if (borders is { IsEmpty: false }) WriteTableBorders(x, borders);
        if (f.Shading is not null) FormattingXml.WriteShading(x, f.Shading);
        if (layout is TableLayout l)
        {
            x.WriteStartElement("tblLayout", W);
            x.WriteAttributeString("type", W, l == TableLayout.Fixed ? "fixed" : "autofit");
            x.WriteEndElement();
        }
        if (f.CellMargins is { IsEmpty: false } m) WriteMargins(x, "tblCellMar", m);
        if (f.Look is TableLook look)
        {
            x.WriteStartElement("tblLook", W);
            x.WriteAttributeString("val", W, ((int)look).ToString("X4", CultureInfo.InvariantCulture));
            x.WriteAttributeString("firstRow", W, Bit(look, TableLook.FirstRow));
            x.WriteAttributeString("lastRow", W, Bit(look, TableLook.LastRow));
            x.WriteAttributeString("firstColumn", W, Bit(look, TableLook.FirstColumn));
            x.WriteAttributeString("lastColumn", W, Bit(look, TableLook.LastColumn));
            x.WriteAttributeString("noHBand", W, Bit(look, TableLook.NoHorizontalBand));
            x.WriteAttributeString("noVBand", W, Bit(look, TableLook.NoVerticalBand));
            x.WriteEndElement();
        }
        if (f.Caption is not null) Val(x, "tblCaption", f.Caption);
        if (f.Description is not null) Val(x, "tblDescription", f.Description);
        x.WriteEndElement(); // tblPr
    }

    private static void WriteRowPr(XmlWriter x, TableRow row)
    {
        bool any = row.IsHeader || row.HeightPt is not null || row.CantSplit is not null || row.Alignment is not null
            || row.Hidden is not null || row.GridBefore > 0 || row.GridAfter > 0 || row.WidthBefore is not null
            || row.WidthAfter is not null || row.CellSpacingPt is not null;
        if (!any) return;
        x.WriteStartElement("trPr", W);
        int columns = row.Cells.Count;
        int before = Math.Clamp(row.GridBefore, 0, columns);
        int after = Math.Clamp(row.GridAfter, 0, columns - before);
        if (before > 0) Val(x, "gridBefore", before.ToString(CultureInfo.InvariantCulture));
        if (after > 0) Val(x, "gridAfter", after.ToString(CultureInfo.InvariantCulture));
        if (row.WidthBefore is TableWidth wb) WriteWidth(x, "wBefore", wb);
        if (row.WidthAfter is TableWidth wa) WriteWidth(x, "wAfter", wa);
        Toggle(x, "cantSplit", row.CantSplit);
        if (row.HeightPt is double h)
        {
            x.WriteStartElement("trHeight", W);
            x.WriteAttributeString("val", W, RunProperties.Twips(h));
            x.WriteAttributeString("hRule", W, row.HeightRule switch
            {
                RowHeightRule.Exact => "exact",
                RowHeightRule.Auto => "auto",
                _ => "atLeast",
            });
            x.WriteEndElement();
        }
        if (row.IsHeader) { x.WriteStartElement("tblHeader", W); x.WriteEndElement(); }
        if (row.CellSpacingPt is double sp) WriteWidth(x, "tblCellSpacing", TableWidth.Points(sp));
        if (row.Alignment is TableAlignment a) Val(x, "jc", AlignToken(a));
        Toggle(x, "hidden", row.Hidden);
        x.WriteEndElement();
    }

    /// <summary>CT_TcPr order: tcW, gridSpan, vMerge, tcBorders, shd, noWrap, tcMar, textDirection, tcFitText, vAlign, hideMark.</summary>
    private static void WriteCellPr(XmlWriter x, DocTable t, TableCell cell, int col, int span)
    {
        TableWidth width;
        if (cell.Width is TableWidth cw) width = cw;
        else
        {
            // A spanned cell is as wide as the columns it covers (previously only the first one).
            double sum = 0;
            bool known = t.ColumnWidthsPt is not null;
            for (int c = col; known && c < col + span; c++)
            {
                if (c < t.ColumnWidthsPt!.Length && t.ColumnWidthsPt[c] > 0) sum += t.ColumnWidthsPt[c];
                else known = false;
            }
            width = known ? TableWidth.Points(sum) : TableWidth.Auto;
        }
        WriteCellPrBody(x, cell, width, span);
    }

    private static void WriteCellPrBody(XmlWriter x, TableCell cell, TableWidth? writeWidth, int span)
    {
        x.WriteStartElement("tcPr", W);
        if (writeWidth is TableWidth width) WriteWidth(x, "tcW", width);

        if (span > 1) Val(x, "gridSpan", span.ToString(CultureInfo.InvariantCulture));
        if (cell.HorizontalMerge is HorizontalMerge hm)
        {
            x.WriteStartElement("hMerge", W);
            if (hm == HorizontalMerge.Restart) x.WriteAttributeString("val", W, "restart");
            x.WriteEndElement();
        }
        if (cell.VerticalMerge is VerticalMerge vm)
        {
            x.WriteStartElement("vMerge", W);
            if (vm == VerticalMerge.Restart) x.WriteAttributeString("val", W, "restart");
            x.WriteEndElement();
        }
        if (cell.Borders is { IsEmpty: false } b)
        {
            x.WriteStartElement("tcBorders", W);
            if (b.Top is not null) FormattingXml.WriteBorder(x, "top", b.Top);
            if (b.Left is not null) FormattingXml.WriteBorder(x, "left", b.Left);
            if (b.Bottom is not null) FormattingXml.WriteBorder(x, "bottom", b.Bottom);
            if (b.Right is not null) FormattingXml.WriteBorder(x, "right", b.Right);
            if (b.InsideHorizontal is not null) FormattingXml.WriteBorder(x, "insideH", b.InsideHorizontal);
            if (b.InsideVertical is not null) FormattingXml.WriteBorder(x, "insideV", b.InsideVertical);
            if (b.DiagonalDown is not null) FormattingXml.WriteBorder(x, "tl2br", b.DiagonalDown);
            if (b.DiagonalUp is not null) FormattingXml.WriteBorder(x, "tr2bl", b.DiagonalUp);
            x.WriteEndElement();
        }
        if (cell.Shading is not null) FormattingXml.WriteShading(x, cell.Shading);
        else if (cell.ShadeHex is not null) FormattingXml.WriteShading(x, Shading.Fill(cell.ShadeHex));
        Toggle(x, "noWrap", cell.NoWrap);
        if (cell.Margins is { IsEmpty: false } m) WriteMargins(x, "tcMar", m);
        if (cell.TextDirection is CellTextDirection td) Val(x, "textDirection", TextDirectionToken(td));
        Toggle(x, "tcFitText", cell.FitText);
        if (cell.VerticalAlignment is CellVerticalAlignment va)
            Val(x, "vAlign", va switch { CellVerticalAlignment.Center => "center", CellVerticalAlignment.Bottom => "bottom", _ => "top" });
        Toggle(x, "hideMark", cell.HideMark);
        x.WriteEndElement();
    }

    // =====================================================================================
    // Read
    // =====================================================================================

    private sealed class PendingCell
    {
        public int Span = 1;
        public TableCell Cell = new();
    }

    private sealed class PendingRow
    {
        public TableRow Props = new(0, false);
        public List<PendingCell> Cells = new();
    }

    /// <summary>
    /// Reads a w:tbl subtree (reader on the tbl element) into a <see cref="DocTable"/> with full
    /// table/row/cell properties and cell content, nested tables included (the XML depth limit
    /// bounds nesting). The grid column count is the larger of tblGrid and the widest row.
    /// </summary>
    public static DocTable Read(XmlReader sub, Func<XmlReader, Paragraph> loadParagraph, Action<XmlReader>? marker = null)
    {
        var format = new TableFormat();
        var gridWidths = new List<double>();
        bool gridHasWidths = false;
        var rows = new List<PendingRow>();

        if (!sub.Read()) return new DocTable(1);
        int childDepth = sub.Depth + 1;
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.Depth != childDepth || sub.NamespaceURI != W) continue;
            switch (sub.LocalName)
            {
                case "tblPr":
                    using (var tp = sub.ReadSubtree()) ReadTablePr(tp, format);
                    break;
                case "tblGrid":
                    using (var g = sub.ReadSubtree())
                    {
                        while (g.Read())
                        {
                            if (g.NodeType != XmlNodeType.Element || g.LocalName != "gridCol" || g.NamespaceURI != W) continue;
                            var w = RunProperties.FromTwips(g.GetAttribute("w", W));
                            gridWidths.Add(w ?? 0);
                            if (w is > 0) gridHasWidths = true;
                        }
                    }
                    break;
                case "tr":
                    using (var tr = sub.ReadSubtree()) rows.Add(ReadRow(tr, loadParagraph, marker));
                    break;
                case "sdt":
                case "customXml":
                    // Rows wrapped in content controls / custom XML: read the rows inside.
                    using (var wrap = sub.ReadSubtree()) ReadWrapped(wrap, "tr", el => rows.Add(ReadRow(el, loadParagraph, marker)));
                    break;
                default:
                    if (marker is not null && IsRangeMarker(sub)) marker(sub);
                    else if (!sub.IsEmptyElement) using (sub.ReadSubtree()) { }
                    break;
            }
        }

        // Grid width: the declared grid, widened only by rows that have more CELLS than it
        // (a huge gridSpan cannot widen it), and never beyond Word's 63 columns.
        int declared = Math.Min(gridWidths.Count, MaxColumns);
        int columns = Math.Max(1, declared);
        foreach (var r in rows)
        {
            // Skipped columns only count up to the declared grid, so a hostile gridBefore cannot widen it.
            int cellsAndSkips = r.Cells.Count + Math.Min(r.Props.GridBefore + r.Props.GridAfter, declared);
            int withSpans = r.Props.GridBefore + r.Props.GridAfter;
            foreach (var c in r.Cells) withSpans += Math.Max(1, c.Span);
            columns = Math.Max(columns, declared > 0 ? cellsAndSkips : withSpans);
        }
        columns = Math.Clamp(columns, 1, MaxColumns);

        var table = new DocTable(columns)
        {
            // An explicit tblBorders becomes Format.Borders; without one the table has no
            // direct borders (the style's apply) — never the legacy default grid.
            Borders = false,
        };
        if (gridHasWidths)
        {
            var widths = new double[columns];
            for (int i = 0; i < columns && i < gridWidths.Count; i++) widths[i] = gridWidths[i];
            table.ColumnWidthsPt = widths;
        }
        CopyFormat(format, table.Format);

        foreach (var r in rows)
        {
            // Placeholder cells stay null until accessed (sparse/hostile tables stay cheap).
            var cells = new TableCell?[columns];
            // Skipped columns give way to real cells: content is never dropped to honour gridBefore/After.
            int cellCount = Math.Min(r.Cells.Count, columns);
            int col = Math.Min(r.Props.GridBefore, columns - cellCount);
            int start = col;
            int contentEnd = columns - Math.Min(r.Props.GridAfter, columns - col - cellCount);
            for (int k = 0; k < cellCount; k++)
            {
                var pc = r.Cells[k];
                int stillToPlace = cellCount - k - 1;
                // Spans give way to later cells too: a huge gridSpan never pushes content out.
                pc.Cell.ColumnSpan = Math.Clamp(pc.Span, 1, Math.Max(1, contentEnd - col - stillToPlace));
                cells[col] = pc.Cell;
                col += pc.Cell.ColumnSpan;
            }
            var row = new TableRow(cells, r.Props.IsHeader)
            {
                OmittedTrailingCells = Math.Max(0, contentEnd - col),
                WidthBefore = r.Props.WidthBefore,
                WidthAfter = r.Props.WidthAfter,
                CellSpacingPt = r.Props.CellSpacingPt,
                HeightPt = r.Props.HeightPt,
                HeightRule = r.Props.HeightRule,
                CantSplit = r.Props.CantSplit,
                Alignment = r.Props.Alignment,
                Hidden = r.Props.Hidden,
                GridBefore = start,
                GridAfter = columns - contentEnd,
            };
            table.AddLoadedRow(row);
        }
        return table;
    }

    /// <summary>Reads tblPr into <paramref name="f"/>; returns true when tblBorders was present.</summary>
    private static bool ReadTablePr(XmlReader r, TableFormat f)
    {
        bool borders = false;
        if (!r.Read()) return false;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "tblStyle": f.StyleId = Attr(r, "val"); break;
                case "bidiVisual": f.RightToLeft = RunProperties.OnOff(r); break;
                case "tblStyleRowBandSize": f.RowBandSize = ParseInt(Attr(r, "val")); break;
                case "tblStyleColBandSize": f.ColumnBandSize = ParseInt(Attr(r, "val")); break;
                case "tblW": f.Width = ReadWidth(r); break;
                case "jc": f.Alignment = ParseAlign(Attr(r, "val")); break;
                case "tblCellSpacing": f.CellSpacingPt = ReadWidth(r) is { Type: TableWidthType.Points } w ? w.Value : null; break;
                case "tblInd": f.IndentPt = ReadWidth(r) is { Type: TableWidthType.Points } i ? i.Value : null; break;
                case "tblBorders":
                    borders = true;
                    using (var b = r.ReadSubtree()) f.Borders = ReadTableBorders(b);
                    break;
                case "shd": f.Shading = FormattingXml.ReadShading(r); break;
                case "tblLayout": f.Layout = Attr(r, "type") == "fixed" ? TableLayout.Fixed : TableLayout.Autofit; break;
                case "tblCellMar":
                    using (var m = r.ReadSubtree()) f.CellMargins = ReadMargins(m);
                    break;
                case "tblLook": f.Look = ReadLook(r); break;
                case "tblCaption": f.Caption = Attr(r, "val"); break;
                case "tblDescription": f.Description = Attr(r, "val"); break;
                case "tblpPr": f.FloatingPosition = ReadFloating(r); break;
                case "tblOverlap": f.AllowOverlap = Attr(r, "val") switch { "never" => false, "overlap" => true, _ => null }; break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return borders;
    }

    /// <summary>Range markers (bookmarks) that may sit between rows and cells; the caller places them.</summary>
    private static bool IsRangeMarker(XmlReader r) => r.NamespaceURI == W && r.LocalName is "bookmarkStart" or "bookmarkEnd";

    private static PendingRow ReadRow(XmlReader r, Func<XmlReader, Paragraph> loadParagraph, Action<XmlReader>? marker)
    {
        var row = new PendingRow();
        if (!r.Read()) return row;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "trPr":
                    using (var tp = r.ReadSubtree()) ReadRowPr(tp, row.Props);
                    break;
                case "tc":
                    using (var tc = r.ReadSubtree()) row.Cells.Add(ReadCell(tc, loadParagraph, marker));
                    break;
                case "sdt":
                case "customXml":
                    // Cells wrapped in content controls / custom XML: read the cells inside.
                    using (var wrap = r.ReadSubtree()) ReadWrapped(wrap, "tc", el => row.Cells.Add(ReadCell(el, loadParagraph, marker)));
                    break;
                default:
                    // tblPrEx (row-level table property exceptions): not modelled.
                    if (marker is not null && IsRangeMarker(r)) marker(r);
                    else if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return row;
    }

    private static void ReadRowPr(XmlReader r, TableRow row, Action? sawHeader = null)
    {
        if (!r.Read()) return;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "gridBefore": row.GridBefore = Math.Clamp(ParseInt(Attr(r, "val")) ?? 0, 0, MaxColumns); break;
                case "gridAfter": row.GridAfter = Math.Clamp(ParseInt(Attr(r, "val")) ?? 0, 0, MaxColumns); break;
                case "wBefore": row.WidthBefore = ReadWidth(r); break;
                case "wAfter": row.WidthAfter = ReadWidth(r); break;
                case "tblCellSpacing": row.CellSpacingPt = ReadWidth(r) is { Type: TableWidthType.Points } sp ? sp.Value : null; break;
                case "cantSplit": row.CantSplit = RunProperties.OnOff(r); break;
                case "trHeight":
                    row.HeightPt = RunProperties.FromTwips(Attr(r, "val"));
                    row.HeightRule = Attr(r, "hRule") switch { "exact" => RowHeightRule.Exact, "auto" => RowHeightRule.Auto, _ => RowHeightRule.AtLeast };
                    break;
                case "tblHeader": row.IsHeader = RunProperties.OnOff(r) == true; sawHeader?.Invoke(); break;
                case "jc": row.Alignment = ParseAlign(Attr(r, "val")); break;
                case "hidden": row.Hidden = RunProperties.OnOff(r); break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
    }

    private static PendingCell ReadCell(XmlReader r, Func<XmlReader, Paragraph> loadParagraph, Action<XmlReader>? marker)
    {
        var pc = new PendingCell();
        var cell = pc.Cell;
        if (!r.Read()) return pc;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth) continue;
            if (DocumentLoader.IsAlternateRepresentation(r) || r.NamespaceURI != W)
            {
                if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                continue;
            }
            switch (r.LocalName)
            {
                case "tcPr":
                    using (var tp = r.ReadSubtree()) pc.Span = ReadCellPr(tp, cell);
                    break;
                case "p":
                    using (var p = r.ReadSubtree()) cell.Paragraphs.Add(loadParagraph(p));
                    break;
                case "tbl":
                    using (var t = r.ReadSubtree()) cell.AppendNestedTable(Read(t, loadParagraph, marker));
                    break;
                case "bookmarkStart":
                case "bookmarkEnd":
                    marker?.Invoke(r);
                    break;
                default:
                    // sdt / customXml wrappers around cell content: keep their paragraphs' text.
                    if (!r.IsEmptyElement)
                    {
                        using var inner = r.ReadSubtree();
                        while (inner.Read())
                        {
                            if (inner.NodeType == XmlNodeType.Element && inner.NamespaceURI == W && inner.LocalName == "p")
                            {
                                using var p = inner.ReadSubtree();
                                cell.Paragraphs.Add(loadParagraph(p));
                            }
                        }
                    }
                    break;
            }
        }
        return pc;
    }

    /// <summary>Reads tcPr into <paramref name="cell"/>; returns the grid span.</summary>
    private static int ReadCellPr(XmlReader r, TableCell cell)
    {
        int span = 1;
        if (!r.Read()) return span;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "tcW": cell.Width = ReadWidth(r); break;
                case "gridSpan": span = Math.Clamp(ParseInt(Attr(r, "val")) ?? 1, 1, MaxColumns); break;
                case "hMerge": cell.HorizontalMerge = Attr(r, "val") == "restart" ? HorizontalMerge.Restart : HorizontalMerge.Continue; break;
                case "vMerge": cell.VerticalMerge = Attr(r, "val") == "restart" ? VerticalMerge.Restart : VerticalMerge.Continue; break;
                case "tcBorders":
                    using (var b = r.ReadSubtree()) cell.Borders = ReadCellBorders(b);
                    break;
                case "shd":
                    var shd = FormattingXml.ReadShading(r);
                    if (FormattingXml.IsPlainFill(shd)) cell.ShadeHex = shd.FillHex; else cell.Shading = shd;
                    break;
                case "noWrap": cell.NoWrap = RunProperties.OnOff(r); break;
                case "tcMar":
                    using (var m = r.ReadSubtree()) cell.Margins = ReadMargins(m);
                    break;
                case "textDirection": cell.TextDirection = ParseTextDirection(Attr(r, "val")); break;
                case "tcFitText": cell.FitText = RunProperties.OnOff(r); break;
                case "vAlign":
                    cell.VerticalAlignment = Attr(r, "val") switch
                    {
                        "center" => CellVerticalAlignment.Center,
                        "bottom" => CellVerticalAlignment.Bottom,
                        "top" => CellVerticalAlignment.Top,
                        _ => null,
                    };
                    break;
                case "hideMark": cell.HideMark = RunProperties.OnOff(r); break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return span;
    }

    /// <summary>
    /// Walks an sdt / customXml wrapper (reader on it) and calls <paramref name="read"/> with a
    /// subtree reader for every w:<paramref name="target"/> inside it, descending through
    /// sdtContent and nested wrappers but never into the targets themselves.
    /// </summary>
    private static void ReadWrapped(XmlReader wrap, string target, Action<XmlReader> read)
    {
        if (!wrap.Read()) return;
        int childDepth = wrap.Depth + 1;
        while (wrap.Read())
        {
            if (wrap.NodeType != XmlNodeType.Element || wrap.Depth != childDepth || wrap.NamespaceURI != W) continue;
            if (wrap.LocalName == target)
            {
                using var el = wrap.ReadSubtree();
                read(el);
            }
            else if (wrap.LocalName is "sdtContent" or "sdt" or "customXml")
            {
                using var inner = wrap.ReadSubtree();
                ReadWrapped(inner, target, read);
            }
            else if (!wrap.IsEmptyElement)
            {
                using (wrap.ReadSubtree()) { } // sdtPr, sdtEndPr, customXmlPr
            }
        }
    }

    // =====================================================================================
    // Shared pieces (also used by table styles)
    // =====================================================================================

    internal static TableBorders ReadTableBorders(XmlReader r)
    {
        var b = new TableBorders();
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "top": b.Top = FormattingXml.ReadBorder(r); break;
                case "left": case "start": b.Left = FormattingXml.ReadBorder(r); break;
                case "bottom": b.Bottom = FormattingXml.ReadBorder(r); break;
                case "right": case "end": b.Right = FormattingXml.ReadBorder(r); break;
                case "insideH": b.InsideHorizontal = FormattingXml.ReadBorder(r); break;
                case "insideV": b.InsideVertical = FormattingXml.ReadBorder(r); break;
            }
        }
        return b;
    }

    internal static CellBorders ReadCellBorders(XmlReader r)
    {
        var b = new CellBorders();
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "top": b.Top = FormattingXml.ReadBorder(r); break;
                case "left": case "start": b.Left = FormattingXml.ReadBorder(r); break;
                case "bottom": b.Bottom = FormattingXml.ReadBorder(r); break;
                case "right": case "end": b.Right = FormattingXml.ReadBorder(r); break;
                case "insideH": b.InsideHorizontal = FormattingXml.ReadBorder(r); break;
                case "insideV": b.InsideVertical = FormattingXml.ReadBorder(r); break;
                case "tl2br": b.DiagonalDown = FormattingXml.ReadBorder(r); break;
                case "tr2bl": b.DiagonalUp = FormattingXml.ReadBorder(r); break;
            }
        }
        return b;
    }

    internal static CellMargins ReadMargins(XmlReader r)
    {
        double? top = null, left = null, bottom = null, right = null;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != W) continue;
            var w = ReadWidth(r);
            double? pt = w.Type == TableWidthType.Points ? w.Value : w.Type == TableWidthType.Nil ? 0 : null;
            switch (r.LocalName)
            {
                case "top": top = pt; break;
                case "left": case "start": left = pt; break;
                case "bottom": bottom = pt; break;
                case "right": case "end": right = pt; break;
            }
        }
        return new CellMargins { TopPt = top, LeftPt = left, BottomPt = bottom, RightPt = right };
    }

    internal static void WriteMargins(XmlWriter x, string element, CellMargins m)
    {
        x.WriteStartElement(element, W);
        if (m.TopPt is double t) WriteWidth(x, "top", TableWidth.Points(t));
        if (m.LeftPt is double l) WriteWidth(x, "left", TableWidth.Points(l));
        if (m.BottomPt is double b) WriteWidth(x, "bottom", TableWidth.Points(b));
        if (m.RightPt is double r) WriteWidth(x, "right", TableWidth.Points(r));
        x.WriteEndElement();
    }

    /// <summary>Writes a CT_TblWidth element: dxa in twips, pct in fiftieths of a percent.</summary>
    internal static void WriteWidth(XmlWriter x, string element, TableWidth w)
    {
        x.WriteStartElement(element, W);
        switch (w.Type)
        {
            case TableWidthType.Points:
                x.WriteAttributeString("w", W, RunProperties.Twips(w.Value));
                x.WriteAttributeString("type", W, "dxa");
                break;
            case TableWidthType.Percent:
                if (!double.IsFinite(w.Value) || w.Value < 0 || w.Value > 1000)
                    throw new ArgumentOutOfRangeException(nameof(w), w.Value, "Percent width must be 0–1000.");
                x.WriteAttributeString("w", W, ((int)Math.Round(w.Value * 50)).ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("type", W, "pct");
                break;
            case TableWidthType.Nil:
                x.WriteAttributeString("w", W, "0");
                x.WriteAttributeString("type", W, "nil");
                break;
            default:
                x.WriteAttributeString("w", W, "0");
                x.WriteAttributeString("type", W, "auto");
                break;
        }
        x.WriteEndElement();
    }

    /// <summary>Reads a CT_TblWidth element (reader on it). "50%" strings and fiftieths both accepted for pct.</summary>
    internal static TableWidth ReadWidth(XmlReader r)
    {
        var type = Attr(r, "type");
        var raw = Attr(r, "w");
        switch (type)
        {
            case "pct":
                if (raw is not null && raw.EndsWith('%')
                    && double.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                    && double.IsFinite(pct) && pct >= 0 && pct <= 1000)
                    return TableWidth.Percent(pct);
                return RunProperties.TryMeasure(raw, out var fifty) && fifty >= 0 && fifty <= 50_000
                    ? TableWidth.Percent(fifty / 50.0) : TableWidth.Auto;
            case "nil":
                return TableWidth.Nil;
            case "auto":
                return TableWidth.Auto;
            default: // dxa or missing
                return RunProperties.FromTwips(raw) is double pt ? TableWidth.Points(pt) : TableWidth.Auto;
        }
    }

    private static TableLook ReadLook(XmlReader r)
    {
        var look = TableLook.None;
        bool anyAttr = false;
        void Flag(string attr, TableLook bit)
        {
            var v = Attr(r, attr);
            if (v is null) return;
            anyAttr = true;
            if (v is "1" or "true" or "on") look |= bit;
        }
        Flag("firstRow", TableLook.FirstRow);
        Flag("lastRow", TableLook.LastRow);
        Flag("firstColumn", TableLook.FirstColumn);
        Flag("lastColumn", TableLook.LastColumn);
        Flag("noHBand", TableLook.NoHorizontalBand);
        Flag("noVBand", TableLook.NoVerticalBand);
        if (!anyAttr && int.TryParse(Attr(r, "val"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var legacy))
            look = (TableLook)(legacy & 0x07E0);
        return look;
    }

    internal static void CopyTableFormat(TableFormat s, TableFormat d) => CopyFormat(s, d);

    internal static void CopyRowFormat(TableRowFormat s, TableRowFormat d)
    {
        d.CantSplit = s.CantSplit ?? d.CantSplit; d.IsHeader = s.IsHeader ?? d.IsHeader; d.Alignment = s.Alignment ?? d.Alignment;
        d.HeightPt = s.HeightPt ?? d.HeightPt; d.HeightRule = s.HeightRule ?? d.HeightRule; d.Hidden = s.Hidden ?? d.Hidden;
    }

    // ---- Table-style pieces (styles.xml) ------------------------------------------------------

    /// <summary>tblPr for a table style or a conditional format: every stated property (CT_TblPr order), nothing implied.</summary>
    internal static void WriteStyleTablePr(XmlWriter x, TableFormat f)
        => WriteTablePrCore(x, f, styleId: null, f.Width, f.Layout, f.Borders, alwaysWrite: false);

    internal static void WriteTableBorders(XmlWriter x, TableBorders borders)
    {
        x.WriteStartElement("tblBorders", W);
        if (borders.Top is not null) FormattingXml.WriteBorder(x, "top", borders.Top);
        if (borders.Left is not null) FormattingXml.WriteBorder(x, "left", borders.Left);
        if (borders.Bottom is not null) FormattingXml.WriteBorder(x, "bottom", borders.Bottom);
        if (borders.Right is not null) FormattingXml.WriteBorder(x, "right", borders.Right);
        if (borders.InsideHorizontal is not null) FormattingXml.WriteBorder(x, "insideH", borders.InsideHorizontal);
        if (borders.InsideVertical is not null) FormattingXml.WriteBorder(x, "insideV", borders.InsideVertical);
        x.WriteEndElement();
    }

    internal static void WriteStyleRowPr(XmlWriter x, TableRowFormat r)
    {
        if (r.IsDefault) return;
        x.WriteStartElement("trPr", W);
        Toggle(x, "cantSplit", r.CantSplit);
        if (r.HeightPt is double h)
        {
            x.WriteStartElement("trHeight", W);
            x.WriteAttributeString("val", W, RunProperties.Twips(h));
            x.WriteAttributeString("hRule", W, (r.HeightRule ?? RowHeightRule.AtLeast) switch
            {
                RowHeightRule.Exact => "exact", RowHeightRule.Auto => "auto", _ => "atLeast",
            });
            x.WriteEndElement();
        }
        Toggle(x, "tblHeader", r.IsHeader);
        if (r.Alignment is TableAlignment a) Val(x, "jc", AlignToken(a));
        Toggle(x, "hidden", r.Hidden);
        x.WriteEndElement();
    }

    internal static void WriteStyleCellPr(XmlWriter x, TableCellFormat c)
    {
        if (c.IsDefault) return;
        var cell = new TableCell
        {
            Shading = c.Shading, Borders = c.Borders, Margins = c.Margins, VerticalAlignment = c.VerticalAlignment,
            TextDirection = c.TextDirection, NoWrap = c.NoWrap, FitText = c.FitText, HideMark = c.HideMark,
        };
        WriteCellPrBody(x, cell, writeWidth: null, span: 1);
    }

    internal static TableRowFormat ReadStyleRowPr(XmlReader r)
    {
        var tmp = new TableRow(0, false);
        bool sawHeader = false;
        // Reuse the row reader, then copy only what was stated.
        ReadRowPr(r, tmp, () => sawHeader = true);
        return new TableRowFormat
        {
            CantSplit = tmp.CantSplit, IsHeader = sawHeader ? tmp.IsHeader : null, Alignment = tmp.Alignment,
            HeightPt = tmp.HeightPt, HeightRule = tmp.HeightPt is null ? null : tmp.HeightRule, Hidden = tmp.Hidden,
        };
    }

    internal static TableCellFormat ReadStyleCellPr(XmlReader r)
    {
        var tmp = new TableCell();
        ReadCellPr(r, tmp);
        return TableCellFormat.FromCell(tmp);
    }

    internal static TableFormat ReadStyleTablePr(XmlReader r)
    {
        var f = new TableFormat();
        ReadTablePr(r, f);
        return f;
    }

    private static void CopyFormat(TableFormat s, TableFormat d)
    {
        d.StyleId = s.StyleId; d.Look = s.Look; d.Width = s.Width; d.Alignment = s.Alignment; d.IndentPt = s.IndentPt;
        d.Layout = s.Layout; d.Borders = s.Borders; d.CellMargins = s.CellMargins; d.CellSpacingPt = s.CellSpacingPt;
        d.Shading = s.Shading; d.RightToLeft = s.RightToLeft; d.RowBandSize = s.RowBandSize; d.ColumnBandSize = s.ColumnBandSize;
        d.Caption = s.Caption; d.Description = s.Description; d.FloatingPosition = s.FloatingPosition?.Clone(); d.AllowOverlap = s.AllowOverlap;
    }

    // ---- Floating tables ----------------------------------------------------------------------

    /// <summary>CT_TblPPr attribute order as Word writes it.</summary>
    private static void WriteFloating(XmlWriter x, TableFloatingPosition p)
    {
        x.WriteStartElement("tblpPr", W);
        void Tw(string name, double? v) { if (v is double d) x.WriteAttributeString(name, W, RunProperties.Twips(d)); }
        static string Anchor(TableFloatAnchor a) => a switch { TableFloatAnchor.Margin => "margin", TableFloatAnchor.Page => "page", _ => "text" };
        Tw("leftFromText", p.LeftFromTextPt);
        Tw("rightFromText", p.RightFromTextPt);
        Tw("topFromText", p.TopFromTextPt);
        Tw("bottomFromText", p.BottomFromTextPt);
        if (p.VerticalAnchor is { } va) x.WriteAttributeString("vertAnchor", W, Anchor(va));
        if (p.HorizontalAnchor is { } ha) x.WriteAttributeString("horzAnchor", W, Anchor(ha));
        if (p.XAlign is { } xa) x.WriteAttributeString("tblpXSpec", W, xa.ToString().ToLowerInvariant());
        Tw("tblpX", p.XPt);
        if (p.YAlign is { } ya) x.WriteAttributeString("tblpYSpec", W, ya.ToString().ToLowerInvariant());
        Tw("tblpY", p.YPt);
        x.WriteEndElement();
    }

    private static TableFloatingPosition ReadFloating(XmlReader r)
    {
        static TableFloatAnchor? Anchor(string? v) => v switch
        {
            "text" => TableFloatAnchor.Text, "margin" => TableFloatAnchor.Margin, "page" => TableFloatAnchor.Page, _ => null,
        };
        double? Tw(string name) => RunProperties.FromTwips(Attr(r, name)) is double d && double.IsFinite(d) && Math.Abs(d) <= 31680 / 20.0 ? d : null;
        return new TableFloatingPosition
        {
            LeftFromTextPt = Tw("leftFromText") is double l && l >= 0 ? l : null,
            RightFromTextPt = Tw("rightFromText") is double rr && rr >= 0 ? rr : null,
            TopFromTextPt = Tw("topFromText") is double t && t >= 0 ? t : null,
            BottomFromTextPt = Tw("bottomFromText") is double b && b >= 0 ? b : null,
            VerticalAnchor = Anchor(Attr(r, "vertAnchor")),
            HorizontalAnchor = Anchor(Attr(r, "horzAnchor")),
            XAlign = Attr(r, "tblpXSpec") switch
            {
                "left" => TableFloatXAlign.Left, "center" => TableFloatXAlign.Center, "right" => TableFloatXAlign.Right,
                "inside" => TableFloatXAlign.Inside, "outside" => TableFloatXAlign.Outside, _ => null,
            },
            XPt = Tw("tblpX"),
            YAlign = Attr(r, "tblpYSpec") switch
            {
                "inline" => TableFloatYAlign.Inline, "top" => TableFloatYAlign.Top, "center" => TableFloatYAlign.Center,
                "bottom" => TableFloatYAlign.Bottom, "inside" => TableFloatYAlign.Inside, "outside" => TableFloatYAlign.Outside, _ => null,
            },
            YPt = Tw("tblpY"),
        };
    }

    // ---- Tokens ---------------------------------------------------------------------------

    private static string AlignToken(TableAlignment a) => a switch
    {
        TableAlignment.Center => "center",
        TableAlignment.Right => "right",
        _ => "left",
    };

    private static TableAlignment? ParseAlign(string? v) => v switch
    {
        "center" => TableAlignment.Center,
        "right" or "end" => TableAlignment.Right,
        "left" or "start" => TableAlignment.Left,
        _ => null,
    };

    private static readonly (CellTextDirection Dir, string[] Tokens)[] TextDirections =
    {
        (CellTextDirection.LeftToRightTopToBottom, new[] { "lrTb", "tb" }),
        (CellTextDirection.TopToBottomRightToLeft, new[] { "tbRl", "rl" }),
        (CellTextDirection.BottomToTopLeftToRight, new[] { "btLr", "lr" }),
        (CellTextDirection.LeftToRightTopToBottomRotated, new[] { "lrTbV", "tbV" }),
        (CellTextDirection.TopToBottomLeftToRightRotated, new[] { "tbLrV", "lrV" }),
        (CellTextDirection.TopToBottomRightToLeftRotated, new[] { "tbRlV", "rlV" }),
    };

    internal static string TextDirectionToken(CellTextDirection d)
    {
        foreach (var (dir, tokens) in TextDirections) if (dir == d) return tokens[0];
        throw new ArgumentOutOfRangeException(nameof(d), d, "Undefined CellTextDirection value.");
    }

    internal static CellTextDirection? ParseTextDirection(string? v)
    {
        foreach (var (dir, tokens) in TextDirections) if (Array.IndexOf(tokens, v) >= 0) return dir;
        return null;
    }

    private static string Bit(TableLook look, TableLook bit) => (look & bit) != 0 ? "1" : "0";

    private static double Sum(double[] values)
    {
        double s = 0;
        foreach (var v in values) s += v;
        return s;
    }

    private static int? ParseInt(string? v)
        => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? Attr(XmlReader r, string name) => r.GetAttribute(name, W);

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }

    private static void Toggle(XmlWriter x, string name, bool? value)
    {
        if (value is null) return;
        x.WriteStartElement(name, W);
        if (value == false) x.WriteAttributeString("val", W, "0");
        x.WriteEndElement();
    }
}
