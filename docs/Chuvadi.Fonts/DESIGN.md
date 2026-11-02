# Chuvadi.Fonts — design

## Scope

One package for the LiPi Sans fonts that more than one library of the family needs (D-101): Chuvadi.Layout uses
them as its built-in fonts; Chuvadi.Pdf.Authoring is to read the same files instead of its own WOFF2 copies (D-113).
One copy of the fonts, one version, one licence notice.

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
