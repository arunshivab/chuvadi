# Chuvadi.Layout.Sheets

Excel workbooks (.xlsx) for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi): reads them with Chuvadi.Sheets
and lays them out the way Excel prints them. Pure BCL; works in memory; values are never recalculated or altered.

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Sheets;

var options = new LayoutOptions();                     // options.Password = "..." for encrypted workbooks
options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));   // metric-compatible substitutes (Chuvadi.Layout.Fonts)
using LaidOutDocument doc = SheetsLayout.Open(File.OpenRead("charges.xlsx"),
    new SheetsImportOptions { DefaultPaperSizeCode = 9 }, options);   // A4 when the file states no paper
foreach (LaidOutPage page in doc.GetPages())
    Console.WriteLine($"{page.SourceName} page {page.PageLabel}");
```

| Member | What it does |
|---|---|
| `SheetsLayout.Open(Stream / byte[], SheetsImportOptions?, LayoutOptions?, ct)` | lays out an .xlsx (encrypted too, with `LayoutOptions.Password`) |
| `SheetsLayout.Open(Chuvadi.Sheets.Model.Workbook, …)` | lays out a workbook already loaded (load it with `IncludeStyledEmptyCells = true`) |
| `SheetsImporter.Import(…)` | builds the grid IR (`GridWorkbook`) only |
| `SheetsImportOptions` | sheets, hidden sheets, default paper, conditional formats, today, display culture, images, black and white, draft |

Every visible sheet prints in workbook order, as Excel's "Print entire workbook" does: displayed cell text with the
number format rendered, the effective style, merges, widths and heights, page setup, print areas and titles, manual
page breaks, conditional formats, pictures and shapes. Charts are drawn as labelled placeholders. For a file of
unknown kind, use Chuvadi.Layout.Documents (`AutoDetectLayout.Open`).

A file that is not a readable workbook raises `LayoutFormatException`, with Chuvadi.Sheets' `XlsxFormatException`
inside; that includes a damaged workbook whose workbook or worksheet part is missing, or whose cells point at shared
strings that do not exist (rejected since Chuvadi.Sheets 2.0.2). A `LayoutLimits` value exceeded raises
`LayoutLimitExceededException`.

A workbook opens at the cost of its page bands, not its pages, so even a sheet that reaches XFD1048576 opens at once
and `PageCount` is exact. `LayoutLimits.MaxPages` applies when pages are built, exported or printed; the exception's
`Location` names the sheet and printed range (D-291). Without a print area a sheet prints, as Excel does, from A1 to
its last value, fill or border; formatting alone on empty cells doesn't widen it.
