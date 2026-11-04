# Chuvadi.OpenType.Oracle

Phase 0 exit check. Compares Chuvadi.OpenType with the published Chuvadi.Pdf.Fonts.Rendering 3.26.0
`FontFace` (public API only, the members listed in the pack's CONTRACT.md) on all 13 pack fonts:

- UnitsPerEm, GlyphCount, IsCff;
- advance width and left side bearing of every glyph;
- the glyph for every code point U+0000..U+10FFFF (surrogates skipped);
- the (code point, glyph) pairs of `FontFace.CharacterMap` against `CharacterMap.EnumerateMappings()`;
- for the WOFF2 fonts, our test-support decoder against Chuvadi.Pdf.Fonts.Woff2's `Woff2Unpacker`:
  identical bytes for every table except glyf/loca/hmtx/head, identical advances, side bearings and
  glyph boxes.

Glyph boxes against `FontFace.GetGlyphBounds` are not compared yet, because its return type is not in
CONTRACT.md; that comparison is added once the package XML docs arrive.

This tool was compiled here only against stand-ins with the contract's member names, so your build is
its first real build. If a member's type differs from what the contract implies, the build error will
say which, and I fix it from the XML docs.

## Run on Windows (PowerShell 5, from the worktree root)

Needs Chuvadi.Pdf.Fonts.Rendering and Chuvadi.Pdf.Fonts.Woff2 3.26.0 (and their dependencies) in
`C:\Users\aruns\Documents\local-nuget`.

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack"
dotnet run -c Release --project tools\Chuvadi.OpenType.Oracle -- $pack --out oracle-phase0.txt
```

The last line is `ORACLE AGREES` or `ORACLE DIFFERS`.
