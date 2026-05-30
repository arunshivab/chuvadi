// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.8 — Chuvadi.Pdf.Rendering.DisplayList
//
// Typed warning surfaced by DisplayListBuilder on the resulting
// PageDisplayList. Lets downstream consumers (e.g. SvgRenderer, test
// harnesses, application callers) detect that the build degraded
// gracefully — for instance, by falling back to Latin-1 byte-passthrough
// when a font dictionary cannot be resolved — without aborting the
// build. Up to v2.1.7 these conditions were swallowed silently.

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Classifies a <see cref="RenderingDiagnostic"/>. New values should be
/// added at the end so existing serialised or persisted enums survive.
/// </summary>
public enum DiagnosticKind
{
    /// <summary>
    /// The builder could not fully resolve a font and fell back to
    /// Latin-1 byte-passthrough decoding. The resulting text characters
    /// equal the raw byte codes from the content stream rather than
    /// their proper Unicode mapping; downstream output (e.g. SVG
    /// <c>&lt;text&gt;</c>) will visibly degrade. Most commonly caused
    /// by the font dictionary not being resolvable (missing entry,
    /// unresolvable indirect reference, malformed ToUnicode CMap, etc.).
    /// </summary>
    DecodeFallback = 0,
}

/// <summary>
/// A single diagnostic event recorded by <see cref="DisplayListBuilder"/>
/// during page construction. Callers can inspect
/// <see cref="PageDisplayList.Diagnostics"/> to detect graceful-degradation
/// conditions that were previously silent.
/// </summary>
/// <param name="Kind">The category of diagnostic event.</param>
/// <param name="Message">
/// Human-readable description of what went wrong, including context
/// (e.g. the font key that could not be resolved). Not localised.
/// </param>
public sealed record RenderingDiagnostic(DiagnosticKind Kind, string Message);
