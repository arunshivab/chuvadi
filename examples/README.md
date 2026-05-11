# Chuvadi Examples

Eight runnable example projects, one per major capability. Each example is
self-contained: its own `.csproj` referencing only the Chuvadi modules it
needs, a single `Program.cs` (typically 30–80 lines), and a `README.md`
explaining what it does and how to run it.

## Examples

| Example | What it does |
|---|---|
| [TextExtraction](Chuvadi.Examples.TextExtraction/README.md) | Extract text using both Operator and Layout strategies. |
| [Watermark](Chuvadi.Examples.Watermark/README.md) | Stamp a diagonal text watermark across every page. |
| [Redaction](Chuvadi.Examples.Redaction/README.md) | PHI-safe redaction with byte-level absence verification. |
| [Render](Chuvadi.Examples.Render/README.md) | Rasterize pages to PNG at any DPI. |
| [FormFill](Chuvadi.Examples.FormFill/README.md) | List and fill AcroForm fields. |
| [Outlines](Chuvadi.Examples.Outlines/README.md) | Print the document outline (bookmarks) tree. |
| [PageOps](Chuvadi.Examples.PageOps/README.md) | Merge, split, delete, and rotate pages. |
| [Annotations](Chuvadi.Examples.Annotations/README.md) | Read existing annotations and add sticky-notes + stamps. |

## Running an example

From the repository root:

```bash
dotnet run --project examples/Chuvadi.Examples.<Name> -- <args>
```

Every example prints usage when run without arguments.

## How they reference Chuvadi

Each example uses `ProjectReference` to point at the actual `src/Chuvadi.Pdf.*`
projects, not packaged NuGet builds. This means the examples build alongside
the library — any breaking API change shows up here immediately and CI catches
it before merge.

## Sample inputs

The examples don't bundle sample PDFs. Use any PDF you have locally, or
generate a minimal test PDF with the CLI:

```bash
dotnet run --project tools/Chuvadi.Pdf.Cli -- info <some.pdf>
```
