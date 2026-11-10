# Old-style Indic script tag fixtures (d.5 follow-up, D-201)

Data only, with no HarfBuzz code: two fonts and their expected glyphs.

**Why these fixtures exist.** No font in the pack has only old-style Indic
script tags (`deva`, `mlym` and so on), so the engine's old-style path was
untested. These two fixtures cover it.

## The fonts

| File | Made from | Change |
|---|---|---|
| `fonts/LiPi-Sans-Devanagari-OldTags.ttf` | The pack's `LiPi-Sans-Devanagari.woff2` | The `dev2` script record is removed from GSUB and GPOS. `deva` stays. |
| `fonts/LiPi-Sans-Malayalam-OldTags.ttf` | The pack's `LiPi-Sans-Malayalam.woff2` | The `mlm2` script record is removed from GSUB and GPOS. `mlym` stays. |

- **What is unchanged:** each font keeps the old-style lookups its own
  designer wrote. Only the name table also changes: the family is now
  "LiPi Sans Devanagari OldTags" or "LiPi Sans Malayalam OldTags", so neither
  can be mistaken for the real face.
- **Why these two faces:** they are the only LiPi faces with an old-style
  tag in both GSUB and GPOS.
- **Licence:** LiPi Sans is under the SIL OFL 1.1, as in the pack's
  `LiPi-Sans-LICENSE.txt`. Its reserved names, Inter and Noto, are not used.

## The expected glyphs

| File | Lines | Lines that differ from the new-style expected glyphs |
|---|---|---|
| `corpus/oldtags-devanagari.tsv` | 3,428 | 137 |
| `corpus/oldtags-malayalam.tsv` | 3,315 | 1,671 |

- **Inputs:** every Devanagari or Malayalam line of `corpus/syllables.tsv`
  and `corpus/words.tsv`, in order.
- **How they were made:** with HarfBuzz 15.0.0 (uharfbuzz 0.57.0), using the
  d.2 revision 2 settings: direction LTR, script Deva or Mlym, no language,
  default features (`cpsp` off, the D-68 set).
- **Check of the method:** the same run on the original face reproduces every
  pack line exactly.
- **Format:** the same four columns as the other corpus files.

## Checked against 3.31.0

`check-3.31.0.txt` is the d.3 harness (revision 2) run on these two files
against published 3.31.0: 6,743 of 6,743 lines match, with the parity check
on. To run it yourself:
1. Make a folder with `fonts/` holding the two fonts and `corpus/words.tsv`
   holding the lines of both files.
2. Run the harness with `--pack <that folder> --set words`.

`SHA256.tsv` lists every file.
