// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — DSS dictionary extraction

using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Signatures.Dss;

/// <summary>
/// Extension methods on <see cref="PdfDocument"/> for accessing its Document
/// Security Store.
/// </summary>
public static class PdfDocumentDssExtensions
{
    /// <summary>
    /// Reads the document's <c>/DSS</c> dictionary and decodes its arrays.
    /// Returns null when the document has no DSS.
    /// </summary>
    public static DocumentSecurityStore? GetDocumentSecurityStore(this PdfDocument document)
    {
        System.ArgumentNullException.ThrowIfNull(document);
        return DocumentSecurityStore.TryRead(document.Catalog, document.Reader.Objects);
    }
}
