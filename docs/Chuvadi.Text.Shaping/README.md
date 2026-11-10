# Chuvadi.Text.Shaping

The clean-room OpenType shaper (D-52, decisions D-74 to D-81). Depends only on Chuvadi.OpenType.
Tag: `text-shaping-vX.Y.Z`.

Status: phase 1 of design note v1 (unreleased 0.1.0; tag `text-shaping-v0.1.0` only after the gate passes,
D-206 P13(a)). It maps characters to glyphs with their default advances only; GSUB and GPOS are applied from
the phase 1 engine onward (plan P2(b): 1 views + engine + Latin, 2 north Indic, 3 south Indic, 4 words to 100%,
5 helpers, 6 hardening, 7 hand-over). The helpers H1–H4 and H6 are approved (D-155) and come in phase 5.

## API (approved shape A3, core part)

- `ShapingFont.Get(OpenTypeFont)`: one shaping font per font, cached; safe to share between threads.
- `ShapeOptions`: `Script` (ISO 15924), `Language` (OpenType language system), `Direction`
  (right-to-left refused until D-57d), `Features` (tag, value, UTF-16 range), `UseDefaultFeatures`.
- `TextShaper.Shape(font, text, start, length, options, GlyphBuffer)`: shapes the run [start, start+length)
  of `text`; the rest of `text` is context. Plus overloads for the whole text and an allocating
  `ShapedRun` form.
- `GlyphBuffer`: reusable output; glyph ids, clusters (UTF-16 indices into the text passed, D-66) and
  exact positions in font units (D-67).
- Diagnostics (D-246): `GlyphBuffer.Diagnostics` and `ShapedRun.Diagnostics` list `ShapingDiagnostic`
  values (`MissingGlyph`: a character has no glyph; `ScriptNotInFont`: the font's layout tables do not list the
  script). Each kind at most once per call; empty, with nothing allocated, when there is nothing to report.

Not yet in the API: `Language.FromBcp47` (phase 2, P12(a)) and the helpers (phase 5).

`IsTrimmable` and `IsAotCompatible` are set (D-217).

## Unicode data

Own tables generated from the pinned UCD 18.0.0 by `tools/Chuvadi.Text.Shaping.UcdGen` (S7). The
Unicode License v3 notice ships in the package as `UnicodeLicense.txt`. Normalisation (NFC/NFD) is our
own, checked against the whole of NormalizationTest.txt, so results don't depend on the operating
system or the .NET runtime.

## Clean-room record

Rules: [trace/rules.md](trace/rules.md). Experiments: [experiments/](experiments/). Sources:
[../Chuvadi.OpenType/trace/sources.md](../Chuvadi.OpenType/trace/sources.md). Review and similarity
reports: [similarity/](similarity/).
