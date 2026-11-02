using System;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Sheets;

namespace Chuvadi.Layout.Documents
{
    /// <summary>
    /// One entry point for a file of unknown kind: the format is detected and the file goes to Chuvadi.Layout.Docs
    /// (.docx), Chuvadi.Layout.Sheets (.xlsx) or the engine (plain text). With <see cref="LayoutOptions.Password"/> an
    /// encrypted Office file is decrypted and laid out as whichever it is. This is what the engine's
    /// <see cref="DocumentLayout.Open(Stream, LayoutSourceFormat, LayoutOptions?, CancellationToken)"/> did before the
    /// importers became their own packages (D-115). Works in memory; writes nothing to disk; never touches the network.
    /// </summary>
    public static class AutoDetectLayout
    {
        /// <summary>
        /// Lays out a document from a stream (read to its end, at most <see cref="LayoutLimits.MaxSourceBytes"/>, and
        /// left open). With <see cref="LayoutSourceFormat.Auto"/> the format is sniffed (<see cref="DocumentLayout.Sniff"/>):
        /// .docx documents go to <see cref="DocsLayout"/>, .xlsx workbooks to <see cref="SheetsLayout"/> (encrypted ones
        /// too, when <see cref="LayoutOptions.Password"/> is set), anything else is read as plain text.
        /// </summary>
        /// <exception cref="LayoutFormatException">Not readable, the password is missing or wrong, or the format is not supported here (Markdown and HTML: Chuvadi.Layout.Markup).</exception>
        /// <exception cref="LayoutLimitExceededException">The input exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(Stream source, LayoutSourceFormat format = LayoutSourceFormat.Auto, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = Shared.ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return Open(bytes, format, options, cancellationToken);
        }

        /// <summary>Lays out a document held in memory (the array is read, never modified). As <see cref="Open(Stream, LayoutSourceFormat, LayoutOptions?, CancellationToken)"/>.</summary>
        /// <exception cref="LayoutFormatException">Not readable, the password is missing or wrong, or the format is not supported here.</exception>
        /// <exception cref="LayoutLimitExceededException">The input exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(byte[] source, LayoutSourceFormat format = LayoutSourceFormat.Auto, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            Shared.Check(nameof(LayoutLimits.MaxSourceBytes), options.Limits.MaxSourceBytes, source.LongLength);
            if (format == LayoutSourceFormat.Auto)
            {
                if (IsOleCompound(source) && options.Password is not null)
                {
                    // An encrypted Office file: what is inside is known only after decryption.
                    return OpenEncrypted(source, options, cancellationToken);
                }
                format = DocumentLayout.Sniff(source);
            }
            switch (format)
            {
                case LayoutSourceFormat.Docx:
                    return DocsLayout.Open(source, null, options, cancellationToken);
                case LayoutSourceFormat.Xlsx:
                    return SheetsLayout.Open(source, null, options, cancellationToken);
                default:
                    // Text, and the engine's own messages for Markdown, HTML and anything unsupported.
                    using (MemoryStream ms = new MemoryStream(source, writable: false))
                    {
                        return DocumentLayout.Open(ms, format, options, cancellationToken);
                    }
            }
        }

        /// <summary>
        /// An encrypted package is a document when Chuvadi.Docs reads it (since Docs 1.5.0, a package whose main part is
        /// not a Word document is rejected), else a workbook.
        /// </summary>
        private static LaidOutDocument OpenEncrypted(byte[] bytes, LayoutOptions options, CancellationToken ct)
        {
            try
            {
                return DocsLayout.Open(bytes, null, options, ct);
            }
            catch (LayoutFormatException ex) when (ex.InnerException is Chuvadi.Docs.Word.DocxFormatException)
            {
                // Not a Word document: a workbook, or nothing readable (Chuvadi.Sheets says which).
            }
            return SheetsLayout.Open(bytes, null, options, ct);
        }

        private static bool IsOleCompound(ReadOnlySpan<byte> b) => b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0;
    }
}
