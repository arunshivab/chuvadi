# Chuvadi.Print — Baseline Invariants

Invariants the package must always satisfy. Changes to these are deliberate, documented decisions.

- **P01 — Zero third-party dependencies.** `src/Chuvadi.Print` declares no `<PackageReference>`. The only
  permitted first-party reference (added in a later layer) is `Chuvadi.Pdf` for rasterisation. The CI grep
  over `src/` and `shared/` enforces the no-third-party rule.
- **P02 — Bottom-up dependencies, no cycles.**
- **P03 — PDF is the interchange.** Rasterisation is delegated to `Chuvadi.Pdf.Rendering`; Print never
  re-implements it. (Arrives with the rendering layer.)
- **P04 — The wire payload is PDF + settings**, never pre-rendered bitmaps. The agent rasterises. The
  `SpoolEnvelope` is the canonical carrier.
- **P05 — Bounded memory.** Pages rasterise one at a time; memory does not scale with document size.
- **P06 — One audit event per job** (AERB / NABH).
- **P07 — Agents dial out only.** No inbound port on the workstation. (Arrives with the service layer.)
- **P08 — Portable core builds and tests on Linux.** Windows-only APIs are confined to the
  `Chuvadi.Print.Windows` namespace / `net10.0-windows` target. (Arrives with the Windows layer.)
- **P09 — Complete option coverage.** Every settings dimension exposes its full set of choices
  (all paper sizes, all orientations, all duplex modes, all colour modes, all scale modes, all nine
  content alignments, all page-selection styles).

## Layer status

- **A1 (this layer)** — portable, pure-BCL foundation: settings, paper sizes, margins, page selection,
  alignment, spool envelope, audit. Builds and tests on Linux and Windows. No external or Windows deps.
- A2 — `PrintJob` + rasterisation via `Chuvadi.Pdf` (pending Pdf in the monorepo).
- A3 — `Chuvadi.Print.Windows` GDI spooler (`net10.0-windows`).

## Build & test conventions (inherited from the monorepo)

- `TreatWarningsAsErrors=true`; the build must be warning-free.
- Tests: xUnit (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`),
  plain `Assert`. The shipped library never references these.
- The `samples/Chuvadi.Print.ManualTests` console exposes verification groups that the
  `tests/Chuvadi.Print.Tests` project drives as `[Fact]`s, and which also run end-to-end in CI.
