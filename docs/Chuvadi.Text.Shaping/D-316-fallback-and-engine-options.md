# D-316 follow-up: FontReaderFallback shape and phase 1 engine options

Date: 2026-11-10. Branch `feat/shaping-phase1` at 4ea450f. Decisions in force: D-316 (L1, L2, L3 approved; one PR for the
font reader once L1–L3 and the fallback shape are in; the engine in its own PR later). Nothing below is built yet.
Rule 3: every item lists the options with pros and cons; the recommendation is mine, the decision is Arun's.

---

## Part 1. FontReaderFallback diagnostic (wrapper, goes to chuvadi-pdf)

### What exists today

- The wrapper keeps the 3.26 contract surface exactly: `TextShaper.Shape(byte[] ttf, …)` and `Shape(FontFace face, …)`,
  both returning `IReadOnlyList<ShapedGlyph>`; `ShapedGlyph`, `ShapingFeatures`, `LipiScript`.
- C5 fallback (approved D-195/D-246): when Chuvadi.OpenType cannot read a font that FontFace accepted, the wrapper
  shapes with FontFace's cmap and advances (no GSUB, no GPOS). The decision is made once per face and cached.
- Today the fallback is silent. The core library's diagnostics (MissingGlyph, ScriptNotInFont) are also not passed on,
  because the contract's return type has nowhere to carry them.

### F1. How a caller receives diagnostics

| | Option | Pros | Cons |
|---|---|---|---|
| a | **New additive methods** `ShapeRun(byte[] …)` and `ShapeRun(FontFace …)`, same parameters, returning `ShapedRun : IReadOnlyList<ShapedGlyph>` with `Diagnostics`. `Shape` stays exactly as it is. | Explicit and discoverable; per call; thread safe; carries all three kinds; matches the core library's D-246 design; nothing existing changes. | Two entry points; Pdf.Authoring must switch to `ShapeRun` to see diagnostics. |
| b | `Shape` keeps its signature but returns a `ShapedRun` object (still typed `IReadOnlyList<ShapedGlyph>`) that has `Diagnostics`; callers cast. | No new methods; every existing call gets diagnostics. | Hidden (needs a cast); the returned object changes from an array to a class, so any caller that casts the result to `ShapedGlyph[]` breaks (a rule 5 behaviour change). |
| c | Per-face query `TextShaper.GetFontStatus(FontFace)` returning whether the engine or the fallback shapes this face, with the reader's reason. | Simplest; no per-call cost; the fallback is per face anyway, so Pdf can log it once per font. | Carries only the fallback, not MissingGlyph or ScriptNotInFont; easy to forget to ask. |
| d | A static event or callback sink. | No signature change. | Global mutable state; ordering and threading problems; hard to test. Not recommended. |

Recommendation: **(a)**. Optionally (c) as well, if Pdf wants a once-per-font log line without shaping.

### F2. The diagnostic types in the wrapper

| | Option | Pros | Cons |
|---|---|---|---|
| a | **The wrapper's own** `ShapingDiagnostic(Kind, Message)` and `ShapingDiagnosticKind { MissingGlyph = 1, ScriptNotInFont = 2, FontReaderFallback = 3 }` in `Chuvadi.Pdf.Text.Shaping`; the core's two kinds are copied across one to one, with the same numbers and messages. | Pdf's public API exposes no Chuvadi.Text.Shaping type, so the engine can change without changing Pdf's surface. | A small mapping step (no allocation when the core reports nothing). |
| b | Reuse the core types and add `FontReaderFallback` to the core enum. | No mapping. | Puts a Chuvadi.Text.Shaping type in Pdf's public surface; the core would carry a kind it never reports. |

Recommendation: **(a)**.

### F3. What is reported when the fallback shapes

| | Option | Pros | Cons |
|---|---|---|---|
| a | **FontReaderFallback, plus MissingGlyph** (from FontFace's cmap: a code point that maps to glyph 0). | The caller still learns about missing glyphs, which matter more for a document than the fallback itself; cheap (the fallback already looks up every glyph). | — |
| b | FontReaderFallback only. | Simplest. | Missing glyphs go unreported for exactly the fonts most likely to be damaged. |

ScriptNotInFont is not reported in fallback mode under either option, because the fallback reads no layout tables.
Recommendation: **(a)**. Each kind still at most once per call, in the order found, as in D-246.

### F4. The FontReaderFallback message

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Name and reason**: "Chuvadi.OpenType could not read the font '<PostScript name, else family name>': <the reader's error message>. Glyphs and advances come from FontFace; GSUB and GPOS were not applied." | Tells the user which font and why, so the damage can be found and fixed. | The wrapper must call `OpenTypeFont.Load` and catch `OpenTypeFormatException` (once per face, at first use) instead of `TryLoad`, to keep the message. |
| b | Name only. | Uses `TryLoad` as today. | The cause is lost. |

Recommendation: **(a)**. The reason is captured once per face and reused, so later calls allocate nothing new.

### F5. Contract note

All of F1(a) to F4 is additive: `Shape`, `ShapedGlyph`, its values, its exceptions and its defaults stay exactly as the
3.26 contract defines them. The new members (`ShapeRun`, `ShapedRun`, `ShapingDiagnostic`, `ShapingDiagnosticKind`, and
`GetFontStatus` if chosen) are listed here for rule 5 approval. The wrapper goes to the Pdf chat as a zip, as before.

---

## Part 2. Phase 1 engine: design options

### Scope (plan P2(b), D-205)

Phase 1 is the layout views (done) plus the engine for the default, non-complex path: Latin first. Phase-1 exit:
**Latin words 852 of 852** and **cpsp-on 358 of 358**. The four Latin fonts are Carlito, Liberation Serif,
LiPi Sans Latin and the CFF test font; 213 lines each. Indic is phase 2 (north) and phase 3 (south).

Sources: OpenType 1.9.1 chapter2, GSUB, GPOS, GDEF, the feature registry and the script tag registry; the Unicode
Standard chapter 3 (conformance, canonical equivalence), chapter 5.21 (default ignorables) and chapter 6.2 (fraction
slash); UCD 18.0.0. The corpus is used only to check results, never as a source (rule 2).

### The pipeline (from the specifications; listed so Arun can see the whole path)

1. UTF-16 to code points (R-TS-0012 for a cut surrogate pair).
2. Normalisation against the font (E2).
3. Initial clusters (E3).
4. cmap, variation selectors kept as zero-width glyphs in the base's cluster (D-179).
5. Glyph properties from GDEF (E4).
6. Plan: script and language system (L1 order, then DFLT), features with their values and ranges (E5, E7).
7. GSUB, then GPOS, lookups applied per the lookup order (E6); legacy 'kern' (E10); mark advances (E9).
8. Output in font units (D-67) with clusters as UTF-16 indices (D-66); diagnostics (D-246).

### E1. Where the engine applies in phase 1

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Latin and every other non-complex script use the new engine; the ten Indic scripts stay on the phase-0 path until their phase.** | Indic gate counts stay exactly where they are, so every phase has an exact, explainable target; no half-shaped Indic output in between. | Indic text gets no GSUB/GPOS until phase 2 (as today). |
| b | The engine runs for every script from phase 1. | Indic text gets ligatures and kerning earlier. | Without Indic reordering, Indic results change unpredictably (some lines that match today would stop matching); the exact phase target is lost. |

Recommendation: **(a)**.

### E2. Normalisation against the font

| | Option | Pros | Cons |
|---|---|---|---|
| a | None: map code points exactly as given. | Simplest; fastest. | A base plus combining marks the font has precomposed (e + U+0301 → é) renders as two glyphs; text that is canonically equivalent renders differently. |
| b | **Font-driven, using our own NFC/NFD tables**: within each combining sequence, reorder marks by canonical combining class; compose to a precomposed character when the font maps it; decompose a precomposed character the font lacks when the font maps all of its parts. | Canonically equivalent text renders the same (Unicode conformance C6 allows exactly this); uses the tables built in phase 0; no new data. | More code; a pass over every combining sequence (cheap: plain ASCII has none). |
| c | Always NFC. | Simple; standard. | Ignores what the font has: NFC can produce characters the font lacks while the decomposed form would render. |

Recommendation: **(b)**. The corpus has 153 Latin lines with combining marks, for example `0065 0301` expected as one
glyph, so (a) cannot reach 852 of 852.

### E3. Initial clusters (before substitution merges any)

| | Option | Pros | Cons |
|---|---|---|---|
| a | One cluster per code point; only ligature and multiple substitutions merge. | Simplest. | A base and its combining marks get separate clusters, so a caret or a selection can split a letter from its accent. |
| b | Extended grapheme clusters (UAX #29). | The Unicode definition of a user-perceived character. | Needs GraphemeBreakProperty and emoji-data pinned and generated (two more generated tables); its Indic conjunct rule (GB9c) merges consonant clusters, which phases 2 and 3 would then have to split again. |
| c | **Combining character sequences** (the Unicode Standard, chapter 3, D56): a base plus the following characters of general category Mn, Mc or Me, plus ZWJ and ZWNJ. | Keeps a letter and its marks together; uses only General_Category, which we already have; no Indic conflict. | Not the full grapheme definition (emoji ZWJ sequences, Hangul jamo are not grouped); fine for Latin and Indic, revisit if emoji or Hangul ever come into scope. |

In every option, when a substitution merges glyphs of different clusters, the merged cluster is the smallest of them,
and clusters stay monotone in visual order (D-66). Recommendation: **(c)**.

### E4. Glyph classes when a font has no GDEF

The CFF test font has no GDEF (it has one GSUB lookup and no GPOS). The other twelve pack fonts have GDEF glyph classes.

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Strict**: no GDEF means no glyph classes, so lookup flags that skip bases, ligatures or marks skip nothing. | Exactly what the font says; nothing invented. | A font without GDEF but with mark-skipping lookups behaves as if the flags were absent. |
| b | Synthesise classes: a glyph mapped from a character of category Mn or Me is a mark; a ligature substitution's output is a ligature; all else is a base. | Lookup flags work on fonts that omit GDEF. | An invented rule the spec does not give; a font designer's intent can be wrong-guessed; more code. |

Recommendation: **(a)**.

### E5. Features that are always applied

P4(a) made the contract's list the default set: ccmp, locl, calt, liga, cpsp, kern, mark, mkmk. The contract also
says "features the script requires are always applied". The d.2 record shows the expected outputs applied the
features the OpenType feature registry marks as required or on by default, without being requested.

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Also apply, always, the registry's required and on-by-default features for the script**: for the default path rvrn (through FeatureVariations, first), rlig, rclt, curs, dist, abvm, blwm; for Indic the script's own required features in phases 2 and 3. A setting with value 0 still turns one off. | What the contract text says; what the feature registry says about these tags; matches how the expected outputs were made. | The always-on list must be written down and approved (this table is that list). |
| b | Same as (a), but required features cannot be turned off. | Protects text that needs them (for example rlig). | Less control for a caller; a library should expose the full option set. |
| c | Only the contract's eight tags. | Smallest. | Contradicts the contract's own sentence; fonts that put needed forms in rlig or rclt render wrongly. |

Optional features a font has but the registry marks off by default (Carlito's c2sc, lnum, onum) are not applied unless
requested, under every option. Recommendation: **(a)**.

### E6. The order lookups are applied in

| | Option | Pros | Cons |
|---|---|---|---|
| a | **One GSUB pass and one GPOS pass**: every lookup of every enabled feature, in LookupList order, each lookup over the whole run (type 8 from the end), with the feature's mask deciding which glyphs it may touch. | The OpenType rule that lookups are processed in LookupList order (chapter2, Lookup List table); the font designer controls the order; simplest. | — |
| b | Stages (groups of features applied one group after another), as complex scripts need. | Required for Indic (phases 2 and 3). | Not specified for the default path; inventing stages there would override the font's lookup order. |

Recommendation: **(a)** for the default path. Stages only where a script's own specification defines them
(Indic, phases 2 and 3), listed for approval then.

### E7. The fraction slash (U+2044)

The Unicode Standard (chapter 6.2) describes U+2044 FRACTION SLASH as forming a fraction with the digits around it. The
d.2 record shows the expected outputs did this without frac, numr or dnom being requested; 4 Latin corpus lines need it.

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Automatic**: for a run of decimal digits, U+2044, a run of decimal digits, apply numr to the first run, dnom to the second and frac to the whole, through the feature ranges; a 'frac' setting with value 0 turns it off. | What the Unicode Standard describes; reaches 852 of 852; the caller can still switch it off. | A rule the font does not state, so it is listed here for approval. |
| b | Never; fractions only when the caller asks for frac. | Nothing implicit. | Cannot reach 852 of 852 (4 lines). |

Recommendation: **(a)**.

### E8. Default ignorable code points (ZWJ, ZWNJ, soft hyphen, word joiner, CGJ and the rest)

The Unicode Standard (chapter 5.21) says default ignorables are not rendered visibly. 8 Latin corpus lines have them.

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Keep them in the buffer through GSUB (so lookups that use ZWJ or ZWNJ still see them), then show each as an invisible glyph with zero advance**: the font's space glyph if it has one, else glyph 0; in the character's own cluster. Not reported as MissingGlyph. | Invisible as the standard says; contextual lookups keep working; clusters stay intact. | Which glyph stands in for an invisible character is a choice (this one). |
| b | Remove them from the output entirely. | No stand-in glyph. | Clusters gain gaps; callers mapping glyphs back to text lose the characters. |
| c | Treat them like any other character. | Simplest. | A font with a visible glyph for, say, a soft hyphen shows it. |

Recommendation: **(a)**.

### E9. Advances of mark glyphs

| | Option | Pros | Cons |
|---|---|---|---|
| a | Trust the font's hmtx advance. | Nothing invented. | A mark with a non-zero advance pushes the next base along even when GPOS attached it. |
| b | **Zero the advance of a glyph GDEF classes as a mark when a mark-attachment lookup positions it.** | A positioned mark never moves the pen. | Changes the font's value in that one case; listed for approval. |
| c | Zero every GDEF mark's advance, positioned or not. | Simplest rule. | An unpositioned mark sits on top of the previous glyph. |

Recommendation: **(b)**. Mostly a phase 2 and 3 matter (Latin fonts usually give marks a zero advance already), but the
rule belongs to the engine core, so it is decided now.

### E10. The legacy 'kern' table

| | Option | Pros | Cons |
|---|---|---|---|
| a | **Apply 'kern' (format 0, already read) only when the font has no GPOS table**, under the 'kern' feature setting. | A font with GPOS gets GPOS only (the spec says GPOS replaces 'kern'); an old font still kerns. | Two code paths. |
| b | Apply it when GPOS has no 'kern' feature for the script. | Covers fonts whose GPOS lacks kerning. | Can double-kern a font that kerns in another GPOS feature. |
| c | Never. | One path. | Old fonts lose kerning. |

Recommendation: **(a)**.

### E11. Data structures and speed (rule 4, D-73)

| Item | Options | Recommendation |
|---|---|---|
| Working buffer | (a) one internal array of glyph records (glyph, cluster, mask, glyph class, ligature and component ids, code point), copied into GlyphBuffer's public arrays at the end; (b) more parallel arrays in GlyphBuffer itself. (a): an insertion or deletion moves one array, and each record is one cache line; (b) avoids the final copy but every insertion touches eight or more arrays. | **(a)** |
| Lookup data | (a) interpret the layout views in place (they read the font's bytes, allocate nothing); (b) compile every lookup into in-memory maps at first use. (a): no start-up cost, small memory; (b): faster lookups, several MB per Indic font and a slow first call. | **(a)**, plus the next row |
| Fast rejection | Per lookup, a bit set of the glyphs its coverage tables list, built once per font (glyph count / 8 bytes per lookup: about 140 bytes × 224 lookups ≈ 31 KB for LiPi Devanagari), so a lookup whose glyphs are not in the run is skipped in one test. Option: build it (a) when the font is first shaped, or (b) never. | **(a)** |
| Plans | An immutable plan (chosen script and language system, the lookups with their masks) per (script, language, feature settings), cached in the ShapingFont. Options: (a) unbounded; (b) a small bounded cache, 16 plans per font, least recently used dropped. | **(b)** |
| Speed check | The D-73 gate against Pdf 3.31.0 on Arun's machine (ShapingTiming), run at phase-1 exit. | — |

### E12. Limits against hostile fonts

A contextual lookup can call other lookups; a hostile font can make that loop or explode.

| | Option | Pros | Cons |
|---|---|---|---|
| a | Fixed internal limits: nesting depth 16, and an operation budget per call (for example 64 × the glyph count, at least 16,384); when hit, the lookup stops and the run is returned as shaped so far. | Can never hang; simple. | The numbers are fixed. |
| b | **The same limits, exposed** as `ShapingLimits` on ShapeOptions with these defaults, plus a diagnostic kind `LimitReached` when one is hit. | Thinks like a library: the caller can see and change them; the caller learns the run was cut short. | More API (listed for approval). |

Recommendation: **(b)**, built now because the engine core needs the limits from its first line.

### E13. Tests and the phase-1 exit

- Unit tests with built fonts (the LayoutBuilder from this phase) for every lookup type, each lookup flag, mark
  filtering sets, ranges and feature values, nested contextual lookups and the limits.
- Gate: Latin 852 of 852 and cpsp-on 358 of 358; Indic and oldtags counts exactly unchanged (E1 a); parity on every
  line; the oracle unchanged.
- D-73 speed check on Arun's machine; the similarity check (D-206 P15 b) on the engine code before the PR.

### E14. Delivery (D-316)

The font-reader PR goes first, from `feat/shaping-phase1`: the L3 doc line, the D-316 record and the Windows results,
then the PR and the `opentype-v0.1.0` tag at merge (D-162 heading and version). The engine is built on a new branch
`feat/shaping-engine` from main after that merge, and goes in its own PR once it meets the phase-1 exit, so main never
holds a half engine. The wrapper with the fallback diagnostic goes to the Pdf chat as a zip.

---

## Summary of the decisions asked for

| Item | Recommendation |
|---|---|
| F1 receiving diagnostics | (a) new `ShapeRun` methods returning `ShapedRun` |
| F2 types | (a) the wrapper's own, FontReaderFallback = 3 |
| F3 reported in fallback mode | (a) FontReaderFallback plus MissingGlyph |
| F4 message | (a) font name and the reader's reason |
| E1 where the engine runs | (a) non-complex scripts now, Indic in their phases |
| E2 normalisation | (b) font-driven compose/decompose with canonical reordering |
| E3 initial clusters | (c) combining character sequences |
| E4 no GDEF | (a) strict |
| E5 always-applied features | (a) registry required/on-by-default, switchable off with value 0 |
| E6 lookup order | (a) LookupList order, one GSUB and one GPOS pass |
| E7 fraction slash | (a) automatic, switchable off with frac = 0 |
| E8 default ignorables | (a) invisible zero-advance glyph after GSUB |
| E9 mark advances | (b) zero only when a mark lookup positions the mark |
| E10 legacy 'kern' | (a) only without GPOS |
| E11 data structures | record buffer, views in place, per-lookup bit sets, bounded plan cache |
| E12 hostile-font limits | (b) exposed `ShapingLimits` with a LimitReached diagnostic |
