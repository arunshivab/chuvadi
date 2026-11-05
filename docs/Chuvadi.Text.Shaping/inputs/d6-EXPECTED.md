# Malformed-font fixtures (d.6)

These are data only: font files and one line each on the expected outcome.
- **What "expected" means.** The outcome of the current engine. 3.26.0 and
  3.31.0 give identical results on every fixture. The contract's own rule
  applies to all of them: no exception, no hang, some output.
- **Glyph notation.** `glyph:cluster:x-advance:x-offset:y-offset`, in font
  units, as in the gate corpus.
- **Features.** Every fixture is shaped with `ShapingFeatures.Default`.
- **HarfBuzz 15.0.0** (the gate's reference shaper) is shown for comparison.
  Where it differs from 3.26, the fixture tests how broken data is handled,
  and the contract asks only that there is output and no exception. The exact
  glyphs are not part of the gate.
- **Hashes.** File SHA-256s are in `SHA256.tsv`.

| Fixture | Input | Expected outcome (3.26.0 = 3.31.0) | HarfBuzz 15.0.0 |
|---|---|---|---|
| `M1-recursive-context-lookup.ttf`: `M1-base-Carlito-Regular.ttf` with a GSUB whose `latn` `liga` feature has one contextual lookup (format 3, two input positions, both covering `a`), and both of its records call that same lookup. | `aaaaaaaaaaaa`, Latin | Returns the 12 `a` glyphs unchanged, `45:0:981:0:0` to `45:11:981:0:0`, promptly. Measured 3 ms with a loaded face, 36 ms including the face load; the library's test allows under 10 s, and the contract says well under 1 s. | Reports failure: the buffer is left in an error state and the binding raises an error. The contract requires output instead. |
| `M2-tamil-gsub-renamed.ttf`: `M3-base-LiPi-Sans-Tamil.ttf` with its GSUB table renamed `XSUB`, so the font has no GSUB. | `கெ` (U+0B95 U+0BC6), Tamil | Returns the pre-base vowel sign reordered before the consonant: `46:0:901:0:0 18:0:825:0:0`. | The same. |
| `M3-GSUB-round0.ttf`: the base font with 8 random bytes overwritten inside GSUB (round 0 of the M3 set). | `கொடு தமிழ் abc`, Tamil | Returns `46:0:901:0:0 18:0:825:0:0 41:0:625:0:0 116:2:1015:0:0 3:4:260:0:0 25:5:825:0:0 29:6:858:0:0 42:6:262:0:0 94:8:858:0:0 3:10:260:0:0 406:11:561:0:0 417:12:615:0:0 418:13:480:0:0`. No exception. | Differs: it drops the damaged part of GSUB (`41:0:640 23:2:693 44:2:502 … 35:8:858 52:8:0 …`). |
| `M3-GPOS-round0.ttf`: the same, inside GPOS. | `கொடு தமிழ் abc`, Tamil | Returns the same 13 glyphs as the GSUB row. No exception. | Differs in one advance (`41:0:640`). |
| `M3-GDEF-round0.ttf`: the same, inside GDEF. | `கொடு தமிழ் abc`, Tamil | Returns the same 13 glyphs as the GSUB row. No exception. | The same as 3.26. |
| `M3-corrupt-layout-patches.tsv`: all 180 variants (60 rounds each in GSUB, GPOS and GDEF), as byte patches on `M3-base-LiPi-Sans-Tamil.ttf`. Each line gives the table, the round, and 8 `offset:byte` patches, decimal, applied in order. The three round-0 lines are the three files above. | `கொடு தமிழ் abc`, Tamil | Every variant returns glyphs with no exception. Slowest variant: 94 ms with 3.26, 45 ms with 3.31, including the face load. | Not run for all 180. |
| `M4-tamil-truncated-half.ttf`: the base font cut to its first 170,350 bytes. The tables needed here (head, maxp, cmap, hhea, hmtx and the layout tables) lie in the first half. | `கெ`, Tamil | Loads, and returns `46:0:901:0:0 18:0:825:0:0`. | The same. |
| `M5-tamil-gsub-past-eof.ttf`: GSUB's table record points 1,000 bytes past the end of the file, with length 5,000. | `கெ`, Tamil | Loads, ignores GSUB, and returns `46:0:901:0:0 18:0:825:0:0`. | The same. |
| `M6-tamil-gsub-zero-length.ttf`: GSUB's table record has length 0. | `கெ`, Tamil | Loads, ignores GSUB, and returns `46:0:901:0:0 18:0:825:0:0`. | The same. |
| `M7-tamil-first-64-bytes.ttf`: the first 64 bytes of the base font (a table directory whose tables are missing). | `கெ`, Tamil | `FontFace.Load` throws `Chuvadi.Pdf.Fonts.Rendering.FontRenderingException` with the message "Font table directory points outside the file." `Shape(byte[])` lets the same exception, with the same message, pass through. The shaper is never reached, so there is no `Shape(FontFace)` call. | Returns two `.notdef` glyphs, `0:0:500:0:0 0:0:500:0:0`. |

**Both call forms.** Where a face loads (M1 to M6), `Shape(byte[])` and
`Shape(FontFace)` give identical results. `FontFace.Load` throws only on M7.
