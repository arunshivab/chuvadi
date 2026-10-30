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
  re-encodes pixels (colour to gray to black-and-white, raster to device bitmap).
- **P04 — The wire payload is PDF + settings + job metadata**, never pre-rendered bitmaps. The agent
  rasterises. The `SpoolEnvelope` is the canonical carrier; every new format version still reads the
  previous ones.
- **P05 — Bounded memory.** Pages rasterise one at a time and are released before the next; memory does
  not scale with document size. `ImageFileTarget` holds one sheet image at a time;
  `WindowsPrinterTarget` sends each page to GDI in bands of a few megabytes.
- **P06 — One audit event per job** (AERB / NABH): exactly one `PrintAuditEvent` per `PrintTo`, whether
  the job is printed, fails or is cancelled.
- **P07 — Agents dial out only.** No inbound port on the workstation. (Arrives with the service layer.)
- **P08 — Portable core builds and tests on Linux; Windows code is namespace-confined.**
  *(Amended 2026-10-28, decision D-12.)* `Chuvadi.Print` is a single plain `net10.0` assembly. All
  Windows-specific code lives in the `Chuvadi.Print.Windows` namespace, is marked
  `[SupportedOSPlatform("windows")]` (so the platform analyzer flags unguarded use) and throws
  `PlatformNotSupportedException` elsewhere. It reaches the spooler and GDI only through P/Invoke to
  operating-system DLLs (`winspool.drv`, `gdi32.dll`); no native binaries ship and no
  `net10.0-windows` target is used. The DEVMODE mapping, paper matching, device geometry and bitmap
  banding are pure code and are unit-tested on Linux; the spooler path is tested on Windows (CI's
  Windows job prints through "Microsoft Print to PDF"; the maintainer verifies on real printers).
  *Previously:* "confined to the `Chuvadi.Print.Windows` namespace / `net10.0-windows` target".
- **P09 — Complete option coverage.** Every settings dimension exposes its full set of choices
  (all paper sizes, all orientations, all duplex modes, all colour modes, all scale modes, all nine
  content alignments, all page-selection styles, both margin references, all three monochrome
  conversions, all copy-handling modes, all four rotations; on Windows: every driver paper, tray, media
  type, resolution and quality preset, and all three paper-matching modes).

## Layer status

- **A1** — portable, pure-BCL foundation: settings, paper sizes, margins, page selection, alignment,
  spool envelope, audit. (0.1.0)
- **A2** — `PrintJob` orchestration: `IPageRasterizer` abstraction, page layout, print plan
  (copies, collation, duplex sheets, blank backs), print targets, pixel conversion, `ImageFileTarget`
  (PNG), spool envelope v2 with job metadata, auto-rotate. (0.2.0)
- **A3 (this layer)** — `Chuvadi.Print.Windows`: printer discovery (`WindowsPrinters`), driver
  capabilities, and `WindowsPrinterTarget` printing through the GDI spooler. (0.3.0)
- **A4** — IPP encoder/decoder and `IppPrinterTarget`: driverless printing to network printers on any OS.
- **A5 — Chuvadi.Print.Pdf** — separate package: `IPageRasterizer` over Chuvadi.Pdf and a vector-imposition
  `PdfFileTarget` (print-to-PDF), once Chuvadi.Pdf is buildable with this repo.
- **Product: "Chuvadi PDF" virtual printer** — separate product consuming the Chuvadi.Print nupkg
  (decision D-11): a Print Support App virtual printer on Windows 11, a "Microsoft Print to PDF" queue
  fallback on Windows 10, an IPP server later. No v3/v4 driver, port monitor or print processor.
- **A6** — print agent / service layer (P07).

## Build & test conventions (inherited from the monorepo)

- `TreatWarningsAsErrors=true`; the build must be warning-free.
- Tests: xUnit (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`),
  plain `Assert`. The shipped library never references these.
- The `samples/Chuvadi.Print.ManualTests` console exposes verification groups that the
  `tests/Chuvadi.Print.Tests` project drives as `[Fact]`s, and which also run end-to-end in CI.
  Groups that do not apply on the current machine (Windows-only on Linux) report SKIP.
  Test doubles (`FakeRasterizer`, `RecordingTarget`, `MemoryAuditSink`) live there too.
- `dotnet run --project samples/Chuvadi.Print.ManualTests -- --preview <folder>` additionally writes PNG
  sheets (alignment, scale, orientation, colour, duplex, margins) for visual verification.
- `-- --windows-printer "<name>" [--windows-output <folder>]` additionally prints an 11-job proof set
  to a Windows printer (or, with an output folder, to one file per job).
