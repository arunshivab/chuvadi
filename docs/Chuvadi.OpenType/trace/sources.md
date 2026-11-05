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
- WOFF File Format 1.0, https://www.w3.org/TR/WOFF/ (downloaded, not used yet)

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
