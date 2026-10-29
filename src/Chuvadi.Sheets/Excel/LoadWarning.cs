using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>What a <see cref="LoadWarning"/> reports.</summary>
public enum LoadWarningKind
{
    /// <summary>
    /// Rows or cells outside the worksheet grid (row below 1 or above 1,048,576, column beyond
    /// XFD) were left out. Excel cannot hold them; some third-party writers produce them.
    /// </summary>
    CellsOutsideGrid = 0,
}

/// <summary>
/// Something <see cref="Workbook.Load(string)"/> could not carry into the model and left out,
/// so a caller can tell the user instead of the content vanishing silently. Data that is
/// loaded is never altered; a warning only ever reports content that was skipped.
/// </summary>
public sealed class LoadWarning
{
    internal LoadWarning(LoadWarningKind kind, string? sheetName, long count, string? reference, string message)
    {
        Kind = kind;
        SheetName = sheetName;
        Count = count;
        Reference = reference;
        Message = message;
    }

    public LoadWarningKind Kind { get; }

    /// <summary>The sheet concerned, or null for a workbook-level warning.</summary>
    public string? SheetName { get; }

    /// <summary>How many items were left out (for <see cref="LoadWarningKind.CellsOutsideGrid"/>: cells).</summary>
    public long Count { get; }

    /// <summary>The first item concerned, as written in the file (e.g. "A0"), or null.</summary>
    public string? Reference { get; }

    /// <summary>A readable description.</summary>
    public string Message { get; }

    public override string ToString() => Message;
}
