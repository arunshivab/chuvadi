using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Converts an opened <see cref="XlsxReader"/> into a populated <see cref="Workbook"/> model.
/// Each sheet part is parsed once: cell values and style indexes stream through the normal row
/// enumerator while layout metadata (sheet format defaults, column spans, row attributes,
/// merged ranges) is collected in the same pass.
/// Loses any features the Workbook model can't represent (charts, pivots, drawings, etc.).
/// </summary>
internal static class WorkbookLoader
{
    public static Workbook Load(XlsxReader reader)
    {
        var styles = reader.StyleTable;
        var includeStyledEmpty = reader.Options.IncludeStyledEmptyCells;
        var wb = new Workbook { StyleTable = styles };

        foreach (var sheetInfo in reader.Sheets)
        {
            var sheet = wb.AddSheet(sheetInfo.Name);
            var sr = reader.Sheet(sheetInfo.Name);
            var layout = new SheetLayoutCollector();

            foreach (var row in sr.RowsWithLayout(layout))
            {
                for (int colIdx = 0; colIdx < row.Count; colIdx++)
                {
                    var v = row[colIdx];
                    var styleIndex = row.GetStyleIndex(colIdx);

                    // Value cells always load; value-less cells only when styled and opted in.
                    if (v is null && !(includeStyledEmpty && styleIndex > 0)) continue;

                    var cell = sheet.Cell(row.RowNumber, colIdx + 1);
                    if (v is not null) cell.Value = v;
                    if (styleIndex >= 0)
                    {
                        // xf 0 is the workbook default: Style stays null (see DefaultCellStyle).
                        cell.SetLoadedStyle(styleIndex, styleIndex == 0 ? null : styles.ResolveCellFormat(styleIndex));
                    }
                }
            }

            // Enumeration has run to the end of the part, so the collector is complete.
            layout.ApplyTo(sheet);
        }
        return wb;
    }
}
