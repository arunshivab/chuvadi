using System;
using System.Threading;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

using Chuvadi.Internal;

/// <summary>
/// State shared between an <see cref="XlsxWriter"/> and its child <c>SheetWriter</c> instances.
/// SheetWriters use this to register strings, styles, and to access writer options without
/// holding a reference to the parent writer (which keeps the public surface clean).
///
/// Lifetime: created by XlsxWriter, lives as long as the writer does, disposed indirectly when
/// the writer is disposed.
/// </summary>
internal sealed class WorkbookWriteState
{
    private int _nextTableId; // Atomic counter for workbook-unique table ids.

    public WorkbookWriteState(SharedStringTable sst, StyleRegistry styles, XlsxWriterOptions options)
    {
        SharedStrings = sst ?? throw new ArgumentNullException(nameof(sst));
        Styles        = styles ?? throw new ArgumentNullException(nameof(styles));
        Options       = options ?? throw new ArgumentNullException(nameof(options));

        // Pre-register default date styles so DateTime/TimeSpan cells without explicit styling
        // still render as dates rather than serial numbers. These are baked at construction so
        // the IDs are stable for the lifetime of the workbook.
        DefaultDateStyleId      = styles.GetCellXfId(new CellStyleBuilder().Format(options.DefaultDateFormat).Build());
        DefaultDateTimeStyleId  = styles.GetCellXfId(new CellStyleBuilder().Format(options.DefaultDateTimeFormat).Build());
        DefaultTimeSpanStyleId  = styles.GetCellXfId(new CellStyleBuilder().Format(options.DefaultTimeSpanFormat).Build());

        // Hyperlink default style — blue underlined text. Matches Excel's "Hyperlink" cellStyle.
        DefaultHyperlinkStyleId = styles.GetCellXfId(new CellStyleBuilder()
            .Foreground("#0563C1")
            .Underline()
            .Build());
    }

    public SharedStringTable SharedStrings { get; }
    public StyleRegistry Styles { get; }
    public XlsxWriterOptions Options { get; }

    /// <summary>cellXf id for the "date-only" default style ("yyyy-mm-dd" by default).</summary>
    public int DefaultDateStyleId { get; }

    /// <summary>cellXf id for the "date + time" default style ("yyyy-mm-dd hh:mm:ss" by default).</summary>
    public int DefaultDateTimeStyleId { get; }

    /// <summary>cellXf id for the "timespan" default style ("[h]:mm:ss" by default).</summary>
    public int DefaultTimeSpanStyleId { get; }

    /// <summary>cellXf id for hyperlink cells — blue + underlined.</summary>
    public int DefaultHyperlinkStyleId { get; }

    /// <summary>Allocates the next workbook-unique table id (1-based).</summary>
    public int NextTableId() => Interlocked.Increment(ref _nextTableId);
}
