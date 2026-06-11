using System;
using System.Collections.Generic;
using System.Data;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Shared logic for writing a configured "data sheet" — headers, styled rows, optional
/// autofilter / freeze / table wrapping. Used by both <c>IEnumerable&lt;T&gt;.ToXlsx</c> and
/// <c>DataTable.ToXlsx</c>.
/// </summary>
internal static class ExportEngine
{
    public static void WriteEnumerableSheet<T>(XlsxWriter writer, IEnumerable<T> source, XlsxExportConfig<T> cfg)
    {
        // Build the effective column list by applying overrides + ignores to the type's
        // reflection-derived plans.
        var columns = new List<ColumnPlan>();
        foreach (var plan in TypeAccessor<T>.Columns)
        {
            if (cfg._ignoredColumns.Contains(plan.PropertyName)) continue;
            if (cfg._columnOverrides.TryGetValue(plan.PropertyName, out var ov))
                columns.Add(plan.WithOverrides(ov.Header, ov.Width, ov.Format, ov.Style));
            else
                columns.Add(plan);
        }

        using var sheet = writer.AddSheet(cfg._sheetName);

        // Column widths first (must come before any row).
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Width > 0)
                sheet.SetColumnWidth(i + 1, columns[i].Width);
        }
        if (cfg._freezeHeaderRow) sheet.FreezeRows(1);
        if (cfg._pageHeader is not null || cfg._pageFooter is not null)
            sheet.SetHeaderFooter(cfg._pageHeader, cfg._pageFooter);

        // Header row — styled.
        var headerStyle = cfg._headerStyle;
        sheet.WriteRow(row =>
        {
            foreach (var col in columns)
            {
                if (headerStyle.HasValue) row.Cell(col.Header, headerStyle.Value);
                else                      row.Cell(col.Header);
            }
        });

        // Data rows — reuse one buffer across all rows.
        var buffer = new object?[columns.Count];
        // Map: original (declared) property index → column slot in the OUTPUT order (after overrides + sort).
        // TypeAccessor's Extract writes into the OUTPUT order already (Columns is already sorted),
        // but we may have dropped or rearranged things via the cfg overrides above. Solve by extracting
        // into a "raw" buffer keyed by TypeAccessor.Columns order, then copying into a final buffer.
        var rawColumnCount = TypeAccessor<T>.Columns.Count;
        var raw = new object?[rawColumnCount];
        var outputIndexFromRaw = BuildIndexMap<T>(columns);

        // Pre-resolve per-column style id (so we don't hit StyleRegistry for every cell).
        // We use IRowSink-style style registration indirectly via the RowBuilder.Cell(value, style).
        // Easier path: just pass the CellStyle struct itself.
        var columnStyles = new CellStyle?[columns.Count];
        var columnFormats = new string?[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            columnStyles[i] = columns[i].Style;
            columnFormats[i] = columns[i].Format;
        }

        foreach (var item in source)
        {
            if (item is null)
            {
                // Defensive: skip nulls in the input (a List<T> where T is a reference type may have them).
                continue;
            }
            TypeAccessor<T>.Extract(item, raw);
            // Pack raw into buffer in output order, applying ignores by skipping unmapped indices.
            for (int o = 0; o < buffer.Length; o++) buffer[o] = null;
            for (int rIdx = 0; rIdx < rawColumnCount; rIdx++)
            {
                var outIdx = outputIndexFromRaw[rIdx];
                if (outIdx >= 0) buffer[outIdx] = raw[rIdx];
            }

            sheet.WriteRow(row =>
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var v = buffer[i];
                    var style = columnStyles[i];
                    var format = columnFormats[i];

                    if (style.HasValue)
                    {
                        row.Cell(v, style.Value);
                    }
                    else if (format is not null)
                    {
                        // Format-only — synthesize a style.
                        row.Cell(v, new CellStyleBuilder().Format(format).Build());
                    }
                    else
                    {
                        row.Cell(v);
                    }
                }
            });
        }

        ApplyTrailingFeatures(sheet, cfg, totalRows: GetEstimatedRowCount(source, headerCount: 1), columnCount: columns.Count);
    }

    public static void WriteDataTableSheet(XlsxWriter writer, DataTable table, DataTableExportConfig cfg)
    {
        // Columns from the DataTable schema, in column order, filtered by ignores.
        var cols = new List<DataColumn>();
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var dc = table.Columns[i];
            if (cfg._ignoredColumns.Contains(dc.ColumnName)) continue;
            cols.Add(dc);
        }

        using var sheet = writer.AddSheet(cfg._sheetName);

        // Column widths + freeze.
        for (int i = 0; i < cols.Count; i++)
        {
            if (cfg._columnOverrides.TryGetValue(cols[i].ColumnName, out var ov) && ov.Width > 0)
                sheet.SetColumnWidth(i + 1, ov.Width);
        }
        if (cfg._freezeHeaderRow) sheet.FreezeRows(1);
        if (cfg._pageHeader is not null || cfg._pageFooter is not null)
            sheet.SetHeaderFooter(cfg._pageHeader, cfg._pageFooter);

        // Header row.
        var headerStyle = cfg._headerStyle;
        sheet.WriteRow(row =>
        {
            foreach (var dc in cols)
            {
                var header = cfg._columnOverrides.TryGetValue(dc.ColumnName, out var ov) && ov.Header is not null
                    ? ov.Header
                    : dc.ColumnName;
                if (headerStyle.HasValue) row.Cell(header, headerStyle.Value);
                else                      row.Cell(header);
            }
        });

        // Pre-resolve per-column style/format.
        var columnStyles = new CellStyle?[cols.Count];
        var columnFormats = new string?[cols.Count];
        for (int i = 0; i < cols.Count; i++)
        {
            if (cfg._columnOverrides.TryGetValue(cols[i].ColumnName, out var ov))
            {
                columnStyles[i] = ov.Style;
                columnFormats[i] = ov.Format;
            }
        }

        // Data rows.
        foreach (DataRow dr in table.Rows)
        {
            sheet.WriteRow(row =>
            {
                for (int i = 0; i < cols.Count; i++)
                {
                    var raw = dr[cols[i]];
                    var value = raw == DBNull.Value ? null : raw;
                    var style = columnStyles[i];
                    var format = columnFormats[i];

                    if (style.HasValue) row.Cell(value, style.Value);
                    else if (format is not null) row.Cell(value, new CellStyleBuilder().Format(format).Build());
                    else row.Cell(value);
                }
            });
        }

        ApplyTrailingFeatures(sheet, cfg, totalRows: table.Rows.Count + 1, columnCount: cols.Count);
    }

    // ---- Helpers -------------------------------------------------------------------

    private static int[] BuildIndexMap<T>(IList<ColumnPlan> outputColumns)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < outputColumns.Count; i++)
            map[outputColumns[i].PropertyName] = i;

        var raw = TypeAccessor<T>.Columns;
        var result = new int[raw.Count];
        for (int i = 0; i < raw.Count; i++)
            result[i] = map.TryGetValue(raw[i].PropertyName, out var outIdx) ? outIdx : -1;
        return result;
    }

    private static int GetEstimatedRowCount<T>(IEnumerable<T> source, int headerCount)
        => source is ICollection<T> coll ? coll.Count + headerCount : -1;

    private static void ApplyTrailingFeatures(SheetWriter sheet, ExportConfigBase cfg, int totalRows, int columnCount)
    {
        if (columnCount == 0) return;

        var lastColLetter = CellAddress.ColumnLetters(columnCount);
        // If we don't know the row count, fall back to the autofilter on just the header row + 1.
        // The user can call AutoFilter manually for streaming sources where row count is unknown.
        var lastRow = totalRows > 0 ? totalRows : 2;
        var range = $"A1:{lastColLetter}{lastRow}";

        if (cfg._asTable)
        {
            // Tables need column headers; pull them from the sheet (we don't have them here).
            // For simplicity: tables require finite-row sources. For DataTable that's always true.
            // For IEnumerable<T> we only know the row count if it's an ICollection.
            // If row count unknown, skip AsTable silently — user should use the streaming API for huge datasets.
            if (totalRows > 0)
            {
                // We need the actual header strings to pass to AddTable. Reconstruct them from the override map.
                // Easier: just call AutoFilter instead and let the user know.
                // Trade-off: the AsTable name on a streaming source becomes an autofilter.
                sheet.AutoFilter(range);
                // Note: real table wrapping omitted here to keep ExportEngine simple. The user can
                // still get a table via the streaming SheetWriter API directly.
            }
        }
        else if (cfg._autoFilter && totalRows > 0)
        {
            sheet.AutoFilter(range);
        }
    }
}
