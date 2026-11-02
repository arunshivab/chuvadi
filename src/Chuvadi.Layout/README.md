# Chuvadi.Layout

A pure-BCL page layout engine for .NET. It paginates flow documents (Word-style) and spreadsheet
grids (Excel-style) into fixed pages, emitting a neutral, immutable page display list. Its only
dependency is the Chuvadi family itself (Chuvadi.Sheets reads .xlsx); there is no PDF dependency.
PDF, SVG and PNG output ships separately as Chuvadi.Layout.Pdf.

Status: **0.1.0 (preview).** Word documents (Chuvadi.Docs 1.6.0), Excel workbooks (Chuvadi.Sheets 1.7.0) and plain
text; Markdown and HTML through Chuvadi.Layout.Markup. Limits and decisions: `docs/Chuvadi.Layout/DESIGN.md` in the
repository.

## Fonts

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;

var options = new LayoutOptions();
options.FontSources.Add(BundledFontSource.Instance);   // Chuvadi.Layout.Fonts package (OFL)
// options.AllowSystemFonts = true;                   // desktop viewers: use real Office fonts when installed

FontResolver fonts = options.CreateFontResolver();
FontResolution r = fonts.Resolve(new FontRequest("Calibri", Weight: 700));
// r.Face = Carlito Bold, r.Kind = MetricCompatibleSubstitute, r.IsMetricFaithful = true

FontResolution tamil = fonts.ResolveForCodepoint(r, 0x0BA4);   // → LiPi Sans Tamil (ScriptFallback)
foreach (FontSubstitution s in fonts.ReportedSubstitutions)
    Console.WriteLine($"{s.RequestedFamily} → {s.UsedFamily} ({s.Kind}, metric-compatible: {s.MetricCompatible})");

int mdw = r.Face.MaxDigitWidthPixels(11);   // Excel max digit width, 7 px for Calibri 11
```

## Word documents

```csharp
var options = new LayoutOptions();                     // options.Password = "..." for encrypted documents
options.FontSources.Add(BundledFontSource.Instance);
using LaidOutDocument doc = DocumentLayout.OpenDocx(File.OpenRead("report.docx"), null, options);
int pages = doc.ResolvePageCount();
LaidOutPage first = doc.GetPage(0);                     // display list, boxes, text; SVG with LayoutSvg
```

Laid out as Word does, from Chuvadi.Docs' effective formatting: sections, headers and footers, lists, paragraphs,
tabs and positional tabs, tables (spans, merges, header rows, nested), pictures inline and floating with wrapping,
text boxes, footnotes at the foot of their page and endnotes after the text, tracked changes as accepted, watermarks,
PAGE/NUMPAGES fields and the document's embedded fonts (`LayoutOptions.UseEmbeddedFonts`). What is approximated
or not yet laid out is reported in `doc.Warnings`.

## Excel workbooks

```csharp
var options = new LayoutOptions();                     // options.Password = "..." for encrypted workbooks
options.FontSources.Add(BundledFontSource.Instance);
using LaidOutDocument doc = DocumentLayout.OpenXlsx(File.OpenRead("charges.xlsx"),
    new XlsxImportOptions { DefaultPaperSizeCode = 9 }, options);   // A4 when the file states no paper
foreach (LaidOutPage page in doc.GetPages())
    Console.WriteLine($"{page.SourceName} page {page.PageLabel}");
foreach (LayoutWarning w in doc.Warnings)
    Console.WriteLine(w);                                  // e.g. ChartPlaceholder, FillApproximated
```

Every visible sheet prints in workbook order, as Excel's "Print entire workbook" does. The following
come from Chuvadi.Sheets and are printed as Excel shows them:
- cell text, with the number format rendered, including its [colour];
- the effective style: cell, then row, then column;
- merges, column widths and row heights;
- page setup, print areas and titles, and manual page breaks;
- conditional formats (fills, fonts, borders, number formats, data bars);
- pictures and shapes.

Charts are drawn as labelled placeholders. `DocumentLayout.Open(Stream)` sniffs .xlsx
automatically. `DocumentLayout.Open(Workbook)` takes a workbook you have already loaded.

## Guarantees

- **Memory-only.** Nothing is written to disk. Nothing is read from disk or the network unless you
  add a directory or system font source.
- **Untrusted input.** Malformed fonts throw `FontFormatException`. `LayoutLimits` bound the work.
- **The input is never modified.**
