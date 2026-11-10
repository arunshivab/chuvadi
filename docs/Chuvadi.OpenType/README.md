# Chuvadi.OpenType

Reads OpenType and TrueType fonts in pure .NET with no dependencies (D-75 / S2 boundary B3: the shared
font reader under Chuvadi.Text.Shaping, for Layout, Print and later Pdf). Tag: `opentype-vX.Y.Z`.

Status: phase 1 of design note v1 (unreleased 0.1.0; tag `opentype-v0.1.0` once the layout views are merged, D-206 P13(a)).

## What it reads

- Containers: a single sfnt (0x00010000, 'OTTO', 'true'), 'ttcf' collections, WOFF 1.0 and WOFF 2.0 (including
  WOFF2 collections), decoded on load (`FontCollection.Kind`, `IsCollection`, `Data`). A WOFF or WOFF2 file that
  declares more decoded data than `FontLoadOptions.MaxDecodedBytes` (default 256 MiB) is refused.
- Table directory (`Tables`, `HasTable`, `TryGetTable`, `GetTableData`).
- `FaceProgram`: the face as a standalone sfnt for embedding (for a collection face, rebuilt once with its own
  directory and checksums, 'DSIG' left out; D-153). `IsCff` is true for CFF version 1 only; CFF2 fonts load
  with `OutlineFormat.Cff2` (D-153).
- 'head', 'maxp', 'hhea' (always read), 'OS/2', 'post' header, 'name', 'kern' (read on first use).
- 'cmap': formats 0, 2, 4, 6, 10, 12, 13 and 14 (`CharacterMap`, `CmapSubtable`), with a fixed choice of
  the Unicode subtable, variation-sequence lookups and enumeration, `HasGlyph` and `HasSymbolSubtable`.
- 'hmtx' advances and left side bearings.
- Glyph bounds: from 'glyf' headers (TrueType) or by running the Type 2 charstrings of 'CFF ' (curve
  extremes included, CID-keyed fonts supported).

- Layout views (namespace `Chuvadi.OpenType.Layout`): `Gsub` and `Gpos` (`LayoutTable`: script list, language
  systems, feature list, lookup list, every GSUB lookup type 1–8 and GPOS type 1–9 with Extension subtables
  resolved, Coverage, ClassDef and Device tables), and `Gdef` (`GdefTable`). Views read in place and allocate
  nothing. These are views of the data; applying lookups is the shaper's job (Chuvadi.Text.Shaping).
- Variable fonts: the default instance only (D-194). FeatureVariations are evaluated at all normalized
  coordinates 0 (`DefaultInstanceVariation`, `GetFeatureAtDefaultInstance`); VariationIndex deltas are 0.
- `Metrics` (`FaceMetrics`): names, weight and style, font box, hhea/typographic/Windows metrics and the chosen
  ascender, descender and line gap, cap height, x-height, underline, strikeout and italic angle, with the member
  names of Chuvadi.Pdf's FontFace (D-247). PDF descriptor values (StemV, flags) stay in Pdf; their raw inputs are
  here (`WeightClass`, `HeadTable.MacStyle`, `PostTable.ItalicAngle`, `IsFixedPitch`, `Os2Table.FamilyClassId`
  and `FamilySubclassId`, `Panose`, `CharacterMap.HasSymbolSubtable`).

Decoding uses only .NET's own Brotli and zlib.

Not read in this version: glyph names, glyph outlines, vertical metrics, variation deltas (non-default
instances), BASE, JSTF and MATH, colour and bitmap glyphs, CFF2 bounds, the deprecated accented-glyph form of
CFF endchar.

## Behaviour on damaged data

Loading throws `OpenTypeFormatException` only when the container header, the table directory, 'head' or
'maxp' is unusable. After that nothing throws: a table record pointing outside the data counts as absent,
and reads past the end of a table return 0.

## Trimming and AOT

`IsTrimmable` and `IsAotCompatible` are set (D-217); a NativeAOT publish gives no trim or AOT warnings.

## Thread safety and memory

`OpenTypeFont` is immutable and reads tables in place from the font bytes; tables read on first use are
cached with lock-free publication. Safe to share between threads.

## Clean-room record

Rules and their sources: [trace/rules.md](trace/rules.md). Sources read: [trace/sources.md](trace/sources.md).
