# Chuvadi.OpenType

Reads OpenType and TrueType fonts in pure .NET with no dependencies (D-75 / S2 boundary B3: the shared
font reader under Chuvadi.Text.Shaping, for Layout, Print and later Pdf). Tag: `opentype-vX.Y.Z`.

Status: phase 0 of design note v1 (unreleased 0.1.0).

## What it reads

- Containers: a single sfnt (0x00010000, 'OTTO', 'true'), 'ttcf' collections, WOFF 1.0 and WOFF 2.0 (including
  WOFF2 collections), decoded on load (`FontCollection.Kind`, `IsCollection`, `Data`). A WOFF or WOFF2 file that
  declares more than 256 MiB of font data is refused.
- Table directory (`Tables`, `HasTable`, `TryGetTable`, `GetTableData`).
- `FaceProgram`: the face as a standalone sfnt for embedding (for a collection face, rebuilt once with its own
  directory and checksums, 'DSIG' left out; D-153). `IsCff` is true for CFF version 1 only; CFF2 fonts load
  with `OutlineFormat.Cff2` (D-153).
- 'head', 'maxp', 'hhea' (always read), 'OS/2', 'post' header, 'name', 'kern' (read on first use).
- 'cmap': formats 0, 2, 4, 6, 10, 12, 13 and 14 (`CharacterMap`, `CmapSubtable`), with a fixed choice of
  the Unicode subtable and variation-sequence lookups.
- 'hmtx' advances and left side bearings.
- Glyph bounds: from 'glyf' headers (TrueType) or by running the Type 2 charstrings of 'CFF ' (curve
  extremes included, CID-keyed fonts supported).

Next (phase 1): read-only views of GDEF, GSUB and GPOS.

Decoding uses only .NET's own Brotli and zlib.

Not read in this version: glyph names, glyph outlines, vertical metrics, variable-font tables, colour and
bitmap glyphs, CFF2 bounds, the deprecated accented-glyph form of CFF endchar.

## Behaviour on damaged data

Loading throws `OpenTypeFormatException` only when the container header, the table directory, 'head' or
'maxp' is unusable. After that nothing throws: a table record pointing outside the data counts as absent,
and reads past the end of a table return 0.

## Thread safety and memory

`OpenTypeFont` is immutable and reads tables in place from the font bytes; tables read on first use are
cached with lock-free publication. Safe to share between threads.

## Clean-room record

Rules and their sources: [trace/rules.md](../../docs/Chuvadi.OpenType/trace/rules.md). Sources read: [trace/sources.md](../../docs/Chuvadi.OpenType/trace/sources.md).
