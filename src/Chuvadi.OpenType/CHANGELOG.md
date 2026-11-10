# Changelog — Chuvadi.OpenType

## Unreleased (0.1.0)

Phase 1 (D-205, D-206, D-295):

- Read-only layout views (`Chuvadi.OpenType.Layout`): `OpenTypeFont.Gsub`, `Gpos` (`LayoutTable`: scripts, language
  systems, features, lookups, every GSUB type 1–8 and GPOS type 1–9 subtable, Extension resolved) and `Gdef` (glyph
  classes, mark attachment classes, mark glyph sets, attachment points, ligature carets). Coverage, ClassDef and
  Device/VariationIndex tables.
- Variable fonts at the default instance only (D-194): FeatureVariations evaluated at all coordinates 0
  (`LayoutTable.DefaultInstanceVariation`, `GetFeatureAtDefaultInstance`); VariationIndex deltas are 0.
  `OpenTypeFont.VariationAxisCount`.
- `OpenTypeFont.Metrics` (`FaceMetrics`, D-201/D-247 option A, FontFace's member names): names, weight, bold,
  italic, fixed pitch, font box, hhea/typographic/Windows values, the chosen ascender, descender and line gap,
  cap height, x-height, underline, strikeout, italic angle.
- `Os2Table.FamilyClassId`/`FamilySubclassId`, `CharacterMap.HasGlyph`, `CharacterMap.HasSymbolSubtable`,
  `CharacterMap.EnumerateVariationSequences` (D-247, D-295).
- `FontLoadOptions` (`MaxDecodedBytes`, default 256 MiB; P5(b), D-205) with new `Load`/`TryLoad` overloads on
  `OpenTypeFont` and `FontCollection`; a value not above 0 throws `ArgumentOutOfRangeException`.
- Lazily read tables are cached without allocating on later reads.
- `IsTrimmable` and `IsAotCompatible` set (D-217).

Phase 0:


- New library: sfnt and 'ttcf' collection reading, table directory, 'head', 'maxp', 'hhea', 'hmtx',
  'OS/2', 'post' header, 'name', 'kern' (format 0 values), 'cmap' formats 0/2/4/6/10/12/13/14,
  TrueType and CFF glyph bounds.
- `OpenTypeFont.IsCff`, and `OpenTypeFont.FaceProgram`: a standalone sfnt for one face of a collection (D-153; the face's
  'DSIG' table is left out). CFF2 fonts load, with `OutlineFormat.Cff2` (D-153, K1).
- WOFF 1.0 and WOFF 2.0 (including WOFF2 collections) are decoded on load (D-155); `FontContainerKind.Woff`/`Woff2`,
  `FontCollection.IsCollection` and `FontCollection.Data`. Files declaring more than 256 MiB of font data are refused.
- Damaged data never throws after loading; loading throws `OpenTypeFormatException` only for an unusable
  container, table directory, 'head' or 'maxp'.
