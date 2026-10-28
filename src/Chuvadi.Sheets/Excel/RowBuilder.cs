using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Fluent builder used inside <c>SheetWriter.WriteRow(Action&lt;RowBuilder&gt;)</c> to write
/// cells with full per-cell control (styles, formulas, type hints).
///
/// Each <c>Cell(...)</c> call appends a cell to the current row. Cells appear in calling
/// order, starting at column A.
///
/// Example:
/// <code>
/// sheet.WriteRow(row => row
///     .Cell("Patient 1")
///     .Cell(42, headerStyle)
///     .Formula("SUM(A2:B2)")
///     .Cell(DateTime.Now));
/// </code>
///
/// This is a reference type so it can be passed through the lambda. It is NOT thread-safe
/// and must not be retained beyond the lifetime of the <c>WriteRow</c> call that produced it.
/// </summary>
public sealed class RowBuilder
{
    // The actual cell-writing work is delegated back to SheetWriter through an internal
    // sink interface, so RowBuilder doesn't have to know about XML or shared strings.
    // The sink is set up by SheetWriter before each call to the user's lambda.
    internal IRowSink Sink { get; }

    internal RowBuilder(IRowSink sink) { Sink = sink; }

    /// <summary>Appends an automatically-typed cell with no explicit style.</summary>
    public RowBuilder Cell(object? value)
    {
        Sink.AppendCell(value, styleId: null);
        return this;
    }

    /// <summary>Appends an automatically-typed cell with the given style.</summary>
    public RowBuilder Cell(object? value, CellStyle style)
    {
        Sink.AppendCell(value, styleId: Sink.RegisterStyle(style));
        return this;
    }

    /// <summary>
    /// Appends a formula cell. The formula is the Excel expression WITHOUT a leading '=' —
    /// e.g. <c>"SUM(A2:B2)"</c>, not <c>"=SUM(A2:B2)"</c>. Excel will compute the value when
    /// the file is opened; the file itself stores no cached result. To store one for readers
    /// that do not calculate, use <see cref="Formula(string, object?)"/>.
    /// </summary>
    public RowBuilder Formula(string formula)
    {
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: null, cachedValue: null, arrayRange: null);
        return this;
    }

    /// <summary>Appends a formula cell with the given style.</summary>
    public RowBuilder Formula(string formula, CellStyle style)
    {
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: Sink.RegisterStyle(style), cachedValue: null, arrayRange: null);
        return this;
    }

    /// <summary>
    /// Appends a formula cell together with its last computed result, so readers that do not
    /// calculate (viewers, converters, this library's reader) show a value. Excel recalculates
    /// on open; see <see cref="XlsxWriterOptions.FullCalculationOnLoad"/>. The cached value may be
    /// a number, string, bool, DateTime/DateOnly/DateTimeOffset (written as a date serial with the
    /// default date style unless a style is given), TimeSpan, or null for none. A string equal
    /// to an Excel error literal (#N/A, #DIV/0!, #VALUE!, #REF!, #NAME?, #NUM!, #NULL!, #SPILL!,
    /// #CALC!, ...) is written as that error.
    /// </summary>
    public RowBuilder Formula(string formula, object? cachedValue)
    {
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: null, cachedValue, arrayRange: null);
        return this;
    }

    /// <summary>Appends a styled formula cell with its cached result. See <see cref="Formula(string, object?)"/>.</summary>
    public RowBuilder Formula(string formula, object? cachedValue, CellStyle style)
    {
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: Sink.RegisterStyle(style), cachedValue, arrayRange: null);
        return this;
    }

    /// <summary>
    /// Appends the top-left cell of an array (Ctrl+Shift+Enter) formula that fills
    /// <paramref name="range"/> (e.g. "C2:C10", which must start at this cell). Write the other
    /// cells of the range as plain values (their cached results) or leave them empty.
    /// <paramref name="cachedValue"/> is this cell's result, as for <see cref="Formula(string, object?)"/>.
    /// </summary>
    public RowBuilder ArrayFormula(string range, string formula, object? cachedValue = null)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: null, cachedValue, arrayRange: range);
        return this;
    }

    /// <summary>Appends a styled array formula. See <see cref="ArrayFormula(string, string, object?)"/>.</summary>
    public RowBuilder ArrayFormula(string range, string formula, object? cachedValue, CellStyle style)
    {
        if (string.IsNullOrEmpty(range)) throw new ArgumentException("Range required.", nameof(range));
        if (formula is null) throw new ArgumentNullException(nameof(formula));
        Sink.AppendFormula(formula, styleId: Sink.RegisterStyle(style), cachedValue, arrayRange: range);
        return this;
    }

    /// <summary>Appends an empty cell (advances the column index but writes nothing).</summary>
    public RowBuilder Empty()
    {
        Sink.AppendCell(value: null, styleId: null);
        return this;
    }

    /// <summary>
    /// Appends a cell containing a clickable hyperlink to <paramref name="url"/>, displaying
    /// <paramref name="display"/> as the cell text. The cell renders in the default hyperlink
    /// style (blue, underlined) unless overridden with <see cref="Hyperlink(string, string, CellStyle, string?)"/>.
    /// </summary>
    /// <param name="url">Target URL (http, https, mailto, etc.). External relationships are added at sheet finalize.</param>
    /// <param name="display">The text shown in the cell.</param>
    /// <param name="tooltip">Optional tooltip shown on hover in Excel.</param>
    public RowBuilder Hyperlink(string url, string display, string? tooltip = null)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        if (display is null) throw new ArgumentNullException(nameof(display));
        Sink.AppendHyperlink(url, display, styleId: null, tooltip);
        return this;
    }

    /// <summary>Appends a hyperlink cell with a custom style overriding the default blue+underline.</summary>
    public RowBuilder Hyperlink(string url, string display, CellStyle style, string? tooltip = null)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        if (display is null) throw new ArgumentNullException(nameof(display));
        Sink.AppendHyperlink(url, display, styleId: Sink.RegisterStyle(style), tooltip);
        return this;
    }

    /// <summary>
    /// Appends a cell with a comment (note) attached. The cell's value is written normally;
    /// Excel will display a small red triangle in the corner indicating a note is present.
    /// </summary>
    /// <param name="cellValue">The value displayed in the cell itself.</param>
    /// <param name="author">The comment's author name (shown in the note's header).</param>
    /// <param name="text">The comment body text.</param>
    public RowBuilder Comment(object? cellValue, string author, string text)
    {
        if (author is null) throw new ArgumentNullException(nameof(author));
        if (text is null) throw new ArgumentNullException(nameof(text));
        Sink.AppendComment(cellValue, styleId: null, author, text);
        return this;
    }

    /// <summary>Appends a styled cell with a comment attached.</summary>
    public RowBuilder Comment(object? cellValue, CellStyle style, string author, string text)
    {
        if (author is null) throw new ArgumentNullException(nameof(author));
        if (text is null) throw new ArgumentNullException(nameof(text));
        Sink.AppendComment(cellValue, styleId: Sink.RegisterStyle(style), author, text);
        return this;
    }
}

/// <summary>
/// Internal sink interface implemented by <c>SheetWriter</c>. RowBuilder writes through this
/// rather than directly into SheetWriter, keeping the two classes loosely coupled.
/// </summary>
internal interface IRowSink
{
    void AppendCell(object? value, int? styleId);
    void AppendFormula(string formula, int? styleId, object? cachedValue, string? arrayRange);
    void AppendHyperlink(string url, string display, int? styleId, string? tooltip);
    void AppendComment(object? cellValue, int? styleId, string author, string text);

    /// <summary>Registers a style with the workbook's style registry and returns the cellXf id.</summary>
    int RegisterStyle(CellStyle style);
}
