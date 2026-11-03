# Chuvadi.Print.Pdf.Raster — changes once Chuvadi.Pdf joins the monorepo (D-28, D-29)

This file is for the integrator who moves chuvadi-pdf into the monorepo.

## Today

Chuvadi.Print.Pdf.Raster and its tests live in `staging/`:
- `staging/Chuvadi.Print.Pdf.Raster`
- `staging/Chuvadi.Print.Pdf.Raster.Tests`

They stay outside `Chuvadi.slnx` and CI for now. They consume version-pinned `PackageReference`s to the
Chuvadi.Pdf packages:
- The packages are `Chuvadi.Pdf.Rendering` and `Chuvadi.Pdf.Operations`.
- The version is `ChuvadiPdfVersion`, currently 3.22.1.
- They come from your local package folder, `..\..\..\local-nuget` relative to `staging/nuget.config` (see `staging/README.md`).

No project references a chuvadi-pdf source clone (D-29).

`staging/` is deliberately outside `src/`, which the CI pure-BCL grep scans.

## Two possible end states

- **A. Chuvadi.Pdf source moves into the monorepo** (`src/Chuvadi.Pdf.*`). The PackageReferences become
  ProjectReferences, and the adapter moves to `src/` and `tests/`. Section 1 below.
- **B. Chuvadi.Pdf is published to nuget.org, but its source stays in its own repo.** The PackageReferences stay
  as they are, and the adapter moves to `src/` and `tests/`. CI then needs the first-party allowlist. Section 2
  below.

**Assumption for A.** The Chuvadi.Pdf projects land at `src/Chuvadi.Pdf.<Name>/Chuvadi.Pdf.<Name>.csproj`
with their current names. If they land elsewhere, only the two `ProjectReference` paths in section 1.4 change.

---

## 1. End state A: Chuvadi.Pdf source in the monorepo

### 1.1 Moves (Print chat, one PR, right after the move)

```powershell
git mv staging/Chuvadi.Print.Pdf.Raster src/Chuvadi.Print.Pdf.Raster
git mv staging/Chuvadi.Print.Pdf.Raster.Tests tests/Chuvadi.Print.Pdf.Raster.Tests
git rm staging/nuget.config staging/README.md
```

### 1.2 `Chuvadi.slnx` (shared)

Add to the `/src/` folder, after `src/Chuvadi.Print/Chuvadi.Print.csproj`:
```xml
    <Project Path="src/Chuvadi.Print.Pdf.Raster/Chuvadi.Print.Pdf.Raster.csproj" />
```
Add to the `/tests/` folder, after `tests/Chuvadi.Print.Tests/Chuvadi.Print.Tests.csproj`:
```xml
    <Project Path="tests/Chuvadi.Print.Pdf.Raster.Tests/Chuvadi.Print.Pdf.Raster.Tests.csproj" />
```

### 1.3 `.github/workflows/ci.yml` (shared)

After the step "Unit tests — Chuvadi.Print (xUnit)":
```yaml
      - name: Unit tests — Chuvadi.Print.Pdf.Raster (xUnit)
        run: dotnet test tests/Chuvadi.Print.Pdf.Raster.Tests -c Release --no-build --logger "trx;LogFileName=print-pdf-raster.trx"
```
In "Pack packages", after the `Chuvadi.Print` line:
```yaml
          dotnet pack src/Chuvadi.Print.Pdf.Raster/Chuvadi.Print.Pdf.Raster.csproj -c Release --no-build
```
In "Upload package artifacts" `path:`, add:
```yaml
            artifacts/bin/Chuvadi.Print.Pdf.Raster/Release/*.nupkg
```
The pure-BCL grep needs no change in end state A, because only ProjectReferences remain.

### 1.4 `src/Chuvadi.Print.Pdf.Raster/Chuvadi.Print.Pdf.Raster.csproj` (Print-owned, whole file)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Chuvadi.Print.Pdf.Raster: the adapter between Chuvadi.Print and Chuvadi.Pdf (BASELINE P01/P02, decisions D-14, D-28, D-29).
       First-party ProjectReferences only; no PackageReference (P01). -->

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <RootNamespace>Chuvadi.Print.Pdf.Raster</RootNamespace>
    <AssemblyName>Chuvadi.Print.Pdf.Raster</AssemblyName>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <Product>Chuvadi.Print.Pdf.Raster</Product>
    <Description>Connects Chuvadi.Print to the Chuvadi PDF library: PdfPageRasterizer renders PDF pages for any Chuvadi.Print target (Windows printers, IPP, PNG preview).</Description>
    <Authors>Arun</Authors>
    <Company>Chuvadi</Company>
    <Version>1.0.0</Version>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/arunshivab/chuvadi</RepositoryUrl>
    <PackageTags>print;printing;pdf;rasterizer</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Chuvadi.Print\Chuvadi.Print.csproj" />
    <ProjectReference Include="..\Chuvadi.Pdf.Rendering\Chuvadi.Pdf.Rendering.csproj" />
    <ProjectReference Include="..\Chuvadi.Pdf.Operations\Chuvadi.Pdf.Operations.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Chuvadi.Print.Pdf.Raster.Tests" />
    <None Include="$(MSBuildThisFileDirectory)README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

</Project>
```
In `tests/Chuvadi.Print.Pdf.Raster.Tests/Chuvadi.Print.Pdf.Raster.Tests.csproj`, the ProjectReference paths become:
- `..\..\src\Chuvadi.Print\Chuvadi.Print.csproj`
- `..\..\src\Chuvadi.Print.Pdf.Raster\Chuvadi.Print.Pdf.Raster.csproj`
- `..\..\samples\Chuvadi.Print.ManualTests\Chuvadi.Print.ManualTests.csproj`

These are the same relative depth as today. Also drop its header comment about `staging/`.

### 1.5 `Directory.Build.props` (shared): caution

chuvadi-pdf's own `Directory.Build.props` turns on `EnforceCodeStyleInBuild`, `AnalysisLevel latest` and its
`.editorconfig` IDE rules (no `var`, braces everywhere, and so on). Print, Docs and Sheets are not written to
those rules.

**Keep those settings scoped to the `Chuvadi.Pdf.*` projects.** For example, use a props file that applies only
to them and imports the root one. Do not merge them into the root file.

Each Chuvadi.Pdf project also needs its real `Version` in the monorepo. chuvadi-pdf sets one only when the
build passes it, and otherwise defaults to 0.1.0. Without real versions, `dotnet pack` of Chuvadi.Print.Pdf.Raster
writes dependencies on Chuvadi.Pdf.Rendering / Operations 0.1.0.

---

## 2. End state B: Chuvadi.Pdf on nuget.org, source stays separate

1. **Move the projects:** `git mv` the two projects to `src/` and `tests/` as in 1.1.
2. **Package references:** keep the PackageReferences and `ChuvadiPdfVersion`, set to the version published on
   nuget.org. Delete `staging/nuget.config`, since nuget.org is the default source. Each project's
   ProjectReference to `src/Chuvadi.Print` becomes `..\Chuvadi.Print\Chuvadi.Print.csproj` (adapter) and
   `..\..\src\Chuvadi.Print\...` (tests).
3. **`Chuvadi.slnx`, `ci.yml`, `release.yml`:** the same additions as 1.2, 1.3 and section 3.
4. **CI pure-BCL grep (shared):** replace it with the first-party allowlist from D-28. It still bans every
   third-party package:
   ```yaml
      - name: Enforce pure-BCL libraries (only first-party Chuvadi.* packages)
        shell: bash
        run: |
          BAD=$(grep -R -n --include="*.csproj" --include="*.props" --include="*.targets" "<PackageReference" src/ shared/ | grep -v 'Include="Chuvadi\.' || true)
          if [ -n "$BAD" ]; then
            echo "$BAD"
            echo "::error::Shipped libraries must remain pure-BCL — only first-party Chuvadi.* PackageReferences are allowed under src/ or shared/."
            exit 1
          fi
   ```

---

## 3. `.github/workflows/release.yml` (shared, both end states)

- **Trigger** (`on.push.tags`): add `"print-pdf-raster-v*"`:
  ```yaml
      tags: ["sheets-v*", "docs-v*", "print-v*", "print-pdf-raster-v*"]
  ```
- **Tag prefix**: in "Resolve package from tag prefix", add this line before the `print-v*` line.
  `print-v*` does not match `print-pdf-raster-v…`, but listing the longer prefix first keeps it clear.
  ```bash
            print-pdf-raster-v*) NAME="Chuvadi.Print.Pdf.Raster" ;;
  ```
- **Build + test**: in "Build + test everything", after the `Chuvadi.Print.Tests` line:
  ```bash
          dotnet test tests/Chuvadi.Print.Pdf.Raster.Tests -c Release --no-build
  ```
- **Pack**: the existing line (`src/<name>/<name>.csproj`) works unchanged.
- **Tag**: `git tag -s print-pdf-raster-v1.0.0 -m "Chuvadi.Print.Pdf.Raster 1.0.0"`.

## 4. Root `README.md` (shared)

The package table (Package | Formats | Status) has no Print rows yet:
```markdown
| **[Chuvadi.Print](src/Chuvadi.Print/README.md)** | printing: Windows (GDI), IPP, PNG preview, remote hub/agent | v1.0.0 |
| **[Chuvadi.Print.Pdf.Raster](src/Chuvadi.Print.Pdf.Raster/README.md)** | PDF rendering for Chuvadi.Print (over Chuvadi.Pdf) | v1.0.0 |
```
The Print row can go in as soon as `print-v1.0.0` is tagged. Use the versions actually released.

## 5. Print-owned follow-ups (Print chat, with 1.1)

- **Docs:** update `BASELINE.md` (the A5 line), `GUIDE.md` §5 and §8, the Chuvadi.Print.Pdf.Raster README ("Building"),
  `PRINTING-REPORTS.md` (the test path) and `WINDOWS-VERIFICATION.md` (step 2 and the `staging/` paths).
- **Gate:** build `Chuvadi.slnx` and run all suites, including `tests/Chuvadi.Print.Pdf.Raster.Tests` (16 tests).
