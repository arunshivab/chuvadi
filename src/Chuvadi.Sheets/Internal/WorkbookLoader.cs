using System.Collections.Generic;
using Chuvadi.Sheets.Model;

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
        // 2.2.1 (P5, SEC-01): the whole workbook's cells, so a small file that would need gigabytes stops early.
        long maxCells = reader.Options.MaxCells ?? long.MaxValue;
        long cellsBefore = 0;   // cells in the sheets already loaded
        var wb = new Workbook
        {
            StyleTable = styles,
            Date1904 = reader.Date1904,
            HasMacros = reader.HasMacros,
            IsStrict = reader.IsStrict,
            LoadedDefinedNames = reader.DefinedNames,
            DisplayCulture = reader.Options.DisplayCulture ?? System.Globalization.CultureInfo.InvariantCulture,
        };

        // One shared, immutable holder per cell format (see Cell.SetLoadedStyle).
        var boxes = new CellStyleBox?[styles.CellFormats.Count];
        var boxed = new bool[boxes.Length];
        CellStyleBox? StyleBoxFor(int index)
        {
            if (index < 0 || index >= boxes.Length) return reader.ResolveStyle(index) is CellStyle st ? new CellStyleBox(st) : null;
            if (!boxed[index])
            {
                boxes[index] = reader.ResolveStyle(index) is CellStyle s ? new CellStyleBox(s) : null;
                boxed[index] = true;
            }
            return boxes[index];
        }

        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var sheetInfo in reader.Sheets)
        {
            // 2.2.1 (P9, SEC-05): a damaged sheet list is a malformed file, not the caller's InvalidOperationException.
            if (sheetInfo.Name.Length == 0)
                throw new XlsxFormatException("The file is not a valid xlsx workbook: a sheet in workbook.xml has an empty name.");
            if (!names.Add(sheetInfo.Name))
                throw new XlsxFormatException($"The file is not a valid xlsx workbook: workbook.xml lists two sheets named '{sheetInfo.Name}' (names are case-insensitive).");
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
                    if (cellsBefore + sheet.CellCount > maxCells)
                        throw new XlsxLimitExceededException("MaxCells", maxCells, cellsBefore + sheet.CellCount,
                            $"Sheet '{sheetInfo.Name}', cell {CellAddress.ToA1(row.RowNumber, colIdx + 1)}: the workbook has more cells than "
                            + "XlsxReaderOptions.MaxCells allows in memory. Read it with the streaming XlsxReader, or raise MaxCells "
                            + "(or use XlsxReaderOptions.ForTrustedInput()) for a file you trust.");
                    if (formula is not null)
                        cell.SetLoadedFormula(formula.Text, v, formula.Kind == FormulaKind.Array ? formula.Range : null);
                    else if (row.GetRichText(colIdx) is { } rich && v is string)
                        cell.RichText = rich;
                    else if (v is not null)
                        cell.Value = v;
                    if (styleIndex >= 0)
                    {
                        // xf 0 is the workbook default: Style stays null (see DefaultCellStyle).
                        cell.SetLoadedStyle(styleIndex, StyleBoxFor(styleIndex));
                    }
                }
            }

            if (sr.OutOfGridCells > 0 || sr.OutOfGridRows > 0)
                wb.AddLoadWarning(new LoadWarning(LoadWarningKind.CellsOutsideGrid, sheetInfo.Name, sr.OutOfGridCells, sr.FirstOutOfGridReference,
                    $"Sheet '{sheetInfo.Name}': {sr.OutOfGridCells:N0} cell(s) in {sr.OutOfGridRows:N0} row(s) outside the grid were left out (first: {sr.FirstOutOfGridReference})."));

            // Enumeration has run to the end of the part, so the collector is complete.
            layout.ApplyTo(sheet);
            foreach (var hl in layout.ResolveHyperlinks(reader, sheetInfo.PartUri)) sheet.Hyperlinks.Add(hl);
            foreach (var cm in LegacyDrawingReader.ReadComments(reader, sheetInfo.PartUri, layout.LegacyDrawingRelId)) sheet.Comments.Add(cm);
            if (layout.LegacyDrawingHFRelId is not null)
                sheet.SetLoadedHeaderFooterPictures(LegacyDrawingReader.ReadHeaderFooterPictures(reader, sheetInfo.PartUri, layout.LegacyDrawingHFRelId, eager: true, reader.Options.LoadImageData));
            if (layout.DrawingRelId is not null)
                sheet.SetLoadedDrawings(DrawingReader.Read(reader, sheetInfo.PartUri, layout.DrawingRelId, eager: true, reader.Options.LoadImageData));
            PrintNames.Apply(sheet.PageSetup, reader.DefinedNames, sheetInfo.WorkbookIndex);
            cellsBefore += sheet.CellCount;
        }

        foreach (var si in reader.Sheets)
            if (si.WorkbookIndex == reader.ActiveTab) { wb.ActiveSheet = wb[si.Name]; break; }

        if (reader.ChartSheets.Count > 0)
        {
            var charts = new List<ChartSheet>(reader.ChartSheets.Count);
            foreach (var cs in reader.ChartSheets) charts.Add(reader.ReadChartSheetCore(cs, eager: true, reader.Options.LoadImageData));
            wb.ChartSheets = charts;
        }

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
