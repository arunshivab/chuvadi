# Chuvadi.Examples.Reader

Exercises the high-level `IPdfReader` facade against a PDF — opens the file,
prints metadata and encryption info, lists the outline tree, searches for a
query, renders page 1 to SVG, and reports text-run geometry on page 1.

This is the integration-test-style example for the `Chuvadi.Pdf.Reader` module:
a single command shakes out every method on the facade against a real document.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.Reader -- input.pdf
```

With a search query (defaults to `the`):

```bash
dotnet run --project examples/Chuvadi.Examples.Reader -- input.pdf "patient"
```

For encrypted PDFs, the program prompts for a password on stdin when the open
fails with an encryption error.

## Output

```
File: report.pdf  (12 pages)

Encryption: none

Metadata:
  Title:        Q4 Operations Review
  Author:       Operations Team
  Subject:      (none)
  Creator:      LibreOffice 7.6
  Producer:     LibreOffice 7.6
  CreationDate: 2025-11-01T14:23:00.0000000+00:00
  ModDate:      2025-11-03T09:11:00.0000000+00:00

Outline: 3 top-level entries
  - Executive Summary  → page 1
  - Operational Highlights  → page 3
    - North region  → page 4
    - South region  → page 7
  - Appendices  → page 11

Search "the":
  Page 1, char offset 42 at (118,712)
  Page 1, char offset 156 at (118,690)
  Page 2, char offset 8 at (72,720)
  Page 2, char offset 91 at (180,690)
  Page 3, char offset 24 at (72,720)
  ... and 47 more (total: 52)

Render: page 1 → /home/aruns/code/chuvadi/page-1.svg (124,829 chars)
Text runs on page 1: 28
```

## What it shows

- `IPdfReader` resolution via `new ChuvadiPdfReader()` — direct instantiation;
  in a Blazor app this would be DI registration as a singleton.
- `OpenAsync(stream, fileName, password?)` — async open with optional password.
- `doc.Encryption?.Algorithm` / `.AllowPrint` / `.AllowCopy` / etc. —
  inspecting permission flags on an encrypted document.
- `doc.Title` / `doc.Author` / `doc.CreationDate` / `doc.ModDate` —
  document-info metadata as typed properties.
- `GetOutlinesAsync(doc)` — async retrieval of the bookmark tree.
- `SearchAsync(doc, query, options)` — streaming search across pages with
  `await foreach`; each `SearchMatch` carries page index, character offset,
  match length, and a list of bounding boxes in PDF user-space coordinates.
- `RenderPageSvgAsync(doc, pageIndex)` — full-page SVG with selectable text
  layer at native PDF coordinates. The reader app sizes via CSS — SVG is
  resolution-independent.
- `GetTextRunsAsync(doc, pageIndex)` — text runs with per-glyph positions,
  for building selection overlays independently of the rendered SVG.
