# Chuvadi.Examples.PageOps

Page-level operations: merge, split, delete, rotate.

## Run

```bash
# Merge multiple PDFs into one
dotnet run --project examples/Chuvadi.Examples.PageOps -- \
  merge merged.pdf chapter1.pdf chapter2.pdf chapter3.pdf

# Split a multi-page PDF into one-page PDFs
dotnet run --project examples/Chuvadi.Examples.PageOps -- \
  split input.pdf pages/

# Delete pages by zero-based index
dotnet run --project examples/Chuvadi.Examples.PageOps -- \
  delete input.pdf trimmed.pdf 0,3,7

# Rotate a single page (degrees: 90, 180, 270)
dotnet run --project examples/Chuvadi.Examples.PageOps -- \
  rotate input.pdf rotated.pdf 2 90
```

## What it shows

- `PageOperations.Merge(stream, params PdfDocument[])` — concatenate documents.
- `PageOperations.SplitPages(document)` — returns one `MemoryStream` per page.
- `PageOperations.DeletePages(stream, document, int[])` — remove pages by index.
- `PageOperations.RotatePages(stream, document, int[], degrees)` — rotate pages.

All operations write a new PDF without mutating the input.
