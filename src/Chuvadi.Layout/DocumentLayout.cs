using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Engine.Grid;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Import;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout
{
    /// <summary>
    /// Entry points. Every overload reads its input into memory, writes nothing to disk, never touches the
    /// network (unless a font source does) and never modifies the input. Opening is cheap: pages are laid out
    /// on demand by <see cref="LaidOutDocument.GetPage"/>.
    /// </summary>
    public static class DocumentLayout
    {
        /// <summary>Lays out a flow document built in code or by an importer.</summary>
        public static LaidOutDocument Open(FlowDocument document, LayoutOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new LayoutOptions();
            cancellationToken.ThrowIfCancellationRequested();
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver(document.EmbeddedFonts));
            FlowEngine engine = new FlowEngine(document, ctx, new LayoutSourceInfo(LayoutSourceFormat.FlowModel, document.Sections.Count, document.Title));
            return new LaidOutDocument(engine, ctx);
        }

        /// <summary>
        /// Lays out a flow document produced by an importer outside this package (for example Chuvadi.Layout.Markup):
        /// <paramref name="sourceFormat"/> is reported in <see cref="LaidOutDocument.Source"/>, and the importer's
        /// <paramref name="importWarnings"/> come first in <see cref="LaidOutDocument.Warnings"/>.
        /// </summary>
        public static LaidOutDocument Open(FlowDocument document, LayoutSourceFormat sourceFormat, IEnumerable<LayoutWarning>? importWarnings,
            LayoutOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new LayoutOptions();
            cancellationToken.ThrowIfCancellationRequested();
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver(document.EmbeddedFonts));
            if (importWarnings is not null)
            {
                foreach (LayoutWarning w in importWarnings)
                {
                    ctx.Warn(w.Code, w.Severity, w.Message, w.Location);
                }
            }
            return OpenFlow(document, ctx, sourceFormat);
        }

        /// <summary>
        /// Lays out a workbook the way Excel prints it: every visible sheet in order, each print area (or the used
        /// range) paginated with its page setup. Page bands are computed at open, so the page count is final at once.
        /// </summary>
        public static LaidOutDocument Open(GridWorkbook workbook, LayoutOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            options ??= new LayoutOptions();
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver());
            GridEngine engine = new GridEngine(workbook, ctx, new LayoutSourceInfo(LayoutSourceFormat.GridModel, workbook.Sheets.Count, workbook.Title), cancellationToken);
            return new LaidOutDocument(engine, ctx);
        }

        /// <summary>
        /// Lays out a document from a stream. With <see cref="LayoutSourceFormat.Auto"/> the format is sniffed:
        /// .docx documents go to <see cref="DocxImporter"/> and .xlsx workbooks to <see cref="XlsxImporter"/> (encrypted ones too,
        /// when <see cref="LayoutOptions.Password"/> is set); anything else is read as plain text. The stream is read to its end and left open.
        /// </summary>
        /// <exception cref="LayoutFormatException">The format is not supported in this version.</exception>
        /// <exception cref="LayoutLimitExceededException">The input exceeds <see cref="LayoutLimits.MaxSourceBytes"/>.</exception>
        public static LaidOutDocument Open(Stream source, LayoutSourceFormat format = LayoutSourceFormat.Auto, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            if (format == LayoutSourceFormat.Auto)
            {
                if (IsOleCompound(bytes) && options.Password is not null)
                {
                    // An encrypted Office file: what is inside is known only after decryption.
                    return OpenEncrypted(bytes, options, cancellationToken);
                }
                format = Sniff(bytes);
            }
            switch (format)
            {
                case LayoutSourceFormat.Text:
                    return OpenTextBytes(bytes, null, options, cancellationToken);
                case LayoutSourceFormat.Xlsx:
                    return OpenXlsxBytes(bytes, null, options, cancellationToken);
                case LayoutSourceFormat.Docx:
                    return OpenDocxBytes(bytes, null, options, cancellationToken);
                case LayoutSourceFormat.Markdown:
                    throw new LayoutFormatException("Markdown is laid out by the Chuvadi.Layout.Markup package (MarkdownLayout.Open); the core package does not depend on Chuvadi.Markup.");
                case LayoutSourceFormat.Html:
                    throw new LayoutFormatException("HTML is laid out by the Chuvadi.Layout.Markup package (HtmlLayout.Open); the core package does not depend on Chuvadi.Markup.");
                default:
                    throw new LayoutFormatException($"Unsupported source format {format}.");
            }
        }

        /// <summary>
        /// Lays out a .docx document the way Word lays it out (see <see cref="DocxImporter"/>). Encrypted documents need
        /// <see cref="LayoutOptions.Password"/>. Import warnings are reported in <see cref="LaidOutDocument.Warnings"/>.
        /// </summary>
        /// <exception cref="LayoutFormatException">Not a readable document, or the password is missing or wrong.</exception>
        /// <exception cref="LayoutLimitExceededException">The document exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument OpenDocx(Stream source, DocxImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return OpenDocxBytes(bytes, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a document already loaded or built with Chuvadi.Docs.</summary>
        public static LaidOutDocument Open(Chuvadi.Docs.Word.Document document, DocxImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = DocxImporter.Import(document, importOptions, options, warnings.Add, cancellationToken);
            return OpenFlow(flow, Context(options, flow, warnings), LayoutSourceFormat.Docx);
        }

        /// <summary>
        /// An encrypted package is a document when Chuvadi.Docs reads it (since Docs 1.5.0, a package whose main part is
        /// not a Word document is rejected), else a workbook.
        /// </summary>
        private static LaidOutDocument OpenEncrypted(byte[] bytes, LayoutOptions options, CancellationToken ct)
        {
            try
            {
                return OpenDocxBytes(bytes, null, options, ct);
            }
            catch (LayoutFormatException ex) when (ex.InnerException is Chuvadi.Docs.Word.DocxFormatException)
            {
                // Not a Word document: a workbook, or nothing readable (Chuvadi.Sheets says which).
            }
            return OpenXlsxBytes(bytes, null, options, ct);
        }

        private static LaidOutDocument OpenDocxBytes(byte[] bytes, DocxImportOptions? importOptions, LayoutOptions options, CancellationToken ct)
        {
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            using MemoryStream ms = new MemoryStream(bytes, writable: false);
            FlowDocument flow = DocxImporter.Import(ms, importOptions, options, warnings.Add, ct);
            (int? pages, string? title) = PackageProperties.Read(bytes);
            flow.PageCountHint ??= pages;
            flow.Title = title ?? flow.Title;
            return OpenFlow(flow, Context(options, flow, warnings), LayoutSourceFormat.Docx);
        }

        /// <summary>The layout context for an imported document: its embedded fonts first, then the import's warnings.</summary>
        private static LayoutContext Context(LayoutOptions options, FlowDocument flow, List<LayoutWarning> importWarnings)
        {
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver(flow.EmbeddedFonts));
            foreach (LayoutWarning w in importWarnings)
            {
                ctx.Warn(w.Code, w.Severity, w.Message, w.Location);
            }
            return ctx;
        }

        private static LaidOutDocument OpenFlow(FlowDocument flow, LayoutContext ctx, LayoutSourceFormat format)
        {
            FlowEngine engine = new FlowEngine(flow, ctx, new LayoutSourceInfo(format, flow.Sections.Count, flow.Title));
            return new LaidOutDocument(engine, ctx);
        }

        /// <summary>
        /// Lays out an .xlsx workbook the way Excel prints it (see <see cref="XlsxImporter"/>). Encrypted workbooks
        /// need <see cref="LayoutOptions.Password"/>. Import warnings are reported in <see cref="LaidOutDocument.Warnings"/>.
        /// </summary>
        /// <exception cref="LayoutFormatException">Not a readable workbook, or the password is missing or wrong.</exception>
        public static LaidOutDocument OpenXlsx(Stream source, XlsxImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return OpenXlsxBytes(bytes, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a workbook already loaded with Chuvadi.Sheets (load it with <c>IncludeStyledEmptyCells = true</c>).</summary>
        public static LaidOutDocument Open(Chuvadi.Sheets.Excel.Workbook workbook, XlsxImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            options ??= new LayoutOptions();
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver());
            GridWorkbook book = XlsxImporter.Import(workbook, importOptions, options, w => ctx.Warn(w.Code, w.Severity, w.Message, w.Location), cancellationToken);
            return OpenGrid(book, ctx, LayoutSourceFormat.Xlsx, cancellationToken);
        }

        private static LaidOutDocument OpenXlsxBytes(byte[] bytes, XlsxImportOptions? importOptions, LayoutOptions options, CancellationToken ct)
        {
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver());
            using MemoryStream ms = new MemoryStream(bytes, writable: false);
            GridWorkbook book = XlsxImporter.Import(ms, importOptions, options, w => ctx.Warn(w.Code, w.Severity, w.Message, w.Location), ct);
            book.Title ??= options.DocumentName;
            return OpenGrid(book, ctx, LayoutSourceFormat.Xlsx, ct);
        }

        private static LaidOutDocument OpenGrid(GridWorkbook book, LayoutContext ctx, LayoutSourceFormat format, CancellationToken ct)
        {
            GridEngine engine = new GridEngine(book, ctx, new LayoutSourceInfo(format, book.Sheets.Count, book.Title), ct);
            return new LaidOutDocument(engine, ctx);
        }

        private static bool IsOleCompound(ReadOnlySpan<byte> b) => b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0;

        /// <summary>Lays out plain text from a stream (BOM-detected; see <see cref="TextImporter.Decode"/>).</summary>
        public static LaidOutDocument OpenText(Stream source, TextImportOptions? textOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return OpenTextBytes(bytes, textOptions, options, cancellationToken);
        }

        /// <summary>Lays out plain text.</summary>
        public static LaidOutDocument OpenText(string text, TextImportOptions? textOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            options ??= new LayoutOptions();
            textOptions ??= new TextImportOptions();
            if (textOptions.Page is null)
            {
                // D-30: no page size in plain text; use the region's paper.
                textOptions = CopyWithPage(textOptions, PaperDefaults.PageForCulture(options.Culture));
            }
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver());
            double tab = DefaultTab(ctx.Fonts, textOptions);
            FlowDocument doc = TextImporter.ToFlowDocument(text, textOptions, tab);
            cancellationToken.ThrowIfCancellationRequested();
            FlowEngine engine = new FlowEngine(doc, ctx, new LayoutSourceInfo(LayoutSourceFormat.Text, 1, options.DocumentName));
            return new LaidOutDocument(engine, ctx);
        }

        private static TextImportOptions CopyWithPage(TextImportOptions o, PageGeometry page) => new TextImportOptions
        {
            FontFamily = o.FontFamily,
            FontSize = o.FontSize,
            Page = page,
            TabSizeInCharacters = o.TabSizeInCharacters,
            Encoding = o.Encoding,
            FormFeedIsPageBreak = o.FormFeedIsPageBreak,
        };

        private static LaidOutDocument OpenTextBytes(byte[] bytes, TextImportOptions? textOptions, LayoutOptions options, CancellationToken ct)
        {
            textOptions ??= new TextImportOptions();
            string text = TextImporter.Decode(bytes, textOptions.Encoding);
            return OpenText(text, textOptions, options, ct);
        }

        private static double DefaultTab(FontResolver fonts, TextImportOptions o)
        {
            FontFace face = fonts.Resolve(new FontRequest(o.FontFamily, 400, false, GenericFontFamily.Monospace)).Face;
            double digit = face.GetAdvance(face.GetGlyphId('0')) * o.FontSize / face.UnitsPerEm;
            return Math.Max(1, o.TabSizeInCharacters) * digit;
        }

        /// <summary>Detects the format from the first bytes.</summary>
        public static LayoutSourceFormat Sniff(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            {
                // Office Open XML: decide by the part names, in the local headers at the start or the central
                // directory at the end (some writers use '\\' as the separator).
                string head = System.Text.Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 1 << 16)]);
                string tail = System.Text.Encoding.Latin1.GetString(bytes[Math.Max(0, bytes.Length - (1 << 16))..]);
                foreach (string part in new[] { head, tail })
                {
                    // The main parts first (a document may embed a workbook, and the reverse).
                    if (part.Contains("word/document", StringComparison.Ordinal) || part.Contains("word\\document", StringComparison.Ordinal))
                    {
                        return LayoutSourceFormat.Docx;
                    }
                    if (part.Contains("xl/workbook", StringComparison.Ordinal) || part.Contains("xl\\workbook", StringComparison.Ordinal))
                    {
                        return LayoutSourceFormat.Xlsx;
                    }
                }
                foreach (string part in new[] { head, tail })
                {
                    if (part.Contains("xl/", StringComparison.Ordinal) || part.Contains("xl\\", StringComparison.Ordinal))
                    {
                        return LayoutSourceFormat.Xlsx;
                    }
                    if (part.Contains("word/", StringComparison.Ordinal) || part.Contains("word\\", StringComparison.Ordinal))
                    {
                        return LayoutSourceFormat.Docx;
                    }
                }
                throw new LayoutFormatException("The input is a ZIP archive but not a recognised Office document.");
            }
            if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
            {
                throw new LayoutFormatException("The input is an OLE compound file: an encrypted Office document (set LayoutOptions.Password) or a legacy .doc/.xls, which is not supported.");
            }
            return LayoutSourceFormat.Text;
        }

        private static byte[] ReadAll(Stream s, long limit, CancellationToken ct)
        {
            if (s.CanSeek)
            {
                long remaining = s.Length - s.Position;
                LayoutLimits.Check(nameof(LayoutLimits.MaxSourceBytes), limit, remaining);
            }
            using MemoryStream ms = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                LayoutLimits.Check(nameof(LayoutLimits.MaxSourceBytes), limit, ms.Length + read);
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }
    }
}
