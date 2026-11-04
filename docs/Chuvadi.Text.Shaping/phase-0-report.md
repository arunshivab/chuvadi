# Clean-room shaper — phase 0 report

Date: 2026-11-01. Branch: `feat/shaping-phase0`. Design note: v1 (project doc `claude/D-52-cleanroom-shaper-design-note-v1.md`).
Decisions in force: D-66, D-67, D-68, D-69, D-73, D-74 to D-81, binding rule 5.

Status (2026-11-02): D-75 is confirmed and the Chuvadi.OpenType work has resumed. The Pdf chat's plan C1: Pdf's rasterizer
keeps its own loader; only Pdf.Authoring moves onto Chuvadi.OpenType, once it passes its gate. See "Pdf.Authoring
swap" below.

## Built

| Part | Where | State |
|---|---|---|
| Chuvadi.OpenType core reader | src/Chuvadi.OpenType | done; layout-table views are phase 1 |
| Unicode table generator (UCD 17.0.0) | tools/Chuvadi.Text.Shaping.UcdGen, tools/ucd/17.0.0 | done |
| Chuvadi.Text.Shaping core API, phase-0 body (cmap + advances) | src/Chuvadi.Text.Shaping | done |
| Own NFC/NFD normalisation | src/Chuvadi.Text.Shaping/Unicode | done |
| Contract wrapper for chuvadi-pdf | separate zip, Chuvadi.Pdf.Text.Shaping/ | done; Pdf chat integrates later |
| Local gate (explicit features, parity, speed, font check, phase targets) | tools/Chuvadi.Text.Shaping.Gate | done |
| FontFace oracle check | tools/Chuvadi.OpenType.Oracle | written; first real build is on Arun's machine |
| WOFF2 decoder, test support only (not shipped) | tools/Chuvadi.Fonts.TestSupport | done |
| Trace log, sources log, check test | docs/Chuvadi.OpenType/trace, docs/Chuvadi.Text.Shaping/trace | 34 + 12 rules; wrapper 4 rules |

## Results in my environment (Linux, .NET 10.0.403)

- Local gate: exactly 2,144 of 29,493 lines (7.27%), the phase target (the skeleton's baseline). PHASE TARGET MET.
- Byte-array parity: 29,493 of 29,493 identical.
- Font check: all 13 pack fonts; every glyph box inside the 'head' box; lsb = xMin on every glyph.
- Pack's official harness through the wrapper: 2,144 / 29,493, as expected for phase 0. This used
  sandbox stand-ins for FontFace and Woff2Unpacker built on our own reader, because I don't have the 3.26.0
  packages. Your run is the real one.
- Tests: Chuvadi.OpenType 36/36 (after FaceProgram), Chuvadi.Text.Shaping 15/15. Also built and tested inside a copy of
  monorepo main 7916282 with its Directory.Build.props; the whole Chuvadi.slnx test run stayed green. These cover:
  - every code point checked against the UCD files;
  - the whole of NormalizationTest.txt;
  - synthetic fonts for every cmap format;
  - TTC collections and CFF bounds;
  - 3,000 randomly damaged fonts, plus 500 garbage CFF tables;
  - concurrency;
  - the trace check.
- Generator: rerunning it gives byte-identical output.
- DLL sizes: Chuvadi.OpenType 60 KB; Chuvadi.Text.Shaping 130 KB, of which about 100 KB is Unicode table data.
- Speed: 0.2–1.2 µs per call with a cached font, 1.2–5.3 µs with the font loaded from bytes on every call.
  This is cmap-only, so it says nothing yet about D-73. Real numbers start in phase 3.

## Bug found and fixed during the phase

The test-support WOFF2 decoder wrote every point coordinate as int16. That made the rebuilt 'glyf' larger
than the original, and the short 'loca' offsets overflowed past 128 KB. The result was wrong glyph boxes
for later glyphs in 7 of the 10 WOFF2 fonts. Advances were unaffected, which is why the gate still read
2,144. Fixed by using the standard compact point encoding (with a switch to long 'loca' if the table is
still too big), and guarded from now on by the gate's `--fontcheck`.

## Pdf.Authoring swap (plan C1)

What Authoring uses today, as the coordinator listed it, and where it lives in Chuvadi.OpenType:

| Authoring needs | FontFace (3.26) | Chuvadi.OpenType |
|---|---|---|
| cmap | GetGlyphIndex(cp), CharacterMap (dictionary) | `font.CharacterMap.GetGlyph(cp)`; `font.CharacterMap.EnumerateMappings()` (code point, glyph) pairs |
| advance widths | GetAdvanceWidth(gid) | `font.GetAdvanceWidth(gid)` (also `GetLeftSideBearing`) |
| units per em | UnitsPerEm | `font.UnitsPerEm` |
| CFF or not | IsCff | `font.IsCff` (added 2026-11-02), or `font.OutlineFormat` |
| face index | FaceIndex | `font.FaceIndex` |
| program bytes | FontProgram (byte[]) | `font.Data` (ReadOnlyMemory of the bytes loaded) |
| TTC faces | FaceIndex on load | `OpenTypeFont.Load(bytes, faceIndex)`; `FontCollection.Load(bytes).Count` / `GetFont(i)` |
| errors | FontRenderingException | `OpenTypeFormatException` (a FormatException) for every unusable font |

Also there for a PDF font descriptor: `Names.GetName(6)` (PostScript name), `Head` (bounding box, units per em),
`Hhea` and `Os2` (ascender, descender, cap height, weight, fsType embedding flags), `Post` (italic angle,
fixed pitch), and glyph boxes.

Four points need an answer before the swap is truly straight (for the Pdf chat, from FontFace's documentation):
1. Program bytes for a face of a collection: does FontFace.FontProgram return the whole 'ttcf' file or a standalone
   sfnt for that face? `font.Data` is the whole file. If Authoring embeds the program in the PDF it needs one face's
   sfnt, and Chuvadi.OpenType would add a method that writes that face out as a standalone font (not built; for approval).
2. CharacterMap as a dictionary: Chuvadi.OpenType gives a lookup method and an enumeration, not a dictionary. A
   dictionary view can be added if Authoring indexes it directly (for approval).
3. Errors: Chuvadi.OpenType throws OpenTypeFormatException for bad data, but ArgumentNullException for null bytes
   and ArgumentOutOfRangeException for a face index outside the collection. Does FontFace use FontRenderingException
   for those too? If so, and a single type is wanted, I'd add a common base (for approval).
4. IsCff for a CFF2 font: Chuvadi.OpenType says false (it has OutlineFormat.Cff2). What does FontFace say?

## Pdf chat answers (2026-11-04, from tests of the published 3.30.2 packages and their API docs)

1. A face of a collection: FontFace.FontProgram is a standalone font for that one face. Authoring embeds it as a Type0
   font (TrueType subset in FontFile2; CFF face data in FontFile3 /CIDFontType0C). AddTrueTypeFont uses face 0;
   AddFont(name, bytes, faceIndex) picks another. Needs an API addition: A1 below.
2. CharacterMap: Authoring only enumerates the pairs once. `EnumerateMappings()` is enough; no dictionary view.
   The oracle now compares the pair set with FontFace.CharacterMap.
3. Exceptions: ArgumentNullException for null bytes, ArgumentOutOfRangeException for a bad face index (negative, past
   the count, non-zero for a single font), FontRenderingException only for bad data. Chuvadi.OpenType already matches
   for the arguments (OpenTypeFormatException for bad data). No common base exception.
4. CFF2: FontFace.Load throws; IsCff is true only for CFF 1. Chuvadi.OpenType loads CFF2 fonts (OutlineFormat.Cff2,
   IsCff false). Decision K below.

### Decided (D-153, Arun, 2026-11-05): A1 = S1 with the face's DSIG dropped, built in phase 0 (`OpenTypeFont.FaceProgram`, rule R-OT-0035, 2 new tests); K = K1, CFF2 keeps loading and Authoring's adapter refuses it with its own FontRenderingException.

### The options as they were put to Arun

A1. Standalone font for one face (needed for Authoring's swap).
- S1 (recommended): `OpenTypeFont.FaceProgram` (ReadOnlyMemory<byte>). For a plain sfnt it is the bytes as loaded, no
  copy. For a face of a collection it is built once on first use and cached: that face's tables copied into a new
  sfnt with its own table directory (sorted by tag, 4-byte padding, recomputed table checksums and
  head.checkSumAdjustment, per the OpenType "Table Directory" and "Calculating Checksums" rules).
  Pros: same meaning as FontFace.FontProgram, so the swap is mechanical; no cost for plain fonts; Layout and Print
  embedding get it too. Cons: one copy of a face's bytes per collection face used (for a 20 MB CJK collection face,
  about the face's own size); bytes are not identical to FontFace's output (table order and padding may differ),
  though the font is equivalent.
- S2: make `Data` itself the standalone face for collection faces, plus a new `ContainerData` for the whole file.
  Pros: `FontProgram` maps to `Data` one-to-one. Cons: every collection face is copied at load even when not
  embedded; changes the meaning of an existing member.
- S3: no addition; Authoring builds the face itself. Cons: font-format code in Pdf, which D-58 and D-75 move away from.
- Open detail for S1/S2: a per-face 'DSIG' table no longer matches after extraction. Drop it (recommended) or keep it.

K. CFF2 fonts.
- K1 (recommended): keep loading them (OutlineFormat.Cff2, IsCff false, glyph boxes not read); Authoring's adapter
  refuses Cff2 with its own FontRenderingException. Pros: the reader stays useful to Layout and Print for metrics and
  cmap of CFF2 fonts. Cons: the swap needs one extra check in Authoring.
- K2: `OpenTypeFont.Load` throws OpenTypeFormatException for CFF2. Pros: identical to FontFace. Cons: no other user
  can read CFF2 fonts; allowing them later is a behaviour change.
- K3: a load option that refuses unsupported outline formats. Pros: both behaviours. Cons: more API to keep.

## Different from design note v1 (for Arun)

1. Unicode tables are about 100 KB, not the 20–40 KB I estimated. They cover general category, combining
   class, both Indic categories, default-ignorable and script for all code points, plus decompositions.
   They can be cut to about 60 KB with a denser decomposition table and smaller blocks, at the cost of a
   slightly slower lookup. Your choice; I have not changed anything.
2. `Language.FromBcp47` is not in this phase. It needs the OpenType language-tag registry as data
   (another generated table); I propose phase 2.
3. `GlyphBuffer` has no per-glyph flags yet. They belong to helper H1, which is waiting for approval.
4. `ShapeOptions` has no published list of default features yet. The list goes to you for approval
   before phase 3 (rule 5), because it decides behaviour.

## Changes and decisions for Arun's approval (rule 5)

| ID | What | Where |
|---|---|---|
| C1 | cpsp default on | wrapper ShapingFeatures.cs; already approved (D-68) |
| C2 | Shape(byte[]) caching per array, to meet D-73 | not implemented: the wrapper keeps the contract's per-call FontFace.Load until you decide |
| C5 | Wrapper fallback (FontFace cmap + advances) if our reader rejects a font FontFace accepted | wrapper R-PW-0004 |
| O1 | Symbol (3, 0) cmap: code points looked up unchanged, no 0xF000 remapping | R-OT-0016 |
| O2 | A cmap glyph index not below the glyph count reads as 0 | R-OT-0017 |
| O3 | Advance/lsb of a glyph index out of range is 0 | R-OT-0009 |
| O4 | CFF glyph box from exact curve extremes, rounded outwards | R-OT-0029 (to be checked by the oracle) |
| O5 | CFF deprecated accented-glyph endchar not composed | R-OT-0030 |
| O6 | 'kern' format 2 values not read (format 0 only) | R-OT-0015 |
| T1 | A surrogate pair cut by the run boundary reads as U+FFFD | R-TS-0012 |
| W1 | Undefined LipiScript value treated as Latin | R-PW-0003 |

Folder and approval items still open from v1: `tools/` (used for the generator, the pinned UCD (6.4 MB of
text), the gates and test support); WOFF/WOFF2 in Chuvadi.OpenType; helpers H1 to H8; G1/G2 timing.

## Waiting on (design note v1 section d)

- d.1 The six 3.26.0 nupkgs, with XML docs. These are needed for the oracle, for the first real build of
  the oracle and the wrapper, and for adding the bounds comparison.
- d.2 Generation settings. `tools/Chuvadi.Text.Shaping.Gate/corpus-features.txt` stays provisional until
  they arrive.
- d.3 How the official harness passes features. Until it does, from phase 3 onward the pack harness runs
  with cpsp on (the library default) while the expected outputs were made with cpsp off.
- d.4 to d.9 as listed in v1.

From the coordinator: the monorepo's Directory.Build.props, a sample csproj and the location convention
for CHANGELOGs. The new csproj files set their properties explicitly, and the test projects use xunit
2.9.3, xunit.runner.visualstudio 3.1.4 and Microsoft.NET.Test.Sdk 17.14.1; these may need to match the
monorepo's.
