# CHANGE-LOG.md — Chuvadi Decision History

> Append-only. Old entries are never rewritten.
> New entries supersede old ones where they conflict.
> Format: A[NN] — entries are numbered sequentially.

---

## A01 — Project Identity

**Date:** 2025-05-09
**Scope:** Project name, purpose, and audience
**Rationale:** Name carries meaning; Tamil origin fits the document-library purpose.

Chuvadi (சுவடி) — Tamil for palm-leaf manuscript / written scroll.
The library is a general-purpose PDF library for the .NET ecosystem,
not a hospital-internal tool. It is intended to be a free, open-source
replacement for PdfSharp and a AGPL-free alternative to iText.

**Files affected:** README.md, all csproj PackageDescription fields.

---

## A02 — License

**Date:** 2025-05-09
**Scope:** Open-source license choice
**Rationale:** Apache 2.0 over MIT because the patent termination clause
matters in the PDF space (historical patent activity around JBIG2, JPEG 2000).
MIT has no patent grant. Apache 2.0 is permissive, royalty-free, commercial-friendly,
and does not require derivative works to be open-sourced (unlike AGPL/GPL).

**Decision:** Apache 2.0. No dual licensing. No commercial tier. Free for all use.
**Files affected:** LICENSE, all csproj PackageLicenseExpression fields.

---

## A03 — Runtime Dependency Policy

**Date:** 2025-05-09
**Scope:** NuGet dependency rules for production vs test code
**Rationale:** Supply chain safety, auditability, air-gap compatibility,
and institutional trust for hospital deployments.

RULE: src/ projects have ZERO NuGet dependencies.
      Every line of production code is owned by this repository.
RULE: tests/ projects may use xUnit, FluentAssertions, FsCheck, BenchmarkDotNet.
      These never ship to production.
RULE: tools/ (CLI) may reference only src/ projects.

Build-time tooling (PyTorch for model training if OCR is later added,
compilers, SDK tools) is explicitly excluded from this rule —
it does not run in production.

**Files affected:** Directory.Packages.props, all csproj files.

---

## A04 — Target Framework and Language Version

**Date:** 2025-05-09
**Scope:** .NET and C# version targeting
**Rationale:** .NET 10 is the current LTS-track release at project start.
Latest C# gives access to primary constructors, collection expressions,
ref struct improvements, and modern span APIs critical for PDF byte processing.

**Decision:** net10.0, LangVersion latest.
             global.json pins SDK to 10.0.203 (installed version).
**Files affected:** Directory.Build.props, global.json.

---

## A05 — Code Quality Enforcement

**Date:** 2025-05-09
**Scope:** Compiler and analyzer settings
**Rationale:** A foundational library that will be used by other developers
must ship with maximum quality. Warnings that are silently ignored in
application code become bugs in library code because callers depend on
correct behavior of every public member.

Locked settings:
- `<Nullable>enable</Nullable>` — all nullability expressed in types
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — no warning is ignorable
- `<ImplicitUsings>disable</ImplicitUsings>` — every dependency explicit
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` — .editorconfig enforced at build
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — XML docs required

Style rules enforced as errors:
- IDE0021: Block body constructors (no expression-body constructors)
- IDE0011: Braces required on all control flow (if, for, foreach, while)
- IDE0005: No unnecessary using directives
- IDE0052: No unread private members
- CA1062: Validate public method parameters before use

**Files affected:** Directory.Build.props, .editorconfig.

---

## A06 — Naming and File Structure Conventions

**Date:** 2025-05-09
**Scope:** C# naming, file layout, namespace style
**Rationale:** Consistency at library scale — consumers read source and
expect predictable structure.

Locked conventions:
- One public type per file. File named after the type.
- File-scoped namespaces (`namespace Chuvadi.Pdf.Primitives;`)
- PascalCase: types, public members, constants
- _camelCase: private fields
- camelCase: parameters, local variables
- IPascalCase: interfaces
- Private fields prefixed with underscore: `_tokenBytes`, not `tokenBytes`
- No regions
- No `var` for primitive types; `var` acceptable when type is obvious from RHS

**Files affected:** .editorconfig (enforces many of these as errors).

---

## A07 — Solution Structure and Project Layering

**Date:** 2025-05-09
**Scope:** Multi-project solution layout and dependency direction
**Rationale:** Each project has exactly one responsibility. Dependency
direction is strictly bottom-up. No circular dependencies. Each layer
can be tested independently.

Layer order (bottom to top):
```
Chuvadi.Pdf.Primitives     — tokens, primitive types, tokenizer
Chuvadi.Pdf.Filters        — stream filters (DEFLATE, ASCII85, LZW, etc.)
Chuvadi.Pdf.Objects        — object model, object store, xref
Chuvadi.Pdf.IO             — reader, writer, file structure
Chuvadi.Pdf.Documents      — document model, pages, outlines, metadata
Chuvadi.Pdf.Fonts          — font parsing, glyph-to-Unicode mapping
Chuvadi.Pdf.Content        — content stream parser, graphics state
Chuvadi.Pdf.Text           — text extraction (3 strategies)
Chuvadi.Pdf.Operations     — high-level operations: merge, split, etc.
```

Test project per src project. Integration tests in Chuvadi.Pdf.Integration.Tests.
CLI tool in tools/Chuvadi.Pdf.Cli.

**Files affected:** Chuvadi.slnx, all csproj files.

---

## A08 — Phase Scope Boundaries

**Date:** 2025-05-09
**Scope:** What is in scope for Phase 1, 2, and 3
**Rationale:** Time-boxed phases prevent scope creep and keep the first
release shipable. Each phase is independently valuable.

**Phase 1 — Core PDF library (current):**
- PDF object model parser (xref classic + stream, objects, indirect refs)
- DEFLATE and all standard non-encryption filters
- PDF writer (full rewrite + incremental update)
- Page-level operations: merge, split, delete, rotate, reorder
- Born-digital text extraction (3 strategies: operator, layout, glyph)
- Font handling for text extraction (TrueType, CFF, Type 1, CMaps)
- Document model: metadata, outlines, page labels, hyperlinks
- Annotation reading (not creation)
- Form field reading (not filling)
- Resource inventory API
- CLI tool: info, merge, split, delete, rotate, extract-text

**Phase 2 — Images and editing:**
- PNG, JPEG, TIFF, BMP decoders/encoders
- Image extraction from PDF
- Image to PDF embedding
- PDF to image rendering (full rasterizer — largest ticket in Phase 2)
- True content redaction (PHI-safe)
- Watermarking
- Annotation creation and editing
- Form field filling and creation

**Phase 3 — Security and compliance:**
- Encryption: RC4-40, RC4-128, AES-128, AES-256 (all revisions)
- Digital signatures (PKCS#7, sign and verify)
- JavaScript preservation across operations
- PDF/A conformance (archival format)
- Full documentation, samples, migration guides

**Files affected:** All src projects scoped accordingly.

---

## A09 — Distribution and Versioning

**Date:** 2025-05-09
**Scope:** How Chuvadi reaches users
**Rationale:** NuGet.org is the standard .NET package channel. GitHub
Releases for binaries and changelog. Semantic versioning for trust.

- Primary channel: NuGet.org (dotnet add package Chuvadi.Pdf.Operations)
- Pre-release builds: GitHub Packages (automated on every push to main)
- Stable releases: NuGet.org (automated on every git tag vX.Y.Z)
- Versioning: Semantic Versioning strictly
  - 0.x.y during active Phase 1 development (API not yet stable)
  - 1.0.0 on first stable Phase 1 release (API stability commitment begins)
  - Breaking changes only in major version bumps

**Files affected:** .github/workflows/ci.yml, .github/workflows/release.yml,
                    Directory.Build.props (version defaults).

---

## A10 — Working Agreement Adoption

**Date:** 2025-05-09
**Scope:** Workflow discipline for this project
**Rationale:** Imported from prior project CLAUDE.md. Adapted for Chuvadi context.

Key rules adopted:
- Complete files only. Never snippets, never diffs, never "insert here."
- Pre-code checklist before every generation batch (WHAT / SPEC / DESIGN / DEPLOY).
- Post-code checklist after every generation batch.
- Every new file registered in deploy.ps1 in the same batch.
- Build must be green before proceeding to next module.
- File header format from next delivery onwards:
  ```csharp
  // SPEC:  PDF 32000-1:2008 §X.Y — Section name
  // PHASE: Phase N — Module name
  // [One-line summary]
  ```
- CHANGE-LOG entries for every locked decision.
- Deploy folder: %USERPROFILE%\Downloads\chuvadi\
- Deploy script: .\deploy.ps1 in repo root (CRLF, ASCII-safe for Windows PS 5.1)

**Files affected:** CLAUDE.md (reference), CHANGE-LOG.md (this file),
                    deploy.ps1 (running), docs/ (this directory).

---

## A11 — Build Progress Checkpoint

**Date:** 2025-05-09
**Scope:** Current state of the codebase
**Rationale:** Session continuity. Future sessions read this to know where we are.

Completed and green:
- Solution scaffold: all 20 projects, build infrastructure, CI workflows
- Chuvadi.Pdf.Primitives: all 12 primitive types (PdfObjectId, PdfPrimitive,
  PdfNull, PdfBoolean, PdfInteger, PdfReal, PdfName, PdfString, PdfArray,
  PdfDictionary, PdfStream, PdfReference)
- PdfTokenType, PdfToken, PdfTokenizer, PdfTokenizerException
- 67 tests passing, 0 failures

In progress (deployed, awaiting build confirmation):
- PdfTokenizer tests (PdfTokenizerTests.cs)
- Expected: additional tokenizer tests to pass on top of 67

Next up:
- Chuvadi.Pdf.Filters: DEFLATE (RFC 1951) — the largest single filter
- Then: ASCII85, ASCIIHex, LZW, RunLength

**Files affected:** All src/Chuvadi.Pdf.Primitives files.

---

---

## A12 — Default Font: LiPi Sans

**Date:** 2025-05-10
**Scope:** Default font for all Chuvadi-adjacent applications and PDF output

LiPi Sans v1.0 is the default font family for all projects in the LiPi
ecosystem, including Chuvadi and any companion applications.

**Font family:** LiPi Sans
**License:** SIL OFL 1.1 (Inter + Noto Sans as base families)
**Coverage:** Latin (English/European), Devanagari, Bengali, Tamil, Telugu,
             Malayalam, Kannada, Gujarati, Gurmukhi, Odia
**Format stored:** woff2 (web fonts) in assets/fonts/lipi-sans/
**Variable font:** Yes — single file covers weight axis 100-900

**Application by phase:**

Phase 1 — Chuvadi.Pdf.Fonts:
  When implementing font embedding for text extraction and document writing,
  LiPi Sans (via its underlying Inter + Noto Sans TTF sources) is the
  reference family. TTF/OTF versions must be obtained separately from the
  Inter and Noto Sans repositories for PDF embedding.
  woff2 files in assets/ serve as the design reference and for Unicode
  range routing logic.

Phase 2 — PDF rendering (image output):
  When rendering PDFs to images, LiPi Sans is the fallback/default font
  for glyph rendering when the PDF's embedded fonts are unavailable.

CLI tool and any HTML output:
  The woff2 files in assets/fonts/lipi-sans/ are ready to use directly.
  Link lipi-sans.css and set font-family: var(--lipi-sans).

**Files stored:** assets/fonts/lipi-sans/ (11 woff2 files + CSS + LICENSE)
**NOT stored:** TTF/OTF versions — obtain from Inter and Noto Sans repos
               when Chuvadi.Pdf.Fonts implementation begins.


---

## A13 — Phase 2 Scope and Architecture

**Date:** 2025-05-10
**Scope:** Phase 2 build plan, dependency policy, rasterizer decision

**Zero-dependency rule extended to Phase 2.**
No SkiaSharp. No external rendering libraries. Every pixel produced by Chuvadi
is produced by code owned by this repository. This is a deliberate product
decision: a hospital-grade, auditable, air-gap-deployable library must own
its full rendering stack.

**Phase 2 module order:**
1. Chuvadi.Pdf.Graphics — 2D geometry, paths, colour spaces (shared foundation)
2. Chuvadi.Pdf.Images — JPEG, PNG, TIFF decoders/encoders, image extraction
3. Chuvadi.Pdf.Fonts.Rendering — TrueType/OTF glyph outline extraction
4. Chuvadi.Pdf.Rendering — Page rasterizer (page → pixel buffer → PNG/BMP)
5. Chuvadi.Pdf.Watermark — Text and image watermarking
6. Chuvadi.Pdf.Redaction — PHI-safe content removal (uses incremental writer)
7. Chuvadi.Pdf.Forms — AcroForm read and fill
8. Chuvadi.Pdf.Annotations — Annotation read and create
9. CLI expansion — info, merge, split, render, redact, extract-text commands

**Deferred Phase 1 items folded into Phase 2:**
- Outlines/bookmarks → with Step 7 (forms/document model)
- Glyph extractor (3rd text strategy) → with Step 3 (font rendering)
- Incremental writer → with Step 6 (redaction requires it)

**Target:** General-purpose PDF library. Hospital/clinical capabilities
(PHI redaction, audit-safe rendering, air-gap deployment) built in as
first-class features, not afterthoughts.

**Version milestone:** 1.0.0 after Phase 2 rendering is stable.
                       Phase 1 = 0.9.x pre-release.

