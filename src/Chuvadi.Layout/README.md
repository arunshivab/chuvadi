# Chuvadi.Layout

A pure-BCL page layout engine for .NET. It paginates flow documents (Word-style) and spreadsheet
grids (Excel-style) into fixed pages, emitting a neutral, immutable page display list, and lays out plain text. Its
only dependency is Chuvadi.Fonts (the built-in LiPi Sans fonts); there is no PDF dependency. The importers are their
own packages, so an app takes only what it needs:

| Package | Lays out |
|---|---|
| Chuvadi.Layout.Docs | Word documents (.docx), through Chuvadi.Docs: `DocsLayout.Open` |
| Chuvadi.Layout.Sheets | Excel workbooks (.xlsx), through Chuvadi.Sheets: `SheetsLayout.Open` |
| Chuvadi.Layout.Documents | any of these, format detected: `AutoDetectLayout.Open` |
| Chuvadi.Layout.Markup | Markdown and sanitized HTML, through Chuvadi.Markup |
| Chuvadi.Layout.Fonts | the substitute fonts (metric-compatible Liberation and Carlito; Caladea for Cambria; Gelasio, Selawik), a pure data pack that Pdf and Print can use too; added with `FontPackSource.For(SubstituteFontPack.Instance)` |

PDF, SVG and PNG output ships separately as Chuvadi.Layout.Pdf; SVG is also in this package (`LayoutSvg`).

Status: **0.1.0 (preview).** Word documents (Chuvadi.Docs 1.6.0), Excel workbooks (Chuvadi.Sheets 1.7.0) and plain
text; Markdown and HTML through Chuvadi.Layout.Markup. Limits and decisions: `docs/Chuvadi.Layout/DESIGN.md` in the
repository.

## Fonts

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;

var options = new LayoutOptions();
options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));   // Chuvadi.Layout.Fonts package (OFL): metric-compatible substitutes
// options.AllowSystemFonts = true;                   // desktop viewers: use real Office fonts when installed
// LiPi Sans (Latin and nine Indic scripts) is built in (Chuvadi.Fonts); options.UseBuiltInFonts = false turns it off.

FontResolver fonts = options.CreateFontResolver();
FontResolution r = fonts.Resolve(new FontRequest("Calibri", Weight: 700));
// r.Face = Carlito Bold, r.Kind = MetricCompatibleSubstitute, r.IsMetricFaithful = true

FontResolution tamil = fonts.ResolveForCodepoint(r, 0x0BA4);   // → LiPi Sans Tamil (ScriptFallback)
foreach (FontSubstitution s in fonts.ReportedSubstitutions)
    Console.WriteLine($"{s.RequestedFamily} → {s.UsedFamily} ({s.Kind}, metric-compatible: {s.MetricCompatible})");

int mdw = r.Face.MaxDigitWidthPixels(11);   // Excel max digit width, 7 px for Calibri 11
```

## Word documents (Chuvadi.Layout.Docs)

```csharp
using Chuvadi.Layout.Docs;

var options = new LayoutOptions();                     // options.Password = "..." for encrypted documents
options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
using LaidOutDocument doc = DocsLayout.Open(File.OpenRead("report.docx"), null, options);
int pages = doc.ResolvePageCount();
LaidOutPage first = doc.GetPage(0);                     // display list, boxes, text; SVG with LayoutSvg
```

Laid out as Word does, from Chuvadi.Docs' effective formatting: sections, headers and footers, lists, paragraphs,
tabs and positional tabs, tables (spans, merges, header rows, nested), pictures inline and floating with wrapping,
text boxes, footnotes at the foot of their page and endnotes after the text, tracked changes as accepted, watermarks,
PAGE/NUMPAGES fields and the document's embedded fonts (`LayoutOptions.UseEmbeddedFonts`). What is approximated
or not yet laid out is reported in `doc.Warnings`.

## Excel workbooks (Chuvadi.Layout.Sheets)

```csharp
using Chuvadi.Layout.Sheets;

var options = new LayoutOptions();                     // options.Password = "..." for encrypted workbooks
options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
using LaidOutDocument doc = SheetsLayout.Open(File.OpenRead("charges.xlsx"),
    new SheetsImportOptions { DefaultPaperSizeCode = 9 }, options);   // A4 when the file states no paper
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

Charts are drawn as labelled placeholders. `SheetsLayout.Open(Workbook)` takes a workbook you have already loaded.

## A file of unknown kind (Chuvadi.Layout.Documents)

```csharp
using Chuvadi.Layout.Documents;

using LaidOutDocument doc = AutoDetectLayout.Open(stream, LayoutSourceFormat.Auto, options);   // docx, xlsx, text
```

The format is sniffed from the content; with `options.Password` an encrypted file is decrypted and laid out as a
document or a workbook, whichever it is. The engine's own `DocumentLayout.Open(Stream)` lays out plain text and, for a
docx, an xlsx or an encrypted file, throws `LayoutFormatException` naming the package to use.

## Guarantees

- **Memory-only.** Nothing is written to disk. Nothing is read from disk or the network unless you
  add a directory or system font source.
- **Untrusted input.** Malformed fonts throw `FontFormatException`. `LayoutLimits` bound the work.
- **The input is never modified.**
