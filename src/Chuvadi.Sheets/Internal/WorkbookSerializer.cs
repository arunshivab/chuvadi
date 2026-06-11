using System.Collections.Generic;
using System.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Converts a <see cref="Workbook"/> model into a sequence of <see cref="XlsxWriter"/> calls.
/// Iterates each sheet's populated cells in row-major order, pushing them through the
/// streaming writer.
/// </summary>
internal static class WorkbookSerializer
{
    public static void WriteWorkbook(XlsxWriter writer, Workbook wb)
    {
        // Register workbook-level defined names first (must be set before Save).
        if (wb.DefinedNames.Count > 0)
            writer.SetDefinedNames(wb.DefinedNames.All);

        // Workbook-level structure protection.
        if (wb.IsStructureProtected && wb.StructureProtectionPassword is not null)
            writer.ProtectWorkbook(wb.StructureProtectionPassword, wb.LockStructure, wb.LockWindows);

        foreach (var sheet in wb.Sheets)
            WriteSheet(writer, sheet);
    }

    private static void WriteSheet(XlsxWriter writer, Sheet sheet)
    {
        using var sw = writer.AddSheet(sheet.Name);

        // Column widths — must come before any row.
        foreach (var kv in sheet.ColumnWidths)
            sw.SetColumnWidth(kv.Key, kv.Value);

        // Freeze panes — must also come before any row.
        if (sheet.FreezeRowCount > 0) sw.FreezeRows(sheet.FreezeRowCount);
        if (sheet.FreezeColumnCount > 0) sw.FreezeColumns(sheet.FreezeColumnCount);

        // Page header/footer — emitted by the sheet writer at finalization.
        if (sheet.PageHeader is not null || sheet.PageFooter is not null)
            sw.SetHeaderFooter(sheet.PageHeader, sheet.PageFooter);

        // If the sheet has no cells, we still need to emit an empty <sheetData>; the streaming
        // SheetWriter handles this automatically when FinalizeSheet runs with no rows.
        if (sheet.PopulatedCells.Any())
        {
            // Group cells by row and emit in row-major order.
            var byRow = sheet.PopulatedCells
                .GroupBy(c => c.Row)
                .OrderBy(g => g.Key);

            int previousRow = 0;
            foreach (var rowGroup in byRow)
            {
                int rowNumber = rowGroup.Key;

                // Skip-row support: streaming writer doesn't yet support row numbering with
                // gaps, so for now we emit blank rows to fill the gap. This means a sheet with
                // a cell at A1 and a cell at A1000 will emit 1000 row elements. Acceptable for
                // the model API which isn't intended for huge sparse sheets.
                while (previousRow + 1 < rowNumber)
                {
                    sw.WriteRow();
                    previousRow++;
                }

                // Build a cell-per-column dictionary for this row, then emit columns in order.
                var byCol = rowGroup.ToDictionary(c => c.Column, c => c);
                int maxCol = byCol.Keys.Max();

                sw.WriteRow(rb =>
                {
                    for (int c = 1; c <= maxCol; c++)
                    {
                        if (byCol.TryGetValue(c, out var cell))
                        {
                            if (cell.IsFormula)
                            {
                                if (cell.Style.HasValue) rb.Formula(cell.Formula!, cell.Style.Value);
                                else rb.Formula(cell.Formula!);
                            }
                            else
                            {
                                if (cell.Style.HasValue) rb.Cell(cell.Value, cell.Style.Value);
                                else rb.Cell(cell.Value);
                            }
                        }
                        else
                        {
                            rb.Empty();
                        }
                    }
                });
                previousRow = rowNumber;
            }
        }

        // Now the "after-sheetData" elements.
        foreach (var mr in sheet.MergeRanges)
            sw.MergeCells(mr);
        if (sheet.AutoFilterRange is not null)
            sw.AutoFilter(sheet.AutoFilterRange);
        foreach (var (range, v) in sheet.Validations)
            sw.AddDataValidation(range, v);
        foreach (var (range, rule) in sheet.ConditionalFormats)
            sw.AddConditionalFormat(range, rule);
        if (sheet.IsProtected && sheet.ProtectionPassword is not null)
            sw.Protect(sheet.ProtectionPassword, sheet.ProtectionOptions);
    }
}
