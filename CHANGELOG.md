# Changelog

All notable changes to Chuvadi will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file records release-by-release notes. Architectural decisions and
rationale live in `docs/CHANGE-LOG.md` (an append-only decision log,
numbered A01..ANN).

---

## [1.10.0] - 2026-05-20

### Added
- **Parser fuzz harness** (`tests/Chuvadi.Pdf.Fuzz/`) — hand-rolled mutation
  fuzzer with three targets: `pdf-open` (full document open), `content-stream`
  (content stream parsing), `truetype` (font loading). No NuGet dependencies.
  Mutations include splice, bit-flip, byte replace/insert/delete, boundary-value
  injection, range duplication, and random truncation. Crash inputs are saved
  to `crashes/<target>/<sha256>.bin` with full stack traces in matching `.txt`
  files for triage. See `tests/Chuvadi.Pdf.Fuzz/README.md`
- GitHub Actions workflow `.github/workflows/fuzz.yml` for scheduled fuzz runs
- `tests/Chuvadi.Pdf.Fuzz/FOLLOW-UPS.md` documenting findings deferred to
  PR 2.1 (truetype IndexOutOfRangeException bounds, PdfName.Intern
  ArgumentException tightening)

### Fixed
- `PdfPageCollection.FindPage` no longer recurses without bound on malformed
  page trees. Cyclic `/Kids` references and pathologically deep `/Pages`
  chains now throw `PdfDocumentException` with a clear message instead of
  killing the process with a `StackOverflowException`. Surfaced by the
  `pdf-open` fuzz target. Depth limit: 1024 (real PDFs use depth 1–5)
- `PdfObjectParser` no longer leaks `OverflowException` or `FormatException`
  from `int.Parse` on malformed integer tokens. All six parse sites now go
  through a guarded `ParseInt32` helper that throws `PdfReaderException` with
  the offending token's text snippet and byte offset. Surfaced by the
  `pdf-open` fuzz target after ~5.7M iterations

---

## [1.9.0] - 2026-05-19

### Added
- `benchmarks/Chuvadi.Benchmarks` project (BenchmarkDotNet harness)
  - `BrotliRatioBench`: output-size comparison vs `System.IO.Compression.BrotliStream`
    Optimal and Fastest across 6 representative scenarios (Lorem ipsum, English prose,
    repetitive, moderate, SFNT-like binary, random incompressible)
  - `BrotliThroughputBench`: encode-time comparison on the same scenarios
  - `ParserOpenBench`: `PdfDocument.Open` timing on synthetic single-page and 20-page PDFs
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests/BrotliLz77Tests.cs` — 11 regression tests covering
  the multi-command emission path

### Changed
- `BrotliCompressedEmitter` rewritten to consume the LZ77 command stream from
  `BrotliCommandStream.Encode()` and emit one Brotli command per LZ77 record. Per-block
  cap raised from 64 KiB to 16 MiB (MNIBBLES=6); inputs larger than that split across
  multiple meta-blocks
- `BrotliCompressedEmitter.TryEmit` (bool fallback) replaced by `Emit` (void, infallible
  for non-empty inputs)
- `BrotliEncoder` simplified — speculative compressed + stored, smaller wins
- Compression ratio on real data improved from 50-100% (single-command stage 3) to 2-6%
  (within ~1% of `BrotliStream` Optimal)

---

## [1.8.0] - 2026-05-19

### Fixed
- RFC 7932 §3.5 "modify rule" violation in `BrotliComplexPrefixCode.RunLengthEncode`:
  consecutive 17 (or 16) codes caused exponential count blowup per the spec's modify
  formula, producing invalid streams. Fixed by inserting a literal-length entry between
  consecutive 17s and 16s to break the run

### Changed
- `BrotliCompressedEmitter` now wires complex prefix codes for inputs with 5+ distinct
  literals (previously fell back to stored meta-blocks)
- `BrotliHuffman.BuildCanonicalCodes` uses explicit `int[] ordered` instead of `var`
  (IDE0008 conformance)
- `BrotliCodeTables.cs` switch-expression arms split one-per-line for `dotnet format`
  conformance

### Added
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests` — first test project for the WOFF2 module, with
  11 regression tests covering the modify-rule fix across 5..25 distinct literals,
  random data, and repeated text

---

## [1.7.0] - 2026-05-16

### Added
- **Phase 1.1.4** — Digital signature verification (`Chuvadi.Pdf.Signatures`)
  - PKCS#7 / CMS detached signature parsing (PDF 32000-1 §12.8.3.3)
  - Certificate chain extraction and signing-time recovery
  - Byte-range verification against the signed bytes of the document
  - Verification-only in this release; signing remains on the backlog

---

## [1.6.0] - 2026-05-15

### Added
- **Phase 1.1.6** — Linearization / Fast Web View (ISO 32000-1 Annex F)
  - Reader detects linearization and exposes the `/Linearized` parameter
    dictionary through `PdfDocument.IsLinearized` / `PdfDocument.Linearization`
  - Writer produces linearized PDFs with primary hint stream via
    `PdfWriter.WriteLinearized(...)`
  - `BitWriter` / `BitReader` and `PageHintTable` infrastructure for
    sub-byte hint encoding

### Notes
- Spec-conformant output. Real-world viewer compatibility (Acrobat,
  Foxit, browser PDF viewers) is not yet verified end-to-end and is
  tracked in the backlog

---

## [1.5.0] - 2026-05-15

### Added
- **Phase 1.1.3** — Form XObject and image redaction. `Redactor` now
  traces the `Do` operator with full CTM intersection so any Form XObject
  or image overlapping a redaction rect is dropped from the rewritten
  content stream
- **Phase 1.1.7** — Optional content (layers) reader. `OptionalContentReader`
  + `OptionalContentGroup` expose `/OCProperties`, `/OCGs`, default
  configuration name, and resolved visibility (`/ON`, `/OFF`, `BaseState`)
- **Phase 1.1.8** — CMYK render output. `PageRasterizer` and `TiffEncoder`
  support CMYK pixel buffers (TIFF photometric=5)

### Fixed
- `Redactor` was silently corrupting name operands (`Tf`, `cs`, `gs`, `Do`)
  in the rewritten content stream. Regression test added

---

## [1.4.0] - 2026-05-13

### Added
- **Encryption fully wired into the public API:**
  - `PdfDocument.Open(stream, password)` for opening encrypted documents
  - `PdfWriter.Write(..., EncryptionOptions)` for writing encrypted documents
  - `EncryptionOptions` factory methods for AES-128 and AES-256 with owner
    and user passwords
  - `EncryptionVisitor` traverses the object graph and encrypts strings and
    streams in place during write
  - `EncryptionDictionaryBuilder` implements PDF Algorithms 3/5 (R=4,
    standard security handler) and ISO 32000-2 Algorithms 8/9 (R=6,
    AES-256)
- 5 integration tests including byte-level plaintext-absence verification
  on round-tripped encrypted documents

### Changed
- AES-128 and AES-256 are now supported for both read AND write paths
  (v1.3.0 shipped read-only)

---

## [1.3.0] - 2026-05-12

### Added
- **Phase 1.1.2** — Pattern-based redaction (`Chuvadi.Pdf.Redaction`)
  - `PatternRule` and `PatternMatcher` for regex-based content matching
  - `CommonPatterns` library covering SSN, email, phone, ICD-10, NHS
    number, and other common PHI identifiers
- **Phase 1.1.9** — TIFF baseline 6.0 read and write
  (`Chuvadi.Pdf.Images.TiffDecoder` / `TiffEncoder`)
  - Uncompressed, PackBits, and LZW compression
  - Multi-page TIFFs via chained IFDs
- **Phase 1.1.5** — Standard security handler encryption (read path)
  (`Chuvadi.Pdf.Encryption`, `Chuvadi.Cryptography`)
  - RC4-40 and RC4-128 decryption
  - AES-128 decryption
  - Password-based key derivation per Algorithm 2

### Notes
- 604 tests across 19 test projects at tag time
- Write path for encryption arrived in v1.4.0

---

## [1.2.0] - 2026-05-12

### Added
- 8 runnable example projects under `examples/` (TextExtraction,
  Watermark, Redaction, Render, FormFill, Outlines, PageOps,
  Annotations)
- Getting Started guide (`docs/getting-started.md`)
- **Auto-generated Markdown API reference** (`docs/api/`, 117 pages)
  produced by `tools/gen_api_docs.py` parsing XML doc comments from
  every public type across all `src/` modules

### Changed
- CI adds a `docs-up-to-date` job that re-runs `gen_api_docs.py` and
  fails the PR if the resulting Markdown diverges from what's committed
- Style check (`tools/check_style.py`) expanded to scan `examples/`
  alongside `src/` and `tests/`

---

## [1.1.0] - 2026-05-11

### Added
- **Phase 1.1.1** — `Chuvadi.Pdf.Annotations` module (read and write
  per PDF 32000-1 §12.5)
  - `AnnotationReader` and `AnnotationWriter` covering Text, FreeText,
    Link, Stamp, Ink, Markup, and Generic annotation types
- GitHub Actions CI matrix: style check + build/test on Ubuntu,
  Windows, and macOS
- Style checker (`tools/check_style.py`) with line-by-line string
  stripping, `CONFLICT_OVERRIDES`, and `bin/` / `obj/` exclusion

### Fixed
- `Chuvadi.Pdf.Text.csproj` was missing several `ProjectReference`
  entries; added
- 10 `var`-in-`src` violations rewritten with explicit types to satisfy
  the IDE0008 rule that the new style checker now enforces

---

## [1.0.0] - 2026-05-11

### Added
- Initial public release. Closes Phase 2 (rendering, watermarking,
  redaction, forms, CLI). 17 modules, ~564 tests, 0 failures
- **Read pipeline:** PDF 1.4–2.0 ingestion (classic and stream xref,
  including hybrid); all standard non-encryption filters (Flate,
  ASCIIHex, ASCII85, RunLength, LZW); Type1 standard 14 fonts, TrueType,
  and CFF/Type1C inspection; full content stream tokenizer and parser
- **Text extraction:** operator-walking, layout-aware, and glyph-level
  fallback strategies (the glyph extractor handles non-Latin scripts via
  TrueType outline extraction)
- **Rendering:** zero-dependency scanline rasterizer producing PNG and
  BMP output; standard PDF fonts (Helvetica, Times, Courier) plus
  embedded TrueType; adaptive Bezier flattening; both fill rules;
  butt/square stroke caps
- **Document operations:** merge, split, delete pages, rotate, extract
  page ranges; text watermarks with rotation, opacity, and per-page
  targeting; image watermarks via PNG XObject embedding
- **PHI-safe redaction:** rectangle-based content-stream rewriting with
  byte-level removal (the redacted text is absent from the output PDF
  at both operator and indirect-object levels). Conservative TJ array
  drop. Tests grep the output bytes to verify removal
- **AcroForms:** read the field tree (fully-qualified names, types,
  current values, object IDs); fill values, set button `/AS`, mark
  `/NeedAppearances=true`
- **Outlines (bookmarks):** read the full tree with children and
  resolve destinations to page indices
- **CLI** (`chuvadi`): 11 user verbs (info, render, watermark, redact,
  form-fill, extract-text, outlines, merge, split, delete, rotate) plus
  6 debug verbs (tokenize, dump-objects, parse-content, decode-stream,
  inspect-xref, validate-fonts)
- Project scaffolding for all 17 Phase 1 modules, Apache 2.0 license,
  initial CI/CD pipelines

---

<!-- Template for future entries:

## [x.y.z] - YYYY-MM-DD

### Added
- New features

### Changed
- Changes to existing features

### Fixed
- Bug fixes

### Deprecated
- Features that will be removed in a future release

### Removed
- Features removed in this release

### Security
- Vulnerability fixes

-->
