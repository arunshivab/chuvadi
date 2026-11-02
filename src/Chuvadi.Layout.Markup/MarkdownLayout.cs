using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Import;
using Chuvadi.Layout.Model;
using M = Chuvadi.Markup.Markdown;

namespace Chuvadi.Layout.Markup
{
    /// <summary>
    /// Lays out Markdown with Chuvadi.Layout: <c>MarkdownLayout.Open(markdown)</c> returns the same lazy
    /// <see cref="LaidOutDocument"/> as a docx or text file, so pages render to SVG, PNG and PDF through
    /// Chuvadi.Layout.Pdf. Import warnings come first in <see cref="LaidOutDocument.Warnings"/>.
    /// </summary>
    public static class MarkdownLayout
    {
        /// <summary>Lays out Markdown text.</summary>
        public static LaidOutDocument Open(string markdown, MarkdownImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(markdown);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = MarkdownImporter.Import(markdown, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Markdown, warnings, options, cancellationToken);
        }

        /// <summary>
        /// Lays out a Markdown file read from a stream: the encoding comes from a byte-order mark, else
        /// <paramref name="encoding"/>, else UTF-8 (see <see cref="TextImporter.Decode"/>). The stream is read to its end
        /// (at most <see cref="LayoutLimits.MaxSourceBytes"/>) and left open.
        /// </summary>
        public static LaidOutDocument Open(Stream source, MarkdownImportOptions? importOptions = null, LayoutOptions? options = null,
            Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            using MemoryStream ms = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Limits.Check(nameof(LayoutLimits.MaxSourceBytes), options.Limits.MaxSourceBytes, ms.Length + read);
                ms.Write(buffer, 0, read);
            }
            return Open(TextImporter.Decode(ms.GetBuffer().AsSpan(0, (int)ms.Length), encoding), importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a Markdown AST (parsed with Chuvadi.Markup, or built in code).</summary>
        public static LaidOutDocument Open(M.MarkdownDocument document, MarkdownImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = MarkdownImporter.Import(document, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Markdown, warnings, options, cancellationToken);
        }
    }
}
