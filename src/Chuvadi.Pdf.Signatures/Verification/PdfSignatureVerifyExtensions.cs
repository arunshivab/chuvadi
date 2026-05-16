// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// The user-visible <c>Verify()</c> entry point on <see cref="PdfSignature"/>.
/// </summary>
public static class PdfSignatureVerifyExtensions
{
    /// <summary>
    /// Verifies <paramref name="signature"/> against the bytes it covers in
    /// <paramref name="document"/>.
    /// </summary>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="document">The PDF the signature came from.</param>
    /// <param name="options">Optional verification settings.</param>
    /// <returns>A <see cref="SignatureVerificationResult"/> describing the outcome.</returns>
    public static SignatureVerificationResult Verify(
        this PdfSignature signature,
        PdfDocument document,
        SignatureVerifyOptions? options = null)
        => PdfSignatureVerifier.Verify(signature, document, options);
}
