# SESSION-STATE.md — Current Build State

> Read this first each session, then CHANGELOG.md (root), then BASELINE.md.
> Rules and pitfalls live in CLAUDE.md — not here.
> Architectural decisions and rationale live in docs/CHANGE-LOG.md
> (append-only, numbered A01..ANN).

---

## Last Updated

2026-05-20 — v1.10.0 shipped (parser fuzz harness + two real bug fixes
the harness surfaced on its first run).

---

## Build Summary

**Last known passing total: 752 tests across 22 src modules, 0 failures.**

### Phase 1 — Core PDF library (v1.0.0)

| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Primitives          | Complete |
| Chuvadi.Pdf.Filters             | Complete |
| Chuvadi.Pdf.Objects             | Complete |
| Chuvadi.Pdf.IO                  | Complete |
| Chuvadi.Pdf.Documents           | Complete |
| Chuvadi.Pdf.Fonts               | Complete |
| Chuvadi.Pdf.Content             | Complete |
| Chuvadi.Pdf.Text                | Complete |
| Chuvadi.Pdf.Operations          | Complete |

### Phase 2 — Rendering, editing, CLI (v1.0.0)

| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Graphics            | Complete |
| Chuvadi.Pdf.Images              | Complete |
| Chuvadi.Pdf.Fonts.Rendering     | Complete |
| Chuvadi.Pdf.Rendering           | Complete |
| Chuvadi.Pdf.Watermark           | Complete |
| Chuvadi.Pdf.Redaction           | Complete |
| Chuvadi.Pdf.Forms               | Complete |
| Chuvadi.Pdf.Cli                 | Complete |

### Phase 1.1 — Backlog clear (v1.1.0–v1.7.0)

| Module                          | Status   | Tag     |
|---------------------------------|----------|---------|
| Chuvadi.Pdf.Annotations         | Complete | v1.1.0  |
| Chuvadi.Cryptography            | Complete | v1.3.0  |
| Chuvadi.Pdf.Encryption          | Complete | v1.3/4.0|
| Chuvadi.Pdf.Signatures          | Complete | v1.7.0  |
| Chuvadi.Pdf.Authoring           | Complete | v1.6.0  |

### Phase 2.2 — WOFF2 / Brotli (v1.8.0–v1.9.0)

| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Fonts.Woff2         | Complete |

### Phase 2.3 — Hardening (v1.10.0)

| Component                       | Status   |
|---------------------------------|----------|
| tests/Chuvadi.Pdf.Fuzz          | Complete |

---

## Phase Status

**Phase 1** — Read pipeline + Phase 1 modules — **Complete** (373 tests at v1.0.0).
**Phase 2** — Rendering, watermarking, redaction, forms, CLI — **Complete** (~564 total at v1.0.0).
**Phase 1.1** — Backlog clear (annotations, encryption, signatures, linearization,
                pattern redaction, Form XObject redaction, OCG, CMYK, TIFF) — **Complete**.
**Phase 2.2** — WOFF2 / Brotli encoder — **Complete to within ~1% of `BrotliStream` Optimal**.
                RFC 7932 §7 (context modeling) and §8 (static dictionary) deferred to v1.11.0.
**Phase 2.3** — Parser fuzz harness — **Complete**. Two real bugs found and fixed on
                first run (page-tree stack overflow, integer-overflow leak).

---

## Version

**1.10.0** — 752 tests across 22 src modules, all green.

---

## What Ships in 1.10

Inherits the v1.0.0 baseline (read pipeline, text extraction, rendering, page
operations, redaction, watermarking, forms, outlines, CLI) plus:

**Annotations** (v1.1.0)
- Read and create per PDF 32000-1 §12.5.
- Types: Text, FreeText, Link, Stamp, Ink, Markup, Generic.

**Pattern-based redaction** (v1.3.0)
- Regex matchers (SSN, email, phone, ICD-10, NHS, etc.) layered on top
  of the v1.0.0 rectangle redaction. Same PHI guarantee (byte-level removal).

**Form XObject & image redaction** (v1.5.0)
- `Do` operator traced with CTM intersection. Form XObjects and images
  overlapping a redaction rect are dropped from the rewritten stream.

**Optional content / layers** (v1.5.0)
- `OptionalContentReader` + `OptionalContentGroup`; reads `/OCProperties`,
  `/OCGs`, default config, resolved visibility (`/ON`, `/OFF`, `BaseState`).

**CMYK render output** (v1.5.0)
- Rasterizer and TIFF encoder write CMYK pixel buffers (photometric=5).

**TIFF I/O** (v1.3.0)
- TIFF baseline 6.0 read and write. Uncompressed, PackBits, LZW.
  Multi-page TIFFs via chained IFDs.

**Encryption** (read in v1.3.0, write in v1.4.0)
- AES-128 and AES-256 read+write. RC4-40 and RC4-128 read.
- Public API: `PdfDocument.Open(stream, password)`,
  `PdfWriter.Write(..., EncryptionOptions)`.
- PDF Algorithms 3/5 (R=4) and ISO 32000-2 Algorithms 8/9 (R=6).

**Linearization** (v1.6.0)
- Read: detects linearization, exposes `/Linearized` parameter dictionary.
- Write: produces linearized PDFs with primary hint stream.
- Spec-conformant per ISO 32000-1 Annex F. Viewer compatibility
  (Acrobat, Foxit, browsers) not yet verified end-to-end.

**Digital signature verification** (v1.7.0)
- PKCS#7/CMS detached signature parsing (PDF §12.8.3.3).
- Certificate chain extraction, signing-time recovery, byte-range
  verification. Verification only; signing is on the backlog.

**WOFF2 / Brotli encoder** (v1.8.0–v1.9.0)
- Brotli compressed encoder with LZ77 multi-command emission.
- Within ~1% of `System.IO.Compression.BrotliStream` Optimal on real
  data; remaining gap is RFC §7 + §8 (planned for v1.11.0).

**Parser fuzz harness** (v1.10.0)
- `tests/Chuvadi.Pdf.Fuzz` with three targets (pdf-open, content-stream,
  truetype). No NuGet deps. Saved-crash files include full stack traces.
- Already shipped two real-bug fixes: cyclic page-tree stack overflow
  (PdfPageCollection depth guard) and integer-overflow exception leak
  in PdfObjectParser (ParseInt32 helper).

---

## Backlog (post-1.10)

**PR 2.1 — Parser throw tightening** (next):
- TrueTypeLoader bounds checks (IndexOutOfRangeException found by
  fuzz harness, currently deferred).
- PdfName.Intern / FromRawBytes ArgumentException → PdfTokenizerException
  at three call sites (content-stream parser and xref/page-tree path).
- After PR 2.1: remove `typeof(ArgumentException)` safety valves from
  PdfOpenTarget and ContentStreamTarget expected-exception lists.

**v1.11.0 — Brotli encoder gap close:**
- RFC 7932 §7 (literal context modeling).
- RFC 7932 §8 (static dictionary).

**v2.0.0 — API review across 22 modules; breaking changes welcome.**

**Open items (no version assigned):**
- Digital signature **signing** (verification shipped in v1.7.0).
- Linearization viewer compatibility verification (Acrobat/Foxit/browser
  end-to-end).
- Vector PDF authoring expansion (basic authoring shipped via
  `Chuvadi.Pdf.Authoring`; broader content-stream-creation API on
  the longer-term roadmap).

---

## Deploy Script Status

`deploy.ps1` — registers every module in the current solution.
Single-backslash paths, CRLF line endings. Audit on each release.
