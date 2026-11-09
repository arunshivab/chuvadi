# staging/

Projects that are ready but cannot join `Chuvadi.slnx` and CI yet.

| Project | Waiting for |
|---|---|
| `Chuvadi.Print.Pdf.Raster` (+ `Chuvadi.Print.Pdf.Raster.Tests`) | Chuvadi.Pdf moving into the monorepo, or Chuvadi.Pdf on nuget.org (D-28, D-29) |
| `src/Chuvadi.Layout.Pdf` (+ `tests/Chuvadi.Layout.Pdf.Tests`) | Chuvadi.Pdf moving into the monorepo (Layout D-42, end state A) |
| `tests/Chuvadi.Layout.Acceptance.Tests`, `samples/Chuvadi.Layout.Acceptance` | the same (corpus acceptance harness and its tests) |
| `samples/Chuvadi.Layout.Viewer` | the same (reference viewer: per-page SVG/PNG, index.html) |
| `samples/Chuvadi.Layout.ReferenceCorpus`, `tests/Chuvadi.Layout.ReferenceCorpus` | the same (corpus generator, and the corpus with its LibreOffice 24.2.7.2 references; D-40) |

The projects here consume the Chuvadi.Pdf **packages** from your local package folder. `staging/nuget.config`
sets it as `..\..\..\local-nuget`, relative to that file, so the folder `local-nuget` sits next to the folder
that holds your clones. For a clone or worktree at `C:\src\chuvadi-monorepo\chuvadi` it is
`C:\src\local-nuget`. The file applies only to projects under this folder. They never reference a chuvadi-pdf source clone (D-29).

The CI pure-BCL check scans `src/` and `shared/` only. These first-party PackageReferences live here, outside
its scope, until the move. Then they go to `src/` and `tests/` as ordinary projects
(see `docs/Chuvadi.Print/INTEGRATION-AFTER-PDF-MOVE.md`).

Chuvadi.Print.Pdf.Raster takes Chuvadi.Pdf from `Chuvadi.Print.Pdf.Raster/ChuvadiPdf.props`: the package references
the range `[3.31.2, 4.0.0)` (D-258), and its tests use exactly `ChuvadiPdfVersion` (3.31.2 by default). The feed needs
every Chuvadi.Pdf 3.31.2 package, plus Chuvadi.Fonts 1.0.0 or later. They are the assets of the chuvadi-pdf `v3.31.2`
GitHub release (with its SHA256SUMS.txt) and of our `fonts-v1.1.0` release. Then:

```powershell
dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests -c Release
```

Layout's staged projects take Chuvadi.Pdf 3.31.1 from `src/Chuvadi.Layout.Pdf/ChuvadiPdf.props`: the package
Chuvadi.Layout.Pdf references the range `[3.31.1, 4.0.0)`, and its tests and the acceptance harness use exactly 3.31.1.
All 21 Chuvadi.Pdf 3.31.1 packages (Svg included) must be in the local feed. They carry their own per-project
`nuget.config` (same local feed). Build them from the repository root:

```powershell
dotnet build staging/tests/Chuvadi.Layout.Pdf.Tests -c Release --no-incremental
dotnet test staging/tests/Chuvadi.Layout.Pdf.Tests -c Release --no-build
dotnet build staging/tests/Chuvadi.Layout.Acceptance.Tests -c Release --no-incremental
dotnet test staging/tests/Chuvadi.Layout.Acceptance.Tests -c Release --no-build
```
