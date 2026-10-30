# Chuvadi.Docs.Conversion

Conversions for [Chuvadi.Docs](../Chuvadi.Docs/README.md) documents: **plain text**, **Markdown** and **HTML**, both ways, plus a self-contained **HTML preview**.

- **Markdown** (CommonMark with GFM tables, task lists, strikethrough and autolinks) is parsed and written with Chuvadi.Markup. It is read exactly as cmark-gfm reads it, and what is written parses back to the same structure.
- **HTML input** is parsed the way a browser parses it, then passed through Chuvadi.Markup's allowlist sanitizer before anything reaches the document model.
- **Pure BCL:** the only dependencies are Chuvadi.Docs and Chuvadi.Markup from the same monorepo.

```csharp
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;

// Markdown → docx
var doc = MarkdownConversion.Import(File.ReadAllText("README.md"));
doc.SaveTo("README.docx");

// docx → Markdown, HTML, text
var report = Document.Load("report.docx");
string md   = MarkdownConversion.Export(report);
string page = HtmlConversion.Export(report);          // a complete page
string text = TextConversion.Export(report);

// HTML (untrusted) → docx, with a report of what was left out
var result = HtmlConversion.ImportDetailed(html);
result.Document.SaveTo("mail.docx");
foreach (var url in result.BlockedResources) Console.WriteLine("blocked: " + url);

// A safe preview of an attachment (for example in Anjal)
string preview = HtmlConversion.ExportPreview(Document.Load(attachmentStream));
```

## What maps to what

| Document | Markdown | HTML |
|---|---|---|
| Heading 1–6 (Title = 1; 7–9 = 6; custom styles by outline level) | `#` … `######` | `h1` … `h6` |
| Paragraph, alignment, left indent | paragraph | `p` with `text-align` / `margin-left` |
| Bold, italic, underline, strike, sub/superscript | `**`, `*`, `<u>`*, `~~`, `<sub>`/`<sup>`* | `strong`, `em`, `u`, `s`, `sub`, `sup` |
| Colour, highlight/shading, size, font, caps | — | inline `style` |
| Monospace text / Code style | `` `code` `` / fenced code | `code` / `pre` |
| Quote style | `>` | `blockquote` |
| Bullet and numbered lists (nested, start numbers) | `-` / `1.` | `ul` / `ol start` (or Word's exact labels) |
| Task items | `- [x]` ↔ ☒ / ☐ | — |
| Tables (header row, colspan, rowspan, alignment, shading) | GFM tables | `table` with `th`, `colspan`, `rowspan` |
| Hyperlinks, bookmarks, internal links, HYPERLINK fields | `[text](url)`, `#anchor` | `a href`, `id` |
| Pictures | `![alt](data:…)` or your URL | `img` (data URIs, or your URL) |
| Footnotes and endnotes | `[1]` and the notes at the end | `sup` links and a notes section |
| Line and page breaks, tabs | hard break | `br` |
| Tracked changes | final, original, or marked view | `ins` / `del` in the marked view |

\* raw HTML, optional.

Markdown has no syntax for underline or for sub- and superscript. Raw HTML for them is optional (`MarkdownExportOptions.UnderlineAsHtml` and `SubSuperscriptAsHtml`).

## Import safety

The importers never read files and never touch the network.

**Where pictures come from.** Pictures come only from:
- inline `data:` URIs of an allowed type (PNG, JPEG, GIF, BMP, TIFF), or
- your `ImportOptions.ImageResolver`, which can supply a file next to the source, a `cid:` mail part, or anything you choose to fetch.

Remote resources are listed in `ConversionResult.BlockedResources`, for a "show remote content" prompt.

**What happens to HTML input.** It always passes through `HtmlImportOptions.Policy`. The default is `HtmlImportOptions.DefaultPolicy()`, built on Markup's `Anjal()` rules:
- **Links:** kept for http, https, mailto and `#fragment` targets.
- **Ids:** become bookmarks.
- **Removed:**
  - scripts and every event handler
  - frames, objects and forms
  - SVG and MathML
  - unsafe URL schemes and style sheets

**Raw HTML inside Markdown** is sanitized and converted the same way. You can keep it as text or drop it instead (`MarkdownImportOptions.RawHtml`).

Inline tags arrive one at a time, so the content of an inline `<script>` or `<style>` is dropped up to its closing tag, or to the end of that paragraph if it is never closed. Text that is not an HTML element at all, such as `<angle>`, stays text.

**Limits and cancellation.**
- Every entry point takes a `CancellationToken`.
- Parsing runs under Chuvadi.Markup's `MarkupLimits`.
- Input beyond them throws `MarkupLimitExceededException`. That is Markup's documented exception: hostile nesting stops quickly instead of running for minutes.

## Export notes

- **Formatting.** Formatting is written only where it differs from the paragraph's own style. A bold heading style does not make every word `**bold**`.
- **Preview** (`HtmlExportOptions.Preview()`):
  - **Output:** a fragment in `div.chuvadi-docx`, with inline styles only.
  - **Content limits:**
    - Pictures are written as `data:` URIs, up to 2 MiB each.
    - Picture types a browser cannot show (EMF, WMF, TIFF) are written as their alternative text.
    - At most 2000 blocks.
  - **Safety:** it contains no script, no event handler and no external reference.
    - Any link that could run something (`javascript:`, `data:` and the like) is written as plain text.
    - Markup's `SanitizerPolicy.Anjal()` removes nothing from the preview. It only makes links inert and prefixes ids, as it does for any mail.
- **Emphasis CommonMark cannot express.** When delimiters cannot open or close at a spot (for example `**Note:**text`, or an emphasised part of a word with `_`), the Markdown exporter writes raw `<strong>`, `<em>` or `<del>` tags, or drops the formatting (`MarkdownExportOptions.EmphasisFallback`). Renderers that escape raw HTML show such tags literally.
- **Unsafe links in every export.** Links with schemes other than http, https, mailto, tel and ftp are never written as links, in any export.
- **Markdown AST.** `MarkdownConversion.ToAst` returns the Chuvadi.Markup AST, so you can edit it, render it or write it with your own `MarkdownWriterOptions`.

## Options

Every exporter's options derive from `ExportOptions`:
- headers and footers, notes, text boxes, hidden text
- tracked-changes view
- code and quote style ids, monospace fonts
- picture mode, resolver and size cap

The importers share `ImportOptions`:
- picture resolver, allowed types, maximum width and size
- code and quote styles, code font
- task markers, bookmarks

On top of those, each format has its own full option set: `TextImportOptions`, `TextExportOptions`, `MarkdownImportOptions`, `MarkdownExportOptions`, `HtmlImportOptions`, `HtmlExportOptions`.
