using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Converts an opened <see cref="XlsxReader"/> into a populated <see cref="Workbook"/> model.
/// Loses any features the Workbook model can't represent (charts, pivots, drawings, etc.).
/// </summary>
internal static class WorkbookLoader
{
    public static Workbook Load(XlsxReader reader)
    {
        var wb = new Workbook();
        foreach (var sheetInfo in reader.Sheets)
        {
            var sheet = wb.AddSheet(sheetInfo.Name);
            var sr = reader.Sheet(sheetInfo.Name);

            foreach (var row in sr.Rows)
            {
                for (int colIdx = 0; colIdx < row.Count; colIdx++)
                {
                    var v = row[colIdx];
                    if (v is null) continue;

                    var cell = sheet.Cell(row.RowNumber, colIdx + 1);
                    cell.Value = v;
                }
            }
        }
        return wb;
    }
}
