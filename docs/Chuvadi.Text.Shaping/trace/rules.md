# Chuvadi.Text.Shaping — trace log (D-74)

Every behaviour rule in the code carries a `// TRACE R-TS-nnnn` tag. Each entry says what the rule is,
where it comes from, and which files carry the tag. The check in `tests/Chuvadi.Text.Shaping.Tests`
(TraceLogTests) fails when a tag has no entry or an entry names a file that does not carry its tag.
The log is append-only: a changed rule gets a new entry that says which entry it supersedes.
Experiments against the corpus are recorded in `../experiments/` (none yet: phase 0 has no shaping
rules inferred from the corpus).

Source types: **spec** (public specification or Unicode data file, read 2026-11-01, see sources.md),
**decision** (Chuvadi design decision; behaviour-visible ones are listed for Arun's approval),
**technique** (engineering with no shaping content).

Short names:
- UAX44 = Unicode Standard Annex #44, Unicode Character Database, https://www.unicode.org/reports/tr44/
- UAX15 = Unicode Standard Annex #15, Unicode Normalization Forms, https://www.unicode.org/reports/tr15/
- TUS = The Unicode Standard, Version 17.0, Chapter 3 "Conformance" (read for phase 0). The data is now UCD 18.0.0; the normalization rules cited are checked against NormalizationTest.txt 18.0.0
- UCD = the Unicode Character Database 18.0.0 files in tools/ucd/18.0.0/ (17.0.0 until 2026-11-05, D-190)

## R-TS-0001
- Rule: General_Category and Canonical_Combining_Class come from UnicodeData.txt fields 2 and 3; a "<…, First>" / "<…, Last>" pair gives one value to the whole range; code points not listed are Cn with class 0. The UCD version is read from the first line of Scripts.txt. Category order in the tables: Cn first, then the order of UAX44 Table 12.
- Source: spec. UAX44 §4.2.3 "Code Point Ranges" (First/Last pairs in UnicodeData.txt), §4.2.9 "Default Values" (unassigned = Cn, class 0), §5.7.1 "General Category Values" (Table 12); UnicodeData.txt field order per UAX44 §5.3 "Property Definitions".
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs, src/Chuvadi.Text.Shaping/Unicode/UnicodeData.cs

## R-TS-0002
- Rule: Canonical decomposition mappings are UnicodeData.txt field 5 when it has no "<tag>" (tagged mappings are compatibility mappings and are left out). Primary composites are the two-character canonical decompositions, excluding Full_Composition_Exclusion: CompositionExclusions.txt, singletons (one-character mappings) and non-starter decompositions (the character or its first mapped character has a non-zero combining class).
- Source: spec. UAX44 §5.7.3 "Character Decomposition Mapping", §5.7.5 "Decompositions and Normalization", and the Full_Composition_Exclusion derivation (DerivedNormalizationProps.txt header: exclusions + singletons + non-starter decompositions); TUS §3.11 D113 "Full composition exclusion", D114 "Primary composite"; CompositionExclusions.txt.
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs, src/Chuvadi.Text.Shaping/Unicode/UnicodeData.cs

## R-TS-0003
- Rule: Indic_Syllabic_Category and Indic_Positional_Category are read from their files (code point or range; value); unlisted code points get the defaults Other and NA.
- Source: spec. UAX44 §4.2.1 "Data Fields", §4.2.3 "Code Point Ranges", §4.2.9 "Default Values"; the @missing lines in IndicSyllabicCategory.txt and IndicPositionalCategory.txt.
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs

## R-TS-0004
- Rule: Default_Ignorable_Code_Point is read from DerivedCoreProperties.txt.
- Source: spec. UAX44 §5.3 "Property Definitions" (Default_Ignorable_Code_Point), §4.2.7 "Binary Property Values"; DerivedCoreProperties.txt.
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs

## R-TS-0005
- Rule: The Script property comes from Scripts.txt (long names), mapped to ISO 15924 codes through the "sc" lines of PropertyValueAliases.txt; unlisted code points are Zzzz (Unknown).
- Source: spec. UAX44 §5.8.2 "Property Value Aliases" (PropertyValueAliases.txt, sc entries), §4.2.9 "Default Values"; the @missing line of Scripts.txt (Unknown).
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs, src/Chuvadi.Text.Shaping/Script.cs

## R-TS-0006
- Rule: Per-code-point properties are stored in three-stage lookup tables (4096-code-point pages, 64-code-point blocks, byte values) with identical blocks and pages shared; the property tuple (general category, combining class, the two Indic categories, default-ignorable) is one record index per code point.
- Source: technique (compact table layout; no Unicode semantics).
- Code: tools/Chuvadi.Text.Shaping.UcdGen/Program.cs, src/Chuvadi.Text.Shaping/Unicode/UnicodeData.cs

## R-TS-0007
- Rule: Hangul syllables decompose and compose algorithmically (SBase U+AC00, LBase U+1100, VBase U+1161, TBase U+11A7, LCount 19, VCount 21, TCount 28).
- Source: spec. TUS §3.12 "Conjoining Jamo Behavior", §3.12.2 "Hangul Syllable Decomposition", §3.12.3 "Hangul Syllable Composition" (constants and arithmetic).
- Code: src/Chuvadi.Text.Shaping/Unicode/Normalization.cs

## R-TS-0008
- Rule: Canonical ordering: in each run of characters with non-zero combining class, sort by combining class, keeping characters of equal class in their original order.
- Source: spec. TUS §3.11 D108 "Reorderable pair", D109 "Canonical Ordering Algorithm".
- Code: src/Chuvadi.Text.Shaping/Unicode/Normalization.cs

## R-TS-0009
- Rule: Canonical composition: after full canonical decomposition and ordering, each character C is combined with the last starter S when C is not blocked from S (no character between them has class 0 or a class >= C's class; a starter directly after S is not blocked) and a primary composite of S and C exists; the composite replaces S and C is removed.
- Source: spec. TUS §3.11 D115 "Blocked", D116 "Non-blocked pair", D117 "Canonical Composition Algorithm"; TUS §3.12.3 "Hangul Syllable Composition". Verified with NormalizationTest.txt (all parts; every other code point unchanged).
- Code: src/Chuvadi.Text.Shaping/Unicode/Normalization.cs

## R-TS-0010
- Rule: One ShapingFont per OpenTypeFont, kept in a ConditionalWeakTable so it lives exactly as long as the font; per-font shaping data is built once and reused (CONTRACT.md "Repeated calls should be fast"; design note v1 T2).
- Source: decision (approved S6 / T2).
- Code: src/Chuvadi.Text.Shaping/ShapingFont.cs

## R-TS-0011
- Rule (phase 0): each code point maps through the font's character map to one glyph with its 'hmtx' advance and no offsets; its cluster is the UTF-16 index of the code point in the text passed in. Superseded in later phases by the full pipeline.
- Source: decision. Cluster unit: D-66 (UTF-16 indices) and CONTRACT.md `Cluster`; positions in exact font units: D-67.
- Code: src/Chuvadi.Text.Shaping/TextShaper.cs

## R-TS-0012
- Rule: The run is read as UTF-16. A high surrogate followed by a low surrogate inside the run is one code point; any other surrogate (including half of a pair cut by the run's boundary) is read as U+FFFD.
- Source: spec/decision. CONTRACT.md "An unpaired surrogate is treated as U+FFFD"; TUS §3.9 D91 (UTF-16 code unit sequences), TUS §3.9.6 "U+FFFD Substitution of Maximal Subparts". A pair cut by the run boundary being unpaired: decision.
- Code: src/Chuvadi.Text.Shaping/TextShaper.cs

## R-TS-0013
- Rule: A script is looked up in a font's script list by its OpenType tags, in order: Devanagari dev2 then deva, Bengali bng2/beng, Gurmukhi gur2/guru, Gujarati gjr2/gujr, Odia ory2/orya, Tamil tml2/taml, Telugu tel2/telu, Kannada knd2/knda, Malayalam mlm2/mlym; Latin latn; any other script its ISO 15924 code in lower case. Common, Inherited and Unknown have no tags of their own. The default script tag is DFLT. The tag arrays are built once, so a lookup does not allocate.
- Source: spec + decision. OpenType 1.9.1 "Script tags" registry (each Indic script has a "v.2" tag alongside its original tag; "DFLT" is the default script; the other scripts' tags are their ISO 15924 codes in lower case). Trying the v.2 tag first, then the original one (so fonts with only the original tags still work; the oldtags gate set checks this): decision, listed for Arun in the phase-1 report (rule 5 item L1).
- Code: src/Chuvadi.Text.Shaping/Layout/ScriptTags.cs

## R-TS-0014
- Rule: Shaping reports typed diagnostics on the result (GlyphBuffer.Diagnostics, ShapedRun.Diagnostics), each kind at most once per call, in the order first found; the list is empty, with nothing allocated, when there is nothing to report. MissingGlyph: a code point with no glyph in the font (glyph 0 used); the message names the first such code point and its UTF-16 index.
- Source: decision D-195 → D-246 (Arun): typed diagnostics, MissingGlyph and ScriptNotInFont first, once per call, no allocation when none.
- Code: src/Chuvadi.Text.Shaping/GlyphBuffer.cs, src/Chuvadi.Text.Shaping/TextShaper.cs

## R-TS-0015
- Rule: ScriptNotInFont is reported when the font has a GSUB or GPOS table and neither lists any of the requested script's tags (R-TS-0013). It is not reported for Common, Inherited or Unknown, nor for a font with no layout tables (nothing to choose between).
- Source: decision D-246; what "lists" means: OpenType 1.9.1 chapter2 "Script List Table and Script Record" (a font without the script's tag uses its DFLT script, or no script, for that text).
- Code: src/Chuvadi.Text.Shaping/TextShaper.cs

## R-TS-0016
- Rule: The language system is chosen per layout table: the first of the script's tags (R-TS-0013) the table lists, else DFLT; within that script, the requested language tag if listed, else the script's default language system. A table with neither the script nor DFLT contributes no features.
- Source: spec. OpenType 1.9.1 chapter2 "Script List Table and Script Record" (DFLT is used when no script table is present for the specific script), "Script Table and Language System Record" (the default language system is used "in the absence of language-specific information"; one or the other is used, "but not both").
- Code: src/Chuvadi.Text.Shaping/Engine/ShapePlan.cs

## R-TS-0017
- Rule: Features for the default path: always on rvrn, rlig, rclt, curs, dist, abvm, blwm; on by default (switched off by a setting with value 0) ccmp, locl, calt, liga, cpsp, kern, mark, mkmk. Settings apply in order after these; a later setting for the same tag and range replaces an earlier one. A setting with a range gets its own mask bit, at most 63 bits in all (bit 0 is the global bit); settings past that are ignored.
- Source: spec + decision. OpenType 1.9.1 featuretags registry ("UI suggestion" and "Application interface" notes: ccmp, locl, rlig, rclt, rvrn, curs, dist, abvm, blwm on by default or required; calt, liga, kern, mark, mkmk on by default) and the contract's default list (P4 a, D-188). Which ones can be switched off, and the 63-bit cap: decision E5 a (D-319), listed for Arun (rule 5).
- Code: src/Chuvadi.Text.Shaping/Engine/ShapePlan.cs

## R-TS-0018
- Rule: The lookups of every chosen feature, plus the language system's required feature, are gathered into one list in LookupList index order; a lookup referenced by several features applies to the union of their masks. A feature's lookups are read at the default instance (FeatureVariations, R-OT entries in Chuvadi.OpenType). An alternate value is carried per lookup.
- Source: spec. OpenType 1.9.1 chapter2 "Language System Table" (requiredFeatureIndex), "Feature List Table" (the client "arranges the indices numerically into their LookupList order"), "Lookup List Table" ("If the different features have been applied to different glyph sub-sequences … each lookup is applied only to the sub-sequence to which the feature … was applied").
- Code: src/Chuvadi.Text.Shaping/Engine/ShapePlan.cs

## R-TS-0019
- Rule: Automatic fractions: a run of decimal digits, U+2044 FRACTION SLASH, a run of decimal digits gets 'frac' on the whole span, 'numr' on the digits before the slash and 'dnom' on the digits after. Off when a global setting frac=0 is given.
- Source: spec + decision. OpenType 1.9.1 featuretags 'frac' ("the application … identifies … numerators and denominators"), 'numr', 'dnom'; UCD General_Category Nd (decimal digits). Automatic marking and the frac=0 switch: decision E7 a (D-319).
- Code: src/Chuvadi.Text.Shaping/Engine/ShapePlan.cs, src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0020
- Rule: Glyph skipping while matching: default ignorables are skipped in GPOS; in GSUB all default ignorables except ZWJ and ZWNJ are skipped. Then the lookup flags: IGNORE_BASE_GLYPHS, IGNORE_LIGATURES, IGNORE_MARKS by GDEF class; USE_MARK_FILTERING_SET skips marks not in the set; a non-zero mark attachment type skips marks of any other attachment class. The current glyph is never skipped by flags.
- Source: spec + decision. OpenType 1.9.1 chapter2 "Lookup Table" (LookupFlag bits, markFilteringSet; "Lookup flags affect pattern matching for other glyphs in the input sequence but not the current glyph"); GDEF "Glyph Class Definition Table", "Mark Attachment Class Definition Table", "Mark Glyph Sets Table". Ignorables: UAX44 Default_Ignorable_Code_Point; letting GSUB see ZWJ/ZWNJ: decision E8 a (D-319), listed for Arun (rule 5).
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0021
- Rule: Single substitution replaces the current glyph by its substitute and keeps its cluster and other properties; the GDEF class is re-read for the new glyph.
- Source: spec. OpenType 1.9.1 GSUB "Lookup Type 1: Single Substitution Subtable"; GDEF "Glyph Class Definition Table".
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0022
- Rule: Ligature substitution: the first ligature in the set whose components match (skipping per R-TS-0020) replaces them; skipped marks between components stay, in order, after the ligature and take its ligature ID and the number of the component they followed; the ligature's cluster is the smallest cluster of the span it covers.
- Source: spec + decision. OpenType 1.9.1 GSUB "Lookup Type 4: Ligature Substitution Subtable" (ligatures in preference order; component glyphs). Mark component numbering (for mark-to-ligature, GPOS "Lookup Type 5") and the cluster rule (contract clusters, D-66): decision.
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0023
- Rule: Reverse chaining single substitution (type 8, also inside an extension lookup) is applied from the end of the run to the start, one glyph at a time.
- Source: spec. OpenType 1.9.1 GSUB "Lookup Type 8: Reverse Chaining Contextual Single Substitution Subtable" ("processing of input glyph sequence goes from end to start").
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0024
- Rule: Single adjustment adds its value record (x placement, y placement, x advance) to the current glyph at the default instance.
- Source: spec. OpenType 1.9.1 GPOS "Lookup Type 1: Single Adjustment Positioning Subtable", "Value Record".
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0025
- Rule: Cursive attachment joins the current glyph's exit anchor to the next (filtered) glyph's entry anchor: the current glyph's advance becomes exit.X − entry.X (left to right) and the next glyph is attached with the y difference.
- Source: spec. OpenType 1.9.1 GPOS "Lookup Type 3: Cursive Attachment Positioning Subtable" (entry and exit anchors aligned).
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0026
- Rule: Mark attachment: mark-to-base attaches to the previous glyph that is neither a mark nor ignorable; mark-to-ligature to the previous ligature, on the component the mark follows; mark-to-mark to the previous filtered glyph when it is a mark of the same ligature ID and component. The mark anchor is aligned with the base's anchor for the mark's class.
- Source: spec. OpenType 1.9.1 GPOS "Lookup Type 4: Mark-to-Base", "Lookup Type 5: Mark-to-Ligature", "Lookup Type 6: Mark-to-Mark Attachment Positioning Subtable" ("the mark2 glyph … is the glyph preceding the mark1 glyph in glyph string order (skipping glyphs according to LookupFlags)"), "Mark Array Table".
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0027
- Rule: A mark positioned by mark attachment gets advance 0 (E9 b); after GPOS every attached glyph's offset is resolved against the glyph it is attached to: y adds the parent's y offset, x adds the parent's x offset and subtracts the advances from the parent up to the glyph before it.
- Source: spec + decision. OpenType 1.9.1 GPOS "Lookup Type 4" ("Placement of the base glyph and advances of both glyphs are not affected"); zeroing the advance anyway: decision E9 b (D-319), listed for Arun (rule 5). Offset resolution: technique (the contract's offsets are relative to the pen position).
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs, src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0028
- Rule: Sequence and chained sequence context, formats 1 to 3, in GSUB and GPOS: the first matching rule wins; backtrack is matched backwards and lookahead forwards, with the lookup's flags; the sequence lookup records are applied in order, each on the result of the one before, with later positions moved by the change in length.
- Source: spec. OpenType 1.9.1 chapter2 "Sequence Context Format 1/2/3", "Chained Sequence Context Format 1/2/3", "Sequence Lookup Record" ("processed in order, and each applies to the results of the actions indicated by the preceding record"; "Lookup flags affect matching in backtrack and lookahead sequences").
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0029
- Rule: After GSUB, every default ignorable still in the run becomes the font's space glyph (glyph 0 when the font has none) with advance 0 and no GDEF class; every other glyph takes its 'hmtx' advance.
- Source: spec + decision. UAX44 Default_Ignorable_Code_Point (not rendered); OpenType 1.9.1 hmtx. The space glyph and zero advance: decision E8 a (D-319), as observed in the black-box statement docs/Chuvadi.Text.Shaping/inputs/d345-joiners-statement.md.
- Code: src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0030
- Rule: When a limit stops shaping (R-TS-0040), the run is returned as shaped so far and LimitReached is reported once.
- Source: decision E12 (D-319).
- Code: src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0031
- Rule: Initial clusters (level 0): a combining mark (General_Category Mn, Mc, Me) or ZWJ joins the cluster of the character before it; ZWNJ, a joiner at the start of the run and every other character start their own cluster (the UTF-16 index of the character).
- Source: spec + decision. UAX44 General_Category; contract clusters (D-66). ZWJ joining the cluster before it and ZWNJ starting its own: decision E3 b (D-345), resting on the black-box statement docs/Chuvadi.Text.Shaping/inputs/d345-joiners-statement.md.
- Code: src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0032
- Rule: Normalisation against the font, one cluster at a time: a single character the font maps is kept; otherwise the cluster is fully decomposed, put into canonical order and recomposed only into characters the font maps. The result is used when the font maps all of it or it leaves fewer characters without glyphs than the text as given; otherwise the text as given is used, still put into canonical order (D-354).
- Source: spec + decision. TUS §3.11 (R-TS-0008, R-TS-0009; canonical equivalence of reordered marks). Composition limited to the font and the choice between the two forms: decision E2 b (D-319). Canonical order on the kept text: the black-box statement docs/Chuvadi.Text.Shaping/inputs/d345-fallback-statement.md ("marks are first put into canonical order"), D-354; approved D-361 (only the shaped glyph order changes, the stored text never).
- Code: src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0033
- Rule: Legacy 'kern' table, only when the font has no GPOS and 'kern' is on: horizontal format 0 subtables that are not cross-stream or minimum add their pair value to the first glyph's advance (an override subtable replaces the sum so far); default ignorables are skipped.
- Source: spec + decision. OpenType 1.9.1 kern ("kern Table Format 0", coverage bits horizontal, minimum, cross-stream, override). Only without GPOS: decision E10 (D-319).
- Code: src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0034
- Rule: Each shaping font keeps at most 16 shape plans, keyed by the options' script, language, default-features switch and feature settings; the least recently used is dropped. Access is locked.
- Source: decision E11 b (D-319).
- Code: src/Chuvadi.Text.Shaping/ShapingFont.cs

## R-TS-0035
- Rule: The nine Indic scripts (Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada, Malayalam) keep the phase-0 mapping (R-TS-0011) until their shaping model is built; every other script uses the default engine. Narrows R-TS-0011.
- Source: decision E1 (D-319).
- Code: src/Chuvadi.Text.Shaping/TextShaper.cs

## R-TS-0036
- Rule: A space character the font has no glyph for takes the font's space glyph with a width by kind, rounded to nearest: U+2000, U+2002 1/2 em; U+2001, U+2003, U+3000 1 em; U+2004 1/3; U+2005 1/4; U+2006 1/6; U+2009 1/5; U+200A 1/16; U+202F 1/8; U+205F 4/18; U+00A0 the space glyph's advance; U+2007 the advance of the digit zero, else the space's; U+2008 the advance of the full stop, else the space's. U+1680 is not replaced. No MissingGlyph is reported for a replaced space (a real glyph is drawn; D-363).
- Source: spec + statement. TUS chapter 6 §6.2 "General Punctuation", Table 6-2 "Unicode Space Characters" (en, em, three-, four-, six-per-em, thin, figure, punctuation, no-break, medium mathematical). Hair space 1/16 and narrow no-break space 1/8 (the standard gives no number) and the rounding: the black-box statement docs/Chuvadi.Text.Shaping/inputs/d345-fallback-statement.md, decision D-354 b. Applies only when the font lacks the glyph (rule 5).
- Code: src/Chuvadi.Text.Shaping/Engine/Fallback.cs

## R-TS-0037
- Rule: In a font with no GPOS table, a combining mark that maps to glyph 0 gets advance 0 and keeps its base's cluster. Horizontally it is centred on its base's advance (x = base start + advance/2 − the mark's ink centre, the half rounded toward zero); the right classes (216, 222, 226, 232) put the mark's ink right edge at the end of the base's advance, the left classes (200, 218, 224, 228) its ink left edge at the start, the double classes (233, 234) centre on the end of the base's advance. Vertically, by canonical combining class with a gap of 1/16 em rounded down: above classes (228, 230, 232, 234) one gap above the base's ink top or the previous above mark; attached above (214, 216) touching; below classes (218, 220, 222, 233) one gap below the base's ink bottom or the previous below mark; attached below (200, 202) touching; overlay and others y 0. A mark with no base in its cluster is moved back by its own advance.
- Source: spec + statement. TUS chapter 3 §3.11 (canonical combining classes), chapter 5 "Rendering Nonspacing Marks" (marks stack outward from the base by position). The distances, the per-class placements and the no-GPOS scope: the black-box statement docs/Chuvadi.Text.Shaping/inputs/d345-fallback-statement.md, decision D-354 b; centring on the advance rather than the ink box, and the horn's right edge at the end of the advance: the corrected statement docs/Chuvadi.Text.Shaping/inputs/fallback-narrow-base-statement.md, which replaces the first statement's "ink box". The classes 222, 226, 232 and the left classes follow the same reference by analogy (the statement covers 216 only); the rounding of an odd half advance is not settled by either statement. Applies only when the font lacks the glyph and the font has no GPOS table (rule 5, approved D-361).
- Code: src/Chuvadi.Text.Shaping/Engine/Fallback.cs, src/Chuvadi.Text.Shaping/Engine/DefaultShaper.cs

## R-TS-0038
- Rule: Multiple substitution replaces the current glyph by its sequence, every glyph keeping the cluster; an empty sequence deletes the glyph. Alternate substitution picks alternate number value − 1 (value 1 is the first); a value past the set leaves the glyph unchanged (no substitution).
- Source: spec + decision. OpenType 1.9.1 GSUB "Lookup Type 2: Multiple Substitution Subtable", "Lookup Type 3: Alternate Substitution Subtable"; featuretags 'aalt', 'salt' (the alternate chosen by the application). The spec does not say what a value past the set does; leaving the glyph: decision D-363. An empty sequence is out of spec (GSUB: "The use of multiple substitution for deletion of an input glyph is prohibited. The glyphCount value must always be greater than 0."); deleting the glyph when a font has one anyway, as lenient handling of a broken font: decision D-363.
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0039
- Rule: Pair adjustment applies the first value record to the current glyph and the second to the next filtered glyph; matching continues from the second glyph when it has no value record of its own, else after it.
- Source: spec + decision. OpenType 1.9.1 GPOS "Lookup Type 2: Pair Adjustment Positioning Subtable" (formats 1 and 2). Where matching continues: decision.
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0040
- Rule: Limits: lookups nested deeper than MaxNestingDepth (default 16) are not applied; the operations budget is the larger of MinOperations (16 384) and MaxOperationsPerGlyph (64) times the glyph count; when it runs out shaping stops. Each attempt to apply a lookup at a glyph costs one operation; a glyph the lookup cannot start at (R-TS-0042) costs nothing.
- Source: decision E12 (D-319). OpenType 1.9.1 chapter2 "Sequence Lookup Record" (nested lookups) shows the recursion being bounded.
- Code: src/Chuvadi.Text.Shaping/ShapingLimits.cs, src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0041
- Rule: One GSUB pass and then one GPOS pass, each applying its lookups in LookupList order, every lookup over the whole run before the next starts; each lookup applies only to glyphs whose mask carries one of its features' bits.
- Source: spec. OpenType 1.9.1 chapter2 "Feature List Table" ("processes the lookups … in the order the lookup definitions occur in the LookupList"), "Lookup List Table"; GSUB "Lookup List Table" ("apply the lookups in the order given"). One pass each: decision E6 (D-319).
- Code: src/Chuvadi.Text.Shaping/Engine/LookupApplier.cs

## R-TS-0042
- Rule: Per font and layout table, each lookup keeps a lazily filled glyph set (pages of 256 glyphs): whether any of its subtables covers the glyph with the coverage the current glyph is tested against (GSUB 1 to 4 and 8 their coverage, 5 and 6 the context coverage; GPOS 1 to 3 their coverage, 4 to 6 the mark coverage, 7 and 8 the context coverage; extensions resolved). The top-level pass skips a glyph the lookup cannot start at; nested lookups are not filtered. No result changes.
- Source: technique, decision E11 (D-319: per-lookup glyph bit sets). That the current glyph must be in the subtable's (first) coverage for the subtable to apply: OpenType 1.9.1 chapter2 "Coverage Table" and each lookup type's description.
- Code: src/Chuvadi.Text.Shaping/Engine/LookupDigest.cs, src/Chuvadi.Text.Shaping/ShapingFont.cs
