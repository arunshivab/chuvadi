# Chuvadi.Fonts — font provenance

All files are licensed under the SIL Open Font License 1.1, each with its own copyright notice. The licence texts
ship in the package's `licenses/` folder and in `Licenses/` in the repository, and `FontPack.GetLicenseText(fileName)`
returns the one that comes with a file:

| Files | Family | Version | Licence file | Source |
|---|---|---|---|---|
| LiPi-Sans-* (22) | LiPi Sans, LiPi Sans &lt;Script&gt; | from LiPi Sans v1.0 | `LiPiSans-OFL.txt` (also `FontPack.LicenseText`) | Static instances of the LiPi Sans variable WOFF2 fonts in arunshivab/chuvadi-pdf `assets/fonts/lipi-sans` (Inter + Noto Sans) |
| NotoEmoji-Regular.ttf (1, since 1.2.0) | Noto Emoji | 3.002 | `NotoEmoji-OFL.txt` (Copyright 2013 Google LLC) | Static weight-400 instance of the monochrome Noto Emoji variable font from google/fonts; see "Noto Emoji" below |

The files moved here unchanged from Chuvadi.Layout.Fonts (D-113); the hashes below are those of the files that
package carried.

## How the static faces were made

The variable fonts were instanced with fontTools 4.66.1. Each face was instanced at wght = 400
(Regular, Italic) and wght = 700 (Bold, Bold Italic), with every other axis at its default:

```
instantiateVariableFont(font, {axis: (w if axis == 'wght' else default)}, updateFontNames=False, optimize=False)
```

The following were then set so that the face identity is correct:

- name IDs 1/2/4/6, with name IDs 16/17/25 and the Macintosh records removed
- OS/2 usWeightClass and fsSelection (BOLD / ITALIC / REGULAR)
- head.macStyle

The STAT table was removed. Glyph outlines, metrics, cmap, GSUB and GPOS are those of the
instanced design.

## Noto Emoji (1.2.0, D-418, D-420)

The monochrome (black-and-white outline) Noto Emoji face, added **unchanged**, byte for byte as the Pdf chat
delivered it with its PROVENANCE.md (v2, 26 Nov 2026). chuvadi-pdf 3.36.0 reads the same file from this package.

- **Font:** family "Noto Emoji", Regular, "Version 3.002" (name ID 5). It is a static TrueType instance at weight 400
  with `glyf` outlines only: no COLR, CBDT or sbix, and no variation tables. It has unitsPerEm 2048 and 1,891 glyphs,
  and fsType 0 (installable embedding). Its names are left exactly as Google ships them.
- **Licence:** SIL Open Font License 1.1, in `Licenses/NotoEmoji-OFL.txt`, which is the upstream `OFL.txt` byte for
  byte (4,330 bytes, SHA-256 `500bb1ccf43df7bbb522112f9133a52b16e1c35e809632f5d8609b179152de5b`). Name IDs 13 and 14
  carry the OFL text and `http://scripts.sil.org/OFL`.
- **No Reserved Font Name.** The copyright line of `OFL.txt` is exactly "Copyright 2013 Google LLC", with no "with
  Reserved Font Name …" clause, and name ID 0 is the same line. The phrase "Reserved Font Name" in `OFL.txt` appears
  only in the licence's own definitions. The weight-400 instance is a Modified Version under the OFL, but because no
  name is reserved it keeps the family name "Noto Emoji". This was checked at the pinned commit by the Pdf chat
  (D-420), which corrected the first PROVENANCE.md on this point.
- **Pinned source:**
  - google/fonts, path `ofl/notoemoji/NotoEmoji[wght].ttf` (the variable font, weight axis 300 to 700), commit
    `6ebe737e44fd64ee736bbc6feed6136a6dbfd222`;
  - URL
    `https://raw.githubusercontent.com/google/fonts/6ebe737e44fd64ee736bbc6feed6136a6dbfd222/ofl/notoemoji/NotoEmoji%5Bwght%5D.ttf`;
  - SHA-256 `de6c18832938afc99caf132b39d6a30a19bac7f2e812e28db2535b4608d27551`.
  - The monochrome build repository is googlefonts/emoji-bw, whose v3.000 release was the original source; Google
    Fonts distributes the same family as 3.002, which is what is pinned.
- **How the static face was made** (fontTools 4.67.0; reproducible, same bytes on every run):

```
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
f = TTFont("NotoEmoji[wght].ttf", recalcTimestamp=False)
instancer.instantiateVariableFont(f, {"wght": 400}, inplace=True)
f.save("NotoEmoji-Regular.ttf")
```

  `recalcTimestamp=False` keeps `head.modified` from the source, so the output does not depend on the build time.
  The first delivery (SHA-256 `b97ef098…`) was made without it and differed only in `head.modified` and the
  checksum; it was replaced before release.
- **Delivered file:** `NotoEmoji-Regular.ttf`, 886,628 bytes, SHA-256
  `1ada997ccb67bf2404828e25528e5257d6db10c80287e77684abf6737a5d198f`. `FontPackTests` and the manual test F6 check
  it.

## Files

23 files, 4,970,604 bytes: the 22 LiPi Sans files (4,083,976 bytes) and Noto Emoji. `FontPackTests` checks every row against the embedded file.

| File | Family | Style | Bytes | SHA-256 |
|---|---|---|---|---|
| LiPi-Sans-Bengali-Bold.ttf | LiPi Sans Bengali | Bold | 141,308 | `e475bf3fbcd6158aa52b418fea9028ec23e44752c6fe090b990351383bf02b69` |
| LiPi-Sans-Bengali-Regular.ttf | LiPi Sans Bengali | Regular | 140,844 | `67379428c24fede9480fdb6e5b74567dc3c2f1f3274dc01b0f0309542969a6a9` |
| LiPi-Sans-Devanagari-Bold.ttf | LiPi Sans Devanagari | Bold | 223,216 | `e80ad5f470c95ae27ed9ebbafdc19cd9647127a23353c0ea34a3571029084487` |
| LiPi-Sans-Devanagari-Regular.ttf | LiPi Sans Devanagari | Regular | 222,588 | `e93b3b6d8029e837dac7ef6d4b018a7f87614c3929772ad0f0d7cdd95c16c1e2` |
| LiPi-Sans-Gujarati-Bold.ttf | LiPi Sans Gujarati | Bold | 179,124 | `f16b4cda2d0a19d038b152c3dbe55529ed59157f550cee40b02c2c39e51fd634` |
| LiPi-Sans-Gujarati-Regular.ttf | LiPi Sans Gujarati | Regular | 178,880 | `18812df221d9f137c954d87eff8f1c1df92f33d613bdc7b6af46705e7135a219` |
| LiPi-Sans-Gurmukhi-Bold.ttf | LiPi Sans Gurmukhi | Bold | 67,220 | `142d822f6ef87a419b4454840254c8899b2dae760bc177e133babbef317cadbd` |
| LiPi-Sans-Gurmukhi-Regular.ttf | LiPi Sans Gurmukhi | Regular | 67,092 | `ca60a7ecd0d02d71a3ffce5e0353a291787ab439ff42cf15367a80ac9a0eda6e` |
| LiPi-Sans-Kannada-Bold.ttf | LiPi Sans Kannada | Bold | 169,096 | `883feb0bf2a153f974714a0c43171a51fdb7fafe5f47e00f9e9d0df0ec62bb58` |
| LiPi-Sans-Kannada-Regular.ttf | LiPi Sans Kannada | Regular | 168,936 | `b9ec4801a0a888ec352953efa966b248060668e7953d2bca65f38a550dadd1ce` |
| LiPi-Sans-Latin-Bold.ttf | LiPi Sans | Bold | 343,372 | `38e5e317dfc405b3da9247232c1891c7224d1dc0c1b877bdda1ab9540ac50776` |
| LiPi-Sans-Latin-BoldItalic.ttf | LiPi Sans | Bold Italic | 347,012 | `357737ae3791ad9966e7b81b4dbb872269480ec72b0f22be2f9f25be305ff968` |
| LiPi-Sans-Latin-Italic.ttf | LiPi Sans | Italic | 345,104 | `5b7923ab2ec3b748e1e73c2ae377fedc29712f056f50d0e83e893501a0dcd343` |
| LiPi-Sans-Latin-Regular.ttf | LiPi Sans | Regular | 341,664 | `f6e023cfaced23f7a61e99818947801f048724ddb7d5996e69a99681596df1ce` |
| LiPi-Sans-Malayalam-Bold.ttf | LiPi Sans Malayalam | Bold | 111,332 | `fe829414007f77699fb55fa837144eff7147d1e79323419bf9ce5af962581e5d` |
| LiPi-Sans-Malayalam-Regular.ttf | LiPi Sans Malayalam | Regular | 111,316 | `275c72f27c82423e0dd7775fb93e2ba267c93e10878368f85a440b2a9f24cd7c` |
| LiPi-Sans-Odia-Bold.ttf | LiPi Sans Odia | Bold | 171,624 | `ed61280a537baa4a0f3310d108c8dc748d5076b9e2eb872dc03eb7dbdbb20f6f` |
| LiPi-Sans-Odia-Regular.ttf | LiPi Sans Odia | Regular | 171,304 | `ef9994066b7ac2abbe920bbff146394289c9051c99f94cb8969e261a66dede53` |
| LiPi-Sans-Tamil-Bold.ttf | LiPi Sans Tamil | Bold | 82,532 | `5da75b9591c4e3afadedd9bda91d11c89814af6764b1bfebe9b0d82feb65288f` |
| LiPi-Sans-Tamil-Regular.ttf | LiPi Sans Tamil | Regular | 82,452 | `a5d9fb05ca958067823bea30eec091bd47051c540f505c2c4fb8e693345906d7` |
| LiPi-Sans-Telugu-Bold.ttf | LiPi Sans Telugu | Bold | 209,248 | `003fa8317915832f090a9e139ec321f6218fd40f4ea4dca8642960b55e7208d7` |
| LiPi-Sans-Telugu-Regular.ttf | LiPi Sans Telugu | Regular | 208,712 | `b39f0daff7c7c118e1c2a050136a4a3564509c244ad4983c0b7e31601df99ae7` |
| NotoEmoji-Regular.ttf | Noto Emoji | Regular | 886,628 | `1ada997ccb67bf2404828e25528e5257d6db10c80287e77684abf6737a5d198f` |
