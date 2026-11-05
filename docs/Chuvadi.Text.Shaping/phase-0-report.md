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

## Results on Arun's machine (Windows, 2026-11-05)

Pushed as commit 17fa3b0 on feat/shaping-phase0 (from main 5818752). Chuvadi.OpenType tests 36/36, Chuvadi.Text.Shaping
tests 15/15, whole Chuvadi.slnx 4,262/4,262. Local gate: 2,144 / 29,493 (7.27%), PHASE TARGET MET; byte-array parity
29,493/29,493; font check clean on all 13 fonts. Every per-script count is identical to the Linux run. Still to run on
Windows: the FontFace oracle and the pack's official harness (both need the Pdf font nupkgs).

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

## Decided (D-155, Arun, 2026-11-05)

WOFF/WOFF2 in Chuvadi.OpenType; helpers H1, H2, H3, H4, H6 now (H5 only on request, H7 no, H8 with Arabic); C2 byte[]
parse cache in the wrapper (documented); similarity timing G1 for engine phases 3-6, G2 elsewhere, hard stop before
hand-over; the tools/ folder; tags opentype-v* and text-shaping-v*; licences MIT (Chuvadi.OpenType) and MIT AND
Unicode-3.0 (Chuvadi.Text.Shaping).

Next, in order (follow-up delivery on feat/shaping-phase0 after Arun's push):
1. WOFF 1.0 and WOFF2 decoding in Chuvadi.OpenType (the WOFF2 decoder moves from tools/Chuvadi.Fonts.TestSupport into
   src/Chuvadi.OpenType/Woff/; WOFF 1.0 added from the W3C spec); FontCollection reports the container kind.
2. C2 in the wrapper: parsed font cached per byte array, documented.
3. Phase 1: GDEF, GSUB and GPOS views. Similarity check: G2 (send the phase zip and continue).

## Follow-up commit (2026-11-05): WOFF/WOFF2 and C2

- WOFF 1.0 and WOFF 2.0 (with collections) decoded by Chuvadi.OpenType on load (R-OT-0036 to R-OT-0039); the WOFF2
  decoder moved from tools/Chuvadi.Fonts.TestSupport into src/Chuvadi.OpenType/Woff/ (rules R-OT-0031 to 0034
  unchanged). The decoded tables of all 10 pack WOFF2 fonts are identical to the phase-0 decoder's output.
- Finding: the pack's LiPi Sans WOFF2 files put other tables between glyf and loca, which WOFF2 §4.1 forbids encoders
  to do; the decoder pairs glyf with the next transformed loca instead of rejecting (R-OT-0039).
- Finding for phase 3: the LiPi Sans fonts are variable fonts (fvar, gvar, avar, HVAR, STAT). Shaping uses the default
  instance; GSUB/GPOS FeatureVariations and variation deltas at the default coordinates will be handled from the
  spec in phase 1/3 and checked against the corpus.
- New limit for approval: WOFF/WOFF2 data declaring more than 256 MiB of font data is refused (internal constant,
  R-OT-0038). Option: expose it as a load option.
- C2 in the wrapper (R-PW-0005): Shape(byte[]) caches the loaded face per array (documented in the method remarks).
- Tests: Chuvadi.OpenType 41/41, Chuvadi.Text.Shaping 15/15; local gate 2,144 (phase target met), parity and font
  check clean; pack harness through the wrapper (sandbox stand-ins) 2,144.

## Oracle finding (2026-11-05): FontFace does not map code points above U+FFFF

First Windows run of tools/Chuvadi.OpenType.Oracle against the real Chuvadi.Pdf.Fonts.Rendering 3.26.0
(branch tip f9fee7b): every value agrees on all 13 pack fonts (UnitsPerEm, GlyphCount, IsCff, every
advance and left side bearing, the glyph for every code point up to U+FFFF) except 45 code points
above U+FFFF, where FontFace returns 0 and Chuvadi.OpenType returns a glyph. Result: ORACLE DIFFERS.

Evidence (cmap subtables, read with Chuvadi.OpenType):

| Font | Subtables | Mappings above U+FFFF |
|---|---|---|
| LiPi-Sans-Devanagari.woff2 | (0,3) f4, (0,4) f12, (3,1) f4, (3,10) f12 | 10: U+11B00..U+11B09 |
| LiPi-Sans-Latin.woff2 | same | 31: U+1F12F..U+1F149, U+1F16A, U+1F16B, U+1F850, U+1F852 |
| LiPi-Sans-Tamil.woff2 | same | 4: U+11301, U+11303, U+1133B, U+1133C |

The format 4 subtables cannot hold these code points; the format 12 subtables (3,10) and (0,4) map
them and agree with each other. The OpenType cmap chapter requires a (3,10) format 12 subtable for
fonts with supplementary-plane characters; Chuvadi.OpenType selects it (R-OT-0016). The other ten pack
fonts have no mappings above U+FFFF.

Decision (Arun, via coordinator): fix at the root in Chuvadi.Pdf (every cmap lookup, with tests); the
oracle stays strict with no known-difference rule, and ORACLE AGREES with zero exceptions remains a
D-158 merge condition, re-run against the fixed Pdf packages. Test vector with glyph ids and font
SHA-256: docs/Chuvadi.OpenType/oracle/fontface-cmap-above-bmp.tsv.

Re-run (2026-11-05) on f9fee7b against Chuvadi.Pdf 3.31.0 (cmap fix, D-169), oracle strict, no
known-difference rule: 0 differences on all 13 fonts, ORACLE AGREES. Built with
-p:ChuvadiVersion=3.31.0 -p:WarningsNotAsErrors=CS0618, because 3.31.0 marks Woff2Unpacker obsolete
(to be removed at the Pdf move); the lasting oracle change is for Arun to choose. Glyph boxes against
FontFace.GetGlyphBounds are still not compared (return type needed from the 3.31.0 package).

Rule 5 item (noted, Arun decides with the Pdf fix): when Pdf.Authoring moves to Chuvadi.OpenType
(plan C1) these characters render with their real glyphs instead of glyph 0.

## Decisions D-179 (2026-11-05)

- The oracle keeps its Woff2Unpacker cross-check, with CS0618 suppressed on that one call only (test tool, D-163).
- The pack harness and the wrapper move to Chuvadi.Pdf 3.31.0. The wrapper's default ChuvadiVersion is 3.31.0;
  the pack harness is unchanged and is run with -p:ChuvadiVersion=3.31.0 -p:WarningsNotAsErrors=CS0618, because
  its own WOFF2 step also calls the now-obsolete Woff2Unpacker.
- Variation selectors with no matching variant in the font stay as a zero-width glyph in the base's cluster (not
  dropped). Implemented with the cmap stage (phase 2 of the engine plan).
- The oracle compares glyph boxes with FontFace.GetGlyphBounds (FontBox, from the 3.31.0 public signatures and
  XML docs); the D-158 oracle condition counts as met only after that run.
- Result (Windows, 2026-11-05, on f9fee7b plus the oracle update): ORACLE AGREES, 0 differences on all 13 fonts,
  glyph boxes included (the CFF test font too). D-158 oracle condition met.
- Pack harness through the wrapper on the real Chuvadi.Pdf 3.31.0 packages (Windows, commit bdae60e): 2,144 of
  29,493 lines, the phase-0 result; GATE FAILED is expected until phase 4.

## Pdf answers d.2, d.4, d.6 and decision D-182 (2026-11-05)

Received and kept in docs/Chuvadi.Text.Shaping/inputs/. D-182 (Arun): d.3 is option (a), Pdf supplies the updated
harness, proven against 3.31.0; d.5 is generated from 3.31.0; the language is always set explicitly (the fonts' default
language system) in the harness and in our tests and tools. Done: the local gate and the wrapper now pass
Language.Default explicitly (no change in output: the library default was already 'dflt', and nothing reads the
machine locale); gate 2,144 and pack harness 2,144 unchanged.

Findings and items for Arun:
- Unicode version: the reference build uses Unicode 18.0; we use UCD 17.0.0. Compared here: 18.0 only adds characters
  (13,007) and changes Indic categories only for new characters (Odia U+0B53/U+0B54, U+11B0A, U+11DF0/U+11DF1) and
  Gurung Khema U+16100; no corpus code point changes in any property we use. Recommendation: move to 18.0.0.
- U+2044 fraction slash: the expected outputs use numerator/denominator forms around it with frac/numr/dnom off. The
  Unicode Standard (fraction slash, digits on either side display as a fraction) is the spec basis; handled in phase 1
  with the Latin words.
- d.4: the D-73 baseline needs restating (see the reply to the coordinator, 2026-11-05).
- d.6: the fixture fonts (3.9 MB) go into the tests in phase 5; storage for approval.

## Merge and D-188 (2026-11-05)

- Similarity checker: CLEAN on 17fa3b0, f9fee7b and bdae60e plus the wrapper (D-180); its two review items closed,
  the last by the WOFF sources statement in docs/Chuvadi.OpenType/trace/sources.md. Arun approved merging #57 with no
  opentype or text-shaping tags yet.
- D-188: the gate passes the D-68 feature set explicitly (cpsp off); the contract's ShapingFeatures.Default becomes cpsp
  on, matching 3.31.0 (the wrapper's ShapingFeatures already defaults cpsp on, C1/D-68). Pdf supplies the contract
  change and a small cpsp-on expected file.

## Merge conditions for the draft PR (D-158)

1. The Zip shared-files commit (slnx, README, RELEASING, ci.yml, release.yml) is added on this branch by Arun (D-161).
2. The WOFF follow-up commit has landed (f9fee7b); CI is green, including both new test suites.
3. The similarity checker reports phase 0 clean (or remediated), D-74.
4. The oracle reports ORACLE AGREES with zero exceptions against Chuvadi.Pdf 3.31.0, including the glyph-box comparison.
5. Arun approves.

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
