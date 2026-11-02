# staging/

Projects that are ready but cannot join `Chuvadi.slnx` and CI yet.

| Project | Waiting for |
|---|---|
| `Chuvadi.Print.Pdf` (+ `Chuvadi.Print.Pdf.Tests`) | Chuvadi.Pdf moving into the monorepo, or Chuvadi.Pdf on nuget.org (D-28, D-29) |

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
dotnet test staging/Chuvadi.Print.Pdf.Tests -c Release
```
