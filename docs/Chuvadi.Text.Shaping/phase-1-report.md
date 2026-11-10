# Phase 1 report (in progress)

Branch `feat/shaping-phase1`, from main 5b03e81 (phase 0 merged, #57); merged with main 9b8110b for the D-295 delivery. Plan: design note v1, phase 1: read-only GDEF,
GSUB and GPOS views in Chuvadi.OpenType (FeatureVariations and variation data at the default instance), the layout
engine and the default script path in Chuvadi.Text.Shaping. Target: Latin words 852 of 852, no regressions.

## Decisions in force for this phase

- D-182: the language is always set explicitly (the fonts' default language system), in the harness and in our tests and
  tools; nothing reads the machine locale.
- D-188: the gate passes the D-68 feature set explicitly (cpsp off) for the 29,493 corpus lines; the contract's
  `ShapingFeatures.Default` has cpsp on (as 3.31.0), tested by gate 2 on `corpus/cpsp-on.tsv` (358 lines).
- D-190:
  1. Unicode 18.0.0, matching the expected outputs. Done: `tools/ucd/18.0.0/` pinned, `tools/ucd/17.0.0/` removed,
     tables regenerated (deterministic; 102,232 bytes of table data), every code point checked against the 18.0.0 files
     and the whole of NormalizationTest.txt 18.0.0 passes. No corpus code point changes in any property we use.
  2. The speed baseline is Chuvadi.Pdf 3.31.0, measured on Arun's machine with Pdf's ShapingTiming program.
  3. The malformed-font fixtures: the two base fonts plus the patch list are committed; the tests rebuild all twelve
     fixture files and the 180 M3 variants, check each against SHA-256 and check its outcome. Done in
     `MalformedFontTests`: all hashes match; M1 gives its exact expected glyphs; every fixture loads (M7 is refused, as
     expected), shapes without exception and within 1 s. The exact Tamil glyphs of M2 to M6 need the Tamil shaping of
     phase 3; that test is present and marked skipped until then.

- D-194 (P3): variable fonts are read at the default instance only. GSUB/GPOS FeatureVariations are evaluated at the
  default instance (all normalized axis coordinates 0) and variation deltas (Device/VariationIndex tables, item
  variation store) contribute nothing there. No instance-coordinates API now; one can be added later as a pure addition
  if Layout or Pdf ask.

## D-201: face members for a straight Pdf.Authoring swap (proposal, rule 5: new API only)

Already public (phase 0): `FaceProgram` (standalone sfnt; for a collection face rebuilt without DSIG, D-153), `Data`,
`IsCff` and `OutlineFormat`, `FaceIndex`, `FontCollection.Count`, `CharacterMap` (formats 0/2/4/6/10/12/13/14:
`GetGlyph(cp)`, `GetGlyph(cp, selector)`, `GetVariationKind`, `EnumerateMappings`), `GetAdvanceWidth`,
`GetLeftSideBearing`, `GetGlyphBounds`, `UnitsPerEm`, `GlyphCount`, and every raw field of head, hhea, OS/2
(including sCapHeight, sxHeight, sFamilyClass, panose, fsSelection), post and name.

Proposed addition, `OpenTypeFont.Metrics` (`FaceMetrics`, read-only, computed once): the derived values FontFace
exposes, under FontFace's names: Ascender, Descender, LineGap, UseTypoMetrics, HheaAscender/Descender/LineGap,
TypoAscender/Descender/LineGap, WinAscent/Descent, HasOs2Table, CapHeight, XHeight, ItalicAngle, FontBounds,
UnderlinePosition/Thickness, StrikeoutPosition/Size, WeightClass, IsBold, IsItalic, IsFixedPitch, FamilyName,
SubfamilyName, FullName, PostScriptName, plus HasGlyph. Each rule from the OpenType spec, traced; the oracle compares
every member with FontFace on all 13 fonts and the d.6 fixtures.

Not in FontFace's public API, so Authoring computes them today: stem estimate (StemV), Serif, Symbolic and the other
PDF font-descriptor flags. Proposal: add them only after Pdf states its current rules in words (no code), then check
them against Authoring's output; until then the raw inputs (OS/2 panose, sFamilyClass, fsSelection, weight, the cmap
(3,0) subtable) are all public.

## Plan of record and decisions D-205 (2026-11-06)

Design note v1 is the plan of record (P1), with this phase numbering (P2 b). Targets are cumulative and include the
old-style tag fixtures (oldtags/, D-201).

| Phase | Scope | Exit target |
|---|---|---|
| 1 | GDEF/GSUB/GPOS views (default instance, D-194), FaceMetrics (D-201, if approved), WOFF size option (P5); layout engine and default script path | Latin words 852/852; the Latin lines of gate 2 (cpsp-on); no regression on the other lines |
| 2 | Indic core and north: Devanagari, Bengali, Gujarati, Gurmukhi, Odia; Language.FromBcp47 (P12) | those syllables and words; oldtags Devanagari 3,428/3,428 |
| 3 | South: Telugu, Kannada, Tamil, Malayalam | all 19,641 syllables; oldtags Malayalam 3,315/3,315; d.6 Tamil exact glyphs |
| 4 | Words: ZWJ/ZWNJ, broken syllables and dotted circle, spaces, missing glyphs | 29,493/29,493, gate 2 358/358, oldtags 6,743/6,743, byte[] parity; speed against Pdf 3.31.0 on Arun's machine (D-190) |
| 5 | Helpers H1, H2, H3, H4, H6 (D-155) | LineBreakTest and GraphemeBreakTest 100% at UCD 18.0.0; H1 split-and-join invariant over the corpus |
| 6 | Hardening: fuzzing, the d.6 fixtures, concurrency, determinism, DLL size, Unicode table size (P11) | all gates 100% |
| 7 | Hand-over: Pdf swaps the wrapper in; Layout adopts both libraries | Pdf's own gate 100% |

Decisions:
- P4 (a): `ShapeOptions`' default features are the contract's list, cpsp on (D-188).
- P5 (b): the WOFF/WOFF2 256 MiB limit becomes a load option, 256 MiB by default, in the style of the other libraries'
  limits (an options record with a `Max...Bytes` init property). Rule 5 entry: new API only (load overloads taking the
  options; exceeding the limit still throws OpenTypeFormatException).
- P7: O1 to O6 approved as built:
  - O1: a Symbol (3,0) cmap is looked up with the code point unchanged, no 0xF000 remapping (R-OT-0016).
  - O2: a cmap glyph index not below the glyph count reads as 0 (R-OT-0017).
  - O3: the advance and left side bearing of a glyph index out of range are 0 (R-OT-0009).
  - O4: a CFF glyph box comes from the exact curve extremes, rounded outwards (R-OT-0029; the 3.31.0 oracle agrees).
  - O5: the deprecated CFF accented-glyph form of endchar is not composed (R-OT-0030).
  - O6: 'kern' format 2 values are not read, format 0 only (R-OT-0015).
- P8: a surrogate pair cut by the run boundary reads as U+FFFD (R-TS-0012), approved.
- P9: an undefined LipiScript value is treated as Latin (R-PW-0003), approved.
- P10: closed (3.31.0 maps the characters above U+FFFF too).
- P11 (a): keep the ~100 KB Unicode tables; revisit in phase 6.
- P12 (a): `Language.FromBcp47` in phase 2, from the OpenType language-tag registry as generated data.
- D-206: P13 (a), tag opentype-v0.1.0 once the layout views land, text-shaping only after the gate passes. P14 (1) and (2):
  fontTools and FreeType as black-box comparisons for the font reader; HarfBuzz 15.0.0 as a batch black box on inputs,
  outputs compared only, every fix justified by the spec (never "to match HarfBuzz") and recorded in the trace; (4) the
  ban on opening Chuvadi.Pdf.Text.Shaping stays. P15 (b): no separate reviewer; the similarity checker samples trace
  entries against the cited spec, more from the engine phases. Q2: the oracle gets a format 14 check (Pdf sends a font
  with variation sequences). Q3: Chuvadi.Pdf.Fonts, Graphics, Objects and Primitives 3.31.1 come here for local harness
  and oracle runs.
- Platform readiness (2026-11-06): pure managed C#, plain net10.0, no OS-specific APIs, bytes in (no paths), constant
  Unicode tables in the DLL, thread-safe faces and caches; trimming and NativeAOT checked (0 warnings; a NativeAOT test
  app shapes correctly). D-217: IsTrimmable and IsAotCompatible on in both csproj files. Web use is server-side only,
  so no own Brotli decoder is needed; no Stream overload for now.
- D-247: FaceMetrics option A, built in phase 1. StemV and the PDF descriptor flags stay in Pdf (Pdf will use
  StemV = 50 + (weight/65)^2 and set FixedPitch and Serif); the reader exposes the raw inputs: WeightClass, macStyle,
  italic angle, isFixedPitch, sFamilyClass (class and subclass bytes documented), panose, and whether a symbol cmap exists.
- D-246 (was D-195): typed diagnostics on the shaping result (ShapedRun and GlyphBuffer), MissingGlyph and ScriptNotInFont
  first, each kind once per call, no allocation when none; the wrapper adds FontReaderFallback.
- D-248: the Chuvadi.Pdf v3.31.2 release packages are fetched here and checked against SHA256SUMS (never
  Chuvadi.Pdf.Text.Shaping). First real runs here (2026-11-08): oracle against 3.31.2, ORACLE AGREES on all 13 fonts;
  pack harness r2 through the wrapper on 3.31.2, self-test passed, gate 1 2,144 of 29,493 (expected before the engine).
- Superseded: D-201's open StemV and flags question (D-247) and the D-195 warning channel (D-246).
- D-295 (2026-11-10): option (b), one combined delivery: the unrun commit 2 block (Unicode 18.0.0, d.6 fixtures) with
  the layout views, FaceMetrics, diagnostics, the trim/AOT flags and the new defaults, merged with today's main. The
  default Pdf version of the oracle and the wrapper moves to 3.32.1 (the latest release, checked against its
  SHA256SUMS.txt; Chuvadi.Pdf.Text.Shaping never touched). The oldtags and uvs fixtures are unpacked into the pack as part
  of the block.

## Inputs applied

- Harness revision 2 (d.3) and the D-188 contract change: applied to the pack copy used here. Pack harness through the
  wrapper (stand-ins, Chuvadi.Pdf 3.31.0 shape): gate 1 2,144 of 29,493; gate 2 `Default` tags match the contract,
  0 of 358 (expected before phase 1); self-test passed (22 checks).
- The wrapper's `ShapingFeatures.cs` is now the pack's D-188 file unchanged (only its comment differed).
- d.2 revision 2 and the d.5 behaviour corpus are kept unchanged in `inputs/`.

## Built for D-295 (2026-11-10)

- Chuvadi.OpenType `Layout` views: GSUB and GPOS (`LayoutTable`, script list, language systems, feature list,
  lookup list, every GSUB type 1–8 and GPOS type 1–9 subtable, Extension resolved), GDEF, Coverage, ClassDef,
  Device/VariationIndex; FeatureVariations at the default instance (R-OT-0040 to R-OT-0053).
- `FaceMetrics` (R-OT-0054 to R-OT-0056), `FontLoadOptions` (R-OT-0057, supersedes R-OT-0038),
  `CharacterMap.EnumerateVariationSequences` (R-OT-0058), `HasGlyph`, `HasSymbolSubtable`, `Os2Table.FamilyClassId`
  and `FamilySubclassId`.
- Chuvadi.Text.Shaping: diagnostics (R-TS-0014, R-TS-0015) and the script-tag lookup they use (R-TS-0013).
- Lazily read tables no longer allocate a delegate on every read (found by the no-allocation diagnostics test; no
  visible change).
- Tools: the oracle (default 3.32.1) now also compares every FaceMetrics member with FontFace, checks format 14 against
  `FontFace.GetVariantGlyphIndex` (every listed sequence and its neighbours, plus the 22 recorded 3.31.1 probes), and
  reads the oldtags and uvs fonts; the local gate has `--oldtags` and `--cpsp`; new `tools/Chuvadi.OpenType.LayoutDump`
  with `fonttools_dump.py` for the fontTools comparison.
- Wrapper: default Chuvadi.Pdf 3.32.1 and Chuvadi.Text.Shaping 0.1.0-phase1.1.

## Findings

- Only LiPi Sans Odia has FeatureVariations (GSUB 1.1, one record, wght range [−1, 0.685]). At the default instance
  the record matches, so 'rclt' (feature 16) uses lookup 183 instead of none. Our view gives exactly that; the engine
  must use `GetFeatureAtDefaultInstance` from phase 1 on.
- fontTools comparison (D-206, black box): identical dumps of GDEF, GSUB and GPOS for all 13 pack fonts, the two
  oldtags fonts and the uvs font (about 580,000 lines, 0 differences). The real fonts do not use GSUB 8, GPOS 5 or
  GPOS 7; those are covered by unit tests with built tables.
- Chuvadi.Pdf 3.32.1 against 3.31.2: FontFace's public signatures are identical and the oracle report is identical;
  nothing the oracle sees changed. With the new checks, ORACLE AGREES on all 16 fonts (13 pack, 2 oldtags, 1 uvs):
  every FaceMetrics member, 33 format 14 probes on the uvs font and the 22 recorded probes.

## Rule 5 list for this delivery

New API only (nothing existing changes):

| Item | Where |
|---|---|
| `Chuvadi.OpenType.Layout` namespace: `LayoutTable`, `LayoutTableKind`, `ScriptList`, `Script`, `LanguageSystem`, `FeatureList`, `Feature`, `LookupList`, `Lookup`, `LookupFlags`, `LookupSubtable`, the typed subtable views, `ValueRecord`, `ValueFormat`, `Anchor`, `Coverage`, `ClassDefinition`, `DeviceTable`, `UInt16List`, `GdefTable`, `GlyphClass` | Chuvadi.OpenType |
| `OpenTypeFont.Gsub`, `Gpos`, `Gdef`, `VariationAxisCount`, `Metrics`; `FaceMetrics` | Chuvadi.OpenType |
| `FontLoadOptions`; `OpenTypeFont.Load(data, faceIndex, options)`, `TryLoad(data, faceIndex, options, out font)`; `FontCollection.Load(data, options)`, `TryLoad(data, options, out result)` (approved P5 b) | Chuvadi.OpenType |
| `Os2Table.FamilyClassId`, `FamilySubclassId`; `CharacterMap.HasGlyph`, `HasSymbolSubtable`, `EnumerateVariationSequences` | Chuvadi.OpenType |
| `ShapingDiagnostic`, `ShapingDiagnosticKind`, `GlyphBuffer.Diagnostics`, `ShapedRun.Diagnostics` (approved D-246) | Chuvadi.Text.Shaping |

Behaviour, for Arun's approval:

- L1: a script is found in a font under its Indic "v.2" tag first, then its original tag (dev2 then deva, and so on);
  other scripts under their lower-case ISO 15924 code (R-TS-0013). Used now by ScriptNotInFont; the engine uses the
  same order from phase 1.
- L2: a WOFF or WOFF2 file declaring more decoded data than the largest .NET array (about 2 GiB) is refused with
  OpenTypeFormatException even when `MaxDecodedBytes` is set higher (R-OT-0057). Before, such a value could not be set.
- L3: a format 14 sequence listed in both the default and the non-default list appears twice in
  `EnumerateVariationSequences`; the lookups give the non-default glyph, as FontFace does (R-OT-0058, R-OT-0024).

Wrapper (goes to chuvadi-pdf): no surface change. The D-246 FontReaderFallback diagnostic is not built: exposing it
changes the 3.26 contract surface, so its shape comes to Arun first.

## Results (Linux, merge of main 9b8110b and this branch, 2026-11-10)

- Chuvadi.slnx builds with 0 warnings and 0 errors. Tests: Chuvadi.OpenType 66/66, Chuvadi.Text.Shaping 41 passed,
  1 skipped (exact Tamil glyphs, phase 3); every other suite passes (Markup 3,522, Print 337, Layout 285, Zip 101, and
  the rest). Chuvadi.Print.Xps.Tests is Windows only and is run on Arun's machine.
- Local gate with `--oldtags --cpsp --parity --fontcheck --expect-lines 2144`: 2,144 of 29,493, PHASE TARGET MET;
  oldtags 400 of 6,743; cpsp-on 0 of 358 (expected before the engine); parity 36,594 of 36,594; font check clean on
  all 15 fonts.
- Oracle against the real Chuvadi.Pdf 3.32.1 packages: ORACLE AGREES on all 16 fonts and the 22 probes.
- Pack harness r2 through the wrapper on 3.32.1 with the 0.1.0-phase1.1 packages: self-test passed (22 checks);
  gate 1 2,144 of 29,493; gate 2 `Default` tags match the contract, 0 of 358. GATE FAILED is expected until phase 4.
