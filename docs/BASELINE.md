# BASELINE.md — Chuvadi Architectural Invariants

> These rules never change regardless of phase, feature, or contributor.
> When a design decision conflicts with a BASELINE rule, the rule wins
> unless a new CHANGE-LOG entry explicitly supersedes it.
> Read this before writing any Chuvadi code.

---

## B01 — Zero Production Dependencies

Production code (anything in src/) has zero NuGet package references.
Every type used in production is either from the .NET BCL or from
another Chuvadi.Pdf.* project in this repository.

This is non-negotiable for the library's core value proposition:
auditability, air-gap compatibility, and supply-chain safety.

Test projects (tests/) may use xUnit, FluentAssertions, FsCheck,
and BenchmarkDotNet. These never ship to production.

Violation signal: a `<PackageReference>` appearing in any src/ csproj.

---

## B02 — Dependency Direction Is Strictly Bottom-Up

The project dependency graph flows in one direction only:

```
Chuvadi.Pdf.Operations
  └── Chuvadi.Pdf.Text
        └── Chuvadi.Pdf.Fonts
        └── Chuvadi.Pdf.Content
              └── Chuvadi.Pdf.Documents
                    └── Chuvadi.Pdf.IO
                          └── Chuvadi.Pdf.Objects
                                └── Chuvadi.Pdf.Filters
                                └── Chuvadi.Pdf.Primitives
```

No project may reference a project above it in this graph.
No circular dependencies under any circumstances.

Violation signal: a `<ProjectReference>` that points upward in the stack.

---

## B03 — Streaming by Default

No operation in Chuvadi loads an entire PDF into memory unless
the caller explicitly opts in for performance reasons.

The reader uses lazy object resolution — objects are parsed on demand,
not on open. Large PDFs (500 pages, 50 MB+) must work without
materialising the entire file in RAM.

The writer emits a valid PDF in a single forward pass where possible.

Corollary: all hot-path types use Span<byte> and ReadOnlySpan<byte>
rather than byte[] where the API permits. Array allocations on the
tokenizer path are a defect, not a style choice.

Violation signal: `File.ReadAllBytes()` or loading a full stream to
a MemoryStream without documented justification.

---

## B04 — Names Are Always Interned

Every PdfName instance is obtained through PdfName.Intern() or
PdfName.FromRawBytes(). The intern table guarantees that identical
name strings always produce the same object reference.

This makes PdfDictionary key lookup allocation-free (reference equality,
no string comparison) and makes == on PdfName correct without overriding
GetHashCode in the dictionary itself.

Never construct a PdfName directly (the constructor is private).
Never compare PdfName values with string.Equals — use == or ReferenceEquals.

Violation signal: any new allocation path for PdfName bypassing the intern table.

---

## B05 — Immutable Primitives, Controlled Mutation at Document Level

PDF primitive objects (PdfNull, PdfBoolean, PdfInteger, PdfReal,
PdfName, PdfString, PdfArray, PdfDictionary, PdfStream, PdfReference)
are immutable once constructed.

PdfArray and PdfDictionary expose mutation methods (Add, Set, Remove)
because the reader must build them incrementally. However, once a
primitive is added to the document model layer (Chuvadi.Pdf.Documents),
it must not be mutated by any path that bypasses the document model's
own mutation API.

The document model (PdfDocument, PdfPage, etc.) exposes explicit
mutation operations that produce new primitives and update references
atomically. No "reach in and mutate the raw dictionary" patterns
outside of the IO and Objects layers.

---

## B06 — Errors Are Exceptions, Not Return Values

Chuvadi uses exceptions for error conditions, not Result<T> or
nullable return types that silently hide failures.

Exception hierarchy:
  PdfException (base)
    PdfTokenizerException  — lexical errors, with byte offset
    PdfParseException      — structural/semantic parse errors
    PdfWriterException     — output errors
    PdfFilterException     — filter encode/decode errors
    PdfFontException       — font parsing errors

Every exception carries enough context to locate the problem:
byte offset for parser errors, object ID for object-level errors,
page number for page-level errors.

Callers that need to handle malformed PDFs gracefully catch
PdfException and its subclasses. Catching Exception is not acceptable
in library code except at the outermost recovery boundary.

---

## B07 — Every Public Member Is Documented

Every public type, property, method, and constructor has an XML doc
comment with at minimum a <summary> element.

Every non-obvious parameter has a <param> element.
Every method that throws has a <exception> element for each type thrown.
Every method that returns a meaningful value has a <returns> element.

The NoWarn for CS1591 in Directory.Build.props is a temporary
development-phase suppression. It is removed before the 1.0.0 release.
From that point, an undocumented public member is a build error.

---

## B08 — Spec Citations in Implementation Comments

Every non-trivial implementation references the PDF 32000-1:2008
section that governs it. Format:

    // PDF 32000-1:2008 §7.3.5 — Name objects.

From Phase 1 delivery onward, every generated file includes a
header block:

    // SPEC:  PDF 32000-1:2008 §X.Y — Section name
    // PHASE: Phase N — Module name
    // [One-line summary of this file's role]

This makes the codebase navigable without the specification in hand
and makes it auditable against the specification.

---

## B09 — Tests Ship With the Code That Needs Them

No implementation file is considered complete without its tests.
Tests are delivered in the same batch as the implementation.

Test discipline:
  - Unit tests: every public method with non-trivial logic
  - Round-trip tests: parse → serialize → reparse must produce equivalent output
  - Corpus tests: every PDF in the corpus/ directory must parse without throwing
  - Performance tests: hot paths (tokenizer, DEFLATE) have benchmark baselines

A module is not "done" when the code compiles. It is done when the
tests pass on the CI matrix (Windows, macOS, Linux).

---

## B10 — The Deploy Workflow Is the Integration Point

Arun never manually edits files in the project.
Claude generates complete files — never snippets, never diffs.
Every generated file is registered in deploy.ps1 in the same batch.
The deploy script is the handoff mechanism between generation and
the local project.

Deploy script invariants:
  - CRLF line endings (Windows PowerShell 5.1 compatibility)
  - ASCII-only characters (no Unicode, no smart quotes)
  - Flat hashtable structure: filename -> destination path
  - Source folder: %USERPROFILE%\Downloads\chuvadi\
  - Script lives in repo root alongside Chuvadi.slnx

---

## B11 — Build Must Be Green Before Proceeding

A build with errors is a stop condition. The next generation batch
does not begin until the current batch produces a clean build.

Warnings are errors (TreatWarningsAsErrors). A "warning-only" build
is not a clean build.

Test failures are a stop condition. The next module does not begin
until the current module's tests pass.

The CI matrix (Windows, macOS, Linux) is the final arbiter. Local
green is necessary but not sufficient.

---

## B12 — Multiple Strategies Where the Trade-Off Is Real

Where PDF processing has genuinely different optimal approaches
for different use cases, Chuvadi exposes the choice explicitly:

  Text extraction: OperatorExtractor, LayoutExtractor, GlyphExtractor
  Output compression: per-stream filter choice
  xref format: classic table vs cross-reference stream
  Merge strategy: resource deduplication vs resource preservation

Sensible defaults ensure beginners get the right answer without choosing.
Named options ensure experts can pick without forking the library.

New options are not added speculatively. An option earns its existence
when a real use case that cannot be served by existing options is
identified and documented.

---

## B13 — Platform Neutrality

No Windows-specific APIs in src/ projects.
System.Drawing.Common is explicitly prohibited (Windows-only despite
the managed appearance).

All file I/O goes through System.IO abstractions, not P/Invoke.
All crypto goes through System.Security.Cryptography (BCL-provided,
cross-platform from .NET 5+).

The CI matrix builds and tests on Ubuntu and macOS precisely to
catch platform regressions before they reach NuGet.org.

Violation signal: any ifdef WIN32, RuntimeInformation.IsOSPlatform check,
or DllImport in src/ without a documented cross-platform fallback.

---

## B14 — Sustainability and Naming

The library is named Chuvadi (சுவடி), Tamil for palm-leaf manuscript.
The NuGet package root namespace is Chuvadi.Pdf.*.
The GitHub organisation/user hosting the repo is chuvadi (TBD — lock
before first public release).

No commercial tier, no dual licensing, no proprietary extensions.
Apache 2.0 for all code in this repository, forever.

Sustainability model: to be decided post-Phase-1. Options include
GitHub Sponsors, paid support contracts, or employer-sponsored open source.
The sustainability model does not affect the license or the code.

---

*End of BASELINE.md*
*Last updated: 2025-05-09 — A10, A11 decisions reflected.*
