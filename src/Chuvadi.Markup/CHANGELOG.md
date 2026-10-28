# Chuvadi.Markup — Changelog

## 1.0.0 (unreleased)

First stable release. Changes since preview.2 come from the independent security review (2026-10-28):
no script execution, network egress or mXSS breakout was found; the hardening items below were fixed.

- **Markdown (F2, F5)**: emphasis resolution is linear for contiguous delimiter runs (a million-character `*`, `_` or `~` run each side now takes well under a second; it was quadratic). `openers_bottom` is keyed by delimiter character x closer can-open x length mod 3. Markdown inline parsing now counts work against `MaxParserWorkPerChar` (throwing `MarkupLimitExceededException`) and polls cancellation inside the opener walk.
- **Sanitizer (F1)**: `@font-face` families are moved into the content's namespace (the id prefix, or `ScopeClass-` / `chuvadi-` when it is empty), together with every reference in the same sheets, style attributes and SVG `font-family` attributes, so content fonts cannot restyle host UI. `Anjal()` drops `@font-face` unless it is added back to `AllowedAtRules`.
- **Sanitizer (F3)**: `@keyframes` and `@counter-style` names are namespaced the same way (with `animation`, `animation-name`, `list-style`, `list-style-type`, `counter()`/`counters()`, `system` and `fallback` references). `@page` and `@layer` are no longer in the default `AllowedAtRules`. `@container` contents are scoped like other rules. Anchor-positioning and view-transition names (`anchor-name`, `position-anchor`, `anchor-scope`, `position-try*`, `position-visibility`, `view-transition-name`, `view-transition-class`, `anchor()`, `anchor-size()`) are denied by default.
- **Sanitizer (F4)**: selectors that start with a combinator, or that put `~` / `+` after the wrapper itself (including `html`, `body`, `:root` and nested `& ~ x` under them), are dropped.

## 1.0.0-preview.2 (unreleased)

- **HTML**: `<selectedcontent>` mirroring of the customizable `<select>`: the selected option's contents are cloned into the select's `<selectedcontent>` while parsing (counted against `MaxNodes`). The html5lib tree-construction suite now passes in full.
- **Sanitizer**: style rules with an empty selector or an empty selector-list item are removed instead of being left unscoped (found by fuzzing; browsers drop such rules, so it was not exploitable). Relative links that would reveal a scheme once control, whitespace and format characters are removed (such as `\u0099javascript:`) are removed as defence in depth.
- **Robustness**: time-boxed random-input fuzz tests for every entry point; timing-sensitive tests run in their own serial collection; globalization analyzers enforced (no culture-sensitive string operations).

## 1.0.0-preview.1 (unreleased)

First preview.

- **Markdown**: `MarkdownParser` (CommonMark 0.31.2, GFM tables, task lists, strikethrough, extended autolinks), public AST (`MarkdownDocument`, block and inline node types, `MarkdownVisitor`, source spans and positions, link reference definitions), `MarkdownHtmlRenderer` / `MarkdownConverter` (`RawHtmlMode` Escape/Strip/Passthrough, tag filter, unsafe URL policy, soft-break modes, heading ids, code language class prefix, task list and table alignment styles, XHTML void elements, `data-sourcepos`), `MarkdownWriter` (AST to Markdown, configurable bullets, emphasis characters, fences, heading style).
- **HTML**: `HtmlTokenizer` (WHATWG, all states, character references, parse errors with positions), `HtmlParser` (document and fragment parsing, string/bytes/span/stream input, charset sniffing via `HtmlEncoding`), DOM (`HtmlDocument`, `HtmlDocumentFragment`, `HtmlElement`, `HtmlText`, `HtmlComment`, `HtmlDocumentType`, namespaces, template contents), `HtmlSerializer`.
- **CSS**: `CssTokenizer`, `CssParser` (style sheets, declaration lists, nested rules), `CssSerializer` (safe inside `<style>` and attributes).
- **Sanitizer**: `HtmlSanitizer`, `SanitizerPolicy` with `Anjal()`, `Reader()`, `Empty()` presets and every knob public; scoped style sheets, id prefixing, inert / rewritten links, `IResourceResolver` for `cid:` and relative URLs, `BlockedResources` and `Actions` reports, title extraction, body style transfer, blocked CSS images replaced with `none` so colours survive, fixed-point stability passes with a plain-text fallback.
- **Plain text**: `PlainText.ToHtml` with `TextLayout` PreWrap / Preformatted / Paragraphs, tab expansion, autolinks (live, inert, rewritten), quote levels, `format=flowed` and `DelSp`.
- **Limits**: `MarkupLimits` (input, nodes, nesting depth, parser work, attributes, output, data URIs, table size, link definitions, recorded errors) with a `Strict()` preset; `MarkupLimitExceededException`.
