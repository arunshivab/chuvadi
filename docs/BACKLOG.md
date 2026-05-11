# BACKLOG.md — Phase 1.1+ Roadmap

> Tracks capabilities not yet shipped, grouped by phase. Items are ordered
> within each phase by approximate priority. Move items to CHANGE-LOG.md
> when work begins (creating a new A-entry for the decision to take it on).

---

## Phase 1.1 — Compatibility & Completeness

The Phase 2 release covers the documented spec ground for general PDF
reading, writing, redaction, watermarking, and form filling. Phase 1.1
fills in capabilities that are common in real-world PDFs but were not
in the original Phase 2 scope.

### 1.1.1 Annotations (read + create)
**Status:** Deferred from Phase 2 Step 8.
**Why deferred:** Forms (Step 7) absorbed the outline-reading scope of
the original annotation work, and AcroForm widget annotations are
already supported via the Forms module. Standalone non-form annotations
(text, highlight, ink, stamp, link) need their own model.

- Read existing annotations from each page's `/Annots` array.
- Subtypes to support: Text, Link, FreeText, Highlight, Underline,
  StrikeOut, Squiggly, Stamp, Ink.
- Create new annotations and write a new PDF (similar pattern to
  WatermarkStamper: copy page dictionary, append to `/Annots`).
- Module name: `Chuvadi.Pdf.Annotations`.

### 1.1.2 Pattern-based redaction
**Status:** Not started.
**Why:** Phase 2 redaction is rectangle-based. Hospitals frequently want
"redact every SSN", "redact every email address", "redact every MRN"
without computing coordinates by hand.

- Regex matcher running on extracted text fragments.
- Resolve each match back to a device-space rectangle using the same
  glyph positions that the text extractor recovers.
- Pre-built patterns: SSN, US phone, email, ICD-10 code prefix, MRN
  (hospital-configurable).
- Extension to `Chuvadi.Pdf.Redaction.RedactionOptions` with a
  `Patterns` collection.

### 1.1.3 Redaction of non-text content
**Status:** Not started. Phase 2 redacts text-showing operators only.

- Inline-image (`BI/ID/EI`) and image XObject (`Do`) operator removal
  when the painted area intersects a redaction rectangle.
- Form XObject (`Do`) recursion into nested content streams.

### 1.1.4 Digital signatures
**Status:** Not started.
**Why:** Hospital workflow signatures, doctor sign-offs, audit trails.

- Read existing `/Sig` field values, report signed/unsigned, signing time.
- Verify integrity (byte-range hash over the document).
- Optional creation of new signatures via callback for signing
  (BYO certificate; library does not embed cryptography).

### 1.1.5 Encryption (read + write)
**Status:** Not started.
**Why:** PDFs delivered between institutions are commonly password-protected.

- Read: AES-128 (V=4) and AES-256 (V=5) decryption with user/owner password.
- Write: same algorithms.
- RC4 not supported for new files (legacy read only, with a warning).
- Module name: `Chuvadi.Pdf.Encryption`.

### 1.1.6 Linearization (Fast Web View)
**Status:** Not started.

- Write linearized PDFs so the first page can render before the full
  document is downloaded.
- Used by some EHR vendors for chart-streaming.

### 1.1.7 Optional content (layers)
**Status:** Not started.

- Read and toggle `/OC` optional content groups.
- Engineering drawings and floor plans rely on this; clinical use is rare
  but increasing for anatomical overlays.

### 1.1.8 CMYK render output
**Status:** Phase 2 renders to RGBA only.

- Add CMYK pixel buffer and TIFF CMYK encoder.
- Required for print-shop integrations (label printing, lab requisitions).

### 1.1.9 TIFF encoder / decoder
**Status:** Mentioned in CHANGE-LOG A13 but not delivered.

- Multi-page TIFF reading (medical imaging often stores DICOM-adjacent
  TIFFs).
- Single-page TIFF writing from rasterizer output.

---

## Phase 1.2 — Authoring

Phase 2 rewrites existing PDFs (redaction, watermarking, form fill,
page operations). Phase 1.2 adds **page-from-scratch authoring**.

### 1.2.1 Vector page creation
**Status:** Not started.

- High-level builder: `PdfPageBuilder.New(width, height).DrawText(...).DrawRectangle(...)`.
- Generates content stream operators directly.

### 1.2.2 Font embedding (subsetted)
**Status:** Phase 2 only uses the 14 standard fonts for new content.

- Embed an arbitrary TrueType as a subsetted CIDFontType2 / Type0.
- Required for non-Latin scripts in generated content
  (Tamil, Devanagari, Han, etc.).

### 1.2.3 Tagged PDF / accessibility
**Status:** Not started.

- Generate structure trees (`/StructTreeRoot`) on page creation.
- Compliance: PDF/UA-1 first, then PDF/A-3.

---

## Phase 1.3 — Performance & Scale

Single-document operations are fast in 1.0. Phase 1.3 targets
high-volume batch and large documents.

### 1.3.1 Streaming page enumeration
- Open a 10 000-page PDF without loading every page into memory.

### 1.3.2 Parallel redaction
- Per-page redaction tasks run in parallel; serialise writes.

### 1.3.3 Benchmarks and regression detection
- BenchmarkDotNet suite for hot paths (tokenizer, deflate, rasterizer).
- Baseline timings tracked per release.

---

## Backlog Triage Rules

1. Items move from this file to a CHANGE-LOG A-entry when work begins.
2. Items below 1.1.5 may be re-ordered without breaking compatibility
   — they are independent.
3. Annotations (1.1.1) blocks "1.0.x bugfix" work only if a customer
   needs it; otherwise it can wait for 1.1.0.
4. Encryption (1.1.5) is the largest backlog item; consider splitting
   into 1.1.5a (read) and 1.1.5b (write) when started.
