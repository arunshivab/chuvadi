# Chuvadi.Docs.Conversion — Changelog

## 2.0.0 (2026-11-09)

The first release. It is versioned with Chuvadi.Docs 2.0.0 (D-15, D-45) and depends on Chuvadi.Docs 2.0.0 and Chuvadi.Markup 1.1.0. Everything listed under 1.6.0 below is part of it; 1.6.0 was never released (D-135).

Changes since the 1.6.0 notes:
- **Namespace:** the Docs types it takes and returns are in `Chuvadi.Docs.Model` (was `Chuvadi.Docs.Word`).
- **Limits are reported as `DocxLimitExceededException`** (an `IOException`, from Chuvadi.Docs, with `LimitName`, `Limit` and `Actual`), from all 16 public import methods.
  - Markdown and HTML: Markup's own limit values, with Markup's `MarkupLimitExceededException` as `InnerException`. It was Markup's exception before.
  - Text: `MaxInputChars`. It was an `InvalidDataException` before.
  - The call produces no partial result. Callers no longer need a reference to Chuvadi.Markup to catch it.
- **Documentation:** Markdown is described as read "as the CommonMark 0.31.2 and GFM 0.29 specifications describe". It used to say "as cmark-gfm reads it", naming another implementation (D-196).
- **Trim and NativeAOT:** the package is marked `IsTrimmable` and `IsAotCompatible`, and the analyzers run on every build (D-217).
- Documentation: the README names Markup's default `SanitizerPolicy` (the `Anjal()` method the old text referred to no longer exists), and has a platforms and threads note.

## 1.6.0 (never released; ships in 2.0.0)

Planned as the first release, versioned with Chuvadi.Docs 1.6.0 (decision D-15), on Chuvadi.Docs 1.6.0 and Chuvadi.Markup 1.0.0.

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
  - **Preview:** `ExportPreview` / `HtmlExportOptions.Preview()` gives a self-contained fragment that Markup's default sanitizer policy removes nothing from.
