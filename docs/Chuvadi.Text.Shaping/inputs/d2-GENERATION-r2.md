# How the expected outputs in cleanroom-shaping-pack were generated (d.2)

Revision 2, 2026-11-05 (D-182): the language is now set explicitly, see
"Language" below. The 29,493 expected lines are unchanged.

Data only. Every one of the 29,493 lines (syllables.tsv and words.tsv) used
the same settings, so one block covers them all. I checked this on
2026-11-05 by regenerating every line with these settings and comparing:
all 29,493 lines are identical to the pack, and every font in the pack is
byte-identical to the font that was shaped.

## Versions

| Item | Value |
|---|---|
| HarfBuzz | 15.0.0 (git tag `15.0.0`) |
| Python binding | uharfbuzz 0.57.0, which reports HarfBuzz `15.0.0` |
| Unicode version of that build | Unicode 18.0. HarfBuzz's own release notes: 14.5.0 "Update Unicode 18.0 data and script support"; 15.0.0 is later. The build also recognises scripts added in Unicode 17.0 (Sidetic, Beria Erfe, Tai Yo). |
| Font reader | fontTools (used only to turn each font into plain sfnt bytes, below) |

## Per-line settings (identical on every line)

| Setting | Value |
|---|---|
| Script tag | Set explicitly from the corpus `script` column: Latin `Latn`, Devanagari `Deva`, Bengali `Beng`, Gurmukhi `Guru`, Gujarati `Gujr`, Odia `Orya`, Tamil `Taml`, Telugu `Telu`, Kannada `Knda`, Malayalam `Mlym`. These are the ISO 15924 tags; the engine picks the font's OpenType script (for example `dev2` or `deva`) itself. |
| Direction | Left to right, set explicitly. |
| Language | **Explicitly none: the fonts' default language system (`dflt`), on every line (D-182).** The lines were regenerated with the language left unset on purpose, never taken from the machine: no automatic guess of segment properties, so direction and script set explicitly and no language. The 29,493 regenerated lines are identical to the pack. Nothing reads the locale, so no machine setting can change them. (The original run guessed the language from the process locale; it was POSIX, which gives `c`, which also selects the default language system, so nothing changed.) For the record, a Marathi language would change 274 Devanagari lines through the fonts' `MAR` language system, which is why it must never come from the machine. |
| Features passed | None. Every line used the shaper's defaults: the expected outputs are `ShapingFeatures.Default` in the contract's terms. |
| `cpsp` | Off (D-68). Checked: with no features passed, capitals keep their plain advances; with `cpsp` on they change. |
| Other defaults that differ from the contract's feature table | (1) Features the OpenType feature registry marks as on by default, or as required for the script, were applied without being requested, as the contract says ("features the script requires are always applied"). The ones present in the pack fonts are `rlig`, `curs`, `dist`, `rclt`, `abvm`, `blwm`, and the Indic `nukt` `akhn` `rphf` `rkrf` `pref` `blwf` `half` `pstf` `vatu` `cjct` `init` `pres` `abvs` `blws` `psts` `haln`, each where a font has it. Carlito also has `c2sc`, `lnum` and `onum`; those are optional and were not applied. (2) **The fraction slash.** Digits around U+2044 FRACTION SLASH were shaped with the font's numerator and denominator forms, although `frac`, `numr` and `dnom` are off. The corpus line `0031 2044 0032 …` on LiPi Sans Latin gives glyphs `1701 1673 1678` (numerator 1, slash, denominator 2), not `1342 1673 1343` (plain digits). It is in the corpus on every Latin font. |
| Buffer flags | None (0): no beginning-of-text or end-of-text flag, and nothing about dotted circles or default ignorables was set. |
| Cluster setting | The default, level 0 (monotone grapheme clusters). The text was added as code points (UTF-32), so clusters are code-point indices. The corpus has no code point above U+FFFF, so these equal the UTF-16 indices that the contract uses. |
| Dotted circle | Default behaviour, inserted where a mark has no base. Nothing was disabled. |
| Invisible glyph | Not set (0). Replacement code point: the default, U+FFFD. Not-found glyph: 0. |
| Font scale | The default, which is the font's units per em. So all advances and offsets in the corpus are in font units, with no rounding to pixels. No variations; the default OpenType font functions. |
| Face index | 0 for every font. No collection fonts are used. |

## How the fonts were passed

Not as the original file bytes. Each font was opened with fontTools,
the WOFF2 flavour cleared, and the font saved again as a plain sfnt.
Those bytes were given to HarfBuzz.
- **The nine `LiPi-Sans-*.woff2` fonts** were therefore WOFF2-unpacked by
  fontTools.
- **`Carlito-Regular.ttf`, `LiberationSerif-Regular.ttf` and
  `ChuvadiTest-Cff.otf`** were also re-saved by fontTools, not passed as the
  original files.
- Re-saving changes the table order, padding and checksums, but no table
  content. The glyph ids, cmap and layout tables are those of the font file
  in the pack. Chuvadi's own gate run unpacks the WOFF2 fonts with
  `Chuvadi.Pdf.Fonts.Woff2` and loads the TTF/OTF files as they are, and
  matches every line, which confirms this.

## The font files shaped

The same files are in the pack's `fonts` folder, byte for byte (SHA-256):

| File | SHA-256 |
|---|---|
| `LiPi-Sans-Bengali.woff2` | `3f90f475b19d9cbd45ad103375edaed687df7aae0cfc7e2d11fc41b37695a256` |
| `LiPi-Sans-Devanagari.woff2` | `e63b9d28ed90cc66e7f925985dc41db5216e384f7efabb0e03a4816f6ed0e509` |
| `LiPi-Sans-Gujarati.woff2` | `13bc1d8b5ea947f2347e9234084fe2607f35c351645bc2c58c20dad707032a9d` |
| `LiPi-Sans-Gurmukhi.woff2` | `2b7fd15cb73e54c82ff4660d60b20649d2ab2de5ddf47d8787ced72ddb7f6721` |
| `LiPi-Sans-Kannada.woff2` | `a0190229d44c3959a287e19deb0667cbe8ce0229836c1bc0afc45d960461e0a1` |
| `LiPi-Sans-Latin.woff2` | `7efa337627066d147cc2e1cb5b114245245145ab7752d04b8356cb28470963d3` |
| `LiPi-Sans-Malayalam.woff2` | `648cfe11e841ba617453a3b3c59a3a9973212d02290ab21a28a14257ba6fcc78` |
| `LiPi-Sans-Odia.woff2` | `074aac37143de8388ea95eb2ff27812eb961529259ad741f912a67a2b5d979e6` |
| `LiPi-Sans-Tamil.woff2` | `2938de75c31e0d36702856d9f23093475c4d0b7eae4deed3273c2aaeb2067522` |
| `LiPi-Sans-Telugu.woff2` | `626f4847a387ee19bd3d17514d516f94f7b14293b253307bf9520ecb9376e7a6` |
| `Carlito-Regular.ttf` | `f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595` |
| `LiberationSerif-Regular.ttf` | `058ea80864aef09a23f45cbec2bb5400bc3dfbdea01c3f10538a21fcb497fb74` |
| `ChuvadiTest-Cff.otf` | `7f30cb9e4b7e9394088d5cee4ef9bbc78f04d7f7656dbb4250950643e4ba8258` |
