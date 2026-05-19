# Changelog

All notable changes to Chuvadi will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

## [Unreleased]

### Added
- Initial solution structure
- Project scaffolding for all Phase 1 modules
- CI/CD pipelines (GitHub Actions)
- Apache 2.0 license

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
