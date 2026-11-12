# Black-box statement on fallback mark positioning and missing spaces (from the Pdf chat, 2026-11-11)

Received through the library coordinator, for the two questions raised at 809 of 852 Latin lines. Observed output only
(rule 2): HarfBuzz through uharfbuzz 0.57.0, the corpus reference, run as a black box on ChuvadiTest-Cff.otf (10
glyphs, 1000 units per em, no GPOS, no glyph for any combining mark). No source was read. Filed as received; Arun's
decision between option (a), no fallback, and option (b), a fallback, is still to come, and nothing here is
implemented yet.

## Marks with no glyph, in a font without GPOS

- Each mark becomes .notdef with advance 0 and keeps its base's cluster.
- Horizontal: the mark's ink is centred on the base's ink box (A gives x −550, x gives −500, W gives −900). On a base
  with no ink (a space) it is centred on the base's advance (−375). On a missing base, the base's .notdef keeps its 500
  advance and the mark is centred on it (−500).
- Vertical, by canonical combining class:
  - Above (230, for example U+0301, U+0300): the mark's bottom sits 62 units above the base's ink top: 762 on A (700
    tall), 562 on x (500 tall).
  - Below (220, for example U+0323, U+0331): the mark's top sits 62 below the base's bottom (−762).
  - Attached below (202, cedilla, ogonek): touching, no gap (−700).
  - Attached above right (216, horn): touching the top (y 700), shifted right (x −450).
  - Overlay (1, U+0334): centred on the base, y 0.
  - Double above (234, U+0361): centred on the boundary between the two bases (x −250), above them (y 762).
- The gap is 62 here, matching 1/16 em rounded down.
- Stacking: above and below are separate stacks; each further mark goes 62 past the previous mark's ink (a second acute
  at 1524, a second dot below at −1524).
- Marks are first put into canonical order, so A acute dot-below and A dot-below acute give the same output (dot below
  −762 first, then acute 762). An acute after a dot below stays at 762 on A; a below mark never raises an above mark.
- A mark with no base (start of text): advance 0, x −500, y 0.

## Missing space characters

The font's own space glyph is used, with a width by kind of space as a fraction of the em, rounded to nearest:

| Character | Width (1000 upem) | Rule as stated |
|---|---|---|
| U+2000 en quad, U+2002 en space | 500 | 1/2 em |
| U+2001 em quad, U+2003 em space, U+3000 ideographic space | 1000 | 1 em |
| U+2004 three-per-em space | 333 | 1/3 em |
| U+2005 four-per-em space | 250 | 1/4 em |
| U+2006 six-per-em space | 167 | 1/6 em |
| U+2009 thin space | 200 | 1/5 em |
| U+200A hair space | 63 | 1/16 em (62.5 rounded up) |
| U+202F narrow no-break space | 125 | 1/8 em |
| U+205F medium mathematical space | 222 | 4/18 em |
| U+00A0 no-break space | 250 | the space glyph's own advance |
| U+2007 figure space | 250 | the width of a digit (here the space glyph's advance) |
| U+2008 punctuation space | 250 | the width of the period; the font has none, so the space glyph's advance |
| U+200B zero-width space | 0 | the space glyph, advance 0 |
| U+1680 ogham space mark | 500 | not replaced: drawn as .notdef |

## Notes for Arun's decision (mine, not part of the statement)

- Where the Unicode Standard (chapter 6, space characters) names a width, the statement agrees with it: en 1/2, em 1,
  three-, four- and six-per-em, thin 1/5, medium mathematical 4/18, figure (digit width), punctuation (period width),
  no-break (as space).
- The standard gives no number for the hair space ("thinner than a thin space") or the narrow no-break space ("narrow …
  typically the width of a thin space or a space"). The 1/16 em and 1/8 em values come only from this statement.
- For marks, the standard describes the model (canonical combining classes for position; stacking outward from the
  base, chapter 5 on nonspacing marks) but no distances. The 1/16 em gap, the centring on the ink box and the
  per-class placements come only from this statement.

## Later correction (2026-11-12)

The horizontal reference "centred on the base's ink box" is corrected by `fallback-narrow-base-statement.md`: the mark is
centred on the base's advance. The wide bases above (A, x, W) have their ink centred in their advance, so their numbers
are unchanged. D-361 (2026-11-12) approved both scope points: the mark fallback only in a font with no GPOS table, and
canonical order always.
