# Chuvadi.Text.Shaping

The clean-room OpenType shaper (D-52, decisions D-74 to D-81). Depends only on Chuvadi.OpenType.
Tag: `text-shaping-vX.Y.Z`.

Status: phase 0 of design note v1 (unreleased 0.1.0). It maps characters to glyphs with their default
advances only; the full pipeline comes in phases 3 to 6, and the line-breaking helpers only after Arun
approves the helper list.

## API (approved shape A3, core part)

- `ShapingFont.Get(OpenTypeFont)`: one shaping font per font, cached; safe to share between threads.
- `ShapeOptions`: `Script` (ISO 15924), `Language` (OpenType language system), `Direction`
  (right-to-left refused until D-57d), `Features` (tag, value, UTF-16 range), `UseDefaultFeatures`.
- `TextShaper.Shape(font, text, start, length, options, GlyphBuffer)`: shapes the run [start, start+length)
  of `text`; the rest of `text` is context. Plus overloads for the whole text and an allocating
  `ShapedRun` form.
- `GlyphBuffer`: reusable output; glyph ids, clusters (UTF-16 indices into the text passed, D-66) and
  exact positions in font units (D-67).

Not yet in the API: `Language.FromBcp47` (needs the OpenType language-tag registry as data) and the
helpers (H1 to H8, pending approval).

## Unicode data

Own tables generated from the pinned UCD 17.0.0 by `tools/Chuvadi.Text.Shaping.UcdGen` (S7). The
Unicode License v3 notice ships in the package as `UnicodeLicense.txt`. Normalisation (NFC/NFD) is our
own, checked against the whole of NormalizationTest.txt, so results don't depend on the operating
system or the .NET runtime.

## Clean-room record

Rules: [trace/rules.md](trace/rules.md). Experiments: [experiments/](experiments/). Sources:
[../Chuvadi.OpenType/trace/sources.md](../Chuvadi.OpenType/trace/sources.md). Review and similarity
reports: [similarity/](similarity/).
