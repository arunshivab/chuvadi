using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Layout.Markup
{
    /// <summary>
    /// Lays out HTML with Chuvadi.Layout: <c>HtmlLayout.Open(html)</c> sanitizes it (by default with a new
    /// <see cref="SanitizerPolicy"/>, the safest policy) and returns the same lazy <see cref="LaidOutDocument"/> as a docx or text file,
    /// so HTML mail and pages print and export to SVG, PNG and PDF. Import warnings come first in
    /// <see cref="LaidOutDocument.Warnings"/>.
    /// </summary>
    public static class HtmlLayout
    {
        /// <summary>Sanitizes and lays out HTML text.</summary>
        public static LaidOutDocument Open(string html, HtmlImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = HtmlImporter.Import(html, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Html, warnings, options, cancellationToken);
        }

        /// <summary>Sanitizes and lays out encoded HTML (see <see cref="HtmlImporter.Import(byte[], HtmlImportOptions?, LayoutOptions?, Action{LayoutWarning}?, CancellationToken)"/> for the encoding).</summary>
        public static LaidOutDocument Open(byte[] html, HtmlImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = HtmlImporter.Import(html, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Html, warnings, options, cancellationToken);
        }

        /// <summary>
        /// Sanitizes and lays out an HTML file read from a stream (read to its end, at most
        /// <see cref="LayoutLimits.MaxSourceBytes"/>, and left open).
        /// </summary>
        public static LaidOutDocument Open(Stream source, HtmlImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
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
            return Open(ms.ToArray(), importOptions, options, cancellationToken);
        }

        /// <summary>Lays out HTML already sanitized (for example what a mail client shows).</summary>
        public static LaidOutDocument Open(SanitizeResult sanitized, HtmlImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sanitized);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = HtmlImporter.Import(sanitized, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Html, warnings, options, cancellationToken);
        }

        /// <summary>Lays out a DOM as it is, without sanitizing it (see <see cref="HtmlImporter.Import(HtmlNode, HtmlImportOptions?, LayoutOptions?, Action{LayoutWarning}?, CancellationToken)"/>).</summary>
        public static LaidOutDocument Open(HtmlNode root, HtmlImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(root);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = HtmlImporter.Import(root, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Html, warnings, options, cancellationToken);
        }
    }
}
