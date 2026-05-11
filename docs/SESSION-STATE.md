# SESSION-STATE.md — Current Build State

> Read this first each session, then CHANGE-LOG.md, then BASELINE.md.
> Rules and pitfalls live in CLAUDE.md — not here.

---

## Last Updated

2026-05-11 — Phase 2 complete, 1.0.0 tag.

---

## Build Summary

| Module                          | Status   | Tests  |
|---------------------------------|----------|--------|
| Chuvadi.Pdf.Primitives          | Complete | 125    |
| Chuvadi.Pdf.Filters             | Complete | 79     |
| Chuvadi.Pdf.Objects             | Complete | ~35    |
| Chuvadi.Pdf.IO                  | Complete | ~21    |
| Chuvadi.Pdf.Documents           | Complete | ~20    |
| Chuvadi.Pdf.Fonts               | Complete | ~30    |
| Chuvadi.Pdf.Content             | Complete | ~21    |
| Chuvadi.Pdf.Text                | Complete | ~17    |
| Chuvadi.Pdf.Operations          | Complete | ~22    |
| Chuvadi.Pdf.Graphics            | Complete | ~55    |
| Chuvadi.Pdf.Images              | Complete | ~25    |
| Chuvadi.Pdf.Fonts.Rendering     | Complete | ~28    |
| Chuvadi.Pdf.Rendering           | Complete | ~20    |
| Chuvadi.Pdf.Watermark           | Complete | ~15    |
| Chuvadi.Pdf.Redaction           | Complete | ~18    |
| Chuvadi.Pdf.Forms               | Complete | ~18    |
| Chuvadi.Pdf.Cli                 | Complete | ~26    |

**Last known passing total: ~564 tests, 0 failures, 0 warnings.**

---

## Phase Status

**Phase 1** — Read pipeline + Phase 1 modules — **Complete** (373 tests at close).
**Phase 2** — Rendering, watermarking, redaction, forms, CLI — **Complete** (~191 tests added).

| Step | Module                       | Status   |
|------|------------------------------|----------|
| 1    | Chuvadi.Pdf.Graphics         | Complete |
| 2    | Chuvadi.Pdf.Images           | Complete |
| 3    | Chuvadi.Pdf.Fonts.Rendering  | Complete |
| 4    | Chuvadi.Pdf.Rendering        | Complete |
| 5    | Chuvadi.Pdf.Watermark        | Complete |
| 6    | Chuvadi.Pdf.Redaction        | Complete |
| 7    | Chuvadi.Pdf.Forms (+Outlines)| Complete |
| 8    | Chuvadi.Pdf.Annotations      | Deferred to Phase 1.1 (BACKLOG #1) |
| 9    | Chuvadi.Pdf.Cli expansion    | Complete |

Deferred Phase 1 items folded into Phase 2 (delivered):
- Glyph extractor (3rd text strategy) → built into Fonts.Rendering (Step 3).
- Incremental writer → built into Redaction (Step 6) via object-graph rewrite.
- Outlines/bookmarks → built into Forms (Step 7).

---

## Version

**1.0.0** — Phase 2 close, all 17 modules green.

---

## What Ships in 1.0

**Read pipeline**
- PDF 1.4–2.0 ingestion (xref table + xref streams, including hybrid).
- All standard PDF filters: Flate, ASCIIHex, ASCII85, RunLength, LZW.
- Type1 standard 14 fonts, TrueType, CFF/Type1C inspection.
- Full content stream tokenizer and parser.

**Text extraction**
- Operator-walking and layout-aware strategies.
- Glyph-level fallback for non-Latin scripts via TrueType outline extraction.

**Rendering**
- Zero-dependency scanline rasterizer (PNG/BMP output).
- Standard PDF fonts (Helvetica, Times, Courier) plus embedded TTF.
- Adaptive Bezier flattening, both fill rules, butt/square stroke caps.

**Document operations**
- Merge, split, delete pages, rotate, extract page ranges.
- Text watermarks (rotation, opacity, per-page targeting).
- Image watermarks via PNG XObject embedding.

**PHI-safe redaction**
- Rectangle-based content-stream rewriting (see CHANGE-LOG A15).
- Original content-stream objects excluded from output.
- Conservative TJ array drop.

**AcroForms**
- Read field tree (fully-qualified names, types, current values, object IDs).
- Fill values, set button `/AS`, mark `/NeedAppearances=true`.

**Outlines (bookmarks)**
- Read full tree including children, resolve destinations to page indices.

**CLI** (`chuvadi`)
- 11 user verbs: info, render, watermark, redact, form-fill, extract-text,
  outlines, merge, split, delete, rotate.
- 6 debug verbs: tokenize, dump-objects, parse-content, decode-stream,
  inspect-xref, validate-fonts.

---

## What's Not in 1.0

See `BACKLOG.md` for Phase 1.1 items:

1. Annotations (read + create).
2. Pattern-based redaction (regex matchers).
3. Form XObject and inline-image redaction.
4. Digital signatures.
5. Encryption (read + write).
6. Linearization (Fast Web View).
7. Optional content (layers).
8. CMYK render output.
9. TIFF encoder/decoder.
10. Vector PDF output (page creation, not just rewriting).

---

## Deploy Script Status

`deploy.ps1` — 151 entries, all modules registered, CRLF line endings,
single-backslash paths verified. No drift between
`%USERPROFILE%\Downloads\chuvadi\` and registry as of last audit.
