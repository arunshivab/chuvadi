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
        /// Lays out a flow document produced by an importer outside this package (for example Chuvadi.Layout.Docs or Chuvadi.Layout.Markup):
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
        /// Lays out a workbook produced by an importer outside this package (for example Chuvadi.Layout.Sheets):
        /// <paramref name="sourceFormat"/> is reported in <see cref="LaidOutDocument.Source"/>, and the importer's
        /// <paramref name="importWarnings"/> come first in <see cref="LaidOutDocument.Warnings"/>. Page bands are
        /// computed at open, so the page count is final at once.
        /// </summary>
        public static LaidOutDocument Open(GridWorkbook workbook, LayoutSourceFormat sourceFormat, IEnumerable<LayoutWarning>? importWarnings,
            LayoutOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            options ??= new LayoutOptions();
            LayoutContext ctx = new LayoutContext(options, options.CreateFontResolver());
            if (importWarnings is not null)
            {
                foreach (LayoutWarning w in importWarnings)
                {
                    ctx.Warn(w.Code, w.Severity, w.Message, w.Location);
                }
            }
            return OpenGrid(workbook, ctx, sourceFormat, cancellationToken);
        }

        /// <summary>
        /// Lays out plain text from a stream. With <see cref="LayoutSourceFormat.Auto"/> the format is sniffed
        /// (<see cref="Sniff"/>): text is laid out here; a .docx document, an .xlsx workbook or an encrypted Office file
        /// throws <see cref="LayoutFormatException"/> naming the package that lays it out (Chuvadi.Layout.Docs,
        /// Chuvadi.Layout.Sheets, or Chuvadi.Layout.Documents, whose AutoDetectLayout.Open keeps this method's former
        /// behaviour for every format). The stream is read to its end and left open.
        /// </summary>
        /// <exception cref="LayoutFormatException">The format is laid out by another package, or not supported.</exception>
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
                    throw new LayoutFormatException("The input is an encrypted Office file: lay it out with the Chuvadi.Layout.Documents package (AutoDetectLayout.Open), which decrypts it and tells a document from a workbook, or with DocsLayout.Open (Chuvadi.Layout.Docs) or SheetsLayout.Open (Chuvadi.Layout.Sheets) when the format is known. The engine package depends on neither Chuvadi.Docs nor Chuvadi.Sheets.");
                }
                format = Sniff(bytes);
            }
            switch (format)
            {
                case LayoutSourceFormat.Text:
                    return OpenTextBytes(bytes, null, options, cancellationToken);
                case LayoutSourceFormat.Xlsx:
                    throw new LayoutFormatException("An .xlsx workbook is laid out by the Chuvadi.Layout.Sheets package (SheetsLayout.Open), or by Chuvadi.Layout.Documents (AutoDetectLayout.Open), which detects the format; the engine package does not depend on Chuvadi.Sheets.");
                case LayoutSourceFormat.Docx:
                    throw new LayoutFormatException("A .docx document is laid out by the Chuvadi.Layout.Docs package (DocsLayout.Open), or by Chuvadi.Layout.Documents (AutoDetectLayout.Open), which detects the format; the engine package does not depend on Chuvadi.Docs.");
                case LayoutSourceFormat.Markdown:
                    throw new LayoutFormatException("Markdown is laid out by the Chuvadi.Layout.Markup package (MarkdownLayout.Open); the core package does not depend on Chuvadi.Markup.");
                case LayoutSourceFormat.Html:
                    throw new LayoutFormatException("HTML is laid out by the Chuvadi.Layout.Markup package (HtmlLayout.Open); the core package does not depend on Chuvadi.Markup.");
                default:
                    throw new LayoutFormatException($"Unsupported source format {format}.");
            }
        }

        private static LaidOutDocument OpenFlow(FlowDocument flow, LayoutContext ctx, LayoutSourceFormat format)
        {
            FlowEngine engine = new FlowEngine(flow, ctx, new LayoutSourceInfo(format, flow.Sections.Count, flow.Title));
            return new LaidOutDocument(engine, ctx);
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
