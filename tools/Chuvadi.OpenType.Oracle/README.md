# Chuvadi.OpenType.Oracle

Phase 0 exit check. Compares Chuvadi.OpenType with the published Chuvadi.Pdf.Fonts.Rendering 3.31.0
`FontFace` (public API only: the members listed in the pack's CONTRACT.md, plus `GetGlyphBounds`) on all 13 pack fonts:

- UnitsPerEm, GlyphCount, IsCff;
- advance width, left side bearing and glyph box (`GetGlyphBounds`, a `FontBox`) of every glyph;
- the glyph for every code point U+0000..U+10FFFF (surrogates skipped);
- the (code point, glyph) pairs of `FontFace.CharacterMap` against `CharacterMap.EnumerateMappings()`;
- for the WOFF2 fonts, Chuvadi.OpenType's own WOFF2 decoding against Chuvadi.Pdf.Fonts.Woff2's `Woff2Unpacker`:
  identical bytes for every table except glyf/loca/hmtx/head, identical advances, side bearings and
  glyph boxes.

Member types are taken from the 3.31.0 package's public signatures and XML docs (FontBox for
`GetGlyphBounds`). Run against 3.31.0 or later: 3.26.0 does not map code points above U+FFFF from
format 12 cmap subtables, which this tool reports as differences.

## Run on Windows (PowerShell 5, from the worktree root)

Needs Chuvadi.Pdf.Fonts.Rendering and Chuvadi.Pdf.Fonts.Woff2 3.31.0 (and their dependencies) in
`C:\Users\aruns\Documents\local-nuget`.

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack\cleanroom-shaping-pack"
dotnet run -c Release --project tools\Chuvadi.OpenType.Oracle -- $pack --out oracle-3.31.0.txt
```

The last line is `ORACLE AGREES` or `ORACLE DIFFERS`. Another package version: add
`-p:ChuvadiVersion=<version>` before `--`.

`Woff2Unpacker` is obsolete in 3.31.0; the warning is suppressed on its one call (D-179), and that
cross-check is removed when the class is.
