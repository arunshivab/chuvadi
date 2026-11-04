# Changelog — Chuvadi.OpenType

## Unreleased (0.1.0, phase 0)

- New library: sfnt and 'ttcf' collection reading, table directory, 'head', 'maxp', 'hhea', 'hmtx',
  'OS/2', 'post' header, 'name', 'kern' (format 0 values), 'cmap' formats 0/2/4/6/10/12/13/14,
  TrueType and CFF glyph bounds.
- `OpenTypeFont.IsCff`, and `OpenTypeFont.FaceProgram`: a standalone sfnt for one face of a collection (D-153; the face's
  'DSIG' table is left out). CFF2 fonts load, with `OutlineFormat.Cff2` (D-153, K1).
- Damaged data never throws after loading; loading throws `OpenTypeFormatException` only for an unusable
  container, table directory, 'head' or 'maxp'.
