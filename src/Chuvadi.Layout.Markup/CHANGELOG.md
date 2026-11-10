# Chuvadi.Layout.Markup — Changelog

## 0.1.0 (2026-11-09)

- Trim- and NativeAOT-compatible: `IsTrimmable` and `IsAotCompatible` are set, so the analyzers check every build (D-217,
  D-254).
D-60 (requires Chuvadi.Markup at main b019524 or later: presets removed, `LinkHandling.Keep` renamed `Active`):

- When `HtmlImportOptions.Policy` is null, HTML is sanitized with `new SanitizerPolicy()`, Chuvadi.Markup's safest
  policy (previously a strict preset that Chuvadi.Markup no longer has). No app names remain in the package, its docs or its tests.
- Output check: 1,794 HTML inputs (the html5lib tree-construction documents plus mail- and page-like samples) give
  byte-identical sanitized HTML, blocked-resource lists, page counts, page text, links, warnings and SVG pages under
  the old default and the new one.

L12: the HTML importer (requires Chuvadi.Markup 1.0.0).

- **`HtmlImporter` and `HtmlLayout`.** HTML text, bytes (charset sniffed as a browser does) or a stream is
  sanitized with Chuvadi.Markup's `HtmlSanitizer` (a new `SanitizerPolicy`, the safest policy, by default, or `HtmlImportOptions.Policy`)
  and its DOM is laid out. A `SanitizeResult` (what a web view shows) or a trusted DOM node can be laid out directly.
- **Elements.** Headings, paragraphs and block elements, breaks, rules, inline formatting, `pre`, `q`, links
  (including inert links), block quotes, lists, definition lists, tables (row groups, spans, widths, padding, spacing,
  borders, backgrounds, alignment, nesting) and `data:` images.
- **CSS.** The browser's default style (`HtmlStyleSheet`), the presentational attributes of mail HTML and the `style`
  attribute, with inheritance, `!important`, collapsing margins, borders, padding and hidden content.
- **Mail layout tables.** One-column wrapper and container tables become blocks, so long mail splits across pages.
- **Warnings.** Blocked images, style sheets not applied, SVG/MathML/forms/media left out, over-deep tables
  flattened, and sanitizer notes.
- **Corpus.** 657 HTML files (mail templates, HTML test pages, documentation) lay out into 2,502 pages. The one
  failure is a clean limit error on a 15 MB page.
- **Shared.** The page footer, `data:` URI decoding and the default link filter are shared with the Markdown
  importer. The Markdown importer now also accepts whitespace inside base64 `data:` URIs.

L10: the Markdown importer (requires Chuvadi.Markup 1.0.0).

- **`MarkdownImporter` and `MarkdownLayout`.** From Chuvadi.Markup's Markdown AST (CommonMark plus GFM) to
  Chuvadi.Layout's flow document. Covers:
  - headings, paragraphs and breaks;
  - emphasis, strong, strikethrough and code;
  - code blocks and block quotes;
  - bullet, ordered and task lists;
  - tables with alignment and repeated header rows;
  - rules, links and images (`data:` URIs or a caller's resolver; nothing is fetched);
  - raw HTML, printed literally or left out.
- **`MarkdownStyleSheet`.** Every font, size, colour and spacing value. The default is close to GitHub's rendering.
- **Page setup.** The paper follows the culture (D-30); a "Page N of M" footer is on by default; an option starts
  every level-1 heading on a new page.
- **Limits and cancellation.** Parser limits come from `LayoutLimits`. Every `MarkupLimitExceededException`,
  including Markup 1.0.0's inline work budget (`MaxParserWorkPerChar`), becomes `LayoutLimitExceededException` with
  the limit name prefixed "Markup". Cancellation is supported.
- **Corpus.** 430 real-world Markdown files (repository docs and package READMEs) lay out into 1,769 pages with no
  errors.
