// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 §12.7.4.5 (signature fields) + §12.5.2 (annotations)
// PHASE: Phase 1.2.6 — signature appearances


namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Visible appearance for a signature field. When supplied on
/// <see cref="PdfSigningOptions.Appearance"/>, the signature field is
/// also marked as a Widget annotation, placed on a specific page within
/// the given rectangle, and gets an appearance stream that PDF readers
/// render in the page view.
/// </summary>
/// <remarks>
/// <para>
/// A signature is cryptographically complete without an appearance,
/// and most automated workflows don't need one. Visible appearances
/// matter for human-reviewed documents (contracts, agreements) where a
/// reader expects to see "signed by …" on the page.
/// </para>
/// <para>
/// Chuvadi generates a minimal default appearance — a thin black border
/// with the signer's common name and the signing time as a label —
/// unless <see cref="PreRenderedAppearanceStream"/> is supplied, in
/// which case those bytes are used verbatim as the Form XObject content
/// stream and the caller is responsible for whatever fonts / colors /
/// images they reference.
/// </para>
/// </remarks>
public sealed class SignatureAppearance
{
    /// <summary>Zero-based index of the page on which to place the widget.</summary>
    public required int PageIndex { get; init; }

    /// <summary>
    /// Rectangle in default user space: [llx, lly, urx, ury].
    /// </summary>
    public required double[] Rectangle { get; init; }

    /// <summary>
    /// Optional pre-rendered Form XObject content stream. When null, a
    /// default appearance is generated with the signer's common name
    /// and signing time as text.
    /// </summary>
    public byte[]? PreRenderedAppearanceStream { get; init; }
}
