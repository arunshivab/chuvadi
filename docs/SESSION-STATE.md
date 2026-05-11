# SESSION-STATE.md — Current Build State

> Read this first each session, then CHANGE-LOG.md, then BASELINE.md.
> Rules and pitfalls live in CLAUDE.md — not here.

---

## Last Updated

2025-05-11

---

## Build Summary

| Module | Status | Tests |
|---|---|---|
| Chuvadi.Pdf.Primitives | Complete | 125 |
| Chuvadi.Pdf.Filters | Complete | 79 |
| Chuvadi.Pdf.Objects | Complete | ~35 |
| Chuvadi.Pdf.IO | Complete | ~21 |
| Chuvadi.Pdf.Documents | Complete | ~20 |
| Chuvadi.Pdf.Fonts | Complete | ~30 |
| Chuvadi.Pdf.Content | Complete | ~21 |
| Chuvadi.Pdf.Text | Complete | ~17 |
| Chuvadi.Pdf.Operations | Complete | ~22 |
| Chuvadi.Pdf.Graphics | Complete | ~55 |
| Chuvadi.Pdf.Images | Complete | ~25 |

**Last known passing total: 439 tests, 0 failures, 0 warnings**

---

## Phase 2 Progress

| Step | Module | Status |
|---|---|---|
| 1 | Chuvadi.Pdf.Graphics | Complete |
| 2 | Chuvadi.Pdf.Images | Complete |
| 3 | Chuvadi.Pdf.Fonts.Rendering | Next |
| 4 | Chuvadi.Pdf.Rendering | After Fonts.Rendering |
| 5 | Chuvadi.Pdf.Watermark | After Rendering |
| 6 | Chuvadi.Pdf.Redaction | After Watermark |
| 7 | Chuvadi.Pdf.Forms | After Redaction |
| 8 | Chuvadi.Pdf.Annotations | After Forms |
| 9 | CLI expansion | After Annotations |

Deferred Phase 1 items (folded into Phase 2 as we go):
- Glyph extractor (3rd text strategy) — with Step 3 (Fonts.Rendering)
- Incremental writer — with Step 6 (Redaction requires it)
- Outlines/bookmarks — with Step 7 (Forms / document model)

---

## What Is Next

### Step 3 — Chuvadi.Pdf.Fonts.Rendering

TrueType/OpenType glyph outline extraction for the rasterizer.

**Scope:**
- `TrueTypeLoader` — reads TTF/OTF from byte array: head, hhea, maxp, loca, glyf tables
- `GlyphOutline` — a `Path` (from Chuvadi.Pdf.Graphics) for a single glyph
- `GlyphMetrics` — advance width, left side bearing, bounding box
- `FontRenderer` — top-level API: font bytes + glyph ID → GlyphOutline + GlyphMetrics
- Deferred glyph extractor strategy from Phase 1 wired in here

**Requires scaffold:** dotnet new classlib + xunit + sln add + delete placeholders

---

## Environment

| Item | Value |
|---|---|
| Developer machine | Windows, Pune, India |
| .NET SDK | 10.0.203 |
| Repo root | C:\Users\aruns\Documents\Chuvadi\chuvadi-scaffold\chuvadi\ |
| Deploy folder | %USERPROFILE%\Downloads\chuvadi\ |
| Deploy script | .\deploy.ps1 (105 entries, CRLF, ASCII-safe) |
| Style checker | python3 tools/check_style.py |

---

*End of SESSION-STATE.md*
