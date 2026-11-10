# Format 14 test font (variation sequences), D-206

Data only, with no HarfBuzz code: one font and one answer file, for the
shaper's oracle.

## Files

| File | What it is |
|---|---|
| `fonts/LiPi-Sans-Latin-UVS.ttf` | The pack's `LiPi-Sans-Latin.woff2` as TrueType, with one format 14 subtable added under (0,5). |
| `variants-3.31.1.tsv` | One line per base and selector pair, with the glyph 3.31.1's `FontFace.GetVariantGlyphIndex` gives. |
| `SHA256.tsv` | The SHA-256 of every file. |

## The font

- **What changed:** a format 14 subtable is added under (0,5). The name table
  family is now "LiPi Sans Latin UVS", so the font cannot be mistaken for the
  real face. Nothing else changed: every other table, and the existing
  formats 4 and 12, are as in the pack.
- **Licence:** LiPi Sans is under the SIL OFL 1.1 (the pack's
  `LiPi-Sans-LICENSE.txt`). Its reserved names, Inter and Noto, are not used.
- **Why not a Noto font:**
  - Noto Sans CJK and Noto Serif CJK do have large format 14 tables, but
    they are 17 to 26 MB, CFF outlines inside TTC collections. Inter and Noto
    are reserved names, so a subset would also have to be renamed.
  - Noto Color Emoji is 10 MB of bitmaps, with one selector.

## What the subtable holds

Every case was chosen on purpose.

| Selector | Default ranges (the base's own glyph) | Non-default mappings (a named glyph) |
|---|---|---|
| U+FE00 (VS1) | U+0041 to U+0043 | U+0030 to `zero.slash` (Unicode's standardized slashed zero); U+0031 to `one.ss01`; U+0042 to `g.sc` |
| U+FE01 (VS2) | U+4E00, which the font has no glyph for | U+4E01 to `a`, a base the font has no glyph for |
| U+FE0E (VS15) | U+2190 to U+2193 | (none) |
| U+FE0F (VS16) | (none) | U+0067 to `g.sc`; U+006C to `l.ss02` |
| U+E0100 (VS17) | (none) | U+1F130 to `A` (a supplementary base and selector) |
| U+E0101 (VS18) | U+1F12F to U+1F131 (supplementary) | (none) |

## The answers (`variants-3.31.1.tsv`)

- **What it holds:** 22 lines. 15 are listed sequences. 7 are probes that
  must give -1: just past a range, a base listed under another selector, a
  selector that is not in the table, and a code point that is not a selector.
- **How it was made:** by running 3.31.1 (`38c82c2`) as a program. The file
  also gives each base's own glyph, and HarfBuzz 15.0.0's
  `hb_font_get_variation_glyph` for comparison.

**Two lines differ from HarfBuzz, both edge cases put in on purpose:**

| Pair | 3.31.1 | HarfBuzz | Why |
|---|---|---|---|
| U+0042 + VS1 | 668 (`g.sc`, the non-default glyph) | 65 (the base's own glyph) | The pair is in both lists, which the spec does not allow. 3.31.1 checks the non-default list first; HarfBuzz checks the default ranges first. |
| U+4E00 + VS2 | 0 | not found | The default range names a base the font cannot draw. 3.31.1 returns the base's glyph, which is 0 (.notdef). HarfBuzz treats the sequence as not found, then shapes the base alone, which is .notdef too. |

On the 20 other lines, both give the same answer.
