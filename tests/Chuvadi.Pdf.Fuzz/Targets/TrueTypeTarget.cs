// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;

namespace Chuvadi.Pdf.Fuzz.Targets;

/// <summary>
/// Fuzz target for the TrueType font parser.
/// </summary>
/// <remarks>
/// Constructs a <see cref="TrueTypeLoader"/> from arbitrary bytes. The loader's
/// constructor parses the offset table, head, hhea, maxp, and cmap tables, exercising
/// the most attack-prone portions of the TTF format. <see cref="FontRenderingException"/>
/// is the documented exception type; anything else is a crash.
/// </remarks>
internal sealed class TrueTypeTarget : IFuzzTarget
{
    public string Name => "truetype";

    public IReadOnlyList<Type> ExpectedExceptionTypes { get; } = new[]
    {
        typeof(FontRenderingException),
        typeof(System.IO.EndOfStreamException),
        typeof(FormatException),
        typeof(ArgumentException),   // some BCL paths in offset arithmetic surface ArgumentException
    };

    public void Run(byte[] input)
    {
        TrueTypeLoader loader = new(input);
        // Exercise some glyph-lookup paths on the parsed font.
        _ = loader.UnitsPerEm;
        _ = loader.NumGlyphs;
        _ = loader.GetGlyphIndex(0x0041);   // 'A'
        _ = loader.GetGlyphIndex(0x4E2D);   // '中' — common BMP non-ASCII
    }
}
