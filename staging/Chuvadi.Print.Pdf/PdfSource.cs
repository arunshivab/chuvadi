using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Print.Pdf
{
    /// <summary>
    /// One opened source PDF plus, on demand, copies with annotations and/or form fields baked into the page
    /// content (Chuvadi.Pdf draws page content only, so annotations are printed by flattening them first).
    /// Documents with no annotations are never copied.
    /// </summary>
    internal sealed class PdfSource : IDisposable
    {
        private static readonly PdfName AnnotsKey = PdfName.Intern("Annots");

        private readonly byte[] _bytes;
        private readonly string? _password;
        private readonly Dictionary<AnnotationFlattenKinds, PdfDocument> _flattened = new Dictionary<AnnotationFlattenKinds, PdfDocument>();
        private bool? _hasAnnotations;
        private bool _disposed;

        public PdfSource(ReadOnlyMemory<byte> document, string? password)
        {
            if (document.IsEmpty)
            {
                throw new ArgumentException("The document is empty.", nameof(document));
            }

            _bytes = MemoryMarshal.TryGetArray(document, out ArraySegment<byte> segment)
                     && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : document.ToArray();
            _password = password;
            Original = Open(_bytes);
        }

        /// <summary>The document as given.</summary>
        public PdfDocument Original { get; }

        public int PageCount => Original.PageCount;

        /// <summary>
        /// The document to draw from: the original, or a copy with the chosen annotation kinds flattened
        /// (cached per combination).
        /// </summary>
        public PdfDocument For(bool includeAnnotations, bool includeFormFields)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AnnotationFlattenKinds kinds = AnnotationFlattenKinds.None;
            if (includeAnnotations)
            {
                kinds |= AnnotationFlattenKinds.Markup;
            }

            if (includeFormFields)
            {
                kinds |= AnnotationFlattenKinds.FormFields;
            }

            if (kinds == AnnotationFlattenKinds.None || !HasAnnotations())
            {
                return Original;
            }

            if (_flattened.TryGetValue(kinds, out PdfDocument? cached))
            {
                return cached;
            }

            AnnotationFlattenOptions options = new AnnotationFlattenOptions
            {
                Kinds = kinds,
                SkipHiddenAndNoView = true,
                DropRemainingAnnotations = true,
            };
            byte[] flattenedBytes;
            using (MemoryStream output = new MemoryStream())
            {
                AnnotationFlattener.Flatten(output, Original, options);
                flattenedBytes = output.ToArray();
            }

            // The flattened copy is written without encryption, so it opens without the password.
            PdfDocument flattened = PdfDocument.Open(new MemoryStream(flattenedBytes, writable: false));
            _flattened[kinds] = flattened;
            return flattened;
        }

        private bool HasAnnotations()
        {
            if (_hasAnnotations is bool known)
            {
                return known;
            }

            bool found = false;
            for (int i = 0; i < Original.PageCount && !found; i++)
            {
                found = Original.Pages[i].Dictionary.ContainsKey(AnnotsKey);
            }

            _hasAnnotations = found;
            return found;
        }

        private PdfDocument Open(byte[] bytes)
        {
            MemoryStream stream = new MemoryStream(bytes, writable: false);
            return _password is null ? PdfDocument.Open(stream) : PdfDocument.Open(stream, _password);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (PdfDocument doc in _flattened.Values)
            {
                doc.Dispose();
            }

            _flattened.Clear();
            Original.Dispose();
        }
    }
}
