# Chuvadi.Docs.Conversion — Changelog

## 1.6.0 (unreleased)

First release, versioned with Chuvadi.Docs 1.6.0 (decision D-15). Depends on Chuvadi.Docs 1.6.0 and Chuvadi.Markup 1.0.0.

- **Plain text** (`TextConversion`):
  - **Import:**
    - One paragraph per line, or blank-line paragraphs joined with line breaks or re-flowed.
    - Tabs become tab runs and form feeds become page breaks.
    - BOM, CR / LF / CRLF / U+2028 line endings, control characters and size cap handled.
  - **Export:**
    - Word's exact list labels, tables as tab-separated rows and notes at the end.
    - Page breaks, headers and footers, and the tracked-changes view.
    - Configurable line endings.
- **Markdown** (`MarkdownConversion`, parsed and written with Chuvadi.Markup: CommonMark 0.31.2 plus GFM tables, task lists, strikethrough and autolinks):
  - **Import:**
    - Headings, emphasis, strong, strikethrough and code.
    - Lists: nested, start numbers and task markers.
    - Nested quotes and code blocks (a Code style is created when missing).
    - Tables with header row and alignment.
    - Links and fragment links, and pictures (data URIs or the host's resolver).
    - Breaks and thematic breaks.
    - Raw HTML is sanitized and converted, kept as text, or dropped.
  - **Export:**
    - The same constructs, plus `ToAst` for callers.
    - Formatting is written relative to the paragraph style.
    - Escaping and output are verified by re-parsing: a Markdown → docx → Markdown corpus renders the same HTML.
- **HTML** (`HtmlConversion`):
  - **Import:**
    - The input passes through Chuvadi.Markup's sanitizer (`HtmlImportOptions.DefaultPolicy()`) before it is mapped.
    - Blocks, headings, inline formatting and a safe subset of inline CSS.
    - Lists, quotes, `pre`, definition lists and rules.
    - Tables with header, colspan, rowspan, alignment, shading and caption (up to Word's 63 columns).
    - Ids become bookmarks for internal links; `data:` pictures; byte input with charset sniffing.
    - `ImportDetailed` reports warnings and blocked remote resources.
  - **Export:**
    - A full page or a fragment, with semantic or inline formatting and semantic or label lists.
    - Notes with back links, pictures, and a cut after a number of blocks.
    - Unsafe link schemes are never written.
  - **Preview:** `ExportPreview` / `HtmlExportOptions.Preview()` gives a self-contained fragment that Markup's `Anjal()` policy removes nothing from.
