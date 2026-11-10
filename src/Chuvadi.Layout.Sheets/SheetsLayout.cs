using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Sheets
{
    /// <summary>
    /// Entry points for Excel workbooks (.xlsx), read with Chuvadi.Sheets and laid out the way Excel prints them (see
    /// <see cref="SheetsImporter"/>). Every overload works in memory, writes nothing to disk, never touches the network
    /// and never modifies the input. Page bands are computed at open, so the page count is final at once.
    /// </summary>
    public static class SheetsLayout
    {
        /// <summary>
        /// Lays out an .xlsx workbook from a stream (read to its end, at most <see cref="LayoutLimits.MaxSourceBytes"/>,
        /// and left open). Encrypted workbooks need <see cref="LayoutOptions.Password"/>. Import warnings are reported in
        /// <see cref="LaidOutDocument.Warnings"/>.
        /// </summary>
        /// <exception cref="LayoutPasswordRequiredException">The workbook is encrypted and the password is missing (<see cref="LayoutPasswordRequiredException.PasswordGiven"/> false) or wrong (true).</exception>
        /// <exception cref="LayoutFormatException">Not a readable workbook.</exception>
        /// <exception cref="LayoutLimitExceededException">The workbook exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(Stream source, SheetsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = Shared.ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return OpenBytes(bytes, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out an .xlsx workbook held in memory (the array is read, never modified). As <see cref="Open(Stream, SheetsImportOptions?, LayoutOptions?, CancellationToken)"/>.</summary>
        /// <exception cref="LayoutPasswordRequiredException">The workbook is encrypted and the password is missing (<see cref="LayoutPasswordRequiredException.PasswordGiven"/> false) or wrong (true).</exception>
        /// <exception cref="LayoutFormatException">Not a readable workbook.</exception>
        /// <exception cref="LayoutLimitExceededException">The workbook exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(byte[] source, SheetsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            Shared.Check(nameof(LayoutLimits.MaxSourceBytes), options.Limits.MaxSourceBytes, source.LongLength);
            return OpenBytes(source, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a workbook already loaded with Chuvadi.Sheets (load it with <c>IncludeStyledEmptyCells = true</c>).</summary>
        public static LaidOutDocument Open(Chuvadi.Sheets.Model.Workbook workbook, SheetsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            GridWorkbook book = SheetsImporter.Import(workbook, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(book, LayoutSourceFormat.Xlsx, warnings, options, cancellationToken);
        }

        private static LaidOutDocument OpenBytes(byte[] bytes, SheetsImportOptions? importOptions, LayoutOptions options, CancellationToken ct)
        {
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            using MemoryStream ms = new MemoryStream(bytes, writable: false);
            GridWorkbook book = SheetsImporter.Import(ms, importOptions, options, warnings.Add, ct);
            book.Title ??= options.DocumentName;
            return DocumentLayout.Open(book, LayoutSourceFormat.Xlsx, warnings, options, ct);
        }
    }
}
