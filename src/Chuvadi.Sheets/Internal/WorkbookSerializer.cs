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
        // Defined names (workbook-level and sheet-scoped; must be set before Save). Print areas
        // and titles are written from each sheet's PageSetup by the writer.
        foreach (var n in wb.DefinedNames.Entries)
            writer.AddDefinedName(n.Name, n.Formula, n.Scope, n.Hidden, n.Comment);

        // Workbook-level structure protection.
        if (wb.IsStructureProtected && wb.StructureProtectionPassword is not null)
            writer.ProtectWorkbook(wb.StructureProtectionPassword, wb.LockStructure, wb.LockWindows);

        foreach (var sheet in wb.Sheets)
            WriteSheet(writer, sheet, wb.UnkeptNames);

        // The active sheet must be visible: a loaded file pointing at a hidden sheet falls back
        // to the first visible one rather than failing the save.
        if (wb.ActiveSheet is { } active)
        {
            int idx = IndexOf(wb.Sheets, active);
            if (wb.Sheets[idx].Visibility != SheetVisibility.Visible)
                for (int i = 0; i < wb.Sheets.Count; i++)
                    if (wb.Sheets[i].Visibility == SheetVisibility.Visible) { idx = i; break; }
            writer.ActiveSheetIndex = idx;
        }
    }

    private static int IndexOf(IReadOnlyList<Sheet> sheets, Sheet s)
    {
        for (int i = 0; i < sheets.Count; i++) if (ReferenceEquals(sheets[i], s)) return i;
        return 0;
    }

    /// <summary>The streaming writer's row settings for a model row, or null when it has none.</summary>
    private static RowFormat? FormatOf(RowData? d, Sheet sheet)
    {
        if (d is null || d.IsEmpty) return null;
        var f = new RowFormat
        {
            Hidden = d.Hidden,
            OutlineLevel = System.Math.Clamp(d.OutlineLevel, 0, 7),
            Collapsed = d.Collapsed,
            ThickTop = d.ThickTop,
            ThickBottom = d.ThickBottom,
        };
        if (d.Height > 0)
        {
            f.Height = System.Math.Min(d.Height, 409);
            f.CustomHeight = d.CustomHeight;
        }
        if (d.StyleSet) f.Style = d.Style;
        else if (d.CustomFormat && d.StyleIndex >= 0) f.Style = d.StyleIndex == 0 ? default(CellStyle) : sheet.StyleTable.ResolveCellFormat(d.StyleIndex) ?? default(CellStyle);
        return f.HasContent ? f : null;
    }

    private static void WriteSheet(XlsxWriter writer, Sheet sheet, ISet<string> unkept)
    {
        using var sw = writer.AddSheet(sheet.Name);

        // Columns: loaded spans are kept as spans; settings made through Columns[n] override
        // them. Sorted and non-overlapping. Width-only spans use SetColumnWidth (as before).
        foreach (var c in sheet.GetColumnsForWrite())
        {
            if (!c.Hidden && c.OutlineLevel == 0 && !c.Collapsed && !c.BestFit && c.Style is null)
                sw.SetColumnWidth(c.Min, c.Max, c.Width);
            else
                sw.SetColumns(c.Min, c.Max, new ColumnFormat
                {
                    Width = c.Width > 0 ? System.Math.Min(c.Width, 255) : null,
                    Hidden = c.Hidden,
                    OutlineLevel = System.Math.Clamp(c.OutlineLevel, 0, 7),
                    Collapsed = c.Collapsed,
                    BestFit = c.BestFit,
                    Style = c.Style,
                });
        }

        // Auto-fit: enabled before rows; merges registered up front so merged cells are skipped
        // while measuring (the writer emits merges at finalization either way, in this order).
        if (sheet.EffectiveAutoFit() is { } autoFit)
        {
            sw.AutoFitColumns(autoFit);
            foreach (var mr in sheet.MergeRanges)
                sw.MergeCells(mr);
        }

        // View (freeze/split panes, zoom, gridlines, selection), sheet format, tab colour, visibility.
        sw.View.CopyFrom(sheet.View);
        sw.Format.CopyFrom(sheet.Format);
        sw.TabColor = sheet.TabColor;
        sw.Visibility = sheet.Visibility;

        // Print settings (header/footer, margins, page setup, breaks, print area and titles) —
        // emitted by the sheet writer at finalization.
        sw.PageSetup.CopyFrom(sheet.PageSetup);

        // Rows with settings but no cells (hidden or tall spacer rows) are written too.
        var rowData = sheet.RowDataMap;
        int lastSettingsRow = 0;
        foreach (var kv in rowData) if (!kv.Value.IsEmpty && kv.Key > lastSettingsRow) lastSettingsRow = kv.Key;

        // If the sheet has no cells, we still need to emit an empty <sheetData>; the streaming
        // SheetWriter handles this automatically when FinalizeSheet runs with no rows.
        int previousRow = 0;
        void Gap(int upTo)
        {
            // Rows are numbered consecutively by the streaming writer, so gaps are filled with
            // empty rows (carrying their settings when they have any).
            while (previousRow + 1 < upTo)
            {
                previousRow++;
                var f = FormatOf(rowData.TryGetValue(previousRow, out var d) ? d : null, sheet);
                if (f is null) sw.WriteRow(); else sw.WriteRow(f);
            }
        }
        if (sheet.PopulatedCells.Any())
        {
            // Group cells by row and emit in row-major order.
            var byRow = sheet.PopulatedCells
                .GroupBy(c => c.Row)
                .OrderBy(g => g.Key);

            foreach (var rowGroup in byRow)
            {
                int rowNumber = rowGroup.Key;
                Gap(rowNumber);

                // Build a cell-per-column dictionary for this row, then emit columns in order.
                var byCol = rowGroup.ToDictionary(c => c.Column, c => c);
                int maxCol = byCol.Keys.Max();
                var rowFormat = FormatOf(rowData.TryGetValue(rowNumber, out var rd) ? rd : null, sheet) ?? new RowFormat();

                sw.WriteRow(rowFormat, rb =>
                {
                    for (int c = 1; c <= maxCol; c++)
                    {
                        if (byCol.TryGetValue(c, out var cell))
                        {
                            if (cell.IsFormula && (FormulaText.HasBracketReference(cell.Formula)
                                                   || FormulaText.UsesAnyName(cell.Formula, unkept)))
                            {
                                // Refers to another workbook, a table, or a loaded name the model
                                // could not keep: keep the result rather than a formula that
                                // would evaluate differently.
                                if (cell.Style.HasValue) rb.Cell(cell.CachedValue, cell.Style.Value);
                                else rb.Cell(cell.CachedValue);
                            }
                            else if (cell.IsFormula && cell.ArrayRangeInternal is string arr)
                            {
                                if (cell.Style.HasValue) rb.ArrayFormula(arr, cell.Formula!, cell.CachedValue, cell.Style.Value);
                                else rb.ArrayFormula(arr, cell.Formula!, cell.CachedValue);
                            }
                            else if (cell.IsFormula)
                            {
                                if (cell.Style.HasValue) rb.Formula(cell.Formula!, cell.CachedValue, cell.Style.Value);
                                else rb.Formula(cell.Formula!, cell.CachedValue);
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
        if (lastSettingsRow > previousRow)
        {
            Gap(lastSettingsRow);
            sw.WriteRow(FormatOf(rowData[lastSettingsRow], sheet)!);
        }

        // Now the "after-sheetData" elements.
        if (sheet.EffectiveAutoFit() is null)
            foreach (var mr in sheet.MergeRanges)
                sw.MergeCells(mr);
        if (sheet.AutoFilterRange is not null)
            sw.AutoFilter(sheet.AutoFilterRange);
        foreach (var (range, v) in sheet.DataValidations)
            sw.AddDataValidation(range, v);
        foreach (var cf in sheet.ConditionalFormats)
            sw.AddConditionalFormatting(cf);
        if (sheet.IsProtected && sheet.ProtectionPassword is not null)
            sw.Protect(sheet.ProtectionPassword, sheet.ProtectionOptions);
    }
}
