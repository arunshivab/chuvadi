using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>One hyperlink in a worksheet. We track the cell address, the target URL, and an
/// optional tooltip. External (http/https/mailto) links require a sheet-level relationship;
/// the writer generates these at sheet finalization.</summary>
internal sealed record SheetHyperlink(string CellAddress, string Url, string? Tooltip);

/// <summary>One comment (note) in a worksheet. Author + text. Position is implicit — Excel
/// auto-positions to the right of the cell using the VML template's default geometry.</summary>
internal sealed record SheetComment(string CellAddress, string Author, string Text);

/// <summary>A table definition attached to a worksheet. Becomes a separate part
/// (xl/tables/tableN.xml) plus a relationship from the sheet.</summary>
internal sealed record SheetTable(
    string Range,
    string Name,
    string DisplayName,
    TableStyle Style,
    string[] ColumnHeaders);
