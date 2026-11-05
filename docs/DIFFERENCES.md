# Where Chuvadi differs from Word, Excel and other applications

Chuvadi follows the file and its standard. For Office formats that is ECMA-376 Part 1, 5th edition (2016). Where the
standard is silent, it follows what Word or Excel does, as measured. This page lists every point where a Chuvadi
library therefore does something different from Word, Excel or another common application **on purpose**.

Each entry gives:
- what the standard says;
- what the application does;
- what Chuvadi does;
- the decision that set it.

Points where Chuvadi simply matches the standard and the applications are not listed here.

Sections, one per library: Chuvadi.Layout first; the other libraries add their own sections.

## Chuvadi.Layout

Layout's full point-by-point account, including the points where it matches Word and Excel, is
[docs/Chuvadi.Layout/FILE-FIDELITY.md](Chuvadi.Layout/FILE-FIDELITY.md).

### L-1: Cell margins when nothing states them (.docx)

- **The standard:** when neither the table, its style nor the Table Normal style states cell margins, they are
  115 twips (5.75 pt) left and right (§17.4.34 start, §17.4.11 end), and no padding top and bottom (§17.4.75 top, and
  the matching bottom element).
- **Word 2021:** about 0 pt left and right in such a file, in both compatibility mode and mode 15 (A7 calibration D1,
  3 Nov 2026). With Word's own Table Normal style in the file (108 twips), 5.4 pt. LibreOffice uses 5.4 pt.
- **Chuvadi.Layout:** 5.75 pt left and right, 0 top and bottom. A margin stated anywhere, including a Table Normal
  style in the file, wins.
- **Effect:** text in such tables sits 0.35 pt further in than in LibreOffice and about 5.75 pt further in than in
  Word; in narrow cells a word can wrap differently.
- **Decision:** F3, approved 3 Nov 2026; built under D-181.

### L-2: Default column width without `defaultColWidth` (.xlsx)

- **The standard:** baseColWidth (default 8) × the maximum digit width + 5 px of padding (§18.3.1.81, §18.3.1.13).
  For Calibri 11 (MDW 7) that is 8 × 7 + 5 = 61 px; the standard's own example gives 61 pixels.
- **Excel 2021:** rounds the default column up to a multiple of 8 px: it shows 63 px and prints the equivalent of
  64 px for Calibri 11.
- **Chuvadi.Layout:** 61 px (45.75 pt), not rounded. A `defaultColWidth` or a column's own width in the file wins.
- **Effect:** every default-width column is 3 px (2.25 pt) narrower than in Excel. Text that just fits a default column
  in Excel can overflow into the next cell, or a number can show ####.
- **Decision:** W1, approved 3 Nov 2026; built under D-181.

### L-3: Workbooks whose font is Cambria, without Cambria installed (.xlsx)

- **The standard:** column widths come from the maximum digit width of the workbook's Normal font (§18.3.1.13).
- **Excel 2021:** uses real Cambria, whose digits are 554/1000 em: MDW 8 at 11 pt.
- **Chuvadi.Layout:** Cambria is not bundled; its substitute Caladea has narrower digits (MDW 7). Layout keeps a small
  table of real digit widths for such fonts, so the columns are as wide as the standard gives with real Cambria
  (MDW 8), while the text is drawn in Caladea. With Cambria installed and `AllowSystemFonts` on, Layout uses
  Cambria itself and the table does not apply.
- **Effect:** column widths and page breaks across match Excel; inside each cell the text is about 6.5% narrower than
  Excel draws it, so it rarely overflows where Excel's would.
- **Decision:** F7c, approved 3 Nov 2026; built under D-181.
