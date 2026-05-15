// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11 — Optional content (layers)
// PHASE: Phase 1.1.7 — Chuvadi.Pdf.Documents optional content

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// An Optional Content Group (OCG) — a named, toggleable layer in a PDF.
/// </summary>
/// <remarks>
/// PDF 32000 §8.11 defines optional content as graphics that can be selectively
/// shown or hidden by the viewer. Common uses: anatomical overlays in medical
/// imaging, engineering drawing layers, multi-language annotation sets.
/// </remarks>
public sealed class OptionalContentGroup
{
    /// <summary>Initialises a new <see cref="OptionalContentGroup"/>.</summary>
    public OptionalContentGroup(
        string name,
        bool isVisibleByDefault,
        IReadOnlyList<string> intents)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsVisibleByDefault = isVisibleByDefault;
        Intents = intents ?? throw new ArgumentNullException(nameof(intents));
    }

    /// <summary>Gets the human-readable layer name (from /Name).</summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether the layer is visible in the default configuration.
    /// Computed from the document's /D/ON and /D/OFF arrays.
    /// </summary>
    public bool IsVisibleByDefault { get; }

    /// <summary>
    /// Gets the layer's intents (e.g., "View", "Design"). Empty when unspecified.
    /// </summary>
    public IReadOnlyList<string> Intents { get; }
}
