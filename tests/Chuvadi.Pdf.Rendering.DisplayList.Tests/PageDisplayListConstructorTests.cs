// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.8 — Chuvadi.Pdf.Rendering.DisplayList tests
//
// Tests for the v2.1.8 additions to PageDisplayList: the Diagnostics
// property and the six-argument constructor that accepts it. Older
// constructors must continue to work and default Diagnostics to an
// empty, non-null list.
//
// These tests target the NEWER Chuvadi.Pdf.Rendering.DisplayList project
// only — the test project's csproj deliberately omits the older
// Chuvadi.Pdf.Rendering reference to avoid namespace ambiguity (both
// projects declare namespace Chuvadi.Pdf.Rendering.DisplayList and define
// their own PageDisplayList).

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class PageDisplayListConstructorTests
{
    // ── 6-arg constructor (new in v2.1.8) ─────────────────────────────────

    [Fact]
    public void SixArgConstructor_ExposesProvidedDiagnostics()
    {
        IReadOnlyList<RenderingDiagnostic> diagnostics =
        [
            new RenderingDiagnostic(DiagnosticKind.DecodeFallback, "font 'F1' missing"),
            new RenderingDiagnostic(DiagnosticKind.DecodeFallback, "font 'F2' malformed"),
        ];

        PageDisplayList list = new PageDisplayList(
            ops: Array.Empty<RenderOp>(),
            mediaWidth: 612,
            mediaHeight: 792,
            rotation: 0,
            fontDictsByKey: new Dictionary<string, PdfDictionary>(),
            diagnostics: diagnostics);

        list.Diagnostics.Should().HaveCount(2);
        list.Diagnostics[0].Kind.Should().Be(DiagnosticKind.DecodeFallback);
        list.Diagnostics[0].Message.Should().Be("font 'F1' missing");
        list.Diagnostics[1].Message.Should().Be("font 'F2' malformed");
    }

    [Fact]
    public void SixArgConstructor_NullDiagnostics_Throws()
    {
        Action act = () => new PageDisplayList(
            ops: Array.Empty<RenderOp>(),
            mediaWidth: 612,
            mediaHeight: 792,
            rotation: 0,
            fontDictsByKey: new Dictionary<string, PdfDictionary>(),
            diagnostics: null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("diagnostics");
    }

    // ── Legacy constructor compatibility ──────────────────────────────────

    [Fact]
    public void FiveArgConstructor_DefaultsDiagnosticsToEmpty()
    {
        // Existing callers that only supply ops + page metrics + fonts must
        // see Diagnostics as a non-null, empty, read-only collection.

        PageDisplayList list = new PageDisplayList(
            ops: Array.Empty<RenderOp>(),
            mediaWidth: 612,
            mediaHeight: 792,
            rotation: 0,
            fontDictsByKey: new Dictionary<string, PdfDictionary>());

        list.Diagnostics.Should().NotBeNull();
        list.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void FourArgConstructor_DefaultsDiagnosticsToEmpty()
    {
        // Even older legacy callers (4-arg) must also default Diagnostics
        // to a non-null empty list, since the chain ultimately reaches
        // the 6-arg constructor.

        PageDisplayList list = new PageDisplayList(
            ops: Array.Empty<RenderOp>(),
            mediaWidth: 612,
            mediaHeight: 792,
            rotation: 0);

        list.Diagnostics.Should().NotBeNull();
        list.Diagnostics.Should().BeEmpty();
    }

    // ── RenderingDiagnostic record semantics ──────────────────────────────

    [Fact]
    public void RenderingDiagnostic_RecordEquality_ComparesByValue()
    {
        // Records compare by value, not reference. This matters for the
        // builder's deduplication path which uses a HashSet<(Kind, Message)>.

        RenderingDiagnostic a = new RenderingDiagnostic(DiagnosticKind.DecodeFallback, "same");
        RenderingDiagnostic b = new RenderingDiagnostic(DiagnosticKind.DecodeFallback, "same");
        RenderingDiagnostic c = new RenderingDiagnostic(DiagnosticKind.DecodeFallback, "different");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
