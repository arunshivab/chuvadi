using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Converts an opened <see cref="XlsxReader"/> into a populated <see cref="Workbook"/> model.
/// Each sheet part is parsed once: cell values and style indexes stream through the normal row
/// enumerator while layout metadata (sheet format defaults, column spans, row attributes,
/// merged ranges, page setup) is collected in the same pass. Formulas load with their cached
/// results; defined names load into <see cref="Workbook.DefinedNames"/>.
/// Loses any features the Workbook model can't represent (charts, pivots, drawings, etc.).
/// </summary>
internal static class WorkbookLoader
{
    public static Workbook Load(XlsxReader reader)
    {
        var styles = reader.StyleTable;
        var includeStyledEmpty = reader.Options.IncludeStyledEmptyCells;
        var ct = reader.Options.CancellationToken;
        var wb = new Workbook
        {
            StyleTable = styles,
            Date1904 = reader.Date1904,
            HasMacros = reader.HasMacros,
            LoadedDefinedNames = reader.DefinedNames,
            DisplayCulture = reader.Options.DisplayCulture ?? System.Globalization.CultureInfo.InvariantCulture,
        };

        foreach (var sheetInfo in reader.Sheets)
        {
            var sheet = wb.AddSheet(sheetInfo.Name);
            sheet.Visibility = sheetInfo.Visibility;
            var sr = reader.Sheet(sheetInfo.Name);
            var layout = new SheetLayoutCollector(styles);

            foreach (var row in sr.RowsWithLayout(layout))
            {
                ct.ThrowIfCancellationRequested();
                for (int colIdx = 0; colIdx < row.Count; colIdx++)
                {
                    var v = row[colIdx];
                    var styleIndex = row.GetStyleIndex(colIdx);
                    var formula = row.GetFormulaInfo(colIdx);
                    // Data tables (TABLE()) are computed by Excel and cannot be written: values only.
                    if (formula is { Kind: FormulaKind.DataTable } || formula is { Text.Length: 0 }) formula = null;

                    // Value and formula cells always load; empty cells only when styled and opted in.
                    if (v is null && formula is null && !(includeStyledEmpty && styleIndex > 0)) continue;

                    var cell = sheet.Cell(row.RowNumber, colIdx + 1);
                    if (formula is not null)
                        cell.SetLoadedFormula(formula.Text, v, formula.Kind == FormulaKind.Array ? formula.Range : null);
                    else if (v is not null)
                        cell.Value = v;
                    if (styleIndex >= 0)
                    {
                        // xf 0 is the workbook default: Style stays null (see DefaultCellStyle).
                        cell.SetLoadedStyle(styleIndex, reader.ResolveStyle(styleIndex));
                    }
                }
            }

            // Enumeration has run to the end of the part, so the collector is complete.
            layout.ApplyTo(sheet);
            if (layout.DrawingRelId is not null)
                sheet.SetLoadedDrawings(DrawingReader.Read(reader, sheetInfo.PartUri, layout.DrawingRelId, eager: true, reader.Options.LoadImageData));
            PrintNames.Apply(sheet.PageSetup, reader.DefinedNames, sheetInfo.WorkbookIndex);
        }

        foreach (var si in reader.Sheets)
            if (si.WorkbookIndex == reader.ActiveTab) { wb.ActiveSheet = wb[si.Name]; break; }

        LoadDefinedNames(wb, reader);
        return wb;
    }

    /// <summary>
    /// Fills <see cref="Workbook.DefinedNames"/> from the file. Skipped (still listed in
    /// LoadedDefinedNames): the print area/titles (owned by PageSetup), names referring to other
    /// workbooks or to tables (their targets are not kept by the model, so saving them would
    /// change what they evaluate to), and names scoped to sheets the model does not load
    /// (chart sheets, macro sheets).
    /// </summary>
    private static void LoadDefinedNames(Workbook wb, XlsxReader reader)
    {
        foreach (var n in reader.DefinedNames)
        {
            if (string.IsNullOrEmpty(n.Name) || string.IsNullOrEmpty(n.Formula)) continue;
            if (PrintNames.IsPrintName(n.Name)) continue;
            if (FormulaText.HasBracketReference(n.Formula)) { wb.UnkeptNames.Add(n.Name); continue; }
            string? scope = null;
            if (n.LocalSheetIndex is int local)
            {
                foreach (var si in reader.Sheets)
                    if (si.WorkbookIndex == local) { scope = si.Name; break; }
                if (scope is null) { wb.UnkeptNames.Add(n.Name); continue; }
            }
            wb.DefinedNames.AddLoaded(n.Name, n.Formula, scope, n.Hidden, n.Comment);
        }
    }
}
