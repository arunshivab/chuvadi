# Chuvadi.Markup — Design, conformance and deviations

## Namespaces

| Namespace | Contents |
|---|---|
| `Chuvadi.Markup` | `MarkupLimits`, `MarkupLimitExceededException`, `SourceSpan`, `SourcePosition` |
| `Chuvadi.Markup.Markdown` | `MarkdownParser`, AST (`MarkdownDocument`, blocks, inlines, `MarkdownVisitor`), `MarkdownHtmlRenderer`, `MarkdownConverter`, `MarkdownWriter`, options |
| `Chuvadi.Markup.Html` | `HtmlTokenizer`, `HtmlParser`, DOM, `HtmlSerializer`, `HtmlEncoding` |
| `Chuvadi.Markup.Css` | `CssTokenizer`, `CssParser`, rule/declaration model, `CssSerializer` |
| `Chuvadi.Markup.Sanitization` | `HtmlSanitizer`, `SanitizerPolicy`, `SanitizeResult`, resolver and report types |
| `Chuvadi.Markup.Text` | `PlainText`, `TextToHtmlOptions` |

## Markdown

A port of cmark's two-phase algorithm: a line-oriented block parser (container matching with
column/tab arithmetic, lazy continuation, link reference definitions) followed by the inline parser
(delimiter stack with `openers_bottom` per delimiter class, bracket stack, backtick and HTML-end
memoisation). GFM tables, task lists, strikethrough and extended autolinks follow cmark-gfm. The internal
block/inline tree is converted to the public AST, which is plain mutable .NET objects: consumers
(Reader, Chuvadi.Layout, Chuvadi.Docs) can walk, edit and build documents, then render HTML or write
Markdown. The writer is verified by round-tripping every spec example: parse, write, parse again, and
the HTML must be identical.

Conformance: CommonMark 0.31.2 652/652; cmark-gfm `extensions.txt` and the extension-tagged GFM spec
examples (footnotes excluded); cmark's pathological-input suite (each case far under its 3 s budget).

## HTML

The WHATWG tokenizer (every state, named and numeric character references, all parse errors with line
and column) and tree builder (every insertion mode, adoption agency, foster parenting, templates,
foreign content, fragment parsing), tracking the current spec including the relaxed `<select>`
content model and the customizable-select `<selectedcontent>` mirroring (the selected option's
contents are cloned into it as option elements are popped, counted against the node limit). The DOM is a small linked-node tree with namespaces and template contents. Byte input is
decoded with the WHATWG sniffing order: BOM, caller encoding, `<meta>` prescan (1024 bytes), then
windows-1252; labels map through the WHATWG label table to .NET encodings (`replacement` and
`x-user-defined` are implemented in the library).

Conformance: html5lib tokenizer tests 100% (tokens and error codes); the full tree-construction suite
in both scripting modes.

## CSS

CSS Syntax Level 3 tokenizer and consume algorithms (rules, at-rules, declarations, nested rules,
`!important`, custom properties). The serializer escapes so that output is safe inside `<style>` and
`style=""`, and inserts empty comments only where two tokens would otherwise merge.

## Limits and robustness

| Limit | Default | Strict() | On excess |
|---|---|---|---|
| `MaxInputChars` | 32 Mi | 8 Mi | throw |
| `MaxNodes` | 4 Mi | 500 000 | throw |
| `MaxNestingDepth` | 512 | 256 | degrade (flatten) |
| `MaxParserWorkPerChar` | 64 | 32 | throw |
| `MaxAttributesPerElement` | 512 | 64 | degrade (extra ignored) |
| `MaxAttributeValueChars` | 32 Mi | 8 Mi | degrade (truncate) |
| `MaxOutputChars` | 128 Mi | 32 Mi | throw |
| `MaxDataUriBytes` | 16 MiB | 4 MiB | degrade (resource blocked) |
| `MaxTableColumns` / `MaxTableCells` | 1000 / 1 Mi | 256 / 200 000 | degrade |
| `MaxLinkReferenceDefinitions` | 100 000 | 10 000 | degrade (further definitions stay text) |
| `MaxRecordedErrors` | 1000 | 200 | stop recording |

Every walk is iterative. Cancellation is
polled every few thousand steps and checked on entry.

Throughput on a 2-core 2.1 GHz Xeon (Linux, .NET 10, Release):

| Operation | MB/s |
|---|---|
| Markdown parse + render (GFM) | ~7 |
| Markdown writer (parse + write) | ~8 |
| HTML parse | ~19 |
| Sanitize (Reader or Anjal rules, incl. stability passes) | ~5.5 |
| Plain text to HTML | ~65 |

Every entry point is also fuzzed with random bytes and random markup fragments (`FuzzTests`, time-boxed;
long campaigns via `CHUVADI_FUZZ_SECONDS` / `CHUVADI_FUZZ_RUN`): only the documented exceptions may
escape, and every sanitizer output must pass the safety invariants.

A 100 KB mail sanitizes in roughly 20 ms. Adversarial inputs (50 000-deep nesting, adoption-agency
traps, 100 000 attributes, entity floods, unclosed comments, 90 000-cell tables, deep CSS) each finish
in well under a second; see `HtmlPathologicalTests` and `PathologicalTests`.

## Deviations (deliberate)

- **Processing instructions** (`<?x ...?>`, added to the HTML spec in 2026) are tokenized as bogus
  comments, as html5lib-tests and shipping browsers still do.
- **Nesting depth**: past `MaxNestingDepth`, new elements are inserted as siblings at the cap (as
  Chromium does at 512) instead of being nested; Markdown containers past the cap degrade to paragraphs.
  The active formatting list is capped at the same depth.
- **Parser work budget**: input that would need more than `MaxParserWorkPerChar` units of HTML
  tree-builder or Markdown inline (delimiter and bracket) work per character throws
  `MarkupLimitExceededException` instead of running for minutes. Markdown emphasis resolution is linear
  (CommonMark `openers_bottom` keyed by delimiter character x closer can-open x length mod 3); the budget
  is a backstop, and cancellation is polled inside the opener walk.
- **Encodings** ISO-8859-10, -14 and -16 and other labels .NET has no code page for are reported as
  unsupported (`HtmlEncoding.GetEncoding` returns null) and decoding falls back to the next sniffing step.
- **Markdown footnotes** are out of scope for 1.0 (Reader requirements §10).
- **Markdown `RawHtmlMode.Escape`** renders HTML blocks as escaped paragraphs (cmark's `--safe` emits
  a comment placeholder instead); `Strip` removes them.
