// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 — Indirect objects
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// Contract for resolving indirect references to their primitive values.

using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Resolves PDF indirect object references to their primitive values.
/// </summary>
/// <remarks>
/// This interface decouples the object model layer from the IO layer.
/// The <c>PdfObjectStore</c> implements it for in-memory graphs.
/// The <c>PdfReader</c> in <c>Chuvadi.Pdf.IO</c> implements it for
/// file-backed lazy resolution.
///
/// Callers that receive a <see cref="PdfPrimitive"/> and want to follow
/// any indirect references should call <see cref="Resolve"/> to unwrap
/// <see cref="PdfReference"/> instances.
///
/// PDF 32000-1:2008 §7.3.10 — Indirect objects.
/// </remarks>
public interface IPdfObjectResolver
{
    /// <summary>
    /// Resolves a <see cref="PdfPrimitive"/>, following any indirect reference.
    /// </summary>
    /// <param name="primitive">
    /// The primitive to resolve. If it is a <see cref="PdfReference"/>,
    /// the referenced object's value is returned. Otherwise the primitive
    /// itself is returned unchanged.
    /// </param>
    /// <returns>
    /// The resolved primitive. Never returns a <see cref="PdfReference"/>.
    /// Returns <see cref="PdfNull.Value"/> when the reference points to a
    /// free or missing object.
    /// </returns>
    PdfPrimitive Resolve(PdfPrimitive primitive);

    /// <summary>
    /// Resolves a <see cref="PdfObjectId"/> directly to its value.
    /// </summary>
    /// <param name="id">The object identity to look up.</param>
    /// <returns>
    /// The object's value, or <see cref="PdfNull.Value"/> when the object
    /// is free or not present.
    /// </returns>
    PdfPrimitive ResolveById(PdfObjectId id);

    /// <summary>
    /// Returns true when the object with the given identity exists and
    /// is not a free object.
    /// </summary>
    bool Contains(PdfObjectId id);
}
