# Chuvadi.Examples.TextExtraction

Extracts text from a PDF using both of Chuvadi's extraction strategies.

## Run

```bash
dotnet run --project examples/Chuvadi.Examples.TextExtraction -- path/to/your.pdf
```

## What it shows

- **Operator strategy** — walks text-showing operators in the order they appear
  in the content stream. Fastest. Preserves the raw operator sequence.
  Best for simple, single-column documents.

- **Layout strategy** — groups glyphs by position into words, lines, paragraphs.
  Handles multi-column layouts, tables, and mixed RTL/LTR text.

## Output

Both strategies print all pages. Compare the two outputs to see which strategy
better suits your documents.
