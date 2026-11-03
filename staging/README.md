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

To build: pack chuvadi-pdf into the feed first. In the chuvadi-pdf clone, run
`.\build\pack.ps1 -Version 3.22.1` and copy `artifacts\nupkg\*.nupkg` into the feed. Do not point
`-OutputDir` at the feed: `pack.ps1` deletes its output folder first. Then:

```powershell
dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests -c Release
```

Layout's staged projects pin Chuvadi.Pdf 3.30.2 in `src/Chuvadi.Layout.Pdf/ChuvadiPdf.props` and carry their own
per-project `nuget.config` (same local feed). Build them from the repository root:

```powershell
dotnet build staging/tests/Chuvadi.Layout.Pdf.Tests -c Release --no-incremental
dotnet test staging/tests/Chuvadi.Layout.Pdf.Tests -c Release --no-build
dotnet build staging/tests/Chuvadi.Layout.Acceptance.Tests -c Release --no-incremental
dotnet test staging/tests/Chuvadi.Layout.Acceptance.Tests -c Release --no-build
```
