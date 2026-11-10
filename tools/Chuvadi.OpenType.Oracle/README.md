# Chuvadi.OpenType.Oracle

Compares Chuvadi.OpenType with the published Chuvadi.Pdf.Fonts.Rendering `FontFace` (public API only,
from the package's signatures and XML docs). Default package version: **3.32.1** (D-295), fetched
from the chuvadi-pdf GitHub release and checked against its `SHA256SUMS.txt`. Chuvadi.Pdf.Text.Shaping
is never used.

Fonts checked: `<pack>/fonts` (13), and when present `<pack>/oldtags/fonts` (2) and `<pack>/uvs/fonts` (1).
For each font:

- UnitsPerEm, GlyphCount, IsCff;
- advance width, left side bearing and glyph box (`GetGlyphBounds`, a `FontBox`) of every glyph;
- the glyph for every code point U+0000..U+10FFFF (surrogates skipped);
- the (code point, glyph) pairs of `FontFace.CharacterMap` against `CharacterMap.EnumerateMappings()`;
- every `FaceMetrics` member against the `FontFace` member of the same name (names, weight, style,
  font box, hhea/typo/Win values, chosen ascender/descender/line gap, cap height, x-height,
  underline, strikeout, italic angle) (D-201, D-247);
- format 14: every sequence `CharacterMap.EnumerateVariationSequences()` lists, plus the code
  points just before and after each, against `FontFace.GetVariantGlyphIndex` (-1 when not listed);
- for the WOFF2 fonts, Chuvadi.OpenType's own WOFF2 decoding against Chuvadi.Pdf.Fonts.Woff2's
  `Woff2Unpacker`: identical bytes for every table except glyf/loca/hmtx/head, identical advances,
  side bearings and glyph boxes.

When `<pack>/uvs/variants-3.31.1.tsv` exists, its 22 probes are checked against the recorded 3.31.1
value and against the live `FontFace`.

Run against 3.31.0 or later: 3.26.0 does not map code points above U+FFFF from format 12 cmap
subtables, which this tool reports as differences.

## Run on Windows (PowerShell 5, from the worktree root)

Needs the seven Chuvadi.Pdf 3.32.1 release packages in `C:\Users\aruns\Documents\local-nuget`.

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack\cleanroom-shaping-pack"
dotnet run -c Release --project tools\Chuvadi.OpenType.Oracle -- $pack --out oracle-3.32.1.txt
```

The last line is `ORACLE AGREES` or `ORACLE DIFFERS`. Another package version: add
`-p:ChuvadiVersion=<version>` before `--`.

`Woff2Unpacker` is obsolete since 3.31.0; the warning is suppressed on its one call (D-179), and that
cross-check is removed when the class is.
