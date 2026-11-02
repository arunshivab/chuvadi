# Chuvadi.Layout acceptance harness

This tool lays out every document in a corpus folder and compares the result with a reference PDF
printed from Word or Excel. It checks:

- the page count;
- the text on every page, which also fixes the page-break positions (tier A);
- where each line sits, within a tolerance.

It writes `acceptance-report.md` and `acceptance-report.tsv`.

The tool reads PDFs with chuvadi-pdf, taken as version-pinned packages from the local NuGet feed
(D-29: `staging/src/Chuvadi.Layout.Pdf/ChuvadiPdf.props` and the `nuget.config` next to the project). It is
not part of `Chuvadi.slnx` or CI.

## Corpus folder

- **Paired by name:** put each source next to its reference with the same name, such as
  `invoice.docx` with `invoice.pdf`, or `charges.xlsx` with `charges.pdf`.
- **Or a manifest:** add a `manifest.tsv` with one row per document. The fields are tab-separated and
  `#` starts a comment:

```
source	reference	printer	notes
charges.xlsx	charges (Excel 365, Microsoft Print to PDF).pdf	Microsoft Print to PDF	A4, default margins
letter.docx	letter.pdf	Save as PDF (Word 365)
```

- **Printer:** state the printer for Excel files. Excel's page breaks depend on the printer driver.
- **Formats:** `.docx`, `.xlsx`, `.txt` and `.md` files are compared (Markdown through Chuvadi.Layout.Markup). A source that cannot be read is an *error*, and an
  unexpected exception is reported as `CRASH` (the run continues).

## Run (PowerShell)

From the repository root:

```
dotnet build staging\samples\Chuvadi.Layout.Acceptance -c Release
dotnet run --project staging\samples\Chuvadi.Layout.Acceptance -c Release --no-build -- --self-test --out C:\Temp\layout-acceptance
dotnet run --project staging\samples\Chuvadi.Layout.Acceptance -c Release --no-build -- C:\Temp\layout-corpus
dotnet test staging\tests\Chuvadi.Layout.Acceptance.Tests -c Release
```

Options:

| Option | Meaning |
|---|---|
| `--out <folder>` | Where the reports go. The default is `<corpus>\_acceptance`. |
| `--tolerance <pt>` | Line position tolerance when the original fonts are used. The default is 2 pt. |
| `--substitute-tolerance <pt>` | Tolerance when a font was substituted. The default is 1% of the page. |
| `--system-fonts` | Use the fonts installed on the machine (`LayoutOptions.AllowSystemFonts`). Use this on Windows so Calibri, Cambria and Arial are the real fonts. |
| `--fonts <folder>` | Add a folder of font files. It can be given more than once. |
| `--geometry` | Fail the run (exit code 1) on geometry, not only on tier A. |
| `--layout-only` | Also lay out source files that have no reference PDF (status `laidout`: page count, timings, warnings), and ignore the manifest. Use it to check that a corpus opens and paginates without errors or crashes. |
| `--pixels [dpi]` | Also score each page by pixels: the reference page and Layout's page are both rasterized by Chuvadi.Pdf at the given DPI (default 96), in grey. A pixel differs when its grey value is more than 64 from the other image's pixel at the same place and at all eight neighbours; the score is the differing share of the content pixels. The report gains a "Pixels (worst page)" column. The score is informational: it does not fail a file. |
| `--pixel-threshold <percent>` | Pages scoring above this are named in the message (and written with `--images`). The default is 10. |
| `--images <folder>` | Write the reference and layout rasters (PGM) of every page above the threshold, for looking at the difference. |
| `--dump-lines <source> <reference.pdf> [page]` | Diagnostics, instead of a corpus run: print the reference's lines (R) and Layout's matching lines (L) of one page with baseline, left edge and text, then the layout warnings. |

## Reading the report

- **Tier A:** the page count matches and every page has the same characters as the reference. Spaces,
  ligatures and soft hyphens are ignored, and a run of four or more leader characters (`.`, `-`, `_`, `·`)
  counts as three, because writers fill tab leaders to slightly different lengths. A failure names the first page whose text differs, with
  context.
- **Geometry:** every reference line was found, and its baseline and left edge are within the
  tolerance. A failure lists, per page, the lines beyond the tolerance and the line with the largest
  offset.
- **Lines matched:** the share of the reference's lines that Layout has too, with the same text (longest common
  subsequence over each page's lines). It shows how close a file is when tier A fails.
- **Pixels (worst page):** with `--pixels`, the worst page's pixel score (see the flag above).
- **Fonts:** `substituted` means at least one requested font was replaced, for example Calibri by
  Carlito without `--system-fonts`.

## Reference corpus

`staging/tests/Chuvadi.Layout.ReferenceCorpus` holds a generated corpus with LibreOffice references (D-40); see its
REFERENCE-CORPUS.md. Run it with:

```powershell
dotnet run --project staging\samples\Chuvadi.Layout.Acceptance -c Release -- staging\tests\Chuvadi.Layout.ReferenceCorpus\corpus --pixels 96 --out C:\Temp\reference-corpus
```
