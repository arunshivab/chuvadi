# Chuvadi.Fonts — design

## Scope

One package for the fonts that more than one library of the family needs (D-101): Chuvadi.Layout uses them as its
built-in fonts; Chuvadi.Pdf.Authoring is to read the same files instead of its own WOFF2 copies (D-113). One copy of
the fonts and one version. Since 1.2.0 there are two families with their own licence notices: LiPi Sans and the
monochrome Noto Emoji face (D-418).

- **Static TrueType only** (D-113): Regular and Bold for each Indic script, four styles for Latin. No variable
  fonts, no WOFF2: every consumer can read TrueType without a decompressor.
- **Bytes, not faces.** The package lists the files (from a catalog generated from the files' own tables) and
  hands out their bytes. Parsing, caching and font matching belong to the consumer. So the package has no
  dependencies and knows nothing of Layout's or Pdf's font types.
- **Names stay generic** apart from the font family names themselves, which D-61 keeps ("LiPi Sans …"). The enum
  is `FontScript`, not `LipiScript`, so it does not clash with Chuvadi.Pdf's.
- **No usage restriction** in the package text (D-115): the OFL allows the fonts to be embedded, and Layout's PDF
  output embeds them.

## Integrity

`FontPackFile` carries each file's length and SHA-256. The tests check every catalog row against the embedded file
(length, hash, name IDs 1/2/4/6, weight, italic flag) and that the files are byte for byte those that
Chuvadi.Layout.Fonts carried before the move.

## Versioning

Tag prefix `fonts-v*`. A font file changes only with a new version, recorded in CHANGELOG.md and FONTS.md with the
new hashes; consumers that compare output (Layout's reference corpus) then see the change in one place.

## Reading any pack the same way (1.1.0, D-195, D-197)

- `IFontFilePack` (`Name`, `Files`, `Open`, `GetLicenseText`) and the record `FontFileInfo` describe a set of font
  files without parsing them. They are additive: `FontPack` and `FontPackFile` are unchanged, and `FontPack.Instance`
  is the pack seen through the interface.
- `FontFileInfo` adds what a general pack needs and LiPi Sans does not: the typographic family (name ID 16, which the
  Selawik Light, Semilight and Semibold files of Chuvadi.Layout.Fonts carry) and the width class. It has no script
  field, because a pack such as Liberation covers several scripts.
- Chuvadi.Layout.Fonts implements the interface (`SubstituteFontPack.Instance`), and Chuvadi.Layout turns any
  implementation into a font source (`FontPackSource`). The package still knows nothing of Layout's or Pdf's types.

## Noto Emoji (1.2.0, D-418, D-420)

- **What:** the monochrome Noto Emoji face (3.002, static weight 400), added byte for byte as the Pdf chat delivered
  it, so chuvadi-pdf 3.36.0 and any other consumer read one copy. Provenance is in FONTS.md.
- **`FontScript.Emoji`**, added at the end (F2), so the earlier values keep their numbers.
- **Licences per file (F3).** The pack now holds two OFL texts with different copyright lines.
  - `GetLicenseText(fileName)`, static and on `Instance`, returns the right one.
  - `LicenseText` stays the LiPi Sans text: changing its value would change what 1.0.0 and 1.1.0 callers already ship.
- **Notices (F4).** The licence file sits in `Licenses/` beside LiPi's (embedded, and packed in `licenses/`), and the
  provenance section is in FONTS.md, as in the other font packages. There is no separate notices file.
- **No Reserved Font Name (F5):** checked against the upstream `OFL.txt` and the font's name table. So the instance, a
  Modified Version under the OFL, keeps the name "Noto Emoji".
- **Chuvadi.Layout:** it lists the face among its built-in fonts (23 instead of 22). Its output is unchanged: Layout
  picks fallback fonts by script, so emoji characters still go to LiPi Sans, and only a document that names "Noto
  Emoji" gets the face. Measured on the 88-document snapshot and the 62 previews, which are byte-identical. Using the
  face for emoji characters would be a Layout change with its own Rule 5 list.
