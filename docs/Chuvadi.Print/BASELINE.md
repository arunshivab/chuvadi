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
  `WindowsPrinterTarget` sends each page to GDI in bands of a few megabytes; `IppPrinterTarget` composes one
  sheet at a time and spools the PWG Raster stream to a temporary file, not memory.
- **P06 — One audit event per job** (AERB / NABH): exactly one `PrintAuditEvent` per `PrintTo` /
  `SubmitTo`, whether the job is printed, fails or is cancelled. Remotely (A6) the agent's event travels to
  the hub with the final status and the hub records it once into the host's sink; a job that ends on the
  hub (cancelled while queued, leases exhausted) or before printing on the agent (unknown printer, payload
  unusable, interrupted) gets exactly one synthesized event instead. Repeated final reports are ignored.
- **P07 — Agents dial out only.** No inbound port on the workstation. `PrintAgent` (A6) only makes
  outbound HTTPS requests to the `PrintHub` its host application embeds (long poll, payload download,
  status reports); the hub never connects to an agent. Agents authenticate with a per-agent bearer token
  (stored hashed on the hub, rotated without reinstalling, revocable at once); plain HTTP is refused
  except to loopback or when explicitly allowed. The IPP client (A4) likewise only makes outbound requests
  to printers; an IPP *server* (for the "Chuvadi PDF" product) lives outside the library and binds to
  loopback unless deliberately configured otherwise. *(Decision D-26.)*
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
  type, resolution and quality preset, and all three paper-matching modes; over IPP: every RFC 8010 value
  type, all three PWG Raster colour spaces, all four sheet-back modes, and both raster and document paths).

## Layer status

- **A1** — portable, pure-BCL foundation: settings, paper sizes, margins, page selection, alignment,
  spool envelope, audit. (0.1.0)
- **A2** — `PrintJob` orchestration: `IPageRasterizer` abstraction, page layout, print plan
  (copies, collation, duplex sheets, blank backs), print targets, pixel conversion, `ImageFileTarget`
  (PNG), spool envelope v2 with job metadata, auto-rotate. (0.2.0)
- **A3** — `Chuvadi.Print.Windows`: printer discovery (`WindowsPrinters`), driver
  capabilities, and `WindowsPrinterTarget` printing through the GDI spooler. (0.3.0)
- **A4** — `Chuvadi.Print.Ipp`: IPP encoder/decoder, `IppClient`, PWG Raster writer/reader,
  `IppPrinterTarget` (rendered pages as PWG Raster) and `IppDocumentTarget` (PDF passthrough via the new
  core `PrintJob.SubmitTo` / `IDocumentTarget`): driverless printing on any OS. (0.4.0)
- **A5 — Chuvadi.Print.Pdf** — separate package (`staging/Chuvadi.Print.Pdf`, tests in
  `staging/Chuvadi.Print.Pdf.Tests`): `PdfPageRasterizer` (`IPageRasterizer` over Chuvadi.Pdf.Rendering) and the
  vector-imposition `PdfFileTarget` (print-to-PDF via `SubmitTo`). It consumes the version-pinned Chuvadi.Pdf
  packages from the local feed through `staging/nuget.config`; it never references a source clone (D-29).
  It stays outside `Chuvadi.slnx` and CI, and outside the `src/` scanned by the pure-BCL check, until
  Chuvadi.Pdf moves into the monorepo or is on nuget.org (D-28). (0.1.0)
- **Product: "Chuvadi PDF" virtual printer** — separate product consuming the Chuvadi.Print nupkg
  (decision D-11): a Print Support App virtual printer on Windows 11, a "Microsoft Print to PDF" queue
  fallback on Windows 10, an IPP server later. No v3/v4 driver, port monitor or print processor.
- **A6 (this layer)** — `Chuvadi.Print.Remote` (decision D-26): `PrintHub`, a host-agnostic hub protocol
  handler the host app embeds (LiPi Connect is the reference host) behind any web stack; `PrintAgent`, which
  dials out, long-polls, downloads with resume and SHA-256 check, prints through any `IPrintTarget` /
  `IDocumentTarget` and reports progress and the outcome; per-agent tokens with rotation and revocation
  (`TokenAgentAuthenticator`); pluggable job storage (`IPrintJobStore`: durable `FileJobStore`,
  `InMemoryJobStore`); leases with expiry and retry limits; idempotent job ids; a crash-safe agent journal
  so a job is never printed twice. Wire protocol v1. (0.5.0)

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
- `--preview` also writes the pages an IPP printer would receive (decoded PWG Raster) under `07-ipp-raster`;
  `-- --ipp-printer <uri> [--ipp-pdf <file>] [--ipp-insecure]` sends a 6-job proof set (and a PDF) to a
  real IPP Everywhere printer.
- Remote (A6) groups run a real hub and agent over HTTP on localhost (`HubHttpListenerHost`, which can also
  cut a download short to prove resume) and in process (`PrintHubMessageHandler`), with a hand-moved clock
  (`ManualClock`) for lease expiry.
