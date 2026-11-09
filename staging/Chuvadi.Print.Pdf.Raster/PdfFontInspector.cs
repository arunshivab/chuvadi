using System;
using System.IO;
using System.Runtime.InteropServices;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Print.Pdf.Raster
{
    /// <summary>
    /// The <see cref="IPdfFontInspector"/> for <see cref="PrintRouter"/>: reads a PDF with Chuvadi.Pdf's
    /// <c>FontEmbeddingReport</c> and says whether it can be sent to a printer as it is (D-189 item 7, D-235 L2).
    /// </summary>
    /// <remarks>
    /// <para>The answer (D-235 L2.2, L2.3):</para>
    /// <list type="bullet">
    /// <item><see cref="FontEmbedding.AllEmbedded"/>: every font the document can use is embedded, and no form
    /// field or comment needs its appearance generated.</item>
    /// <item><see cref="FontEmbedding.NeedsGeneratedAppearances"/>: every font is embedded, but some form fields or
    /// comments have no stored appearance (or the form asks for appearances to be generated). A printer would not
    /// draw them as a viewer does, so the router keeps such a PDF off the as-is routes.</item>
    /// <item><see cref="FontEmbedding.NotAllEmbedded"/>: at least one font is not embedded (standard 14 fonts
    /// included); <see cref="LastReport"/> lists each one with its page and place.</item>
    /// <item><see cref="FontEmbedding.Unknown"/>: part of the document could not be read, the file is not a PDF,
    /// or the password is missing or wrong; <see cref="LastError"/> holds the reason.</item>
    /// </list>
    /// <para>The check reads resources, so a font that is named but never used still counts; the answer errs
    /// towards raster. Keep one inspector per router (it remembers the last report), as with other per-caller types.</para>
    /// </remarks>
    public sealed class PdfFontInspector : IPdfFontInspector
    {
        /// <summary>Password for encrypted documents (user or owner password). Default none.</summary>
        public string? Password { get; set; }

        /// <summary>The full report of the most recent check; null before the first, or when the document could not be read.</summary>
        public FontEmbeddingReport? LastReport { get; private set; }

        /// <summary>Why the most recent check answered <see cref="FontEmbedding.Unknown"/> without a report; otherwise null.</summary>
        public Exception? LastError { get; private set; }

        /// <inheritdoc />
        public FontEmbedding Inspect(ReadOnlyMemory<byte> pdf)
        {
            LastReport = null;
            LastError = null;
            if (pdf.IsEmpty)
            {
                LastError = new ArgumentException("The document is empty.", nameof(pdf));
                return FontEmbedding.Unknown;
            }

            byte[] bytes = MemoryMarshal.TryGetArray(pdf, out ArraySegment<byte> segment)
                           && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : pdf.ToArray();
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using PdfDocument document = Password is null ? PdfDocument.Open(stream) : PdfDocument.Open(stream, Password);
                FontEmbeddingReport report = FontEmbeddingReport.Check(document);
                LastReport = report;
                return Map(report.Verdict, report.NeedsGeneratedAppearances);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LastError = ex;
                return FontEmbedding.Unknown;
            }
        }

        /// <summary>The mapping of D-235 L2.2 and L2.3.</summary>
        internal static FontEmbedding Map(FontEmbeddingVerdict verdict, bool needsGeneratedAppearances) => verdict switch
        {
            FontEmbeddingVerdict.AllEmbedded => needsGeneratedAppearances ? FontEmbedding.NeedsGeneratedAppearances : FontEmbedding.AllEmbedded,
            FontEmbeddingVerdict.NotAllEmbedded => FontEmbedding.NotAllEmbedded,
            _ => FontEmbedding.Unknown
        };
    }
}
