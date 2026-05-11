# Chuvadi (சுவடி)

> A complete, high-performance PDF library for .NET 10+.
> Pure managed code. Zero external dependencies. Apache 2.0.

**Chuvadi** (Tamil: *palm-leaf manuscript*) is a from-scratch PDF library
built for correctness, performance, and a modern .NET developer experience.

It aims to be the definitive free alternative to commercial PDF libraries —
better API design, better documentation, and competitive performance,
with zero licensing restrictions.

---

## Status

**Pre-release — active development.**
API is not yet stable. Not recommended for production use until v1.0.0.

| Module | Status |
|---|---|
| Chuvadi.Pdf.Primitives | 🔧 In development |
| Chuvadi.Pdf.Filters | 🔧 In development |
| Chuvadi.Pdf.Objects | 🔧 In development |
| Chuvadi.Pdf.IO | 🔧 In development |
| Chuvadi.Pdf.Documents | 🔧 In development |
| Chuvadi.Pdf.Content | 🔧 In development |
| Chuvadi.Pdf.Fonts | 🔧 In development |
| Chuvadi.Pdf.Text | 🔧 In development |
| Chuvadi.Pdf.Operations | 🔧 In development |

---

## Quick Start

```bash
dotnet add package Chuvadi.Pdf.Operations
```

```csharp
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Text;

// Merge PDFs
await PdfMerger.MergeAsync(
    ["report.pdf", "appendix.pdf"],
    "combined.pdf");

// Split pages
await PdfSplitter.ExtractPagesAsync(
    "combined.pdf",
    pageRange: "1-3",
    "pages1to3.pdf");

// Extract text — three strategies
using var doc = PdfDocument.Open("report.pdf");

var text = doc.Pages[0].ExtractText(TextExtractionStrategy.Layout);
```

---

## Design Principles

- **Zero dependencies in production code.** The library has no NuGet
  references. No transitive vulnerabilities, no license complications.
- **Streaming by default.** Large PDFs are never fully loaded into RAM.
- **Multiple strategies where it matters.** Text extraction, compression,
  and output format all expose options with sensible defaults.
- **Correctness over speed.** We test against a corpus of real-world PDFs
  from diverse generators and handle malformed files gracefully.
- **Modern C#.** Nullable reference types, spans, records, pattern matching
  — used correctly throughout, not retrofitted.

---

## Architecture

```
Chuvadi.Pdf.Operations      ← Application entry point
├── Chuvadi.Pdf.Text        ← Text extraction (3 strategies)
├── Chuvadi.Pdf.Fonts       ← Font parsing, glyph→Unicode mapping
├── Chuvadi.Pdf.Content     ← Content stream parser, graphics state
├── Chuvadi.Pdf.Documents   ← Document model: pages, outlines, metadata
│   └── Chuvadi.Pdf.IO      ← Reader, writer, xref, streaming
│       └── Chuvadi.Pdf.Objects  ← Object graph, object store
│           ├── Chuvadi.Pdf.Filters  ← DEFLATE, ASCII85, LZW, ...
│           └── Chuvadi.Pdf.Primitives  ← Tokenizer, primitive types
```

---

## Building

Prerequisites: .NET 10 SDK.

```bash
git clone https://github.com/chuvadi/chuvadi
cd chuvadi
pwsh setup.ps1          # First time only: creates the .sln file
dotnet build
dotnet test
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

Apache 2.0 — see [LICENSE](LICENSE).

Free for commercial and open-source use. No AGPL, no dual-licensing, no fees.
