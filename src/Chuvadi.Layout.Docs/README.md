# Chuvadi.Layout.Docs

Word documents (.docx) for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi): reads them with Chuvadi.Docs and
lays them out the way Word lays them out. Pure BCL; works in memory; never alters a value.

```csharp
using Chuvadi.Layout;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Fonts;

var options = new LayoutOptions();                     // options.Password = "..." for encrypted documents
options.FontSources.Add(BundledFontSource.Instance);   // Office metric-compatible substitutes (Chuvadi.Layout.Fonts)
using LaidOutDocument doc = DocsLayout.Open(File.OpenRead("report.docx"), null, options);
int pages = doc.ResolvePageCount();
LaidOutPage first = doc.GetPage(0);                    // display list, boxes, text; SVG with LayoutSvg
```

| Member | What it does |
|---|---|
| `DocsLayout.Open(Stream / byte[], DocsImportOptions?, LayoutOptions?, ct)` | lays out a .docx (encrypted too, with `LayoutOptions.Password`) |
| `DocsLayout.Open(Chuvadi.Docs.Model.Document, …)` | lays out a document already loaded or built with Chuvadi.Docs |
| `DocsImporter.Import(…)` | builds the flow IR (`FlowDocument`) only, for callers that lay it out themselves |
| `DocsImportOptions` | headers and footers, pictures, watermarks, hidden text, auto paragraph spacing |

Laid out from Chuvadi.Docs' effective formatting: sections, headers and footers, lists, paragraphs, tabs and positional
tabs, tables, pictures inline and floating with wrapping, text boxes, footnotes and endnotes, tracked changes as
accepted, watermarks, PAGE/NUMPAGES/SECTIONPAGES fields and the document's embedded fonts. What is approximated or
not yet laid out is reported in `doc.Warnings`. For a file of unknown kind, use Chuvadi.Layout.Documents
(`AutoDetectLayout.Open`).

Layout follows the file and its standard (ECMA-376); where the standard is silent it follows Word 2021 as measured.
Point by point, including where Layout differs from Word or WPS Office on purpose (floating tables, columns):
[FILE-FIDELITY.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Layout/FILE-FIDELITY.md).
