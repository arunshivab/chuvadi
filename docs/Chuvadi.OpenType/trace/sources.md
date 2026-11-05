# Sources log — Chuvadi.OpenType and Chuvadi.Text.Shaping (clean room, D-74)

Every external document read for the engine work, by phase. Only public specifications and Unicode
data. No shaping-engine source code, no chuvadi-pdf shaping code or data, no notes from the 3.26
shaping work. The pack's expected outputs are used as test data only.

## Phase 0 (2026-11-01)

OpenType 1.9.1 (Microsoft Learn), https://learn.microsoft.com/en-us/typography/opentype/spec/ :
- otff (The OpenType Font File: data types, table directory, font collections)
- cmap, head, hhea, hmtx, maxp, name, os2, post, glyf, loca, kern, cff

Adobe technical notes (normative references of the OpenType 'CFF ' chapter), https://adobe-type-tools.github.io/font-tech-notes/pdfs/ :
- 5176.CFF.pdf — The Compact Font Format Specification (4 Dec 2003)
- 5177.Type2.pdf — The Type 2 Charstring Format (16 Mar 2000)

W3C:
- WOFF File Format 2.0, https://www.w3.org/TR/WOFF2/ (used for the test-support decoder)
- WOFF File Format 1.0, https://www.w3.org/TR/WOFF/ (used for Woff1Decoder from 2026-11-04)

Unicode:
- UCD 17.0.0, https://www.unicode.org/Public/17.0.0/ucd/ : UnicodeData.txt, IndicSyllabicCategory.txt,
  IndicPositionalCategory.txt, DerivedCoreProperties.txt, Scripts.txt, ScriptExtensions.txt (kept for H6, not used yet),
  PropertyValueAliases.txt, CompositionExclusions.txt, NormalizationTest.txt (committed in tools/ucd/17.0.0/)
- Unicode License v3, https://www.unicode.org/license.txt
- UAX #44 Unicode Character Database, https://www.unicode.org/reports/tr44/
- The Unicode Standard 17.0, Chapter 3 Conformance, https://www.unicode.org/versions/Unicode17.0.0/core-spec/chapter-3/
- Apple Mac OS Roman mapping (data), https://www.unicode.org/Public/MAPPINGS/VENDORS/APPLE/ROMAN.TXT

Tooling only (no content used in code): the .NET install script (builds.dotnet.microsoft.com) and the NuGet
package index (api.nuget.org, for the xUnit test packages).

Not opened, searched for or recalled: HarfBuzz or any other shaping engine's source or internals, the chuvadi-pdf
repository's src/Chuvadi.Pdf.Text.Shaping/ and tools/shaping-data/, the Chuvadi.Pdf.Text.Shaping and Chuvadi.Pdf
packages.

## 2026-11-05

Unicode:
- UCD 18.0.0, https://www.unicode.org/Public/18.0.0/ucd/ (same file set as 17.0.0; downloaded to compare with 17.0.0 after
  d.2 named Unicode 18.0 for the reference build; not committed or used in code yet).

Chuvadi.Pdf 3.31.0 packages (attached by Arun): Chuvadi.Pdf.Fonts.Rendering (sha256 a75a59b3...53d5) and
Chuvadi.Pdf.Fonts.Woff2 (sha256 2672e9f0...2154). Read: public type and member signatures (metadata) and the XML
docs only; no method bodies, no decompiling. Used for the oracle (FontFace.GetGlyphBounds returns FontBox).

Data from the Pdf chat (docs/Chuvadi.Text.Shaping/inputs/): d.2 generation settings, d.4 timings and the
ShapingTiming program (a benchmark that calls the public TextShaper API; it contains no shaping code), d.6
malformed-font fixtures with expected outcomes. Test data and test conditions only. The d.2 text mentions HarfBuzz
release notes; those were not opened.

## WOFF and WOFF2 decoding: sources (statement for the clean-room record, 2026-11-05)

Asked by the coordinator. Woff1Decoder.cs, Woff2Decoder.cs, WoffLimits.cs and the sfnt/collection writing in
SfntWriter.cs were written from these sources only:
- W3C WOFF File Format 1.0, https://www.w3.org/TR/WOFF/
- W3C WOFF File Format 2.0, https://www.w3.org/TR/WOFF2/
- OpenType 1.9.1 'otff' chapter (table directory, checksums, checkSumAdjustment, collections), and 'glyf', 'loca',
  'hmtx', 'head' for the transformed tables.
- .NET's own System.IO.Compression (ZLibStream for WOFF 1.0, BrotliDecoder for WOFF2), used through their public API.
  The zlib and Brotli specifications (RFC 1950, RFC 7932) were not needed and not read.

Not read, opened or consulted, even briefly: google/woff2, fontTools, Chuvadi.Pdf's code (including
Chuvadi.Pdf.Fonts.Woff2), or any other WOFF or WOFF2 implementation. The session's record of web requests shows the
two W3C pages as the only WOFF-related sources fetched. Chuvadi.Pdf.Fonts.Woff2 was used only as a black box, by
the oracle comparing its unpacked output with ours, through its public Woff2Unpacker.Unpack method. As with all
clean-room work, general background knowledge from training cannot be excluded with certainty; nothing was
deliberately recalled, and the similarity checker's review (clean, 2026-11-05) is the independent control.

Names: field and data-type names follow the specifications (numTables, totalSfntSize, flavor, origLength,
compLength, transformLength, metaOffset/metaLength, privOffset/privLength, numFonts, numGlyphs, indexFormat,
bboxBitmap, overlapSimpleBitmap as overlapBitmap, optionFlags, 255UInt16 as Read255UInt16, UIntBase128 as Base128).
Helper and method names (U8/U16/U32, Put16/Put32, AddBlock, DecodeCore, Inflate, ReconstructHmtx, Find, Skip) are
our own.
