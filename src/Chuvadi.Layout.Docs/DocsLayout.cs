using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Docs
{
    /// <summary>
    /// Entry points for Word documents (.docx), read with Chuvadi.Docs and laid out the way Word lays them out (see
    /// <see cref="DocsImporter"/>). Every overload works in memory, writes nothing to disk, never touches the network and
    /// never modifies the input. Opening is cheap: pages are laid out on demand by <see cref="LaidOutDocument.GetPage"/>.
    /// </summary>
    public static class DocsLayout
    {
        /// <summary>
        /// Lays out a .docx document from a stream (read to its end, at most <see cref="LayoutLimits.MaxSourceBytes"/>,
        /// and left open). Encrypted documents need <see cref="LayoutOptions.Password"/>. Import warnings are reported in
        /// <see cref="LaidOutDocument.Warnings"/>.
        /// </summary>
        /// <exception cref="LayoutPasswordRequiredException">The document is encrypted and the password is missing (<see cref="LayoutPasswordRequiredException.PasswordGiven"/> false) or wrong (true).</exception>
        /// <exception cref="LayoutFormatException">Not a readable document.</exception>
        /// <exception cref="LayoutLimitExceededException">The document exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(Stream source, DocsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            byte[] bytes = Shared.ReadAll(source, options.Limits.MaxSourceBytes, cancellationToken);
            return OpenBytes(bytes, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a .docx document held in memory (the array is read, never modified). As <see cref="Open(Stream, DocsImportOptions?, LayoutOptions?, CancellationToken)"/>.</summary>
        /// <exception cref="LayoutPasswordRequiredException">The document is encrypted and the password is missing (<see cref="LayoutPasswordRequiredException.PasswordGiven"/> false) or wrong (true).</exception>
        /// <exception cref="LayoutFormatException">Not a readable document.</exception>
        /// <exception cref="LayoutLimitExceededException">The document exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static LaidOutDocument Open(byte[] source, DocsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new LayoutOptions();
            Shared.Check(nameof(LayoutLimits.MaxSourceBytes), options.Limits.MaxSourceBytes, source.LongLength);
            return OpenBytes(source, importOptions, options, cancellationToken);
        }

        /// <summary>Lays out a document already loaded or built with Chuvadi.Docs.</summary>
        public static LaidOutDocument Open(Chuvadi.Docs.Model.Document document, DocsImportOptions? importOptions = null, LayoutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new LayoutOptions();
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            FlowDocument flow = DocsImporter.Import(document, importOptions, options, warnings.Add, cancellationToken);
            return DocumentLayout.Open(flow, LayoutSourceFormat.Docx, warnings, options, cancellationToken);
        }

        private static LaidOutDocument OpenBytes(byte[] bytes, DocsImportOptions? importOptions, LayoutOptions options, CancellationToken ct)
        {
            List<LayoutWarning> warnings = new List<LayoutWarning>();
            using MemoryStream ms = new MemoryStream(bytes, writable: false);
            FlowDocument flow = DocsImporter.Import(ms, importOptions, options, warnings.Add, ct);
            (int? pages, string? title) = PackageProperties.Read(bytes);
            flow.PageCountHint ??= pages;
            flow.Title = title ?? flow.Title;
            return DocumentLayout.Open(flow, LayoutSourceFormat.Docx, warnings, options, ct);
        }
    }
}
