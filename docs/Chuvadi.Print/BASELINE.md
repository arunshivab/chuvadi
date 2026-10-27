# Chuvadi.Print — Baseline Invariants

Invariants the package must always satisfy. Changes to these are deliberate, documented decisions.

- **P01 — Zero dependencies.** `src/Chuvadi.Print` declares no `<PackageReference>` and no
  `<ProjectReference>`. It does not reference Chuvadi.Pdf: rasterisation is reached through the
  `IPageRasterizer` abstraction, and the PDF implementation ships as the separate thin adapter package
  **Chuvadi.Print.Pdf**. The CI grep over `src/` and `shared/` enforces the no-third-party rule.
  Print does not use `shared/Chuvadi.Internal`.
- **P02 — Bottom-up dependencies, no cycles.** Chuvadi.Print.Pdf depends on Chuvadi.Print and
  Chuvadi.Pdf; nothing depends back on it.
- **P03 — PDF is the interchange; Print never rasterises.** Turning PDF into pixels is delegated to an
  `IPageRasterizer` (Chuvadi.Pdf.Rendering via Chuvadi.Print.Pdf). Print only lays out, plans and
  re-encodes pixels (colour to gray to black-and-white).
- **P04 — The wire payload is PDF + settings + job metadata**, never pre-rendered bitmaps. The agent
  rasterises. The `SpoolEnvelope` is the canonical carrier; every new format version still reads the
  previous ones.
- **P05 — Bounded memory.** Pages rasterise one at a time and are released before the next; memory does
  not scale with document size. `ImageFileTarget` holds one sheet image at a time.
- **P06 — One audit event per job** (AERB / NABH): exactly one `PrintAuditEvent` per `PrintTo`, whether
  the job is printed, fails or is cancelled.
- **P07 — Agents dial out only.** No inbound port on the workstation. (Arrives with the service layer.)
- **P08 — Portable core builds and tests on Linux.** Windows-only APIs are confined to the
  `Chuvadi.Print.Windows` namespace / `net10.0-windows` target. (Arrives with the Windows layer.)
- **P09 — Complete option coverage.** Every settings dimension exposes its full set of choices
  (all paper sizes, all orientations, all duplex modes, all colour modes, all scale modes, all nine
  content alignments, all page-selection styles, both margin references, all three monochrome
  conversions, all copy-handling modes, all four rotations).

## Layer status

- **A1** — portable, pure-BCL foundation: settings, paper sizes, margins, page selection, alignment,
  spool envelope, audit. (0.1.0)
- **A2 (this layer)** — `PrintJob` orchestration: `IPageRasterizer` abstraction, page layout, print plan
  (copies, collation, duplex sheets, blank backs), print targets, pixel conversion, `ImageFileTarget`
  (PNG), spool envelope v2 with job metadata, auto-rotate. (0.2.0)
- **Chuvadi.Print.Pdf** — thin adapter implementing `IPageRasterizer` over Chuvadi.Pdf (separate package,
  once Chuvadi.Pdf is ready).
- **A3** — `Chuvadi.Print.Windows` GDI spooler (`net10.0-windows`), implementing `IPrintTarget`.

## Build & test conventions (inherited from the monorepo)

- `TreatWarningsAsErrors=true`; the build must be warning-free.
- Tests: xUnit (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`),
  plain `Assert`. The shipped library never references these.
- The `samples/Chuvadi.Print.ManualTests` console exposes verification groups that the
  `tests/Chuvadi.Print.Tests` project drives as `[Fact]`s, and which also run end-to-end in CI.
  Test doubles (`FakeRasterizer`, `RecordingTarget`, `MemoryAuditSink`) live there too.
- `dotnet run --project samples/Chuvadi.Print.ManualTests -- --preview <folder>` additionally writes PNG
  sheets (alignment, scale, orientation, colour, duplex, margins) for visual verification.
