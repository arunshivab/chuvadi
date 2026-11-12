# Phase 1 report

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

- D-316 (2026-11-10): L1, L2 and L3 approved (L3 with one line in the docs on the double listing). One PR for the font
  reader once L1–L3 and the fallback shape are in, with the `opentype-v0.1.0` tag at its merge; the engine later in its
  own PR, so main never holds a half engine.
- D-319 (2026-11-10): every recommendation of the D-316 follow-up (project doc `claude/D-316-fallback-and-engine-options.md`)
  approved. Wrapper: F1 new `ShapeRun` methods returning `ShapedRun`, `Shape` unchanged; F2 wrapper-owned
  `ShapingDiagnostic` types (FontReaderFallback = 3); F3 the fallback reports FontReaderFallback and MissingGlyph; F4 the
  message names the font and the reader's reason, captured once per face. Engine: E1 Latin and the non-complex scripts
  only in phase 1, Indic unchanged; E2 normalisation against the font with our own NFC/NFD tables; E3 combining
  character sequences as initial clusters; E4 no GDEF means no classes; E5 the registry's required and on-by-default
  features always on, switchable off with value 0; E6 LookupList order, one GSUB and one GPOS pass; E7 automatic
  fraction slash, switchable with frac = 0; E8 default ignorables as invisible zero-advance glyphs after GSUB; E9 a mark's
  advance zeroed only when a mark lookup positions it; E10 legacy 'kern' only without GPOS; E11 record buffer, views in
  place, per-lookup glyph bit sets, a bounded plan cache; E12 `ShapingLimits` with a `LimitReached` diagnostic.
  Order: 1) this font-reader PR, 2) `opentype-v0.1.0` at its merge, 3) the engine on `feat/shaping-engine` in its own
  PR at the phase-1 exit (Latin 852 of 852, cpsp-on 358 of 358, Indic unchanged), 4) the wrapper fallback to Pdf as a zip.

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

Behaviour (approved D-316):

- L1: a script is found in a font under its Indic "v.2" tag first, then its original tag (dev2 then deva, and so on);
  other scripts under their lower-case ISO 15924 code (R-TS-0013). Used now by ScriptNotInFont; the engine uses the
  same order from phase 1.
- L2: a WOFF or WOFF2 file declaring more decoded data than the largest .NET array (about 2 GiB) is refused with
  OpenTypeFormatException even when `MaxDecodedBytes` is set higher (R-OT-0057). Before, such a value could not be set.
- L3: a format 14 sequence listed in both the default and the non-default list appears twice in
  `EnumerateVariationSequences`; the lookups give the non-default glyph, as FontFace does (R-OT-0058, R-OT-0024).

Wrapper (goes to chuvadi-pdf): no surface change in this delivery. The FontReaderFallback diagnostic's shape was
approved in D-319 (F1–F4) and is delivered to the Pdf chat as its own zip.

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

## Results on Arun's machine (Windows, 2026-11-10)

Pushed as commit 4ea450f on feat/shaping-phase1, on top of the merge 49644cf of main f87ea0a; the pushed tree is
identical, file by file, to the tree built and tested on Linux.

- Chuvadi.slnx builds with 0 warnings and 0 errors. Full-solution `dotnet test`: 4,636 passed, 1 skipped (Tamil, phase
  3), 1 failed: Chuvadi.Print.Tests `RawTargetTests.Socket_target_does_not_wait_longer_than_the_drain_timeout` took
  4.25 s against its 3 s budget while every suite ran at once. Run alone, Chuvadi.Print.Tests passed 337 of 337. A
  Print timing flake under load, not touched here (Print chat's test); Chuvadi.Print.Xps.Tests passed.
- Local gate: PHASE TARGET MET, 2,144 of 29,493; oldtags 400 of 6,743; cpsp-on 0 of 358; parity 36,594 of 36,594;
  font check clean on 15 fonts. Every count identical to Linux.
- Oracle against the real Chuvadi.Pdf 3.32.1 packages (checked against SHA256SUMS.txt): ORACLE AGREES on 16 fonts,
  33 format 14 sequences and the 22 recorded probes.
- Pack harness r2 through the wrapper on 3.32.1 with the 0.1.0-phase1.1 packages: SELF-TEST PASSED (22 checks);
  gate 1 2,144 of 29,493; gate 2 Default tags match the contract, 0 of 358; GATE FAILED, expected until phase 4.

## The engine (feat/shaping-engine, phase-1 exit)

Branch `feat/shaping-engine`, from main 055d258 (after the font reader, #124, and `opentype-v0.1.0`). One PR at the
phase-1 exit (D-323), nothing on main in between: the engine commit, then one commit with D-361, D-363 and the
corrected mark centring.

Decisions:

- D-332 (2026-11-11): the wrapper fallback (F1 to F4) is not in Chuvadi.Pdf 3.33.0; it ships as its own Pdf release
  once Chuvadi.Text.Shaping and Chuvadi.OpenType are stable and hosted and the gate is green. When the wrapper is next
  updated it needs a `LimitReached` member (value 4).
- D-345 (2026-11-11): E3 option (b). ZWJ joins the cluster of the character before it; ZWNJ starts its own. It rests
  on the black-box statement filed in `inputs/d345-joiners-statement.md` (R-TS-0031).
- D-361 (2026-11-12): both D-354 scope points approved. (1) The mark fallback applies only when the font lacks the
  glyph and the font has no GPOS table. (2) Marks are always put into canonical order before placement; only the shaped
  glyph order changes, the stored text never.
- D-363 (2026-11-12): empty multiple-substitution sequence deletes the glyph, kept and documented as lenient handling
  of a broken font; an alternate value past the end of the set leaves the glyph unchanged (changed, tested); no
  MissingGlyph for a missing space replaced by the space glyph, kept.
- D-354 (2026-11-11): option (b) for both fallbacks, built from the Unicode Standard and the black-box statement in
  `inputs/d345-fallback-statement.md`: widths for missing space characters (R-TS-0036) and positions for missing
  combining marks (R-TS-0037). Measured, not tuned (rule 2).

What is built (trace R-TS-0016 to R-TS-0042): language system choice; feature plan with ranged settings and alternate
values; a cache of 16 plans per font; GSUB types 1 to 8 and GPOS 1 to 9 (extensions resolved by the views); lookup flags,
mark attachment classes and mark filtering sets; LookupList order, one GSUB and one GPOS pass; level-0 clusters;
normalisation against the font; automatic fractions; default ignorables; legacy 'kern'; attachment resolution;
`ShapingLimits` with `LimitReached`; the D-354 fallbacks; per-lookup glyph sets so a lookup is not tried at a glyph it
cannot start at (E11, R-TS-0042). The Indic scripts keep the phase 0 path (E1).

Speed here (Linux container, gate `--bench 200`, Latin words, microseconds per call): 7.74 with the font cached,
28.34 when the font is loaded on every call. The comparison with Chuvadi.Pdf 3.31.0 (D-73, D-190) is on Arun's machine.

### First measurement of the D-354 fallbacks

Latin 809 to 848 of 852 with nothing fitted to the corpus. In two places the statement's own wording set the scope:

1. The mark fallback runs only in a font with no GPOS table (the statement is headed "in a font without GPOS"). The
   first build used it wherever no lookup had positioned the mark, and 19 Carlito lines (Carlito has GPOS) broke: there
   the reference keeps the .notdef mark at its own advance with no offset.
2. Canonical order is applied even when a cluster is kept as given ("marks are first put into canonical order"); it
   fixed line 9652 and changed no other line.

Then still differing, all on ChuvadiTest-Cff.otf, the horizontal offset of a fallback mark on a narrow base: 9675 and
9743 (f + mark: expected x −410, ours −390) and 9751 and 9825 (i + acute: expected −370, ours −375). The first
statement had said the mark is centred on the base's ink box; a second black-box statement from a separate test chat
(`inputs/fallback-narrow-base-statement.md`, 2026-11-12) corrected that: the mark is centred on the base's advance, and
the horn (216) puts its right edge at the end of the advance on every base. R-TS-0037 now follows it.

### Phase-1 exit

Latin 852 of 852, cpsp-on 358 of 358, every Indic and oldtags count unchanged, parity 36,594 of 36,594; the gate's
phase target is 2,503 matching lines (syllables 290, words 2,213). Nothing was fitted to the corpus: every fallback
number comes from the Unicode Standard or one of the two statements.

Open, not in the corpus: the rounding of an odd half advance; the classes 222, 226, 232 and the left classes, which
follow the horn's reference by analogy; a mark after a ligature in a font without GPOS (the statement saw −385 on f_i,
probably the last component).

### Rule 5 list for the engine PR

Behaviour (each traced; items 1 to 8 approved in D-319 or D-345, listed again here for the record):

| # | Item | Trace |
|---|---|---|
| 1 | Latin and every non-Indic script are now shaped with GSUB and GPOS (before: character map and advances only); the Indic scripts are unchanged | R-TS-0035 |
| 2 | Features always on: rvrn, rlig, rclt, curs, dist, abvm, blwm; on by default ccmp, locl, calt, liga, cpsp, kern, mark, mkmk; each switched off by value 0 | R-TS-0017 |
| 3 | Ranged feature settings get their own mask bits, at most 63 in all; settings past that are ignored | R-TS-0017 |
| 4 | GSUB skips default ignorables except ZWJ and ZWNJ; GPOS skips all default ignorables | R-TS-0020 |
| 5 | Default ignorables become the space glyph with zero advance after GSUB | R-TS-0029 |
| 6 | A mark positioned by a mark-attachment lookup gets advance 0 (the spec leaves advances unchanged) | R-TS-0027 |
| 7 | Legacy 'kern' only when the font has no GPOS | R-TS-0033 |
| 8 | ZWJ joins the cluster before it; ZWNJ starts its own (D-345) | R-TS-0031 |
| 9 | Automatic fractions around U+2044, switched off by a global frac=0 | R-TS-0019 |
| 10 | A multiple substitution with an empty sequence (out of spec) deletes the glyph: lenient handling of a broken font, kept (D-363) | R-TS-0038 |
| 11 | An alternate value past the end of the set leaves the glyph unchanged, no substitution (D-363; before D-363 it picked the last alternate) | R-TS-0038 |
| 12 | Fallbacks (D-354) apply only when the font lacks the glyph: a missing space character takes the space glyph with the width for its kind, with no MissingGlyph for it (kept, D-363); a missing combining mark is positioned only when the font lacks the glyph and the font has no GPOS table (scope point 1 above, approved D-361) | R-TS-0036, R-TS-0037 |
| 13 | Canonical mark order is applied even when a cluster is kept as given; only the shaped glyph order changes, the stored text never (scope point 2 above, approved D-361) | R-TS-0032 |

New API: `ShapingLimits` (`MaxNestingDepth`, `MaxOperationsPerGlyph`, `MinOperations`, `Default`), `ShapeOptions.Limits`,
`ShapingDiagnosticKind.LimitReached` (value 4; 3 is kept free for the wrapper's FontReaderFallback). Error types:
`ShapingLimits` throws ArgumentOutOfRangeException for a value below 1; `ShapeOptions.Limits` throws
ArgumentNullException for null. Nothing existing changes.

### Tests

`EngineTests` (60 cases) on built fonts: every GSUB type (single, multiple, alternate, ligature, context formats 1 and 2,
chained context format 3, extension, reverse chaining), GPOS single, pair, cursive, mark-to-base, mark-to-ligature,
mark-to-mark and chained context; IgnoreMarks, mark attachment type and mark filtering set; ranged settings, later
settings replacing earlier ones and `UseDefaultFeatures = false`; fractions; normalisation and canonical order;
joiners and the D-345 clusters; the operation and nesting limits; the plan cache; legacy 'kern'; every D-354 case in
the statement. IgnoreBaseGlyphs and IgnoreLigatures have no test of their own yet.
