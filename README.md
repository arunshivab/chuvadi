# Chuvadi (சுவடி)

> A zero-dependency, audit-safe PDF library for .NET.

**Chuvadi** (Tamil: சுவடி, "palm-leaf manuscript") is a general-purpose
PDF library written entirely in C#, with **zero NuGet dependencies in
production code**. Every byte read, every pixel rendered, every redacted
string removed — owned by this repository, auditable line by line.

- **License:** Apache-2.0
- **Target:** .NET 10
- **Version:** 1.0.0

---

## Why Chuvadi

The .NET PDF ecosystem has three rough categories:

1. **PdfSharp / iTextSharp 4.x** — mature but unmaintained; security CVEs go unpatched.
2. **iText 7+ / Aspose / PDFsharp 6+** — actively maintained, but AGPL or
   commercial-license. Hospital deployments and air-gapped environments
   either can't accept AGPL terms or can't pay per-seat fees.
3. **SkiaSharp-backed wrappers** — pull in a 30 MB native dependency that
   audit teams can't review byte by byte.

Chuvadi is a permissively-licensed (Apache-2.0), zero-dependency,
fully-managed alternative. Designed from the ground up for environments
where **every line of code in the dependency tree matters**: clinical
informatics, financial document processing, government, defence,
air-gap-deployed kiosks.

---

## What's in 1.0

| Capability                             | Module                          |
|----------------------------------------|---------------------------------|
| Read PDF 1.4–2.0 (xref + xref streams) | Chuvadi.Pdf.IO                  |
| All standard filters                   | Chuvadi.Pdf.Filters             |
| Text extraction (3 strategies)         | Chuvadi.Pdf.Text                |
| Page rasterisation → PNG/BMP           | Chuvadi.Pdf.Rendering           |
| TrueType / OpenType glyph extraction   | Chuvadi.Pdf.Fonts.Rendering     |
| Text and image watermarks              | Chuvadi.Pdf.Watermark           |
| **True PHI-safe redaction**            | Chuvadi.Pdf.Redaction           |
| AcroForm read and fill                 | Chuvadi.Pdf.Forms               |
| Document outlines (bookmarks)          | Chuvadi.Pdf.Forms               |
| Merge / split / delete / rotate        | Chuvadi.Pdf.Operations          |
| Command-line tool (17 verbs)           | tools/Chuvadi.Pdf.Cli           |

Full module list and dependency graph: see `docs/BASELINE.md`.
Decision history: see `docs/CHANGE-LOG.md`.

---

## Quick start (library)

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;

using FileStream fs = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false);

TextExtractor extractor = new(doc.Objects, ExtractionStrategy.Layout);
for (int i = 0; i < doc.PageCount; i++)
{
    Console.WriteLine(extractor.ExtractText(doc.Pages[i]));
}
```

```csharp
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Graphics;

RedactionOptions opts = new()
{
    Rectangles =
    {
        new RedactionRect(0, new RectangleF(90, 100, 200, 30)),
    }
};

using FileStream input = File.OpenRead("patient_chart.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);
using FileStream output = File.Create("patient_chart_redacted.pdf");
Redactor.Apply(output, doc, opts);
```

The redacted text is **byte-by-byte absent** from `patient_chart_redacted.pdf`.
No content-stream operator and no indirect object holds the removed data.
See `BASELINE.md` §B15 for the formal definition.

---

## Quick start (CLI)

After `dotnet build`, the `chuvadi` executable is in
`tools/Chuvadi.Pdf.Cli/bin/Debug/net10.0/`.

```bash
chuvadi info patient_chart.pdf
chuvadi watermark in.pdf --output out.pdf --text DRAFT --opacity 0.3
chuvadi redact in.pdf --output out.pdf --rect 0,90,100,200,30
chuvadi extract-text in.pdf --strategy layout
chuvadi render in.pdf --output page0.png --page 0 --dpi 150
chuvadi form-fill in.pdf --output filled.pdf --field name=Jane --field dob=1985-04-12
chuvadi outlines in.pdf
chuvadi merge a.pdf b.pdf --output merged.pdf

# debug verbs
chuvadi tokenize in.pdf --page 0
chuvadi dump-objects in.pdf
chuvadi inspect-xref in.pdf
chuvadi validate-fonts in.pdf
```

Run `chuvadi help` for the full verb surface.

---

## Architectural invariants

The library is structured around 16 invariants that NEVER change without
an explicit CHANGE-LOG entry superseding them. The most consequential:

- **B01** — zero NuGet packages in `src/`.
- **B02** — strict bottom-up dependency direction. No circular references.
- **B15** — redaction is byte-level removal, not visual cover-up.
- **B16** — preload the object graph before iterating for rewrites.

Full list: `docs/BASELINE.md`.

---

## Repository layout

```
chuvadi/
├── src/                       # production code (no NuGet deps)
│   ├── Chuvadi.Pdf.Primitives/
│   ├── Chuvadi.Pdf.Filters/
│   ├── Chuvadi.Pdf.Objects/
│   ├── Chuvadi.Pdf.IO/
│   ├── Chuvadi.Pdf.Documents/
│   ├── Chuvadi.Pdf.Fonts/
│   ├── Chuvadi.Pdf.Content/
│   ├── Chuvadi.Pdf.Text/
│   ├── Chuvadi.Pdf.Operations/
│   ├── Chuvadi.Pdf.Graphics/
│   ├── Chuvadi.Pdf.Images/
│   ├── Chuvadi.Pdf.Fonts.Rendering/
│   ├── Chuvadi.Pdf.Rendering/
│   ├── Chuvadi.Pdf.Watermark/
│   ├── Chuvadi.Pdf.Redaction/
│   └── Chuvadi.Pdf.Forms/
├── tests/                     # xUnit, FluentAssertions
├── tools/
│   └── Chuvadi.Pdf.Cli/       # the `chuvadi` executable
└── docs/
    ├── BASELINE.md            # invariants (B01–B16)
    ├── CHANGE-LOG.md          # decision history (A01–A17)
    ├── SESSION-STATE.md       # current build state
    └── BACKLOG.md             # planned features for 1.1+
```

---

## Building

```bash
dotnet build
dotnet test
```

Requires .NET 10 SDK. **~564 tests across 19 test projects, 0 failures.**

---

## Contributing

This is currently a single-author project. PRs welcome once contribution
guidelines are written.

Style rules and pitfall list: `CLAUDE.md` (root). The repository uses a
zero-warnings policy and an in-repo style checker (`tools/check_style.py`).
Run it on every changed file before committing.

---

## Roadmap

See `docs/BACKLOG.md`. Phase 1.1 targets: annotations, pattern-based
redaction, digital signatures, encryption, linearization, vector page
creation.

---

## License

Apache-2.0. See `LICENSE`.

Chuvadi is and will remain free for all use including commercial.
There is no dual-licensing tier and no premium edition.

