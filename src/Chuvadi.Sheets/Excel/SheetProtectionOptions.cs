namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Options for sheet protection. Pass to <c>SheetWriter.Protect(...)</c> or
/// <c>Sheet.Protect(...)</c>. All defaults match Excel's "Protect Sheet" dialog defaults.
///
/// When a sheet is protected:
/// - Cells marked Locked (the default for all cells) cannot be edited.
/// - Sheet structure (rows/columns insert/delete) is restricted.
/// - Specific actions can be allowed via the flags below.
///
/// Note: this is "polite" protection — file content is still readable by anyone, and a
/// determined user can strip protection. For confidential content, use workbook encryption
/// (open-password) instead.
/// </summary>
public sealed class SheetProtectionOptions
{
    public bool AllowSelectLockedCells   { get; set; } = true;
    public bool AllowSelectUnlockedCells { get; set; } = true;
    public bool AllowFormatCells         { get; set; } = false;
    public bool AllowFormatColumns       { get; set; } = false;
    public bool AllowFormatRows          { get; set; } = false;
    public bool AllowInsertColumns       { get; set; } = false;
    public bool AllowInsertRows          { get; set; } = false;
    public bool AllowInsertHyperlinks    { get; set; } = false;
    public bool AllowDeleteColumns       { get; set; } = false;
    public bool AllowDeleteRows          { get; set; } = false;
    public bool AllowSort                { get; set; } = false;
    public bool AllowAutoFilter          { get; set; } = false;
    public bool AllowPivotTables         { get; set; } = false;
    public bool AllowEditObjects         { get; set; } = false;
    public bool AllowEditScenarios       { get; set; } = false;
}
