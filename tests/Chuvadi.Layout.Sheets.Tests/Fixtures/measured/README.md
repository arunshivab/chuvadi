# Excel-measured workbooks (D-287, D-291)

Written for these tests (openpyxl), then opened in Excel 2021 and exported to PDF through COM
(`Workbooks.Open`, `ExportAsFixedFormat(0, …)`) by Arun on 2026-11-09. The page counts and first printed cells
asserted in `PrintedPagesTests` are read from those PDFs (A4 portrait, footer `Page &P of &N`).

| File | Sheets | What Excel showed |
|---|---|---|
| d287-blank-pages.xlsx | 5 | every page of the range is printed, blank ones too; a fill or a border extends the range, bold alone does not |
| d287-formats.xlsx | 15 | of the formats on an otherwise empty cell, only fill and border extend the range |
| d287-edges.xlsx | 5 | printing starts at A1; gridlines do not change which cells count |
| extent-a1-xfd1048576.xlsx | 1 | A1 and XFD1048576 (the Reader's case, D-282). Excel's export did not finish on a file like this |
| overflow-split.xlsx | 1 | long Latin and Tamil text overflowing across page edges; Excel prints 6 pages and cuts through the letter at each edge (saved as PDF by Arun on 2026-11-10). Layout keeps letters whole there by design (`WholeLettersTests`) |
