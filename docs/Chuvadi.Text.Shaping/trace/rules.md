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
